using System.Net;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.OpenCollective.Models;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.SystemTextJson;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace FMBot.OpenCollective.Services;

public class OpenCollectiveService
{
    private readonly HttpClient _client;

    private const string ApiUrl = "https://api.opencollective.com/graphql/v2";

    private readonly IMemoryCache _cache;

    public OpenCollectiveService(HttpClient client, IMemoryCache cache)
    {
        this._client = client;
        this._cache = cache;
    }

    public async Task<OpenCollectiveOverview> GetOpenCollectiveOverview()
    {
        var data = await GetBackersFromOpenCollective();

        if (data.Account?.Name == null)
        {
            return null;
        }

        var users = data.Account.Members.Nodes.Select(s => new OpenCollectiveUser
        {
            Id = s.Account.Id,
            Name = s.Account.Name,
            Slug = s.Account.Slug,
            CreatedAt = s.Account.CreatedAt,
            SubscriptionType = GetSubscriptionType(s.Account.Transactions.Nodes),
            FirstPayment = s.Account.Transactions.Nodes.OrderBy(o => o.CreatedAt).First().CreatedAt,
            LastPayment = s.Account.Transactions.Nodes.OrderByDescending(o => o.CreatedAt).First().CreatedAt
        });

        return new OpenCollectiveOverview
        {
            Users = users.ToList()
        };
    }

    private static SubscriptionType GetSubscriptionType(IReadOnlyCollection<Node> transactionsNodes)
    {
        if (transactionsNodes.Any(a => a.Description.ToLower().Contains("monthly")))
        {
            return SubscriptionType.Monthly;
        }
        if (transactionsNodes.Any(a => a.Description.ToLower().Contains("yearly")))
        {
            return SubscriptionType.Yearly;
        }

        return SubscriptionType.Lifetime;
    }

    private async Task<OpenCollectiveResponseModel> GetBackersFromOpenCollective()
    {
        const string cacheKey = "opencollective-supporters";

        if (this._cache.TryGetValue(cacheKey, out OpenCollectiveResponseModel cachedResponse))
        {
            return cachedResponse;
        }

        var query = new GraphQLRequest
        {
            Query = @"
            query account($slug: String) {
              account(slug: $slug) {
                name
                slug
                members(role: BACKER, limit: 5000) {
                  totalCount
                  nodes {
                    account {
                      name
                      slug
                      id
                      createdAt
                      transactions(kind: CONTRIBUTION, fromAccount: { slug: $slug }) {
                        nodes {
                          createdAt
                          amount {
                            currency
                            value
                          }
                          description
                          type
                          kind
                        }
                      }
                    }
                  }
                }
              }
            }",
            Variables = @"{""slug"": ""fmbot""}"
        };

        var graphQLClient = new GraphQLHttpClient(new GraphQLHttpClientOptions
        {
            EndPoint = new Uri(ApiUrl)
        }, new SystemTextJsonSerializer(options => options.PropertyNameCaseInsensitive = true), this._client);

        var response = await graphQLClient.SendQueryAsync<OpenCollectiveResponseModel>(query);
        Statistics.OpenCollectiveApiCalls.Inc();

        if (response.Errors != null && response.Errors.Any())
        {
            foreach (var error in response.Errors)
            {
                Log.Error("Error while fetching OpenCollective backers - {error}", error.Message);
            }
        }

        if (response is GraphQLHttpResponse<OpenCollectiveResponseModel> httpResponse)
        {
            Log.Information("OpenCollective response is {statusCode}", httpResponse.StatusCode);

            if (httpResponse.StatusCode == HttpStatusCode.OK)
            {
                this._cache.Set(cacheKey, response.Data, TimeSpan.FromMinutes(2));
            }
        }

        return response.Data;
    }
}
