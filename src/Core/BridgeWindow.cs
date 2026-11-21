// Revit Multi Bridge - status window of this Revit (ribbon button "Multi Bridge"): same design as the RR Bridge window, plus multiport.
//   status (RUNNING / STOPPED, URL, running script), "Bridges on this PC" (every Revit with the bridge, any version), recent calls,
//   Open log, Cancel script, port picker + Re-claim (takes that port; the Revit that had it moves to another free port), Start / Stop.
// No Revit API here (testable outside Revit). C# 5 syntax only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace RevitMultiBridge
{
    class OwnerHandle : WF.IWin32Window
    {
        readonly IntPtr h;
        public OwnerHandle(IntPtr handle) { h = handle; }
        public IntPtr Handle { get { return h; } }
    }

    class BridgeWindow : WF.Form
    {
        static BridgeWindow inst;
        readonly WF.Label lStatus, lUrl, lJob, lMoved;
        readonly WF.ListView peers, list;
        readonly WF.Button bToggle, bCancel, bLog, bReclaim;
        readonly WF.ComboBox cbPort;
        readonly WF.ToolTip tips = new WF.ToolTip();
        readonly WF.Timer timer;
        int shown = -1; string peersShown = null;
        bool styled;

        // ---- peers are scanned on a background thread while the window is open (every 2 s) ----
        static volatile List<Peer> lastPeers = new List<Peer>();
        static volatile bool scanning;
        static void ScanLoop()
        {
            while (scanning)
            {
                try { lastPeers = Peers.Scan(Server.First, Server.Last, 250); } catch { }
                for (int i = 0; i < 20 && scanning; i++) Thread.Sleep(100);
            }
        }

        public static void ShowIt(IntPtr owner)
        {
            if (inst == null || inst.IsDisposed)
            {
                inst = new BridgeWindow();
                if (owner != IntPtr.Zero) inst.Show(new OwnerHandle(owner)); else inst.Show();
            }
            else
            {
                if (inst.WindowState == WF.FormWindowState.Minimized) inst.WindowState = WF.FormWindowState.Normal;
                inst.Activate();
            }
        }

        // layout in design px (Segoe UI 9 pt = 12 px), like the RR windows: Ui.Apply adds the header and scales it to 14 px
        internal BridgeWindow()
        {
            Text = "Multi Bridge – Revit " + HostInfo.Year + " · pid " + HostInfo.Pid;   // Ui.Apply: header "Multi Bridge" + context line
            try   // title bar icon = the ribbon icon
            {
                string ico = Path.Combine(Path.Combine(HostInfo.Dir, "icons"), "mcp.png");
                if (File.Exists(ico))
                    using (var src = new SD.Bitmap(ico))
                    using (var bmp = new SD.Bitmap(src, 32, 32))
                        Icon = SD.Icon.FromHandle(bmp.GetHicon());
            }
            catch (Exception ex) { Log.Write("Window icon: " + ex.Message); }
            ClientSize = new SD.Size(640, 560);
            MinimumSize = new SD.Size(560, 480);
            StartPosition = WF.FormStartPosition.CenterParent;
            Font = new SD.Font("Segoe UI", 9f);
            ShowInTaskbar = false;
            var muted = Ui.Text2;
            var all = WF.AnchorStyles.Top | WF.AnchorStyles.Bottom | WF.AnchorStyles.Left | WF.AnchorStyles.Right;
            var topWide = WF.AnchorStyles.Top | WF.AnchorStyles.Left | WF.AnchorStyles.Right;

            lStatus = new WF.Label { AutoSize = true, Location = new SD.Point(12, 8), Font = new SD.Font("Segoe UI Semibold", 11f) };
            lUrl = new WF.Label { AutoSize = true, Location = new SD.Point(14, 34), ForeColor = muted };
            lJob = new WF.Label { AutoSize = true, Location = new SD.Point(14, 54) };
            lMoved = new WF.Label { AutoSize = false, Location = new SD.Point(300, 54), Size = new SD.Size(328, 18), ForeColor = Ui.Amber.Back, Anchor = topWide,
                                    TextAlign = SD.ContentAlignment.TopRight, AutoEllipsis = true };
            var lPeers = new WF.Label { AutoSize = true, Location = new SD.Point(14, 82), Text = "Bridges on this PC (any Revit version, port pool " + Server.First + "–" + Server.Last + ")", ForeColor = muted };
            peers = new WF.ListView { Location = new SD.Point(12, 102), Size = new SD.Size(616, 172), View = WF.View.Details, HeaderStyle = WF.ColumnHeaderStyle.Nonclickable,
                                      Anchor = topWide, MultiSelect = false };
            peers.Columns.Add("Port", 56); peers.Columns.Add("Revit", 56); peers.Columns.Add("Document", 220); peers.Columns.Add("Status", 96); peers.Columns.Add("PID", 64); peers.Columns.Add("View", 124);
            var lRecent = new WF.Label { AutoSize = true, Location = new SD.Point(14, 286), Text = "Recent calls to this Revit", ForeColor = muted };
            list = new WF.ListView { Location = new SD.Point(12, 306), Size = new SD.Size(616, 202), View = WF.View.Details, HeaderStyle = WF.ColumnHeaderStyle.Nonclickable, Anchor = all };
            list.Columns.Add("Time", 64); list.Columns.Add("Call", 104); list.Columns.Add("Result", 104); list.Columns.Add("Duration", 70); list.Columns.Add("Script", 270);

            // bottom row: secondary buttons on the left; port picker + Re-claim + the main one (Start / Stop) on the right
            var bl = WF.AnchorStyles.Bottom | WF.AnchorStyles.Left; var br = WF.AnchorStyles.Bottom | WF.AnchorStyles.Right;
            bLog = new WF.Button { Text = "Open log", Size = new SD.Size(90, 28), Location = new SD.Point(12, 520), Anchor = bl };
            bCancel = new WF.Button { Text = "Cancel script", Size = new SD.Size(110, 28), Location = new SD.Point(110, 520), Anchor = bl };
            cbPort = new WF.ComboBox { DropDownStyle = WF.ComboBoxStyle.DropDownList, Size = new SD.Size(96, 24), Location = new SD.Point(338, 523), Anchor = br };
            bReclaim = new WF.Button { Text = "Re-claim", Size = new SD.Size(96, 28), Location = new SD.Point(440, 520), Anchor = br };
            bToggle = new WF.Button { Text = "Stop", Size = new SD.Size(86, 28), Location = new SD.Point(542, 520), Anchor = br };
            cbPort.Items.Add("Any free port");
            for (int p = Server.First; p <= Server.Last; p++) cbPort.Items.Add(p.ToString());
            cbPort.SelectedIndex = 0;
            tips.SetToolTip(cbPort, "Port for Start / Re-claim. \"Any free port\" = the first free one of the pool.");
            tips.SetToolTip(bReclaim, "Takes the selected port for this Revit: the Revit that has it moves to another free port of the pool.");
            tips.SetToolTip(bToggle, "Stop: this Revit leaves the pool (Claude cannot reach it). Start: takes the selected port, or the first free one.");

            bToggle.Click += delegate
            {
                if (Server.Running) Server.Stop();
                else
                {
                    int want = SelectedPort();
                    bool ok = want > 0 ? Server.Start(want, true) : Server.StartAny(0);
                    if (!ok) WF.MessageBox.Show(this, Server.LastError, "Multi Bridge", WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Warning);
                }
                RefreshUi();
            };
            bCancel.Click += delegate { var j = Jobs.Current; if (j != null) j.Cts.Cancel(); };
            bLog.Click += delegate { try { System.Diagnostics.Process.Start(Log.PathName); } catch { } };
            bReclaim.Click += delegate
            {
                int want = SelectedPort();
                if (want <= 0) return;
                UseWaitCursor = true;
                string msg = Server.TakeOver(want);
                UseWaitCursor = false;
                RefreshUi();
                if (msg != null) WF.MessageBox.Show(this, msg, "Multi Bridge", WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Warning);
            };
            cbPort.SelectedIndexChanged += delegate { RefreshUi(); };
            // a row of the bridges table selects its port (then Re-claim takes it)
            peers.SelectedIndexChanged += delegate
            {
                if (peers.SelectedItems.Count == 0) return;
                int i = cbPort.Items.IndexOf(peers.SelectedItems[0].Text);
                if (i > 0) cbPort.SelectedIndex = i;
            };

            Controls.AddRange(new WF.Control[] { lStatus, lUrl, lJob, lMoved, lPeers, peers, lRecent, list, bLog, bCancel, cbPort, bReclaim, bToggle });
            Ui.Apply(this, bToggle);
            Ui.Pills(peers, 3);
            Ui.Pills(list, 2);
            styled = true;

            scanning = true;
            new Thread(ScanLoop) { IsBackground = true, Name = "RevitMultiBridge peers" }.Start();
            timer = new WF.Timer { Interval = 500 };
            timer.Tick += delegate { try { RefreshUi(); } catch (Exception ex) { Log.Write("Window refresh: " + ex.Message); } };
            timer.Start();
            FormClosed += delegate { scanning = false; timer.Stop(); timer.Dispose(); tips.Dispose(); };
            RefreshUi();
        }

        int SelectedPort()
        {
            int p;
            return cbPort.SelectedIndex > 0 && int.TryParse(cbPort.SelectedItem.ToString(), out p) ? p : 0;
        }

        bool? wasOn;
        void RefreshUi()
        {
            bool on = Server.Running;
            lStatus.Text = on ? "● RUNNING on port " + Server.Port : "● STOPPED";
            lStatus.ForeColor = on ? Ui.Green.Back : Ui.DangerColor;
            lUrl.Text = on ? "http://localhost:" + Server.Port + "/mcp   ·   " + HostInfo.Label : (Server.LastError ?? "Not listening: Claude cannot reach this Revit.");
            bToggle.Text = on ? "Stop" : "Start";
            if (styled && wasOn != on) { Ui.SetPrimaryKind(bToggle, on); wasOn = on; }
            int want = SelectedPort();
            bReclaim.Enabled = want > 0 && !(on && want == Server.Port);   // a port to take, and not the one this Revit already has
            var j = Jobs.Current;
            lJob.Text = j == null ? "Idle" : (j.Started ? "Running " : "Waiting for Revit: ") + j.Tool + (j.Execute ? " (execute)" : "") + "  " + (int)(DateTime.Now - j.T0).TotalSeconds + " s";
            bCancel.Enabled = j != null;
            lMoved.Text = Server.Moved ?? "";

            var ps = lastPeers;
            string key = string.Join("|", ps.Select(p => p.Port + "/" + p.Pid + "/" + p.Document + "/" + p.Busy + "/" + p.View).ToArray()) + "#" + Server.Port;
            if (key != peersShown)
            {
                peersShown = key;
                string sel = peers.SelectedItems.Count > 0 ? peers.SelectedItems[0].Text : null;
                peers.BeginUpdate();
                peers.Items.Clear();
                foreach (var p in ps.OrderBy(x => x.Pid == HostInfo.Pid ? 0 : 1).ThenBy(x => x.Port))   // this Revit first
                {
                    string st = p.Pid == HostInfo.Pid ? "THIS WINDOW" : p.Busy ? "BUSY" : "READY";
                    var it = new WF.ListViewItem(new[] { p.Port.ToString(), p.Revit, p.Document == "" ? "(no document)" : p.Document, st, p.Pid.ToString(), p.View });
                    peers.Items.Add(it);
                    if (sel == it.Text) it.Selected = true;
                }
                peers.EndUpdate();
                Ui.EmptyText(peers, "No Revit bridge found on ports " + Server.First + "–" + Server.Last + ".");
            }
            if (Server.RecentVersion != shown)
            {
                shown = Server.RecentVersion;
                list.BeginUpdate();
                list.Items.Clear();
                foreach (var r in Server.RecentCalls().Reverse()) list.Items.Add(new WF.ListViewItem(r));
                list.EndUpdate();
                Ui.EmptyText(list, "No calls yet.");
            }
        }

        // test helper: picture of the window without showing it (tests\FakeRevit.cs -snapshot)
        internal static void Snapshot(string png, int waitMs)
        {
            var w = new BridgeWindow();
            w.StartPosition = WF.FormStartPosition.Manual; w.Location = new SD.Point(-4000, -4000); w.ShowInTaskbar = false;
            w.Show();
            var t0 = DateTime.Now;
            while ((DateTime.Now - t0).TotalMilliseconds < waitMs) { WF.Application.DoEvents(); Thread.Sleep(50); }
            w.RefreshUi(); WF.Application.DoEvents();
            using (var bmp = new SD.Bitmap(w.Width, w.Height)) { w.DrawToBitmap(bmp, new SD.Rectangle(0, 0, w.Width, w.Height)); bmp.Save(png); }
            w.Close();
        }
    }
}
