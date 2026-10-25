<div align="center">

# DayLeaf · 叶历

**一款轻量、离线优先的日程与记事应用，安卓与 Windows 双端。**

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Android%20%7C%20Windows-3DDC84.svg)](#下载)
[![Release](https://img.shields.io/github/v/release/RainxButterfly/DayLeaf?color=4C6FFF)](https://github.com/RainxButterfly/DayLeaf/releases)

</div>

叶历（DayLeaf）是一款表格式日程与记事应用：把一周排成一张课程表——日期为列、时间为行，一周的安排一眼看全。所有数据都在本地，完全离线运行。

## 功能特性

- **表格视图** —— 日期为列、时间为行；拖拽日程改时间，拖到别的列改日期。
- **每日记事** —— 月历 + 每日标题正文，用颜色标记串联相关的日子。
- **提醒与闹钟** —— 提前弹系统通知；到点闹钟响铃并震动（手机端）。
- **重复日程** —— 按规则一次生成一批（比如「每周一」铺到期末）。
- **纪念日** —— 每年重复，跨年依然提醒。
- **粘贴识别** —— 粘贴通知，本地自动提取标题、时间、地点（不联网）。
- **全局搜索** —— 搜标题、地点、备注。
- **深色模式** —— 浅色 / 深色 / 跟随系统。
- **睡眠时段** —— 把表格折叠到清醒的时间段（默认关闭，即始终显示完整 24 小时）。
- **个性化** —— 个人背景、语录卡、时间微调、新手引导。
- **导入 / 导出** —— JSON 备份与恢复。
- **自动清理** —— 可选规则，清理 N 天前的过期日程。

## 下载

| 平台 | 要求 | 文件 |
|---|---|---|
| Android | 8.0（API 26）及以上 | `DayLeaf-android-*.apk` |
| Windows | 10 / 11（需 WebView2 运行时，系统自带） | `DayLeaf-Setup-*.exe` 安装包；也可用 `*-windows-*.zip` 免安装 |

都在 [Releases](https://github.com/RainxButterfly/DayLeaf/releases) 页面。两端数据结构一致，可互相导入导出的 JSON。

## 从源码构建

**安卓**：需要 JDK 17+ 与 Android SDK（compile SDK 34）。

```bash
cd mobile/frontend
./gradlew assembleDebug      # 调试包，本地测试用
./gradlew assembleRelease    # 签名发布包（见下文）
```

发布包需要签名密钥。生成一把并长期备份——升级必须用同一把签名：

```bash
keytool -genkey -v -keystore dayleaf-release.jks -alias dayleaf \
  -keyalg RSA -keysize 2048 -validity 10000
```

详见 [`mobile/frontend/README.md`](mobile/frontend/README.md)。

**Windows**：需要 .NET Framework 4.8（系统自带）、任意版本的 .NET SDK 与 [NSIS](https://nsis.sourceforge.io)。

```powershell
cd desktop/frontend
powershell -ExecutionPolicy Bypass -File build.ps1 -Version 1.0.0
```

脚本会自动同步移动端的 `index.html`、下载 WebView2 SDK、编译、打包，并调 NSIS 生成安装包到 `dist\`。
详见 [`desktop/frontend/README.md`](desktop/frontend/README.md)。

## 自动更新

没有自建服务器。更新通过 GitHub Releases 分发：客户端请求一份静态 `update.json`（经多个 CDN 镜像），取最高版本号。安卓端原地覆盖安装，数据完整保留；Windows 端提示新版本并跳转下载页。启动时静默检测，只有发现新版本才提示。

清单位置：安卓 [`mobile/backend/update.json`](mobile/backend/update.json)、Windows [`desktop/backend/update.json`](desktop/backend/update.json)。

## 发版

两端共用一个版本号，**一个 Release 挂两个附件**（因为界面是同一份，修复天然同时生效，没必要各走各的版本）。

1. 改版本号：`mobile/frontend/app/build.gradle` 的 `versionCode` +1、`versionName`；`index.html` 里的 `version`
2. 构建两端：

   ```bash
   cd mobile/frontend && ./gradlew assembleRelease
   cd desktop/frontend && powershell -ExecutionPolicy Bypass -File build.ps1 -Version <版本>
   ```

3. 填两份清单的 `versionName` / `url` / `size` / `sha256`
4. 提交，打 tag `v<版本>` 并推送
5. 在 Releases 建同名 Release，上传 `DayLeaf-android-v<版本>.apk` 和 `DayLeaf-Setup-<版本>.exe`
6. 刷一次 CDN 缓存：

   ```bash
   curl "https://purge.jsdelivr.net/gh/RainxButterfly/DayLeaf@latest/mobile/backend/update.json"
   curl "https://purge.jsdelivr.net/gh/RainxButterfly/DayLeaf@latest/desktop/backend/update.json"
   ```

> tag 推送后不要再移动。CDN 把 tag 当不可变内容，同一个地址会长期返回旧文件，purge 也救不回来——要改内容就发新版本号。

## 目录结构

```
mobile/
  frontend/   安卓客户端（原生 WebView 壳 + 内置 index.html）
  backend/    安卓更新清单 update.json
desktop/
  frontend/   Windows 客户端（WinForms + WebView2 壳）
  backend/    Windows 更新清单 update.json
```

界面只有一份：`mobile/frontend/app/src/main/assets/www/index.html`。两端外壳不同，加载的是同一个文件——Windows 端在构建时同步过去并内嵌进 exe，所以改一次界面两端都生效。

目录用英文名，是因为安卓构建工具在 Windows 上不支持中文路径。「前端 / 后端」的拆分，是因为自动更新需要一个「服务端」；这里的服务端只是托管在 GitHub 上的静态 JSON 文件。

## 技术栈

- **界面** —— 单个自包含的 `index.html`（HTML / CSS / 原生 JS），无框架、无构建步骤。
- **安卓外壳** —— 纯安卓框架（无 AndroidX、无第三方库），一个 WebView 承载界面。
- **Windows 外壳** —— .NET Framework 4.8 + WinForms + WebView2，约 700 KB，免安装。
- **数据** —— WebView 的 `localStorage` 与 `IndexedDB`，存在各自平台的本地目录。
- **更新** —— GitHub Releases + 静态 JSON 清单。

## 许可证

[MIT](LICENSE) © 2026 StarLeaf·Roxy（星河一叶Roxy）

## 作者

- **StarLeaf·Roxy**（星河一叶Roxy）
