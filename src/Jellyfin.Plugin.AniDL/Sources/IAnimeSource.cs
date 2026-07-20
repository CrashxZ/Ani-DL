using Jellyfin.Plugin.AniDL.Models;

namespace Jellyfin.Plugin.AniDL.Sources;

public interface IAnimeSource
{
    string Id { get; }

    Task<IReadOnlyList<AnimeCard>> SearchAsync(string query, CancellationToken cancellationToken);

    Task<IReadOnlyList<AnimeCard>> BrowseAsync(string category, CancellationToken cancellationToken);

    Task<AnimeDetails> GetDetailsAsync(string seriesUrl, CancellationToken cancellationToken);

    Task<IReadOnlyList<Episode>> GetEpisodesAsync(string seriesUrl, CancellationToken cancellationToken);

    Task<MediaResource> ResolveAsync(Episode episode, AudioPreference audio, bool includeEnglishSubtitles, CancellationToken cancellationToken);
}

