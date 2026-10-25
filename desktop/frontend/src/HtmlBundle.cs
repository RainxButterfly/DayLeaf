using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace DayLeaf
{
    /// <summary>
    /// 界面 HTML 内嵌在 exe 里，启动时按内容哈希释放到本地目录。
    /// 这样发布出去只有一个 exe（加几个依赖 DLL），也保证界面就是构建时那一份。
    /// </summary>
    internal static class HtmlBundle
    {
        private const string ResourceName = "www.index.html";
        private const string TargetName = "index.html";
        private const string HashName = "index.sha256";

        public static string EnsureExtracted()
        {
            string dir = AppPaths.WwwDir;
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, TargetName);
            string hashFile = Path.Combine(dir, HashName);

            byte[] data = ReadResource();
            if (data == null || data.Length == 0)
            {
                // 没内嵌（开发时直接放了文件）就用手边这份
                if (File.Exists(target)) return target;
                throw new InvalidOperationException("界面资源缺失：请先运行 build.ps1 同步 index.html");
            }

            string hash = ComputeHash(data);
            string old = null;
            try { if (File.Exists(hashFile)) old = File.ReadAllText(hashFile).Trim(); }
            catch (Exception) { /* ignore */ }

            if (old == hash && File.Exists(target)) return target;

            // 先写临时文件再替换，避免写一半被读到
            string tmp = target + ".tmp";
            File.WriteAllBytes(tmp, data);
            if (File.Exists(target))
            {
                try { File.Delete(target); } catch (Exception) { /* 被占用时直接覆盖 */ }
            }
            File.Move(tmp, target);
            try { File.WriteAllText(hashFile, hash); } catch (Exception) { /* ignore */ }
            return target;
        }

        private static byte[] ReadResource()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(ResourceName))
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
