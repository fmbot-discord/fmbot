using System.Net;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Subscriptions.Models;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.SystemTextJson;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace FMBot.Subscriptions.Services;

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

        var users = data.Account.Members.Nodes
            .Where(w => w.Account.Transactions != null)
            .Select(s => new OpenCollectiveUser
        {
            Id = s.Account.Id,
            Name = s.Account.Name,
            Slug = s.Account.Slug,
            CreatedAt = s.Account.CreatedAt,
            SubscriptionType = GetSubscriptionType(s.Account.Transactions.Nodes),
            FirstPayment = s.Account.Transactions.Nodes.OrderBy(o => o.CreatedAt).First().CreatedAt,
            LastPayment = s.Account.Transactions.Nodes.OrderByDescending(o => o.CreatedAt).First().CreatedAt,
            Transactions = s.Account.Transactions.Nodes.Select(s => new OpenCollectiveTransaction
            {
                Amount = s.Amount.Value,
                CreatedAt = s.CreatedAt,
                Description = s.Description,
                Kind = s.Kind,
                Type = s.Type
            }).ToList()
        });

        return new OpenCollectiveOverview
        {
            Users = users.ToList()
        };
    }

    private static SubscriptionType GetSubscriptionType(IReadOnlyCollection<Node> transactionsNodes)
    {
        var lastDescription = transactionsNodes.OrderByDescending(o => o.CreatedAt).First().Description;

        if (lastDescription.ToLower().Contains("monthly"))
        {
            return SubscriptionType.MonthlyOpenCollective;
        }
        if (lastDescription.ToLower().Contains("yearly"))
        {
            return SubscriptionType.YearlyOpenCollective;
        }

        return SubscriptionType.LifetimeOpenCollective;
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
            query account {
              account(slug: ""fmbot"") {
                name
                slug
                members(role: BACKER, limit: 1000) {
                  totalCount
                  nodes {
                    account {
                      name
                      slug
                      id
                      createdAt
                      transactions(kind: CONTRIBUTION, fromAccount: { slug: ""fmbot"" }) {
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
            }"
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
                this._cache.Set(cacheKey, response.Data, TimeSpan.FromMinutes(1));
            }
        }

        return response.Data;
    }
}
