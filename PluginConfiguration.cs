using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Chatz
{
    public class NotificationTemplate
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }

    public class NotificationPriority
    {
        public string Key { get; set; } = "";
        public int Value { get; set; } = 5;
    }

    public class PluginConfiguration : BasePluginConfiguration
    {
        // ---------- Gotify 目标 ----------
        public bool GotifyEnabled { get; set; } = true;
        public string GotifyUrl { get; set; } = "";
        public string GotifyToken { get; set; } = "";

        // ---------- Chatz 目标 ----------
        // Chatz 走 webhook 入口：POST {ChatzUrl}/hook/{ChatzToken}
        // 这里的 Token 是 Chatz「应用」的应用 Token（applications.token），不是全局 AUTH_TOKEN
        public bool ChatzEnabled { get; set; } = false;
        public string ChatzUrl { get; set; } = "";
        public string ChatzToken { get; set; } = "";

        // 可选：指定频道 ID；留空/0 时使用应用自身的默认频道
        public int ChatzChannelId { get; set; } = 0;

        // 可选：给 Chatz 消息打标签（逗号分隔），便于路由规则匹配
        public string ChatzTags { get; set; } = "";

        public List<string> NotificationTypes { get; set; } = new();
        public List<NotificationTemplate> NotificationTemplates { get; set; } = new();
        public List<NotificationTemplate> NotificationTitleTemplates { get; set; } = new();
        public List<NotificationPriority> NotificationPriorities { get; set; } = new();
        // ★ 「封面」勾选框存的是**被关掉的** key（黑名单），不是「启用的」（白名单）。
        //
        //   为什么反过来存：这个开关以前压根没被 C# 代码读过，所以老配置里那个
        //   "启用列表"是空的。若按白名单语义，空 = 全关 ⇒ 所有老用户一升级就突然丢封面。
        //   黑名单则天然兼容：空 = 谁都没关 = 全部启用（正是升级前的实际行为），
        //   于是**不需要任何一次性迁移**。
        public List<string> NotificationCoverDisabled { get; set; } = new();
        public string JellyfinServerUrl { get; set; } = "";
        public bool PlaybackProgressEnabled { get; set; } = false;
        public int PlaybackProgressPercent { get; set; } = 90;

        // ★ 冷却时间（分钟），默认 10 分钟
        public int PlaybackProgressCooldownMinutes { get; set; } = 10;

        // ★ 已经推送过「播放进度」通知的这一集，就不再推送「停止播放」。
        //   追剧连播时每集都会先到阈值（进度通知）再播完（停止通知），后者基本是噪音。
        public bool SkipStopAfterProgress { get; set; } = true;

        public bool UseMarkdown { get; set; } = true;

        public string GetTemplate(string key)
        {
            var item = NotificationTemplates?.Find(t => t.Key == key);
            if (item != null && !string.IsNullOrWhiteSpace(item.Value))
                return item.Value;
            return DefaultTemplates.TryGetValue(key, out var def) ? def : $"通知: {key}";
        }

        public string GetTitleTemplate(string key)
        {
            var item = NotificationTitleTemplates?.Find(t => t.Key == key);
            if (item != null && !string.IsNullOrWhiteSpace(item.Value))
                return item.Value;
            return DefaultTitles.TryGetValue(key, out var def) ? def : $"[Jellyfin] {key}";
        }

        public int GetPriority(string key)
        {
            var item = NotificationPriorities?.Find(p => p.Key == key);
            if (item != null) return item.Value;
            return 5;
        }

        /// <summary>
        /// 这个事件的封面要不要带。
        ///
        /// 注意语义方向：名单里存的是**被关掉的**事件（见 NotificationCoverDisabled 的说明），
        /// 所以"不在名单里" = 启用。
        /// </summary>
        public bool IsCoverEnabled(string key)
        {
            return !(NotificationCoverDisabled?.Contains(key) ?? false);
        }

        public static Dictionary<string, string> DefaultTemplates => new()
        {
            ["AuthenticationSuccess"] = "✅ 用户 {username} 已登录",
            ["AuthenticationFailure"] = "❌ 用户 {username} 登录失败",
            ["PlaybackStart"] = "▶️ 开始播放\n📺 {itemName}\n📁 {itemType}\n👤 {username}\n📱 {device}",
            ["PlaybackStop"] = "⏹️ 停止播放\n📺 {itemName}\n📁 {itemType}\n👤 {username}\n📱 {device}\n✅ 播完：{completed}",
            ["PlaybackProgress"] = "📊 播放进度已达 {progressPercent}%\n📺 {itemName}\n📁 {itemType}\n👤 {username}\n📱 {device}",
            ["ItemAdded"] = "🆕 新增媒体\n📺 {mediaName}\n📁 {mediaType}\n📂 {path}",
            ["ItemDeleted"] = "🗑️ 删除媒体\n📺 {mediaName}\n📁 {mediaType}\n📂 {path}",
            ["SubtitleFailure"] = "⚠️ 字幕下载失败\n📺 {itemName}\n🌐 字幕源：{provider}\n❌ {reason}"
        };

        public static Dictionary<string, string> DefaultTitles => new()
        {
            ["AuthenticationSuccess"] = "[Jellyfin] 登录成功",
            ["AuthenticationFailure"] = "[Jellyfin] 登录失败",
            ["PlaybackStart"] = "[Jellyfin] 开始播放",
            ["PlaybackStop"] = "[Jellyfin] 停止播放",
            ["PlaybackProgress"] = "[Jellyfin] 播放进度提醒",
            ["ItemAdded"] = "[Jellyfin] 新增媒体",
            ["ItemDeleted"] = "[Jellyfin] 删除媒体",
            ["SubtitleFailure"] = "[Jellyfin] 字幕下载失败"
        };
    }
}
