# 发版 / 更新流程

每次改完插件要让它出现在用户的 Jellyfin 里，一共 5 步。
**顺序不能乱** —— manifest 的 checksum 是 zip 的 MD5，必须等 Release 出来之后才能算。

### 速查（复制即用，把 1.0.3 换成新版本号）

```bash
# ① 提交代码（没改代码就跳过）
git add -A
git commit -m "说明"
git pull --rebase origin main && git push origin main

# ② 打 tag，触发 CI 编译 + 发 Release
git tag v1.0.3
git push origin v1.0.3

# ③ ⏸ 等 1–3 分钟，确认 Release 已发布（这步不能跳，
#    跳过去第 ④ 步会 404 —— 因为 zip 还没生成，MD5 也就无从算起）
curl -sIL -o /dev/null -w "%{http_code}\n" \
  https://github.com/yezi8430/jellyfin-plugin-chatz/releases/download/v1.0.3/Chatz-1.0.3.0.zip
# 期望 200；404 就再等等

# ④ 更新 manifest.json
python tools/bump-manifest.py v1.0.3
git add manifest.json
git commit -m "chore: 更新 manifest.json (1.0.3.0)"
git push
```

⑤ 是 NAS 那边：Jellyfin 刷新插件源 → 安装 → 重启 → 确认只有一个 dll。见下面第 5 节。


---

## 1. 改代码，本地自检

```bash
# 配置页能不能正常跑起来（node vm + 假 DOM，能抓出 ReferenceError 这类运行时错）
node .workbuddy/smoke-plugin-page.js

# 配置页里列的变量 和 Consumers.cs 里实际替换的 {xxx} 是否一致
python .workbuddy/check-plugin-vars.py
```

想编出来装到 NAS 上试，用 `build.sh`（在 NAS 上跑，会走 docker SDK 容器编译并直接放进插件目录）：

```bash
JELLYFIN_PLUGIN_DIR=/docker/jellyfin/config/plugins/Chatz ./build.sh
```

## 2. 提交到 main

```bash
git add -A
git commit -m "..."
git pull --rebase origin main
git push origin main
```

> 先 pull 再 push。仓库现在只有你一个人写 manifest，正常不会冲突，
> 但万一某次 CI 的回写延迟落地了，rebase 一下就平了。

## 3. 打 tag，让 CI 发 Release

```bash
git tag v1.0.3          # 版本号三段，CI 会补成 1.0.3.0（Jellyfin 要求 4 段）
git push origin v1.0.3
```

CI 会做：编译 → 打包 `Chatz-<版本>.zip`（`-j`，zip 里直接是 dll）→ 发布 Release。

等 1–3 分钟，确认产物能下载（**这步别跳过**）：

```bash
curl -sIL -o /dev/null -w "%{http_code}\n" \
  https://github.com/yezi8430/jellyfin-plugin-chatz/releases/download/v1.0.3/Chatz-1.0.3.0.zip
# 期望 200
```

## 4. 更新 manifest.json（本地跑，CI 不代劳）

```bash
python tools/bump-manifest.py v1.0.3
git add manifest.json
git commit -m "chore: 更新 manifest.json (1.0.3.0)"
git push
```

脚本会自己：从 git remote 解析仓库地址 → 从 csproj 读 AssemblyName 拼 zip 名 →
下载 Release 的 zip → 算 MD5 → 校验包内只有根目录一个 `Chatz.dll` → 写进 manifest。

**默认保留历史版本**，Jellyfin 里就能看到一列版本可选。
如果这次改了 dll 名，脚本会告警（旧版 dll 会和新版并存 ⇒ 重复推送），
那种情况加 `--drop-old` 只留最新版。

## 5. 验证 + NAS 安装

```bash
curl -s https://raw.githubusercontent.com/yezi8430/jellyfin-plugin-chatz/main/manifest.json
```

看到新版本号就说明插件源更新了。然后在 Jellyfin：
控制台 → 插件 → 存储库 → 刷新 → 找到「Chatz 推送」→ 安装 → **重启 Jellyfin**。

装完确认只有一个 dll（有两个就会重复推送）：

```bash
find /docker/jellyfin/config/plugins -name "*.dll"
```

---

## 坑（都是踩过的）

| 坑 | 说明 |
|---|---|
| **CI 不写 manifest** | 原来让 CI 提交回主分支，实测不稳（v1.0.2 就出现过 Release 发了、manifest 没更新），而且它一改 main 你本地下次 push 就被拒。已废弃。 |
| checksum 是 zip 的 MD5 | 手写一定对不上，Jellyfin 下载时校验失败。只能拿到 zip 之后算。 |
| 版本号必须 4 段 | `1.0.3` → CI 补成 `1.0.3.0` |
| **dll 改名 ⇒ 配置要跟着改名** | Jellyfin 的配置文件名 = `<AssemblyName>.xml`。从 HelloWorldPlugin 改成 Chatz 后要 `cp HelloWorldPlugin.xml Chatz.xml`，否则配置被重置。 |
| **别手动往 plugins 目录丢 dll** | 手动装的 + 从仓库装的会同在，两个 dll 都被加载 ⇒ 每条通知发两遍。 |
| Release 里必须带 manifest.json | 出问题时可以下载它来对照 CI 到底生成了什么（v1.0.2 就是靠这个定位的） |

## 回滚

到 [Releases](https://github.com/yezi8430/jellyfin-plugin-chatz/releases) 下旧版本的 zip，
解压出 `Chatz.dll` 覆盖回去，重启 Jellyfin。
（manifest 里保留着历史版本的话，也可以直接在 Jellyfin 里选版本装。）
