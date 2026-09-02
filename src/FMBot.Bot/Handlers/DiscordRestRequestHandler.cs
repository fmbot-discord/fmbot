using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Quic;
using System.Threading;
using System.Threading.Tasks;
using FMBot.Domain;
using NetCord.Rest;
using Serilog;

#pragma warning disable CA1416

namespace FMBot.Bot.Handlers;

public sealed class DiscordRestRequestHandler : IRestRequestHandler
{
    private readonly HttpClient _httpClient = new(new SocketsHttpHandler());

    private readonly ConcurrentDictionary<string, byte> _seenProtocols = new();

    public DiscordRestRequestHandler()
    {
        Log.Information("Discord REST handler: requesting HTTP/3 with fallback, QUIC supported on this host = {QuicSupported}", QuicConnection.IsSupported);
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        request.Version = HttpVersion.Version30;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

        var response = await this._httpClient.SendAsync(request, cancellationToken);

        var protocol = response.Version.ToString(2);
        Statistics.DiscordRestResponses.WithLabels(protocol).Inc();
        if (this._seenProtocols.TryAdd(protocol, 0))
        {
            Log.Information("Discord REST handler: first response over HTTP/{Protocol}", protocol);
        }

        return response;
    }

    public void AddDefaultHeader(string name, IEnumerable<string> values)
    {
        this._httpClient.DefaultRequestHeaders.Add(name, values);
    }

    public void Dispose()
    {
        this._httpClient.Dispose();
    }
}
