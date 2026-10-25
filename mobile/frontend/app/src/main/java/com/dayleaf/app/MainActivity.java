/**
 * 主界面：WebView 承载应用，负责启动时的自动更新检测。
 *
 * 作者名：StarLeaf·Roxy
 * 创建日期：2026-10-25
 */
package com.dayleaf.app;

import android.app.Activity;
import android.content.Intent;
import android.content.pm.ActivityInfo;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Toast;

import java.io.OutputStream;

public class MainActivity extends Activity {

    private WebView webView;
    private UpdateManager updateManager;
    private boolean startupChecked = false;
    private boolean hasModal = false;      // 当前是否有弹窗（由 JS 通过桥同步）
    private long lastBackTime = 0;         // 上次按返回键的时间，用于「再按一次退出」
    private ValueCallback<Uri[]> filePathCallback;   // 网页 <input type="file"> 的选择回调
    private static final int REQ_FILE_CHOOSER = 2001;
    private String pendingSaveContent = null;        // 待写入「另存为」的内容
    private static final int REQ_SAVE_FILE = 2002;

    /** 由 WebAppInterface 回调，标记当前是否有弹窗 */
    public void setHasModal(boolean has) {
        this.hasModal = has;
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        // 手机（最小宽度 < 600dp）锁竖屏；平板放行横竖屏旋转
        if (getResources().getConfiguration().smallestScreenWidthDp < 600) {
            setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_PORTRAIT);
        }
        setContentView(R.layout.activity_main);

        webView = findViewById(R.id.webview);
        updateManager = new UpdateManager(this);

        setupWebView();

        // 启动后稍等片刻，静默检测一次更新（有新版才提示，不打扰）
        webView.postDelayed(new Runnable() {
            @Override public void run() {
                if (!startupChecked && !isFinishing()) {
                    startupChecked = true;
                    updateManager.checkForUpdate(false);
                }
            }
        }, 1200);
    }

    private void setupWebView() {
        WebSettings s = webView.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true);          // localStorage 持久化（覆盖升级后数据保留的关键）
        s.setDatabaseEnabled(true);
        s.setAllowFileAccess(true);
        s.setAllowContentAccess(true);
        s.setUseWideViewPort(true);
        s.setLoadWithOverviewMode(true);
        s.setLoadsImagesAutomatically(true);
        s.setCacheMode(WebSettings.LOAD_DEFAULT);
        if (Build.VERSION.SDK_INT >= 21) {
            s.setMixedContentMode(WebSettings.MIXED_CONTENT_ALWAYS_ALLOW);
        }

        // 背景跟随主题色（values-night 自动切深色），避免加载期间露出白屏
        webView.setBackgroundColor(getColor(R.color.dayleaf_bg));
        webView.setWebViewClient(new WebViewClient() {
            @Override public boolean shouldOverrideUrlLoading(WebView view, String url) {
                // 外链（mailto / http / https）交给系统；其余留在壳内
                if (url.startsWith("mailto:") || url.startsWith("http://") || url.startsWith("https://")) {
                    try { startActivity(new Intent(Intent.ACTION_VIEW, android.net.Uri.parse(url))); } catch (Exception e) { /* ignore */ }
                    return true;
                }
                return false;
            }
        });
        // 重写 onShowFileChooser：网页里的「选择图片 / 导入 JSON / 上传字体」
        // 必须由这里拉起系统文件选择器，否则 <input type="file"> 在 WebView 里点了没反应
        webView.setWebChromeClient(new WebChromeClient() {
            @Override
            public boolean onShowFileChooser(WebView view, ValueCallback<Uri[]> callback,
                                             FileChooserParams params) {
                if (filePathCallback != null) {
                    filePathCallback.onReceiveValue(null);
                }
                filePathCallback = callback;

                Intent intent;
                try {
                    intent = params.createIntent();
                } catch (Exception e) {
                    intent = new Intent(Intent.ACTION_GET_CONTENT);
                    intent.addCategory(Intent.CATEGORY_OPENABLE);
                    intent.setType("*/*");
                }
                try {
                    startActivityForResult(intent, REQ_FILE_CHOOSER);
                } catch (Exception e) {
                    filePathCallback = null;
                    return false;
                }
                return true;
            }
        });

        webView.addJavascriptInterface(new WebAppInterface(this, updateManager), "DayLeafNative");
        webView.loadUrl("file:///android_asset/www/index.html");
    }

    @Override
    public void onBackPressed() {
        // 有弹窗：优先关弹窗（交给 JS 关闭最上层），不退出
        if (hasModal && webView != null) {
            webView.evaluateJavascript("window.__closeTopModal && window.__closeTopModal()", null);
            return;
        }
        // 无弹窗：首次返回仅提示，2 秒内再按才真正退出
        long now = System.currentTimeMillis();
        if (now - lastBackTime > 2000) {
            lastBackTime = now;
            Toast.makeText(this, "再按一次返回键退出", Toast.LENGTH_SHORT).show();
            return;
        }
        super.onBackPressed();
    }

    /** 由 WebAppInterface 调用：拉起系统「另存为」，把文本写入用户选的位置 */
    public void saveTextFile(String filename, String content) {
        pendingSaveContent = content;
        Intent intent = new Intent(Intent.ACTION_CREATE_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType("application/octet-stream");
        intent.putExtra(Intent.EXTRA_TITLE, filename);
        try {
            startActivityForResult(intent, REQ_SAVE_FILE);
        } catch (Exception e) {
            pendingSaveContent = null;
            Toast.makeText(this, "无法打开保存界面", Toast.LENGTH_SHORT).show();
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        if (requestCode == REQ_FILE_CHOOSER) {
            if (filePathCallback != null) {
                Uri[] results = null;
                if (resultCode == RESULT_OK && data != null) {
                    if (data.getClipData() != null) {
                        int count = data.getClipData().getItemCount();
                        results = new Uri[count];
                        for (int i = 0; i < count; i++) {
                            results[i] = data.getClipData().getItemAt(i).getUri();
                        }
                    } else if (data.getData() != null) {
                        results = new Uri[]{ data.getData() };
                    }
                }
                filePathCallback.onReceiveValue(results);
                filePathCallback = null;
            }
            return;
        }
        if (requestCode == REQ_SAVE_FILE) {
            if (resultCode == RESULT_OK && data != null && data.getData() != null && pendingSaveContent != null) {
                try {
                    OutputStream os = getContentResolver().openOutputStream(data.getData());
                    if (os != null) {
                        os.write(pendingSaveContent.getBytes("UTF-8"));
                        os.close();
                    }
                    Toast.makeText(this, "已保存", Toast.LENGTH_SHORT).show();
                } catch (Exception e) {
                    Toast.makeText(this, "保存失败", Toast.LENGTH_SHORT).show();
                }
            }
            pendingSaveContent = null;
            return;
        }
        super.onActivityResult(requestCode, resultCode, data);
    }

    @Override
    protected void onDestroy() {
        if (webView != null) {
            webView.destroy();
            webView = null;
        }
        super.onDestroy();
    }
}
