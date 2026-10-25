using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;

namespace DayLeaf
{
    /// <summary>
    /// 桌面端版本检测：读 desktop/backend/update.json，和自身版本比。
    /// 多源并行（jsDelivr 的 tag 解析 + main 兜底），取最高版本。
    /// </summary>
    internal static class DesktopUpdate
    {
        private static readonly string[] Sources =
        {
            "https://cdn.jsdelivr.net/gh/RainxButterfly/DayLeaf@latest/desktop/backend/update.json",
            "https://fastly.jsdelivr.net/gh/RainxButterfly/DayLeaf@latest/desktop/backend/update.json",
            "https://raw.githubusercontent.com/RainxButterfly/DayLeaf/main/desktop/backend/update.json",
            "https://cdn.jsdelivr.net/gh/RainxButterfly/DayLeaf@main/desktop/backend/update.json"
        };

        internal sealed class Info
        {
            public string VersionName = "";
            public string Url = "";
            public string Notes = "";
            public long Size;
        }

        internal sealed class Result
        {
            public Info Latest;          // 网络失败时为 null
            public string Current = "";
            public bool HasNewer;
        }

        public static string CurrentVersion()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                object[] attrs = asm.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
                if (attrs.Length > 0)
                {
                    string v = ((AssemblyInformationalVersionAttribute)attrs[0]).InformationalVersion;
                    int plus = v.IndexOf('+');
                    if (plus > 0) v = v.Substring(0, plus);
                    if (v.Length > 0) return v;
                }
                Version ver = asm.GetName().Version;
                return ver.Major + "." + ver.Minor + "." + ver.Build;
            }
            catch (Exception)
            {
                return "1.0.0";
            }
        }

        /// <summary>异步检测；回调在后台线程，调用方自己切回 UI。</summary>
        public static void Check(Action<Result> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                var result = new Result { Current = CurrentVersion() };
                Info best = null;

                var list = new List<Info>();
                var lockObj = new object();
                var threads = new List<Thread>();

                foreach (string src in Sources)
                {
                    string url = src;
                    var t = new Thread(delegate()
                    {
                        Info info = Fetch(url);
                        if (info != null)
                        {
                            lock (lockObj) { list.Add(info); }
                        }
                    });
                    t.IsBackground = true;
                    threads.Add(t);
                    t.Start();
                }

                foreach (Thread t in threads)
                {
                    if (!t.Join(4000)) { /* 超时就当这个源没结果 */ }
                }

                lock (lockObj)
                {
                    foreach (Info i in list)
                    {
                        if (best == null || CompareVersion(i.VersionName, best.VersionName) > 0) best = i;
                    }
                }

                result.Latest = best;
                result.HasNewer = best != null && !string.IsNullOrEmpty(best.Url)
                    && CompareVersion(best.VersionName, result.Current) > 0;

                if (done != null) done(result);
            });
        }

        private static Info Fetch(string url)
        {
            try
            {
                using (var wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "DayLeaf-Desktop/" + CurrentVersion();
                    wc.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                    wc.Encoding = System.Text.Encoding.UTF8;
                    string json = wc.DownloadString(url);
                    if (string.IsNullOrEmpty(json)) return null;

                    var ser = new JavaScriptSerializer();
                    var dict = ser.Deserialize<Dictionary<string, object>>(json);
                    if (dict == null || !dict.ContainsKey("versionName")) return null;

                    var info = new Info
                    {
                        VersionName = Convert.ToString(dict["versionName"]),
                        Url = dict.ContainsKey("url") ? Convert.ToString(dict["url"]) : "",
                        Notes = dict.ContainsKey("notes") ? Convert.ToString(dict["notes"]) : ""
                    };
                    if (dict.ContainsKey("size"))
                    {
                        long sz;
                        if (long.TryParse(Convert.ToString(dict["size"]), out sz)) info.Size = sz;
                    }
                    return info;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>按点分数字比版本：返回 >0 表示 a 更新。</summary>
        public static int CompareVersion(string a, string b)
        {
            int[] pa = Parse(a);
            int[] pb = Parse(b);
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                int x = i < pa.Length ? pa[i] : 0;
                int y = i < pb.Length ? pb[i] : 0;
                if (x != y) return x > y ? 1 : -1;
            }
            return 0;
        }

        private static int[] Parse(string v)
        {
            if (string.IsNullOrEmpty(v)) return new int[0];
            var parts = v.Split('.');
            var nums = new List<int>();
            foreach (string p in parts)
            {
                int n;
                string digits = "";
                foreach (char c in p)
                {
                    if (c >= '0' && c <= '9') digits += c; else break;
                }
                nums.Add(int.TryParse(digits, out n) ? n : 0);
            }
            return nums.ToArray();
        }
    }
}
