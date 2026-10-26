/**
 * 后台提醒广播：应用没开时由 AlarmManager 唤起，直接发系统通知。
 * 通知上带「完成」「10 分钟后提醒」两个操作。
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

    /** 「完成」：只取消通知 */
    public static final String ACTION_DONE = "com.dayleaf.app.REMINDER_DONE";
    /** 「10 分钟后提醒」：排一个一次性的延后闹钟 */
    public static final String ACTION_SNOOZE = "com.dayleaf.app.REMINDER_SNOOZE";

    private static final long SNOOZE_MS = 10L * 60L * 1000L;

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

        String action = intent.getAction();

        if (ACTION_DONE.equals(action)) {
            cancelNotification(context, id);
            return;
        }

        if (ACTION_SNOOZE.equals(action)) {
            try {
                ReminderScheduler.scheduleSnooze(context, id,
                        System.currentTimeMillis() + SNOOZE_MS,
                        title, body, kind);
            } catch (Exception e) { /* 排不上就算了，至少别崩 */ }
            cancelNotification(context, id);
            return;
        }

        showReminder(context, id, title, body, kind, true);
    }

    private static void cancelNotification(Context context, String id) {
        try {
            NotificationManager nm = (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
            if (nm != null) nm.cancel(notificationId(id));
        } catch (Exception e) { /* ignore */ }
    }

    /** 通知 id 与旧版本保持一致（id.hashCode()），升级后不会多出一条重复提醒 */
    public static int notificationId(String id) {
        return id == null ? 0 : id.hashCode();
    }

    /**
     * 发一条提醒通知（前台提醒与开机补发共用）。
     *
     * @return 是否真的发出去了（没通知权限 / 没通知服务时返回 false），
     *         开机补发据此决定要不要记为「已补发」，权限到手后还有机会补。
     */
    public static boolean showReminder(Context context, String id, String title, String body,
                                       String kind, boolean withActions) {
        if (context == null) return false;
        if (title == null || title.length() == 0) title = "日程提醒";
        if (body == null) body = "";
        if (id == null) id = "reminder";
        if (kind == null) kind = "rm";

        NotificationManager nm = (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
        if (nm == null) return false;

        if (Build.VERSION.SDK_INT >= 33) {
            try {
                if (context.checkSelfPermission("android.permission.POST_NOTIFICATIONS")
                        != PackageManager.PERMISSION_GRANTED) {
                    return false;
                }
            } catch (Exception e) {
                return false;
            }
        }

        boolean alarm = "al".equals(kind);
        String channel = alarm ? CH_AL : CH_RM;
        ensureChannel(nm, channel, alarm);

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

        if (withActions) {
            String doneLabel = context.getString(R.string.notif_action_done);
            String snoozeLabel = context.getString(R.string.notif_action_snooze);
            Notification.Action done = new Notification.Action.Builder(
                    android.R.drawable.ic_menu_close_clear_cancel,
                    doneLabel,
                    actionIntent(context, ACTION_DONE, id, title, body, kind,
                            ReminderScheduler.SLOT_DONE))
                    .build();
            Notification.Action snooze = new Notification.Action.Builder(
                    android.R.drawable.ic_menu_recent_history,
                    snoozeLabel,
                    actionIntent(context, ACTION_SNOOZE, id, title, body, kind,
                            ReminderScheduler.SLOT_SNOOZE))
                    .build();
            try {
                b.addAction(done);
                b.addAction(snooze);
            } catch (Exception e) { /* 老内核不支持就不加按钮，通知照发 */ }
        }

        if (Build.VERSION.SDK_INT < 26) {
            b.setPriority(Notification.PRIORITY_HIGH);
            b.setDefaults(Notification.DEFAULT_SOUND);
            if (alarm) b.setVibrate(new long[]{0, 500, 220, 500});
        }

        try {
            nm.notify(notificationId(id), b.build());
            return true;
        } catch (Exception e) {
            return false; /* 发失败：调用方（开机补发）不记为已补，下次还有机会 */
        }
    }

    /** 建一次通道即可；已存在的通道保持用户设置（声音/震动/重要性）不变 */
    private static void ensureChannel(NotificationManager nm, String channel, boolean alarm) {
        if (Build.VERSION.SDK_INT < 26) return;
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

    /** 操作按钮的广播：requestCode = 提醒 id 派生 + 固定槽位，互不覆盖 */
    private static PendingIntent actionIntent(Context context, String action, String id,
                                              String title, String body, String kind, int slot) {
        Intent i = new Intent(context, ReminderReceiver.class);
        i.setAction(action);
        i.putExtra("id", id);
        i.putExtra("title", title);
        i.putExtra("body", body);
        i.putExtra("kind", kind);
        return PendingIntent.getBroadcast(context,
                ReminderScheduler.requestCodeFor(id, slot), i,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
    }
}
