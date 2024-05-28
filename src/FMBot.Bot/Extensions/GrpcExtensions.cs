using System;
using Grpc.Core.Interceptors;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FMBot.Bot.Extensions;

public static class GrpcExtensions
{
    private class SecretKeyInterceptor : Interceptor
    {
        private readonly string _secretKey;

        public SecretKeyInterceptor(string secretKey)
        {
            this._secretKey = secretKey;
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            var headers = context.Options.Headers;
            if (headers == null)
            {
                headers = [];
                context = new ClientInterceptorContext<TRequest, TResponse>(
                    context.Method, context.Host, context.Options.WithHeaders(headers));
            }

            headers.Add("Secret-Key", this._secretKey);

            return continuation(request, context);
        }
    }


    public static void AddConfiguredGrpcClient<TClient>(this IServiceCollection services, IConfiguration configuration)
        where TClient : ClientBase<TClient>
    {
        var secretKey = configuration["ApiConfig:InternalSecretKey"];
        var endpoint = configuration["ApiConfig:InternalEndpoint"];
        
        services.AddGrpcClient<TClient>(o =>
        {
            o.Address = new Uri(endpoint ?? "http://localhost:5285/");
        }).ConfigureChannel(o =>
        {
            o.MaxReceiveMessageSize = null;
            o.MaxSendMessageSize = null;
        }).AddInterceptor(() => new SecretKeyInterceptor(secretKey));
    }
}
