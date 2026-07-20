using Jellyfin.Plugin.AniDL.Sources.AniSuge;

namespace Jellyfin.Plugin.AniDL.Tests;

public sealed class AniSugeVrfTests
{
    [Theory]
    [InlineData("94", "GH1ICj==")]
    public void CreateMatchesObservedSiteContract(string input, string expected)
    {
        Assert.Equal(expected, AniSugeVrf.Create(input));
    }
}

