// Revit Multi Bridge - script engine (from Claude Revit Bridge, unchanged logic): C# script compiled with Roslyn (.\lib),
// run on Revit's API thread inside a transaction (read = rolled back, write = dry run unless execute=true, unsafe = no host transaction).
// C# 5 syntax only (.NET Framework csc for Revit 2024, Roslyn for 2025+).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using MetadataReaderProvider = System.Reflection.Metadata.MetadataReaderProvider;
using MetadataTokens = System.Reflection.Metadata.Ecma335.MetadataTokens;
using WF = System.Windows.Forms;

namespace RevitMultiBridge
{
    // ---------- globals visible to scripts ----------
    public class ScriptGlobals
    {
        public Document Doc;
        public UIDocument UIDoc;
        public Autodesk.Revit.ApplicationServices.Application App;
        public UIApplication UIApp;
        public CancellationToken CancelToken;
        internal StringBuilder Out = new StringBuilder();
        public void Print(object o) { Out.AppendLine(o == null ? "null" : o.ToString()); }
    }

    // what the (Revit-free) server asks of Revit
    class RevitBackend : IBackend
    {
        public string Compile(Job j)
        {
            string errors;
            j.Runner = Compiler.Compile(j.Code, out errors, out j.Pdb);
            return j.Runner == null ? errors : null;
        }

        public string Raise()
        {
            var r = BridgeApp.Event.Raise();
            return r == ExternalEventRequest.Accepted || r == ExternalEventRequest.Pending ? null : r.ToString();
        }
    }

    class FailureCollector : IFailuresPreprocessor
    {
        readonly Job job;
        public FailureCollector(Job j) { job = j; }
        public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
        {
            bool err = false;
            foreach (var m in fa.GetFailureMessages())
            {
                string ids = string.Join(",", m.GetFailingElementIds().Select(x => x.ToString()).Take(10).ToArray());
                if (m.GetSeverity() == FailureSeverity.Warning)
                {
                    job.Warnings.Add("warning: " + m.GetDescriptionText() + (ids == "" ? "" : " [" + ids + "]"));
                    fa.DeleteWarning(m);
                }
                else
                {
                    job.Warnings.Add("ERROR: " + m.GetDescriptionText() + (ids == "" ? "" : " [" + ids + "]"));
                    err = true;
                }
            }
            return err ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }

    class JobHandler : IExternalEventHandler
    {
        internal static Job Current { get { return Jobs.Current; } }
        static Job collecting;

        public string GetName() { return "Revit Multi Bridge"; }

        internal static void OnDocChanged(object s, DocumentChangedEventArgs e)
        {
            var j = collecting;
            if (j == null) return;
            j.Added += e.GetAddedElementIds().Count;
            j.Modified += e.GetModifiedElementIds().Count;
            j.Deleted += e.GetDeletedElementIds().Count;
        }

        public void Execute(UIApplication uiapp)
        {
            var j = Current;
            if (j == null || j.Abandoned || j.Started) return;
            j.Started = true;
            ScriptGlobals g = new ScriptGlobals();
            try
            {
                Run(uiapp, j, g);
            }
            catch (Exception ex)
            {
                j.IsError = true;
                j.Result = g.Out.ToString() + "\n--- " + FormatEx(ex, j) + "\n--- nothing changed" + (j.Tool == "revit_write_unsafe" ? " by the host (script's own committed transactions stay)" : "");
                if (j.Warnings.Count > 0) j.Result += "\n" + string.Join("\n", j.Warnings.Take(30).ToArray());
            }
            finally
            {
                collecting = null;
                j.Done.Set();
            }
        }

        static void Run(UIApplication uiapp, Job j, ScriptGlobals g)
        {
            var uidoc = uiapp.ActiveUIDocument;
            if (j.Tool == "revit_status")
            {
                var a = uiapp.Application;
                g.Print("Revit: " + a.VersionName + " (" + a.VersionBuild + ")");
                g.Print("Active document: " + (uidoc == null ? "(none)" : uidoc.Document.Title + " | " + uidoc.Document.PathName));
                if (uidoc != null)
                {
                    g.Print("Active view: " + uidoc.ActiveView.Name + " (" + uidoc.ActiveView.ViewType + ")");
                    g.Print("Selected elements: " + uidoc.Selection.GetElementIds().Count);
                }
                foreach (Document d in a.Documents) g.Print("Open: " + d.Title + (d.IsLinked ? " (link)" : "") + (d.IsFamilyDocument ? " (family)" : ""));
                j.Result = g.Out.ToString();
                return;
            }
            if (uidoc == null) throw new InvalidOperationException("No active document in Revit.");
            var doc = uidoc.Document;
            g.Doc = doc; g.UIDoc = uidoc; g.App = uiapp.Application; g.UIApp = uiapp; g.CancelToken = j.Cts.Token;

            if (j.Tool == "revit_write_unsafe")
            {
                object r = Invoke(j, g);
                j.Result = Output(g, r) + "\n--- UNSAFE: no host transaction; whatever the script committed is in the model.";
                return;
            }

            bool write = j.Tool == "revit_write";
            string footer;
            using (var tg = new TransactionGroup(doc, "Claude: " + (write ? "write" : "read")))
            {
                tg.Start();
                object r;
                using (var t = new Transaction(doc, "Claude script"))
                {
                    t.Start();
                    var fho = t.GetFailureHandlingOptions();
                    fho.SetFailuresPreprocessor(new FailureCollector(j));
                    fho.SetClearAfterRollback(true);
                    t.SetFailureHandlingOptions(fho);
                    try
                    {
                        r = Invoke(j, g);
                    }
                    catch
                    {
                        if (t.GetStatus() == TransactionStatus.Started) t.RollBack();
                        if (tg.GetStatus() == TransactionStatus.Started) tg.RollBack();
                        throw;
                    }
                    if (!write)
                    {
                        t.RollBack();
                    }
                    else
                    {
                        collecting = j;
                        var st = t.Commit();
                        collecting = null;
                        if (st != TransactionStatus.Committed)
                        {
                            if (tg.GetStatus() == TransactionStatus.Started) tg.RollBack();
                            throw new InvalidOperationException("Commit failed (" + st + "). " + string.Join(" | ", j.Warnings.ToArray()));
                        }
                    }
                }
                string counts = "added " + j.Added + ", modified " + j.Modified + ", deleted " + j.Deleted + " element(s)";
                if (!write) { tg.RollBack(); footer = "--- read: rolled back"; }
                else if (j.Execute) { tg.Assimilate(); footer = "--- EXECUTED (one undo step 'Claude: write'): " + counts; }
                else { tg.RollBack(); footer = "--- DRY RUN, rolled back: would have " + counts + ". Re-run with execute=true to apply."; }
                j.Result = Output(g, r) + "\n" + footer;
            }
            if (j.Warnings.Count > 0)
                j.Result += "\n--- " + j.Warnings.Count + " failure message(s):\n" + string.Join("\n", j.Warnings.Take(30).ToArray());
        }

        static object Invoke(Job j, ScriptGlobals g)
        {
            var task = j.Runner(new object[] { g, null });
            return task.GetAwaiter().GetResult();
        }

        static string Output(ScriptGlobals g, object r)
        {
            string s = g.Out.ToString();
            if (r != null) s += "=> " + r;
            return s.TrimEnd();
        }

        internal static string FormatEx(Exception ex, Job j)
        {
            while ((ex is AggregateException || ex is TargetInvocationException) && ex.InnerException != null) ex = ex.InnerException;
            if (ex is OperationCanceledException) return "CANCELLED";
            string s = "ERROR " + ex.GetType().Name + ": " + ex.Message;
            int line = j == null || j.Runner == null ? -1 : ScriptLine(ex, j.Pdb, j.Runner.Method.Module.Assembly);
            if (line > 0) s += "\n    at script line " + line;
            return s;
        }

        // Maps the innermost script stack frame (IL offset) to a source line using the portable PDB.
        static int ScriptLine(Exception ex, byte[] pdb, Assembly asm)
        {
            if (pdb == null) return -1;
            try
            {
                var frames = new System.Diagnostics.StackTrace(ex, false).GetFrames();
                if (frames == null) return -1;
                using (var prov = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(pdb)))
                {
                    var rd = prov.GetMetadataReader();
                    foreach (var f in frames)
                    {
                        var m = f.GetMethod();
                        if (m == null || m.Module.Assembly != asm) continue;
                        int il = f.GetILOffset();
                        if (il < 0) continue;
                        int line = -1;
                        foreach (var sp in rd.GetMethodDebugInformation(MetadataTokens.MethodDefinitionHandle(m.MetadataToken)).GetSequencePoints())
                        {
                            if (sp.IsHidden) continue;
                            if (sp.Offset <= il) line = sp.StartLine; else break;
                        }
                        if (line > 0) return line;
                    }
                }
            }
            catch { }
            return -1;
        }
    }

    // ---------- C# script compilation (Roslyn from .\lib) ----------
    static class Compiler
    {
        static List<MetadataReference> refs;
        static int n;
        static readonly string[] Usings = {
            "System", "System.Collections.Generic", "System.Linq", "System.Text", "System.IO",
            "Autodesk.Revit.DB", "Autodesk.Revit.DB.Structure", "Autodesk.Revit.DB.Architecture",
            "Autodesk.Revit.UI", "Autodesk.Revit.UI.Selection" };

        static List<MetadataReference> Refs()
        {
            if (refs != null) return refs;
            var paths = new List<string>();
            // .NET 8/10 (Revit 2025+): the framework is the list of trusted platform assemblies; take the ones of the
            // runtime (System.*) and of Windows Desktop (WinForms/WPF), not Revit's own dlls
            string tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (!string.IsNullOrEmpty(tpa))
            {
                var dirs = new[] { Path.GetDirectoryName(typeof(object).Assembly.Location), Path.GetDirectoryName(typeof(WF.Form).Assembly.Location) };
                foreach (var p in tpa.Split(Path.PathSeparator))
                    if (dirs.Any(d => string.Equals(Path.GetDirectoryName(p), d, StringComparison.OrdinalIgnoreCase))) paths.Add(p);
            }
            else
            {
                string fw = RuntimeEnvironment.GetRuntimeDirectory();
                foreach (var f in new[] { "mscorlib", "System", "System.Core", "System.Xml", "System.Xml.Linq", "System.Data",
                                          "System.Drawing", "System.Numerics", "Microsoft.CSharp", "System.Runtime", "netstandard" })
                {
                    string p = Path.Combine(fw, f + ".dll");
                    if (File.Exists(p)) paths.Add(p);
                }
            }
            string revitApi = typeof(Document).Assembly.Location;
            paths.Add(revitApi);
            paths.Add(Path.Combine(Path.GetDirectoryName(revitApi), "RevitAPIUI.dll"));
            paths.Add(typeof(ScriptGlobals).Assembly.Location);
            refs = paths.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
            return refs;
        }

        // Compile cache (9 Nov 2026): dry run -> execute sends the same code twice; the second call reuses the loaded assembly's delegate
        // (static entry point, a fresh script instance per call = no state kept) and its pdb. Only successful builds are cached; 20 entries, oldest dropped.
        static readonly object cacheLock = new object();
        static readonly Dictionary<string, KeyValuePair<Func<object[], Task<object>>, byte[]>> cache = new Dictionary<string, KeyValuePair<Func<object[], Task<object>>, byte[]>>();
        static readonly Queue<string> cacheOrder = new Queue<string>();

        // Returns null + errors on failure.
        public static Func<object[], Task<object>> Compile(string code, out string errors, out byte[] pdbBytes)
        {
            string key;
            using (var sha = System.Security.Cryptography.SHA256.Create()) key = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(code)));
            lock (cacheLock)
            {
                KeyValuePair<Func<object[], Task<object>>, byte[]> hit;
                if (cache.TryGetValue(key, out hit)) { errors = null; pdbBytes = hit.Value; return hit.Key; }
            }
            var runner = CompileUncached(code, out errors, out pdbBytes);
            // a script with "static" members would carry their values from the dry run into the execute run: never reused
            if (runner != null && !System.Text.RegularExpressions.Regex.IsMatch(code, @"\bstatic\b"))
                lock (cacheLock)
                {
                    if (!cache.ContainsKey(key))
                    {
                        if (cache.Count >= 20) cache.Remove(cacheOrder.Dequeue());
                        cacheOrder.Enqueue(key);
                    }
                    cache[key] = new KeyValuePair<Func<object[], Task<object>>, byte[]>(runner, pdbBytes);
                }
            return runner;
        }

        static Func<object[], Task<object>> CompileUncached(string code, out string errors, out byte[] pdbBytes)
        {
            errors = null;
            pdbBytes = null;
            var po = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Script);
            var tree = CSharpSyntaxTree.ParseText(SourceText.From(code, Encoding.UTF8), po, "script.csx");
            var co = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, usings: Usings,
                                                  optimizationLevel: OptimizationLevel.Debug, allowUnsafe: true);
            var comp = CSharpCompilation.CreateScriptCompilation("ClaudeScript" + Interlocked.Increment(ref n), tree, Refs(), co,
                                                                 null, typeof(object), typeof(ScriptGlobals));
            using (var pe = new MemoryStream())
            using (var pdb = new MemoryStream())
            {
                var r = comp.Emit(pe, pdb, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
                if (!r.Success)
                {
                    errors = "COMPILE ERRORS:\n" + string.Join("\n", r.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error).Take(20)
                        .Select(d => "  line " + (d.Location.GetLineSpan().StartLinePosition.Line + 1) + ": " + d.Id + " " + d.GetMessage()).ToArray());
                    return null;
                }
                pdbBytes = pdb.ToArray();
                var asm = Assembly.Load(pe.ToArray(), pdbBytes);
                var ep = comp.GetEntryPoint(CancellationToken.None);
                var type = asm.GetType(ep.ContainingType.MetadataName, true);
                var mi = type.GetMethod(ep.MetadataName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                return (Func<object[], Task<object>>)Delegate.CreateDelegate(typeof(Func<object[], Task<object>>), mi);
            }
        }
    }

    // ---------- compatibility (9 Nov 2026): the same source builds for Revit 2023 to 2026+ ----------
    // ElementId as int: IntegerValue up to Revit 2025 (same as before); Revit 2026 removed it (/define:REVIT2026_OR_GREATER).
    static class RevitIds
    {
        public static int IntId(this ElementId id)
        {
#if REVIT2026_OR_GREATER
            return checked((int)id.Value);
#else
            return id.IntegerValue;
#endif
        }
    }
}
