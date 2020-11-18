using System.Threading.Tasks;
using FMBot.Bot.Models;
using SkiaSharp;

namespace FMBot.Bot.Interfaces
{
    public interface IChartService
    {
        Task<SKImage> GenerateChartAsync(ChartSettings chart);
        ChartSettings SetSettings(ChartSettings currentChartSettings, string extraOptions);
    }
}
