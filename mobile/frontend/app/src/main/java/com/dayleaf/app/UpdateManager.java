/**
 * 自动更新：多源检测版本、下载并覆盖安装。
 *
 * 作者名：StarLeaf·Roxy
 * 创建日期：2026-10-25
 */
package com.dayleaf.app;

import android.app.Activity;
import android.app.AlertDialog;
import android.app.ProgressDialog;
import android.content.DialogInterface;
import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.widget.Toast;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedInputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.List;

public class UpdateManager {

    /**
     * 更新清单的多个备用源。
     *
     * 用 @latest 而不是 @main：CDN 对分支地址的缓存长达十几小时，发版后客户端要等很久
     * 才看得到新版本；而 @latest 解析到最新 tag，每次发版都是新的缓存键，可以立即生效。
     * 最后一条 @main 是兜底——即使滞后也没关系，客户端会取所有源里版本号最高的一个。
     */
    private static final String[] UPDATE_SOURCES = {
        "https://cdn.jsdelivr.net/gh/RainxButterfly/DayLeaf@latest/mobile/backend/update.json",
        "https://fastly.jsdelivr.net/gh/RainxButterfly/DayLeaf@latest/mobile/backend/update.json",
        "https://gcore.jsdelivr.net/gh/RainxButterfly/DayLeaf@latest/mobile/backend/update.json",
        "https://raw.githubusercontent.com/RainxButterfly/DayLeaf/main/mobile/backend/update.json",
        "https://cdn.jsdelivr.net/gh/RainxButterfly/DayLeaf@main/mobile/backend/update.json"
    };

    private final Activity activity;
    private final Handler ui = new Handler(Looper.getMainLooper());

    public UpdateManager(Activity activity) {
        this.activity = activity;
    }

    private int currentVersionCode() {
        try {
            return activity.getPackageManager().getPackageInfo(activity.getPackageName(), 0).versionCode;
        } catch (Exception e) { return 0; }
    }

    private String currentVersionName() {
        try {
            return activity.getPackageManager().getPackageInfo(activity.getPackageName(), 0).versionName;
        } catch (Exception e) { return "0"; }
    }

    /**
     * 检查更新。manual=true（设置里的按钮）会给结果提示；
     * manual=false（启动静默检测）不打扰，只在真的发现新版本时才弹窗。
     */
    public void checkForUpdate(final boolean manual) {
        new Thread(new Runnable() {
            @Override public void run() {
                final UpdateInfo best = fetchBestWithDeadline(manual);
                ui.post(new Runnable() {
                    @Override public void run() {
                        if (best != null && best.versionCode > currentVersionCode()) {
                            showUpdateDialog(best);
                        } else if (manual) {
                            if (best == null) {
                                Toast.makeText(activity, "检查失败，请检查网络后重试", Toast.LENGTH_SHORT).show();
                            } else {
                                Toast.makeText(activity, "已是最新版本 v" + currentVersionName(), Toast.LENGTH_SHORT).show();
                            }
                        }
                    }
                });
            }
        }).start();
    }

    /** 整体最多等 14 秒（手动检查多一轮重试，给到 20 秒）；超时就当没结果，绝不卡住界面。 */
    private UpdateInfo fetchBestWithDeadline(final boolean thorough) {
        final UpdateInfo[] out = new UpdateInfo[1];
        Thread t = new Thread(new Runnable() {
            @Override public void run() { out[0] = fetchBest(thorough); }
        });
        t.setDaemon(true);
        t.start();
        try {
            t.join(thorough ? 20000 : 14000);
        } catch (InterruptedException e) { /* ignore */ }
        return out[0];
    }

    /**
     * 取所有源里最新的那一份。
     *
     * 清单里只有最新版本，客户端一次就能拿到目标版本，不存在「必须先装中间版本」的
     * 逻辑。之所以有人看到逐版本升级，是 CDN 边缘缓存了旧清单：1.0.10 的机器先看到
     * 1.0.11，装完再请求才看到 1.0.12。这里每个地址都带时间戳（尽力而为），
     * 5 个源并行取最大版本；@latest 每次发版都是新 tag/new cache key，双重保证直达最新。
     */
    private UpdateInfo fetchBest(boolean thorough) {
        UpdateInfo first = fetchRound(String.valueOf(System.currentTimeMillis()));
        if (first != null && first.versionCode > currentVersionCode()) return first;
        if (!thorough) return first;

        /* 手动检查再问一轮：防止刚好撞上某个边缘节点的旧缓存 */
        try { Thread.sleep(350); } catch (InterruptedException e) { /* ignore */ }
        UpdateInfo again = fetchRound(String.valueOf(System.currentTimeMillis()));
        if (again == null) return first;
        if (first == null || again.versionCode > first.versionCode) return again;
        return first;
    }

    /** 一轮并发请求；buster 非空时给每个地址加时间戳参数，绕开 CDN 缓存。 */
    private UpdateInfo fetchRound(String buster) {
        final java.util.List<UpdateInfo> got =
                java.util.Collections.synchronizedList(new java.util.ArrayList<UpdateInfo>());
        final java.util.concurrent.CountDownLatch latch =
                new java.util.concurrent.CountDownLatch(UPDATE_SOURCES.length);

        for (final String src : UPDATE_SOURCES) {
            final String url = withBuster(src, buster);
            Thread t = new Thread(new Runnable() {
                @Override public void run() {
                    try {
                        UpdateInfo info = parse(httpGet(url, 2500));
                        if (info != null && info.url != null && info.url.length() > 0) {
                            got.add(info);
                        }
                    } catch (Exception e) {
                        // 该源不可用（网络/解析失败），忽略
                    } finally {
                        latch.countDown();
                    }
                }
            });
            t.setDaemon(true);
            t.start();
        }

        try {
            latch.await(6000, java.util.concurrent.TimeUnit.MILLISECONDS);
        } catch (InterruptedException e) { /* ignore */ }

        UpdateInfo best = null;
        synchronized (got) {
            for (UpdateInfo info : got) {
                if (best == null || info.versionCode > best.versionCode) best = info;
            }
        }
        return best;
    }

    private static String withBuster(String url, String buster) {
        if (buster == null || buster.length() == 0) return url;
        return url + (url.indexOf('?') >= 0 ? "&" : "?") + "_=" + buster;
    }

    private UpdateInfo parse(String json) throws Exception {
        JSONObject o = new JSONObject(json);
        UpdateInfo info = new UpdateInfo();
        info.versionCode = o.optInt("versionCode", 0);
        info.versionName = o.optString("versionName", "");
        info.url = o.optString("url", "");
        info.notes = o.optString("notes", "");
        info.size = o.optLong("size", 0);
        info.sha256 = o.optString("sha256", "").trim();
        info.force = o.optBoolean("force", false);
        info.minVersionCode = o.optInt("minVersionCode", 0);

        JSONArray arr = o.optJSONArray("urls");
        if (arr != null) {
            for (int i = 0; i < arr.length(); i++) {
                String u = arr.optString(i, "");
                if (u != null && u.length() > 0) info.urls.add(u);
            }
        }
        return info;
    }

    /** 把 URL 里的非 ASCII 字符按 UTF-8 百分号编码，避免请求失败。 */
    private static String encodeUrl(String url) throws Exception {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < url.length(); i++) {
            char c = url.charAt(i);
            if (c > 0x7F) {
                byte[] b = String.valueOf(c).getBytes("UTF-8");
                for (byte x : b) sb.append('%').append(String.format("%02X", x & 0xFF));
            } else {
                sb.append(c);
            }
        }
        return sb.toString();
    }

    private String httpGet(String urlStr, int timeout) throws Exception {
        HttpURLConnection c = (HttpURLConnection) new URL(encodeUrl(urlStr)).openConnection();
        c.setConnectTimeout(timeout);
        c.setReadTimeout(timeout);
        c.setRequestProperty("User-Agent", "DayLeaf-Android/" + currentVersionName());
        c.setRequestProperty("Cache-Control", "no-cache, no-store");
        c.setInstanceFollowRedirects(true);
        c.connect();
        int code = c.getResponseCode();
        if (code < 200 || code >= 300) throw new Exception("HTTP " + code);
        InputStream in = c.getInputStream();
        java.io.ByteArrayOutputStream bos = new java.io.ByteArrayOutputStream();
        byte[] buf = new byte[4096];
        int n;
        while ((n = in.read(buf)) != -1) bos.write(buf, 0, n);
        in.close();
        c.disconnect();
        return new String(bos.toByteArray(), "UTF-8");
    }

    /** 把字节数换成便于阅读的大小；几百 KB 的包不能被整除成「0 MB」。 */
    private static String humanSize(long bytes) {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) {
            long kb = Math.round(bytes / 1024.0);
            return (kb < 1 ? 1 : kb) + " KB";
        }
        return String.format(java.util.Locale.US, "%.1f MB", bytes / 1048576.0);
    }

    /* ---------------- 弹窗 ---------------- */

    private void showUpdateDialog(final UpdateInfo info) {
        final int curCode = currentVersionCode();
        final boolean force = info.force ||
                (info.minVersionCode > 0 && curCode < info.minVersionCode);

        StringBuilder msg = new StringBuilder();
        msg.append("当前 v").append(currentVersionName())
           .append(" → 最新 v").append(info.versionName)
           .append("\n可直接一步升级到最新版，不用逐版安装。");
        if (info.notes != null && info.notes.length() > 0) msg.append("\n\n").append(info.notes);
        if (info.size > 0) msg.append("\n\n大小约 ").append(humanSize(info.size));

        AlertDialog.Builder b = new AlertDialog.Builder(activity);
        b.setTitle(force ? "需要更新" : "发现新版本");
        b.setMessage(msg.toString());
        b.setPositiveButton("立即更新", new DialogInterface.OnClickListener() {
            @Override public void onClick(DialogInterface d, int which) { downloadAndInstall(info); }
        });
        if (!force) {
            b.setNegativeButton("以后再说", null);
        }
        b.setCancelable(!force);
        b.show();
    }

    /* ---------------- 下载并安装 ---------------- */

    interface ProgressCallback { void onProgress(long done, long total); }

    private void downloadAndInstall(final UpdateInfo info) {
        final ProgressDialog pd = new ProgressDialog(activity);
        pd.setProgressStyle(ProgressDialog.STYLE_HORIZONTAL);
        pd.setTitle("正在下载更新");
        pd.setMessage("v" + info.versionName + " · 可直接覆盖安装");
        pd.setCancelable(false);
        pd.show();

        new Thread(new Runnable() {
            @Override public void run() {
                File dir = activity.getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS);
                if (dir == null) dir = activity.getCacheDir();
                final File apk = new File(dir, "dayleaf-" + info.versionCode + ".apk");
                final File part = new File(dir, "dayleaf-" + info.versionCode + ".part");

                List<String> sources = candidateUrls(info);
                Exception last = null;
                boolean ok = false;

                for (int i = 0; i < sources.size(); i++) {
                    final String src = sources.get(i);
                    try {
                        if (part.exists()) part.delete();
                        /* 每个地址都带时间戳：避免 CDN 把旧版本安装包当成新包发下来 */
                        download(withBuster(src, String.valueOf(System.currentTimeMillis())), part,
                                new ProgressCallback() {
                                    @Override public void onProgress(final long done, final long total) {
                                        ui.post(new Runnable() {
                                            @Override public void run() {
                                                if (total > 0) {
                                                    pd.setIndeterminate(false);
                                                    pd.setMax((int) total);
                                                    pd.setProgress((int) done);
                                                } else {
                                                    pd.setIndeterminate(true);
                                                }
                                            }
                                        });
                                    }
                                });
                        verifyPackage(part, info);
                        if (apk.exists() && !apk.delete()) throw new Exception("无法写入安装包");
                        if (!part.renameTo(apk)) throw new Exception("无法保存安装包");
                        ok = true;
                        break;
                    } catch (Exception e) {
                        last = e;
                        if (part.exists()) part.delete();
                        /* 还有备用源时提示一下，别让用户以为下载失败了 */
                        if (i < sources.size() - 1) {
                            ui.post(new Runnable() {
                                @Override public void run() {
                                    pd.setMessage("下载源不可用，正在切换备用源…");
                                }
                            });
                        }
                    }
                }

                if (!ok) {
                    final String why = last != null && last.getMessage() != null
                            ? last.getMessage() : "没有可用的下载源";
                    ui.post(new Runnable() {
                        @Override public void run() {
                            try { pd.dismiss(); } catch (Exception ex) { /* ignore */ }
                            Toast.makeText(activity, "下载失败：" + why + "，请稍后重试",
                                    Toast.LENGTH_LONG).show();
                        }
                    });
                    return;
                }

                ui.post(new Runnable() {
                    @Override public void run() {
                        try { pd.dismiss(); } catch (Exception e) { /* ignore */ }
                        installApk(apk);
                    }
                });
            }
        }).start();
    }

    /** 主地址 + urls[] 备用源，去重后按顺序尝试 */
    private static List<String> candidateUrls(UpdateInfo info) {
        List<String> out = new ArrayList<String>();
        addUrl(out, info.url);
        for (String u : info.urls) addUrl(out, u);
        return out;
    }

    private static void addUrl(List<String> list, String url) {
        if (url == null) return;
        String u = url.trim();
        if (u.length() == 0 || list.contains(u)) return;
        list.add(u);
    }

    /**
     * 安装包校验：大小与 sha256 至少对上一个。
     * 只有清单里给了 sha256 才比对；老清单没有这项时仍会校验大小。
     */
    private static void verifyPackage(File f, UpdateInfo info) throws Exception {
        long len = f.length();
        if (len <= 0) throw new Exception("下载到的文件为空");
        if (info.size > 0 && len != info.size) {
            throw new Exception("安装包大小不符（应为 " + info.size + " 字节，实际 " + len + "）");
        }
        if (info.sha256 != null && info.sha256.length() == 64) {
            String got = sha256(f);
            if (!got.equalsIgnoreCase(info.sha256)) {
                throw new Exception("安装包校验失败（文件可能被 CDN 缓存替换）");
            }
        }
    }

    private static String sha256(File f) throws Exception {
        MessageDigest md = MessageDigest.getInstance("SHA-256");
        FileInputStream in = new FileInputStream(f);
        try {
            byte[] buf = new byte[8192];
            int n;
            while ((n = in.read(buf)) != -1) md.update(buf, 0, n);
        } finally {
            try { in.close(); } catch (Exception e) { /* ignore */ }
        }
        byte[] d = md.digest();
        StringBuilder sb = new StringBuilder(d.length * 2);
        for (int i = 0; i < d.length; i++) {
            sb.append(String.format("%02x", d[i] & 0xFF));
        }
        return sb.toString();
    }

    private void download(String urlStr, File out, ProgressCallback cb) throws Exception {
        HttpURLConnection c = (HttpURLConnection) new URL(encodeUrl(urlStr)).openConnection();
        c.setConnectTimeout(10000);
        c.setReadTimeout(60000);
        c.setRequestProperty("User-Agent", "DayLeaf-Android/" + currentVersionName());
        c.setInstanceFollowRedirects(true);
        c.connect();
        int code = c.getResponseCode();
        if (code < 200 || code >= 300) throw new Exception("HTTP " + code);

        long total = c.getContentLength();
        InputStream in = new BufferedInputStream(c.getInputStream());
        FileOutputStream fos = new FileOutputStream(out);
        byte[] buf = new byte[8192];
        int n;
        long done = 0;
        while ((n = in.read(buf)) != -1) {
            fos.write(buf, 0, n);
            done += n;
            if (cb != null) cb.onProgress(done, total);
        }
        fos.flush();
        fos.close();
        in.close();
        c.disconnect();
    }

    private void installApk(File apk) {
        Intent intent = new Intent(Intent.ACTION_VIEW);
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        if (Build.VERSION.SDK_INT >= 24) {
            Uri uri = InstallApkProvider.getUriForFile(activity, apk);
            intent.setDataAndType(uri, "application/vnd.android.package-archive");
            intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        } else {
            intent.setDataAndType(Uri.fromFile(apk), "application/vnd.android.package-archive");
        }
        try {
            activity.startActivity(intent);
        } catch (Exception e) { /* ignore */ }

        // 8.0+ 需要「允许安装未知应用」；没开就跳去设置页引导用户打开
        if (Build.VERSION.SDK_INT >= 26 && !activity.getPackageManager().canRequestPackageInstalls()) {
            try {
                Intent si = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                        Uri.parse("package:" + activity.getPackageName()));
                activity.startActivity(si);
            } catch (Exception ex) { /* ignore */ }
        }
    }
}
