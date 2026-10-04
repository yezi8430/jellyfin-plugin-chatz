using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using MediaBrowser.Controller.Events.Session;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.HelloWorld
{
    public class AuthenticationSuccessConsumer : IEventConsumer<AuthenticationResultEventArgs>
    {
        public async Task OnEvent(AuthenticationResultEventArgs eventArgs)
        {
            if (eventArgs is null || eventArgs.User == null) return;
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.NotificationTypes.Contains("AuthenticationSuccess")) return;
            var username = eventArgs.User.Name ?? "未知用户";
            var title = config.GetTitleTemplate("AuthenticationSuccess").Replace("{username}", username);
            var message = config.GetTemplate("AuthenticationSuccess").Replace("{username}", username);
            await Notifier.SendAsync(title, message, config, config.GetPriority("AuthenticationSuccess"));
        }
    }

    public class AuthenticationFailureConsumer : IEventConsumer<AuthenticationRequestEventArgs>
    {
        public async Task OnEvent(AuthenticationRequestEventArgs eventArgs)
        {
            if (eventArgs is null) return;
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.NotificationTypes.Contains("AuthenticationFailure")) return;
            var username = eventArgs.Username ?? "未知用户";
            var title = config.GetTitleTemplate("AuthenticationFailure").Replace("{username}", username);
            var message = config.GetTemplate("AuthenticationFailure").Replace("{username}", username);
            await Notifier.SendAsync(title, message, config, config.GetPriority("AuthenticationFailure"));
        }
    }

    public class PlaybackStartConsumer : IEventConsumer<PlaybackStartEventArgs>
    {
        public async Task OnEvent(PlaybackStartEventArgs eventArgs)
        {
            if (eventArgs?.Item is null || eventArgs.Item.IsThemeMedia || eventArgs.Users.Count == 0) return;
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.NotificationTypes.Contains("PlaybackStart")) return;

            var itemName = eventArgs.Item.Name ?? "未知媒体";
            var itemType = eventArgs.Item.GetType().Name;
            var usernames = string.Join(", ", eventArgs.Users.Select(u => u.Username));
            var device = eventArgs.Session?.DeviceName ?? "未知设备";
            var client = eventArgs.Session?.Client ?? "未知客户端";

            int seasonNum = 0, episodeNum = 0;
            string seriesName = itemName;
            if (eventArgs.Item is Episode ep)
            {
                seriesName = ep.SeriesName ?? itemName;
                seasonNum = ep.ParentIndexNumber ?? 0;
                episodeNum = ep.IndexNumber ?? 0;
            }
            var seasonEpisode = (seasonNum > 0 && episodeNum > 0)
                ? $"S{seasonNum.ToString().PadLeft(2, '0')}E{episodeNum.ToString().PadLeft(2, '0')}"
                : "";

            var title = config.GetTitleTemplate("PlaybackStart")
                .Replace("{itemName}", itemName).Replace("{seriesName}", seriesName).Replace("{mediaName}", seriesName)
                .Replace("{seasonNumber}", seasonNum.ToString()).Replace("{episodeNumber}", episodeNum.ToString())
                .Replace("{seasonEpisode}", seasonEpisode).Replace("{itemType}", itemType)
                .Replace("{username}", usernames).Replace("{device}", device).Replace("{client}", client);

            var message = config.GetTemplate("PlaybackStart")
                .Replace("{itemName}", itemName).Replace("{seriesName}", seriesName).Replace("{mediaName}", seriesName)
                .Replace("{seasonNumber}", seasonNum.ToString()).Replace("{episodeNumber}", episodeNum.ToString())
                .Replace("{seasonEpisode}", seasonEpisode).Replace("{itemType}", itemType)
                .Replace("{username}", usernames).Replace("{device}", device).Replace("{client}", client);

            var imageUrl = config.IsCoverEnabled("PlaybackStart") ? Notifier.GetCoverUrl(eventArgs.Item, config) : null;
            PlaybackProgressHandler.RecordPlaybackStart(eventArgs.Item.Id);
            await Notifier.SendAsync(title, message, config, config.GetPriority("PlaybackStart"), imageUrl);
        }
    }

    public class PlaybackStopConsumer : IEventConsumer<PlaybackStopEventArgs>
    {
        public async Task OnEvent(PlaybackStopEventArgs eventArgs)
        {
            if (eventArgs?.Item is null || eventArgs.Item.IsThemeMedia || eventArgs.Users.Count == 0) return;
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.NotificationTypes.Contains("PlaybackStop")) return;

            var itemName = eventArgs.Item.Name ?? "未知媒体";
            var itemType = eventArgs.Item.GetType().Name;
            var usernames = string.Join(", ", eventArgs.Users.Select(u => u.Username));
            var device = eventArgs.Session?.DeviceName ?? "未知设备";
            var completed = eventArgs.PlayedToCompletion ? "是" : "否";

            int seasonNum = 0, episodeNum = 0;
            string seriesName = itemName;
            if (eventArgs.Item is Episode ep)
            {
                seriesName = ep.SeriesName ?? itemName;
                seasonNum = ep.ParentIndexNumber ?? 0;
                episodeNum = ep.IndexNumber ?? 0;
            }
            var seasonEpisode = (seasonNum > 0 && episodeNum > 0)
                ? $"S{seasonNum.ToString().PadLeft(2, '0')}E{episodeNum.ToString().PadLeft(2, '0')}"
                : "";

            var title = config.GetTitleTemplate("PlaybackStop")
                .Replace("{itemName}", itemName).Replace("{seriesName}", seriesName).Replace("{mediaName}", seriesName)
                .Replace("{seasonNumber}", seasonNum.ToString()).Replace("{episodeNumber}", episodeNum.ToString())
                .Replace("{seasonEpisode}", seasonEpisode).Replace("{itemType}", itemType)
                .Replace("{username}", usernames).Replace("{device}", device).Replace("{completed}", completed);

            var message = config.GetTemplate("PlaybackStop")
                .Replace("{itemName}", itemName).Replace("{seriesName}", seriesName).Replace("{mediaName}", seriesName)
                .Replace("{seasonNumber}", seasonNum.ToString()).Replace("{episodeNumber}", episodeNum.ToString())
                .Replace("{seasonEpisode}", seasonEpisode).Replace("{itemType}", itemType)
                .Replace("{username}", usernames).Replace("{device}", device).Replace("{completed}", completed);

            // 追剧连播时，这一集多半已经推过「播放进度到 XX%」，
            // 紧接着的停止通知是重复信息 —— 开了开关就把它吃掉。
            // 注意是「消耗」标记而不是查询（见 TryConsumeProgressNotice 的说明），
            // 否则重看这一集时会两条通知都收不到。
            if (config.SkipStopAfterProgress && PlaybackProgressHandler.TryConsumeProgressNotice(eventArgs.Item.Id))
            {
                PlaybackProgressHandler.ClearPlaybackStart(eventArgs.Item.Id);
                Console.WriteLine($"[Notify] ⏭️ 已推送过进度通知，跳过停止播放: {itemName}");
                return;
            }

            var imageUrl = config.IsCoverEnabled("PlaybackStop") ? Notifier.GetCoverUrl(eventArgs.Item, config) : null;
            PlaybackProgressHandler.ClearPlaybackStart(eventArgs.Item.Id);
            await Notifier.SendAsync(title, message, config, config.GetPriority("PlaybackStop"), imageUrl);
        }
    }

    public class PlaybackProgressConsumer : IEventConsumer<PlaybackProgressEventArgs>
    {
        public async Task OnEvent(PlaybackProgressEventArgs eventArgs)
        {
            await PlaybackProgressHandler.Handle(eventArgs);
        }
    }

    public class PlaybackProgressHostedService : IHostedService
    {
        private readonly ISessionManager _sessionManager;
        public PlaybackProgressHostedService(ISessionManager sessionManager) { _sessionManager = sessionManager; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _sessionManager.PlaybackProgress += OnPlaybackProgress;
            Console.WriteLine("[Notify] ✅ 已订阅 ISessionManager.PlaybackProgress 事件");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _sessionManager.PlaybackProgress -= OnPlaybackProgress;
            return Task.CompletedTask;
        }

        private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
        {
            _ = Task.Run(async () => await PlaybackProgressHandler.Handle(e));
        }
    }

    public static class PlaybackProgressHandler
    {
        private static readonly ConcurrentDictionary<Guid, DateTime> _lastNotify = new();
        private static readonly ConcurrentDictionary<Guid, DateTime> _playbackStart = new();
        private const int StartGracePeriodSeconds = 120;

        public static void RecordPlaybackStart(Guid itemId) => _playbackStart[itemId] = DateTime.UtcNow;
        public static void ClearPlaybackStart(Guid itemId) => _playbackStart.TryRemove(itemId, out _);

        /// <summary>
        /// 「这一集是否推送过播放进度通知」—— **取出即作废**，不是普通查询。
        ///
        /// 为什么必须消耗掉：标记如果不清，看完一集倒回去重看时，
        /// 进度通知因为还在冷却期内推不出来，而停止通知又被上一次残留的标记抑制，
        /// 结果整集一条通知都收不到。每次消耗，就等价于「一次观看对应一次抑制」。
        /// </summary>
        public static bool TryConsumeProgressNotice(Guid itemId) => _lastNotify.TryRemove(itemId, out _);

        public static async Task Handle(PlaybackProgressEventArgs eventArgs)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.PlaybackProgressEnabled) return;
            if (eventArgs?.Item is null || eventArgs.Users.Count == 0) return;
            if (eventArgs.IsPaused) return;

            var itemName = eventArgs.Item.Name ?? "未知媒体";
            var itemId = eventArgs.Item.Id;
            var runtimeTicks = eventArgs.Item.RunTimeTicks ?? 0;
            var positionTicks = eventArgs.PlaybackPositionTicks ?? 0;
            if (runtimeTicks == 0 || positionTicks == 0) return;

            var percent = (int)((double)positionTicks / runtimeTicks * 100);
            var threshold = config.PlaybackProgressPercent;
            var cooldown = TimeSpan.FromMinutes(config.PlaybackProgressCooldownMinutes);

            Console.WriteLine($"[Notify][Debug] Progress: '{itemName}' {percent}% (阈值{threshold}%)");

            if (percent < threshold) return;

            if (_playbackStart.TryGetValue(itemId, out var startTime))
            {
                var elapsed = DateTime.UtcNow - startTime;
                if (elapsed.TotalSeconds < StartGracePeriodSeconds)
                {
                    Console.WriteLine($"[Notify][Debug] Progress: 刚启动 {elapsed.TotalSeconds:F1}s，跳过");
                    return;
                }
            }

            var now = DateTime.UtcNow;
            if (_lastNotify.TryGetValue(itemId, out var lastTime) && now - lastTime < cooldown)
            {
                Console.WriteLine($"[Notify][Debug] Progress: 冷却中");
                return;
            }

            _lastNotify[itemId] = now;
            Console.WriteLine($"[Notify][Debug] Progress: ✅ 触发推送！{percent}%");

            var expire = now.AddHours(-6);
            foreach (var kv in _lastNotify.Where(kv => kv.Value < expire).ToList()) _lastNotify.TryRemove(kv.Key, out _);
            foreach (var kv in _playbackStart.Where(kv => kv.Value < expire).ToList()) _playbackStart.TryRemove(kv.Key, out _);

            var itemType = eventArgs.Item.GetType().Name;
            var usernames = string.Join(", ", eventArgs.Users.Select(u => u.Username));
            var device = eventArgs.Session?.DeviceName ?? eventArgs.DeviceName ?? "未知设备";

            int seasonNum = 0, episodeNum = 0;
            string seriesName = itemName;
            if (eventArgs.Item is Episode ep)
            {
                seriesName = ep.SeriesName ?? itemName;
                seasonNum = ep.ParentIndexNumber ?? 0;
                episodeNum = ep.IndexNumber ?? 0;
            }
            var seasonEpisode = (seasonNum > 0 && episodeNum > 0) ? $"S{seasonNum.ToString().PadLeft(2, '0')}E{episodeNum.ToString().PadLeft(2, '0')}" : "";

            var title = config.GetTitleTemplate("PlaybackProgress")
                .Replace("{progressPercent}", percent.ToString()).Replace("{itemName}", itemName)
                .Replace("{seriesName}", seriesName).Replace("{mediaName}", seriesName)
                .Replace("{seasonNumber}", seasonNum.ToString()).Replace("{episodeNumber}", episodeNum.ToString())
                .Replace("{seasonEpisode}", seasonEpisode).Replace("{itemType}", itemType)
                .Replace("{username}", usernames).Replace("{device}", device);

            var message = config.GetTemplate("PlaybackProgress")
                .Replace("{progressPercent}", percent.ToString()).Replace("{itemName}", itemName)
                .Replace("{seriesName}", seriesName).Replace("{mediaName}", seriesName)
                .Replace("{seasonNumber}", seasonNum.ToString()).Replace("{episodeNumber}", episodeNum.ToString())
                .Replace("{seasonEpisode}", seasonEpisode).Replace("{itemType}", itemType)
                .Replace("{username}", usernames).Replace("{device}", device);

            var imageUrl = config.IsCoverEnabled("PlaybackProgress") ? Notifier.GetCoverUrl(eventArgs.Item, config) : null;
            await Notifier.SendAsync(title, message, config, config.GetPriority("PlaybackProgress"), imageUrl);
        }
    }

    // ★★★ MediaLibraryNotifier：类型过滤 + 逐条投递 + 排队限速 ★★★
    //
    // 为什么**不再自己汇总**（原来是收集名字 → 5 分钟发一条"汇总"）：
    //
    //   Chatz 服务端本身就带一层聚合 —— messageCreate.js 的 findAggregateTarget() 会把
    //   「同频道 + 同应用 + 标题完全相同」且在 AGG_WINDOW_MS（默认 5 分钟）窗口内的消息
    //   折叠成一条可展开的卡片；而且**两端客户端都会渲染每个子项自己的封面**：
    //     · 网页    app.js: renderAggChildren  → 每个子项一个 <img class="agg-child-img">
    //     · Android MessageCard.kt: AggregateChildItem → MessageText.imageUrlFromExtras(child.extras)
    //
    //   所以逐条发出去效果反而**更好**：每一条都带自己的封面 / 路径 / 类型，
    //   界面上仍然是一条可展开的卡片（标题不带变量的默认模板就会折叠）。
    //   插件自己再汇总一遍，只会把封面、{path}、{mediaType} 这三样信息压扁成一条。
    //
    // ⚠️ 代价是必须自己控速：Chatz 的 webhook 入口按 token 分桶限流 **60 次/分钟**
    //    （服务端 hooks.js），而库扫描时 ItemAdded 是成批涌来的。不限速会有一大批被
    //    429 丢掉，而且 webhook 是 fire-and-forget —— 插件这边只会看到一句"发送失败"。
    public class MediaLibraryNotifier : IHostedService
    {
        private readonly ILibraryManager _libraryManager;

        // 事件回调只负责入队（要快，它跑在库扫描线程上），
        // 真正发 HTTP 的只有下面这一个串行 worker。
        private readonly ConcurrentQueue<MediaEvent> _queue = new();
        private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
        private CancellationTokenSource? _cts;
        private Task? _worker;

        // ≈40 次/分钟，压在 hook 的 60 次/分钟之下，给播放/登录那几类通知留余量
        private const int SendIntervalMs = 1500;

        // 上限：异常情况下（比如库一再重扫）别把内存撑爆
        private const int MaxQueueLength = 2000;

        /// <summary>
        /// 入队时就把要用的字段摘出来，**不持有 BaseItem 引用** ——
        /// 一次扫描可能上万条，长期持有 BaseItem 很占内存。
        /// </summary>
        private readonly record struct MediaEvent(Guid CoverId, string Name, string TypeName, string Path, bool Added);

        public MediaLibraryNotifier(ILibraryManager libraryManager)
        {
            _libraryManager = libraryManager;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _libraryManager.ItemAdded += OnItemAdded;
            _libraryManager.ItemRemoved += OnItemRemoved;

            _cts = new CancellationTokenSource();
            _worker = Task.Run(() => RunAsync(_cts.Token));

            Console.WriteLine("[Notify] ✅ 已订阅 ILibraryManager 媒体库事件（逐条投递，折叠交给 Chatz）");
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _libraryManager.ItemAdded -= OnItemAdded;
            _libraryManager.ItemRemoved -= OnItemRemoved;

            // 唤醒 worker 让它退出（Release 是为了让 WaitAsync 立刻返回）
            try { _cts?.Cancel(); _signal.Release(); } catch { }
            if (_worker != null)
            {
                try { await Task.WhenAny(_worker, Task.Delay(2000, cancellationToken)); } catch { }
            }
            _cts?.Dispose();
        }

        // ★ 类型过滤：只处理真正的影视媒体，忽略 Person/Genre/Studio/Year 等元数据实体
        private static bool IsMediaItem(BaseItem item)
        {
            return item is Movie || item is Series || item is Season || item is Episode;
        }

        /// <summary>给 {mediaType} 变量和自动标签用的中文类型名</summary>
        private static string MediaTypeName(BaseItem item) => item switch
        {
            Movie => "电影",
            Series => "剧集",
            Season => "季",
            Episode => "单集",
            _ => item.GetType().Name,
        };

        private void OnItemAdded(object? sender, ItemChangeEventArgs e) => Enqueue(e.Item, true);

        private void OnItemRemoved(object? sender, ItemChangeEventArgs e) => Enqueue(e.Item, false);

        /// <summary>
        /// 只做入队，**不做任何 IO** —— 这个回调跑在库扫描线程上，卡住它会拖慢整个扫描。
        /// </summary>
        private void Enqueue(BaseItem? item, bool added)
        {
            if (item == null || item.IsVirtualItem) return;
            if (!IsMediaItem(item)) return; // ★ 过滤掉元数据实体，不再轰炸

            var config = Plugin.Instance?.Configuration;
            var key = added ? "ItemAdded" : "ItemDeleted";
            if (config == null || !config.NotificationTypes.Contains(key)) return;

            if (_queue.Count >= MaxQueueLength)
            {
                Console.WriteLine($"[Notify] ⚠️ 媒体库通知队列已满（{MaxQueueLength}），丢弃: {item.Name}");
                return;
            }

            // Episode 用所属剧集的海报（和播放通知的取图规则保持一致）
            var coverId = (item is Episode ep && ep.SeriesId != Guid.Empty) ? ep.SeriesId : item.Id;

            _queue.Enqueue(new MediaEvent(
                coverId,
                item.Name ?? "未知媒体",
                MediaTypeName(item),
                item.Path ?? string.Empty,
                added));

            _signal.Release();
        }

        /// <summary>
        /// 串行 worker：一次取一条、发一条、歇一会儿再取 —— 最简单可靠地压住发送频率。
        /// （原来的实现是 Timer + 两个 ConcurrentBag，drain 之后到 finally 复位之间的
        ///   新条目会因为 _timerStarted 仍为 true 而丢失，这里顺带把那个竞态一并消掉了）
        /// </summary>
        private async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { await _signal.WaitAsync(ct); }
                catch (OperationCanceledException) { break; }

                if (!_queue.TryDequeue(out var ev)) continue;

                try { await DispatchAsync(ev); }
                catch (Exception ex) { Console.WriteLine($"[Notify] ❌ 媒体库通知异常: {ex.Message}"); }

                // 限速：见 SendIntervalMs 的说明
                try { await Task.Delay(SendIntervalMs, ct); }
                catch (OperationCanceledException) { break; }
            }
            Console.WriteLine("[Notify] 媒体库通知队列已停止");
        }

        private static async Task DispatchAsync(MediaEvent ev)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null) return;

            var key = ev.Added ? "ItemAdded" : "ItemDeleted";
            // 排队期间用户可能刚好关掉了这一类，这里再确认一次
            if (!config.NotificationTypes.Contains(key)) return;

            var title = config.GetTitleTemplate(key)
                .Replace("{mediaName}", ev.Name)
                .Replace("{mediaType}", ev.TypeName)
                .Replace("{path}", ev.Path);

            var message = config.GetTemplate(key)
                .Replace("{mediaName}", ev.Name)
                .Replace("{mediaType}", ev.TypeName)
                .Replace("{path}", ev.Path);

            // ⚠️ 删除的条目已经从库里移除了，/Items/{id}/Images/Primary 必然 404
            //    ⇒ 删除通知一律不带封面，免得正文里挂一个破图。
            var imageUrl = (ev.Added && config.IsCoverEnabled(key)) ? Notifier.GetCoverUrl(ev.CoverId, config) : null;

            // 自动带上语义标签，方便在 Chatz 里用路由规则做静默 / 分流 / 转发
            var tags = ev.Added
                ? new[] { "新增媒体", ev.TypeName }
                : new[] { "删除媒体", ev.TypeName };

            Console.WriteLine($"[Notify][Debug] 媒体库{(ev.Added ? "新增" : "删除")}投递: {ev.Name} [{ev.TypeName}]");

            await Notifier.SendAsync(title, message, config, config.GetPriority(key), imageUrl, tags);
        }
    }

    /// <summary>
    /// 统一推送出口：按配置把同一条通知分发到 Gotify / Chatz（可同时开启）。
    /// 两者共用标题、正文模板、优先级和封面 extras —— Chatz 的图片读取顺序是
    /// extras.image → client::display.url → client::notification.bigImageUrl，
    /// 正好覆盖 Gotify 的约定，所以 extras 可以原样复用。
    /// </summary>
    /// <summary>
    /// 字幕下载失败
    ///
    /// 这类失败在 Jellyfin 里是完全静默的：用户只会觉得「这片子没字幕」，
    /// 既不知道是下载挂了，也不清楚该去手动找字幕。把它推出来是这个通知的主要价值。
    /// 事件自带 Item（媒体） / Provider（字幕源） / Exception（失败原因）三样。
    /// </summary>
    public class SubtitleDownloadFailureConsumer : IEventConsumer<SubtitleDownloadFailureEventArgs>
    {
        public async Task OnEvent(SubtitleDownloadFailureEventArgs eventArgs)
        {
            if (eventArgs?.Item is null) return;
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.NotificationTypes.Contains("SubtitleFailure")) return;

            var itemName = eventArgs.Item.Name ?? "未知媒体";
            var itemType = eventArgs.Item.GetType().Name;
            var provider = string.IsNullOrWhiteSpace(eventArgs.Provider) ? "未知字幕源" : eventArgs.Provider;

            // 异常消息可能长得离谱（甚至带整段堆栈），截断后再塞进模板，别撑爆通知栏
            var reason = eventArgs.Exception?.Message;
            if (string.IsNullOrWhiteSpace(reason)) reason = "未提供原因";
            else if (reason.Length > 300) reason = reason.Substring(0, 300) + "…";

            int seasonNum = 0, episodeNum = 0;
            string seriesName = itemName;
            if (eventArgs.Item is Episode ep)
            {
                seriesName = ep.SeriesName ?? itemName;
                seasonNum = ep.ParentIndexNumber ?? 0;
                episodeNum = ep.IndexNumber ?? 0;
            }
            var seasonEpisode = (seasonNum > 0 && episodeNum > 0)
                ? $"S{seasonNum.ToString().PadLeft(2, '0')}E{episodeNum.ToString().PadLeft(2, '0')}"
                : "";

            var title = config.GetTitleTemplate("SubtitleFailure")
                .Replace("{itemName}", itemName).Replace("{seriesName}", seriesName).Replace("{mediaName}", seriesName)
                .Replace("{seasonNumber}", seasonNum.ToString()).Replace("{episodeNumber}", episodeNum.ToString())
                .Replace("{seasonEpisode}", seasonEpisode).Replace("{itemType}", itemType)
                .Replace("{provider}", provider).Replace("{reason}", reason);

            var message = config.GetTemplate("SubtitleFailure")
                .Replace("{itemName}", itemName).Replace("{seriesName}", seriesName).Replace("{mediaName}", seriesName)
                .Replace("{seasonNumber}", seasonNum.ToString()).Replace("{episodeNumber}", episodeNum.ToString())
                .Replace("{seasonEpisode}", seasonEpisode).Replace("{itemType}", itemType)
                .Replace("{provider}", provider).Replace("{reason}", reason);

            var imageUrl = config.IsCoverEnabled("SubtitleFailure") ? Notifier.GetCoverUrl(eventArgs.Item, config) : null;
            await Notifier.SendAsync(title, message, config, config.GetPriority("SubtitleFailure"), imageUrl);
        }
    }

    internal static class Notifier
    {
        // 复用同一个 HttpClient：Jellyfin 事件很密集，每次 new 会把连接池拖垮
        private static readonly HttpClient Http = new(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        })
        { Timeout = TimeSpan.FromSeconds(10) };

        /// <summary>从 BaseItem 取封面（Episode 用所属剧集的海报）</summary>
        internal static string? GetCoverUrl(BaseItem item, PluginConfiguration config)
        {
            if (item == null) return null;
            Guid coverId = item.Id;
            if (item is Episode ep && ep.SeriesId != Guid.Empty) coverId = ep.SeriesId;
            return GetCoverUrl(coverId, config);
        }

        /// <summary>
        /// 直接按 id 拼封面地址。
        ///
        /// 媒体库通知改成逐条投递后，队列里**不持有 BaseItem 引用**（一次扫描可能上万条，
        /// 留着引用很占内存），所以入队时就把封面 id 摘了出来，这里只负责拼串。
        /// </summary>
        internal static string? GetCoverUrl(Guid coverId, PluginConfiguration config)
        {
            if (coverId == Guid.Empty || string.IsNullOrEmpty(config.JellyfinServerUrl)) return null;
            return $"{config.JellyfinServerUrl.TrimEnd('/')}/Items/{coverId}/Images/Primary";
        }

        internal static async Task SendAsync(string title, string message, PluginConfiguration config, int priority, string? imageUrl = null, IEnumerable<string>? extraTags = null)
        {
            if (config == null) return;

            var tasks = new List<Task>();
            if (config.GotifyEnabled) tasks.Add(SendGotifyAsync(title, message, config, priority, imageUrl));
            if (config.ChatzEnabled) tasks.Add(SendChatzAsync(title, message, config, priority, imageUrl, extraTags));

            if (tasks.Count == 0)
            {
                Console.WriteLine("[Notify] ⚠️ Gotify 与 Chatz 均未启用，消息未发送: " + title);
                return;
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Gotify：POST {url}/message?token={appToken}
        /// </summary>
        private static async Task SendGotifyAsync(string title, string message, PluginConfiguration config, int priority, string? imageUrl)
        {
            if (string.IsNullOrWhiteSpace(config.GotifyUrl) || string.IsNullOrWhiteSpace(config.GotifyToken))
            {
                Console.WriteLine("[Notify] ⚠️ 已启用但未填写服务器地址或 Token，跳过");
                return;
            }

            try
            {
                var payload = BuildPayload(title, message, config, priority, imageUrl);
                var fullUrl = $"{config.GotifyUrl.TrimEnd('/')}/message?token={config.GotifyToken}";
                await PostJsonAsync("Gotify", fullUrl, payload);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Notify] ❌ 异常 ({title}): {ex.Message}");
            }
        }

        /// <summary>
        /// Chatz：POST {url}/hook/{应用Token}
        /// Token 是 Chatz 里「应用」的应用 Token（applications.token），不是全局 AUTH_TOKEN。
        /// </summary>
        private static async Task SendChatzAsync(string title, string message, PluginConfiguration config, int priority, string? imageUrl, IEnumerable<string>? extraTags = null)
        {
            if (string.IsNullOrWhiteSpace(config.ChatzUrl) || string.IsNullOrWhiteSpace(config.ChatzToken))
            {
                Console.WriteLine("[Chatz] ⚠️ 已启用但未填写服务器地址或应用 Token，跳过");
                return;
            }

            try
            {
                var payload = BuildPayload(title, message, config, priority, imageUrl);

                // channel_id 只有显式指定才带上，否则走应用自身的默认频道
                if (config.ChatzChannelId > 0) payload["channel_id"] = config.ChatzChannelId;

                // 全局标签（配置页填的那一串）+ 本次事件自动带的语义标签。
                // 自动标签是给 Chatz 的路由规则用的：那边能按 tag 匹配，做静默 / 改优先级 /
                // 分频道 / 转发到别处 —— 这些策略都该在服务端配，插件不重复造。
                var tags = ParseTags(config.ChatzTags);
                if (extraTags != null)
                {
                    foreach (var t in extraTags)
                    {
                        var v = (t ?? string.Empty).Trim();
                        if (v.Length > 0 && !tags.Contains(v)) tags.Add(v);
                    }
                }
                // Chatz 服务端对 tags 的限制：最多 20 个、单个不超过 50 字符（messageCreate.js）
                if (tags.Count > 20) tags = tags.Take(20).ToList();
                if (tags.Count > 0) payload["tags"] = tags;

                // Token 里可能含 URL 敏感字符，统一转义
                var fullUrl = $"{config.ChatzUrl.TrimEnd('/')}/hook/{Uri.EscapeDataString(config.ChatzToken.Trim())}";
                await PostJsonAsync("Chatz", fullUrl, payload);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Chatz] ❌ 异常 ({title}): {ex.Message}");
            }
        }

        private static Dictionary<string, object> BuildPayload(string title, string message, PluginConfiguration config, int priority, string? imageUrl)
        {
            string finalMessage = message;
            // 封面图片：extras 给客户端/App 用，markdown 那段给只看正文的 WebUI 用
            if (config.UseMarkdown && !string.IsNullOrEmpty(imageUrl))
                finalMessage = message + $"\n\n![封面]({imageUrl})";

            var payload = new Dictionary<string, object>
            {
                ["title"] = title,
                ["message"] = finalMessage,
                ["priority"] = priority,
            };

            var extras = BuildExtras(imageUrl);
            if (extras != null) payload["extras"] = extras;

            return payload;
        }

        private static Dictionary<string, object>? BuildExtras(string? imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl)) return null;

            return new Dictionary<string, object>
            {
                ["image"] = imageUrl!,
                ["client::display"] = new Dictionary<string, object> { ["contentType"] = "text/markdown" },
                ["client::notification"] = new Dictionary<string, object> { ["bigImageUrl"] = imageUrl! },
            };
        }

        private static List<string> ParseTags(string? raw)
        {
            var tags = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return tags;

            foreach (var part in raw.Split(',', '，'))
            {
                var t = part.Trim();
                if (t.Length > 0 && !tags.Contains(t)) tags.Add(t);
            }
            return tags;
        }

        private static async Task PostJsonAsync(string target, string url, Dictionary<string, object> payload)
        {
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                // 不把中文/emoji 转义成 \uXXXX：语义等价，但服务端日志和抓包能直接读懂
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await Http.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                var respBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[{target}] ❌ 发送失败: HTTP {response.StatusCode} | {respBody}");
            }
            else
            {
                Console.WriteLine($"[{target}] ✅ 发送成功");
            }
        }
    }
}
