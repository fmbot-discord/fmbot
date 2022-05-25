using System;
using System.Diagnostics;
using System.Threading.Tasks;
using DiscogsClient;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Models;
using RestSharpHelper.OAuth1;

namespace FMBot.Bot.Builders;

public class DiscogsBuilder
{
    private readonly UserService _userService;
    private readonly DiscogsService _discogsService;

    public DiscogsBuilder(UserService userService, DiscogsService discogsService)
    {
        this._userService = userService;
        this._discogsService = discogsService;
    }

    public async Task<ResponseModel> DiscogsCollectionAsync(
        ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        //await this._discogsService.AuthDiscogs();

        return response;
    }
}
