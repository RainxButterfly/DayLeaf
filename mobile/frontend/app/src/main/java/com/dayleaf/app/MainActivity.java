/**
 * 主界面：WebView 承载应用，负责启动时的自动更新检测、系统分享接收、文件保存。
 *
 * 作者名：StarLeaf·Roxy
 * 创建日期：2026-10-25
 */
package com.dayleaf.app;

import android.app.Activity;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.ActivityInfo;
import android.content.res.Configuration;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.util.Base64;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Toast;

import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
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
    private byte[] pendingSaveBytes = null;          // 待写入的二进制（PNG / 备份包）
    private static final int REQ_SAVE_BINARY = 2003;

    private final Object shareLock = new Object();
    private String pendingSharedText = null;   // 系统分享进来、还没被 JS 取走的内容
    private volatile boolean pageLoaded = false;

    /** 分享图片进内存的上限：再大就放弃，避免 OOM */
    private static final int MAX_SHARE_BYTES = 8 * 1024 * 1024;
    /** 小图直接给 data URL（WebView 里必然能显示），大图给 file:// 路径 */
    private static final int DATA_URL_LIMIT = 900 * 1024;
    private static final int MAX_SHARED_TEXT = 100 * 1024;

    /** 由 WebAppInterface 回调，标记当前是否有弹窗 */
    public void setHasModal(boolean has) {
        this.hasModal = has;
    }

    /** 读 JS 同步过来的主题偏好（light / dark / system），判断当前是否深色 */
    private boolean isDarkStored() {
        String mode = "light";
        try {
            SharedPreferences sp = getSharedPreferences("dayleaf", MODE_PRIVATE);
            mode = sp.getString("theme", "light");
        } catch (Exception e) { /* 读不到就按浅色 */ }

        if ("dark".equals(mode)) return true;
        if ("system".equals(mode)) {
            int ui = getResources().getConfiguration().uiMode & Configuration.UI_MODE_NIGHT_MASK;
            return ui == Configuration.UI_MODE_NIGHT_YES;
        }
        return false;
    }

    /** 运行时切换主题后，同步状态栏、导航栏与 WebView 底色（由 JS 触发） */
    public void applyStoredThemeToChrome() {
        final boolean dark = isDarkStored();
        final int bg = getColor(dark ? R.color.dayleaf_bg_dark : R.color.dayleaf_bg);
        runOnUiThread(new Runnable() {
            @Override public void run() {
                try {
                    getWindow().setStatusBarColor(bg);
                    getWindow().setNavigationBarColor(bg);
                    if (webView != null) webView.setBackgroundColor(bg);
                    if (Build.VERSION.SDK_INT >= 23) {
                        int flags = getWindow().getDecorView().getSystemUiVisibility();
                        if (dark) {
                            flags &= ~android.view.View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR;
                        } else {
                            flags |= android.view.View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR;
                        }
                        if (Build.VERSION.SDK_INT >= 26) {
                            if (dark) {
                                flags &= ~android.view.View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR;
                            } else {
                                flags |= android.view.View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR;
                            }
                        }
                        getWindow().getDecorView().setSystemUiVisibility(flags);
                    }
                } catch (Exception e) { /* ignore */ }
            }
        });
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        // 主题必须在 super.onCreate 之前定：窗口底色与 Android 12+ 系统闪屏都取自它，
        // 晚一步就会先闪一层浅色再变深色。
        setTheme(isDarkStored() ? R.style.Theme_DayLeaf_Dark : R.style.Theme_DayLeaf);
        super.onCreate(savedInstanceState);
        // 手机（最小宽度 < 600dp）锁竖屏；平板放行横竖屏旋转
        if (getResources().getConfiguration().smallestScreenWidthDp < 600) {
            setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_PORTRAIT);
        }
        setContentView(R.layout.activity_main);

        // 从系统「分享到叶历」进来：先收下内容，等网页加载完再取
        handleShareIntent(getIntent());

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
        // 关掉 overview 缩放与手势缩放：页面自带 viewport 与自适应布局，
        // 再让内核按内容宽度缩放，部分机型（vivo 等）会把整页缩小或错位。
        s.setLoadWithOverviewMode(false);
        s.setSupportZoom(false);
        s.setBuiltInZoomControls(false);
        s.setDisplayZoomControls(false);
        s.setLoadsImagesAutomatically(true);
        s.setCacheMode(WebSettings.LOAD_DEFAULT);
        if (Build.VERSION.SDK_INT >= 21) {
            s.setMixedContentMode(WebSettings.MIXED_CONTENT_ALWAYS_ALLOW);
        }

        // 背景跟随主题：深色用户不该在网页加载完之前看到一层浅色
        webView.setBackgroundColor(getColor(isDarkStored() ? R.color.dayleaf_bg_dark : R.color.dayleaf_bg));
        webView.setWebViewClient(new WebViewClient() {
            @Override public boolean shouldOverrideUrlLoading(WebView view, String url) {
                // 外链（mailto / http / https）交给系统；其余留在壳内
                if (url.startsWith("mailto:") || url.startsWith("http://") || url.startsWith("https://")) {
                    try { startActivity(new Intent(Intent.ACTION_VIEW, android.net.Uri.parse(url))); } catch (Exception e) { /* ignore */ }
                    return true;
                }
                return false;
            }

            @Override public void onPageFinished(WebView view, String url) {
                pageLoaded = true;
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

    /** 在 UI 线程执行一段 JS（后台线程如 WebDAV 同步回调用） */
    public void runJs(final String js) {
        if (js == null || js.length() == 0) return;
        runOnUiThread(new Runnable() {
            @Override public void run() {
                if (webView == null) return;
                try { webView.evaluateJavascript(js, null); } catch (Exception e) { /* ignore */ }
            }
        });
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        if (intent == null) return;
        setIntent(intent);
        boolean has = handleShareIntent(intent);
        if (!has) return;

        // 应用正在运行时：直接把内容推给页面，避免用户要手动再点一次
        if (pageLoaded) {
            String text = takeSharedText();
            if (text != null && text.length() > 0) {
                runJs("window.__dlShared && window.__dlShared(" + JSONObject.quote(text) + ");");
            }
        }
    }

    /** 取走分享内容并清空（JS 侧 consumeSharedText） */
    public String consumeSharedText() {
        String s = takeSharedText();
        return s == null ? "" : s;
    }

    private String takeSharedText() {
        synchronized (shareLock) {
            String s = pendingSharedText;
            pendingSharedText = null;
            return s;
        }
    }

    private void putSharedText(String text) {
        if (text == null) return;
        if (text.length() > MAX_SHARED_TEXT) text = text.substring(0, MAX_SHARED_TEXT);
        synchronized (shareLock) {
            pendingSharedText = text;
        }
    }

    /**
     * 处理系统分享：text/* 取 EXTRA_TEXT；image/* 把图片复制到应用缓存，
     * 小的返回 data URL、大的返回 file:// 路径（都从 consumeSharedText 出去）。
     *
     * @return 是否收到了分享内容
     */
    private boolean handleShareIntent(Intent intent) {
        if (intent == null) return false;
        String action = intent.getAction();
        if (!Intent.ACTION_SEND.equals(action)) return false;
        String type = intent.getType();
        if (type == null) type = "";

        if (type.startsWith("image/")) {
            Uri uri = null;
            try {
                Object extra = intent.getParcelableExtra(Intent.EXTRA_STREAM);
                if (extra instanceof Uri) uri = (Uri) extra;
            } catch (Exception e) { uri = null; }
            String shared = copySharedImage(uri, type);
            if (shared == null) return false;
            putSharedText(shared);
            return true;
        }

        String text = null;
        try { text = intent.getStringExtra(Intent.EXTRA_TEXT); } catch (Exception e) { text = null; }
        if (text == null || text.trim().length() == 0) {
            try { text = intent.getStringExtra(Intent.EXTRA_SUBJECT); } catch (Exception e) { text = null; }
        }
        if (text == null || text.trim().length() == 0) return false;
        putSharedText(text);
        return true;
    }

    /** 把分享进来的图片落到 cache；读不到/太大/没权限一律返回 null，不抛异常 */
    private String copySharedImage(Uri uri, String mime) {
        if (uri == null) return null;
        InputStream in = null;
        FileOutputStream out = null;
        try {
            in = getContentResolver().openInputStream(uri);
            if (in == null) return null;

            ByteArrayOutputStream buf = new ByteArrayOutputStream();
            byte[] chunk = new byte[16 * 1024];
            int n;
            int total = 0;
            while ((n = in.read(chunk)) > 0) {
                total += n;
                if (total > MAX_SHARE_BYTES) return null; // 超限直接放弃，别把内存吃光
                buf.write(chunk, 0, n);
            }
            byte[] data = buf.toByteArray();
            if (data.length == 0) return null;

            String ext = "img";
            String m = mime == null ? "" : mime.toLowerCase();
            if (m.contains("png")) ext = "png";
            else if (m.contains("jpeg") || m.contains("jpg")) ext = "jpg";
            else if (m.contains("webp")) ext = "webp";
            else if (m.contains("gif")) ext = "gif";

            cleanOldShareFiles();
            File f = new File(getCacheDir(), "share_" + System.currentTimeMillis() + "." + ext);
            out = new FileOutputStream(f);
            out.write(data);
            out.flush();

            if (data.length <= DATA_URL_LIMIT) {
                String mimeOut = (mime == null || mime.length() == 0) ? "image/*" : mime;
                return "data:" + mimeOut + ";base64,"
                        + Base64.encodeToString(data, Base64.NO_WRAP);
            }
            return "file://" + f.getAbsolutePath();
        } catch (Exception e) {
            return null; /* 没读权限 / 供应商 URI 异常：静默失败 */
        } finally {
            try { if (in != null) in.close(); } catch (Exception e) { /* ignore */ }
            try { if (out != null) out.close(); } catch (Exception e) { /* ignore */ }
        }
    }

    /** 分享缓存只留 1 天，避免越攒越多 */
    private void cleanOldShareFiles() {
        try {
            File dir = getCacheDir();
            File[] files = dir == null ? null : dir.listFiles();
            if (files == null) return;
            long cutoff = System.currentTimeMillis() - 24L * 60L * 60L * 1000L;
            for (File f : files) {
                if (f != null && f.getName().startsWith("share_") && f.lastModified() < cutoff) {
                    try { f.delete(); } catch (Exception ignore) { /* ignore */ }
                }
            }
        } catch (Exception e) { /* ignore */ }
    }

    /** 由 WebAppInterface 调用：拉起系统「另存为」，把文本写入用户选的位置 */
    public void saveTextFile(String filename, String content) {
        pendingSaveBytes = null;
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

    /** 由 WebAppInterface 调用：保存二进制（分享出去的 PNG、备份包等） */
    public void saveBinaryFile(String filename, byte[] bytes, String mime) {
        if (bytes == null || bytes.length == 0) {
            Toast.makeText(this, "内容为空", Toast.LENGTH_SHORT).show();
            return;
        }
        pendingSaveContent = null;
        pendingSaveBytes = bytes;
        Intent intent = new Intent(Intent.ACTION_CREATE_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType(mime != null && mime.length() > 0 ? mime : "application/octet-stream");
        intent.putExtra(Intent.EXTRA_TITLE, filename);
        try {
            startActivityForResult(intent, REQ_SAVE_BINARY);
        } catch (Exception e) {
            pendingSaveBytes = null;
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
        if (requestCode == REQ_SAVE_BINARY) {
            if (resultCode == RESULT_OK && data != null && data.getData() != null && pendingSaveBytes != null) {
                try {
                    OutputStream os = getContentResolver().openOutputStream(data.getData());
                    if (os != null) {
                        os.write(pendingSaveBytes);
                        os.close();
                    }
                    Toast.makeText(this, "已保存", Toast.LENGTH_SHORT).show();
                } catch (Exception e) {
                    Toast.makeText(this, "保存失败", Toast.LENGTH_SHORT).show();
                }
            }
            pendingSaveBytes = null;
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
