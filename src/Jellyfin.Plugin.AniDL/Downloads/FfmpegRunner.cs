using System.Diagnostics;
using Jellyfin.Plugin.AniDL.Models;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.AniDL.Downloads;

public sealed class FfmpegRunner(IMediaEncoder mediaEncoder, ILogger<FfmpegRunner> logger)
{
    public async Task RunAsync(MediaResource resource, string destinationPath, Action<double> progress, CancellationToken cancellationToken)
    {
        var configured = Plugin.Instance?.Configuration.FfmpegPath;
        var executable = string.IsNullOrWhiteSpace(configured) ? mediaEncoder.EncoderPath : configured;
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException("Jellyfin's ffmpeg executable could not be located.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var temporary = Path.ChangeExtension(destinationPath, ".part.mkv");
        if (File.Exists(destinationPath))
        {
            throw new IOException("The destination episode already exists.");
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-nostdin");
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("warning");
        startInfo.ArgumentList.Add("-y");
        if (resource.Headers.Count > 0)
        {
            var headers = string.Join("\r\n", resource.Headers.Select(pair => $"{ValidateHeader(pair.Key)}: {ValidateHeader(pair.Value)}")) + "\r\n";
            startInfo.ArgumentList.Add("-headers");
            startInfo.ArgumentList.Add(headers);
        }

        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(resource.Uri.AbsoluteUri);
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("0");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("copy");
        startInfo.ArgumentList.Add("-progress");
        startInfo.ArgumentList.Add("pipe:1");
        startInfo.ArgumentList.Add(temporary);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("ffmpeg could not be started.");
            }

            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch (InvalidOperationException)
                {
                }
            });

            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            while (await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (line == "progress=end")
                {
                    progress(100);
                }
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var error = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                var safeError = Regex.Replace(error, @"https?://\S+", "[media-url]", RegexOptions.IgnoreCase);
                throw new InvalidOperationException($"ffmpeg exited with code {process.ExitCode}: {Truncate(safeError, 1000)}");
            }

            File.Move(temporary, destinationPath, false);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        logger.LogInformation("Completed AniDL media import at {DestinationPath}", destinationPath);
    }

    private static string ValidateHeader(string value)
    {
        if (value.Contains('\r') || value.Contains('\n'))
        {
            throw new InvalidOperationException("Media headers cannot contain line breaks.");
        }

        return value;
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
