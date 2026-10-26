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
import android.provider.AlarmClock;
import android.provider.Settings;
import android.util.Base64;
import android.widget.Toast;
import android.webkit.JavascriptInterface;

import org.json.JSONArray;
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
        try { TodayWidgetProvider.saveItems(activity, json); } catch (Exception e) { /* ignore */ }
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                try { NextWidgetProvider.updateAll(activity); } catch (Exception e) { /* ignore */ }
                try { TodayWidgetProvider.updateAll(activity); } catch (Exception e) { /* ignore */ }
            }
        });
    }

    /** 请求把「下一项」小组件钉到桌面（Android 8+ 支持；不支持的启动器会提示手动添加） */
    @JavascriptInterface
    public void pinNextWidget() {
        pinWidget(NextWidgetProvider.class);
    }

    /** 请求把「今日日程」小组件钉到桌面 */
    @JavascriptInterface
    public void pinTodayWidget() {
        pinWidget(TodayWidgetProvider.class);
    }

    /** 请求把「快速新建」小组件钉到桌面 */
    @JavascriptInterface
    public void pinQuickWidget() {
        pinWidget(QuickWidgetProvider.class);
    }

    private void pinWidget(final Class<?> provider) {
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                try {
                    if (Build.VERSION.SDK_INT < 26) {
                        Toast.makeText(activity, "长按桌面空白处 → 小组件 → 叶历", Toast.LENGTH_LONG).show();
                        return;
                    }
                    android.appwidget.AppWidgetManager manager =
                            android.appwidget.AppWidgetManager.getInstance(activity);
                    if (manager == null || !manager.isRequestPinAppWidgetSupported()) {
                        Toast.makeText(activity, "桌面长按空白处 → 小组件 → 叶历", Toast.LENGTH_LONG).show();
                        return;
                    }
                    android.content.ComponentName cn = new android.content.ComponentName(activity, provider);
                    manager.requestPinAppWidget(cn, null, null);
                    Toast.makeText(activity, "请在桌面确认添加", Toast.LENGTH_SHORT).show();
                } catch (Exception e) {
                    Toast.makeText(activity, "桌面长按空白处 → 小组件 → 叶历", Toast.LENGTH_LONG).show();
                }
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

    /**
     * 局域网同步（手机作为客户端）：
     *   lanPush(url, token, body) → PUT /sync
     *   lanPull(url, token)       → GET /sync
     * 结束后回调 window.__dlLanResult(op, ok, data, error)。
     */
    @JavascriptInterface
    public void lanPush(final String url, final String token, final String body) {
        lanRequest("push", url, token, body);
    }

    @JavascriptInterface
    public void lanPull(final String url, final String token) {
        lanRequest("pull", url, token, "");
    }

    /** 只检查「这个地址是不是这台叶历电脑端」，不校验配对码 */
    @JavascriptInterface
    public void lanPing(final String url) {
        lanRequest("ping", url, "", "");
    }

    private void lanRequest(final String op, final String url, final String token, final String body) {
        if (!SYNCING.compareAndSet(false, true)) {
            finishLan(op, false, "", "同步进行中，请稍后再试");
            return;
        }
        new Thread(new Runnable() {
            @Override public void run() {
                String data = "";
                boolean ok = false;
                String err = "";
                HttpURLConnection conn = null;
                try {
                    String u = url == null ? "" : url.trim();
                    if (!u.startsWith("http://") && !u.startsWith("https://")) {
                        err = "地址要以 http:// 开头";
                    } else {
                        if ("ping".equals(op)) {
                            u = u.replaceAll("/sync$", "").replaceAll("/+$", "") + "/ping";
                        } else if (u.indexOf("/sync") < 0) {
                            u = u.replaceAll("/+$", "") + "/sync";
                        }
                        conn = (HttpURLConnection) new URL(u).openConnection();
                        conn.setConnectTimeout(6000);
                        conn.setReadTimeout(20000);
                        conn.setUseCaches(false);
                        conn.setRequestProperty("Accept", "application/json");
                        conn.setRequestProperty("X-DayLeaf-Token", token == null ? "" : token);
                        if ("ping".equals(op)) {
                            conn.setRequestMethod("GET");
                            int code = conn.getResponseCode();
                            if (code >= 200 && code < 300) {
                                data = readLimited(conn.getInputStream());
                                ok = true;
                            } else err = code == 404 ? "这个地址不是叶历电脑端" : lanError(code);
                        } else if ("push".equals(op)) {
                            conn.setRequestMethod("PUT");
                            conn.setDoOutput(true);
                            conn.setRequestProperty("Content-Type", "application/json; charset=utf-8");
                            byte[] payload = (body == null ? "" : body).getBytes("UTF-8");
                            conn.setFixedLengthStreamingMode(payload.length);
                            OutputStream os = conn.getOutputStream();
                            os.write(payload);
                            os.flush();
                            os.close();
                            int code = conn.getResponseCode();
                            ok = code >= 200 && code < 300;
                            if (ok) {
                                try { data = readLimited(conn.getInputStream()); } catch (Exception ignore) { data = ""; }
                            } else err = lanError(code);
                        } else {
                            conn.setRequestMethod("GET");
                            int code = conn.getResponseCode();
                            if (code >= 200 && code < 300) {
                                data = readLimited(conn.getInputStream());
                                ok = true;
                            } else err = lanError(code);
                        }
                    }
                } catch (Exception e) {
                    err = lanFriendlyError(e);
                    ok = false;
                } finally {
                    try { if (conn != null) conn.disconnect(); } catch (Exception e) { /* ignore */ }
                    SYNCING.set(false);
                }
                finishLan(op, ok, data, err);
            }
        }, "dayleaf-lan").start();
    }

    /** 不把 java.net 的原始异常抛给用户；校园网/AP 隔离最常见 */
    private static String lanFriendlyError(Exception e) {
        String msg = e == null ? "" : String.valueOf(e.getMessage());
        String low = msg.toLowerCase();
        if (e instanceof java.net.UnknownHostException) return "地址无法解析，请检查电脑地址是否写对";
        if (e instanceof java.net.SocketTimeoutException || low.contains("timed out")) {
            return "连接超时：请确认手机和电脑在同一 Wi‑Fi、电脑防火墙允许 17890，并用电脑 Wi‑Fi/以太网卡的 IPv4";
        }
        if (e instanceof java.net.ConnectException || low.contains("failed to connect")
                || low.contains("connection refused") || low.contains("econnrefused")) {
            return "连不上电脑：请确认同一 Wi‑Fi、电脑已开启局域网同步并允许防火墙；校园网若开了 AP 隔离，手机和电脑可能互相不可见";
        }
        if (low.contains("network is unreachable") || low.contains("no route to host")) {
            return "网络不可达：手机和电脑可能不在同一个 Wi‑Fi/子网，或校园网隔离了设备互访";
        }
        return "网络错误，请检查 Wi‑Fi 与电脑地址（不要使用 192.168.x.1 这类虚拟网卡地址）";
    }

    private static String lanError(int code) {
        if (code == 401) return "配对码不对（401）";
        if (code == 404) return "电脑端没有开启局域网同步（404）";
        if (code == 413) return "数据超过 8MB";
        return "电脑端返回 HTTP " + code;
    }

    private void finishLan(String op, boolean ok, String data, String err) {
        String js = "(function(){try{if(window.__dlLanResult){window.__dlLanResult("
                + quote(op == null ? "pull" : op) + ","
                + (ok ? "true" : "false") + ","
                + quote(data == null ? "" : data) + ","
                + quote(err == null ? "" : err)
                + ");}}catch(e){}})();";
        if (activity instanceof MainActivity) {
            ((MainActivity) activity).runJs(js);
        }
    }

    /**
     * 写入手机系统闹钟（时钟 App）：
     * hour/minute 为 24 小时制；daysJson 是 [1..7]（1=周日 … 7=周六），空数组表示只响一次。
     * 需要 manifest 里的 com.android.alarm.permission.SET_ALARM。
     */
    @JavascriptInterface
    public void setSystemAlarm(final int hour, final int minute, final String label, final String daysJson) {
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                try {
                    int[] days = null;
                    try {
                        JSONArray arr = new JSONArray(daysJson == null || daysJson.length() == 0 ? "[]" : daysJson);
                        if (arr.length() > 0) {
                            days = new int[arr.length()];
                            for (int i = 0; i < arr.length(); i++) days[i] = arr.optInt(i);
                        }
                    } catch (Exception e) { days = null; }

                    Intent i = new Intent(AlarmClock.ACTION_SET_ALARM);
                    i.putExtra(AlarmClock.EXTRA_HOUR, Math.max(0, Math.min(23, hour)));
                    i.putExtra(AlarmClock.EXTRA_MINUTES, Math.max(0, Math.min(59, minute)));
                    i.putExtra(AlarmClock.EXTRA_MESSAGE, label == null ? "叶历日程" : label);
                    if (days != null) i.putExtra(AlarmClock.EXTRA_DAYS, days);
                    i.putExtra(AlarmClock.EXTRA_SKIP_UI, true);
                    i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                    try {
                        activity.startActivity(i);
                    } catch (Exception first) {
                        i.putExtra(AlarmClock.EXTRA_SKIP_UI, false);
                        activity.startActivity(i);
                    }
                    Toast.makeText(activity, "已写入系统时钟", Toast.LENGTH_SHORT).show();
                } catch (Exception e) {
                    Toast.makeText(activity, "这台手机没有可用的时钟 App", Toast.LENGTH_LONG).show();
                }
            }
        });
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
