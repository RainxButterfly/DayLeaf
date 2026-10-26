/**
 * 后台提醒调度：把网页同步过来的提醒/闹钟排进系统 AlarmManager。
 * 关闭应用后仍由系统唤醒广播；开机后自动重排。
 *
 * 作者名：StarLeaf·Roxy
 * 创建日期：2026-10-26
 */
package com.dayleaf.app;

import android.app.AlarmManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.os.Build;

import org.json.JSONArray;
import org.json.JSONObject;

public final class ReminderScheduler {

    public static final String PREFS = "dayleaf";
    public static final String KEY = "native_reminders";
    public static final String ACTION = "com.dayleaf.app.REMINDER";

    private ReminderScheduler() { }

    /** JS 同步的完整载荷：{enabled:boolean, items:[{id,at,title,body,kind}]} */
    public static synchronized void apply(Context context, String payload) {
        if (payload == null || payload.length() == 0) payload = "{\"enabled\":false,\"items\":[]}";
        SharedPreferences sp = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        String old = sp.getString(KEY, "");
        cancelPayload(context, old);

        boolean enabled = false;
        JSONArray items = null;
        try {
            JSONObject o = new JSONObject(payload);
            enabled = o.optBoolean("enabled", false);
            items = o.optJSONArray("items");
        } catch (Exception e) {
            enabled = false;
        }

        sp.edit().putString(KEY, payload).apply();
        if (!enabled || items == null) return;

        long now = System.currentTimeMillis();
        for (int i = 0; i < items.length(); i++) {
            JSONObject it = items.optJSONObject(i);
            if (it == null) continue;
            long at = it.optLong("at", 0);
            if (at <= now + 1000) continue;
            schedule(context, it.optString("id", "r" + i), at,
                    it.optString("title", "日程提醒"),
                    it.optString("body", ""),
                    it.optString("kind", "rm"));
        }
    }

    /** 开机/时区变化后，用上次保存的载荷重排。 */
    public static synchronized void rescheduleStored(Context context) {
        SharedPreferences sp = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        String payload = sp.getString(KEY, "");
        if (payload == null || payload.length() == 0) return;
        apply(context, payload);
    }

    private static void cancelPayload(Context context, String payload) {
        if (payload == null || payload.length() == 0) return;
        try {
            JSONObject o = new JSONObject(payload);
            JSONArray items = o.optJSONArray("items");
            if (items == null) return;
            AlarmManager am = (AlarmManager) context.getSystemService(Context.ALARM_SERVICE);
            if (am == null) return;
            for (int i = 0; i < items.length(); i++) {
                JSONObject it = items.optJSONObject(i);
                if (it == null) continue;
                String id = it.optString("id", "");
                if (id.length() == 0) continue;
                PendingIntent pi = pending(context, id, null, null, null, null,
                        PendingIntent.FLAG_NO_CREATE | PendingIntent.FLAG_IMMUTABLE);
                if (pi != null) {
                    am.cancel(pi);
                    pi.cancel();
                }
            }
        } catch (Exception e) {
            /* 旧载荷坏了就跳过 */
        }
    }

    private static void schedule(Context context, String id, long at,
                                 String title, String body, String kind) {
        AlarmManager am = (AlarmManager) context.getSystemService(Context.ALARM_SERVICE);
        if (am == null) return;
        PendingIntent pi = pending(context, id, at, title, body, kind,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        if (pi == null) return;

        try {
            if (Build.VERSION.SDK_INT >= 31) {
                if (am.canScheduleExactAlarms()) {
                    am.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, pi);
                } else {
                    am.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, pi);
                }
            } else if (Build.VERSION.SDK_INT >= 23) {
                am.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, pi);
            } else {
                am.setExact(AlarmManager.RTC_WAKEUP, at, pi);
            }
        } catch (SecurityException e) {
            try {
                if (Build.VERSION.SDK_INT >= 23) {
                    am.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, pi);
                } else {
                    am.set(AlarmManager.RTC_WAKEUP, at, pi);
                }
            } catch (Exception ignore) { /* ignore */ }
        } catch (Exception e) {
            /* ignore */
        }
    }

    private static PendingIntent pending(Context context, String id, Long at,
                                         String title, String body, String kind, int flags) {
        Intent i = new Intent(context, ReminderReceiver.class);
        i.setAction(ACTION);
        i.putExtra("id", id);
        i.putExtra("title", title == null ? "日程提醒" : title);
        i.putExtra("body", body == null ? "" : body);
        i.putExtra("kind", kind == null ? "rm" : kind);
        if (at != null) i.putExtra("at", at.longValue());
        int req = requestCode(id);
        try {
            return PendingIntent.getBroadcast(context, req, i, flags);
        } catch (Exception e) {
            return null;
        }
    }

    private static int requestCode(String id) {
        int h = id == null ? 0 : id.hashCode();
        return h == Integer.MIN_VALUE ? 1 : Math.abs(h);
    }
}
