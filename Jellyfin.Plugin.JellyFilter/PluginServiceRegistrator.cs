using Jellyfin.Plugin.JellyFilter.Providers;
using Jellyfin.Plugin.JellyFilter.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyFilter;

/// <summary>
/// Registers the plugin's services with the server.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Shared so that the parsed filter files are cached once for the watcher, the segment
        // provider and the API.
        serviceCollection.AddSingleton<FilterFileRepository>();
        serviceCollection.AddHostedService<PlaybackFilterService>();
        serviceCollection.AddSingleton<IMediaSegmentProvider, FilterSegmentProvider>();
    }
}
