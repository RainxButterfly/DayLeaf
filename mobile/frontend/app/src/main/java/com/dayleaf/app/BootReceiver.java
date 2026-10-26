/**
 * 开机、改时间、换时区、应用更新后，重新排后台提醒。
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
    public void onReceive(Context context, Intent intent) {
        try {
            ReminderScheduler.rescheduleStored(context);
        } catch (Exception e) { /* ignore */ }
    }
}
