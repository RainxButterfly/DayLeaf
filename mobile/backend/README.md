# Update manifest ("backend")

There is no self-hosted server. Auto-update runs entirely on GitHub, so the "backend"
is just a static `update.json` manifest hosted in the repository and served through a
few CDN/mirror endpoints.

## `update.json` fields

| Field | Meaning |
| --- | --- |
| `channel` | `android` for now; the Windows client will use `windows` with its own manifest |
| `versionCode` | Integer, only ever increases. The client compares this. |
| `versionName` | Human-readable version, e.g. `1.0.0` |
| `minVersionCode` | Lowest version that can upgrade in place (not enforced for now) |
| `force` | `false` = non-forced (user can dismiss) |
| `url` | APK download URL (a GitHub Release asset) |
| `size` | APK size in bytes, for display ("about X MB"); may be 0 |
| `sha256` | APK SHA-256 for download verification; empty skips the check |
| `notes` | Changelog shown in the dialog |
| `publishedAt` | Release date, informational |

## How the client checks

Implemented in `frontend/.../UpdateManager.java`:

1. Requests each source in `UPDATE_SOURCES` in turn; a source that cannot be reached
   is skipped.
2. Parses `versionCode` from each response.
3. Takes the **highest** `versionCode` across all reachable sources as the latest
   version.
4. Compares it against the installed `versionCode`; if higher, shows a non-forced
   "new version" prompt.

The multiple sources exist because the primary one (GitHub raw) can be flaky in some
regions; jsDelivr and a ghproxy mirror act as fallbacks. Taking the highest version
also tolerates a CDN that has not refreshed yet.

## Releasing a version

1. In `frontend/app/build.gradle`, bump `versionCode` and set the new `versionName`.
2. Build the APK with the **same** signing key:
   `./gradlew assembleRelease` (see `frontend/README.md`).
3. Compute the size and SHA-256 of the APK:
   ```bash
   ls -l DayLeaf-android-v1.0.0.apk
   shasum -a 256 DayLeaf-android-v1.0.0.apk      # macOS / Linux
   Get-FileHash DayLeaf-android-v1.0.0.apk -Algorithm SHA256   # Windows PowerShell
   ```
4. Update `update.json`: `versionCode`, `versionName`, `url`, `size`, `sha256`, `notes`.
5. Create a tag (e.g. `v1.0.0`) and a GitHub Release, attach the APK, and point `url`
   at it.
6. Push. Mirrors refresh automatically; clients pick it up on the next check.

> In-place upgrade (data preserved) requires the same package name **and** signature.
> If the signature changes, the system treats it as a different app and the user has to
> uninstall first — losing data. Back the key up.

## Release automation (optional)

`.github/workflows/android-release.yml` can automate "tag → build → release"; a template
lives in this directory (`android-release.yml`) and needs signing secrets configured.
