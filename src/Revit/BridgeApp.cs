// Revit Multi Bridge - add-in entry (Revit 2024-2027). Each Revit window that starts takes the first free port of the pool
// (5310-5329 by default, the same pool for every version) and serves MCP there; ribbon tab "MCP" > "Multi Bridge" opens the status window.
// C# 5 syntax only (.NET Framework csc for Revit 2024, Roslyn for 2025+).
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SD = System.Drawing;

namespace RevitMultiBridge
{
    public class BridgeApp : IExternalApplication
    {
        internal static ExternalEvent Event;
        internal static JobHandler Handler;
        static DateTime lastInfo = DateTime.MinValue;

        public Result OnStartup(UIControlledApplication app)
        {
            HostInfo.Dir = Path.GetDirectoryName(typeof(BridgeApp).Assembly.Location);
            try { HostInfo.Year = app.ControlledApplication.VersionNumber; HostInfo.Build = app.ControlledApplication.VersionBuild; } catch { }
            Log.Rotate();
            AppDomain.CurrentDomain.AssemblyResolve += OnResolve;
            PreloadRoslyn();
            try
            {
                Handler = new JobHandler();
                Event = ExternalEvent.Create(Handler);
                app.ControlledApplication.DocumentChanged += JobHandler.OnDocChanged;
                app.Idling += OnIdling;
                app.ViewActivated += (s, e) => { lastInfo = DateTime.MinValue; };
                Server.Backend = new RevitBackend();
                if ((Environment.GetEnvironmentVariable("REVIT_MULTI_BRIDGE_AUTOSTART") ?? "").Trim().ToLowerInvariant() != "off") Server.StartAny(0);
                else Log.Write("Autostart off (REVIT_MULTI_BRIDGE_AUTOSTART=off): press Start in the Multi Bridge window.");
                AddRibbon(app);
            }
            catch (Exception ex) { Log.Write("STARTUP ERROR: " + ex); }
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            Server.Stop();
            return Result.Succeeded;
        }

        // what /status reports (active document, view, open documents), read on Revit's thread at most once a second
        static void OnIdling(object sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
        {
            if ((DateTime.Now - lastInfo).TotalMilliseconds < 1000) return;
            lastInfo = DateTime.Now;
            try
            {
                var uiapp = sender as UIApplication;
                if (uiapp == null) return;
                var uidoc = uiapp.ActiveUIDocument;
                HostInfo.ActiveDoc = uidoc == null ? "" : uidoc.Document.Title;
                HostInfo.ActivePath = uidoc == null ? "" : uidoc.Document.PathName ?? "";
                HostInfo.ActiveView = uidoc == null || uidoc.ActiveView == null ? "" : uidoc.ActiveView.Name;
                HostInfo.Docs = uiapp.Application.Documents.Cast<Document>().Where(d => !d.IsLinked).Select(d => d.Title + (d.IsFamilyDocument ? " (family)" : "")).ToArray();
            }
            catch { }
        }

        static void AddRibbon(UIControlledApplication app)
        {
            try
            {
                // own tab: the older RevitMCP add-in already has tab "MCP" with a panel "Claude" (same panel name = Revit refuses it,
                // and the button did not appear, 21 Nov 2026). A taken panel name falls back to a unique one.
                const string tab = "Claude Bridge";
                try { app.CreateRibbonTab(tab); } catch { }   // already there
                RibbonPanel panel;
                try { panel = app.CreateRibbonPanel(tab, "MCP"); }
                catch (Autodesk.Revit.Exceptions.ArgumentException) { panel = app.CreateRibbonPanel(tab, "MCP Multi Bridge"); }
                var pbd = new PushButtonData("RevitMultiBridge_Window", "MCP\nBridge", typeof(BridgeApp).Assembly.Location, "RevitMultiBridge.ShowWindowCommand");
                pbd.ToolTip = "Revit Multi Bridge: status of this Revit (port, RUNNING / STOPPED), every Revit bridge on this PC, recent calls, Start / Stop, Re-claim, Cancel.";
                pbd.LargeImage = Icon(32); pbd.Image = Icon(16);
                panel.AddItem(pbd);
            }
            catch (Exception ex) { Log.Write("Ribbon: " + ex); }
        }

        static System.Windows.Media.ImageSource Icon(int size)
        {
            try
            {
                string f = Path.Combine(Path.Combine(HostInfo.Dir, "icons"), "mcp.png");
                if (File.Exists(f))
                {
                    var bi = new System.Windows.Media.Imaging.BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;   // file not locked
                    bi.StreamSource = new MemoryStream(File.ReadAllBytes(f));
                    bi.DecodePixelWidth = size;
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
            }
            catch (Exception ex) { Log.Write("Icon: " + ex.Message); }
            using (var bmp = new SD.Bitmap(size, size))
            {
                using (var g = SD.Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
                    using (var b = new SD.SolidBrush(Ui.Brand)) g.FillEllipse(b, 0, 0, size - 1, size - 1);
                    using (var f = new SD.Font("Segoe UI", size * 0.5f, SD.FontStyle.Bold, SD.GraphicsUnit.Pixel))
                    using (var sf = new SD.StringFormat { Alignment = SD.StringAlignment.Center, LineAlignment = SD.StringAlignment.Center })
                        g.DrawString("M", f, SD.Brushes.White, new SD.RectangleF(0, 0, size, size), sf);
                }
                IntPtr h = bmp.GetHbitmap();
                try { return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(h, IntPtr.Zero, System.Windows.Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions()); }
                finally { DeleteObject(h); }
            }
        }

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr h);

        // Revit 2025+ (.NET 8/10): one copy of each assembly per process, the first one loaded wins. Another add-in shipping an OLDER
        // Roslyn (RevitMCP 2026 has 4.8) loaded first would make ours (4.13) fail. Loading ours at startup means the newest one is in
        // the process, and the older requests are satisfied by it. On .NET Framework (Revit 2024) OnResolve is enough.
        static void PreloadRoslyn()
        {
            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") == null) return;
            foreach (var n in new[] { "System.Collections.Immutable", "System.Reflection.Metadata", "Microsoft.CodeAnalysis", "Microsoft.CodeAnalysis.CSharp" })
            {
                try
                {
                    string p = Path.Combine(Path.Combine(HostInfo.Dir, "lib"), n + ".dll");
                    if (!File.Exists(p)) continue;
                    var have = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => !a.IsDynamic && a.GetName().Name == n);
                    if (have != null) { Log.Write("Roslyn preload: " + n + " " + have.GetName().Version + " already loaded by another add-in"); continue; }
                    if (n.StartsWith("System.")) continue;          // the runtime has newer ones itself
                    Assembly.LoadFrom(p);
                }
                catch (Exception ex) { Log.Write("Roslyn preload " + n + ": " + ex.Message); }
            }
        }

        // Roslyn and its dependencies live in .\lib (newer versions than the ones Revit ships).
        static Assembly OnResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                var an = new AssemblyName(args.Name);
                string p = Path.Combine(Path.Combine(HostInfo.Dir, "lib"), an.Name + ".dll");
                if (!File.Exists(p)) return null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.IsDynamic) continue;
                    string loc = "";
                    try { loc = a.Location; } catch { }
                    if (string.Equals(loc, p, StringComparison.OrdinalIgnoreCase)) return a;
                }
                var have = AssemblyName.GetAssemblyName(p).Version;
                if (an.Version != null && have < an.Version) return null;
                return Assembly.LoadFrom(p);
            }
            catch { return null; }
        }
    }

    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.ReadOnly)]
    public class ShowWindowCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try { BridgeWindow.ShowIt(data.Application.MainWindowHandle); }
            catch (Exception ex) { Log.Write("Window: " + ex); TaskDialog.Show("Multi Bridge", "The window could not open:\n" + ex.Message); return Result.Failed; }
            return Result.Succeeded;
        }
    }
}
