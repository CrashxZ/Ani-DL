using System.Collections.Concurrent;
using System.Threading.Channels;
using Jellyfin.Plugin.AniDL.Models;
using Jellyfin.Plugin.AniDL.Security;
using Jellyfin.Plugin.AniDL.Sources;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AniDL.Downloads;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "This hosted service is the bounded download queue exposed by the plugin API.")]
public sealed partial class DownloadQueue(
    DownloadStore store,
    SourceRegistry sources,
    FfmpegRunner ffmpeg,
    ILibraryMonitor libraryMonitor,
    ILogger<DownloadQueue> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, DownloadJob> _jobs = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellations = new();
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(Math.Clamp(Plugin.Instance?.Configuration.QueueCapacity ?? 100, 1, 1000))
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleWriter = false,
        SingleReader = false
    });
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly object _enqueueGate = new();

    public IReadOnlyList<DownloadJob> GetJobs() => _jobs.Values.OrderByDescending(x => x.CreatedAt).ToArray();

    public async Task<DownloadJob> EnqueueAsync(QueueDownloadRequest request, string ownerUserId, CancellationToken cancellationToken)
    {
        DownloadJob job;
        lock (_enqueueGate)
        {
            if (_jobs.Values.Any(x => (x.State is DownloadState.Queued or DownloadState.Resolving or DownloadState.Downloading) && IsSameEpisode(x.Request, request)))
            {
                throw new InvalidOperationException("This episode is already queued.");
            }

            job = new DownloadJob { OwnerUserId = ownerUserId, Request = request };
            if (!_jobs.TryAdd(job.Id, job) || !_channel.Writer.TryWrite(job.Id))
            {
                _jobs.TryRemove(job.Id, out _);
                throw new InvalidOperationException("The download queue is full.");
            }
        }

        await PersistAsync(cancellationToken).ConfigureAwait(false);
        return job;
    }

    public async Task<bool> CancelAsync(Guid id, string requesterUserId, bool isAdministrator, CancellationToken cancellationToken)
    {
        if (!_jobs.TryGetValue(id, out var job) || (!isAdministrator && job.OwnerUserId != requesterUserId))
        {
            return false;
        }

        if (_cancellations.TryGetValue(id, out var source))
        {
            source.Cancel();
        }

        job.State = DownloadState.Cancelled;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await PersistAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var stored in await store.LoadAsync(stoppingToken).ConfigureAwait(false))
        {
            if (stored.State is DownloadState.Resolving or DownloadState.Downloading)
            {
                stored.State = DownloadState.Queued;
                stored.Error = "Jellyfin restarted; the job was safely re-queued.";
            }

            _jobs[stored.Id] = stored;
            if (stored.State == DownloadState.Queued)
            {
                _channel.Writer.TryWrite(stored.Id);
            }
        }

        var count = Math.Clamp(Plugin.Instance?.Configuration.MaxConcurrentDownloads ?? 1, 1, 4);
        await Task.WhenAll(Enumerable.Range(0, count).Select(_ => WorkerAsync(stoppingToken))).ConfigureAwait(false);
    }

    private async Task WorkerAsync(CancellationToken stoppingToken)
    {
        await foreach (var id in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            if (!_jobs.TryGetValue(id, out var job) || job.State != DownloadState.Queued)
            {
                continue;
            }

            using var userCancellation = new CancellationTokenSource();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, userCancellation.Token);
            _cancellations[id] = userCancellation;
            try
            {
                await ProcessAsync(job, cancellation.Token, stoppingToken).ConfigureAwait(false);
            }
            finally
            {
                _cancellations.TryRemove(id, out _);
            }
        }
    }

    private async Task ProcessAsync(DownloadJob job, CancellationToken cancellationToken, CancellationToken stoppingToken)
    {
        var configuration = Plugin.Instance?.Configuration ?? throw new InvalidOperationException("Plugin configuration is unavailable.");
        var maxAttempts = Math.Clamp(configuration.MaxRetries + 1, 1, 6);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                job.Attempt = attempt;
                job.State = DownloadState.Resolving;
                job.Error = null;
                Touch(job);
                await PersistAsync(cancellationToken).ConfigureAwait(false);

                var source = sources.GetRequired(job.Request.SourceId);
                var episode = new Episode(job.Request.SourceId, job.Request.SeriesUrl, job.Request.EpisodeSlug, job.Request.EpisodeNumber, true, job.Request.Audio == AudioPreference.EnglishDub);
                var resource = await source.ResolveAsync(episode, job.Request.Audio, job.Request.IncludeEnglishSubtitles, cancellationToken).ConfigureAwait(false);
                var destination = LibraryPathPolicy.BuildEpisodePath(configuration.LibraryRoot, job.Request.SeriesTitle, job.Request.SeasonNumber, job.Request.EpisodeNumber);
                job.DestinationPath = destination;
                job.State = DownloadState.Downloading;
                Touch(job);
                await PersistAsync(cancellationToken).ConfigureAwait(false);
                await ffmpeg.RunAsync(resource, destination, value => job.ProgressPercent = value, cancellationToken).ConfigureAwait(false);

                job.State = DownloadState.Completed;
                job.ProgressPercent = 100;
                Touch(job);
                await PersistAsync(cancellationToken).ConfigureAwait(false);
                if (configuration.AutoRefreshLibrary)
                {
                    libraryMonitor.ReportFileSystemChanged(Path.GetDirectoryName(destination)!);
                }

                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                job.State = DownloadState.Queued;
                job.Error = "Jellyfin stopped; the job will resume after restart.";
                Touch(job);
                await PersistWithoutCancellationAsync().ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                job.State = DownloadState.Cancelled;
                job.Error = "Cancelled";
                Touch(job);
                await PersistWithoutCancellationAsync().ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (attempt < maxAttempts)
            {
                job.State = DownloadState.Queued;
                job.Error = exception.Message;
                Touch(job);
                await PersistWithoutCancellationAsync().ConfigureAwait(false);
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
                LogRetry(logger, exception, job.Id, attempt, delay);
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    job.Error = "Jellyfin stopped; the job will resume after restart.";
                    Touch(job);
                    await PersistWithoutCancellationAsync().ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException)
                {
                    job.State = DownloadState.Cancelled;
                    job.Error = "Cancelled";
                    Touch(job);
                    await PersistWithoutCancellationAsync().ConfigureAwait(false);
                    return;
                }
            }
            catch (Exception exception)
            {
                job.State = DownloadState.Failed;
                job.Error = exception.Message;
                Touch(job);
                LogFailure(logger, exception, job.Id);
                await PersistWithoutCancellationAsync().ConfigureAwait(false);
                return;
            }
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await store.SaveAsync(_jobs.Values, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private Task PersistWithoutCancellationAsync() => PersistAsync(CancellationToken.None);

    private static bool IsSameEpisode(QueueDownloadRequest left, QueueDownloadRequest right) => left.SourceId.Equals(right.SourceId, StringComparison.OrdinalIgnoreCase) && left.SeriesUrl.Equals(right.SeriesUrl, StringComparison.OrdinalIgnoreCase) && left.EpisodeSlug.Equals(right.EpisodeSlug, StringComparison.OrdinalIgnoreCase) && left.Audio == right.Audio;

    private static void Touch(DownloadJob job) => job.UpdatedAt = DateTimeOffset.UtcNow;

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning, Message = "AniDL job {JobId} failed on attempt {Attempt}; retrying in {Delay}")]
    private static partial void LogRetry(ILogger logger, Exception exception, Guid jobId, int attempt, TimeSpan delay);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Error, Message = "AniDL job {JobId} failed")]
    private static partial void LogFailure(ILogger logger, Exception exception, Guid jobId);
}
