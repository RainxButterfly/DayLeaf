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

import org.json.JSONObject;

import java.io.BufferedInputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;

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
     * thorough=true（用户手动点「检查更新」）时多问一轮：如果拿到的版本不比当前新，
     * 很可能是这台设备连到的 CDN 边缘还缓存着旧清单，这时带一个时间戳再请求一次——
     * URL 变了就是新的缓存键，会强制回源。启动时的静默检测不做这一步，省一次请求。
     */
    private UpdateInfo fetchBest(boolean thorough) {
        UpdateInfo first = fetchRound(null);
        if (!thorough) return first;

        if (first != null && first.versionCode > currentVersionCode()) return first;

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
        info.sha256 = o.optString("sha256", "");
        info.force = o.optBoolean("force", false);
        return info;
    }

    /** 把 URL 里的非 ASCII 字符按 UTF-8 百分号编码，避免请求失败。 */
    private static String encodeUrl(String url) throws Exception {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < url.length(); i++) {
            char c = url.charAt(i);
            if (c > 0x7F) {
                byte[] b = String.valueOf(c).getBytes("UTF-8");
                for (byte x : b) sb.append('%').append(String.format("%02X", x));
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
        StringBuilder sb = new StringBuilder();
        byte[] buf = new byte[4096];
        int n;
        while ((n = in.read(buf)) != -1) sb.append(new String(buf, 0, n, "UTF-8"));
        in.close();
        c.disconnect();
        return sb.toString();
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
        StringBuilder msg = new StringBuilder();
        msg.append("发现新版本 v").append(info.versionName)
           .append("（当前 v").append(currentVersionName()).append("）");
        if (info.notes != null && info.notes.length() > 0) msg.append("\n\n").append(info.notes);
        if (info.size > 0) msg.append("\n\n大小约 ").append(humanSize(info.size));

        AlertDialog.Builder b = new AlertDialog.Builder(activity);
        b.setTitle(info.force ? "需要更新" : "发现新版本");
        b.setMessage(msg.toString());
        b.setPositiveButton("立即更新", new DialogInterface.OnClickListener() {
            @Override public void onClick(DialogInterface d, int which) { downloadAndInstall(info); }
        });
        if (!info.force) {
            b.setNegativeButton("以后再说", null);
        }
        b.setCancelable(!info.force);
        b.show();
    }

    /* ---------------- 下载并安装 ---------------- */

    interface ProgressCallback { void onProgress(long done, long total); }

    private void downloadAndInstall(final UpdateInfo info) {
        final ProgressDialog pd = new ProgressDialog(activity);
        pd.setProgressStyle(ProgressDialog.STYLE_HORIZONTAL);
        pd.setTitle("正在下载更新");
        pd.setMessage(info.versionName);
        pd.setCancelable(false);
        pd.show();

        new Thread(new Runnable() {
            @Override public void run() {
                try {
                    File dir = activity.getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS);
                    if (dir == null) dir = activity.getCacheDir();
                    final File apk = new File(dir, "dayleaf-" + info.versionCode + ".apk");

                    download(info.url, apk, new ProgressCallback() {
                        @Override public void onProgress(final long done, final long total) {
                            ui.post(new Runnable() {
                                @Override public void run() {
                                    if (total > 0) {
                                        pd.setMax((int) total);
                                        pd.setProgress((int) done);
                                    }
                                }
                            });
                        }
                    });

                    ui.post(new Runnable() {
                        @Override public void run() {
                            try { pd.dismiss(); } catch (Exception e) { /* ignore */ }
                            installApk(apk);
                        }
                    });
                } catch (final Exception e) {
                    ui.post(new Runnable() {
                        @Override public void run() {
                            try { pd.dismiss(); } catch (Exception ex) { /* ignore */ }
                            Toast.makeText(activity, "下载失败：" + e.getMessage(), Toast.LENGTH_LONG).show();
                        }
                    });
                }
            }
        }).start();
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
