# Release / update process (English)

> **Chinese**: [RELEASE.md](RELEASE.md)

Five steps to make a plugin change show up in users' Jellyfin.
**The order matters** — the manifest checksum is the MD5 of the zip, so it can only be computed
after the release exists.

### Quick copy-paste (replace 1.0.3 with the new version)

```bash
# ① commit the code (skip if nothing changed)
git add -A
git commit -m "description"
git pull --rebase origin main && git push origin main

# ② tag — triggers CI build + release
git tag v1.0.3
git push origin v1.0.3

# ③ ⏸ wait 1–3 minutes and confirm the release is published (do not skip this;
#    step ④ will 404 if you do — the zip does not exist yet, so there is no MD5 to compute)
curl -sIL -o /dev/null -w "%{http_code}\n" \
  https://github.com/yezi8430/jellyfin-plugin-chatz/releases/download/v1.0.3/Chatz-1.0.3.0.zip
# expect 200; on 404 wait a bit longer

# ④ update manifest.json
python tools/bump-manifest.py v1.0.3
git add manifest.json
git commit -m "chore: update manifest.json (1.0.3.0)"
git push
```

⑤ happens on the NAS: refresh the plugin repository → install → restart → confirm there is only one
dll. See section 5.

---

## 1. Change the code and self-check locally

```bash
# does the config page run (node vm + fake DOM; catches runtime errors like ReferenceError)
node .workbuddy/smoke-plugin-page.js

# do the variables listed on the config page match the {xxx} actually replaced in Consumers.cs?
python .workbuddy/check-plugin-vars.py
```

To build it and try it on the NAS, use `build.sh` (run it on the NAS: it compiles through the
docker SDK container and drops the result straight into the plugin directory):

```bash
JELLYFIN_PLUGIN_DIR=/docker/jellyfin/config/plugins/Chatz ./build.sh
```

## 2. Push to main

```bash
git add -A
git commit -m "..."
git pull --rebase origin main
git push origin main
```

> Pull before pushing. You are the only one editing the manifest right now so conflicts are
> unlikely, but if some CI write-back ever lands late, a rebase flattens it.

## 3. Tag it and let CI publish the release

```bash
git tag v1.0.3          # three-part version; CI expands it to 1.0.3.0 (Jellyfin needs four parts)
git push origin v1.0.3
```

CI then: compiles → packages `Chatz-<version>.zip` (`-j`, so the dll sits at the zip root) →
publishes the release.

Wait 1–3 minutes and confirm the artifact is downloadable (**do not skip this**):

```bash
curl -sIL -o /dev/null -w "%{http_code}\n" \
  https://github.com/yezi8430/jellyfin-plugin-chatz/releases/download/v1.0.3/Chatz-1.0.3.0.zip
# expect 200
```

## 4. Update manifest.json (run locally — CI does not do it for you)

```bash
python tools/bump-manifest.py v1.0.3
git add manifest.json
git commit -m "chore: update manifest.json (1.0.3.0)"
git push
```

The script: resolves the repo URL from the git remote → reads `AssemblyName` from the csproj to
build the zip name → downloads the release zip → computes the MD5 → verifies the package contains
exactly one `Chatz.dll` at the root → writes the manifest.

**Old versions are kept by default**, so Jellyfin shows a list of versions to choose from.
If you renamed the dll, the script warns (the old dll would coexist with the new one ⇒ duplicate
pushes); in that case add `--drop-old` to keep only the latest.

## 5. Verify + install on the NAS

```bash
curl -s https://raw.githubusercontent.com/yezi8430/jellyfin-plugin-chatz/main/manifest.json
```

Seeing the new version means the plugin repository is updated. Then in Jellyfin:
Dashboard → Plugins → Repositories → Refresh → find "Chatz 推送" → Install → **restart Jellyfin**.

After installing, confirm there is only one dll (two means duplicate pushes):

```bash
find /docker/jellyfin/config/plugins -name "*.dll"
# expect a single Chatz.dll line, e.g.:
#   /docker/jellyfin/config/plugins/Chatz 推送_1.0.3.0/Chatz.dll
# the Chinese in the directory name shows as ?????? on some terminals — that is the locale not
# displaying Chinese, not a broken filename
```

> ⚠️ **Re-check especially when upgrading**: installing a new version makes Jellyfin create another
> `Chatz 推送_<new-version>` directory and clean up the old one on restart. So **run the `find`
> again after restarting** — if two `Chatz.dll` really show up, `rm -rf` the whole old-version
> directory and restart.

---

## Traps (all of these were hit for real)

| Trap | Details |
|---|---|
| **CI does not write the manifest** | Letting CI commit back to main proved unreliable (on v1.0.2 the release went out but the manifest was never updated), and as soon as it touches main your next local push is rejected. Abandoned. |
| The checksum is the MD5 of the zip | Hand-writing it never matches; Jellyfin's download check fails. It can only be computed once you have the zip. |
| The version must have four parts | `1.0.3` → CI expands it to `1.0.3.0` |
| **Renaming the dll means renaming the config too** | Jellyfin's config file is named `<AssemblyName>.xml`. After renaming HelloWorldPlugin to Chatz you must `cp HelloWorldPlugin.xml Chatz.xml`, or the configuration is wiped. |
| **Never drop a dll into the plugins directory by hand** | A manually installed copy and a repository-installed copy coexist, both get loaded ⇒ every notification is sent twice. |
| The release must include manifest.json | When something breaks you can download it and see exactly what CI produced (that is how v1.0.2 was diagnosed) |

## Rollback

Download the older zip from
[Releases](https://github.com/yezi8430/jellyfin-plugin-chatz/releases), extract `Chatz.dll`,
overwrite it, and restart Jellyfin.
(If the manifest still lists old versions, you can also just pick a version inside Jellyfin.)

## Changing the cover image

The cover is not compiled into the dll — it is the **`imageUrl` field in manifest.json** (a raw URL
pointing at `images/logo.png`). Jellyfin's catalogue uses it as the card image, and the README
references the same file. To change it, replace `images/logo.png` and push; if it does not update,
it is the cache — hard refresh with Ctrl+F5.
(The compositing script lives at `.workbuddy/tmp/make_logo.py`: logo on the left, Chatz on the
right, with a gradient sampled from the logo.)

## Editing UI copy (Chinese / English)

All UI copy lives in the `I18N` dictionary inside `Configuration/configPage.html` (two sets, `zh`
and `en`). The page picks one based on **Jellyfin's UI language** and falls back to Chinese when it
cannot get English.

- Static copy: mark the HTML with `data-i18n` / `data-i18n-label` / `data-i18n-title` / `data-i18n-ph`
- Dynamic copy (notification rows, variable table): call `t("key")` directly
- English notification names are in `notificationDefs[].en`; English variable names are in
  `VAR_DICT[].en` / `.noteEn`

⚠️ Two formatting constraints (`.workbuddy/check-plugin-vars.py` verifies them with regexes and
reports inconsistencies if you break them):
1. In `notificationDefs`, each row's `label` must be immediately followed by `vars`, and `en` may
   only come after `vars`
2. Every `VAR_DICT` row must start with `{ cn:`

Always run these afterwards:

```bash
node .workbuddy/smoke-plugin-page.js        # Chinese mode
node .workbuddy/tmp/smoke-en.js             # English mode (simulates UICulture=en-US)
python .workbuddy/check-plugin-vars.py      # variables match Consumers.cs
```

The config page is an **EmbeddedResource**, so it must be recompiled into the dll before changes
take effect.
