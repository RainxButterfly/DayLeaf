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

    /** JS 传入 {today:[{time,title}]}；最多保留 4 行。 */
    public static void saveItems(Context context, String json) {
        StringBuilder sb = new StringBuilder();
        try {
            JSONObject o = new JSONObject(json == null ? "{}" : json);
            JSONArray arr = o.optJSONArray("today");
            if (arr != null) {
                for (int i = 0; i < arr.length() && i < MAX_LINES; i++) {
                    JSONObject it = arr.optJSONObject(i);
                    if (it == null) continue;
                    String time = trim(it.optString("time", ""));
                    String title = trim(it.optString("title", ""));
                    if (time.length() == 0 && title.length() == 0) continue;
                    if (sb.length() > 0) sb.append('\n');
                    sb.append(time.length() == 0 ? title : (title.length() == 0 ? time : time + "  " + title));
                }
            }
        } catch (Exception e) {
            sb.setLength(0);
        }
        try {
            SharedPreferences sp = context.getSharedPreferences(ReminderScheduler.PREFS, Context.MODE_PRIVATE);
            sp.edit().putString(KEY_WIDGET_TODAY, sb.toString()).apply();
        } catch (Exception e) { /* ignore */ }
    }

    private static RemoteViews build(Context context) {
        RemoteViews rv = new RemoteViews(context.getPackageName(), R.layout.widget_today);
        String lines = "";
        try {
            SharedPreferences sp = context.getSharedPreferences(ReminderScheduler.PREFS, Context.MODE_PRIVATE);
            lines = sp.getString(KEY_WIDGET_TODAY, "");
        } catch (Exception e) { /* ignore */ }
        if (lines == null) lines = "";
        rv.setTextViewText(R.id.widget_today_line,
                lines.length() == 0 ? context.getString(R.string.widget_today_empty) : lines);
        rv.setTextViewText(R.id.widget_today_label, context.getString(R.string.widget_today_label));

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