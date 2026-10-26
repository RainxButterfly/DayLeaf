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

import java.util.Iterator;

public final class ReminderScheduler {

    public static final String PREFS = "dayleaf";
    public static final String KEY = "native_reminders";
    public static final String KEY_CATCHUP = "catchup_fired";
    public static final String ACTION = "com.dayleaf.app.REMINDER";

    /** 开机补发窗口：只补 12 小时内错过的提醒 */
    public static final long CATCHUP_WINDOW_MS = 12L * 60L * 60L * 1000L;
    /** 补发记录保留 3 天，避免无限膨胀 */
    private static final long CATCHUP_KEEP_MS = 3L * 24L * 60L * 60L * 1000L;

    /* 同一个提醒要占多个 PendingIntent 号段，避免互相覆盖 */
    public static final int SLOT_ALARM = 0;
    public static final int SLOT_DONE = 1;
    public static final int SLOT_SNOOZE = 2;

    private ReminderScheduler() { }

    /** JS 同步的完整载荷：{enabled:boolean, items:[{id,at,title,body,kind}], nextOn, next} */
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

        /* 常驻「下一项」通知：nextOn / next 是可选的，缺失即取消 */
        try { NextNotifier.update(context, payload); } catch (Exception e) { /* ignore */ }

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

    /**
     * 「10 分钟后提醒」：按原 id 派生一个一次性闹钟。
     * 用 at 拼进 id，重复点也不会把旧闹钟顶掉。
     */
    public static void scheduleSnooze(Context context, String id, long at,
                                      String title, String body, String kind) {
        String base = (id == null || id.length() == 0) ? "reminder" : id;
        schedule(context, base + "#sn" + at, at, title, body, kind);
    }

    /**
     * 开机 / 改时间 / 换时区后补发：过去 12 小时内错过、且还没补过的提醒各发一条。
     * 已补发的 id 记在 prefs（catchup_fired），避免每次开机重复弹。
     */
    public static synchronized void catchUpMissed(Context context) {
        SharedPreferences sp = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        String payload = sp.getString(KEY, "");
        if (payload == null || payload.length() == 0) return;

        JSONObject o;
        try { o = new JSONObject(payload); } catch (Exception e) { return; }
        if (!o.optBoolean("enabled", false)) return;
        JSONArray items = o.optJSONArray("items");
        if (items == null) return;

        JSONObject fired;
        try { fired = new JSONObject(sp.getString(KEY_CATCHUP, "{}")); } catch (Exception e) { fired = new JSONObject(); }

        long now = System.currentTimeMillis();
        long cutoff = now - CATCHUP_WINDOW_MS;

        /* 先清理过期记录（3 天前的），再往里塞新的 */
        JSONObject next = new JSONObject();
        try {
            Iterator<String> keys = fired.keys();
            while (keys.hasNext()) {
                String k = keys.next();
                long t = fired.optLong(k, 0);
                if (t <= 0 || t < now - CATCHUP_KEEP_MS) continue;
                try { next.put(k, t); } catch (Exception ignore) { /* ignore */ }
            }
        } catch (Exception e) { /* ignore */ }

        for (int i = 0; i < items.length(); i++) {
            JSONObject it = items.optJSONObject(i);
            if (it == null) continue;
            long at = it.optLong("at", 0);
            if (at <= 0 || at > now || at < cutoff) continue;

            String id = it.optString("id", "r" + i);
            String key = id + "@" + at;
            if (next.has(key)) continue; /* 这次开机已经补过 */

            boolean shown;
            try {
                shown = ReminderReceiver.showReminder(context,
                        id,
                        it.optString("title", "日程提醒"),
                        it.optString("body", ""),
                        it.optString("kind", "rm"),
                        true);
            } catch (Exception e) {
                continue; /* 异常：不记为已补发，下次还有机会 */
            }
            if (!shown) continue; /* 没通知权限 / 发失败：不记为已补发，下次还有机会 */
            try { next.put(key, at); } catch (Exception ignore) { /* ignore */ }
        }

        sp.edit().putString(KEY_CATCHUP, next.toString()).apply();
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
                /* 老版本用的是 id.hashCode() 作为 requestCode，升级后这里顺手清掉 */
                PendingIntent legacy = pendingLegacy(context, id);
                if (legacy != null) {
                    am.cancel(legacy);
                    legacy.cancel();
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
        int req = requestCodeFor(id, SLOT_ALARM);
        try {
            return PendingIntent.getBroadcast(context, req, i, flags);
        } catch (Exception e) {
            return null;
        }
    }

    /** 老版本 requestCode（只用于升级后清理残留闹钟） */
    private static PendingIntent pendingLegacy(Context context, String id) {
        try {
            Intent i = new Intent(context, ReminderReceiver.class);
            i.setAction(ACTION);
            return PendingIntent.getBroadcast(context, legacyRequestCode(id), i,
                    PendingIntent.FLAG_NO_CREATE | PendingIntent.FLAG_IMMUTABLE);
        } catch (Exception e) {
            return null;
        }
    }

    /**
     * 每个提醒占 4 个号：0=定时广播，1=完成按钮，2=延后按钮，3=预留。
     * 这样两个互不相干的事件（如 id 只差一位）也不会撞号。
     */
    public static int requestCodeFor(String id, int slot) {
        int h = id == null ? 0 : id.hashCode();
        if (h == Integer.MIN_VALUE) h = 1;
        /* 掐到 29 位再乘 4，防止溢出成负数 */
        int base = Math.abs(h) & 0x1FFFFFFF;
        return base * 4 + (slot & 3);
    }

    private static int legacyRequestCode(String id) {
        int h = id == null ? 0 : id.hashCode();
        return h == Integer.MIN_VALUE ? 1 : Math.abs(h);
    }
}
