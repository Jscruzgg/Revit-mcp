// Revit Multi Bridge - gateway: ONE MCP server (stdio) for Claude that reaches EVERY Revit window running the bridge on this PC.
// Each Revit (any version 2024-2027, several windows of the same version) listens on its own port of the pool (5310-5329);
// the gateway finds them (GET /status on every port) and forwards each tool call to the one named by "target":
// a port ("5311"), "pid:1234", a Revit year ("2025") or part of the active document name. With one Revit open, target is optional.
// Calls to different Revit windows run in parallel. .NET Framework 4.8 console exe, C# 5 syntax (Framework csc).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace RevitMultiBridge
{
    static class Gateway
    {
        static readonly object outLock = new object();
        static Stream stdout;

        static int Main(string[] args)
        {
            HostInfo.Year = "gw";
            if (args.Length > 0 && args[0] == "--list")      // quick check from a terminal: RevitMultiBridgeGateway.exe --list
            {
                Console.WriteLine(Instances());
                return 0;
            }
            stdout = Console.OpenStandardOutput();
            var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            Log.Write("Gateway started (pool " + Server.First + "-" + Server.Last + ")");
            string line;
            while ((line = stdin.ReadLine()) != null)
            {
                if (line.Trim() == "") continue;
                string l = line;
                ThreadPool.QueueUserWorkItem(_ => Handle(l));
            }
            return 0;
        }

        static void Write(object msg)
        {
            var bytes = Encoding.UTF8.GetBytes(Json.Write(msg) + "\n");
            lock (outLock) { stdout.Write(bytes, 0, bytes.Length); stdout.Flush(); }
        }

        static void Handle(string line)
        {
            Dictionary<string, object> req;
            try { req = (Dictionary<string, object>)Json.Parse(line); }
            catch { Write(Err(null, -32700, "Parse error")); return; }
            object id = req.ContainsKey("id") ? req["id"] : null;
            string m = req.ContainsKey("method") ? req["method"] as string : null;
            var prms = req.ContainsKey("params") ? req["params"] as Dictionary<string, object> : null;
            if (prms == null) prms = new Dictionary<string, object>();
            if (id == null) return;                         // notification (notifications/initialized ...)
            try
            {
                object result;
                if (m == "initialize")
                    result = new Dictionary<string, object> {
                        { "protocolVersion", prms.ContainsKey("protocolVersion") ? prms["protocolVersion"] : "2025-03-26" },
                        { "capabilities", new Dictionary<string, object> { { "tools", new Dictionary<string, object>() } } },
                        { "serverInfo", new Dictionary<string, object> { { "name", "revit-multi-bridge" }, { "version", Server.Version } } },
                        { "instructions", "Reaches every Revit window (2024-2027, several at once) that runs the Revit Multi Bridge add-in. " +
                                          "Call revit_instances to see them; pass target (port, \"pid:N\", year or document name) when more than one is open. " +
                                          "revit_read always rolls back; revit_write is a dry run unless execute=true (only after the user approved the dry run)." } };
                else if (m == "ping") result = new Dictionary<string, object>();
                else if (m == "tools/list") result = new Dictionary<string, object> { { "tools", Server.ToolList(true) } };
                else if (m == "tools/call") result = Call(prms);
                else { Write(Err(id, -32601, "Method not found: " + m)); return; }
                Write(new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", id }, { "result", result } });
            }
            catch (Exception ex)
            {
                Log.Write("Gateway " + m + ": " + ex);
                Write(Err(id, -32603, "Gateway error: " + ex.Message));
            }
        }

        static Dictionary<string, object> Err(object id, int c, string msg)
        {
            return new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", id },
                { "error", new Dictionary<string, object> { { "code", c }, { "message", msg } } } };
        }

        static Dictionary<string, object> Text(string text, bool isError)
        {
            return new Dictionary<string, object> {
                { "content", new object[] { new Dictionary<string, object> { { "type", "text" }, { "text", text } } } },
                { "isError", isError } };
        }

        static string Instances()
        {
            var ps = Peers.Scan(Server.First, Server.Last, 250);
            if (ps.Count == 0) return NoneText();
            var sb = new StringBuilder(ps.Count + " Revit window(s) with the bridge (pool " + Server.First + "-" + Server.Last + "):\n");
            foreach (var p in ps)
            {
                sb.Append("- port " + p.Port + " | Revit " + p.Revit + (p.Build != "" ? " (" + p.Build + ")" : "") + " | pid " + p.Pid +
                          " | active: " + (p.Document == "" ? "(no document)" : p.Document) + (p.View != "" ? " / view " + p.View : "") +
                          (p.Busy ? " | BUSY (" + p.Job + ")" : " | ready") + "\n");
                if (p.Documents.Length > 1) sb.Append("    open: " + string.Join("; ", p.Documents) + "\n");
            }
            sb.Append("Use target = port (most precise), \"pid:N\", year or part of the document name.");
            return sb.ToString();
        }

        static string NoneText()
        {
            return "No Revit window with the Multi Bridge is running (ports " + Server.First + "-" + Server.Last + " checked). " +
                   "Open Revit 2024-2027 with the add-in installed, or press Start in its MCP > Multi Bridge window.";
        }

        // target -> one Revit; error = why not (none, ambiguous, not found)
        internal static Peer Resolve(string target, List<Peer> ps, out string error)
        {
            error = null;
            if (ps.Count == 0) { error = NoneText(); return null; }
            target = (target ?? "").Trim();
            List<Peer> hit;
            if (target == "")
            {
                if (ps.Count == 1) return ps[0];
                error = "Several Revit windows are open: pass 'target'.\n" + List(ps);
                return null;
            }
            int n;
            if (target.StartsWith("pid:", StringComparison.OrdinalIgnoreCase) && int.TryParse(target.Substring(4).Trim(), out n))
                hit = ps.Where(p => p.Pid == n).ToList();
            else if (int.TryParse(target, out n) && n >= 2000 && n <= 2100)
                hit = ps.Where(p => p.Revit == target).ToList();
            else if (int.TryParse(target, out n))
                hit = ps.Where(p => p.Port == n).ToList();
            else
            {
                string t = target.ToLowerInvariant();
                hit = ps.Where(p => p.Document.ToLowerInvariant().Contains(t) || p.Path.ToLowerInvariant().Contains(t)).ToList();
                if (hit.Count == 0) hit = ps.Where(p => p.Documents.Any(d => d.ToLowerInvariant().Contains(t))).ToList();
            }
            if (hit.Count == 1) return hit[0];
            error = (hit.Count == 0 ? "No Revit window matches target '" + target + "'." : "Target '" + target + "' matches " + hit.Count + " Revit windows: use the port.") + "\n" + List(ps);
            return null;
        }

        static string List(List<Peer> ps)
        {
            return string.Join("\n", ps.Select(p => "- " + p.Label + (p.Busy ? " BUSY" : "")).ToArray());
        }

        static object Call(Dictionary<string, object> prms)
        {
            string name = prms.ContainsKey("name") ? prms["name"] as string : "";
            var args = prms.ContainsKey("arguments") ? prms["arguments"] as Dictionary<string, object> : null;
            if (args == null) args = new Dictionary<string, object>();
            if (name == "revit_instances") return Text(Instances(), false);

            string target = args.ContainsKey("target") && args["target"] != null ? args["target"].ToString() : "";
            string error;
            var peer = Resolve(target, Peers.Scan(Server.First, Server.Last, 250), out error);
            if (peer == null) return Text(error, true);

            var fwd = new Dictionary<string, object>(args);
            fwd.Remove("target");
            int timeout = 590;
            if (args.ContainsKey("timeout")) { try { timeout = Math.Max(5, Math.Min(3600, Convert.ToInt32(args["timeout"]))); } catch { } }
            string body = Json.Write(new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", 1 }, { "method", "tools/call" },
                                                                       { "params", new Dictionary<string, object> { { "name", name }, { "arguments", fwd } } } });
            string head = "[" + peer.Label + "]\n";
            string resp;
            try { resp = Peers.Post(peer.Port, body, (timeout + 60) * 1000); }
            catch (WebException ex)
            {
                return Text(head + "Could not reach the bridge on port " + peer.Port + ": " + ex.Message + " (Revit closed, or the bridge moved to another port? See revit_instances.)", true);
            }
            var d = Json.Parse(resp) as Dictionary<string, object>;
            if (d != null && d.ContainsKey("error"))
            {
                var e = d["error"] as Dictionary<string, object>;
                return Text(head + "Bridge error: " + (e != null && e.ContainsKey("message") ? e["message"] : resp), true);
            }
            var result = d != null && d.ContainsKey("result") ? d["result"] as Dictionary<string, object> : null;
            if (result == null) return Text(head + "Unexpected answer from the bridge: " + resp, true);
            // say which Revit answered (first text block)
            var content = result.ContainsKey("content") ? result["content"] as object[] : null;
            if (content != null && content.Length > 0)
            {
                var c0 = content[0] as Dictionary<string, object>;
                if (c0 != null && c0.ContainsKey("text")) c0["text"] = head + c0["text"];
            }
            return result;
        }
    }
}
