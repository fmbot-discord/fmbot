using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Attributes;
using FMBot.Bot.Models.TemplateOptions;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Npgsql;

namespace FMBot.Bot.Services;

public partial class TemplateService
{
    public static async Task<List<string>> GetFooterAsync(FmFooterOption footerOptions, TemplateContext context)
    {
        var options = new ConcurrentBag<(VariableResult Result, int Order)>();
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

            foreach (var option in sqlOptions)
            {
                await ProcessSqlOptionAsync(option, context, reader, options);
            }
        }

        var complexTasks = complexOptions.Select(o => ProcessComplexOptionAsync(o, context, options));
        await Task.WhenAll(complexTasks);

        if (context.DbTrack?.SpotifyId != null)
        {
            var eurovisionEntry =
                await context.EurovisionService.GetEurovisionEntryForSpotifyId(context.DbTrack.SpotifyId);
            if (eurovisionEntry != null)
            {
                var description = context.EurovisionService.GetEurovisionDescription(eurovisionEntry);
                options.Add((new VariableResult(description.oneline), 500));
            }
        }

        return options.Where(w => context.Genres != w.Result.Content).OrderBy(o => o.Order)
            .Select(o => o.Result.Content).ToList();
    }

    public async Task<TemplateResult> GetTemplateFmAsync(int userId, TemplateContext context)
    {
        var template = ExampleTemplates.Templates.First();

        return await TemplateToEmbed(template, context);
    }

    private static async Task ProcessSqlOptionAsync(SqlTemplateOption option, TemplateContext context,
        NpgsqlDataReader reader,
        ConcurrentBag<(VariableResult Result, int Order)> options)
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
        ConcurrentBag<(VariableResult Result, int Order)> options)
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

    public record TemplateResult(EmbedBuilder EmbedBuilder, Dictionary<EmbedOption, string> Content);

    private static async Task<TemplateResult> TemplateToEmbed(Template template, TemplateContext context)
    {
        var embed = new EmbedBuilder();
        var script = template.Content.Replace("$$fm-template", "").TrimStart();
        var lines = script.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var currentOption = EmbedOption.Description;
        var contentBuilders = new Dictionary<EmbedOption, StringBuilder>();
        var embedOptions = new Dictionary<EmbedOption, string>();
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

                        var lineContent = parts[1].Trim();
                        lineOptions.Add((key, lineContent));
                        embedOptions.Add(currentOption, lineContent);
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

                if (!embedOptions.TryAdd(currentOption, line))
                {
                    embedOptions[currentOption] += line;
                }
            }
        }

        var replacedParts = await ReplaceVariablesAsync(lineOptions, context);

        foreach (var (key, value) in replacedParts.variableResults)
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

        return new TemplateResult(embed, embedOptions);
    }

    private static async
        Task<(List<(string Key, string Value)> variableResults, Dictionary<string, VariableResult> variableMap)>
        ReplaceVariablesAsync(
            List<(string Key, string Line)> lines,
            TemplateContext context)
    {
        var variableResults = new List<(string Key, string Value)>();
        var sqlOptions = new List<SqlTemplateOption>();
        var complexOptions = new List<ComplexTemplateOption>();
        var variableMap = new Dictionary<string, VariableResult>();

        // First pass: Collect all variables
        foreach (var (key, line) in lines)
        {
            var regex = VariableRegex();
            var matches = regex.Matches(line);

            foreach (Match match in matches)
            {
                var expression = match.Groups[1].Value.Trim();
                var parts = expression.Split('+').Select(p => p.Trim()).ToList();

                foreach (var part in parts)
                {
                    if (!part.StartsWith("\"") && !part.EndsWith("\""))
                    {
                        var option = TemplateOptions.Options.FirstOrDefault(o =>
                            (o.Variable == part ||
                             o.Variable == part.Replace("-result", "", StringComparison.OrdinalIgnoreCase)));
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
                    var variableResult = await option.ExecuteAsync(context, reader);
                    if (variableResult != null)
                    {
                        variableMap[option.Variable] = variableResult;
                    }
                }
                else
                {
                    if (await reader.ReadAsync())
                    {
                        var fmResult = await option.ExecuteAsync(context, reader);
                        if (fmResult != null)
                        {
                            variableMap[option.Variable] = fmResult;
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
                variableMap[variable] = fmResult;
            }
        }

        // Second pass: Replace variables
        foreach (var (key, line) in lines)
        {
            var replacedLine = ReplaceVariablesInLine(line, variableMap);
            variableResults.Add((key, replacedLine));
        }

        return (variableResults, variableMap);
    }

    private static string ReplaceVariablesInLine(string input, Dictionary<string, VariableResult> variableMap)
    {
        var result = input;
        var regex = VariableRegex();

        var matches = regex.Matches(result);
        foreach (Match match in matches)
        {
            var expression = match.Groups[1].Value.Trim();
            var replacementValue = EvaluateExpressionAsync(expression, variableMap);
            result = result.Replace(match.Value, replacementValue);
        }

        return result;
    }

    private static string EvaluateExpressionAsync(string expression, Dictionary<string, VariableResult> variableMap)
    {
        var parts = expression.Split('+').Select(p => p.Trim()).ToList();
        var resultParts = new List<string>();

        var hasVariable = parts.Any(p =>
            !p.StartsWith("\"") && !p.EndsWith("\"") &&
            (variableMap.ContainsKey(p) ||
             variableMap.ContainsKey(p.Replace("-result", "", StringComparison.OrdinalIgnoreCase))));
        if (!hasVariable)
        {
            return null;
        }

        foreach (var part in parts)
        {
            var variablePart = part;

            var resultOnly = part.Contains("-result", StringComparison.OrdinalIgnoreCase);
            if (resultOnly)
            {
                variablePart = part.Replace("-result", "");
            }

            if (variablePart.StartsWith("\"") && variablePart.EndsWith("\""))
            {
                resultParts.Add(variablePart.Trim('"'));
            }
            else if (variableMap.TryGetValue(variablePart, out var value))
            {
                resultParts.Add(resultOnly ? value.Result : value.Content);
            }
            else
            {
                resultParts.Add(variablePart);
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

    public async Task<EmbedBuilder> GetTemplateVariablesAsync(int userId, TemplateContext context)
    {
        var template = ExampleTemplates.Templates.First();

        var result = await TemplateToEmbed(template, context);

        return result.EmbedBuilder;
    }

    public async Task<List<Template>> GetTemplates(int userId)
    {
        return ExampleTemplates.Templates;
    }

    public async Task<Template> GetTemplate(int templateId)
    {
        return ExampleTemplates.Templates.First(f => f.Id == templateId);
    }

    public async Task<Template> CreateTemplate(int userId)
    {
        // TODO: Count where user id
        var count = ExampleTemplates.Templates.Count();

        var name = $"Template {count}";

        var newTemplate = new UserTemplate
        {
            Name = name,
            Content = "$$fm-template",
            GlobalDefault = count == 0,
            Created = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
            Type = TemplateType.Fm
        };

        return newTemplate;
    }

    public async Task UpdateTemplateName(int templateId, string newName)
    {
        var template = ExampleTemplates.Templates.First(f => f.Id == templateId);

        template.Name = newName;
    }

    public async Task UpdateTemplateContent(int templateId, string newContent)
    {
        var template = ExampleTemplates.Templates.First(f => f.Id == templateId);

        template.Content = newContent;
    }

    public async Task DeleteTemplate(int templateId)
    {
        // TODO implement template deletion
    }

    [GeneratedRegex(@"\{\{(.*?)\}\}")]
    private static partial Regex VariableRegex();
}
