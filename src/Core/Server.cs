// Revit Multi Bridge - local HTTP server + MCP (JSON-RPC) of ONE Revit window, on a port taken from a shared pool.
// Every Revit (2024, 2025, 2026, 2027, several windows of the same version...) takes the first free port of the pool
// (default 5310-5329, env REVIT_MULTI_BRIDGE_PORTS="first-last"); the port says nothing about the version.
// GET /status tells who is on a port (pid, Revit year, active document) -> the gateway and the Bridge windows find every Revit.
// POST /release (Re-claim from another Revit): this Revit lets that port go and moves to another free port of the pool.
// No Revit API here (see IBackend), so the server can be tested outside Revit. C# 5 syntax only.
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
    static class Server
    {
        public const string Name = "revit-multi-bridge";
        public const string Version = "1.0.0";
        public const int DefaultFirst = 5310, DefaultLast = 5329;

        static TcpListener listener;
        static volatile bool running;
        static volatile int port;
        static readonly object startLock = new object(), busyLock = new object();
        static readonly List<string[]> recent = new List<string[]>();
        static int recentVersion;

        public static IBackend Backend;
        public static bool Running { get { return running; } }
        public static int Port { get { return running ? port : 0; } }
        public static volatile string LastError;
        public static int RecentVersion { get { return recentVersion; } }

        // ---------- port pool ----------
        static int first = -1, last = -1;
        public static int First { get { Pool(); return first; } }
        public static int Last { get { Pool(); return last; } }
        static void Pool()
        {
            if (first > 0) return;
            int a = DefaultFirst, b = DefaultLast;
            string env = Environment.GetEnvironmentVariable("REVIT_MULTI_BRIDGE_PORTS");
            if (!string.IsNullOrEmpty(env))
            {
                var parts = env.Split('-');
                int x, y;
                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out x) && int.TryParse(parts[1].Trim(), out y) && x > 1024 && y >= x && y - x < 200) { a = x; b = y; }
                else if (parts.Length == 1 && int.TryParse(parts[0].Trim(), out x) && x > 1024) { a = x; b = x + 19; }
            }
            first = a; last = b;
        }

        // first free port of the pool (exclude = a port not to take, e.g. the one just released); false = the pool is full
        public static bool StartAny(int exclude)
        {
            for (int p = First; p <= Last; p++)
            {
                if (p == exclude) continue;
                if (Start(p, false)) return true;
            }
            LastError = "Every port of the pool " + First + "-" + Last + " is taken (" + (Last - First + 1) + " Revit bridges running?).";
            Log.Write(LastError);
            return false;
        }

        // one given port; false = busy (LastError says why)
        public static bool Start(int p, bool logBusy)
        {
            lock (startLock)
            {
                if (running) return p == port;
                TcpListener l = null;
                try
                {
                    l = new TcpListener(IPAddress.Loopback, p);
                    l.ExclusiveAddressUse = true;        // never share a port with another Revit
                    l.Start();
                }
                catch (Exception ex)
                {
                    try { if (l != null) l.Stop(); } catch { }
                    LastError = "Port " + p + " is busy (another Revit has it). Re-claim takes it; that Revit moves to another free port.";
                    if (logBusy) Log.Write("Could not listen on port " + p + ": " + ex.Message);
                    return false;
                }
                listener = l; port = p; running = true; LastError = null;
                var t = new Thread(Loop) { IsBackground = true, Name = "RevitMultiBridge " + p };
                t.Start(l);
                Log.Write("Listening on http://localhost:" + p + "/mcp (Revit " + HostInfo.Year + ", pid " + HostInfo.Pid + ")");
                return true;
            }
        }

        public static void Stop()
        {
            lock (startLock)
            {
                if (running) Log.Write("Stopped (port " + port + ")");
                running = false;
                try { if (listener != null) listener.Stop(); } catch { }
                listener = null;
            }
        }

        // Take a port another Revit holds: ask it to move (POST /release), then listen there.
        // If this Revit is on another port it leaves that one first. Returns null on success, otherwise the reason to show.
        public static string TakeOver(int target)
        {
            if (running && port == target) return null;
            var peer = Peers.Probe(target, 600);
            if (peer != null && peer.Pid == HostInfo.Pid) return null;
            if (peer != null)
            {
                try
                {
                    var rq = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + target + "/release");
                    rq.Method = "POST"; rq.Timeout = 3000; rq.Proxy = null; rq.ContentLength = 0;
                    rq.Headers["X-Pid"] = HostInfo.Pid.ToString();
                    rq.Headers["X-Revit"] = HostInfo.Year;
                    using (rq.GetResponse()) { }
                }
                catch (WebException ex)
                {
                    var r = ex.Response as HttpWebResponse;
                    if (r != null && r.StatusCode == HttpStatusCode.Conflict)
                        return "Revit " + peer.Revit + " (pid " + peer.Pid + ") on port " + target + " is running a script right now. Wait for it to finish (or cancel it there) and try again.";
                    // nobody answered: the port may have been freed meanwhile, just try to start
                }
            }
            int was = running ? port : 0;
            if (running) Stop();
            for (int i = 0; i < 25 && !running; i++)
            {
                if (Start(target, false)) break;
                Thread.Sleep(120);
            }
            if (running) { Log.Write("Took port " + target + (peer != null ? " from Revit " + peer.Revit + " (pid " + peer.Pid + ")" : "")); return null; }
            if (was > 0) Start(was, true);           // could not take it: stay where this Revit was
            return "Port " + target + " is still busy (a program that is not a Revit bridge may hold it). Pick another port.";
        }

        // another Revit asked for this port: answer, let it go, and move to another free port of the pool
        static void Release(string who, string whoRevit)
        {
            int was = port;
            Stop();
            var t = new Thread(() =>
            {
                Thread.Sleep(1500);                   // the asking Revit binds the port first
                if (StartAny(was))
                    LastError = null;
                Moved = "Port " + was + " was taken by Revit " + whoRevit + (who != "" ? " (pid " + who + ")" : "") + "; this Revit moved to port " + (running ? port.ToString() : "-") + ".";
                Log.Write(Moved);
            }) { IsBackground = true, Name = "RevitMultiBridge move" };
            t.Start();
        }
        public static volatile string Moved;             // last "moved to another port" notice (shown by the window)

        // ---------- recent calls (columns of the Bridge window) ----------
        public static string[][] RecentCalls() { lock (recent) return recent.ToArray(); }

        static void AddRecent(Job j, string res)
        {
            var s = new[] { DateTime.Now.ToString("HH:mm:ss"), j.Tool.Replace("revit_", "") + (j.Execute ? " (execute)" : ""), res,
                            ((int)(DateTime.Now - j.T0).TotalMilliseconds) + " ms", j.FirstLine };
            lock (recent)
            {
                recent.Add(s);
                if (recent.Count > 100) recent.RemoveAt(0);
                recentVersion++;
            }
        }

        // ---------- HTTP ----------
        static void Loop(object o)
        {
            var l = (TcpListener)o;
            while (running && listener == l)
            {
                try
                {
                    var c = l.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(Serve, c);
                }
                catch (Exception ex)
                {
                    if (running && listener == l) Log.Write("accept: " + ex.Message);
                }
            }
        }

        // the port this connection came in on (the server may have moved meanwhile)
        static int LocalPort(TcpClient c) { try { return ((IPEndPoint)c.Client.LocalEndPoint).Port; } catch { return port; } }

        static void Serve(object o)
        {
            var c = (TcpClient)o;
            try
            {
                using (c)
                {
                    var s = c.GetStream();
                    s.ReadTimeout = 30000;
                    string head = ReadHead(s);
                    if (head == null) return;
                    var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    var first0 = lines[0].Split(' ');
                    string method = first0[0];
                    string path = first0.Length > 1 ? first0[1] : "/";
                    int len = 0;
                    string origin = null, host = null, ctype = "", who = "", whoRevit = "?";
                    foreach (var l in lines)
                    {
                        if (l.StartsWith("content-length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(l.Substring(15).Trim(), out len);
                        else if (l.StartsWith("origin:", StringComparison.OrdinalIgnoreCase)) origin = l.Substring(7).Trim();
                        else if (l.StartsWith("host:", StringComparison.OrdinalIgnoreCase)) host = l.Substring(5).Trim();
                        else if (l.StartsWith("content-type:", StringComparison.OrdinalIgnoreCase)) ctype = l.Substring(13).Trim();
                        else if (l.StartsWith("x-pid:", StringComparison.OrdinalIgnoreCase)) who = l.Substring(6).Trim();
                        else if (l.StartsWith("x-revit:", StringComparison.OrdinalIgnoreCase)) whoRevit = l.Substring(8).Trim();
                    }
                    // Only local tools may talk to the bridge: browsers always send Origin on a cross-site POST (blocks a web page
                    // posting a script), and a Host other than this machine means DNS rebinding.
                    string reject = RejectReason(method, path, origin, host, ctype, len, LocalPort(c));
                    if (reject != null)
                    {
                        Log.Write("Request refused (" + method + " " + path + ", origin " + (origin ?? "-") + ", host " + (host ?? "-") + "): " + reject);
                        if (len > 0 && len <= MaxBody) ReadExact(s, len);
                        Send(s, reject.StartsWith("body") ? 413 : 403, Json.Write(new Dictionary<string, object> { { "error", reject } }), false);
                        return;
                    }
                    byte[] body = ReadExact(s, len);

                    if (method == "POST" && path.StartsWith("/mcp"))
                    {
                        int code; string resp; bool init;
                        Mcp(Encoding.UTF8.GetString(body), out code, out resp, out init);
                        Send(s, code, resp, init);
                    }
                    else if (method == "POST" && path == "/release")
                    {
                        if (Jobs.Current != null) { Send(s, 409, "{\"released\":false,\"reason\":\"script running\"}", false); return; }
                        Send(s, 200, "{\"released\":true}", false);
                        Release(who, whoRevit);
                    }
                    else if (method == "GET" && (path == "/" || path == "/status"))
                        Send(s, 200, Json.Write(Status()), false);
                    else
                        Send(s, 405, "", false);
                }
            }
            catch (Exception ex) { Log.Write("serve: " + ex.Message); }
        }

        // who is on this port (read by Peers.Probe: the gateway and the other Revit windows)
        public static Dictionary<string, object> Status()
        {
            var j = Jobs.Current;
            return new Dictionary<string, object> {
                { "server", Name }, { "version", Version }, { "pid", HostInfo.Pid }, { "port", port },
                { "revit", HostInfo.Year }, { "build", HostInfo.Build },
                { "document", HostInfo.ActiveDoc }, { "path", HostInfo.ActivePath }, { "view", HostInfo.ActiveView },
                { "documents", HostInfo.Docs }, { "busy", j != null }, { "job", j == null ? "" : j.Tool },
                { "started", HostInfo.Started.ToString("yyyy-MM-dd HH:mm:ss") } };
        }

        const int MaxBody = 16 * 1024 * 1024;

        // null = accepted; otherwise the reason (a reason starting with "body" is answered 413, the rest 403)
        static string RejectReason(string method, string path, string origin, string host, string ctype, int len, int localPort)
        {
            if (origin != null) return "requests from a web page (Origin header) are not accepted";
            if (!LocalHost(host, localPort)) return "host is not this machine";
            if (len < 0 || len > MaxBody) return "body too large (" + len + " bytes, max " + MaxBody + ")";
            if (method == "POST" && path.StartsWith("/mcp") && !ctype.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                return "content type must be application/json";
            return null;
        }

        static bool LocalHost(string host, int localPort)
        {
            if (string.IsNullOrEmpty(host)) return false;
            string h = host.ToLowerInvariant(), p = ":" + localPort;
            if (h.EndsWith(p)) h = h.Substring(0, h.Length - p.Length);
            return h == "localhost" || h == "127.0.0.1" || h == "[::1]";
        }

        static string ReadHead(NetworkStream s)
        {
            var buf = new List<byte>();
            while (buf.Count < 65536)
            {
                int b = s.ReadByte();
                if (b < 0) return null;
                buf.Add((byte)b);
                int k = buf.Count;
                if (k >= 4 && buf[k - 4] == 13 && buf[k - 3] == 10 && buf[k - 2] == 13 && buf[k - 1] == 10)
                    return Encoding.ASCII.GetString(buf.ToArray(), 0, k - 4);
            }
            return null;
        }

        static byte[] ReadExact(NetworkStream s, int len)
        {
            var b = new byte[len];
            int got = 0;
            while (got < len)
            {
                int r = s.Read(b, got, len - got);
                if (r <= 0) break;
                got += r;
            }
            return b;
        }

        static void Send(NetworkStream s, int code, string body, bool sessionHeader)
        {
            var bytes = Encoding.UTF8.GetBytes(body ?? "");
            string reason = code == 200 ? "OK" : code == 202 ? "Accepted" : code == 405 ? "Method Not Allowed" : code == 409 ? "Conflict" : code == 403 ? "Forbidden" : code == 413 ? "Payload Too Large" : "Error";
            var h = new StringBuilder();
            h.Append("HTTP/1.1 " + code + " " + reason + "\r\n");
            h.Append("Content-Type: application/json; charset=utf-8\r\n");
            h.Append("Content-Length: " + bytes.Length + "\r\n");
            if (sessionHeader) h.Append("Mcp-Session-Id: " + Guid.NewGuid().ToString("N") + "\r\n");
            h.Append("Connection: close\r\n\r\n");
            var hb = Encoding.ASCII.GetBytes(h.ToString());
            s.Write(hb, 0, hb.Length);
            s.Write(bytes, 0, bytes.Length);
            s.Flush();
        }

        // ---------- MCP (JSON-RPC over HTTP POST) ----------
        static void Mcp(string body, out int code, out string resp, out bool init)
        {
            code = 200; init = false;
            Dictionary<string, object> req;
            try { req = (Dictionary<string, object>)Json.Parse(body); }
            catch { resp = Json.Write(Err(null, -32700, "Parse error")); return; }
            object id = req.ContainsKey("id") ? req["id"] : null;
            string m = req.ContainsKey("method") ? req["method"] as string : null;
            var prms = req.ContainsKey("params") ? req["params"] as Dictionary<string, object> : null;
            if (prms == null) prms = new Dictionary<string, object>();

            if (id == null) { code = 202; resp = ""; return; } // notification

            object result;
            if (m == "initialize")
            {
                init = true;
                result = new Dictionary<string, object> {
                    { "protocolVersion", prms.ContainsKey("protocolVersion") ? prms["protocolVersion"] : "2025-03-26" },
                    { "capabilities", new Dictionary<string, object> { { "tools", new Dictionary<string, object>() } } },
                    { "serverInfo", new Dictionary<string, object> { { "name", "revit-" + HostInfo.Year + "-port-" + port }, { "version", Version } } },
                    { "instructions", "One Revit " + HostInfo.Year + " window (pid " + HostInfo.Pid + ", port " + port + "). Runs C# script code inside its active document. " +
                                      "Globals: Doc, UIDoc, App, UIApp, Print(object), CancelToken. revit_read always rolls back. " +
                                      "revit_write is a dry run unless execute=true (only after the user approves the dry-run summary)." }
                };
            }
            else if (m == "ping") result = new Dictionary<string, object>();
            else if (m == "tools/list") result = new Dictionary<string, object> { { "tools", ToolList(false) } };
            else if (m == "tools/call")
            {
                string name = prms.ContainsKey("name") ? prms["name"] as string : "";
                var args = prms.ContainsKey("arguments") ? prms["arguments"] as Dictionary<string, object> : null;
                if (args == null) args = new Dictionary<string, object>();
                bool isError;
                string text = Call(name, args, out isError);
                result = new Dictionary<string, object> {
                    { "content", new object[] { new Dictionary<string, object> { { "type", "text" }, { "text", text } } } },
                    { "isError", isError } };
            }
            else { resp = Json.Write(Err(id, -32601, "Method not found: " + m)); return; }

            resp = Json.Write(new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", id }, { "result", result } });
        }

        static Dictionary<string, object> Err(object id, int c, string msg)
        {
            return new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", id },
                { "error", new Dictionary<string, object> { { "code", c }, { "message", msg } } } };
        }

        static object Tool(string name, string desc, Dictionary<string, object> props, string[] required)
        {
            return new Dictionary<string, object> { { "name", name }, { "description", desc },
                { "inputSchema", new Dictionary<string, object> { { "type", "object" }, { "properties", props }, { "required", required } } } };
        }

        static Dictionary<string, object> P(string type, string desc)
        {
            return new Dictionary<string, object> { { "type", type }, { "description", desc } };
        }

        // the same list serves the Revit side (one instance) and the gateway (withTarget = adds "target" + revit_instances)
        public static object[] ToolList(bool withTarget)
        {
            const string g = " Globals: Doc, UIDoc, App (Application), UIApp (UIApplication), Print(object), CancelToken (call CancelToken.ThrowIfCancellationRequested() in loops). " +
                             "Default usings: System, System.Linq, System.Collections.Generic, System.Text, System.IO, Autodesk.Revit.DB(.Structure/.Architecture), Autodesk.Revit.UI(.Selection). " +
                             "Statements run top-level (C# script); classes and extra 'using' lines are allowed. Do not start your own Transaction. " +
                             "Use id.Value (Revit 2024+) rather than ElementId.IntegerValue (removed in 2026).";
            Func<Dictionary<string, object>, Dictionary<string, object>> T = props =>
            {
                if (withTarget) props["target"] = P("string", "Which Revit window: its port (\"5311\"), \"pid:1234\", a Revit year (\"2025\") or part of the active document name. " +
                                                              "Optional when only one Revit is running; see revit_instances.");
                return props;
            };
            var code = P("string", "C# script code.");
            var timeout = P("integer", "Seconds to wait for the result (default 590, max 3600).");
            var list = new List<object> {
                Tool("revit_read", "Run C# in the active document inside a transaction that is ALWAYS rolled back." + g,
                     T(new Dictionary<string, object> { { "code", code }, { "timeout", timeout } }), new[] { "code" }),
                Tool("revit_write", "Run C# that modifies the active document. Dry run by default (rolled back, reports added/modified/deleted counts). " +
                     "execute=true commits as one undo step - only after the user approved the dry run." + g,
                     T(new Dictionary<string, object> { { "code", code }, { "execute", P("boolean", "Commit the changes (default false).") }, { "timeout", timeout } }), new[] { "code" }),
                Tool("revit_write_unsafe", "Run C# with NO host transaction (script manages its own Transactions, e.g. to save, sync or open views). Changes are real. Only with explicit user approval.",
                     T(new Dictionary<string, object> { { "code", code }, { "timeout", timeout } }), new[] { "code" }),
                Tool("revit_status", "Revit version, active document/view, open documents, and whether a script is running.",
                     T(new Dictionary<string, object>()), new string[0]),
                Tool("revit_cancel", "Request cancellation of the running script (works where the script calls CancelToken.ThrowIfCancellationRequested()).",
                     T(new Dictionary<string, object>()), new string[0])
            };
            if (withTarget)
                list.Insert(0, Tool("revit_instances", "List every Revit window that runs the bridge on this PC (any version, several windows of the same version): " +
                                    "port, Revit year, pid, active document, open documents, busy. Call it first when several Revit are open, then pass 'target'.",
                                    new Dictionary<string, object>(), new string[0]));
            return list.ToArray();
        }

        static string Call(string name, Dictionary<string, object> args, out bool isError)
        {
            isError = false;
            var cur = Jobs.Current;
            if (name == "revit_cancel")
            {
                if (cur == null) return "Nothing is running in Revit " + HostInfo.Year + " (port " + port + ").";
                cur.Cts.Cancel();
                return "Cancellation requested for " + cur.Tool + " (running " + (int)(DateTime.Now - cur.T0).TotalSeconds + " s).";
            }
            if (name != "revit_read" && name != "revit_write" && name != "revit_write_unsafe" && name != "revit_status")
            {
                isError = true;
                return "Unknown tool: " + name;
            }
            if (name == "revit_status" && cur != null)
                return "Revit " + HostInfo.Year + " (pid " + HostInfo.Pid + ", port " + port + ")\nBUSY: " + cur.Tool + (cur.Started ? " running " : " waiting for Revit ") + (int)(DateTime.Now - cur.T0).TotalSeconds + " s.";
            if (Backend == null) { isError = true; return "The bridge has no Revit backend."; }

            var j = new Job();
            j.Tool = name;
            j.Execute = args.ContainsKey("execute") && args["execute"] is bool && (bool)args["execute"];
            int timeout = 590;
            if (args.ContainsKey("timeout")) { try { timeout = Math.Max(5, Math.Min(3600, Convert.ToInt32(args["timeout"]))); } catch { } }

            if (name != "revit_status")
            {
                string code = args.ContainsKey("code") ? args["code"] as string : null;
                if (string.IsNullOrWhiteSpace(code)) { isError = true; return "Missing 'code'."; }
                j.Code = code;
                j.FirstLine = code.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x != "" && !x.StartsWith("//") && !x.StartsWith("CancelToken")) ?? "";
                if (j.FirstLine.Length > 60) j.FirstLine = j.FirstLine.Substring(0, 60) + "…";
                string errors;
                try { errors = Backend.Compile(j); }
                catch (Exception ex) { isError = true; Log.Write("compile crash: " + ex); return "Compiler crashed: " + ex.Message; }
                if (errors != null) { isError = true; AddRecent(j, "FAILED (compile)"); return errors; }
            }

            lock (busyLock)
            {
                if (Jobs.Current != null) { isError = true; return "BUSY: another script is running in this Revit (" + Jobs.Current.Tool + "). Use revit_status / revit_cancel."; }
                Jobs.Current = j;
            }
            try
            {
                string refused = Backend.Raise();
                if (refused != null) { isError = true; j.Abandoned = true; return "Revit refused the request (" + refused + ")."; }
                if (!j.Done.WaitOne(TimeSpan.FromSeconds(timeout)))
                {
                    if (!j.Started)
                    {
                        j.Abandoned = true;
                        isError = true;
                        AddRecent(j, "TIMEOUT");
                        return "Revit did not pick up the request in " + timeout + " s (a dialog open, an edit mode active, or Revit busy?). Nothing ran.";
                    }
                    j.Cts.Cancel();
                    if (!j.Done.WaitOne(TimeSpan.FromSeconds(30)))
                    {
                        isError = true;
                        AddRecent(j, "TIMEOUT");
                        return "TIMEOUT after " + timeout + " s; cancellation requested but the script is still running. Check revit_status.";
                    }
                }
                isError = j.IsError;
                AddRecent(j, !j.IsError ? "OK" : j.Result.Contains("CANCELLED") ? "CANCELLED" : "FAILED");
                Log.Write(name + (j.Execute ? " (execute)" : "") + " " + (int)(DateTime.Now - j.T0).TotalMilliseconds + " ms" + (j.IsError ? " ERROR" : ""));
                return j.Result;
            }
            finally
            {
                lock (busyLock)
                {
                    // keep the job "current" while it is still running on Revit's thread
                    if (j.Done.WaitOne(0) || j.Abandoned) { if (Jobs.Current == j) Jobs.Current = null; }
                    else ThreadPool.QueueUserWorkItem(_ => { j.Done.WaitOne(); lock (busyLock) { if (Jobs.Current == j) Jobs.Current = null; } });
                }
            }
        }
    }
}
