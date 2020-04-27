using System.Threading.Tasks;
using FMBot.Bot.Models;

namespace FMBot.Bot.Interfaces
{
    public interface IChartService
    {
        Task GenerateChartAsync(ChartSettings chart);
        ChartSettings SetSettings(ChartSettings currentChartSettings, string[] extraOptions);
    }
}