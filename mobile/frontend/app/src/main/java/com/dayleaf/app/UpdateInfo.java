/**
 * 更新信息模型。
 *
 * 作者名：StarLeaf·Roxy
 * 创建日期：2026-10-25
 */
package com.dayleaf.app;

public class UpdateInfo {
    public int versionCode;
    public String versionName;
    /** 主下载地址 */
    public String url;
    /** 备用下载地址（urls[]）：主地址失败或校验不过时按顺序重试 */
    public java.util.List<String> urls = new java.util.ArrayList<String>();
    public String notes;
    public long size;
    public String sha256;
    public boolean force;
    /** 低于这个 versionCode 必须更新；0 表示不限制 */
    public int minVersionCode;
}
