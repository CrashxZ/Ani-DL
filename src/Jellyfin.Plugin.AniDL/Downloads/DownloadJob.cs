using Jellyfin.Plugin.AniDL.Models;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.AniDL.Downloads;

[JsonConverter(typeof(JsonStringEnumConverter<DownloadState>))]
public enum DownloadState
{
    Queued,
    Resolving,
    Downloading,
    Completed,
    Failed,
    Cancelled
}

public sealed class DownloadJob
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string OwnerUserId { get; init; }

    public required QueueDownloadRequest Request { get; init; }

    public DownloadState State { get; set; } = DownloadState.Queued;

    public int Attempt { get; set; }

    public double ProgressPercent { get; set; }

    public string? DestinationPath { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
