/**
 * 桌面小组件「下一项」：2x1，显示最近一项的标题与时间，点击回到应用。
 * 数据由 JS 通过 DayLeafNative.updateWidget(json) 推过来，存在 prefs 的 widget_next。
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
import android.widget.RemoteViews;

import org.json.JSONObject;

public class NextWidgetProvider extends AppWidgetProvider {

    /** 存在 dayleaf prefs 里的紧凑字符串（JSON：{title,time,body}） */
    public static final String KEY_WIDGET_NEXT = "widget_next";
    private static final int REQ_OPEN = 76001;

    @Override
    public void onUpdate(Context context, AppWidgetManager manager, int[] appWidgetIds) {
        if (appWidgetIds == null || appWidgetIds.length == 0) return;
        RemoteViews rv = build(context);
        for (int id : appWidgetIds) {
            try { manager.updateAppWidget(id, rv); } catch (Exception e) { /* ignore */ }
        }
    }

    /** JS 写入新数据后调用：刷新所有实例 */
    public static void updateAll(Context context) {
        try {
            AppWidgetManager manager = AppWidgetManager.getInstance(context);
            if (manager == null) return;
            ComponentName cn = new ComponentName(context, NextWidgetProvider.class);
            int[] ids = manager.getAppWidgetIds(cn);
            if (ids == null || ids.length == 0) return;
            RemoteViews rv = build(context);
            for (int id : ids) {
                try { manager.updateAppWidget(id, rv); } catch (Exception e) { /* ignore */ }
            }
        } catch (Exception e) { /* ignore */ }
    }

    /** 存第一条数据；items 为空则清空（组件显示空状态） */
    public static void saveFirstItem(Context context, String json) {
        String compact = "";
        try {
            JSONObject o = new JSONObject(json == null ? "{}" : json);
            org.json.JSONArray items = o.optJSONArray("items");
            if (items != null && items.length() > 0) {
                JSONObject first = items.optJSONObject(0);
                if (first != null) {
                    JSONObject out = new JSONObject();
                    out.put("title", trim(first.optString("title", "")));
                    out.put("time", trim(first.optString("time", "")));
                    out.put("body", trim(first.optString("body", "")));
                    if (out.optString("title").length() > 0 || out.optString("time").length() > 0) {
                        compact = out.toString();
                    }
                }
            }
        } catch (Exception e) {
            compact = "";
        }
        try {
            SharedPreferences sp = context.getSharedPreferences(ReminderScheduler.PREFS, Context.MODE_PRIVATE);
            sp.edit().putString(KEY_WIDGET_NEXT, compact).apply();
        } catch (Exception e) { /* ignore */ }
    }

    private static RemoteViews build(Context context) {
        RemoteViews rv = new RemoteViews(context.getPackageName(), R.layout.widget_next);

        String title = "";
        String time = "";
        try {
            SharedPreferences sp = context.getSharedPreferences(ReminderScheduler.PREFS, Context.MODE_PRIVATE);
            String raw = sp.getString(KEY_WIDGET_NEXT, "");
            if (raw != null && raw.length() > 0) {
                JSONObject o = new JSONObject(raw);
                title = trim(o.optString("title", ""));
                time = trim(o.optString("time", ""));
            }
        } catch (Exception e) { /* 读不到就按空状态 */ }

        String line;
        if (title.length() == 0 && time.length() == 0) {
            rv.setTextViewText(R.id.widget_next_line, context.getString(R.string.widget_next_empty));
        } else {
            line = time.length() == 0 ? title : (title.length() == 0 ? time : time + " " + title);
            rv.setTextViewText(R.id.widget_next_line, line);
        }
        rv.setTextViewText(R.id.widget_next_label, context.getString(R.string.widget_next_label));

        Intent open = new Intent(context, MainActivity.class);
        open.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        PendingIntent pi = PendingIntent.getActivity(context, REQ_OPEN, open,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        rv.setOnClickPendingIntent(R.id.widget_next_root, pi);
        return rv;
    }

    private static String trim(String s) {
        return s == null ? "" : s.trim();
    }
}
