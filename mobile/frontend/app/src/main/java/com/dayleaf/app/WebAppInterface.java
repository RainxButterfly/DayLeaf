/**
 * 暴露给 JS 的原生桥：通知、震动、检查更新、版本号、系统设置入口、
 * 系统分享、桌面小组件、二进制保存、WebDAV 同步。
 *
 * 作者名：StarLeaf·Roxy
 * 创建日期：2026-10-25
 */
package com.dayleaf.app;

import android.app.Activity;
import android.app.AlarmManager;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.provider.Settings;
import android.util.Base64;
import android.webkit.JavascriptInterface;

import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.concurrent.atomic.AtomicBoolean;

public class WebAppInterface {

    private static final String CHANNEL_ID = "dayleaf_remind";
    private static final int REQ_NOTIFY_PERM = 1001;

    private static final int NET_TIMEOUT_MS = 20000;
    private static final int MAX_RESPONSE = 8 * 1024 * 1024;   // WebDAV 响应上限 8MB

    /** 同一时间只跑一个 WebDAV 请求，避免多开线程互相踩 */
    private static final AtomicBoolean SYNCING = new AtomicBoolean(false);

    private final Activity activity;
    private final UpdateManager updateManager;

    public WebAppInterface(Activity activity, UpdateManager updateManager) {
        this.activity = activity;
        this.updateManager = updateManager;
    }

    @JavascriptInterface
    public void checkUpdate() {
        activity.runOnUiThread(new Runnable() {
            @Override public void run() { updateManager.checkForUpdate(true); }
        });
    }

    @JavascriptInterface
    public String getVersionName() {
        try {
            return activity.getPackageManager().getPackageInfo(activity.getPackageName(), 0).versionName;
        } catch (Exception e) {
            return "";
        }
    }

    @JavascriptInterface
    public void notify(final String title, final String body, final String tag) {
        activity.runOnUiThread(() -> postNotification(title, body, tag));
    }

    /** 网页把未来的提醒/闹钟排给系统；关闭应用后仍会触发 */
    @JavascriptInterface
    public void syncReminders(final String json) {
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                try { ReminderScheduler.apply(activity, json); } catch (Exception e) { /* ignore */ }
            }
        });
    }

    /** Android 12+ 精确闹钟特殊权限：没有时给出系统设置入口 */
    @JavascriptInterface
    public void ensureExactAlarmPermission() {
        if (Build.VERSION.SDK_INT < 31) return;
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                try {
                    AlarmManager am = (AlarmManager) activity.getSystemService(Context.ALARM_SERVICE);
                    if (am != null && !am.canScheduleExactAlarms()) {
                        Intent i = new Intent(Settings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM,
                                android.net.Uri.parse("package:" + activity.getPackageName()));
                        activity.startActivity(i);
                    }
                } catch (Exception e) { /* ignore */ }
            }
        });
    }

    @JavascriptInterface
    public void ensureNotifyPermission() {
        if (Build.VERSION.SDK_INT >= 33) {
            activity.runOnUiThread(() -> activity.requestPermissions(
                    new String[]{"android.permission.POST_NOTIFICATIONS"}, REQ_NOTIFY_PERM));
        }
    }

    /** 打开系统的「电池优化」列表（不申请任何危险权限），打不开就退到应用详情页 */
    @JavascriptInterface
    public void openBatterySettings() {
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                try {
                    activity.startActivity(new Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS));
                } catch (Exception e) {
                    openAppDetails();
                }
            }
        });
    }

    /** 打开本应用的通知设置页（Android 8+），低版本退到应用详情页 */
    @JavascriptInterface
    public void openNotificationSettings() {
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                if (Build.VERSION.SDK_INT >= 26) {
                    try {
                        Intent i = new Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS);
                        i.putExtra(Settings.EXTRA_APP_PACKAGE, activity.getPackageName());
                        activity.startActivity(i);
                        return;
                    } catch (Exception e) { /* 退到应用详情 */ }
                }
                openAppDetails();
            }
        });
    }

    private void openAppDetails() {
        try {
            Intent i = new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS);
            i.setData(Uri.parse("package:" + activity.getPackageName()));
            activity.startActivity(i);
        } catch (Exception e) { /* ignore */ }
    }

    @JavascriptInterface
    public void vibrate(long ms) {
        try {
            Vibrator v = (Vibrator) activity.getSystemService(Context.VIBRATOR_SERVICE);
            if (v != null) {
                if (Build.VERSION.SDK_INT >= 26) {
                    v.vibrate(VibrationEffect.createOneShot(ms, VibrationEffect.DEFAULT_AMPLITUDE));
                } else {
                    v.vibrate(ms);
                }
            }
        } catch (Exception e) { /* ignore */ }
    }

    /** JS 通知当前是否有弹窗，用于返回键/返回手势「先关弹窗，再退出」 */
    @JavascriptInterface
    public void setHasModal(final boolean has) {
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                if (activity instanceof MainActivity) {
                    ((MainActivity) activity).setHasModal(has);
                }
            }
        });
    }

    /**
     * JS 把主题偏好同步给原生存一份。
     * 原生在 Activity 起来时（比网页加载更早）就要用到它，
     * 否则深色用户会先看到一层浅色的窗口背景和系统闪屏。
     */
    @JavascriptInterface
    public void setThemeMode(final String mode) {
        try {
            activity.getSharedPreferences("dayleaf", Context.MODE_PRIVATE)
                    .edit().putString("theme", mode == null ? "light" : mode).apply();
        } catch (Exception e) { /* ignore */ }

        /* 运行中切换主题时，状态栏/导航栏也跟着走，不用等下次启动 */
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                if (activity instanceof MainActivity) {
                    ((MainActivity) activity).applyStoredThemeToChrome();
                }
            }
        });
    }

    /** 导出备份/日历文件：交给原生「另存为」保存（WebView 里 <a download> 不生效）。 */
    @JavascriptInterface
    public void saveTextFile(final String filename, final String content) {
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                if (activity instanceof MainActivity) {
                    ((MainActivity) activity).saveTextFile(filename, content);
                }
            }
        });
    }

    /**
     * 保存二进制：base64（可带 data:xxx;base64, 前缀）解码后走系统「另存为」。
     * 用于分享出去的 PNG、备份包等。
     */
    @JavascriptInterface
    public void saveBinary(final String name, final String base64, final String mime) {
        byte[] data = null;
        try { data = decodeBase64(base64); } catch (Exception e) { data = null; }
        if (data == null || data.length == 0) {
            activity.runOnUiThread(new Runnable() {
                @Override public void run() {
                    android.widget.Toast.makeText(activity, "内容为空或格式不对",
                            android.widget.Toast.LENGTH_SHORT).show();
                }
            });
            return;
        }
        final byte[] bytes = data;
        final String filename = (name == null || name.trim().length() == 0) ? "dayleaf.bin" : name.trim();
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                if (activity instanceof MainActivity) {
                    ((MainActivity) activity).saveBinaryFile(filename, bytes, mime);
                }
            }
        });
    }

    /** 系统分享进来的内容（文本 / 图片 data URL 或 file://）；取一次就清空 */
    @JavascriptInterface
    public String consumeSharedText() {
        try {
            if (activity instanceof MainActivity) {
                return ((MainActivity) activity).consumeSharedText();
            }
        } catch (Exception e) { /* ignore */ }
        return "";
    }

    /**
     * 刷新桌面「下一项」小组件。
     * json: {items:[{title,time,body}], updatedAt}，只取第一项。
     */
    @JavascriptInterface
    public void updateWidget(final String json) {
        try { NextWidgetProvider.saveFirstItem(activity, json); } catch (Exception e) { /* ignore */ }
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                try { NextWidgetProvider.updateAll(activity); } catch (Exception e) { /* ignore */ }
            }
        });
    }

    /**
     * WebDAV 同步：{op:"pull"|"push", url, user, pass, body}
     * 全程在后台线程，结束后回调 window.__dlSyncResult(op, ok, data, error)。
     */
    @JavascriptInterface
    public void webdavSync(final String configJson) {
        if (!SYNCING.compareAndSet(false, true)) {
            finishSync("pull", false, "", "同步进行中，请稍后再试");
            return;
        }
        new Thread(new Runnable() {
            @Override public void run() {
                SyncResult r;
                try {
                    r = runSync(configJson);
                } catch (Throwable t) {
                    r = new SyncResult();
                    r.err = t.getClass().getSimpleName();
                } finally {
                    SYNCING.set(false);
                }
                finishSync(r.op, r.ok, r.data, r.err);
            }
        }, "dayleaf-webdav").start();
    }

    // ------------------------------------------------------------------
    // WebDAV
    // ------------------------------------------------------------------

    private static final class SyncResult {
        String op = "pull";
        boolean ok = false;
        String data = "";
        String err = "";
    }

    private SyncResult runSync(String configJson) {
        SyncResult r = new SyncResult();
        HttpURLConnection conn = null;
        try {
            JSONObject c = new JSONObject(configJson == null || configJson.length() == 0 ? "{}" : configJson);
            r.op = "push".equals(c.optString("op", "pull")) ? "push" : "pull";
            String url = c.optString("url", "").trim();
            String user = c.optString("user", "");
            String pass = c.optString("pass", "");
            String body = c.optString("body", "");

            if (!url.startsWith("http://") && !url.startsWith("https://")) {
                r.err = "URL 无效";
                return r;
            }

            conn = (HttpURLConnection) new URL(url).openConnection();
            conn.setConnectTimeout(NET_TIMEOUT_MS);
            conn.setReadTimeout(NET_TIMEOUT_MS);
            conn.setUseCaches(false);
            conn.setInstanceFollowRedirects(true);
            conn.setRequestProperty("Accept", "application/json, text/plain, */*");
            conn.setRequestProperty("Content-Type", "application/json; charset=utf-8");
            if (user.length() > 0 || pass.length() > 0) {
                String token = user + ":" + pass;
                conn.setRequestProperty("Authorization", "Basic "
                        + Base64.encodeToString(token.getBytes("UTF-8"), Base64.NO_WRAP));
            }

            if ("push".equals(r.op)) {
                conn.setRequestMethod("PUT");
                conn.setDoOutput(true);
                byte[] payload = (body == null ? "" : body).getBytes("UTF-8");
                conn.setFixedLengthStreamingMode(payload.length);
                OutputStream os = conn.getOutputStream();
                os.write(payload);
                os.flush();
                os.close();
                int code = conn.getResponseCode();
                if (code >= 200 && code < 300) {
                    r.ok = true;
                    r.data = "";
                } else {
                    r.err = httpError(code);
                }
            } else {
                conn.setRequestMethod("GET");
                int code = conn.getResponseCode();
                if (code == 404) {
                    r.ok = true;      // 远端还没有文件：当作空的
                    r.data = "";
                } else if (code >= 200 && code < 300) {
                    r.data = readLimited(conn.getInputStream());
                    r.ok = true;
                } else {
                    r.err = httpError(code);
                }
            }
        } catch (Exception e) {
            String msg = e.getMessage();
            r.err = (msg == null || msg.length() == 0) ? e.getClass().getSimpleName() : msg;
            r.ok = false;
        } finally {
            try { if (conn != null) conn.disconnect(); } catch (Exception e) { /* ignore */ }
        }
        return r;
    }

    private static String httpError(int code) {
        if (code == 401) return "认证失败（401）";
        if (code == 403) return "没有权限（403）";
        if (code == 507) return "云端空间不足（507）";
        return "HTTP " + code;
    }

    private static String readLimited(InputStream in) throws Exception {
        ByteArrayOutputStream buf = new ByteArrayOutputStream();
        byte[] chunk = new byte[16 * 1024];
        int total = 0;
        int n;
        try {
            while ((n = in.read(chunk)) > 0) {
                total += n;
                if (total > MAX_RESPONSE) throw new IllegalStateException("响应超过 8MB");
                buf.write(chunk, 0, n);
            }
        } finally {
            try { in.close(); } catch (Exception e) { /* ignore */ }
        }
        return new String(buf.toByteArray(), "UTF-8");
    }

    /** 回调网页：data / error 都做了 JSON 转义，正文里有引号也不会把脚本弄断 */
    private void finishSync(String op, boolean ok, String data, String err) {
        String js = "(function(){try{if(window.__dlSyncResult){window.__dlSyncResult("
                + quote(op == null ? "pull" : op) + ","
                + (ok ? "true" : "false") + ","
                + quote(data == null ? "" : data) + ","
                + quote(err == null ? "" : err)
                + ");}}catch(e){}})();";
        if (activity instanceof MainActivity) {
            ((MainActivity) activity).runJs(js);
        } else {
            final String fjs = js;
            activity.runOnUiThread(new Runnable() {
                @Override public void run() {
                    try {
                        android.webkit.WebView wv = activity.findViewById(R.id.webview);
                        if (wv != null) wv.evaluateJavascript(fjs, null);
                    } catch (Exception e) { /* ignore */ }
                }
            });
        }
    }

    private static String quote(String s) {
        try {
            return JSONObject.quote(s == null ? "" : s);
        } catch (Exception e) {
            return "\"\"";
        }
    }

    private static byte[] decodeBase64(String s) {
        if (s == null) return null;
        String v = s.trim();
        if (v.startsWith("data:")) {
            int comma = v.indexOf(',');
            if (comma > 0) v = v.substring(comma + 1);
        }
        v = v.replace("\r", "").replace("\n", "").replace(" ", "");
        if (v.length() == 0) return null;
        return Base64.decode(v, Base64.DEFAULT);
    }

    private void postNotification(String title, String body, String tag) {
        NotificationManager nm = (NotificationManager) activity.getSystemService(Context.NOTIFICATION_SERVICE);
        if (nm == null) return;

        if (Build.VERSION.SDK_INT >= 33) {
            if (activity.checkSelfPermission("android.permission.POST_NOTIFICATIONS")
                    != PackageManager.PERMISSION_GRANTED) {
                return; // 没授权就静默丢弃，不报错
            }
        }

        if (Build.VERSION.SDK_INT >= 26) {
            NotificationChannel ch = new NotificationChannel(
                    CHANNEL_ID, "日程提醒", NotificationManager.IMPORTANCE_HIGH);
            nm.createNotificationChannel(ch);
        }

        Notification.Builder b;
        if (Build.VERSION.SDK_INT >= 26) {
            b = new Notification.Builder(activity, CHANNEL_ID);
        } else {
            b = new Notification.Builder(activity);
        }

        Intent open = new Intent(activity, MainActivity.class);
        open.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        PendingIntent pi = PendingIntent.getActivity(activity, 0, open,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);

        b.setSmallIcon(android.R.drawable.ic_dialog_info)
                .setContentTitle(title)
                .setContentText(body)
                .setAutoCancel(true)
                .setContentIntent(pi);

        try {
            nm.notify(tag != null ? tag.hashCode() : 0, b.build());
        } catch (Exception e) { /* ignore */ }
    }
}
