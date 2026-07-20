using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AniDL.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public string LibraryRoot { get; set; } = string.Empty;

    public int MaxConcurrentDownloads { get; set; } = 1;

    public int QueueCapacity { get; set; } = 100;

    public int MaxRetries { get; set; } = 3;

    public bool AutoRefreshLibrary { get; set; } = true;

    public bool AllowNonAdministratorDownloads { get; set; }

    public string[] AuthorizedUserIds { get; set; } = [];

    public string PreferredAudio { get; set; } = "ja";

    public string PreferredSubtitle { get; set; } = "en";

    public bool PreferEnglishDub { get; set; }

    public string FfmpegPath { get; set; } = string.Empty;

    public string AniSugeBaseUrl { get; set; } = "https://anisuge.tv";
}

