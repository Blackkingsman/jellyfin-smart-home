using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.SmartHome.Shelves;

/// <summary>A collection the plugin keeps in sync: which items belong and in what order.</summary>
public sealed record Shelf(string Key, string Name, string Unit, int Min, Func<BaseItem, bool> Pick, Func<BaseItem, double>? Sort = null, int Limit = ShelfCatalog.MaxItems);

public sealed record ShelfResult(Shelf Shelf, IReadOnlyList<Guid> Ids);

public static class ShelfCatalog
{
    public const int MaxItems = 200;      // cap per shelf
    private const int MovieMin = 8;       // skip shelves too thin to be useful
    private const int ShowMin = 6;        // TV libraries are smaller
    private const int EraMin = 15;        // a genre+decade needs this many movies to get a shelf
    private const int EraLimit = 12;      // at most this many era shelves
    private const int EraPerDecade = 3;   // so the biggest decades don't take every era slot
    private const int GenreLimit = 40;    // genre rows are "the best of", not the whole genre
    private const int RecentYears = 5;    // "recent" = released in the last five years

    private static readonly HashSet<string> KidRatings = ["G", "PG", "TV-G", "TV-Y", "TV-Y7", "TV-PG"];

    // genre, label for "Top Rated ___" and "Recent ___ Hits"
    private static readonly (string Genre, string Plural, string Single)[] MovieGenres =
    [
        ("Comedy", "Comedies", "Comedy"), ("Action", "Action Movies", "Action"), ("Drama", "Dramas", "Drama"),
        ("Thriller", "Thrillers", "Thriller"), ("Horror", "Horror Movies", "Horror"), ("Science Fiction", "Sci-Fi Movies", "Sci-Fi"),
        ("Romance", "Romance Movies", "Romance"), ("Crime", "Crime Movies", "Crime"), ("Adventure", "Adventure Movies", "Adventure"),
        ("Animation", "Animated Movies", "Animated"), ("Fantasy", "Fantasy Movies", "Fantasy"), ("Mystery", "Mysteries", "Mystery"),
        ("Family", "Family Movies", "Family"),
    ];

    private static readonly (string Genre, string Plural, string Single)[] ShowGenres =
    [
        ("Drama", "TV Dramas", "TV Drama"), ("Comedy", "TV Comedies", "TV Comedy"), ("Sci-Fi & Fantasy", "Sci-Fi & Fantasy TV", "Sci-Fi & Fantasy TV"),
        ("Action & Adventure", "Action TV", "Action TV"), ("Crime", "Crime TV", "Crime TV"), ("Mystery", "Mystery TV", "Mystery TV"),
        ("Animation", "Animated Series", "Animated Series"),
    ];

    private static readonly string[] EraGenres = ["Action", "Comedy", "Horror", "Thriller", "Science Fiction", "Drama", "Crime", "Animation", "Romance", "Adventure"];

    private static bool G(BaseItem m, params string[] names) => m.Genres?.Any(names.Contains) ?? false;

    /// <summary>
    /// Audience score, blended with critics when there's a critic score. Brand-new releases without
    /// one get pulled toward average: a week-old movie with 40 votes shouldn't top "Top Rated".
    /// </summary>
    private static double Rating(BaseItem m)
    {
        double audience = m.CommunityRating ?? 0;
        if (m.CriticRating is float critics)
        {
            return (0.7 * audience) + (0.3 * critics / 10);
        }

        bool recent = DateTime.UtcNow.Year - (m.ProductionYear ?? 0) < 2;
        return recent && audience > 0 ? (audience + 6.5) / 2 : audience;
    }

    private static double Minutes(BaseItem m) => (m.RunTimeTicks ?? 0) / 600_000_000.0;

    private static DateTime? LastEpisodeAdded(BaseItem s) => (s as Folder)?.DateLastMediaAdded;

    private static double DaysAgo(DateTime? date, DateTime today) => date is null ? 10_000 : (today - date.Value).TotalDays;

    /// <summary>TMDb lists the defining genre first: Aliens is Action, not Thriller.</summary>
    private static string? MainGenre(BaseItem m) => m.Genres?.FirstOrDefault();

    private static Shelf Movies(string key, string name, Func<BaseItem, bool> pick, Func<BaseItem, double>? sort = null, int limit = MaxItems)
        => new(key, name, "MOVIES", MovieMin, pick, sort, limit);

    // released (not added to the server) in the last year, newest first
    private static bool NewRelease(BaseItem m, DateTime today) => DaysAgo(m.PremiereDate, today) is >= 0 and <= 365;

    private static Shelf Shows(string key, string name, Func<BaseItem, bool> pick, Func<BaseItem, double>? sort = null, int min = ShowMin)
        => new(key, name, "SHOWS", min, pick, sort);

    private static List<Shelf> MovieShelves(DateTime today) =>
    [
        Movies("new-releases", "New Releases", m => NewRelease(m, today), m => DaysAgo(m.PremiereDate, today), GenreLimit),
        Movies("top-rated", "Top Rated", m => Rating(m) >= 7.5),
        Movies("critics-picks", "Critics' Picks", m => (m.CriticRating ?? 0) >= 90 && (m.CommunityRating ?? 0) >= 7.0, m => -((m.CriticRating ?? 0) + (5 * Rating(m)))),
        Movies("fresh-hits", "Fresh Hits", m => DaysAgo(m.DateCreated, today) <= 45 && Rating(m) >= 6.8, m => DaysAgo(m.DateCreated, today)),
        Movies("short-sweet", "Short & Sweet", m => Minutes(m) is > 0 and <= 95 && Rating(m) >= 7.0),
        Movies("date-night", "Date Night", m => G(m, "Romance") && G(m, "Comedy", "Drama") && Rating(m) >= 6.5),
        Movies("edge-of-seat", "Edge of Your Seat", m => G(m, "Thriller") && G(m, "Crime", "Mystery") && Rating(m) >= 6.8),
        Movies("mind-benders", "Mind-Benders", m => G(m, "Science Fiction") && G(m, "Mystery") && Rating(m) >= 6.5),
        Movies("family-night", "Family Movie Night", m => G(m, "Family", "Animation") && KidRatings.Contains(m.OfficialRating ?? string.Empty) && Rating(m) >= 6.5),
        Movies("laugh-out-loud", "Laugh Out Loud", m => G(m, "Comedy") && !G(m, "Family", "Animation", "Crime", "Thriller", "Horror", "Romance") && Rating(m) >= 6.5),
        Movies("fright-night", "Fright Night", m => G(m, "Horror") && Rating(m) >= 6.3),
        Movies("epic-adventures", "Epic Adventures", m => G(m, "Adventure") && G(m, "Fantasy", "Action") && Rating(m) >= 7.0),
        Movies("crime-sagas", "Crime Sagas", m => G(m, "Crime") && G(m, "Drama") && Rating(m) >= 7.0),
        Movies("war-history", "War & History", m => G(m, "War", "History") && Rating(m) >= 6.8),
        Movies("westerns", "Westerns", m => G(m, "Western") && Rating(m) >= 6.3),
        Movies("documentaries", "Documentaries", m => G(m, "Documentary") && Rating(m) >= 6.5),
    ];

    // TV genres follow TMDb's TV list ("Sci-Fi & Fantasy", "Action & Adventure", "War & Politics")
    private static List<Shelf> ShowShelves(DateTime today) =>
    [
        Shows("tv-new-episodes", "New Episodes", s => DaysAgo(LastEpisodeAdded(s), today) <= 21, s => DaysAgo(LastEpisodeAdded(s), today), min: 4),
        Shows("tv-new-releases", "New TV Releases", s => NewRelease(s, today), s => DaysAgo(s.PremiereDate, today)),
        Shows("tv-top-rated", "Top Rated TV", s => Rating(s) >= 8.0),
        Shows("tv-kids", "Kids & Family TV", s => G(s, "Kids", "Family") && Rating(s) >= 6.5),
        Shows("tv-docuseries", "Docuseries", s => G(s, "Documentary") && Rating(s) >= 7.0),
        Shows("tv-reality", "Reality TV", s => G(s, "Reality")),
    ];

    private static string Slug(string text) => new(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());

    /// <summary>Two shelves per genre: its best all-time, and its best from the last few years.</summary>
    private static IEnumerable<Shelf> Genres(string prefix, string unit, int min, (string Genre, string Plural, string Single)[] genres, DateTime today)
    {
        foreach (var (genre, plural, single) in genres)
        {
            yield return new Shelf($"{prefix}top-{Slug(genre)}", $"Top Rated {plural}", unit, min, m => G(m, genre) && Rating(m) >= 7.0, Limit: GenreLimit);
            yield return new Shelf($"{prefix}recent-{Slug(genre)}", $"Recent {single} Hits", unit, min,
                m => G(m, genre) && today.Year - (m.ProductionYear ?? 0) < RecentYears && Rating(m) >= 6.5, Limit: GenreLimit);
        }
    }

    /// <summary>Genre + decade shelves, e.g. "90s Action", for the biggest combos in the library.</summary>
    private static List<Shelf> Eras(IReadOnlyList<BaseItem> movies)
    {
        var counts = movies
            .Where(m => m.ProductionYear is int y && y < 2020)   // 2020s is just "new"; eras are for nostalgia
            .Where(m => MainGenre(m) is string g && EraGenres.Contains(g) && Rating(m) >= 6.0)
            .GroupBy(m => (Decade: m.ProductionYear!.Value / 10 * 10, Genre: MainGenre(m)!))
            .Select(group => (group.Key.Decade, group.Key.Genre, Count: group.Count()))
            .Where(c => c.Count >= EraMin)
            .OrderByDescending(c => c.Count);

        // round-robin: each decade's best genre first (oldest decade first), then each decade's runner-up
        var byDecade = new SortedDictionary<int, List<string>>();
        foreach (var (decade, genre, _) in counts)
        {
            if (!byDecade.TryGetValue(decade, out var genres))
            {
                byDecade[decade] = genres = [];
            }

            genres.Add(genre);
        }

        var picks = Enumerable.Range(0, EraPerDecade)
            .SelectMany(round => byDecade.Where(d => round < d.Value.Count).Select(d => (Decade: d.Key, Genre: d.Value[round])))
            .Take(EraLimit)
            .OrderBy(p => p.Decade)
            .ThenBy(p => p.Genre, StringComparer.Ordinal);

        return picks.Select(p =>
        {
            string label = p.Decade >= 2000 || p.Decade < 1950 ? $"{p.Decade}s" : $"{p.Decade % 100}s";
            string genre = p.Genre == "Science Fiction" ? "Sci-Fi" : p.Genre;
            return Movies(
                $"era-{p.Decade}-{p.Genre.ToLowerInvariant().Replace(' ', '-')}",
                $"{label} {genre}",
                m => (m.ProductionYear ?? 0) / 10 * 10 == p.Decade && MainGenre(m) == p.Genre && Rating(m) >= 6.0);
        }).ToList();
    }

    /// <summary>Every shelf with enough items, with its items in display order.</summary>
    public static List<ShelfResult> Build(IReadOnlyList<BaseItem> movies, IReadOnlyList<BaseItem> shows, DateTime today)
    {
        var results = new List<ShelfResult>();
        void Fill(IEnumerable<Shelf> shelves, IReadOnlyList<BaseItem> items)
        {
            foreach (Shelf shelf in shelves)
            {
                List<Guid> ids = items.Where(shelf.Pick)
                    .OrderBy(shelf.Sort ?? (m => -Rating(m)))
                    .Take(shelf.Limit)
                    .Select(m => m.Id)
                    .ToList();
                if (ids.Count >= shelf.Min)
                {
                    results.Add(new ShelfResult(shelf, ids));
                }
            }
        }

        Fill(MovieShelves(today).Concat(Genres(string.Empty, "MOVIES", MovieMin, MovieGenres, today)).Concat(Eras(movies)), movies);
        Fill(ShowShelves(today).Concat(Genres("tv-", "SHOWS", ShowMin, ShowGenres, today)), shows);
        return results;
    }
}
