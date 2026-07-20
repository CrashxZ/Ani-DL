using System.Net;
using System.Net.Sockets;
using Jellyfin.Plugin.AniDL.Downloads;
using Jellyfin.Plugin.AniDL.Security;
using Jellyfin.Plugin.AniDL.Sources;
using Jellyfin.Plugin.AniDL.Sources.AniSuge;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.AniDL;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient("AniDL.AniSuge", client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                ConnectCallback = ConnectPublicAsync
            });
        serviceCollection.AddSingleton<IAnimeSource, AniSugeSource>();
        serviceCollection.AddSingleton<SourceRegistry>();
        serviceCollection.AddSingleton<DownloadStore>();
        serviceCollection.AddSingleton<FfmpegRunner>();
        serviceCollection.AddSingleton<DownloadQueue>();
        serviceCollection.AddHostedService(provider => provider.GetRequiredService<DownloadQueue>());
    }

    private static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await RemoteUriGuard.GetPublicAddressesAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
        Exception? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, true);
            }
            catch (Exception exception) when (exception is SocketException or IOException)
            {
                socket.Dispose();
                lastError = exception;
            }
        }

        throw new HttpRequestException("No public endpoint for the remote host could be reached.", lastError);
    }
}
