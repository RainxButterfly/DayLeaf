<div align="center">

# DayLeaf · 叶历

**一款轻量、离线优先的安卓日程与记事应用。**

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Android-3DDC84.svg)](https://developer.android.com)
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
- **个性化** —— 个人背景、语录卡、时间微调、新手引导。
- **导入 / 导出** —— JSON 备份与恢复。
- **自动清理** —— 可选规则，清理 N 天前的过期日程。

## 下载

最新 APK 见 [Releases](https://github.com/RainxButterfly/DayLeaf/releases) 页面。需要 Android 8.0（API 26）及以上。

## 从源码构建

需要 JDK 17+ 与 Android SDK（compile SDK 34）。

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

## 自动更新

没有自建服务器。更新通过 GitHub Releases 分发：客户端请求一份静态 `update.json`（经多个 CDN 镜像），取最高版本号，原地覆盖安装——数据完整保留。启动时静默检测，只有发现新版本才提示。

详见 [`mobile/backend/README.md`](mobile/backend/README.md)。

## 目录结构

```
mobile/
  frontend/   安卓客户端（原生 WebView 壳 + 内置 index.html）
  backend/    update.json 版本清单 + 发布自动化
desktop/
  frontend/   Windows 客户端（规划中）
  backend/    Windows 更新清单（规划中）
```

目录用英文名，是因为安卓构建工具在 Windows 上不支持中文路径。「前端 / 后端」的拆分，是因为自动更新需要一个「服务端」；这里的服务端只是托管在 GitHub 上的静态 JSON 文件。

## 技术栈

- **客户端** —— 纯安卓框架（无 AndroidX、无第三方库），一个 WebView 承载单个自包含的 `index.html`（HTML / CSS / 原生 JS）。
- **数据** —— WebView 的 `localStorage`，位于应用私有数据目录。
- **更新** —— GitHub Releases + 静态 JSON 清单。

## 许可证

[MIT](LICENSE) © 2026 StarLeaf·Roxy（星河一叶Roxy）

## 作者

- **StarLeaf·Roxy**（星河一叶Roxy）
