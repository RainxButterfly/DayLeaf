# Windows 更新清单

`update.json` 是 Windows 客户端的版本清单：启动后会在后台静默读一次，设置里的「检查更新」也会读它。托管在 GitHub 上，经 CDN 多源分发，没有自建服务器。

## 字段

| 字段 | 说明 |
|---|---|
| `channel` | 固定 `windows`，与安卓端清单区分 |
| `versionCode` | 整数版本号，备用 |
| `versionName` | 版本字符串，客户端按点分数字比对 |
| `minVersionCode` | 最低可用版本，低于此值需强制更新 |
| `url` | 新版**安装包**的下载地址（jsDelivr 镜像，仓库文件 `desktop/backend/DayLeaf-Setup.bin`） |
| `urls` | 备用下载地址数组，客户端按顺序自动切换 |
| `releasePage` | Releases 页面地址 |
| `size` | 包大小（字节），用于提示与校验 |
| `sha256` | 包的 SHA-256，用于校验 |
| `notes` | 更新说明，直接显示在弹窗里 |
| `publishedAt` | 发布日期 |

> 安装包在仓库里以 `.bin` 后缀保存：jsDelivr 会拒绝 `.exe`（403），但 `.bin` 正常返回 `application/octet-stream`。客户端下载后保存为 `.exe` 再运行，内容就是 NSIS 安装程序。

客户端在两处用它：启动后约 1.5 秒静默查一次（只有新版才问，同一版本只提醒一次）；以及设置里的「检查更新」手动入口。

## 客户端怎么读

并发请求以下地址，取版本最高的一份：

```
https://cdn.jsdelivr.net/gh/RainxButterfly/DayLeaf@latest/desktop/backend/update.json
https://fastly.jsdelivr.net/gh/RainxButterfly/DayLeaf@latest/desktop/backend/update.json
https://gcore.jsdelivr.net/gh/RainxButterfly/DayLeaf@latest/desktop/backend/update.json
https://raw.githubusercontent.com/RainxButterfly/DayLeaf/main/desktop/backend/update.json
https://cdn.jsdelivr.net/gh/RainxButterfly/DayLeaf@main/desktop/backend/update.json
```

用 `@latest` 而不是 `@main`：CDN 对分支地址的缓存长达十几小时，发版后要等很久；`@latest` 解析到最新 tag，每次发版都是新的缓存键，可以立即生效。客户端取所有源里最高的版本，并给每个地址带时间戳，尽量绕开边缘缓存。

## 发版流程

1. 改版本号并构建：

   ```powershell
   cd desktop/frontend
   powershell -ExecutionPolicy Bypass -File build.ps1 -Version 1.0.12
   ```

2. 把 `dist\DayLeaf-Setup-<版本>.exe` 复制成固定文件名 `desktop/backend/DayLeaf-Setup.bin`（覆盖上一版，路径不变）；
3. 计算字节数与 SHA-256，用实际值更新本文件的 `versionName`、`url`、`urls`、`size`、`sha256`、`notes`：
   - `url` 用**固定 tag** 的 jsDelivr 地址，保证旧清单里的哈希不会因为后续版本而失效：
     `https://cdn.jsdelivr.net/gh/RainxButterfly/DayLeaf@v<版本>/desktop/backend/DayLeaf-Setup.bin`
   - `urls` 放 fastly / gcore / raw 的同一 tag 地址；
4. 提交，打新 tag（与移动端共用版本号，如 `v1.0.12`），推送到 GitHub；
5. 刷新一次 CDN 缓存：

   ```bash
   curl "https://purge.jsdelivr.net/gh/RainxButterfly/DayLeaf@latest/desktop/backend/update.json"
   ```

   不需要再把安装包上传到 GitHub Release（仓库里的 `.bin` 已可供下载）。

> tag 一旦推送就不要再移动。CDN 把 tag 当作不可变内容，移动后同一个地址会长期返回旧文件，purge 也救不回来——需要改内容就发新版本号。
>
> 旧版 1.0.10 的更新器只读 `url`、不校验 SHA；因此 `url` 必须指向可达的镜像（当前是 jsDelivr），否则仍会出现 `github.com` 无法解析的下载失败。新版会读 `urls` 并在下载后校验大小与 SHA-256。
