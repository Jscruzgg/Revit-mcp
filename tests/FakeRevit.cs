// Test double of one Revit window: the real Server + BridgeWindow (src\Core) with a fake backend instead of the Revit API.
// Several of them at once = several Revit windows (any version) taking ports of the pool.
// Usage: FakeRevit.exe <year> <document> [--seconds N] [--port N] [--snapshot out.png] [--show]
//   fake scripts: code containing "COMPILE_ERROR" fails to compile, "sleep:N" runs N ms, "fail" ends with an error.
using System;
using System.Linq;
using System.Threading;

namespace RevitMultiBridge
{
    class FakeBackend : IBackend
    {
        public string Compile(Job j) { return j.Code.Contains("COMPILE_ERROR") ? "COMPILE ERRORS:\n  line 1: CS1002 ; expected" : null; }
        public string Raise()
        {
            var j = Jobs.Current;
            new Thread(() =>
            {
                j.Started = true;
                int ms = 50;
                var m = System.Text.RegularExpressions.Regex.Match(j.Code ?? "", @"sleep:(\d+)");
                if (m.Success) ms = int.Parse(m.Groups[1].Value);
                var t0 = DateTime.Now;
                while ((DateTime.Now - t0).TotalMilliseconds < ms && !j.Cts.IsCancellationRequested) Thread.Sleep(10);
                if (j.Cts.IsCancellationRequested) { j.IsError = true; j.Result = "--- CANCELLED"; }
                else if (j.Tool == "revit_status") j.Result = "Revit: Autodesk Revit " + HostInfo.Year + " (fake)\nActive document: " + HostInfo.ActiveDoc;
                else if ((j.Code ?? "").Contains("fail")) { j.IsError = true; j.Result = "--- ERROR InvalidOperationException: fake failure"; }
                else j.Result = "ran in fake Revit " + HostInfo.Year + " / " + HostInfo.ActiveDoc + ": " + j.FirstLine + "\n--- " + (j.Tool == "revit_write" && j.Execute ? "EXECUTED" : "read: rolled back");
                j.Done.Set();
            }) { IsBackground = true }.Start();
            return null;
        }
    }

    static class FakeRevit
    {
        [STAThread]
        static int Main(string[] a)
        {
            HostInfo.Year = a.Length > 0 ? a[0] : "2025";
            HostInfo.ActiveDoc = a.Length > 1 ? a[1] : "Fake model";
            HostInfo.ActivePath = @"C:\Projects\" + HostInfo.ActiveDoc + ".rvt";
            HostInfo.ActiveView = "Level 1";
            HostInfo.Docs = new[] { HostInfo.ActiveDoc };
            HostInfo.Dir = AppDomain.CurrentDomain.BaseDirectory;
            Func<string, string> opt = k => { int i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; };
            Server.Backend = new FakeBackend();
            int port; bool ok = int.TryParse(opt("--port") ?? "", out port) ? Server.Start(port, true) : Server.StartAny(0);
            Console.WriteLine(ok ? "PORT " + Server.Port : "NOPORT " + Server.LastError);
            Console.Out.Flush();
            int rc;
            if (int.TryParse(opt("--reclaim") ?? "", out rc))
            {
                string msg = Server.TakeOver(rc);
                Console.WriteLine(msg == null ? "RECLAIMED " + Server.Port : "RECLAIM FAILED " + msg);
                Console.Out.Flush();
            }
            string snap = opt("--snapshot");
            if (snap != null)
            {
                System.Windows.Forms.Application.EnableVisualStyles();
                int wait; if (!int.TryParse(opt("--wait") ?? "", out wait)) wait = 3000;
                BridgeWindow.Snapshot(snap, wait);
                Console.WriteLine("SNAPSHOT " + snap);
            }
            if (a.Contains("--show"))
            {
                System.Windows.Forms.Application.EnableVisualStyles();
                BridgeWindow.ShowIt(IntPtr.Zero);
                System.Windows.Forms.Application.Run();
            }
            int secs; if (!int.TryParse(opt("--seconds") ?? "", out secs)) secs = 60;
            var t0 = DateTime.Now;
            while ((DateTime.Now - t0).TotalSeconds < secs) Thread.Sleep(200);
            Server.Stop();
            return 0;
        }
    }
}
