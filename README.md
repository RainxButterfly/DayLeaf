<div align="center">

# DayLeaf · 叶历

**A lightweight, offline-first schedule and journal app for Android.**

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Android-3DDC84.svg)](https://developer.android.com)
[![Release](https://img.shields.io/badge/release-v1.0.0-4C6FFF.svg)](https://github.com/RainxButterfly/DayLeaf/releases)

</div>

DayLeaf (叶历) is a table-style schedule and journal app. It lays the week out as a
timetable — days as columns, hours as rows — so a whole week fits on one screen.
Everything runs offline; your data never leaves the device.

## Features

- **Timetable view** — days as columns, hours as rows. Drag an event to reschedule it, or drag it across a column to move it to another day.
- **Daily journal** — a month calendar with a note per day, plus color tags to link related days.
- **Reminders & alarms** — a system notification ahead of an event; an on-device alarm that rings and vibrates at the moment.
- **Repeating events** — generate a batch from a rule (for example, every Monday until the end of term).
- **Anniversaries** — yearly reminders that roll over across years.
- **Paste-to-schedule** — paste a notice and the title, time and place are extracted locally (no network).
- **Global search** — search across title, place and notes.
- **Dark mode** — light, dark, or follow the system.
- **Customization** — personal background, quote card, time-step, first-run guide.
- **Import / export** — JSON backup and restore.
- **Auto-clean** — optional rule to clear events older than N days.

## Download

The latest APK is on the [Releases](https://github.com/RainxButterfly/DayLeaf/releases) page.
Requires Android 8.0 (API 26) or later.

## Building from source

You need JDK 17+ and the Android SDK (compile SDK 34).

```bash
cd mobile/frontend
./gradlew assembleDebug      # debug APK, for local testing
./gradlew assembleRelease    # signed release APK (see below)
```

Release builds need a signing key. Generate one and keep it safe — upgrades require
the same signature:

```bash
keytool -genkey -v -keystore dayleaf-release.jks -alias dayleaf \
  -keyalg RSA -keysize 2048 -validity 10000
```

See [`mobile/frontend/README.md`](mobile/frontend/README.md) for the full build and
signing instructions.

## Auto-update

There is no self-hosted server. Updates are distributed through GitHub Releases: the
client fetches a static `update.json` mirrored across a few CDN endpoints, picks the
highest version, and installs in place — existing data is preserved. The check runs
silently at startup and only prompts when a new version is found.

See [`mobile/backend/README.md`](mobile/backend/README.md) for the manifest format and
release workflow.

## Project structure

```
mobile/
  frontend/   Android client — native WebView shell + built-in index.html
  backend/    update.json manifest + release automation
desktop/
  frontend/   Windows client (planned)
  backend/    Windows update manifest (planned)
```

Directories use ASCII names because the Android build tools reject non-ASCII paths on
Windows. The `frontend` / `backend` split exists because auto-update needs a
"server side"; here that side is just a static JSON file hosted on GitHub.

## Tech stack

- **Client** — plain Android framework (no AndroidX, no third-party libraries). A
  WebView hosts a single self-contained `index.html` (HTML / CSS / vanilla JS).
- **Data** — WebView `localStorage` inside the app's private storage directory.
- **Updates** — GitHub Releases + a static JSON manifest.

## License

[MIT](LICENSE) © 2026 StarLeaf·Roxy (星河一叶Roxy)

## Author

- **StarLeaf·Roxy** (星河一叶Roxy) — <ck_xuv@vip.qq.com>
