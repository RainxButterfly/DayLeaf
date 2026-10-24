/**
 * 极简文件提供者：把下载到应用私有目录的 APK 用 content:// 暴露给系统安装器。
 *
 * 作者名：StarLeaf·Roxy
 * 创建日期：2026-10-25
 */
package com.dayleaf.app;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.net.Uri;
import android.os.Environment;
import android.os.ParcelFileDescriptor;

import java.io.File;
import java.io.FileNotFoundException;

public class InstallApkProvider extends ContentProvider {

    @Override
    public boolean onCreate() { return true; }

    @Override
    public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        File dir = getContext().getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS);
        if (dir == null) dir = getContext().getCacheDir();
        String name = uri.getPath();
        if (name != null && name.startsWith("/")) name = name.substring(1);
        File f = new File(dir, name);
        return ParcelFileDescriptor.open(f, ParcelFileDescriptor.MODE_READ_ONLY);
    }

    @Override
    public Cursor query(Uri uri, String[] p, String sel, String[] args, String order) { return null; }

    @Override
    public String getType(Uri uri) { return "application/vnd.android.package-archive"; }

    @Override
    public Uri insert(Uri uri, ContentValues values) { throw new UnsupportedOperationException(); }

    @Override
    public int delete(Uri uri, String sel, String[] args) { return 0; }

    @Override
    public int update(Uri uri, ContentValues values, String sel, String[] args) { return 0; }

    /** 生成某个文件的 content:// URI（与 Manifest 里的 authorities 保持一致）。 */
    public static Uri getUriForFile(Context ctx, File file) {
        String authority = ctx.getPackageName() + ".fileprovider";
        return Uri.parse("content://" + authority + "/" + file.getName());
    }
}
