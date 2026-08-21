namespace CognitivePlatform.Api.Registry.Domains;

public sealed record WatchListDomain : IDomainDefinition
{
    public string Name        => "WatchList";
    public string Description => "Track movies, TV shows, and streaming availability across platforms.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "watchlist"
      , "watch list"
      , "movie"
      , "movies"
      , "show"
      , "shows"
      , "series"
      , "streaming"
      , "netflix"
      , "prime video"
      , "hulu"
      , "disney"
      , "watched"
      , "watching"
      , "to watch"
    };
}
