namespace FMBot.Persistence.Repositories;

internal static class SearchSql
{
    public const string WholeWordMatch =
        "public.f_search_vector((COALESCE(t.name, ''::citext) || ' '::citext || COALESCE(t.artist_name, ''::citext))::text) " +
        "@@ public.f_search_query(@searchTerm)";

    public const string PrefixMatch =
        "public.f_search_vector((COALESCE(t.name, ''::citext) || ' '::citext || COALESCE(t.artist_name, ''::citext))::text) " +
        "@@ public.f_search_prefix_query(@searchTerm)";

    public const string BestMatches = @"SELECT s.id, s.name, s.artist_name, s.score
FROM scored s
WHERE s.score >= (SELECT max(score) FROM scored) - @libraryWeight
ORDER BY s.score DESC, length(s.name) ASC
LIMIT 200;";

    public const string BestMatchPerTitle = @"SELECT s.name, s.artist_name, s.popularity, s.score
FROM (SELECT scored.*,
             row_number() OVER (PARTITION BY scored.norm_artist, scored.norm_core
                                ORDER BY scored.score DESC, length(scored.name) ASC) AS title_rank
      FROM scored) s
WHERE s.title_rank = 1
ORDER BY s.score DESC, length(s.name) ASC
LIMIT 25;";
}
