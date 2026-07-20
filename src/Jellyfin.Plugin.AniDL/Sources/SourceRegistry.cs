namespace Jellyfin.Plugin.AniDL.Sources;

public sealed class SourceRegistry(IEnumerable<IAnimeSource> sources)
{
    private readonly Dictionary<string, IAnimeSource> _sources = sources.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

    public IAnimeSource GetRequired(string id) => _sources.TryGetValue(id, out var source)
        ? source
        : throw new KeyNotFoundException($"Unknown anime source '{id}'.");
}
