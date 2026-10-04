#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
CI 用：往 manifest.json 里追加一个版本条目。

为什么不在仓库里手写：
  checksum 是**打包后的 zip**的 MD5，本地手写必然对不上（Jellyfin 下载时会校验，
  对不上就装不了）。所以必须由 CI 在 zip 生成之后算出来填进去。

用法（在仓库根目录跑）：
  python .github/scripts/update_manifest.py <zip路径> <版本号> <下载URL> [更新日志]

  · 版本号：4 段，如 1.0.0.0（Jellyfin 要求）
  · targetAbi：自动从仓库里的 .csproj 中 Jellyfin.Controller 的版本推导，
    不用手填 —— 免得升了 Jellyfin 包却忘了改这里，导致用户装不上
"""
import hashlib
import glob
import io
import json
import re
import sys
import os
from datetime import datetime, timezone


def md5_of(path):
    h = hashlib.md5()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b''):
            h.update(chunk)
    return h.hexdigest()


def find_csproj():
    """自动找仓库根目录的 .csproj。

    ⚠️ 不要写死文件名 —— 插件从 HelloWorldPlugin 改名为 Chatz 时这里没跟着改，
    CI 直接 FileNotFoundError 挂掉（2026-10-04 踩过）。以后再改名也不会受影响。"""
    found = sorted(glob.glob('*.csproj'))
    if not found:
        raise SystemExit('当前目录没有找到 .csproj，无法推导 targetAbi')
    return found[0]


def target_abi_from_csproj(csproj=None):
    csproj = csproj or find_csproj()
    text = io.open(csproj, encoding='utf-8').read()
    m = re.search(r'Jellyfin\.Controller"\s+Version="(\d+)\.(\d+)\.(\d+)"', text)
    if not m:
        raise SystemExit('没从 csproj 里解析到 Jellyfin.Controller 版本，无法推导 targetAbi')
    return '%s.%s.%s.0' % (m.group(1), m.group(2), m.group(3))


def main():
    if len(sys.argv) < 4:
        raise SystemExit(__doc__)
    zip_path, version, source_url = sys.argv[1], sys.argv[2], sys.argv[3]
    changelog = sys.argv[4] if len(sys.argv) > 4 else '更新到 %s' % version

    manifest = json.load(io.open('manifest.json', encoding='utf-8'))
    entry = manifest[0]

    # 同一个版本重复跑就覆盖，别堆两条
    entry['versions'] = [v for v in entry.get('versions', []) if v.get('version') != version]
    entry['versions'].append({
        'version': version,
        'changelog': changelog,
        'targetAbi': target_abi_from_csproj(),
        'sourceUrl': source_url,
        'checksum': md5_of(zip_path),
        'timestamp': datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ'),
    })
    # 新版本放最前（Jellyfin 取第一个满足 targetAbi 的）
    entry['versions'].sort(key=lambda v: [int(x) for x in v['version'].split('.')], reverse=True)

    with io.open('manifest.json', 'w', encoding='utf-8', newline='\n') as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)
        f.write('\n')

    print('manifest 已更新: %s  checksum=%s  targetAbi=%s'
          % (version, md5_of(zip_path), target_abi_from_csproj()))


if __name__ == '__main__':
    main()
