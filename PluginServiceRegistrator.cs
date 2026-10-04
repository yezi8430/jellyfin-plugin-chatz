using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using MediaBrowser.Controller.Events.Session;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Chatz
{
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            System.Console.WriteLine("[Gotify] ★ PluginServiceRegistrator.RegisterServices 被调用");

            // IEventConsumer 注册
            serviceCollection.AddScoped<IEventConsumer<AuthenticationResultEventArgs>, AuthenticationSuccessConsumer>();
            serviceCollection.AddScoped<IEventConsumer<AuthenticationRequestEventArgs>, AuthenticationFailureConsumer>();
            serviceCollection.AddScoped<IEventConsumer<PlaybackStartEventArgs>, PlaybackStartConsumer>();
            serviceCollection.AddScoped<IEventConsumer<PlaybackStopEventArgs>, PlaybackStopConsumer>();
            serviceCollection.AddScoped<IEventConsumer<PlaybackProgressEventArgs>, PlaybackProgressConsumer>();
            serviceCollection.AddScoped<IEventConsumer<SubtitleDownloadFailureEventArgs>, SubtitleDownloadFailureConsumer>();

            // ★ 注册 PlaybackProgressHostedService
            serviceCollection.AddSingleton<PlaybackProgressHostedService>();
            serviceCollection.AddHostedService<PlaybackProgressHostedService>(provider => provider.GetRequiredService<PlaybackProgressHostedService>());

            // 注册 MediaLibraryNotifier
            serviceCollection.AddSingleton<MediaLibraryNotifier>();
            serviceCollection.AddHostedService<MediaLibraryNotifier>(provider => provider.GetRequiredService<MediaLibraryNotifier>());

            System.Console.WriteLine("[Gotify] ★ 所有服务已注册（5个 IEventConsumer + 2个 IHostedService）");
        }
    }
}
