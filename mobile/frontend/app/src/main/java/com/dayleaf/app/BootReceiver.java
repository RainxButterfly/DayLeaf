/**
 * 开机、改时间、换时区、应用更新后：重排提醒、补发错过的提醒、刷新「下一项」通知。
 *
 * 作者名：StarLeaf·Roxy
 * 创建日期：2026-10-26
 */
package com.dayleaf.app;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

public class BootReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(final Context context, Intent intent) {
        final Context app = context.getApplicationContext() != null
                ? context.getApplicationContext() : context;
        final PendingResult pending = goAsync();
        new Thread(new Runnable() {
            @Override public void run() {
                try {
                    try { ReminderScheduler.rescheduleStored(app); } catch (Exception e) { /* ignore */ }
                    try { ReminderScheduler.catchUpMissed(app); } catch (Exception e) { /* ignore */ }
                    try { NextNotifier.updateFromStored(app); } catch (Exception e) { /* ignore */ }
                    try { NextWidgetProvider.updateAll(app); } catch (Exception e) { /* ignore */ }
                    try { TodayWidgetProvider.updateAll(app); } catch (Exception e) { /* ignore */ }
                } finally {
                    try { pending.finish(); } catch (Exception e) { /* ignore */ }
                }
            }
        }).start();
    }
}
