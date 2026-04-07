using System.Net.Http.Headers;
using Serilog;

namespace FMBot.Images.Generators;

public class AppleMusicAltAuthHandler(PuppeteerService puppeteerService) : DelegatingHandler
{
    private string _cachedToken;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        this._cachedToken ??= await puppeteerService.GetAppleToken();

        if (this._cachedToken != null)
        {
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(this._cachedToken);
        }
        else
        {
            Log.Warning("No alt Apple Music auth header");
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
