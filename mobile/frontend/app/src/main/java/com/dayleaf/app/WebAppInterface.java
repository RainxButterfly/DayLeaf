/**
 * 暴露给 JS 的原生桥：通知、震动、检查更新、版本号。
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
import android.os.Build;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.provider.Settings;
import android.webkit.JavascriptInterface;

public class WebAppInterface {

    private static final String CHANNEL_ID = "dayleaf_remind";
    private static final int REQ_NOTIFY_PERM = 1001;

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
