using System;
using System.Threading.Tasks;
using NetCord.Rest;
using Serilog;

namespace FMBot.Bot.Extensions;

public static class MessageExtensions
{
    public static async Task DeleteAfterAsync(this RestMessage message, TimeSpan delay)
    {
        if (message == null)
            return;

        try
        {
            await Task.Delay(delay);
            await message.DeleteAsync();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to delete message {MessageId} after delay", message.Id);
        }
    }

    public static Task DeleteAfterAsync(this RestMessage message, int seconds)
        => message.DeleteAfterAsync(TimeSpan.FromSeconds(seconds));
}
