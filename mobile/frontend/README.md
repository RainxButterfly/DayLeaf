# Android client

A minimal native WebView shell that hosts a single self-contained `index.html`
containing all of DayLeaf's UI and logic. It deliberately depends only on the Android
framework — no AndroidX, no AppCompat, no Kotlin — to keep the APK small and the
startup fast.

## Structure

```
frontend/
├── settings.gradle / build.gradle / gradle.properties
├── gradle/wrapper/gradle-wrapper.properties
└── app/
    ├── build.gradle
    ├── proguard-rules.pro
    └── src/main/
        ├── AndroidManifest.xml
        ├── java/com/dayleaf/app/
        │   ├── MainActivity.java          WebView host + silent update check on launch
        │   ├── WebAppInterface.java       JS bridge (notify / vibrate / check update / version)
        │   ├── UpdateManager.java         multi-source check + download + in-place install
        │   ├── UpdateInfo.java            update manifest model
        │   └── InstallApkProvider.java    minimal content:// provider for the installer
        ├── res/                            layout / theme / icon
        └── assets/www/index.html           the app itself
```

## Build

With Android Studio: **Open** this directory, let Gradle sync, then **Run**.

From the command line:

```bash
cd mobile/frontend
./gradlew assembleDebug      # debug APK — installs directly, for testing
./gradlew assembleRelease    # release APK — requires signing (below)
# output: app/build/outputs/apk/{debug,release}/
```

## Signing

In-place upgrades (no uninstall, data kept) require the same `applicationId` **and**
the same signature. Generate a dedicated key and back it up long-term:

```bash
keytool -genkey -v -keystore dayleaf-release.jks -alias dayleaf \
  -keyalg RSA -keysize 2048 -validity 10000
```

Provide the key to Gradle, either in `~/.gradle/gradle.properties`:

```properties
DAYLEAF_KEYSTORE=/absolute/path/dayleaf-release.jks
DAYLEAF_STORE_PASSWORD=your-store-password
DAYLEAF_KEY_ALIAS=dayleaf
DAYLEAF_KEY_PASSWORD=your-key-password
```

or on the command line:

```bash
./gradlew assembleRelease \
  -PDAYLEAF_KEYSTORE=... -PDAYLEAF_STORE_PASSWORD=... \
  -PDAYLEAF_KEY_ALIAS=... -PDAYLEAF_KEY_PASSWORD=...
```

## Why data survives an upgrade

App state lives in the WebView's `localStorage` (key `dayleaf.prototype.v2`), stored
inside the app's private data directory. An in-place install with the same package name
and signature leaves that directory untouched, so data is preserved. The shell enables
`setDomStorageEnabled(true)`; uninstalling first would delete it.

## Auto-update

- Checks silently at startup — no dialog unless a new version is actually found.
- Reads the manifest from multiple sources and takes the highest `versionCode`;
  unreachable sources are skipped.
- Updates are non-forced; there is also a manual "Check for updates" entry under
  Settings → About & updates.
- Downloads the APK, then hands it to the system installer (Android 8.0+ prompts to
  allow "install unknown apps").

## Author

- **StarLeaf·Roxy** (星河一叶Roxy) — <ck_xuv@vip.qq.com>
