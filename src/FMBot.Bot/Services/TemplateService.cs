using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Models.TemplateOptions;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Npgsql;

namespace FMBot.Bot.Services;

public class TemplateService
{
    public async Task<List<string>> GetFooterAsync(FmFooterOption footerOptions, TemplateContext context)
    {
        var options = new ConcurrentBag<(FmResult Result, int Order)>();
        var relevantOptions = TemplateOptions.Options
            .Where(w => w.FooterOption != 0 && footerOptions.HasFlag(w.FooterOption))
            .ToList();

        var sqlOptions = relevantOptions.OfType<SqlTemplateOption>().ToList();
        var complexOptions = relevantOptions.OfType<ComplexTemplateOption>().ToList();

        await using var batch = new NpgsqlBatch(context.Connection);
        foreach (var option in sqlOptions)
        {
            batch.BatchCommands.Add(option.CreateBatchCommand(context));
        }

        if (batch.BatchCommands.Count > 0)
        {
            await using var reader = await batch.ExecuteReaderAsync();
            var sqlTasks = new List<Task>();

            foreach (var option in sqlOptions)
            {
                var task = ProcessSqlOptionAsync(option, context, reader, options);
                sqlTasks.Add(task);
            }

            await Task.WhenAll(sqlTasks);
        }

        var complexTasks = complexOptions.Select(o => ProcessComplexOptionAsync(o, context, options));
        await Task.WhenAll(complexTasks);

        var eurovision =
            EurovisionService.GetEurovisionEntry(context.CurrentTrack.ArtistName, context.CurrentTrack.TrackName);
        if (eurovision != null)
        {
            var description = EurovisionService.GetEurovisionDescription(eurovision);
            options.Add((new FmResult(description.oneline), 500));
        }

        return options.Where(w => context.Genres != w.Result.Content).OrderBy(o => o.Order)
            .Select(o => o.Result.Content).ToList();
    }

    public async Task<EmbedBuilder> GetTemplateFmAsync(int userId, TemplateContext context)
    {
        var template = ExampleTemplates.Templates.First();

        return await TemplateToEmbed(template, context);
    }

    private static async Task ProcessSqlOptionAsync(SqlTemplateOption option, TemplateContext context,
        NpgsqlDataReader reader,
        ConcurrentBag<(FmResult Result, int Order)> options)
    {
        if (option.ProcessMultipleRows)
        {
            var result = await option.ExecuteAsync(context, reader);
            if (result != null)
            {
                options.Add((result, option.FooterOrder));
            }
        }
        else
        {
            if (await reader.ReadAsync())
            {
                var result = await option.ExecuteAsync(context, reader);
                if (result != null)
                {
                    options.Add((result, option.FooterOrder));
                }
            }
        }

        await reader.NextResultAsync();
    }

    private static async Task ProcessComplexOptionAsync(ComplexTemplateOption option, TemplateContext context,
        ConcurrentBag<(FmResult Result, int Order)> options)
    {
        var result = await option.ExecuteAsync(context, null);
        if (result != null)
        {
            options.Add((result, option.FooterOrder));
        }
    }

    private static readonly Dictionary<string, EmbedOption> EmbedOptionMap = BuildEmbedOptionMap();

    private static Dictionary<string, EmbedOption> BuildEmbedOptionMap()
    {
        return Enum.GetValues(typeof(EmbedOption))
            .Cast<EmbedOption>()
            .ToDictionary(
                option => typeof(EmbedOption).GetMember(option.ToString())[0]
                    .GetCustomAttribute<EmbedOptionAttribute>()?.ScriptName.ToLower() ?? option.ToString().ToLower(),
                option => option
            );
    }

    private async Task<EmbedBuilder> TemplateToEmbed(Template template, TemplateContext context)
    {
        var embed = new EmbedBuilder();
        var script = template.Content.Replace("$$fm-template", "").TrimStart();
        var lines = script.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var currentOption = EmbedOption.Description;
        var contentBuilders = new Dictionary<EmbedOption, StringBuilder>();
        var lineOptions = new List<(string Key, string Line)>();

        foreach (var line in lines)
        {
            if (line.StartsWith("$$"))
            {
                var parts = line.Split([':'], 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Substring(2).Trim().ToLower();
                    if (EmbedOptionMap.TryGetValue(key, out var embedOption))
                    {
                        currentOption = embedOption;
                        contentBuilders[currentOption] = new StringBuilder();
                        lineOptions.Add((key, parts[1].Trim()));
                    }
                }
            }
            else
            {
                if (!contentBuilders.ContainsKey(currentOption))
                {
                    contentBuilders[currentOption] = new StringBuilder();
                }

                lineOptions.Add((string.Empty, line));
            }
        }

        var replacedParts = await ReplaceVariablesAsync(lineOptions, context);

        foreach (var (key, value) in replacedParts)
        {
            if (EmbedOptionMap.TryGetValue(key, out var embedOption))
            {
                currentOption = embedOption;
            }

            if (!string.IsNullOrWhiteSpace(value) || value == "\r")
            {
                if (contentBuilders[currentOption].Length == 0)
                {
                    contentBuilders[currentOption].Append(value);
                    contentBuilders[currentOption].AppendLine();
                }
                else
                {
                    contentBuilders[currentOption].Append(value);
                }
            }
        }

        foreach (var (option, contentBuilder) in contentBuilders)
        {
            var content = contentBuilder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(content) || content == "\n")
            {
                ApplyEmbedOption(embed, option, content);
            }
        }

        return embed;
    }

    private static async Task<List<(string Key, string Value)>> ReplaceVariablesAsync(List<(string Key, string Line)> lines,
        TemplateContext context)
    {
        var result = new List<(string Key, string Value)>();
        var sqlOptions = new List<SqlTemplateOption>();
        var complexOptions = new List<ComplexTemplateOption>();
        var variableMap = new Dictionary<string, string>();

        // First pass: Collect all variables
        foreach (var (key, line) in lines)
        {
            var regex = new Regex(@"\{\{(.*?)\}\}");
            var matches = regex.Matches(line);

            foreach (Match match in matches)
            {
                var expression = match.Groups[1].Value.Trim();
                var parts = expression.Split('+').Select(p => p.Trim()).ToList();

                foreach (var part in parts)
                {
                    if (!part.StartsWith("\"") && !part.EndsWith("\""))
                    {
                        var option = TemplateOptions.Options.FirstOrDefault(o => o.Variable == part);
                        if (option is SqlTemplateOption sqlOption)
                        {
                            sqlOptions.Add(sqlOption);
                        }
                        else if (option is ComplexTemplateOption complexOption)
                        {
                            complexOptions.Add(complexOption);
                        }
                    }
                }
            }
        }

        // Execute SQL batch
        await using var batch = new NpgsqlBatch(context.Connection);
        foreach (var option in sqlOptions)
        {
            batch.BatchCommands.Add(option.CreateBatchCommand(context));
        }

        if (batch.BatchCommands.Count > 0)
        {
            await using var reader = await batch.ExecuteReaderAsync();
            foreach (var option in sqlOptions)
            {
                if (option.ProcessMultipleRows)
                {
                    var fmResult = await option.ExecuteAsync(context, reader);
                    if (fmResult != null)
                    {
                        variableMap[option.Variable] = fmResult.Content;
                    }
                }
                else
                {
                    if (await reader.ReadAsync())
                    {
                        var fmResult = await option.ExecuteAsync(context, reader);
                        if (fmResult != null)
                        {
                            variableMap[option.Variable] = fmResult.Content;
                        }
                    }
                }

                await reader.NextResultAsync();
            }
        }

        // Execute complex options concurrently
        var complexTasks = complexOptions.Select(async option =>
        {
            var fmResult = await option.ExecutionLogic(context);
            return (option.Variable, fmResult);
        });

        var complexResults = await Task.WhenAll(complexTasks);
        foreach (var (variable, fmResult) in complexResults)
        {
            if (fmResult != null)
            {
                variableMap[variable] = fmResult.Content;
            }
        }

        // Second pass: Replace variables
        foreach (var (key, line) in lines)
        {
            var replacedLine = ReplaceVariablesInLine(line, variableMap);
            result.Add((key, replacedLine));
        }

        return result;
    }

    private static string ReplaceVariablesInLine(string input, Dictionary<string, string> variableMap)
    {
        var result = input;
        var regex = new Regex(@"\{\{(.*?)\}\}");

        var matches = regex.Matches(result);
        foreach (Match match in matches)
        {
            var expression = match.Groups[1].Value.Trim();
            var replacementValue = EvaluateExpressionAsync(expression, variableMap);
            result = result.Replace(match.Value, replacementValue);
        }

        return result;
    }

    private static string EvaluateExpressionAsync(string expression, Dictionary<string, string> variableMap)
    {
        var parts = expression.Split('+').Select(p => p.Trim()).ToList();
        var resultParts = new List<string>();

        var hasVariable = parts.Any(p => !p.StartsWith("\"") && !p.EndsWith("\"") && variableMap.ContainsKey(p));
        if (!hasVariable)
        {
            return null;
        }

        foreach (var part in parts)
        {
            if (part.StartsWith("\"") && part.EndsWith("\""))
            {
                resultParts.Add(part.Trim('"'));
            }
            else if (variableMap.TryGetValue(part, out var value))
            {
                resultParts.Add(value);
            }
            else
            {
                resultParts.Add(part);
            }
        }

        return string.Join("", resultParts);
    }

    private static void ApplyEmbedOption(EmbedBuilder embed, EmbedOption option, string content)
    {
        switch (option)
        {
            case EmbedOption.Title:
                embed.Title = content;
                break;
            case EmbedOption.Description:
                embed.Description = content;
                break;
            case EmbedOption.ThumbnailImageUrl:
                embed.ThumbnailUrl = content;
                break;
            case EmbedOption.LargeImageUrl:
                embed.ImageUrl = content;
                break;
            case EmbedOption.Footer:
                embed.Footer ??= new EmbedFooterBuilder();
                embed.Footer.Text = content;
                break;
            case EmbedOption.FooterIconUrl:
                embed.Footer ??= new EmbedFooterBuilder();
                embed.Footer.IconUrl = content;
                break;
            case EmbedOption.FooterTimestamp:
                if (DateTime.TryParse(content, out var timestamp))
                {
                    embed.Timestamp = timestamp;
                }

                break;
            case EmbedOption.Author:
                embed.Author ??= new EmbedAuthorBuilder();
                embed.Author.Name = content;
                break;
            case EmbedOption.AuthorIconUrl:
                embed.Author ??= new EmbedAuthorBuilder();
                embed.Author.IconUrl = content;
                break;
            case EmbedOption.AuthorUrl:
                embed.Author ??= new EmbedAuthorBuilder();
                embed.Author.Url = content;
                break;
            case EmbedOption.Url:
                embed.Url = content;
                break;
            case EmbedOption.ColorHex:
                if (uint.TryParse(content.TrimStart('#'), System.Globalization.NumberStyles.HexNumber, null,
                        out var colorVal))
                {
                    embed.Color = new Color(colorVal);
                }

                break;
        }
    }

    public async Task<List<Template>> GetTemplates(int userId)
    {
        return ExampleTemplates.Templates;
    }

    public async Task<Template> GetTemplate(int templateId)
    {
        return ExampleTemplates.Templates.First(f => f.Id == templateId);
    }
}
