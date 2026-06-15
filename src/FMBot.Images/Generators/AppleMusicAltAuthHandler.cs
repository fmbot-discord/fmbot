using System.Net;
using System.Net.Http.Headers;
using Serilog;

namespace FMBot.Images.Generators;

public class AppleMusicAltAuthHandler(PuppeteerService puppeteerService) : DelegatingHandler
{
    private string _cachedToken;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        this._cachedToken ??= await puppeteerService.GetAppleToken();

        if (this._cachedToken == null)
        {
            Log.Error("No alt Apple Music auth header");
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        request.Headers.Authorization = AuthenticationHeaderValue.Parse(this._cachedToken);

        return await base.SendAsync(request, cancellationToken);
    }
}
