using FMBot.Domain.Models;
using FMBot.OpenCollective.Models;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.SystemTextJson;
using Serilog;

namespace FMBot.OpenCollective.Services;

public class OpenCollectiveService
{
    private readonly HttpClient _client;

    private const string ApiUrl = "https://api.opencollective.com/graphql/v2";

    public OpenCollectiveService(HttpClient client)
    {
        this._client = client;
    }

    public async Task<OpenCollectiveOverview> GetOpenCollectiveOverview()
    {
        var data = await GetBackersFromOpenCollective();

        var users = data.Account.Members.Nodes.Select(s => new OpenCollectiveUser
        {
            Id = s.Account.Id,
            Name = s.Account.Name,
            Slug = s.Account.Slug,
            CreatedAt = s.Account.CreatedAt,
            SubscriptionType = GetSubscriptionType(s.Account.Transactions.Nodes),
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
                          transactions(kind: CONTRIBUTION){
                              nodes{
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

        if (response.Errors != null && response.Errors.Any())
        {
            foreach (var error in response.Errors)
            {
                Log.Error("Error while fetching OpenCollective backers - {error}", error.Message);
            }
        }

        return response.Data;
    }
}
