# 安卓客户端

一个极简的原生 WebView 壳，承载单个自包含的 `index.html`（DayLeaf 的全部 UI 与逻辑）。刻意只依赖安卓框架——不引入 AndroidX、AppCompat、Kotlin——把 APK 压到最小、启动最快。

## 目录结构

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
        │   ├── MainActivity.java          WebView 壳 + 启动静默检测更新
        │   ├── WebAppInterface.java       JS 桥（通知 / 震动 / 检查更新 / 版本号）
        │   ├── UpdateManager.java         多源检测 + 下载 + 原地覆盖安装
        │   ├── UpdateInfo.java            更新清单模型
        │   └── InstallApkProvider.java    极简 content:// 文件提供者
        ├── res/                           布局 / 主题 / 图标
        └── assets/www/index.html          应用本体
```

## 构建

Android Studio：直接 **Open** 本目录，等 Gradle 同步后点 **Run**。

命令行：

```bash
cd mobile/frontend
./gradlew assembleDebug      # 调试包，可直接安装测试
./gradlew assembleRelease    # 发布包，需先配签名（见下）
# 产物：app/build/outputs/apk/{debug,release}/
```

## 签名

原地升级（不卸载、数据保留）的前提是同一 `applicationId` + 同一签名。生成一把专用密钥并长期备份：

```bash
keytool -genkey -v -keystore dayleaf-release.jks -alias dayleaf \
  -keyalg RSA -keysize 2048 -validity 10000
```

把签名信息写进 `~/.gradle/gradle.properties`：

```properties
DAYLEAF_KEYSTORE=/绝对路径/dayleaf-release.jks
DAYLEAF_STORE_PASSWORD=你的store密码
DAYLEAF_KEY_ALIAS=dayleaf
DAYLEAF_KEY_PASSWORD=你的key密码
```

或在命令行临时传入：

```bash
./gradlew assembleRelease \
  -PDAYLEAF_KEYSTORE=... -PDAYLEAF_STORE_PASSWORD=... \
  -PDAYLEAF_KEY_ALIAS=... -PDAYLEAF_KEY_PASSWORD=...
```

## 数据为什么能保留

应用状态存在 WebView 的 `localStorage`（键 `dayleaf.prototype.v2`），位于应用的私有数据目录。同包名 + 同签名的原地覆盖安装不会清空该目录，数据得以保留。壳已开启 `setDomStorageEnabled(true)`；先卸载再装才会丢数据。

## 自动更新

- 启动后静默检测，不弹窗，只有发现新版本才提示。
- 多备用源取最高 `versionCode`，某个源不可用自动跳过。
- 非强制更新，可「以后再说」；设置 → 关于与更新 → 检查更新可手动触发。
- 下载 APK 后交给系统安装器覆盖安装（Android 8.0+ 会引导开启「安装未知应用」）。

## 作者

- **StarLeaf·Roxy**（星河一叶Roxy）
