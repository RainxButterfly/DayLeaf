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

    /** 更新清单的多个备用源。 */
    private static final String[] UPDATE_SOURCES = {
        "https://raw.githubusercontent.com/RainxButterfly/DayLeaf/main/mobile/backend/update.json",
        "https://cdn.jsdelivr.net/gh/RainxButterfly/DayLeaf@main/mobile/backend/update.json",
        "https://cdn.statically.io/gh/RainxButterfly/DayLeaf/main/mobile/backend/update.json"
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
                final UpdateInfo best = fetchBestWithDeadline();
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

    /** 用一条独立线程做检测，最多等 6 秒；超时就当没结果，绝不卡住界面。 */
    private UpdateInfo fetchBestWithDeadline() {
        final UpdateInfo[] out = new UpdateInfo[1];
        Thread t = new Thread(new Runnable() {
            @Override public void run() { out[0] = fetchBest(); }
        });
        t.setDaemon(true);
        t.start();
        try {
            t.join(6000);
        } catch (InterruptedException e) { /* ignore */ }
        return out[0];
    }

    /** 依次尝试每个源，解析 JSON，返回 versionCode 最大的一个；全失败返回 null。 */
    private UpdateInfo fetchBest() {
        UpdateInfo best = null;
        for (String src : UPDATE_SOURCES) {
            try {
                UpdateInfo info = parse(httpGet(src, 5000));
                if (info != null && info.url != null && info.url.length() > 0) {
                    if (best == null || info.versionCode > best.versionCode) best = info;
                }
            } catch (Exception e) {
                // 该源不可用（网络/解析失败），忽略并尝试下一个
            }
        }
        return best;
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

    /* ---------------- 弹窗 ---------------- */

    private void showUpdateDialog(final UpdateInfo info) {
        StringBuilder msg = new StringBuilder();
        msg.append("发现新版本 v").append(info.versionName)
           .append("（当前 v").append(currentVersionName()).append("）");
        if (info.notes != null && info.notes.length() > 0) msg.append("\n\n").append(info.notes);
        if (info.size > 0) msg.append("\n\n大小约 ").append(info.size / 1024 / 1024).append(" MB");

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
