# Chatz 推送（Jellyfin 插件）

把 Jellyfin 的事件推送到 **Chatz** 或 **Gotify**，两者可以同时启用，同一条通知会分别推到两边。

> 适用版本：**Jellyfin 12.x**（插件目标框架 net10.0，对应 Jellyfin.Controller 12.1.0）

## 特性

- **8 类通知**：登录成功 / 登录失败、开始播放 / 停止播放、播放进度、新增媒体 / 删除媒体、字幕下载失败
- **双推送目标**：Chatz 与 Gotify 独立开关，可同时开
- **Webhook 转发**：插件以 webhook 端点方式（`POST /hook/{应用Token}`）推给 Chatz；在 Chatz 服务端配路由规则（调用 Webhook 动作），还能把匹配的消息转发到任意 HTTP 端点（ntfy、钉钉、飞书、自建服务等，服务端带 SSRF 防护）
- **每类通知独立配置**：启用开关、优先级（0-10）、是否带封面
- **自定义模板**：标题和内容都能改，支持变量（见下方对照表），留空用默认文案
- **封面图**：把 Jellyfin 的封面地址直接塞进 `extras.image`，客户端/网页端都能显示
- **Markdown 模式**：开启后正文追加 `![封面](url)`，让只看正文的网页端也能出图
- **播放进度提醒**：达到设定百分比（默认 90%）推一次，带冷却时间；可选「推过进度就不再推停止」
- **媒体库通知限速**：新增媒体逐条投递（保住封面和路径），按 ~40 次/分钟排队发送，避免撞上 Chatz 的限流
- **Chatz 标签**：全局标签 + 自动语义标签（如「新增媒体」「电影」），配合服务端路由规则做静默/分流

## 安装

### 方式一：手动放 DLL（最简单）

1. 到 [Releases](https://github.com/yezi8430/jellyfin-plugin-chatz/releases) 下载最新版本的 zip
2. 解压出 `Chatz.dll`
3. 放到 Jellyfin 的插件目录，例如：
   ```
   <Jellyfin 配置目录>/plugins/chatz/Chatz.dll
   ```
4. 重启 Jellyfin

### 方式二：作为插件源安装（可自动检测更新）

1. Jellyfin 仪表盘 → **插件** → 仓库（Repositories）→ 添加
2. 填入本仓库的 manifest 地址：
   ```
   https://raw.githubusercontent.com/yezi8430/jellyfin-plugin-chatz/main/manifest.json
   ```
3. 在「目录（Catalog）」里找到 **Chatz 推送** 安装，以后有新版本会提示更新

## 配置

仪表盘 → 插件 → **Chatz 推送**。

### 推送目标

| 项 | 说明 |
|---|---|
| Gotify 服务器地址 | 例如 `https://mess.example.com`，**不要带路径** |
| Gotify 应用 Token | Gotify 里创建「应用」后得到的 Token |
| Chatz 服务器地址 | 例如 `http://192.168.1.10:20010`，**只填到域名/端口，不要带 `/hook`** |
| Chatz 应用 Token | Chatz WebUI 创建「应用」后的应用 Token —— **不是全局 AUTH_TOKEN** |
| Chatz 频道 ID | 可选，留空或 0 = 用该应用自己的默认频道 |
| Chatz 标签 | 可选，逗号分隔，供服务端路由规则匹配 |
| Jellyfin 外部访问地址 | 用于拼封面图地址，例如 `https://jellyfin.example.com` |

> Chatz 会把 5 分钟内「同应用 + 同频道 + 同标题」的消息折叠成一条。
> 想要每条通知都独立显示，就在标题模板里带上变量（如 `{itemName}`）。

### 模板变量

标题和内容都支持这些变量（配置页里点「变量对照表」按钮也能看到同一份）：

| 变量 | 含义 | 说明 |
|---|---|---|
| `{itemName}` | 媒体名称 | 剧集=单集标题；电影=电影名 |
| `{seriesName}` | 所属剧集 | 非剧集时等于「媒体名称」 |
| `{mediaName}` | 媒体名称 | 播放类里与「所属剧集」同值；新增/删除通知里就是条目名 |
| `{seasonNumber}` | 季号 | 纯数字，非剧集为 0 |
| `{episodeNumber}` | 集号 | 纯数字，非剧集为 0 |
| `{seasonEpisode}` | 季集编号 | 形如 `S01E02`；非剧集为空 |
| `{itemType}` | 媒体类型(英) | 英文类名：`Episode` / `Movie` / `Series` |
| `{mediaType}` | 媒体类型(中) | 中文：电影 / 剧集 / 季 / 单集 |
| `{username}` | 用户名 | 多人同时播放时以逗号连接 |
| `{device}` | 设备名 | 如「客厅电视」 |
| `{client}` | 客户端 | 如 Jellyfin Web / Infuse（**仅「开始播放」有**） |
| `{progressPercent}` | 播放进度 | 百分比数字，如 90（**仅「播放进度」有**） |
| `{completed}` | 是否播完 | 是 / 否（**仅「停止播放」有**） |
| `{path}` | 文件路径 | 服务器上的完整路径（媒体库通知） |
| `{provider}` | 字幕源 | 如 OpenSubtitles（**仅「字幕下载失败」**） |
| `{reason}` | 失败原因 | 超过 300 字会截断（**仅「字幕下载失败」**） |

> 注意：变量只在它所属的那类通知里会被替换，写在别的通知模板里会原样保留。

## 从源码构建

### 本地（Docker，无需装 .NET）

```bash
JELLYFIN_PLUGIN_DIR=/你的/插件目录 bash build.sh
```

- 部署目录用 `JELLYFIN_PLUGIN_DIR` 指定（脚本里的默认值只是占位）
- 不想让它自动重启就加 `SKIP_RESTART=1`
- 首次执行会拉一次 .NET SDK 镜像（走国内镜像代理），之后复用本地镜像

### 本地（dotnet）

```bash
dotnet build -c Release
# 产物：bin/Release/net10.0/Chatz.dll
```

### 发版

> 完整的分步流程（自检 → 提交 → 打 tag → 更新 manifest → NAS 安装，以及踩过的坑）
> 见 **[RELEASE.md](RELEASE.md)**。

推一个 tag，GitHub Actions 会自动编译、打包 zip、发布 Release：

```bash
git tag v1.0.0
git push origin v1.0.0
```

Release 出来之后，**还要在本地把版本写进 `manifest.json` 并 push**（CI 不代劳）：

```bash
python tools/bump-manifest.py v1.0.0
git add manifest.json
git commit -m "chore: 更新 manifest.json (1.0.0.0)"
git push
```

> `manifest.json` 里的 checksum 是 **打包后的 zip 的 MD5**，只能拿到 zip 之后才算得出，
> 手写一定对不上（Jellyfin 下载时会校验）。`bump-manifest.py` 会自己下载 Release 上的
> zip、算 MD5、校验包内确实只有根目录一个 `Chatz.dll`，然后写进 manifest。
>
> 为什么不顺便让 CI 提交回主分支：CI checkout 的是 tag（detached HEAD），
> push 回 main 要 fetch+rebase，实测不稳，而且它一改 main 你本地下次 push 就会被拒。

## 目录结构

```
.
├── Consumers.cs                     # 事件消费者：登录/播放/进度/媒体库/字幕失败
├── Plugin.cs                        # 插件入口，注册配置页
├── PluginConfiguration.cs           # 配置模型
├── PluginServiceRegistrator.cs       # DI 注册
├── Configuration/configPage.html    # 仪表盘里的配置页（HTML+CSS+JS 单文件）
├── manifest.json                    # 插件源清单（发版后由 bump-manifest.py 更新）
├── tools/bump-manifest.py           # 发版后生成本地 manifest 条目
├── RELEASE.md                       # 发版 / 更新流程 + 常见坑
└── .github/workflows/release.yml    # 打 tag 自动发版
```

## 许可证

**GPL-2.0-or-later**（见 [LICENSE](LICENSE)）。

插件引用了 Jellyfin 的 GPL 包，因此必须使用 GPL 许可证 ——
这一点与本项目里的服务端/客户端（MIT）不同，修改或分发本插件时请注意遵守。
