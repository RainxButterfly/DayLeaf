# 更新清单（「后端」）

本项目没有自建服务器：`update.json` 和安装包都托管在仓库里，经 jsDelivr 多源分发。GitHub Release 仍可作为发布记录，但客户端下载不依赖 `github.com`（部分地区无法解析）。

## `update.json` 字段

| 字段 | 含义 |
| --- | --- |
| `channel` | 目前固定 `android`；Windows 客户端将来用 `windows`，各管各的清单 |
| `versionCode` | 整数，只增不减，客户端只认它比大小 |
| `versionName` | 展示用版本名，如 `1.0.0` |
| `minVersionCode` | 可原地覆盖升级的最低版本号（当前不强制） |
| `force` | 是否强制更新（`false` = 非强制，可「以后再说」） |
| `url` | APK 下载地址（首选 jsDelivr 镜像；安装包随仓库托管在 `mobile/backend/DayLeaf-android.apk`） |
| `urls` | 备用下载地址数组，客户端按顺序自动切换 |
| `size` | APK 字节数（用于展示与下载校验），可为 0 |
| `sha256` | APK 的 SHA-256（下载校验，为空则跳过） |
| `notes` | 更新说明，弹窗里展示 |
| `publishedAt` | 发布日期，仅记录 |

## 客户端如何检测（对应 `frontend/.../UpdateManager.java`）

1. 并发请求 `UPDATE_SOURCES` 里的多个备用源，连不上的跳过；
2. 解析每个源返回的 `versionCode`；
3. 取所有可达源里 **最高的 `versionCode`** 作为「最新版本」；
4. 与当前安装的 `versionCode` 比较，更高则弹「发现新版本」（非强制）。

下载时先试 `url`，失败再按 `urls` 顺序切换备用源；写完 `.part` 后校验大小和 SHA-256，通过才改名安装。

多源的意义：jsDelivr 的各个 provider 刷新时间并不一致（实测同一天 cdn 已是新版、fastly/gcore 还是旧版），取最高版本可避免某一路缓存滞后导致误报「已是最新」；下载则靠 `urls` 绕过某个域名不可达。

## 发布一个新版本

1. 改 `frontend/app/build.gradle`：`versionCode +1`、`versionName` 改成新号；同步改界面里的 `APP.version`；
2. 本地 `./gradlew assembleRelease` 用**同一把签名**打出 APK（签名见 `frontend/README.md`）；
3. 把 APK 复制成固定文件名 `mobile/backend/DayLeaf-android.apk`（覆盖上一版，路径不变）；
4. 计算字节数与 SHA-256：
   ```bash
   stat -c '%s' mobile/backend/DayLeaf-android.apk
   sha256sum mobile/backend/DayLeaf-android.apk
   ```
5. 更新 `update.json`：`versionCode`、`versionName`、`url`、`urls`、`size`、`sha256`、`notes`；
   - `url` 用 **固定 tag** 的 jsDelivr 地址，避免下载时被解析到更新版本导致哈希不符：
     `https://cdn.jsdelivr.net/gh/RainxButterfly/DayLeaf@v<版本>/mobile/backend/DayLeaf-android.apk`
   - `urls` 放 fastly / gcore / raw 的同一 tag 地址；
6. 提交并打 tag（如 `v1.0.12`）推送；
7. 刷新 jsDelivr 的清单缓存：
   ```bash
   curl "https://purge.jsdelivr.net/gh/RainxButterfly/DayLeaf@latest/mobile/backend/update.json"
   ```
8. 用浏览器/curl 验证 `@v<版本>` 的 APK 地址和清单可访问；不需要再上传 GitHub Release（安装包已在仓库里）。

> ⚠️ 原地升级（数据保留）的前提是**同一包名 + 同一签名**。签名一变，系统会当成另一个 App，用户得卸载重装（数据就丢了）。所以签名文件务必备份。
>
> ⚠️ `@latest` 只用于清单；安装包地址必须锁到 `@v<版本>`。否则新版本发布后，旧清单里的 URL 会解析到新包，大小/SHA 校验必然失败。

## 发布自动化（可选）

本目录的 `android-release.yml` 是 GitHub Actions 模板，可自动完成「打 tag → 构建 → 发 Release」。启用后仍需把 APK 复制到 `mobile/backend/DayLeaf-android.apk` 并提交，镜像下载才生效；模板需自行配置签名 secrets。
