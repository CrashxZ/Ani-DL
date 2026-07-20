using System.Net;
using System.Text;
using Jellyfin.Plugin.AniDL.Sources.AniSuge;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AniDL.Tests;

public sealed class AniSugeSourceTests
{
    [Fact]
    public async Task SearchParsesCardsAndNormalizesEpisodeUrl()
    {
        const string html = """
            <div class="anime main-card"><div class="item">
              <a class="poster" href="https://anisuge.tv/watch/example-abc/ep-12"><img data-src="https://cdn.example/poster.jpg" alt="Example"></a>
              <div class="item-status"><span class="type">TV</span></div>
              <div class="dub-sub-total"><span class="sub">12</span><span class="dub">8</span></div>
              <div class="name"><a href="https://anisuge.tv/watch/example-abc/ep-12">Example Anime</a></div>
            </div></div>
            """;
        var source = CreateSource(_ => TextResponse(html));

        var results = await source.SearchAsync("example", CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("Example Anime", result.Title);
        Assert.Equal("https://anisuge.tv/watch/example-abc", result.Url);
        Assert.Equal(12, result.SubtitledEpisodes);
        Assert.Equal(8, result.DubbedEpisodes);
    }

    [Fact]
    public async Task EpisodesUsesVerifiedAjaxContract()
    {
        var requests = new List<HttpRequestMessage>();
        var page = "<div class=\"watch-wrap\" data-id=\"94\" data-url=\"https://anisuge.tv/watch/example-abc\"></div>";
        var episodeMarkup = "<div class=\"range\"><a data-slug=\"1\" data-num=\"1\" data-sub=\"1\" data-dub=\"1\" data-ids=\"opaque\">1</a></div>";
        var ajax = "{\"status\":200,\"result\":" + System.Text.Json.JsonSerializer.Serialize(episodeMarkup) + ",\"message\":\"\"}";
        var source = CreateSource(request =>
        {
            requests.Add(request);
            return request.RequestUri!.AbsolutePath.StartsWith("/ajax/", StringComparison.Ordinal) ? TextResponse(ajax, "application/json") : TextResponse(page);
        });

        var episodes = await source.GetEpisodesAsync("https://anisuge.tv/watch/example-abc/ep-1", CancellationToken.None);

        var episode = Assert.Single(episodes);
        Assert.True(episode.HasJapaneseWithEnglishSubtitles);
        Assert.True(episode.HasEnglishDub);
        var ajaxRequest = Assert.Single(requests.Where(request => request.RequestUri!.AbsolutePath.StartsWith("/ajax/", StringComparison.Ordinal)));
        Assert.Contains("vrf=GH1ICj%3D%3D", ajaxRequest.RequestUri!.Query, StringComparison.Ordinal);
        Assert.True(ajaxRequest.Headers.Contains("X-Requested-With"));
    }

    private static AniSugeSource CreateSource(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var client = new HttpClient(new StubHandler(responder));
        return new AniSugeSource(new StubFactory(client), NullLogger<AniSugeSource>.Instance);
    }

    private static HttpResponseMessage TextResponse(string content, string mediaType = "text/html") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, mediaType)
    };

    private sealed class StubFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }
}
