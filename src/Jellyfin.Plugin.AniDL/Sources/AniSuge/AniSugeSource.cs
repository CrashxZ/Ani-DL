using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Jellyfin.Plugin.AniDL.Models;
using Jellyfin.Plugin.AniDL.Security;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AniDL.Sources.AniSuge;

public sealed partial class AniSugeSource(IHttpClientFactory httpClientFactory, ILogger<AniSugeSource> logger) : IAnimeSource
{
    private const int MaxPageBytes = 2 * 1024 * 1024;
    private readonly HtmlParser _parser = new();

    public string Id => "anisuge";

    public async Task<IReadOnlyList<AnimeCard>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        query = query.Trim();
        if (query.Length is < 2 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Search text must contain between 2 and 100 characters.");
        }

        var html = await GetSiteTextAsync($"filter?keyword={Uri.EscapeDataString(query)}", null, cancellationToken).ConfigureAwait(false);
        return ParseCards(html);
    }

    public async Task<IReadOnlyList<AnimeCard>> BrowseAsync(string category, CancellationToken cancellationToken)
    {
        var path = category.ToLowerInvariant() switch
        {
            "home" or "updated" => "home",
            "new" => "new-release",
            "popular" => "most-viewed",
            _ => throw new ArgumentOutOfRangeException(nameof(category), "Supported categories are home, updated, new, and popular.")
        };
        var html = await GetSiteTextAsync(path, null, cancellationToken).ConfigureAwait(false);
        return ParseCards(html);
    }

    public async Task<AnimeDetails> GetDetailsAsync(string seriesUrl, CancellationToken cancellationToken)
    {
        var uri = ValidateSeriesUri(seriesUrl);
        var html = await GetSiteTextAsync(uri.PathAndQuery.TrimStart('/'), uri, cancellationToken).ConfigureAwait(false);
        var document = await _parser.ParseDocumentAsync(html, cancellationToken).ConfigureAwait(false);
        var root = document.QuerySelector(".watch-wrap") ?? throw new InvalidDataException("AniSuge watch-page marker was not found.");
        var counts = ReadCounts(root);
        return new AnimeDetails(
            Id,
            Text(document.QuerySelector("#media-info h1.title")) ?? throw new InvalidDataException("Anime title was not found."),
            root.GetAttribute("data-url") ?? seriesUrl,
            document.QuerySelector("#media-info .poster img")?.GetAttribute("src"),
            Text(document.QuerySelector("#media-info .description .full div")) ?? Text(document.QuerySelector("#media-info .description .short div")),
            Text(document.QuerySelector("#media-info .type")),
            counts.Sub,
            counts.Dub);
    }

    public async Task<IReadOnlyList<Episode>> GetEpisodesAsync(string seriesUrl, CancellationToken cancellationToken)
    {
        var uri = ValidateSeriesUri(seriesUrl);
        var page = await GetSiteTextAsync(uri.PathAndQuery.TrimStart('/'), uri, cancellationToken).ConfigureAwait(false);
        var document = await _parser.ParseDocumentAsync(page, cancellationToken).ConfigureAwait(false);
        var root = document.QuerySelector(".watch-wrap") ?? throw new InvalidDataException("AniSuge watch-page marker was not found.");
        var animeId = root.GetAttribute("data-id") ?? throw new InvalidDataException("AniSuge anime id was not found.");
        var baseUrl = root.GetAttribute("data-url") ?? seriesUrl.Split("/ep-", StringSplitOptions.Ordinal)[0];
        var response = await GetAjaxAsync($"ajax/episode/list/{Uri.EscapeDataString(animeId)}?vrf={Uri.EscapeDataString(AniSugeVrf.Create(animeId))}", uri, cancellationToken).ConfigureAwait(false);
        var episodesDocument = await _parser.ParseDocumentAsync(response, cancellationToken).ConfigureAwait(false);

        return episodesDocument.QuerySelectorAll(".range a[data-slug]")
            .Select(link => new Episode(
                Id,
                baseUrl,
                link.GetAttribute("data-slug")!,
                ParseEpisodeNumber(link),
                link.GetAttribute("data-sub") == "1",
                link.GetAttribute("data-dub") == "1"))
            .ToArray();
    }

    public async Task<MediaResource> ResolveAsync(Episode episode, AudioPreference audio, bool includeEnglishSubtitles, CancellationToken cancellationToken)
    {
        var seriesUri = ValidateSeriesUri(episode.SeriesUrl);
        var page = await GetSiteTextAsync(seriesUri.PathAndQuery.TrimStart('/'), seriesUri, cancellationToken).ConfigureAwait(false);
        var document = await _parser.ParseDocumentAsync(page, cancellationToken).ConfigureAwait(false);
        var animeId = document.QuerySelector(".watch-wrap")?.GetAttribute("data-id") ?? throw new InvalidDataException("AniSuge anime id was not found.");
        var episodeHtml = await GetAjaxAsync($"ajax/episode/list/{animeId}?vrf={Uri.EscapeDataString(AniSugeVrf.Create(animeId))}", seriesUri, cancellationToken).ConfigureAwait(false);
        var episodeDocument = await _parser.ParseDocumentAsync(episodeHtml, cancellationToken).ConfigureAwait(false);
        var episodeNode = episodeDocument.QuerySelector($".range a[data-slug='{CssEscape(episode.Slug)}']") ?? throw new InvalidDataException("The requested episode no longer exists.");
        var serverIds = episodeNode.GetAttribute("data-ids") ?? throw new InvalidDataException("AniSuge server ids were not found.");
        var serverHtml = await GetAjaxAsync($"ajax/server/list?servers={Uri.EscapeDataString(serverIds)}", seriesUri, cancellationToken).ConfigureAwait(false);
        var serverDocument = await _parser.ParseDocumentAsync(serverHtml, cancellationToken).ConfigureAwait(false);
        var type = audio == AudioPreference.EnglishDub ? "dub" : "sub";
        var servers = serverDocument.QuerySelectorAll($".server-type[data-type='{type}'] .server[data-link-id]");
        var linkIds = new List<string>();
        try
        {
            linkIds.AddRange(await GetMapperLinkIdsAsync(episodeNode, type, seriesUri, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "AniSuge mapper alternatives were unavailable");
        }

        linkIds.AddRange(servers.Select(server => server.GetAttribute("data-link-id")).OfType<string>().Where(value => !string.IsNullOrWhiteSpace(value)));
        linkIds = linkIds.Distinct(StringComparer.Ordinal).ToList();
        if (linkIds.Count == 0)
        {
            throw new InvalidOperationException(type == "dub" ? "No English dub is available for this episode." : "No Japanese/English-subtitled source is available for this episode.");
        }

        Exception? lastError = null;
        foreach (var linkId in linkIds)
        {
            try
            {
                var mediaJson = await GetAjaxResponseAsync($"ajax/server?get={Uri.EscapeDataString(linkId)}", seriesUri, cancellationToken).ConfigureAwait(false);
                var embedUrl = mediaJson.Result.ValueKind == System.Text.Json.JsonValueKind.Object
                    && mediaJson.Result.TryGetProperty("url", out var urlElement)
                    ? urlElement.GetString()
                    : null;
                var embed = new Uri(embedUrl ?? throw new InvalidDataException("AniSuge did not return a player URL."));
                await RemoteUriGuard.EnsurePublicHttpsAsync(embed, cancellationToken).ConfigureAwait(false);
                var mediaUri = await FindMediaUriAsync(embed, seriesUri, 0, cancellationToken).ConfigureAwait(false);
                var extension = Path.GetExtension(mediaUri.AbsolutePath).TrimStart('.').ToLowerInvariant();
                return new MediaResource(
                    mediaUri,
                    extension is "m3u8" ? "hls" : extension is "mpd" ? "dash" : extension,
                    audio,
                    audio == AudioPreference.Japanese && includeEnglishSubtitles ? "en" : null,
                    new Dictionary<string, string> { ["Referer"] = embed.AbsoluteUri, ["User-Agent"] = "Mozilla/5.0" });
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lastError = exception;
                logger.LogDebug(exception, "An AniSuge provider could not expose a direct media resource");
            }
        }

        throw new NotSupportedException("None of the available providers exposed a direct HLS, DASH, or MP4 resource.", lastError);
    }

    private IReadOnlyList<AnimeCard> ParseCards(string html)
    {
        var document = _parser.ParseDocument(html);
        return document.QuerySelectorAll(".anime.main-card .item")
            .Select(item =>
            {
                var anchor = item.QuerySelector(".name a[href]") ?? item.QuerySelector("a.poster[href]");
                if (anchor is null)
                {
                    return null;
                }

                var seriesUrl = NormalizeSeriesUrl(anchor.GetAttribute("href")!);
                if (!Uri.TryCreate(seriesUrl, UriKind.Absolute, out var seriesUri)
                    || seriesUri.Scheme != Uri.UriSchemeHttps
                    || !seriesUri.Host.Equals("anisuge.tv", StringComparison.OrdinalIgnoreCase)
                    || !seriesUri.AbsolutePath.StartsWith("/watch/", StringComparison.Ordinal))
                {
                    return null;
                }

                var counts = ReadCounts(item);
                return new AnimeCard(Id, Text(anchor) ?? item.QuerySelector("img")?.GetAttribute("alt") ?? "Unknown", seriesUri.AbsoluteUri, item.QuerySelector("img")?.GetAttribute("data-src") ?? item.QuerySelector("img")?.GetAttribute("src"), Text(item.QuerySelector(".item-status .type")), counts.Sub, counts.Dub);
            })
            .Where(card => card is not null)
            .Cast<AnimeCard>()
            .DistinctBy(card => card.Url, StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();
    }

    private async Task<Uri> FindMediaUriAsync(Uri uri, Uri referer, int depth, CancellationToken cancellationToken)
    {
        if (MediaExtensionRegex().IsMatch(uri.AbsolutePath))
        {
            return uri;
        }

        if (TryDecodeFragmentMediaUri(uri, out var fragmentUri))
        {
            await RemoteUriGuard.EnsurePublicHttpsAsync(fragmentUri, cancellationToken).ConfigureAwait(false);
            return fragmentUri;
        }

        if (depth >= 2)
        {
            throw new NotSupportedException("The selected provider did not expose a direct HLS, DASH, or MP4 resource.");
        }

        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Referrer = referer;
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
        var text = await SendTextAsync(request, cancellationToken).ConfigureAwait(false);
        var decoded = text.Replace("\\/", "/", StringComparison.Ordinal);
        var match = MediaUrlRegex().Match(decoded);
        if (match.Success)
        {
            var direct = new Uri(uri, System.Net.WebUtility.HtmlDecode(match.Groups["url"].Value));
            await RemoteUriGuard.EnsurePublicHttpsAsync(direct, cancellationToken).ConfigureAwait(false);
            return direct;
        }

        var providerDocument = _parser.ParseDocument(decoded);
        var candidates = providerDocument.QuerySelectorAll("video[src], source[src], iframe[src]")
            .Select(node => node.GetAttribute("src"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(5);
        foreach (var candidate in candidates)
        {
            try
            {
                var next = new Uri(uri, System.Net.WebUtility.HtmlDecode(candidate!));
                await RemoteUriGuard.EnsurePublicHttpsAsync(next, cancellationToken).ConfigureAwait(false);
                return await FindMediaUriAsync(next, uri, depth + 1, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogTrace(exception, "Nested provider resource was not directly downloadable");
            }
        }

        throw new NotSupportedException("The selected provider did not expose a direct HLS, DASH, or MP4 resource.");
    }

    private async Task<IReadOnlyList<string>> GetMapperLinkIdsAsync(IElement episodeNode, string type, Uri referer, CancellationToken cancellationToken)
    {
        var mal = episodeNode.GetAttribute("data-mal");
        var slug = episodeNode.GetAttribute("data-slug");
        var timestamp = episodeNode.GetAttribute("data-timestamp");
        if (!IsDigits(mal) || string.IsNullOrWhiteSpace(slug) || !IsDigits(timestamp))
        {
            return [];
        }

        var uri = new Uri($"https://mapper.nekostream.site/api/mal/{Uri.EscapeDataString(mal!)}/{Uri.EscapeDataString(slug)}/{Uri.EscapeDataString(timestamp!)}");
        await RemoteUriGuard.EnsurePublicHttpsAsync(uri, cancellationToken).ConfigureAwait(false);
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Referrer = referer;
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
        var json = await SendTextAsync(request, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var results = new List<string>();
        foreach (var provider in document.RootElement.EnumerateObject())
        {
            if (provider.Value.ValueKind != JsonValueKind.Object
                || !provider.Value.TryGetProperty(type, out var variant)
                || variant.ValueKind != JsonValueKind.Object
                || !variant.TryGetProperty("url", out var link)
                || link.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = link.GetString();
            if (!string.IsNullOrWhiteSpace(value) && value.Length <= 4096)
            {
                results.Add(value);
            }
        }

        return results;
    }

    private static bool TryDecodeFragmentMediaUri(Uri uri, out Uri mediaUri)
    {
        mediaUri = null!;
        if (string.IsNullOrWhiteSpace(uri.Fragment) || uri.Fragment.Length > 4096)
        {
            return false;
        }

        foreach (var token in uri.Fragment.Trim('#').Split('#', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var normalized = token.Replace('-', '+').Replace('_', '/');
                normalized = normalized.PadRight(normalized.Length + ((4 - (normalized.Length % 4)) % 4), '=');
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
                if (Uri.TryCreate(decoded, UriKind.Absolute, out var candidate)
                    && candidate.Scheme == Uri.UriSchemeHttps
                    && MediaExtensionRegex().IsMatch(candidate.AbsolutePath))
                {
                    mediaUri = candidate;
                    return true;
                }
            }
            catch (FormatException)
            {
            }
        }

        return false;
    }

    private static bool IsDigits(string? value) => !string.IsNullOrWhiteSpace(value) && value.All(char.IsAsciiDigit);

    private async Task<string> GetSiteTextAsync(string relative, Uri? referer, CancellationToken cancellationToken)
    {
        var baseUri = GetBaseUri();
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relative));
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; Jellyfin-AniDL/0.1)");
        request.Headers.Referrer = referer;
        return await SendTextAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetAjaxAsync(string relative, Uri referer, CancellationToken cancellationToken)
    {
        var response = await GetAjaxResponseAsync(relative, referer, cancellationToken).ConfigureAwait(false);
        return response.Result.ValueKind == System.Text.Json.JsonValueKind.String
            ? response.Result.GetString() ?? throw new InvalidDataException("AniSuge returned an empty AJAX result.")
            : throw new InvalidDataException("AniSuge returned an unexpected AJAX result.");
    }

    private async Task<AjaxResponse> GetAjaxResponseAsync(string relative, Uri referer, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(GetBaseUri(), relative));
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
        request.Headers.Referrer = referer;
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        var client = httpClientFactory.CreateClient("AniDL.AniSuge");
        using var result = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        result.EnsureSuccessStatusCode();
        var response = await result.Content.ReadFromJsonAsync<AjaxResponse>(cancellationToken: cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException("AniSuge returned invalid JSON.");
        if (response.Status != 200)
        {
            throw new InvalidDataException(response.Message ?? "AniSuge rejected the request.");
        }

        return response;
    }

    private async Task<string> SendTextAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        using (var response = await httpClientFactory.CreateClient("AniDL.AniSuge").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(MaxPageBytes, cancellationToken).ConfigureAwait(false);
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static (int Sub, int Dub) ReadCounts(IParentNode node) => (ReadCount(node.QuerySelector(".dub-sub-total .sub")), ReadCount(node.QuerySelector(".dub-sub-total .dub")));

    private static int ReadCount(IElement? element) => element is null ? 0 : int.TryParse(NumberRegex().Match(element.TextContent).Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static double ParseEpisodeNumber(IElement element) => double.TryParse(element.GetAttribute("data-num") ?? element.TextContent.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : double.Parse(element.GetAttribute("data-slug")!, CultureInfo.InvariantCulture);

    private static string? Text(IElement? element) => string.IsNullOrWhiteSpace(element?.TextContent) ? null : Regex.Replace(element.TextContent, "\\s+", " ").Trim();

    private static string NormalizeSeriesUrl(string value) => EpisodeSuffixRegex().Replace(value, string.Empty);

    private Uri GetBaseUri()
    {
        var configured = Plugin.Instance?.Configuration.AniSugeBaseUrl ?? "https://anisuge.tv";
        var uri = new Uri(configured.TrimEnd('/') + "/");
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("anisuge.tv", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("AniSugeBaseUrl must be https://anisuge.tv.");
        }

        return uri;
    }

    private Uri ValidateSeriesUri(string value)
    {
        var uri = new Uri(value);
        var expected = GetBaseUri();
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals(expected.Host, StringComparison.OrdinalIgnoreCase) || !uri.AbsolutePath.StartsWith("/watch/", StringComparison.Ordinal))
        {
            throw new ArgumentException("Only AniSuge watch URLs are accepted.", nameof(value));
        }

        return uri;
    }

    private static string CssEscape(string value) => value.Replace("'", "\\'", StringComparison.Ordinal);

    private sealed record AjaxResponse(
        [property: JsonPropertyName("status")] int Status,
        [property: JsonPropertyName("result")] System.Text.Json.JsonElement Result,
        [property: JsonPropertyName("message")] string? Message);

    [GeneratedRegex(@"/ep-[^/?#]+(?:[?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeSuffixRegex();

    [GeneratedRegex(@"\d+")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"\.(?:m3u8|mpd|mp4)$", RegexOptions.IgnoreCase)]
    private static partial Regex MediaExtensionRegex();

    [GeneratedRegex("(?<url>https?://[^\\\"'<> ]+?\\.(?:m3u8|mpd|mp4)(?:\\?[^\\\"'<> ]*)?)", RegexOptions.IgnoreCase)]
    private static partial Regex MediaUrlRegex();
}
