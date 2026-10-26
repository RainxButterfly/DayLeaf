/**
 * 桌面小组件「快速新建」：2x1，点一下直接打开快速录入。
 *
 * 作者名：StarLeaf·Roxy
 * 创建日期：2026-10-26
 */
package com.dayleaf.app;

import android.app.PendingIntent;
import android.appwidget.AppWidgetManager;
import android.appwidget.AppWidgetProvider;
import android.content.Context;
import android.content.Intent;
import android.widget.RemoteViews;

public class QuickWidgetProvider extends AppWidgetProvider {

    private static final int REQ_QUICK = 76003;

    @Override
    public void onUpdate(Context context, AppWidgetManager manager, int[] appWidgetIds) {
        if (appWidgetIds == null || appWidgetIds.length == 0) return;
        RemoteViews rv = build(context);
        for (int id : appWidgetIds) {
            try { manager.updateAppWidget(id, rv); } catch (Exception e) { /* ignore */ }
        }
    }

    private static RemoteViews build(Context context) {
        RemoteViews rv = new RemoteViews(context.getPackageName(), R.layout.widget_quick);
        Intent open = new Intent(context, MainActivity.class);
        open.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        open.putExtra("quick_add", true);
        PendingIntent pi = PendingIntent.getActivity(context, REQ_QUICK, open,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        rv.setOnClickPendingIntent(R.id.widget_quick_root, pi);
        return rv;
    }
}