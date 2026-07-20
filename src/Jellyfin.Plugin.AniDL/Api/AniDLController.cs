using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Security.Claims;
using Jellyfin.Plugin.AniDL.Downloads;
using Jellyfin.Plugin.AniDL.Models;
using Jellyfin.Plugin.AniDL.Sources;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.AniDL.Api;

[ApiController]
[Authorize]
[Route("AniDL")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class AniDLController(SourceRegistry sources, DownloadQueue queue, IUserManager userManager) : ControllerBase
{
    [HttpGet("Search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AnimeCard>>> Search([Required] string query, string source = "anisuge", CancellationToken cancellationToken = default)
    {
        if (!TryAuthorize(out _))
        {
            return Forbid();
        }

        return Ok(await sources.GetRequired(source).SearchAsync(query, cancellationToken).ConfigureAwait(false));
    }

    [HttpGet("Browse")]
    public async Task<ActionResult<IReadOnlyList<AnimeCard>>> Browse(string category = "updated", string source = "anisuge", CancellationToken cancellationToken = default)
    {
        if (!TryAuthorize(out _))
        {
            return Forbid();
        }

        return Ok(await sources.GetRequired(source).BrowseAsync(category, cancellationToken).ConfigureAwait(false));
    }

    [HttpGet("Details")]
    public async Task<ActionResult<AnimeDetails>> Details([Required] string url, string source = "anisuge", CancellationToken cancellationToken = default)
    {
        if (!TryAuthorize(out _))
        {
            return Forbid();
        }

        return Ok(await sources.GetRequired(source).GetDetailsAsync(url, cancellationToken).ConfigureAwait(false));
    }

    [HttpGet("Episodes")]
    public async Task<ActionResult<IReadOnlyList<Episode>>> Episodes([Required] string url, string source = "anisuge", CancellationToken cancellationToken = default)
    {
        if (!TryAuthorize(out _))
        {
            return Forbid();
        }

        return Ok(await sources.GetRequired(source).GetEpisodesAsync(url, cancellationToken).ConfigureAwait(false));
    }

    [HttpGet("Downloads")]
    public ActionResult<IReadOnlyList<DownloadJob>> Downloads()
    {
        if (!TryAuthorize(out var user))
        {
            return Forbid();
        }

        return Ok(queue.GetJobs().Where(x => user.IsAdministrator || x.OwnerUserId == user.Id).ToArray());
    }

    [HttpPost("Downloads")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<DownloadJob>> Queue([FromBody] CreateDownloadRequest request, CancellationToken cancellationToken)
    {
        if (!TryAuthorize(out var user))
        {
            return Forbid();
        }

        var source = sources.GetRequired(request.SourceId);
        var details = await source.GetDetailsAsync(request.SeriesUrl, cancellationToken).ConfigureAwait(false);
        var episodes = await source.GetEpisodesAsync(request.SeriesUrl, cancellationToken).ConfigureAwait(false);
        var episode = episodes.SingleOrDefault(x => x.Slug.Equals(request.EpisodeSlug, StringComparison.OrdinalIgnoreCase));
        if (episode is null)
        {
            return BadRequest("The requested episode does not exist.");
        }

        if (request.Audio == AudioPreference.EnglishDub && !episode.HasEnglishDub)
        {
            return BadRequest("English dub is not available for this episode.");
        }

        if (request.Audio == AudioPreference.Japanese && !episode.HasJapaneseWithEnglishSubtitles)
        {
            return BadRequest("Japanese audio with English subtitles is not available for this episode.");
        }

        var trusted = new QueueDownloadRequest(request.SourceId, details.Url, details.Title, episode.Slug, episode.Number, Math.Clamp(request.SeasonNumber, 0, 99), request.Audio, request.Audio == AudioPreference.Japanese && request.IncludeEnglishSubtitles);
        var job = await queue.EnqueueAsync(trusted, user.Id, cancellationToken).ConfigureAwait(false);
        return Accepted(job);
    }

    [HttpDelete("Downloads/{id:guid}")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        if (!TryAuthorize(out var user))
        {
            return Forbid();
        }

        return await queue.CancelAsync(id, user.Id, user.IsAdministrator, cancellationToken).ConfigureAwait(false) ? NoContent() : NotFound();
    }

    private bool TryAuthorize(out UserContext context)
    {
        var idValue = User.FindFirst("Jellyfin-UserId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var name = User.FindFirst(ClaimTypes.Name)?.Value ?? User.Identity?.Name;
        var jellyfinUser = Guid.TryParse(idValue, out var id) ? userManager.GetUserById(id) : string.IsNullOrWhiteSpace(name) ? null : userManager.GetUserByName(name);
        if (jellyfinUser is null)
        {
            context = default;
            return false;
        }

        var userId = jellyfinUser.Id.ToString("N");
        var isAdministrator = userManager.GetUserDto(jellyfinUser).Policy.IsAdministrator;
        var configuration = Plugin.Instance?.Configuration;
        var allowed = isAdministrator || (configuration?.AllowNonAdministratorDownloads == true && configuration.AuthorizedUserIds.Any(value => value.Equals(userId, StringComparison.OrdinalIgnoreCase) || value.Equals(jellyfinUser.Username, StringComparison.OrdinalIgnoreCase)));
        context = new UserContext(userId, isAdministrator);
        return allowed;
    }

    public sealed record CreateDownloadRequest(
        [property: Required] string SourceId,
        [property: Required] string SeriesUrl,
        [property: Required] string EpisodeSlug,
        int SeasonNumber,
        AudioPreference Audio,
        bool IncludeEnglishSubtitles = true);

    private readonly record struct UserContext(string Id, bool IsAdministrator);
}
