using System.Threading.Tasks;
using Discord.Commands;
using FMBot.Bot.Models;
using SkiaSharp;

namespace FMBot.Bot.Interfaces
{
    public interface IChartService
    {
        Task<SKImage> GenerateChartAsync(ChartSettings chart, bool nsfwAllowed);
        ChartSettings SetSettings(ChartSettings currentChartSettings, string[] extraOptions,
            ICommandContext commandContext);
    }
}
