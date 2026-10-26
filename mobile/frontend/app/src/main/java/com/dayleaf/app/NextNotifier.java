/**
 * 「下一项」常驻通知：低重要性、无声无振动，点击回到应用。
 * 由 JS 通过 syncReminders 的 nextOn / next 字段驱动。
 *
 * 作者名：StarLeaf·Roxy
 * 创建日期：2026-10-26
 */
package com.dayleaf.app;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.os.Build;

import org.json.JSONObject;

public final class NextNotifier {

    public static final String CHANNEL = "dayleaf_next";
    public static final int ID = 73001;

    private static final int REQ_OPEN = 73101;

    private NextNotifier() { }

    /** 从 prefs 里存的完整载荷刷新（开机后用） */
    public static void updateFromStored(Context context) {
        try {
            SharedPreferences sp = context.getSharedPreferences(ReminderScheduler.PREFS, Context.MODE_PRIVATE);
            String payload = sp.getString(ReminderScheduler.KEY, "");
            update(context, payload);
        } catch (Exception e) { /* ignore */ }
    }

    /** payload 为空/nextOn 不成立时只做取消 */
    public static void update(Context context, String payload) {
        if (context == null) return;
        JSONObject next = null;
        boolean on = false;
        if (payload != null && payload.length() > 0) {
            try {
                JSONObject o = new JSONObject(payload);
                on = o.optBoolean("nextOn", false);
                next = o.optJSONObject("next");
            } catch (Exception e) { /* 坏载荷 = 关 */ }
        }

        if (!on || next == null) {
            cancel(context);
            return;
        }
        String title = safe(next.optString("title", ""));
        String time = safe(next.optString("time", ""));
        String body = safe(next.optString("body", ""));
        if (title.length() == 0 && time.length() == 0) {
            cancel(context);
            return;
        }
        post(context, title, time, body);
    }

    public static void cancel(Context context) {
        try {
            NotificationManager nm = (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
            if (nm != null) nm.cancel(ID);
        } catch (Exception e) { /* ignore */ }
    }

    private static void post(Context context, String title, String time, String body) {
        NotificationManager nm = (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
        if (nm == null) return;

        if (Build.VERSION.SDK_INT >= 33) {
            try {
                if (context.checkSelfPermission("android.permission.POST_NOTIFICATIONS")
                        != PackageManager.PERMISSION_GRANTED) {
                    return;
                }
            } catch (Exception e) {
                return;
            }
        }

        if (Build.VERSION.SDK_INT >= 26) {
            try {
                NotificationChannel ch = new NotificationChannel(
                        CHANNEL, "下一项", NotificationManager.IMPORTANCE_LOW);
                ch.setSound(null, null);
                ch.enableVibration(false);
                ch.setShowBadge(false);
                nm.createNotificationChannel(ch);
            } catch (Exception e) { /* ignore */ }
        }

        String head = title.length() == 0 ? "下一项" : title;
        if (time.length() > 0) head = time + "  " + head;
        String text = body;

        Intent open = new Intent(context, MainActivity.class);
        open.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        PendingIntent pi = PendingIntent.getActivity(context, REQ_OPEN, open,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);

        Notification.Builder b;
        if (Build.VERSION.SDK_INT >= 26) b = new Notification.Builder(context, CHANNEL);
        else b = new Notification.Builder(context);

        b.setSmallIcon(android.R.drawable.ic_dialog_info)
                .setContentTitle(head)
                .setContentText(text)
                .setOngoing(true)
                .setOnlyAlertOnce(true)
                .setShowWhen(false)
                .setContentIntent(pi)
                .setCategory(Notification.CATEGORY_STATUS);
        if (text != null && text.length() > 0) {
            b.setStyle(new Notification.BigTextStyle().bigText(text));
        }

        if (Build.VERSION.SDK_INT < 26) {
            b.setPriority(Notification.PRIORITY_LOW);
            b.setDefaults(0);
        }

        try {
            nm.notify(ID, b.build());
        } catch (Exception e) { /* ignore */ }
    }

    private static String safe(String s) {
        return s == null ? "" : s.trim();
    }
}
