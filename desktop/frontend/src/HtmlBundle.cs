using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace DayLeaf
{
    /// <summary>
    /// 界面资源内嵌在 exe 里，启动时按内容哈希释放到本地目录。
    /// 现在有两份：index.html 与 xlsx.full.min.js（课表导入用）。
    /// 这样发布出去只有一个 exe（加几个依赖 DLL），也保证界面就是构建时那一份。
    /// </summary>
    internal static class HtmlBundle
    {
        private sealed class Item
        {
            public string Resource;
            public string Target;
        }

        private static readonly Item[] Items =
        {
            new Item { Resource = "www.index.html", Target = "index.html" },
            new Item { Resource = "www.xlsx.full.min.js", Target = "xlsx.full.min.js" }
        };

        public static string EnsureExtracted()
        {
            string dir = AppPaths.WwwDir;
            Directory.CreateDirectory(dir);
            string indexPath = Path.Combine(dir, "index.html");
            bool indexOk = false;

            foreach (Item it in Items)
            {
                byte[] data = ReadResource(it.Resource);
                string target = Path.Combine(dir, it.Target);
                if (data == null || data.Length == 0)
                {
                    // 没内嵌（开发时直接放了文件）就用手边这份
                    if (it.Target == "index.html" && File.Exists(target)) indexOk = true;
                    continue;
                }
                string hashFile = target + ".sha256";
                string hash = ComputeHash(data);
                string old = null;
                try { if (File.Exists(hashFile)) old = File.ReadAllText(hashFile).Trim(); }
                catch (Exception) { /* ignore */ }

                if (old == hash && File.Exists(target))
                {
                    if (it.Target == "index.html") indexOk = true;
                    continue;
                }

                // 先写临时文件再替换，避免写一半被读到
                string tmp = target + ".tmp";
                File.WriteAllBytes(tmp, data);
                if (File.Exists(target))
                {
                    try { File.Delete(target); } catch (Exception) { /* 被占用时直接覆盖 */ }
                }
                File.Move(tmp, target);
                try { File.WriteAllText(hashFile, hash); } catch (Exception) { /* ignore */ }
                if (it.Target == "index.html") indexOk = true;
            }

            if (!indexOk && !File.Exists(indexPath))
            {
                throw new InvalidOperationException("界面资源缺失：请先运行 build.ps1 同步 index.html");
            }
            return indexPath;
        }

        private static byte[] ReadResource(string name)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(name))
                {
                    if (s == null) return null;
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ComputeHash(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(data);
                var sb = new System.Text.StringBuilder(h.Length * 2);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}