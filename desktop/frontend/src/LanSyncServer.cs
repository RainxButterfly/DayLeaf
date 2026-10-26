using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace DayLeaf
{
    /// <summary>
    /// 局域网同步服务：电脑在同一个 Wi-Fi 下开一个极小的 HTTP 端点，
    /// 手机用「地址 + 配对码」推送/拉取数据。只在用户主动开启时监听，关掉即释放端口。
    /// 协议：
    ///   GET  /sync   → 返回当前最新同步包
    ///   PUT  /sync   → 接收手机推来的同步包，交给网页合并
    /// 鉴权：请求头 X-DayLeaf-Token 或查询参数 t 必须等于配对码。
    /// </summary>
    internal sealed class LanSyncServer : IDisposable
    {
        public const string DefaultToken = "";
        private TcpListener _listener;
        private Thread _thread;
        private volatile bool _running;
        private string _token = "";
        private string _latest = "{\"app\":\"DayLeaf\",\"state\":{\"events\":[]}}";

        public int Port { get; private set; }
        public bool Running { get { return _running; } }
        public string Token { get { return _token; } }
        public string Latest
        {
            get { return _latest; }
            set { if (!string.IsNullOrEmpty(value)) _latest = value; }
        }

        /// <summary>手机推来数据时触发（在后台线程），参数是完整 JSON 文本。</summary>
        public event Action<string> Incoming;

        public bool Start(string token, out int port, out string error)
        {
            port = 0;
            error = null;
            Stop();
            _token = string.IsNullOrEmpty(token) ? "000000" : token.Trim();
            for (int p = 17890; p < 17900; p++)
            {
                try
                {
                    var l = new TcpListener(IPAddress.Any, p);
                    l.Start();
                    _listener = l;
                    Port = p;
                    port = p;
                    _running = true;
                    _thread = new Thread(Loop) { IsBackground = true, Name = "DayLeafLanSync" };
                    _thread.Start();
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }
            return false;
        }

        public void Stop()
        {
            _running = false;
            try { if (_listener != null) _listener.Stop(); } catch (Exception) { /* ignore */ }
            _listener = null;
            _thread = null;
            Port = 0;
        }

        public void Dispose() { Stop(); }

        private void Loop()
        {
            while (_running)
            {
                TcpClient client = null;
                try
                {
                    client = _listener.AcceptTcpClient();
                    var c = client;
                    ThreadPool.QueueUserWorkItem(delegate { Handle(c); });
                }
                catch (Exception)
                {
                    if (!_running) return;
                }
            }
        }

        private void Handle(TcpClient client)
        {
            try
            {
                using (client)
                using (NetworkStream ns = client.GetStream())
                {
                    client.ReceiveTimeout = 15000;
                    client.SendTimeout = 15000;

                    string headerText = ReadHeaders(ns);
                    if (string.IsNullOrEmpty(headerText)) return;
                    string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    if (lines.Length == 0) return;

                    string[] first = lines[0].Split(' ');
                    if (first.Length < 2) return;
                    string method = first[0].ToUpperInvariant();
                    string path = first[1];
                    int q = path.IndexOf('?');
                    string query = q >= 0 ? path.Substring(q + 1) : "";
                    path = (q >= 0 ? path.Substring(0, q) : path);
                    if (path.IndexOf("/sync", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        Write(ns, 404, "not found");
                        return;
                    }

                    string token = HeaderValue(lines, "X-DayLeaf-Token");
                    if (string.IsNullOrEmpty(token)) token = QueryValue(query, "t");
                    if (!string.Equals(token, _token, StringComparison.Ordinal))
                    {
                        Write(ns, 401, "bad token");
                        return;
                    }

                    int len = 0;
                    string cl = HeaderValue(lines, "Content-Length");
                    int.TryParse(cl, out len);
                    if (len < 0 || len > 8 * 1024 * 1024)
                    {
                        Write(ns, 413, "too large");
                        return;
                    }

                    if (method == "GET")
                    {
                        Write(ns, 200, _latest);
                        return;
                    }
                    if (method == "PUT" || method == "POST")
                    {
                        byte[] body = ReadBody(ns, len);
                        string text = Encoding.UTF8.GetString(body);
                        if (string.IsNullOrEmpty(text) || text.IndexOf("\"events\"", StringComparison.Ordinal) < 0)
                        {
                            Write(ns, 400, "bad payload");
                            return;
                        }
                        Latest = text;
                        Action<string> h = Incoming;
                        if (h != null) { try { h(text); } catch (Exception) { /* ignore */ } }
                        Write(ns, 200, "ok");
                        return;
                    }

                    Write(ns, 405, "method not allowed");
                }
            }
            catch (Exception)
            {
                /* 单个请求失败不影响服务 */
            }
        }

        private static string ReadHeaders(NetworkStream ns)
        {
            var sb = new StringBuilder();
            var buf = new byte[1];
            while (sb.Length < 16384)
            {
                int n = ns.Read(buf, 0, 1);
                if (n <= 0) break;
                sb.Append((char)buf[0]);
                if (sb.Length >= 4 && sb[sb.Length - 4] == '\r' && sb[sb.Length - 3] == '\n'
                    && sb[sb.Length - 2] == '\r' && sb[sb.Length - 1] == '\n') break;
            }
            return sb.ToString();
        }

        private static byte[] ReadBody(NetworkStream ns, int len)
        {
            if (len <= 0) return new byte[0];
            var data = new byte[len];
            int off = 0;
            while (off < len)
            {
                int n = ns.Read(data, off, len - off);
                if (n <= 0) break;
                off += n;
            }
            if (off == len) return data;
            var cut = new byte[off];
            Array.Copy(data, cut, off);
            return cut;
        }

        private static void Write(NetworkStream ns, int code, string body)
        {
            byte[] payload = Encoding.UTF8.GetBytes(body ?? "");
            string reason = code == 200 ? "OK" : code == 401 ? "Unauthorized" : code == 404 ? "Not Found"
                : code == 405 ? "Method Not Allowed" : code == 413 ? "Payload Too Large" : "Bad Request";
            string head = "HTTP/1.1 " + code + " " + reason + "\r\n" +
                "Content-Type: application/json; charset=utf-8\r\n" +
                "Content-Length: " + payload.Length + "\r\n" +
                "Connection: close\r\n" +
                "Access-Control-Allow-Origin: *\r\n\r\n";
            byte[] hb = Encoding.ASCII.GetBytes(head);
            ns.Write(hb, 0, hb.Length);
            ns.Write(payload, 0, payload.Length);
            ns.Flush();
        }

        private static string HeaderValue(string[] lines, string name)
        {
            string prefix = name + ":";
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return lines[i].Substring(prefix.Length).Trim();
            }
            return "";
        }

        private static string QueryValue(string query, string key)
        {
            foreach (string part in query.Split('&'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0 && string.Equals(part.Substring(0, eq), key, StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(part.Substring(eq + 1));
            }
            return "";
        }

        /// <summary>本机所有可用的 IPv4 局域网地址（过滤回环与虚拟网卡）。</summary>
        public static List<string> LocalIPv4()
        {
            var list = new List<string>();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string s = ip.Address.ToString();
                        if (s.StartsWith("127.") || s.StartsWith("169.254.")) continue;
                        if (!list.Contains(s)) list.Add(s);
                    }
                }
            }
            catch (Exception) { /* ignore */ }
            if (list.Count == 0) list.Add("127.0.0.1");
            return list;
        }
    }
}