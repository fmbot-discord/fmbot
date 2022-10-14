using System;
using System.IO;

namespace FMBot.Logger;

public class Logger
{
    public void Log(string text, ConsoleColor color = ConsoleColor.Gray)
    {
        Console.ForegroundColor = color;
        Console.WriteLine($"{DateTime.Now:hh:mm:ss.fff} : " + text);
        Console.ResetColor();

        var filePath = $"Logs/{DateTime.Now:MMMM, yyyy}";
        if (!File.Exists(filePath)) Directory.CreateDirectory(filePath);
        filePath += $"/{DateTime.Now:dddd, MMMM d, yyyy}.txt";
        using var file = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var sw = new StreamWriter(file);

        sw.WriteLine($"{DateTime.Now:T} : {text}");
    }

    public void LogError(string errorReason, string message = null, string username = null, string guildName = null, ulong? guildId = null)
    {
        string error = $"{DateTime.Now:T} : Error - {errorReason} \n" +
                       $"{DateTime.Now:T} : {message} \n" +
                       $"{DateTime.Now:T} : User: {username} \n" +
                       $"{DateTime.Now:T} : Guild: {guildName} | Id: {guildId} \n" +
                       "====================================";

        Log(error);
    }

    public void LogException(string errorReason, Exception exception)
    {
        string error = $"{DateTime.Now:T} : Exception - {errorReason} \n" +
                       $"{DateTime.Now:T} : {exception.Message} \n" +
                       $"{DateTime.Now:T} : {exception.InnerException} \n" +
                       "====================================";

        Log(error);
    }
}
