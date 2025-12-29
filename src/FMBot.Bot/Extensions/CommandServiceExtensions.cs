using System;
using System.Linq;
using NetCord.Services.Commands;

namespace FMBot.Bot.Extensions;

/// <summary>
/// Result of searching for a command in the command service
/// </summary>
public class CommandSearchResult
{
    public bool IsSuccess { get; init; }
    public ICommandInfo<CommandContext>? Command { get; init; }
    public string? ErrorReason { get; init; }

    public static CommandSearchResult Success(ICommandInfo<CommandContext> command) =>
        new() { IsSuccess = true, Command = command };

    public static CommandSearchResult Failure(string reason) =>
        new() { IsSuccess = false, ErrorReason = reason };
}

public static class CommandServiceExtensions
{
    /// <summary>
    /// Searches for a command by name or alias in the command service
    /// </summary>
    public static CommandSearchResult Search(this CommandService<CommandContext> commandService, string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return CommandSearchResult.Failure("Input is empty");
        }

        // Get the command name (first word)
        var commandName = input.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrEmpty(commandName))
        {
            return CommandSearchResult.Failure("No command name found");
        }

        // Search through all commands - flatten the dictionary values
        var allCommands = commandService.GetCommands().SelectMany(kvp => kvp.Value);

        foreach (var command in allCommands)
        {
            // Check if any alias matches (aliases include the primary name)
            if (command.Aliases.Any(alias =>
                alias.Equals(commandName, StringComparison.OrdinalIgnoreCase)))
            {
                return CommandSearchResult.Success(command);
            }
        }

        return CommandSearchResult.Failure($"Command '{commandName}' not found");
    }
}
