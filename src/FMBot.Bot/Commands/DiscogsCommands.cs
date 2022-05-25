using System.Threading.Tasks;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.Commands;

public class DiscogsCommands : BaseCommandModule
{
    private readonly DiscogsBuilder _discogsBuilder;

    public DiscogsCommands(DiscogsBuilder discogsBuilder, IOptions<BotSettings> botSettings) : base(botSettings)
    {
        this._discogsBuilder = discogsBuilder;
    }

    //[Command("collection", RunMode = RunMode.Async)]
    //[Summary("Displays user stats related to Last.fm and .fmbot")]
    //[UsernameSetRequired]
    //[CommandCategories(CommandCategory.Other)]
    //public async Task CollectionAsync()
    //{
    //    await this._discogsBuilder.DiscogsCollectionAsync(new ContextModel(this.Context, "/"));
    //}
}
