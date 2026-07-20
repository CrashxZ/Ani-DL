using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Jellyfin.Plugin.AniDL.Api;

namespace Jellyfin.Plugin.AniDL.Tests;

public sealed class DownloadRequestValidationTests
{
    [Fact]
    public void RequiredValidationIsDefinedOnRecordConstructorParameters()
    {
        var requestType = typeof(AniDLController.CreateDownloadRequest);
        var constructor = Assert.Single(requestType.GetConstructors());

        foreach (var name in new[] { "SourceId", "SeriesUrl", "EpisodeSlug" })
        {
            var property = requestType.GetProperty(name);
            Assert.NotNull(property);
            Assert.Null(property!.GetCustomAttribute<RequiredAttribute>());

            var parameter = Assert.Single(constructor.GetParameters(), value => value.Name == name);
            Assert.NotNull(parameter.GetCustomAttribute<RequiredAttribute>());
        }
    }
}
