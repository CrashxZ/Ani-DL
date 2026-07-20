using System.Text;

namespace Jellyfin.Plugin.AniDL.Security;

public static class LibraryPathPolicy
{
    public static string BuildEpisodePath(string libraryRoot, string seriesTitle, int season, double episode)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot) || !Path.IsPathFullyQualified(libraryRoot))
        {
            throw new InvalidOperationException("An absolute library root must be configured by an administrator.");
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraryRoot));
        var safeTitle = SanitizeSegment(seriesTitle);
        var episodeText = episode % 1 == 0 ? episode.ToString("00", System.Globalization.CultureInfo.InvariantCulture) : episode.ToString("00.##", System.Globalization.CultureInfo.InvariantCulture);
        var relative = Path.Combine(safeTitle, $"Season {season:00}", $"{safeTitle} - S{season:00}E{episodeText}.mkv");
        var result = Path.GetFullPath(Path.Combine(root, relative));
        var prefix = root + Path.DirectorySeparatorChar;
        if (!result.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The destination escaped the configured library root.");
        }

        return result;
    }

    public static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        invalid.UnionWith(['/', '\\', ':', '*', '?', '"', '<', '>', '|']);
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Normalize(NormalizationForm.FormKC))
        {
            builder.Append(char.IsControl(ch) || invalid.Contains(ch) ? '_' : ch);
        }

        var sanitized = builder.ToString().Trim().TrimEnd('.');
        if (sanitized.Length > 120)
        {
            sanitized = sanitized[..120].TrimEnd();
        }

        return string.IsNullOrWhiteSpace(sanitized) ? "Unknown Anime" : sanitized;
    }
}

