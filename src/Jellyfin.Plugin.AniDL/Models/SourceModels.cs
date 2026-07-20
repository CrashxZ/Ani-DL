using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.AniDL.Models;

[JsonConverter(typeof(JsonStringEnumConverter<AudioPreference>))]
public enum AudioPreference
{
    Japanese,
    EnglishDub
}

public sealed record AnimeCard(
    string SourceId,
    string Title,
    string Url,
    string? PosterUrl,
    string? MediaType,
    int SubtitledEpisodes,
    int DubbedEpisodes);

public sealed record AnimeDetails(
    string SourceId,
    string Title,
    string Url,
    string? PosterUrl,
    string? Description,
    string? MediaType,
    int SubtitledEpisodes,
    int DubbedEpisodes);

public sealed record Episode(
    string SourceId,
    string SeriesUrl,
    string Slug,
    double Number,
    bool HasJapaneseWithEnglishSubtitles,
    bool HasEnglishDub);

public sealed record MediaResource(
    Uri Uri,
    string Container,
    AudioPreference Audio,
    string? SubtitleLanguage,
    IReadOnlyDictionary<string, string> Headers);

public sealed record QueueDownloadRequest(
    string SourceId,
    string SeriesUrl,
    string SeriesTitle,
    string EpisodeSlug,
    double EpisodeNumber,
    int SeasonNumber,
    AudioPreference Audio,
    bool IncludeEnglishSubtitles);
