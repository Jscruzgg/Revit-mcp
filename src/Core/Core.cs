// Revit Multi Bridge - pieces that do not touch the Revit API (so they can be tested outside Revit): log, host info, job.
// C# 5 syntax only (compiled with the .NET Framework csc for Revit 2024, Roslyn for 2025+).
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RevitMultiBridge
{
    // ---------- one log for every Revit on this PC; each line says which process and Revit wrote it ----------
    static class Log
    {
        static readonly object L = new object();
        public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "revit_multi_bridge");
        public static readonly string PathName = Path.Combine(Root, "bridge.log");
        public const long MaxBytes = 2L * 1024 * 1024;

        public static void Write(string s)
        {
            try
            {
                Directory.CreateDirectory(Root);
                string who = "[" + HostInfo.Pid + " R" + HostInfo.Year + (Server.Port > 0 ? " :" + Server.Port : "") + "]";
                lock (L) File.AppendAllText(PathName, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + who + "  " + s + "\r\n");
            }
            catch { }   // another Revit writing at this very moment: the line is lost, never the session
        }

        // at Revit start, a log over 2 MB becomes bridge.old.log
        public static void Rotate()
        {
            try
            {
                var f = new FileInfo(PathName);
                if (!f.Exists || f.Length <= MaxBytes) return;
                string old = Path.ChangeExtension(PathName, ".old.log");
                lock (L) { if (File.Exists(old)) File.Delete(old); File.Move(PathName, old); }
            }
            catch { }
        }
    }

    // ---------- what this Revit is; the Revit side fills it (Idling, on Revit's thread), the server only reads it ----------
    static class HostInfo
    {
        public static string Year = "?";                 // 2024 / 2025 / 2026 / 2027
        public static string Build = "";
        public static string Dir = "";                   // folder of the add-in dll
        public static readonly int Pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        public static readonly DateTime Started = DateTime.Now;
        public static volatile string ActiveDoc = "";     // title of the active document ("" = none)
        public static volatile string ActivePath = "";
        public static volatile string ActiveView = "";
        public static volatile string[] Docs = new string[0];
        public static string Label { get { return "Revit " + Year + (ActiveDoc == "" ? "" : " · " + ActiveDoc); } }
    }

    // ---------- one request = one job, run on Revit's thread ----------
    class Job
    {
        public string Tool;            // revit_read | revit_write | revit_write_unsafe | revit_status
        public bool Execute;
        public string Code;
        public Func<object[], Task<object>> Runner;
        public byte[] Pdb;
        public string FirstLine = "";
        public CancellationTokenSource Cts = new CancellationTokenSource();
        public ManualResetEvent Done = new ManualResetEvent(false);
        public volatile bool Started;
        public volatile bool Abandoned;
        public DateTime T0 = DateTime.Now;
        public string Result = "";
        public bool IsError;
        public List<string> Warnings = new List<string>();
        public int Added, Modified, Deleted;
    }

    static class Jobs
    {
        internal static volatile Job Current;            // at most one script per Revit at a time (Revit's API thread is single)
    }

    // ---------- what the server needs from Revit: compile a script and hand a job to Revit's thread ----------
    interface IBackend
    {
        string Compile(Job j);       // null = compiled (j.Runner set), otherwise the compile errors
        string Raise();              // null = Revit accepted the request, otherwise the reason
    }
}
