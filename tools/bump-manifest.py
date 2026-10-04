#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
本地用：Release 发布之后，把新版本写进 manifest.json。

为什么要有这个脚本：
  manifest.json 必须提交到主分支，别人的 Jellyfin 才能从
  https://raw.githubusercontent.com/<owner>/<repo>/main/manifest.json 读到。
  但 checksum 是**打包后的 zip** 的 MD5，只能拿到 zip 之后才算得出来，
  手写必然对不上（Jellyfin 下载时会校验，对不上就装不了）。

  原来这一步放在 GitHub Actions 里「CI 改完 manifest 再 push 回 main」，有两个毛病：
    1) CI checkout 的是 tag（detached HEAD），push 回 main 要 fetch+rebase，实测不稳 ——
       v1.0.2 就出现过「Release 发了、zip 能下载，但 main 上的 manifest 还是旧版本」。
    2) CI 一改 main，你本地下次 push 就会被 non-fast-forward 拒绝。
  ⇒ 改成：CI 只管打包发 Release，manifest 由这个脚本在本地生成后你自己 commit + push。

用法（仓库根目录跑）：
  python tools/bump-manifest.py v1.0.2
  python tools/bump-manifest.py v1.0.2 -m "修复 xxx"   # 自定义更新日志
  python tools/bump-manifest.py v1.0.2 --drop-old      # 只留本次这个版本

默认**保留历史版本**，Jellyfin 里就能看到一列版本可以选/回退。
⚠️ 唯一要清旧版的情况是 dll 改过名（比如 HelloWorldPlugin.dll -> Chatz.dll）：
   旧版 zip 里的 dll 名和新版不同，两个 dll 会被同时加载 => 重复推送。
   脚本检测到旧版本的 zip 名前缀和当前 AssemblyName 不一致时会警告，
   那种情况下请手动删掉旧条目（或加 --drop-old）。

它会自动：
  · 从 tag 推导 4 段版本号（v1.0.2 -> 1.0.2.0，Jellyfin 要求 4 段）
  · 从 csproj 读 AssemblyName，拼出 Release 上的 zip 名
  · 从 git remote 解析 owner/repo，拼出下载 URL
  · 下载 zip、算 MD5、校验 zip 里只有根目录一个 <AssemblyName>.dll
  · 写进 manifest.json，然后你自己 commit + push
"""
import argparse
import glob
import importlib.util
import io
import json
import os
import re
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)


def load_updater():
    """复用 CI 那个脚本的写入逻辑，避免两处维护。"""
    path = os.path.join(ROOT, '.github', 'scripts', 'update_manifest.py')
    if not os.path.isfile(path):
        raise SystemExit('找不到 %s' % path)
    spec = importlib.util.spec_from_file_location('update_manifest', path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def asm_from_tag(tag):
    """v1.0.2 -> 1.0.2.0（和 workflow 里的推导规则保持一致）"""
    ver = tag[1:] if tag.startswith('v') else tag
    parts = ver.split('.')
    while len(parts) < 4:
        parts.append('0')
    return '.'.join(parts[:4])


def assembly_name():
    csproj = sorted(glob.glob(os.path.join(ROOT, '*.csproj')))
    if not csproj:
        raise SystemExit('仓库根目录没有 .csproj，无法推导 dll 名')
    text = io.open(csproj[0], encoding='utf-8').read()
    m = re.search(r'<AssemblyName>\s*(.*?)\s*</AssemblyName>', text)
    if m:
        return m.group(1)
    return os.path.splitext(os.path.basename(csproj[0]))[0]


def repo_slug():
    """从 git remote 解析 owner/repo，不用手填仓库名。"""
    try:
        out = subprocess.check_output(
            ['git', '-C', ROOT, 'remote', 'get-url', 'origin'],
            text=True, stderr=subprocess.STDOUT).strip()
    except Exception:
        raise SystemExit('读不到 git remote origin，无法确定仓库地址')
    m = re.search(r'github\.com[:/](.+?)(?:\.git)?$', out)
    if not m:
        raise SystemExit('无法从 remote 解析 owner/repo：%s' % out)
    return m.group(1)


def download(url, dest):
    try:
        with urllib.request.urlopen(url) as r, open(dest, 'wb') as f:
            f.write(r.read())
    except urllib.error.HTTPError as e:
        raise SystemExit('下载失败 %s（HTTP %s）\n'
                         '  Release 还没发布、zip 名对不上、或 tag 写错了都会是这个错。'
                         % (url, e.code))


def main():
    ap = argparse.ArgumentParser(description='把某个 Release 版本写进 manifest.json')
    ap.add_argument('tag', help='Release 的 tag，如 v1.0.2')
    ap.add_argument('-m', '--message', default=None, help='更新日志（默认“发布 <版本>”）')
    ap.add_argument('--drop-old', action='store_true',
                    help='只保留本次写入的版本（默认保留所有历史版本）')
    args = ap.parse_args()

    version = asm_from_tag(args.tag)
    dll = assembly_name() + '.dll'
    zip_name = '%s-%s.zip' % (assembly_name(), version)
    slug = repo_slug()
    url = 'https://github.com/%s/releases/download/%s/%s' % (slug, args.tag, zip_name)

    print('tag       : %s' % args.tag)
    print('版本      : %s' % version)
    print('zip       : %s' % zip_name)
    print('下载 URL  : %s' % url)

    tmp = os.path.join(tempfile.gettempdir(), zip_name)
    download(url, tmp)

    # 打包检查：Jellyfin 解压后要在根目录直接找到 dll，
    # 多一层目录或者 dll 名不对都会加载失败
    names = zipfile.ZipFile(tmp).namelist()
    if names != [dll]:
        raise SystemExit('zip 内容不对，期望只含根目录的 %s，实际是 %s' % (dll, names))
    print('zip 校验  : OK（只含 %s）' % dll)

    os.chdir(ROOT)  # update_manifest.py 用的是相对路径
    updater = load_updater()
    changelog = args.message or ('发布 %s' % version)
    sys.argv = ['update_manifest.py', tmp, version, url, changelog]
    updater.main()

    manifest = json.load(io.open('manifest.json', encoding='utf-8'))

    # dll 改名检测：旧版 zip 里的 dll 名和新版不同 ⇒ 两个 dll 会被同时加载
    prefix = assembly_name() + '-'
    renamed = []
    for v in manifest[0]['versions']:
        if v.get('version') == version:
            continue
        fn = os.path.basename(v.get('sourceUrl', '') or '')
        if not fn.startswith(prefix):
            renamed.append('%s（%s）' % (v.get('version'), fn or '?'))

    if args.drop_old:
        before = len(manifest[0]['versions'])
        manifest[0]['versions'] = [
            v for v in manifest[0]['versions'] if v.get('version') == version]
        with io.open('manifest.json', 'w', encoding='utf-8', newline='\n') as f:
            json.dump(manifest, f, ensure_ascii=False, indent=2)
            f.write('\n')
        print('已清理旧版本条目（%d 条 -> 1 条）' % before)
    else:
        print('manifest 现有版本: %s' % ', '.join(
            v.get('version') for v in manifest[0]['versions']))

    if renamed:
        print('')
        print('⚠️ 这些旧版本的 zip 名不是 %s-*：%s' % (assembly_name(), '、'.join(renamed)))
        print('   说明 dll 改过名，装它们会和 %s.dll 并存 => 重复推送。' % assembly_name())
        print('   建议手动删掉这些条目，或加 --drop-old 只留最新版。')

    print('\n下一步：')
    print('  git add manifest.json')
    print('  git commit -m "chore: 更新 manifest.json (%s)"' % version)
    print('  git push')


if __name__ == '__main__':
    main()
