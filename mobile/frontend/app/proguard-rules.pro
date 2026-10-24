# 保留 WebView JS 接口（@JavascriptInterface 方法不能被混淆掉）
-keepclassmembers class * {
    @android.webkit.JavascriptInterface <methods>;
}
-keep public class com.dayleaf.app.** { *; }
