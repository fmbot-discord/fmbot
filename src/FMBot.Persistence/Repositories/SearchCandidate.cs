using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Npgsql;

namespace FMBot.Persistence.Repositories;

internal class SearchCandidate
{
    public int Id { get; set; }

    public string Name { get; set; }

    public string ArtistName { get; set; }

    public double Score { get; set; }

    public static async Task<SearchCandidate> PickBest(List<SearchCandidate> candidates, int? userId,
        double libraryWeight, string librarySql, NpgsqlConnection connection)
    {
        if (!userId.HasValue || candidates.Count == 1)
        {
            return candidates[0];
        }

        var library = await connection.QueryAsync<(string ArtistName, string Name)>(librarySql, new
        {
            userId,
            names = candidates.Select(s => s.Name).Distinct().ToArray()
        });

        var inLibrary = library
            .Select(s => (s.ArtistName?.ToLowerInvariant(), s.Name?.ToLowerInvariant()))
            .ToHashSet();

        return candidates
            .OrderByDescending(o =>
                o.Score + (inLibrary.Contains((o.ArtistName.ToLowerInvariant(), o.Name.ToLowerInvariant())) ? libraryWeight : 0))
            .ThenBy(o => o.Name.Length)
            .First();
    }
}
