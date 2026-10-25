# Windows 更新清单

`update.json` 是 Windows 客户端的版本清单：启动后会在后台静默读一次，设置里的「检查更新」也会读它。托管在 GitHub 上，经 CDN 多源分发，没有自建服务器。

## 字段

| 字段 | 说明 |
|---|---|
| `channel` | 固定 `windows`，与安卓端清单区分 |
| `versionCode` | 整数版本号，备用 |
| `versionName` | 版本字符串，客户端按点分数字比对 |
| `minVersionCode` | 最低可用版本，低于此值需强制更新 |
| `url` | 新版**安装包**的下载地址（`DayLeaf-Setup-*.exe`） |
| `releasePage` | Releases 页面地址 |
| `size` | 包大小（字节），用于提示 |
| `sha256` | 包的 SHA-256，用于校验 |
| `notes` | 更新说明，直接显示在弹窗里 |
| `publishedAt` | 发布日期 |

客户端在两处用它：启动后约 4 秒静默查一次（只有新版才问，同一版本只提醒一次）；以及设置里的「检查更新」手动入口。

## 客户端怎么读

并发请求以下地址，取版本最高的一份：

```
https://cdn.jsdelivr.net/gh/RainxButterfly/DayLeaf@latest/desktop/backend/update.json
https://fastly.jsdelivr.net/gh/RainxButterfly/DayLeaf@latest/desktop/backend/update.json
https://raw.githubusercontent.com/RainxButterfly/DayLeaf/main/desktop/backend/update.json
https://cdn.jsdelivr.net/gh/RainxButterfly/DayLeaf@main/desktop/backend/update.json
```

用 `@latest` 而不是 `@main`：CDN 对分支地址的缓存长达十几小时，发版后要等很久；`@latest` 解析到最新 tag，每次发版都是新的缓存键，可以立即生效。

## 发版流程

1. 改版本号并构建：

   ```powershell
   cd desktop/frontend
   powershell -ExecutionPolicy Bypass -File build.ps1 -Version 1.0.1
   ```

2. 用发布包的实际值更新本文件的 `versionName`、`url`、`size`、`sha256`、`notes`。
3. 提交，打新 tag（形如 `desktop-v1.0.1`），推送到 GitHub。
4. 在 GitHub 上建 Release，上传 `dist\DayLeaf-windows-1.0.1.zip`。
5. 刷新一次 CDN 缓存：

   ```bash
   curl "https://purge.jsdelivr.net/gh/RainxButterfly/DayLeaf@latest/desktop/backend/update.json"
   ```

> tag 一旦推送就不要再移动。CDN 把 tag 当作不可变内容，移动后同一个地址会长期返回旧文件，purge 也救不回来——需要改内容就发新版本号。
