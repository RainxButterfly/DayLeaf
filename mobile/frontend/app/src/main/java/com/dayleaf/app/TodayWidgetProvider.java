/**
 * 桌面小组件「今日日程」：4x2，显示今天前 4 条日程，点击回到应用。
 * 数据由 JS 通过 DayLeafNative.updateWidget(json) 推过来，存在 prefs 的 widget_today。
 *
 * 作者名：StarLeaf·Roxy
 * 创建日期：2026-10-26
 */
package com.dayleaf.app;

import android.app.PendingIntent;
import android.appwidget.AppWidgetManager;
import android.appwidget.AppWidgetProvider;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.view.View;
import android.widget.RemoteViews;

import org.json.JSONArray;
import org.json.JSONObject;

public class TodayWidgetProvider extends AppWidgetProvider {

    public static final String KEY_WIDGET_TODAY = "widget_today";
    private static final int REQ_OPEN = 76002;
    private static final int MAX_LINES = 4;

    @Override
    public void onUpdate(Context context, AppWidgetManager manager, int[] appWidgetIds) {
        if (appWidgetIds == null || appWidgetIds.length == 0) return;
        RemoteViews rv = build(context);
        for (int id : appWidgetIds) {
            try { manager.updateAppWidget(id, rv); } catch (Exception e) { /* ignore */ }
        }
    }

    public static void updateAll(Context context) {
        try {
            AppWidgetManager manager = AppWidgetManager.getInstance(context);
            if (manager == null) return;
            ComponentName cn = new ComponentName(context, TodayWidgetProvider.class);
            int[] ids = manager.getAppWidgetIds(cn);
            if (ids == null || ids.length == 0) return;
            RemoteViews rv = build(context);
            for (int id : ids) {
                try { manager.updateAppWidget(id, rv); } catch (Exception e) { /* ignore */ }
            }
        } catch (Exception e) { /* ignore */ }
    }

    /** 存一份精简载荷：{today:[{time,title}],todayLabel:"10月26日 · 周一"} */
    public static void saveItems(Context context, String json) {
        String compact = "";
        try {
            JSONObject o = new JSONObject(json == null ? "{}" : json);
            JSONArray arr = o.optJSONArray("today");
            JSONObject out = new JSONObject();
            JSONArray keep = new JSONArray();
            if (arr != null) {
                for (int i = 0; i < arr.length() && keep.length() < MAX_LINES; i++) {
                    JSONObject it = arr.optJSONObject(i);
                    if (it == null) continue;
                    String time = trim(it.optString("time", ""));
                    String title = trim(it.optString("title", ""));
                    if (time.length() == 0 && title.length() == 0) continue;
                    JSONObject one = new JSONObject();
                    one.put("time", time);
                    one.put("title", title);
                    keep.put(one);
                }
            }
            out.put("today", keep);
            out.put("todayLabel", trim(o.optString("todayLabel", "")));
            compact = out.toString();
        } catch (Exception e) {
            compact = "";
        }
        try {
            SharedPreferences sp = context.getSharedPreferences(ReminderScheduler.PREFS, Context.MODE_PRIVATE);
            sp.edit().putString(KEY_WIDGET_TODAY, compact).apply();
        } catch (Exception e) { /* ignore */ }
    }

    private static RemoteViews build(Context context) {
        RemoteViews rv = new RemoteViews(context.getPackageName(), R.layout.widget_today);
        int[] rowIds = { R.id.widget_today_row1, R.id.widget_today_row2, R.id.widget_today_row3, R.id.widget_today_row4 };
        int[] timeIds = { R.id.widget_today_t1, R.id.widget_today_t2, R.id.widget_today_t3, R.id.widget_today_t4 };
        int[] nameIds = { R.id.widget_today_n1, R.id.widget_today_n2, R.id.widget_today_n3, R.id.widget_today_n4 };

        JSONArray arr = null;
        String head = "";
        try {
            SharedPreferences sp = context.getSharedPreferences(ReminderScheduler.PREFS, Context.MODE_PRIVATE);
            String raw = sp.getString(KEY_WIDGET_TODAY, "");
            if (raw != null && raw.length() > 0) {
                if (raw.trim().startsWith("{")) {
                    JSONObject o = new JSONObject(raw);
                    arr = o.optJSONArray("today");
                    head = trim(o.optString("todayLabel", ""));
                } else {
                    /* 兼容旧版按行存的字符串 */
                    arr = new JSONArray();
                    for (String line : raw.split("\\n")) {
                        JSONObject one = new JSONObject();
                        one.put("time", "");
                        one.put("title", line);
                        arr.put(one);
                    }
                }
            }
        } catch (Exception e) { arr = null; }

        int n = arr == null ? 0 : arr.length();
        rv.setTextViewText(R.id.widget_today_label, context.getString(R.string.widget_today_label));
        rv.setTextViewText(R.id.widget_today_head, head.length() == 0 ? "今天" : head);

        for (int i = 0; i < MAX_LINES; i++) {
            if (i < n) {
                JSONObject one = arr.optJSONObject(i);
                String time = one == null ? "" : trim(one.optString("time", ""));
                String title = one == null ? "" : trim(one.optString("title", ""));
                rv.setTextViewText(timeIds[i], time);
                rv.setTextViewText(nameIds[i], title);
                rv.setViewVisibility(rowIds[i], View.VISIBLE);
            } else {
                rv.setViewVisibility(rowIds[i], View.GONE);
            }
        }
        if (n == 0) {
            rv.setTextViewText(timeIds[0], "");
            rv.setTextViewText(nameIds[0], context.getString(R.string.widget_today_empty));
            rv.setViewVisibility(rowIds[0], View.VISIBLE);
        }

        Intent open = new Intent(context, MainActivity.class);
        open.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        PendingIntent pi = PendingIntent.getActivity(context, REQ_OPEN, open,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        rv.setOnClickPendingIntent(R.id.widget_today_root, pi);
        return rv;
    }

    private static String trim(String s) {
        return s == null ? "" : s.trim();
    }
}