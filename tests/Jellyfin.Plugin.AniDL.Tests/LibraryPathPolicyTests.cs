using Jellyfin.Plugin.AniDL.Security;

namespace Jellyfin.Plugin.AniDL.Tests;

public sealed class LibraryPathPolicyTests
{
    [Fact]
    public void BuildEpisodePathKeepsOutputUnderRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "anidl-library");
        var result = LibraryPathPolicy.BuildEpisodePath(root, "A/../Danger: Title", 1, 2);

        Assert.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, result, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        Assert.EndsWith(".mkv", result, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildEpisodePathRequiresAbsoluteRoot()
    {
        Assert.Throws<InvalidOperationException>(() => LibraryPathPolicy.BuildEpisodePath("relative", "Title", 1, 1));
    }

    [Theory]
    [InlineData("../", ".._")]
    [InlineData("A/B", "A_B")]
    public void SanitizeSegmentRemovesPathSyntax(string input, string expected)
    {
        Assert.Equal(expected, LibraryPathPolicy.SanitizeSegment(input));
    }
}
