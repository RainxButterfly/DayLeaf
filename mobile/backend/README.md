# 更新清单（「后端」）

本项目没有自建服务器，自动更新完全走 GitHub 分发，所以「后端」就是一个托管在仓库里的静态 `update.json`，通过多个 CDN/镜像地址对外提供。

## `update.json` 字段

| 字段 | 含义 |
| --- | --- |
| `channel` | 目前固定 `android`；Windows 客户端将来用 `windows`，各管各的清单 |
| `versionCode` | 整数，只增不减，客户端只认它比大小 |
| `versionName` | 展示用版本名，如 `1.0.0` |
| `minVersionCode` | 可原地覆盖升级的最低版本号（当前不强制） |
| `force` | 是否强制更新（`false` = 非强制，可「以后再说」） |
| `url` | APK 下载地址（GitHub Release 附件） |
| `size` | APK 字节数（用于展示「约 X MB」），可为 0 |
| `sha256` | APK 的 SHA-256（下载校验，为空则跳过） |
| `notes` | 更新说明，弹窗里展示 |
| `publishedAt` | 发布日期，仅记录 |

## 客户端如何检测（对应 `frontend/.../UpdateManager.java`）

1. 依次请求 `UPDATE_SOURCES` 里的多个备用源，连不上的跳过；
2. 解析每个源返回的 `versionCode`；
3. 取所有可达源里 **最高的 `versionCode`** 作为「最新版本」；
4. 与当前安装的 `versionCode` 比较，更高则弹「发现新版本」（非强制）。

多源的意义：主源（GitHub raw）在国内偶尔抽风，jsDelivr CDN 和 ghproxy 镜像兜底；同时天然兼容「某个 CDN 还没刷新到最新版」——反正取最高的。

## 发布一个新版本

1. 改 `frontend/app/build.gradle`：`versionCode +1`、`versionName` 改成新号；
2. 本地 `./gradlew assembleRelease` 用**同一把签名**打出 APK（签名见 `frontend/README.md`）；
3. 计算 APK 字节数与 SHA-256：
   ```bash
   ls -l DayLeaf-android-v1.0.0.apk
   shasum -a 256 DayLeaf-android-v1.0.0.apk      # macOS / Linux
   Get-FileHash DayLeaf-android-v1.0.0.apk -Algorithm SHA256   # Windows PowerShell
   ```
4. 更新 `update.json`：`versionCode`、`versionName`、`url`、`size`、`sha256`、`notes`；
5. 在 GitHub 打一个 tag（如 `v1.0.0`）并创建 Release，把 APK 作为附件上传，`url` 指向它；
6. `git push`，等镜像自动刷新，客户端下次启动/手动检查即可发现。

> ⚠️ 原地升级（数据保留）的前提是**同一包名 + 同一签名**。签名一变，系统会当成另一个 App，用户得卸载重装（数据就丢了）。所以签名文件务必备份。

## 发布自动化（可选）

仓库根的 `.github/workflows/android-release.yml` 可自动完成「打 tag → 构建 → 发 Release」，模板见本目录 `android-release.yml`（需自行配置签名 secrets）。
