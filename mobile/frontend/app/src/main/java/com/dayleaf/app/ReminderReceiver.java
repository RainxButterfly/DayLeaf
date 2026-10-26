/**
 * 后台提醒广播：应用没开时由 AlarmManager 唤起，直接发系统通知。
 *
 * 作者名：StarLeaf·Roxy
 * 创建日期：2026-10-26
 */
package com.dayleaf.app;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.os.Build;

public class ReminderReceiver extends BroadcastReceiver {

    private static final String CH_RM = "dayleaf_bg_remind";
    private static final String CH_AL = "dayleaf_bg_alarm";

    @Override
    public void onReceive(Context context, Intent intent) {
        if (intent == null) return;
        String id = intent.getStringExtra("id");
        String title = intent.getStringExtra("title");
        String body = intent.getStringExtra("body");
        String kind = intent.getStringExtra("kind");
        if (title == null || title.length() == 0) title = "日程提醒";
        if (body == null) body = "";
        if (id == null) id = "reminder";
        if (kind == null) kind = "rm";

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

        boolean alarm = "al".equals(kind);
        String channel = alarm ? CH_AL : CH_RM;
        if (Build.VERSION.SDK_INT >= 26) {
            try {
                NotificationChannel ch = new NotificationChannel(
                        channel,
                        alarm ? "日程闹钟" : "日程提醒",
                        NotificationManager.IMPORTANCE_HIGH);
                ch.enableVibration(true);
                if (alarm) ch.setVibrationPattern(new long[]{0, 500, 220, 500});
                ch.setShowBadge(true);
                nm.createNotificationChannel(ch);
            } catch (Exception e) { /* ignore */ }
        }

        Intent open = new Intent(context, MainActivity.class);
        open.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        PendingIntent pi = PendingIntent.getActivity(context, 0, open,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);

        Notification.Builder b;
        if (Build.VERSION.SDK_INT >= 26) b = new Notification.Builder(context, channel);
        else b = new Notification.Builder(context);

        b.setSmallIcon(android.R.drawable.ic_dialog_info)
                .setContentTitle(title)
                .setContentText(body)
                .setStyle(new Notification.BigTextStyle().bigText(body))
                .setAutoCancel(true)
                .setContentIntent(pi)
                .setWhen(System.currentTimeMillis())
                .setShowWhen(true);

        if (Build.VERSION.SDK_INT < 26) {
            b.setPriority(Notification.PRIORITY_HIGH);
            b.setDefaults(Notification.DEFAULT_SOUND);
            if (alarm) b.setVibrate(new long[]{0, 500, 220, 500});
        }

        try {
            nm.notify(id.hashCode(), b.build());
        } catch (Exception e) { /* ignore */ }
    }
}
