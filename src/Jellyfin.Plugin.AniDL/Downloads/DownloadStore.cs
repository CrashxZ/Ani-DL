using System.Text.Json;

namespace Jellyfin.Plugin.AniDL.Downloads;

public sealed class DownloadStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;

    public DownloadStore()
    {
        var folder = Plugin.Instance?.DataFolderPath ?? throw new InvalidOperationException("The plugin data path is unavailable.");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "downloads.json");
    }

    public async Task<IReadOnlyList<DownloadJob>> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<List<DownloadJob>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(IEnumerable<DownloadJob> jobs, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporary = _path + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, jobs.OrderBy(x => x.CreatedAt), JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, _path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
