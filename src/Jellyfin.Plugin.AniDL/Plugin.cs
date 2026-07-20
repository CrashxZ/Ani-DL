using System.Globalization;
using Jellyfin.Plugin.AniDL.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.AniDL;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static readonly Guid PluginId = Guid.Parse("6c408441-bce2-4f41-a7d7-b2786f759342");

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "AniDL";

    public override string Description => "Search, queue, and import authorized anime downloads into Jellyfin.";

    public override Guid Id => PluginId;

    public IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = GetType().Namespace;
        return
        [
            new PluginPageInfo
            {
                Name = "AniDL",
                DisplayName = "Anime Downloader",
                MenuSection = "server",
                MenuIcon = "download",
                EnableInMainMenu = true,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.anidl.html", ns)
            },
            new PluginPageInfo
            {
                Name = "AniDLJS",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.anidl.js", ns)
            },
            new PluginPageInfo
            {
                Name = "AniDLConfig",
                DisplayName = "AniDL",
                MenuSection = "server",
                MenuIcon = "settings",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.config.html", ns)
            },
            new PluginPageInfo
            {
                Name = "AniDLConfigJS",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.config.js", ns)
            }
        ];
    }
}

