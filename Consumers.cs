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

namespace Jellyfin.Plugin.Chatz
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
            var openUrl = Notifier.GetDetailsUrl(eventArgs.Item.Id, config);
            PlaybackProgressHandler.RecordPlaybackStart(eventArgs.Item.Id);
            await Notifier.SendAsync(title, message, config, config.GetPriority("PlaybackStart"), imageUrl, null, openUrl);
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
            var openUrl = Notifier.GetDetailsUrl(eventArgs.Item.Id, config);
            PlaybackProgressHandler.ClearPlaybackStart(eventArgs.Item.Id);
            await Notifier.SendAsync(title, message, config, config.GetPriority("PlaybackStop"), imageUrl, null, openUrl);
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
            var openUrl = Notifier.GetDetailsUrl(eventArgs.Item.Id, config);
            await Notifier.SendAsync(title, message, config, config.GetPriority("PlaybackProgress"), imageUrl, null, openUrl);
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
    //   界面上仍然是一条可展开的卡片（默认标题带 {workName}/{seriesName}，所以折叠
    //   是**按作品分组**的 —— 同一部剧一次新增 N 集折成一条，不同作品各自成条；
    //   这同时保证了折叠主卡的 extras.url 深链永远指向正确作品）。
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
        /// 入队时只摘「事件那一刻就确定有值」的字段（名字/类型/是否单集），**不摘父子关系**。
        ///
        /// 为什么 SeriesId / SeriesName / 封面 id 不在这里取：实测发现 ItemAdded 触发的那一刻，
        /// 单集的这两个字段**还没落库**（症状：标题落回单集名 ⇒ 同剧的集折不到一起；
        /// 封面退化成去要单集自己的主图 ⇒ 404 挂一张裂图）。所以这些留到派发时再查 ——
        /// 那时至少已经过了限速的 1.5 秒，通常已经写进去了（见 DispatchAsync）。
        ///
        /// ⚠️ IsEpisode 必须**入队时**摘下来：删除的条目在派发时已经不在库里，
        ///    回查拿不到对象，就只能靠这个快照位来判断"这一条是不是单集"
        ///    （决定了要不要用「所属剧」当作品名、能不能带封面）。
        ///
        /// 也不持有 BaseItem 引用：一次扫描可能上万条，长期持有很占内存。
        /// </summary>
        private readonly record struct MediaEvent(Guid ItemId, string Name, string TypeName, string Path, bool Added, bool IsEpisode);

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

        // ★ 类型过滤：只处理真正的「内容」，忽略两类东西
        //   1) Person / Genre / Studio / Year 等元数据实体（本来就不该通知）
        //   2) **Series / Season** —— 它们是扫描的**结构性副产品**：
        //      往媒体库放一季剧，Jellyfin 会逐个报「新增 剧集」「新增 季」「新增 单集×N」，
        //      对用户有意义的只有单集和电影。实测加两集动画会收到 3 条通知
        //      （剧集+季折成一条 ×2，再加上两集），前一条纯属噪音。
        //      ⇒ 只留 Movie / Episode。（想恢复剧集/季通知就加回这两个类型）
        private static bool IsMediaItem(BaseItem item)
        {
            return item is Movie || item is Episode;
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

            // 只摘即时字段，父子关系留给派发时查（原因见 MediaEvent 的注释）
            _queue.Enqueue(new MediaEvent(
                item.Id,
                item.Name ?? "未知媒体",
                MediaTypeName(item),
                item.Path ?? string.Empty,
                added,
                item is Episode));

            _signal.Release();
        }

        /// <summary>
        /// 派发时按 id 重取条目。取不到（比如删除的条目、或库正在重扫）返回 null。
        /// 绝不抛异常 —— 这里的目标是"能补就补"，补不到也照常回落快照。
        /// </summary>
        private BaseItem? TryGetItem(Guid id)
        {
            try { return _libraryManager.GetItemById(id); }
            catch { return null; }
        }

        /// <summary>
        /// 作品名的**路径兜底**：单集通知在拿不到 SeriesName 时用。
        ///
        /// 目录约定是 `剧名/Season XX/文件`，所以取「文件所在目录的上一级目录名」
        /// 就是剧名：/media/anime/进击的巨人/Season 1/xxx.mp4 → 进击的巨人。
        /// ⚠️ 只用于单集兜底，**不能给电影用**（电影的路径上一级是 /media/movies 这种库目录名）。
        /// </summary>
        private static string? WorkNameFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                var dir = System.IO.Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir)) return null;
                var parent = System.IO.Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(parent)) return null;
                var name = System.IO.Path.GetFileName(parent);
                return string.IsNullOrEmpty(name) ? null : name;
            }
            catch { return null; }
        }

        /// <summary>
        /// 按**路径**找回单集所属的剧 —— 单集封面必须用剧的海报，而剧的 id/名字
        /// 有时候两种来源都拿不到：扫描瞬间元数据还没落库（回查也还是空），
        /// 或者条目已经被删（对象都没了）。这时还有一条路：目录结构本身就在库里。
        ///
        /// 顺序：
        ///   1) Season 目录对应的条目 → 它的 SeriesId → 取剧（最准）
        ///   2) 直接按「剧名目录」的路径找（Season 目录可能还没建索引）
        /// 找不到返回 null（调用方会退到「按目录名当剧名、不带封面」）。
        /// </summary>
        private Series? FindSeriesByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                var seasonDir = System.IO.Path.GetDirectoryName(path);      // .../Season 1
                if (string.IsNullOrEmpty(seasonDir)) return null;
                var seriesDir = System.IO.Path.GetDirectoryName(seasonDir);  // .../进击的巨人
                if (string.IsNullOrEmpty(seriesDir)) return null;

                if (_libraryManager.FindByPath(seasonDir, true) is Season season && season.SeriesId != Guid.Empty)
                {
                    if (_libraryManager.GetItemById(season.SeriesId) is Series byId) return byId;
                }

                return _libraryManager.FindByPath(seriesDir, true) as Series;
            }
            catch { return null; }
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

        /// <summary>
        /// 派发一条媒体库通知。
        ///
        /// **为什么要"回查"**：入队是在 ItemAdded 回调里做的，而那一刻单集的
        /// SeriesId / SeriesName 可能还没落库（实测踩到两个症状）：
        ///   · 标题里的 {workName} 落回单集名 ⇒ 同一部剧的两集标题不同 ⇒ 折不到一起
        ///   · 封面退化成要单集自己的主图 ⇒ 404 ⇒ 正文里挂一张裂图
        /// 所以这里按 id 重取一次条目（此时至少过了 SendIntervalMs），补全父子关系；
        /// 拿不到就按下面的顺序兜底，**宁可少显示也不显示错的**。
        /// </summary>
        private async Task DispatchAsync(MediaEvent ev)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null) return;

            var key = ev.Added ? "ItemAdded" : "ItemDeleted";
            // 排队期间用户可能刚好关掉了这一类，这里再确认一次
            if (!config.NotificationTypes.Contains(key)) return;

            BaseItem? item = TryGetItem(ev.ItemId);

            // 新增的单集如果连所属剧都还没认出来，说明元数据还在落库 —— 等一下重取一次。
            // （只对"新增的单集且信息不全"生效，不影响其它条目、也不影响删除）
            if (ev.Added && item is Episode pending
                && (pending.SeriesId == Guid.Empty || string.IsNullOrEmpty(pending.SeriesName)))
            {
                try { await Task.Delay(2000, _cts?.Token ?? CancellationToken.None); } catch { }
                var again = TryGetItem(ev.ItemId);
                if (again != null) item = again;
            }

            var name = !string.IsNullOrEmpty(item?.Name) ? item!.Name! : ev.Name;
            var path = !string.IsNullOrEmpty(item?.Path) ? item!.Path! : ev.Path;
            var typeName = item != null ? MediaTypeName(item) : ev.TypeName;

            // 作品名 / 所属剧：单集归到所属剧（Chatz 的折叠按作品分组、封面都靠它），
            // 电影就是条目名。三级兜底，越靠前越准：
            //   1) 回查到的条目上的 SeriesName / SeriesId
            //   2) 按路径找回剧（目录结构就在库里，删掉的单集也能找到还在的剧）
            //   3) 按目录名当剧名（只有名字，没有 id ⇒ 不带封面）
            var isEpisode = item is Episode || (item == null && ev.IsEpisode);
            var seriesId = (item as Episode)?.SeriesId ?? Guid.Empty;
            var seriesName = (item as Episode)?.SeriesName ?? string.Empty;

            if (isEpisode && (seriesId == Guid.Empty || string.IsNullOrEmpty(seriesName)))
            {
                var series = FindSeriesByPath(path);
                if (series != null)
                {
                    if (seriesId == Guid.Empty) seriesId = series.Id;
                    if (string.IsNullOrEmpty(seriesName)) seriesName = series.Name ?? string.Empty;
                }
            }

            var workName = name;
            if (isEpisode)
            {
                if (string.IsNullOrEmpty(seriesName)) seriesName = WorkNameFromPath(path) ?? string.Empty;
                if (!string.IsNullOrEmpty(seriesName)) workName = seriesName;
            }

            var title = config.GetTitleTemplate(key)
                .Replace("{workName}", workName)
                .Replace("{mediaName}", name)
                .Replace("{mediaType}", typeName)
                .Replace("{path}", path);

            var message = config.GetTemplate(key)
                .Replace("{workName}", workName)
                .Replace("{mediaName}", name)
                .Replace("{mediaType}", typeName)
                .Replace("{path}", path);

            // 封面：单集一律用**所属剧**的海报（和播放通知的取图规则一致）；
            // 电影用自己的 id。
            // ⚠️ 单集拿不到 SeriesId 时**宁可不带封面** —— 退而求其次去要单集自己的
            //    主图会 404，正文里就是一张裂图（实测踩过）。
            // ⚠️ 删除的条目已经从库里移除了，/Items/{id}/Images/Primary 必然 404
            //    ⇒ 删除通知一律不带封面。
            Guid? coverId = null;
            if (ev.Added && config.IsCoverEnabled(key))
            {
                if (isEpisode) { if (seriesId != Guid.Empty) coverId = seriesId; }
                else coverId = ev.ItemId;
            }
            var imageUrl = coverId.HasValue ? Notifier.GetCoverUrl(coverId.Value, config) : null;

            // ⚠️ 深链同理：删除的条目详情页必然 404 ⇒ 删除通知不带 url。
            var openUrl = ev.Added ? Notifier.GetDetailsUrl(ev.ItemId, config) : null;

            // 自动带上语义标签，方便在 Chatz 里用路由规则做静默 / 分流 / 转发
            var tags = ev.Added
                ? new[] { "新增媒体", typeName }
                : new[] { "删除媒体", typeName };

            Console.WriteLine($"[Notify][Debug] 媒体库{(ev.Added ? "新增" : "删除")}投递: {name} [{typeName}]"
                + (isEpisode ? $" 剧={workName} 封面={(imageUrl != null ? "有" : "无")}" : ""));

            await Notifier.SendAsync(title, message, config, config.GetPriority(key), imageUrl, tags, openUrl);
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
            // 字幕失败深链到条目详情页：下一步动作很明确 —— 去那部片子的页面手动找/换字幕
            var openUrl = Notifier.GetDetailsUrl(eventArgs.Item.Id, config);
            await Notifier.SendAsync(title, message, config, config.GetPriority("SubtitleFailure"), imageUrl, null, openUrl);
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

        /// <summary>
        /// 拼条目详情页的深链地址，放进 Chatz 的 extras.url ——
        /// 网页端会渲染成「在 Jellyfin 中打开」按钮，点通知直达这部片子/这一集。
        ///
        /// · id 用无连字符的 N 格式（Jellyfin Web 路由的形态，实测可直达详情页）
        /// · 不需要 serverId：用户登录了哪个服务器，Web 客户端自己知道
        /// · JellyfinServerUrl 没填就返回 null —— 跟封面一样，深链是可选项
        /// </summary>
        internal static string? GetDetailsUrl(Guid itemId, PluginConfiguration config)
        {
            if (itemId == Guid.Empty || string.IsNullOrWhiteSpace(config.JellyfinServerUrl)) return null;
            return $"{config.JellyfinServerUrl.TrimEnd('/')}/web/#/details?id={itemId.ToString("N")}";
        }

        internal static async Task SendAsync(string title, string message, PluginConfiguration config, int priority, string? imageUrl = null, IEnumerable<string>? extraTags = null, string? openUrl = null)
        {
            if (config == null) return;

            var tasks = new List<Task>();
            if (config.GotifyEnabled) tasks.Add(SendGotifyAsync(title, message, config, priority, imageUrl));
            if (config.ChatzEnabled) tasks.Add(SendChatzAsync(title, message, config, priority, imageUrl, extraTags, openUrl));

            if (tasks.Count == 0)
            {
                Console.WriteLine("[Notify] ⚠️ Chatz 与 Gotify 均未启用，消息未发送: " + title);
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
        private static async Task SendChatzAsync(string title, string message, PluginConfiguration config, int priority, string? imageUrl, IEnumerable<string>? extraTags = null, string? openUrl = null)
        {
            if (string.IsNullOrWhiteSpace(config.ChatzUrl) || string.IsNullOrWhiteSpace(config.ChatzToken))
            {
                Console.WriteLine("[Chatz] ⚠️ 已启用但未填写服务器地址或应用 Token，跳过");
                return;
            }

            try
            {
                var payload = BuildPayload(title, message, config, priority, imageUrl, openUrl);

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

        private static Dictionary<string, object> BuildPayload(string title, string message, PluginConfiguration config, int priority, string? imageUrl, string? openUrl = null)
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

            var extras = BuildExtras(imageUrl, openUrl);
            if (extras != null) payload["extras"] = extras;

            return payload;
        }

        private static Dictionary<string, object>? BuildExtras(string? imageUrl, string? openUrl = null)
        {
            if (string.IsNullOrEmpty(imageUrl) && string.IsNullOrEmpty(openUrl)) return null;

            var extras = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(imageUrl))
            {
                extras["image"] = imageUrl!;
                extras["client::display"] = new Dictionary<string, object> { ["contentType"] = "text/markdown" };
                extras["client::notification"] = new Dictionary<string, object> { ["bigImageUrl"] = imageUrl! };
            }
            if (!string.IsNullOrEmpty(openUrl))
            {
                // 深链：网页端渲染成「在 Jellyfin 中打开」按钮。urlLabel 是按钮文字，
                // 发送方说了算 —— 服务端和客户端都不翻译它
                extras["url"] = openUrl!;
                extras["urlLabel"] = "在 Jellyfin 中打开";
            }
            return extras;
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
