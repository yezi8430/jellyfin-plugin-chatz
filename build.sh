#!/usr/bin/env bash
#
# 编译 Jellyfin 插件并部署（在 NAS 上跑：bash build.sh）
#
# 换源：mcr.microsoft.com 在国外，直连很慢。这里用 DaoCloud 公共代理拉一次，
# 再打成本地 tag，之后构建就走本地镜像、不再联网。
# （daemon.json 里的 registry-mirrors 只对 Docker Hub 生效，不代理 mcr，别指望它）
#
# set -e 很关键：构建失败就立刻停住，别拿着上一次的旧 DLL 去 cp + 重启 Jellyfin
# —— 那样会以为"更新成功了"，其实跑的还是老代码。
set -e

cd "$(dirname "$0")"

BASE_IMAGE="m.daocloud.io/mcr.microsoft.com/dotnet/sdk:10.0"
LOCAL_IMAGE="dotnet-sdk:10.0"

# ★ 部署目标不要写死在仓库里 —— 用环境变量覆盖，默认给一条通用路径。
#   用法：JELLYFIN_PLUGIN_DIR=/volume1/docker/jellyfin/config/plugins/helloworld bash build.sh
#   （仓库里的默认值只是占位，第一次跑前务必先设成你自己的目录）
JELLYFIN_PLUGIN_DIR="${JELLYFIN_PLUGIN_DIR:-/config/plugins/helloworld}"
# 重启的容器名同理；不想让它自动重启就 SKIP_RESTART=1
JELLYFIN_CONTAINER="${JELLYFIN_CONTAINER:-jellyfin}"

# 本地没有才拉（只走一次国内代理）
docker image inspect "$LOCAL_IMAGE" >/dev/null 2>&1 || {
  docker pull "$BASE_IMAGE" && docker tag "$BASE_IMAGE" "$LOCAL_IMAGE"
}

rm -rf bin obj

docker run --rm \
  -v "$(pwd)":/src \
  -v ~/.nuget/packages:/root/.nuget/packages \
  -w /src \
  "$LOCAL_IMAGE" \
  dotnet build -c Release --tl:off

mkdir -p "$JELLYFIN_PLUGIN_DIR"
cp bin/Release/net10.0/HelloWorldPlugin.dll "$JELLYFIN_PLUGIN_DIR/HelloWorldPlugin.dll"

if [ "${SKIP_RESTART:-0}" = "1" ]; then
    echo "== DLL 已替换；按 SKIP_RESTART=1 跳过重启，请手动重启 Jellyfin =="
else
    docker restart "$JELLYFIN_CONTAINER"
    echo "== 完成：DLL 已替换，Jellyfin 已重启 =="
fi
