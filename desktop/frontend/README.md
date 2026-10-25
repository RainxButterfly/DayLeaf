# Windows 客户端

叶历的 Windows 端。外壳是 .NET Framework 4.8 + WinForms + WebView2，界面**直接复用移动端那一份 `index.html`**，所以两端功能、数据格式、外观完全一致。

## 环境要求

| 项目 | 要求 |
|---|---|
| 运行 | Windows 10 / 11；WebView2 运行时（系统自带，个别精简版需[手动安装](https://developer.microsoft.com/microsoft-edge/webview2/)） |
| 构建 | .NET Framework 4.8（系统自带）+ 任意版本的 .NET SDK（只为跑 `dotnet build`） |

## 构建

```powershell
cd desktop/frontend
powershell -ExecutionPolicy Bypass -File build.ps1 -Version 1.0.0
```

脚本做四件事，按顺序：

1. 把 `mobile/frontend/app/src/main/assets/www/index.html` 同步到 `res\www\`
2. 首次构建时下载 WebView2 SDK 到 `lib\`（只下一次）
3. `dotnet build -c Release`，HTML 作为嵌入资源打进 exe
4. 输出到 `dist\DayLeaf-windows-<版本>\` 并打成 zip

只编译不打包：加 `-SkipPack`。

## 产物

```
dist\DayLeaf-windows-1.0.0\
  DayLeaf.exe                          主程序（含界面 HTML）
  WebView2Loader.dll                   WebView2 加载器
  Microsoft.Web.WebView2.Core.dll
  Microsoft.Web.WebView2.WinForms.dll
  DayLeaf.exe.config
```

整个目录约 2 MB，解压即用，不写注册表、不装服务。

## 界面

同一份 `index.html` 在宽度 960px 以上会切换成桌面布局（左侧导航栏 + 双列内容），窄了就回落到手机布局。窗口默认尺寸按系统缩放比例放大（例如 150% 缩放下开 1860×1245），保证默认就是桌面布局；最小尺寸同样按比例限制，缩不到手机布局那一档。

## 运行时数据

| 内容 | 位置 |
|---|---|
| 日程 / 记事 / 设置 | `%LOCALAPPDATA%\DayLeaf\webview\`（WebView2 的 localStorage 与 IndexedDB） |
| 界面文件（构建产物释放） | `%LOCALAPPDATA%\DayLeaf\www\index.html` |
| 主题偏好 | `%LOCALAPPDATA%\DayLeaf\theme.txt` |
| 窗口位置与大小 | `%LOCALAPPDATA%\DayLeaf\window.json` |

卸载 = 删掉 exe 所在目录 + 上面这个数据目录。

## 与网页的接口

外壳在页面脚本执行前注入 `window.DayLeafNative`，接口与安卓端保持一致，网页里无需判断平台：

| 方法 | 桌面端行为 |
|---|---|
| `checkUpdate()` | 读 `desktop/backend/update.json`，多源并发取最高版本，弹窗询问是否前往下载 |
| `saveTextFile(name, content)` | 弹系统「另存为」，以 UTF-8 无 BOM 写入 |
| `setThemeMode(mode)` | 记住 `light` / `dark` / `system`，同步标题栏与窗口底色 |
| `getVersionName()` | 返回程序版本 |
| `notify` / `vibrate` / `setHasModal` / `ensureNotifyPermission` | 空实现（桌面端不需要，保留是为了网页调用不报错） |

其余交互（拖拽、提醒弹窗、导入导出、搜索）都是网页自身逻辑，两端共用。

## 目录

```
desktop/frontend/
  src/            外壳源码
    Program.cs          入口、单实例
    MainForm.cs         窗口、WebView2、原生桥
    DesktopUpdate.cs    版本检测
    HtmlBundle.cs       释放内嵌界面
    AppPaths.cs         数据目录与主题解析
  res/
    app.ico             应用图标（与安卓端同一张图生成）
    app.manifest        高 DPI、权限声明
    www/index.html      构建时同步，不入库
  lib/                  WebView2 SDK，构建时下载，不入库
  build.ps1             构建脚本
```

## 已知限制

- 无系统通知与闹钟（网页内的提醒只在应用打开时可见）。
- 自动更新只提示并跳转下载，不静默替换正在运行的程序。
- 首次启动的窗口底色取自上次记住的主题，全新安装那一次会先按浅色起窗口。
- 开发自检：`DayLeaf.exe --selftest` 会在加载完界面后把状态写入 `%LOCALAPPDATA%\DayLeaf\selftest.log` 并退出，用于确认壳与网页链路正常。
