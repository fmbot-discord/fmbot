using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

    public async Task<List<Template>> GetTemplates(int userId)
    {
        return ExampleTemplates.Templates;
    }

    public async Task<Template> GetTemplate(int templateId)
    {
        return ExampleTemplates.Templates.First(f => f.Id == templateId);
    }
}
