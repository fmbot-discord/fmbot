using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
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
            .Where(w => footerOptions.HasFlag(w.FooterOption))
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

        return options.OrderBy(o => o.Order).Select(o => o.Result.Content).ToList();
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

    public async Task<EmbedBuilder> TemplateToEmbed(Template template, TemplateContext context)
    {
        var embed = new EmbedBuilder();
        var script = template.Content.Replace("$$fm-template", "").TrimStart();
        var lines = script.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var currentOption = EmbedOption.Description;
        var contentBuilders = new Dictionary<EmbedOption, StringBuilder>();
        var replacementTasks = new List<Task<(string Key, string Value)>>();

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
                        replacementTasks.Add(ReplaceVariablesAsync(key, parts[1].Trim(), context));
                    }
                }
            }
            else
            {
                if (!contentBuilders.ContainsKey(currentOption))
                {
                    contentBuilders[currentOption] = new StringBuilder();
                }

                replacementTasks.Add(ReplaceVariablesAsync(string.Empty, line, context));
            }
        }

        var replacedParts = await Task.WhenAll(replacementTasks);

        foreach (var (key, value) in replacedParts)
        {
            if (EmbedOptionMap.TryGetValue(key, out var embedOption))
            {
                currentOption = embedOption;
            }

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

        foreach (var (option, contentBuilder) in contentBuilders)
        {
            var content = contentBuilder.ToString().Trim();
            ApplyEmbedOption(embed, option, content);
        }

        return embed;
    }

    private void ApplyEmbedOption(EmbedBuilder embed, EmbedOption option, string content)
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

    private static async Task<(string Key, string Value)> ReplaceVariablesAsync(string key, string input,
        TemplateContext context)
    {
        var result = input;
        var replacementTasks = new List<Task<(string Placeholder, string ReplacementValue)>>();

        foreach (var option in TemplateOptions.Options)
        {
            var placeholder = $"{{{{{option.Variable}}}}}";
            if (result.Contains(placeholder))
            {
                replacementTasks.Add(GetReplacementValueAsync(option, placeholder, context));
            }
        }

        var replacements = await Task.WhenAll(replacementTasks);

        foreach (var (placeholder, replacementValue) in replacements)
        {
            result = result.Replace(placeholder, replacementValue);
        }

        return (key, result);
    }

    private static async Task<(string Placeholder, string ReplacementValue)> GetReplacementValueAsync(
        TemplateOption option,
        string placeholder, TemplateContext context)
    {
        FmResult fmResult = null;

        if (option is SqlTemplateOption sqlOption)
        {
            await using var command = new NpgsqlCommand(sqlOption.SqlQuery, context.Connection);
            foreach (var param in sqlOption.ParametersFactory(context))
            {
                command.Parameters.AddWithValue(param.Key, param.Value);
            }

            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                fmResult = await sqlOption.ResultProcessor(context, reader);
            }
        }
        else if (option is ComplexTemplateOption complexOption)
        {
            fmResult = await complexOption.ExecutionLogic(context);
        }

        return (placeholder, fmResult?.Content ?? "");
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
