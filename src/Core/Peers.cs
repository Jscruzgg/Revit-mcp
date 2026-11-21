// Revit Multi Bridge - finds every Revit bridge on this PC: GET /status on each port of the pool (localhost, in parallel, short timeout).
// Used by the Bridge window (table "Bridges on this PC") and by the gateway (revit_instances / target). C# 5 syntax only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace RevitMultiBridge
{
    class Peer
    {
        public int Port, Pid;
        public string Revit = "", Build = "", Document = "", Path = "", View = "", Job = "", Version = "";
        public string[] Documents = new string[0];
        public bool Busy;
        public string Label { get { return "Revit " + Revit + " (port " + Port + ", pid " + Pid + ")" + (Document == "" ? " - no document" : " - " + Document); } }
    }

    static class Peers
    {
        // who is on that port; null = nobody, or something that is not a Revit Multi Bridge
        public static Peer Probe(int port, int timeoutMs)
        {
            try
            {
                using (var c = new TcpClient())
                {
                    var ar = c.BeginConnect(IPAddress.Loopback, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs) || !c.Connected) return null;
                    c.EndConnect(ar);
                    var s = c.GetStream();
                    s.ReadTimeout = timeoutMs * 3; s.WriteTimeout = timeoutMs;
                    var req = Encoding.ASCII.GetBytes("GET /status HTTP/1.1\r\nHost: 127.0.0.1:" + port + "\r\nConnection: close\r\n\r\n");
                    s.Write(req, 0, req.Length);
                    var ms = new MemoryStream();
                    var buf = new byte[8192]; int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) { ms.Write(buf, 0, n); if (ms.Length > 1024 * 1024) break; }
                    string text = Encoding.UTF8.GetString(ms.ToArray());
                    int cut = text.IndexOf("\r\n\r\n");
                    if (cut < 0 || !text.StartsWith("HTTP/1.1 200")) return null;
                    var d = Json.Parse(text.Substring(cut + 4)) as Dictionary<string, object>;
                    if (d == null || !(Server.Name).Equals(d.ContainsKey("server") ? d["server"] : null)) return null;
                    Func<string, string> S = k => d.ContainsKey(k) && d[k] != null ? d[k].ToString() : "";
                    var p = new Peer { Port = port, Revit = S("revit"), Build = S("build"), Document = S("document"), Path = S("path"), View = S("view"),
                                       Job = S("job"), Version = S("version"), Busy = true.Equals(d.ContainsKey("busy") ? d["busy"] : null) };
                    int pid; int.TryParse(S("pid"), out pid); p.Pid = pid;
                    var docs = d.ContainsKey("documents") ? d["documents"] as object[] : null;
                    if (docs != null) p.Documents = docs.Select(x => x == null ? "" : x.ToString()).ToArray();
                    return p;
                }
            }
            catch { return null; }
        }

        // every port of the pool at once; sorted by port
        public static List<Peer> Scan(int first, int last, int timeoutMs)
        {
            var found = new List<Peer>();
            int n = last - first + 1, left = n;
            if (n <= 0) return found;
            using (var done = new ManualResetEvent(false))
            {
                for (int p = first; p <= last; p++)
                {
                    int port = p;
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        var peer = Probe(port, timeoutMs);
                        lock (found) { if (peer != null) found.Add(peer); }
                        if (Interlocked.Decrement(ref left) == 0) try { done.Set(); } catch { }
                    });
                }
                done.WaitOne(timeoutMs * 6 + 1000);
            }
            lock (found) return found.OrderBy(x => x.Port).ToList();
        }

        // POST a JSON-RPC body to one bridge, return the response body (WebException on HTTP errors / timeouts)
        public static string Post(int port, string json, int timeoutMs)
        {
            ServicePointManager.Expect100Continue = false;    // the bridge does not answer "100 Continue"
            var rq = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/mcp");
            rq.Method = "POST"; rq.ContentType = "application/json"; rq.Proxy = null;
            rq.Timeout = timeoutMs; rq.ReadWriteTimeout = timeoutMs; rq.KeepAlive = false;
            var bytes = Encoding.UTF8.GetBytes(json);
            rq.ContentLength = bytes.Length;
            using (var s = rq.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
            using (var r = (HttpWebResponse)rq.GetResponse())
            using (var sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd();
        }
    }
}
