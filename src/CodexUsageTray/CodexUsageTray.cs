using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("Codex Usage Tray")]
[assembly: AssemblyDescription("Windows tray indicators for Codex quota and physical memory")]
[assembly: AssemblyCompany("Local")]
[assembly: AssemblyProduct("Codex Usage Tray")]
[assembly: AssemblyVersion("2.0.4.0")]

namespace CodexUsageTray
{
    internal static class Program
    {
        internal const string MeterMutexName = @"Local\CodexUsageTray_8E41D6B4";
        private static Mutex instanceMutex;

        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && args.Length > 0 &&
                string.Equals(args[0], "--watch-codex", StringComparison.OrdinalIgnoreCase))
            {
                CodexLaunchWatcher.Run();
                return;
            }

            bool createdNew;
            instanceMutex = new Mutex(true, MeterMutexName, out createdNew);
            if (!createdNew)
            {
                return;
            }

            CodexLaunchWatcher.EnsureStarted();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApplicationContext());

            instanceMutex.ReleaseMutex();
            instanceMutex.Dispose();
        }
    }

    internal static class CodexLaunchWatcher
    {
        private const string WatcherMutexName = @"Local\CodexUsageTrayWatcher_8E41D6B4";
        private const int PollIntervalMilliseconds = 2000;
        private static readonly object SyncRoot = new object();
        private static bool hasObservedCodex;
        private static HashSet<int> codexProcessIds = new HashSet<int>();

        internal static void EnsureStarted()
        {
            if (IsWatcherRunning())
            {
                return;
            }

            try
            {
                string executable = GetCurrentExecutablePath();
                if (string.IsNullOrEmpty(executable))
                {
                    return;
                }

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = executable;
                startInfo.Arguments = "--watch-codex";
                startInfo.WorkingDirectory = Path.GetDirectoryName(executable);
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                Process.Start(startInfo);
            }
            catch
            {
                // The meter itself remains usable if the optional watcher cannot start.
            }
        }

        internal static void Run()
        {
            bool createdNew;
            using (Mutex watcherMutex = new Mutex(true, WatcherMutexName, out createdNew))
            {
                if (!createdNew)
                {
                    return;
                }

                try
                {
                    using (ManualResetEvent stopEvent = new ManualResetEvent(false))
                    using (System.Threading.Timer timer = new System.Threading.Timer(
                        delegate { ObserveCodexState(); }, null, 0, PollIntervalMilliseconds))
                    {
                        stopEvent.WaitOne();
                    }
                }
                finally
                {
                    watcherMutex.ReleaseMutex();
                }
            }
        }

        private static void ObserveCodexState()
        {
            try
            {
                HashSet<int> currentProcessIds = GetCodexDesktopProcessIds();
                bool shouldRestoreMeter;
                lock (SyncRoot)
                {
                    shouldRestoreMeter = currentProcessIds.Count > 0 &&
                        (!hasObservedCodex || HasNewCodexProcess(currentProcessIds));
                    hasObservedCodex = true;
                    codexProcessIds = currentProcessIds;
                }

                if (shouldRestoreMeter)
                {
                    StartMeterIfMissing();
                }
            }
            catch
            {
                // A transient process-query failure is retried at the next interval.
            }
        }

        private static HashSet<int> GetCodexDesktopProcessIds()
        {
            HashSet<int> processIds = new HashSet<int>();
            Process[] candidates = Process.GetProcessesByName("ChatGPT");
            foreach (Process candidate in candidates)
            {
                try
                {
                    string executable = candidate.MainModule == null ? null : candidate.MainModule.FileName;
                    if (!string.IsNullOrEmpty(executable) &&
                        executable.IndexOf(@"\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        processIds.Add(candidate.Id);
                    }
                }
                catch
                {
                    // Individual protected processes are irrelevant to this user-session check.
                }
                finally
                {
                    candidate.Dispose();
                }
            }

            return processIds;
        }

        private static bool HasNewCodexProcess(HashSet<int> currentProcessIds)
        {
            foreach (int processId in currentProcessIds)
            {
                if (!codexProcessIds.Contains(processId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsWatcherRunning()
        {
            try
            {
                using (Mutex watcher = Mutex.OpenExisting(WatcherMutexName))
                {
                    return true;
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool IsMeterRunning()
        {
            try
            {
                using (Mutex meter = Mutex.OpenExisting(Program.MeterMutexName))
                {
                    return true;
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static void StartMeterIfMissing()
        {
            if (IsMeterRunning())
            {
                return;
            }

            try
            {
                string executable = GetCurrentExecutablePath();
                if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
                {
                    return;
                }

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = executable;
                startInfo.WorkingDirectory = Path.GetDirectoryName(executable);
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                Process.Start(startInfo);
            }
            catch
            {
                // The next Codex launch can retry restoration.
            }
        }

        private static string GetCurrentExecutablePath()
        {
            using (Process current = Process.GetCurrentProcess())
            {
                return current.MainModule == null ? null : current.MainModule.FileName;
            }
        }
    }

    internal sealed class MemoryStatusEx
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal sealed class NativeMemoryStatus
        {
            internal uint Length;
            internal uint MemoryLoad;
            internal ulong TotalPhysical;
            internal ulong AvailablePhysical;
            internal ulong TotalPageFile;
            internal ulong AvailablePageFile;
            internal ulong TotalVirtual;
            internal ulong AvailableVirtual;
            internal ulong AvailableExtendedVirtual;

            internal NativeMemoryStatus()
            {
                Length = (uint)Marshal.SizeOf(typeof(NativeMemoryStatus));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] NativeMemoryStatus buffer);

        internal static MemoryReading Read()
        {
            NativeMemoryStatus status = new NativeMemoryStatus();
            if (!GlobalMemoryStatusEx(status))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            MemoryReading reading = new MemoryReading();
            reading.TotalBytes = status.TotalPhysical;
            reading.AvailableBytes = status.AvailablePhysical;
            ulong used = status.TotalPhysical - status.AvailablePhysical;
            reading.UsedPercent = status.TotalPhysical == 0
                ? 0
                : Clamp((int)Math.Round(used * 100.0 / status.TotalPhysical), 0, 100);
            return reading;
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }

    internal sealed class MemoryReading
    {
        internal ulong TotalBytes;
        internal ulong AvailableBytes;
        internal int UsedPercent;

        internal double TotalGigabytes
        {
            get { return TotalBytes / 1073741824.0; }
        }

        internal double AvailableGigabytes
        {
            get { return AvailableBytes / 1073741824.0; }
        }

        internal double UsedGigabytes
        {
            get { return (TotalBytes - AvailableBytes) / 1073741824.0; }
        }
    }

    internal sealed class QuotaWindow
    {
        internal int UsedPercent;
        internal long? WindowDurationMinutes;
        internal long? ResetsAtUnixSeconds;
    }

    internal sealed class QuotaReading
    {
        internal bool Success;
        internal string ErrorMessage;
        internal bool? OrdinaryUsageAllowed;
        internal int UsedPercent;
        internal int RemainingPercent;
        internal long? WindowDurationMinutes;
        internal long? ResetsAtUnixSeconds;
        internal string PlanType;
    }

    internal static class MeterPreferences
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexUsageTray",
            "settings.json");

        internal static bool LoadAutoHideFullscreen()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    Dictionary<string, object> settings =
                        new JavaScriptSerializer().DeserializeObject(File.ReadAllText(SettingsPath)) as Dictionary<string, object>;
                    object value;
                    if (settings != null && settings.TryGetValue("autoHideFullscreen", out value) && value is bool)
                    {
                        return (bool)value;
                    }
                }
            }
            catch
            {
                // Damaged or inaccessible settings should not prevent startup.
            }
            return true;
        }

        internal static void SaveAutoHideFullscreen(bool enabled)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                Dictionary<string, object> settings = new Dictionary<string, object>();
                settings["autoHideFullscreen"] = enabled;
                string json = new JavaScriptSerializer().Serialize(settings);
                File.WriteAllText(SettingsPath, json, new UTF8Encoding(false));
            }
            catch
            {
                // A read-only configuration path must not stop monitoring.
            }
        }
    }

    internal sealed class TaskbarMeterForm : Form
    {
        private const int MeterWidth = 204;
        private const int MeterHeight = 34;
        private static readonly Color CodexPurple = Color.FromArgb(147, 51, 234);
        private static readonly Color MemoryBlue = Color.FromArgb(37, 99, 235);
        private static readonly IntPtr HwndTopMost = new IntPtr(-1);

        private readonly System.Windows.Forms.Timer positionTimer;
        private bool autoHideFullscreen;
        private readonly ToolTip detailsToolTip;
        private int quotaPercent = -1;
        private int memoryPercent = -1;
        private string quotaDetails = "Codex 额度：正在读取";
        private string memoryDetails = "内存：正在读取";

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            internal int X;
            internal int Y;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr window, out NativeRectangle rectangle);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr window, out NativeRectangle rectangle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        internal TaskbarMeterForm(ContextMenuStrip contextMenu, bool autoHideFullscreen)
        {
            this.autoHideFullscreen = autoHideFullscreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(22, 28, 39);
            ClientSize = new Size(MeterWidth, MeterHeight);
            ContextMenuStrip = contextMenu;
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);

            detailsToolTip = new ToolTip();
            detailsToolTip.AutoPopDelay = 10000;
            detailsToolTip.InitialDelay = 250;
            detailsToolTip.ReshowDelay = 100;
            UpdateToolTip();

            positionTimer = new System.Windows.Forms.Timer();
            positionTimer.Interval = 1000;
            positionTimer.Tick += delegate { UpdateVisibilityAndPosition(); };
            positionTimer.Start();

            Shown += delegate { UpdateVisibilityAndPosition(); };
            SizeChanged += delegate { UpdateRoundedRegion(); };
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WsExToolWindow = 0x00000080;
                const int WsExNoActivate = 0x08000000;
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
                return parameters;
            }
        }

        internal void SetAutoHideFullscreen(bool enabled)
        {
            autoHideFullscreen = enabled;
            UpdateVisibilityAndPosition();
        }

        internal void SetQuota(int percent, string details)
        {
            quotaPercent = Clamp(percent);
            quotaDetails = details;
            UpdateToolTip();
            Invalidate();
        }

        internal void SetMemory(int percent, string details)
        {
            memoryPercent = Clamp(percent);
            memoryDetails = details;
            UpdateToolTip();
            Invalidate();
        }

        internal void SetQuotaError(string details)
        {
            quotaDetails = details;
            UpdateToolTip();
        }

        internal void SetMemoryError(string details)
        {
            memoryDetails = details;
            UpdateToolTip();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (GraphicsPath panel = RoundedRectangle(new RectangleF(0, 0, Width - 1, Height - 1), 8f))
            using (SolidBrush panelBrush = new SolidBrush(Color.FromArgb(22, 28, 39)))
            using (Pen panelBorder = new Pen(Color.FromArgb(70, 255, 255, 255), 1f))
            {
                e.Graphics.FillPath(panelBrush, panel);
                e.Graphics.DrawPath(panelBorder, panel);
            }

            DrawBattery(e.Graphics, new Rectangle(7, 6, 91, 22), "额度", quotaPercent, CodexPurple);
            DrawBattery(e.Graphics, new Rectangle(106, 6, 91, 22), "内存", memoryPercent, MemoryBlue);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                positionTimer.Stop();
                positionTimer.Dispose();
                detailsToolTip.Dispose();
            }
            base.Dispose(disposing);
        }

        private static void DrawBattery(Graphics graphics, Rectangle bounds, string label, int percent, Color color)
        {
            Rectangle body = new Rectangle(bounds.Left, bounds.Top, bounds.Width - 5, bounds.Height);
            Rectangle terminal = new Rectangle(body.Right + 1, bounds.Top + 6, 4, bounds.Height - 12);
            Rectangle interior = new Rectangle(body.Left + 3, body.Top + 3, body.Width - 6, body.Height - 6);

            using (GraphicsPath bodyPath = RoundedRectangle(body, 4f))
            using (SolidBrush emptyBrush = new SolidBrush(Color.FromArgb(42, 255, 255, 255)))
            using (SolidBrush fillBrush = new SolidBrush(color))
            using (Pen outlinePen = new Pen(Color.FromArgb(205, 255, 255, 255), 1.2f))
            using (SolidBrush terminalBrush = new SolidBrush(Color.FromArgb(205, 255, 255, 255)))
            {
                graphics.FillPath(emptyBrush, bodyPath);
                if (percent >= 0)
                {
                    int fillWidth = (int)Math.Round(interior.Width * percent / 100.0);
                    if (percent > 0)
                    {
                        fillWidth = Math.Max(2, fillWidth);
                    }
                    if (fillWidth > 0)
                    {
                        Rectangle fill = new Rectangle(interior.Left, interior.Top, Math.Min(interior.Width, fillWidth), interior.Height);
                        using (GraphicsPath fillPath = RoundedRectangle(fill, 2.5f))
                        {
                            graphics.FillPath(fillBrush, fillPath);
                        }
                    }
                }
                graphics.DrawPath(outlinePen, bodyPath);
                graphics.FillRectangle(terminalBrush, terminal);
            }

            string value = percent < 0 ? "--" : percent.ToString(CultureInfo.InvariantCulture) + "%";
            using (Font font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
            using (SolidBrush textBrush = new SolidBrush(Color.White))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                string text = label + " " + value;
                RectangleF textBounds = new RectangleF(body.Left, body.Top, body.Width, body.Height);
                RectangleF shadowBounds = textBounds;
                shadowBounds.Offset(0, 1);
                graphics.DrawString(text, font, shadowBrush, shadowBounds, format);
                graphics.DrawString(text, font, textBrush, textBounds, format);
            }
        }

        private void UpdateVisibilityAndPosition()
        {
            if (autoHideFullscreen && IsFullscreenOnTaskbarDisplay())
            {
                if (Visible)
                {
                    Hide();
                }
                return;
            }

            if (!Visible)
            {
                Show();
            }
            PositionBesideSystemTray();
        }

        private bool IsFullscreenOnTaskbarDisplay()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == Handle ||
                foreground == GetShellWindow() || IsIconic(foreground))
            {
                return false;
            }

            StringBuilder className = new StringBuilder(256);
            if (GetClassName(foreground, className, className.Capacity) > 0)
            {
                string windowClass = className.ToString();
                if (windowClass == "Progman" || windowClass == "WorkerW" ||
                    windowClass == "Shell_TrayWnd" || windowClass == "Shell_SecondaryTrayWnd")
                {
                    return false;
                }
            }

            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            Rectangle display = taskbar == IntPtr.Zero
                ? Screen.PrimaryScreen.Bounds
                : Screen.FromHandle(taskbar).Bounds;

            NativeRectangle windowBounds;
            NativeRectangle clientBounds;
            if (!GetWindowRect(foreground, out windowBounds) ||
                !GetClientRect(foreground, out clientBounds))
            {
                return false;
            }

            // A normal maximized window commonly extends its invisible resize
            // border beyond the monitor. An actual fullscreen window fits it.
            const int edgeTolerance = 2;
            if (Math.Abs(windowBounds.Left - display.Left) > edgeTolerance ||
                Math.Abs(windowBounds.Top - display.Top) > edgeTolerance ||
                Math.Abs(windowBounds.Right - display.Right) > edgeTolerance ||
                Math.Abs(windowBounds.Bottom - display.Bottom) > edgeTolerance)
            {
                return false;
            }

            NativePoint clientTopLeft = new NativePoint();
            clientTopLeft.X = clientBounds.Left;
            clientTopLeft.Y = clientBounds.Top;
            NativePoint clientBottomRight = new NativePoint();
            clientBottomRight.X = clientBounds.Right;
            clientBottomRight.Y = clientBounds.Bottom;
            if (!ClientToScreen(foreground, ref clientTopLeft) ||
                !ClientToScreen(foreground, ref clientBottomRight))
            {
                return false;
            }

            // Excluding the non-client title/tab bar avoids treating ordinary
            // maximized windows as fullscreen, including auto-hide taskbars.
            return clientTopLeft.X <= display.Left + edgeTolerance &&
                clientTopLeft.Y <= display.Top + edgeTolerance &&
                clientBottomRight.X >= display.Right - edgeTolerance &&
                clientBottomRight.Y >= display.Bottom - edgeTolerance;
        }

        private void PositionBesideSystemTray()
        {
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            NativeRectangle taskbarRectangle;
            if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out taskbarRectangle))
            {
                Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
                SetBounds(workingArea.Right - Width - 360, workingArea.Bottom, Width, Height);
                return;
            }

            int left;
            IntPtr tray = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            NativeRectangle trayRectangle;
            if (tray != IntPtr.Zero && GetWindowRect(tray, out trayRectangle))
            {
                left = trayRectangle.Left - Width - 8;
            }
            else
            {
                left = taskbarRectangle.Right - Width - 360;
            }

            int top = taskbarRectangle.Top + Math.Max(0, (taskbarRectangle.Bottom - taskbarRectangle.Top - Height) / 2);
            SetBounds(left, top, Width, Height);
            SetWindowPos(Handle, HwndTopMost, left, top, Width, Height, 0x0010 | 0x0040);
        }

        private void UpdateRoundedRegion()
        {
            using (GraphicsPath path = RoundedRectangle(new RectangleF(0, 0, Width, Height), 8f))
            {
                Region oldRegion = Region;
                Region = new Region(path);
                if (oldRegion != null)
                {
                    oldRegion.Dispose();
                }
            }
        }

        private void UpdateToolTip()
        {
            detailsToolTip.SetToolTip(this, quotaDetails + Environment.NewLine + memoryDetails);
        }

        private static int Clamp(int value)
        {
            return Math.Max(0, Math.Min(100, value));
        }

        private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            float diameter = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private const int MemoryRefreshMilliseconds = 30 * 60 * 1000;
        private const int QuotaRefreshMilliseconds = 5 * 60 * 1000;
        private const int QuotaReadTimeoutMilliseconds = 15000;

        private readonly NotifyIcon quotaNotifyIcon;
        private readonly NotifyIcon memoryNotifyIcon;
        private readonly ContextMenuStrip contextMenu;
        private readonly ToolStripMenuItem quotaStatusItem;
        private readonly ToolStripMenuItem memoryStatusItem;
        private readonly System.Windows.Forms.Timer memoryTimer;
        private readonly System.Windows.Forms.Timer quotaTimer;
        private readonly Control dispatcher;
        private readonly string statusFilePath;
        private readonly TaskbarMeterForm meterForm;

        private Icon quotaRenderedIcon;
        private Icon memoryRenderedIcon;
        private MemoryReading latestMemory;
        private QuotaReading latestQuota;
        private int lastMemoryIconPercent = -1;
        private int lastQuotaIconPercent = -1;
        private bool quotaRefreshRunning;
        private bool disposed;

        internal TrayApplicationContext()
        {
            statusFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "status.json");

            dispatcher = new Control();
            dispatcher.CreateControl();

            contextMenu = new ContextMenuStrip();
            quotaStatusItem = new ToolStripMenuItem("Codex：正在读取额度…");
            quotaStatusItem.Enabled = false;
            memoryStatusItem = new ToolStripMenuItem("内存：正在读取…");
            memoryStatusItem.Enabled = false;

            ToolStripMenuItem refreshItem = new ToolStripMenuItem("立即刷新");
            refreshItem.Click += delegate { RefreshMemory(); StartQuotaRefresh(); };

            ToolStripMenuItem autoHideFullscreenItem = new ToolStripMenuItem("全屏时自动隐藏");
            autoHideFullscreenItem.Checked = MeterPreferences.LoadAutoHideFullscreen();
            autoHideFullscreenItem.CheckOnClick = true;
            autoHideFullscreenItem.CheckedChanged += delegate
            {
                meterForm.SetAutoHideFullscreen(autoHideFullscreenItem.Checked);
                MeterPreferences.SaveAutoHideFullscreen(autoHideFullscreenItem.Checked);
            };

            ToolStripMenuItem openCodexItem = new ToolStripMenuItem("打开 Codex");
            openCodexItem.Click += delegate { OpenCodex(); };

            ToolStripMenuItem openTaskManagerItem = new ToolStripMenuItem("打开任务管理器");
            openTaskManagerItem.Click += delegate { OpenTaskManager(); };

            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出监控");
            exitItem.Click += delegate { ExitThread(); };

            contextMenu.Items.Add(quotaStatusItem);
            contextMenu.Items.Add(memoryStatusItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(refreshItem);
            contextMenu.Items.Add(autoHideFullscreenItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(openCodexItem);
            contextMenu.Items.Add(openTaskManagerItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitItem);

            quotaRenderedIcon = IconFactory.Create("--", "C", Color.FromArgb(147, 51, 234));
            memoryRenderedIcon = IconFactory.Create("--", "M", Color.FromArgb(37, 99, 235));

            quotaNotifyIcon = new NotifyIcon();
            quotaNotifyIcon.Icon = quotaRenderedIcon;
            quotaNotifyIcon.Text = "Codex 额度：正在读取";
            quotaNotifyIcon.Visible = false;
            quotaNotifyIcon.ContextMenuStrip = contextMenu;
            quotaNotifyIcon.DoubleClick += delegate { OpenCodex(); };

            memoryNotifyIcon = new NotifyIcon();
            memoryNotifyIcon.Icon = memoryRenderedIcon;
            memoryNotifyIcon.Text = "内存：正在读取";
            memoryNotifyIcon.Visible = false;
            memoryNotifyIcon.ContextMenuStrip = contextMenu;
            memoryNotifyIcon.DoubleClick += delegate { OpenTaskManager(); };

            meterForm = new TaskbarMeterForm(contextMenu, autoHideFullscreenItem.Checked);
            meterForm.Show();

            memoryTimer = new System.Windows.Forms.Timer();
            memoryTimer.Interval = MemoryRefreshMilliseconds;
            memoryTimer.Tick += delegate { RefreshMemory(); };
            memoryTimer.Start();

            quotaTimer = new System.Windows.Forms.Timer();
            quotaTimer.Interval = QuotaRefreshMilliseconds;
            quotaTimer.Tick += delegate { StartQuotaRefresh(); };
            quotaTimer.Start();

            RefreshMemory();
            StartQuotaRefresh();
        }

        private void RefreshMemory()
        {
            try
            {
                latestMemory = MemoryStatusEx.Read();
                int usedPercent = latestMemory.UsedPercent;
                Color background = Color.FromArgb(37, 99, 235);

                if (usedPercent != lastMemoryIconPercent)
                {
                    Icon nextIcon = IconFactory.Create(usedPercent.ToString(CultureInfo.InvariantCulture), "M", background);
                    memoryNotifyIcon.Icon = nextIcon;
                    Icon previousIcon = memoryRenderedIcon;
                    memoryRenderedIcon = nextIcon;
                    if (previousIcon != null)
                    {
                        previousIcon.Dispose();
                    }
                    lastMemoryIconPercent = usedPercent;
                }

                string text = string.Format(
                    CultureInfo.CurrentCulture,
                    "内存 {0}%｜已用 {1:0.0}/{2:0.0} GB",
                    usedPercent,
                    latestMemory.UsedGigabytes,
                    latestMemory.TotalGigabytes);
                memoryNotifyIcon.Text = SafeTooltip(text);
                memoryStatusItem.Text = text + string.Format(CultureInfo.CurrentCulture, "｜可用 {0:0.0} GB", latestMemory.AvailableGigabytes);
                meterForm.SetMemory(usedPercent, memoryStatusItem.Text);
            }
            catch
            {
                memoryNotifyIcon.Text = "内存读取失败";
                memoryStatusItem.Text = "内存：读取失败";
                meterForm.SetMemoryError(memoryStatusItem.Text);
            }
        }

        private void StartQuotaRefresh()
        {
            if (quotaRefreshRunning || disposed)
            {
                return;
            }

            quotaRefreshRunning = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                QuotaReading reading;
                try
                {
                    reading = CodexQuotaReader.Read(QuotaReadTimeoutMilliseconds);
                }
                catch (Exception ex)
                {
                    reading = new QuotaReading();
                    reading.Success = false;
                    reading.ErrorMessage = CompactError(ex.Message);
                }

                if (disposed || dispatcher.IsDisposed)
                {
                    return;
                }

                try
                {
                    dispatcher.BeginInvoke((Action)delegate { ApplyQuotaReading(reading); });
                }
                catch
                {
                    // The message loop may already be shutting down.
                }
            });
        }

        private void ApplyQuotaReading(QuotaReading reading)
        {
            quotaRefreshRunning = false;
            if (disposed)
            {
                return;
            }

            if (!reading.Success)
            {
                string previous = latestQuota != null && latestQuota.Success
                    ? string.Format(CultureInfo.CurrentCulture, "（保留上次：剩余 {0}%）", latestQuota.RemainingPercent)
                    : string.Empty;
                quotaNotifyIcon.Text = SafeTooltip("Codex 额度暂时无法刷新" + previous);
                quotaStatusItem.Text = "Codex：刷新失败 " + previous;
                meterForm.SetQuotaError(quotaStatusItem.Text);
                WriteStatus(reading.ErrorMessage);
                return;
            }

            latestQuota = reading;
            int remaining = reading.RemainingPercent;
            Color background = Color.FromArgb(147, 51, 234);

            if (remaining != lastQuotaIconPercent)
            {
                Icon nextIcon = IconFactory.Create(remaining.ToString(CultureInfo.InvariantCulture), "C", background);
                quotaNotifyIcon.Icon = nextIcon;
                Icon previousIcon = quotaRenderedIcon;
                quotaRenderedIcon = nextIcon;
                if (previousIcon != null)
                {
                    previousIcon.Dispose();
                }
                lastQuotaIconPercent = remaining;
            }

            string windowText = FormatWindow(reading.WindowDurationMinutes);
            string resetText = FormatResetTime(reading.ResetsAtUnixSeconds);
            string status = string.Format(
                CultureInfo.CurrentCulture,
                "Codex 剩余 {0}%｜{1}已用 {2}%{3}",
                remaining,
                windowText,
                reading.UsedPercent,
                resetText);

            if (reading.OrdinaryUsageAllowed.HasValue && !reading.OrdinaryUsageAllowed.Value)
            {
                status = "Codex 普通额度当前不可用｜" + status;
            }

            quotaNotifyIcon.Text = SafeTooltip(status);
            quotaStatusItem.Text = status;
            meterForm.SetQuota(remaining, status);
            WriteStatus(null);
        }

        private void WriteStatus(string lastError)
        {
            try
            {
                Dictionary<string, object> status = new Dictionary<string, object>();
                status["updatedAt"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
                status["appVersion"] = "2.0.4";
                status["memoryRefreshSeconds"] = MemoryRefreshMilliseconds / 1000;
                status["codexRefreshSeconds"] = QuotaRefreshMilliseconds / 1000;

                if (latestQuota != null && latestQuota.Success)
                {
                    status["codexRemainingPercent"] = latestQuota.RemainingPercent;
                    status["codexUsedPercent"] = latestQuota.UsedPercent;
                    status["codexWindowMinutes"] = latestQuota.WindowDurationMinutes;
                    status["codexResetsAt"] = latestQuota.ResetsAtUnixSeconds;
                    status["codexPlanType"] = latestQuota.PlanType;
                }

                if (latestMemory != null)
                {
                    status["memoryUsedPercent"] = latestMemory.UsedPercent;
                    status["memoryUsedGB"] = Math.Round(latestMemory.UsedGigabytes, 2);
                    status["memoryTotalGB"] = Math.Round(latestMemory.TotalGigabytes, 2);
                    status["memoryAvailableGB"] = Math.Round(latestMemory.AvailableGigabytes, 2);
                }

                status["lastError"] = lastError;
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(status);
                File.WriteAllText(statusFilePath, json, new UTF8Encoding(false));
            }
            catch
            {
                // Status output is diagnostic only; tray indicators remain authoritative.
            }
        }

        private static string SafeTooltip(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "资源监控";
            }
            return text.Length <= 63 ? text : text.Substring(0, 63);
        }

        private static string CompactError(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "未知错误";
            }

            string compact = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return compact.Length <= 240 ? compact : compact.Substring(0, 240);
        }

        private static string FormatWindow(long? minutes)
        {
            if (!minutes.HasValue || minutes.Value <= 0)
            {
                return "当前窗口";
            }
            if (minutes.Value % 10080 == 0)
            {
                return (minutes.Value / 10080).ToString(CultureInfo.CurrentCulture) + "周窗口";
            }
            if (minutes.Value % 1440 == 0)
            {
                return (minutes.Value / 1440).ToString(CultureInfo.CurrentCulture) + "天窗口";
            }
            if (minutes.Value % 60 == 0)
            {
                return (minutes.Value / 60).ToString(CultureInfo.CurrentCulture) + "小时窗口";
            }
            return minutes.Value.ToString(CultureInfo.CurrentCulture) + "分钟窗口";
        }

        private static string FormatResetTime(long? unixSeconds)
        {
            if (!unixSeconds.HasValue || unixSeconds.Value <= 0)
            {
                return string.Empty;
            }

            DateTime localTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddSeconds(unixSeconds.Value)
                .ToLocalTime();
            return "｜重置 " + localTime.ToString("MM/dd HH:mm", CultureInfo.CurrentCulture);
        }

        private static void OpenCodex()
        {
            try
            {
                string codex = CodexQuotaReader.FindCodexExecutable();
                ProcessStartInfo info = new ProcessStartInfo(codex, "app");
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                Process.Start(info);
            }
            catch
            {
                // Opening the app is optional; monitoring continues.
            }
        }

        private static void OpenTaskManager()
        {
            try
            {
                Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
            }
            catch
            {
                // Opening Task Manager is optional; monitoring continues.
            }
        }

        protected override void ExitThreadCore()
        {
            if (!disposed)
            {
                disposed = true;
                memoryTimer.Stop();
                quotaTimer.Stop();
                quotaNotifyIcon.Visible = false;
                memoryNotifyIcon.Visible = false;
                quotaNotifyIcon.Dispose();
                memoryNotifyIcon.Dispose();
                meterForm.Close();
                meterForm.Dispose();
                contextMenu.Dispose();
                memoryTimer.Dispose();
                quotaTimer.Dispose();
                if (quotaRenderedIcon != null)
                {
                    quotaRenderedIcon.Dispose();
                }
                if (memoryRenderedIcon != null)
                {
                    memoryRenderedIcon.Dispose();
                }
                dispatcher.Dispose();
            }
            base.ExitThreadCore();
        }
    }

    internal static class CodexQuotaReader
    {
        internal static QuotaReading Read(int timeoutMilliseconds)
        {
            Process process = null;
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = FindCodexExecutable();
                startInfo.Arguments = "app-server --stdio";
                startInfo.UseShellExecute = false;
                startInfo.RedirectStandardInput = true;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.CreateNoWindow = true;
                startInfo.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;

                process = new Process();
                process.StartInfo = startInfo;
                process.Start();
                Task<string> ignoredStandardError = process.StandardError.ReadToEndAsync();

                process.StandardInput.WriteLine("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"codex_usage_tray\",\"title\":\"Codex Usage Tray\",\"version\":\"1.1.0\"},\"capabilities\":{\"experimentalApi\":true}}}");
                process.StandardInput.Flush();

                DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = int.MaxValue;
                bool initializeComplete = false;

                while (DateTime.UtcNow < deadline)
                {
                    int remainingMilliseconds = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                    string line = ReadLineWithTimeout(process, remainingMilliseconds);
                    if (line == null)
                    {
                        throw new InvalidOperationException("Codex app-server 提前退出");
                    }

                    Dictionary<string, object> message = serializer.DeserializeObject(line) as Dictionary<string, object>;
                    if (message == null || !message.ContainsKey("id"))
                    {
                        continue;
                    }

                    int id = Convert.ToInt32(message["id"], CultureInfo.InvariantCulture);
                    if (id == 1)
                    {
                        ThrowIfError(message);
                        initializeComplete = true;
                        process.StandardInput.WriteLine("{\"method\":\"initialized\"}");
                        process.StandardInput.WriteLine("{\"id\":2,\"method\":\"account/rateLimits/read\",\"params\":{\"excludeResetCreditDetails\":true,\"supportsLunaReserve\":false}}");
                        process.StandardInput.Flush();
                    }
                    else if (id == 2)
                    {
                        if (!initializeComplete)
                        {
                            throw new InvalidOperationException("Codex app-server 初始化顺序异常");
                        }
                        ThrowIfError(message);
                        return ParseQuota(message);
                    }
                }

                throw new TimeoutException("读取 Codex 额度超时");
            }
            finally
            {
                if (process != null)
                {
                    try { process.StandardInput.Close(); } catch { }
                    try
                    {
                        if (!process.WaitForExit(500))
                        {
                            process.Kill();
                            process.WaitForExit(1000);
                        }
                    }
                    catch { }
                    process.Dispose();
                }
            }
        }

        internal static string FindCodexExecutable()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string root = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
            try
            {
                if (Directory.Exists(root))
                {
                    string[] candidates = Directory.GetFiles(root, "codex.exe", SearchOption.AllDirectories);
                    if (candidates.Length > 0)
                    {
                        Array.Sort(candidates, delegate(string left, string right)
                        {
                            return File.GetLastWriteTimeUtc(right).CompareTo(File.GetLastWriteTimeUtc(left));
                        });
                        return candidates[0];
                    }
                }
            }
            catch
            {
                // Fall through to PATH resolution.
            }
            return "codex.exe";
        }

        private static string ReadLineWithTimeout(Process process, int timeoutMilliseconds)
        {
            Task<string> readTask = process.StandardOutput.ReadLineAsync();
            if (!readTask.Wait(timeoutMilliseconds))
            {
                throw new TimeoutException("等待 Codex app-server 响应超时");
            }
            return readTask.Result;
        }

        private static void ThrowIfError(Dictionary<string, object> message)
        {
            if (!message.ContainsKey("error") || message["error"] == null)
            {
                return;
            }

            Dictionary<string, object> error = message["error"] as Dictionary<string, object>;
            if (error != null && error.ContainsKey("message"))
            {
                throw new InvalidOperationException(Convert.ToString(error["message"], CultureInfo.CurrentCulture));
            }
            throw new InvalidOperationException("Codex app-server 返回错误");
        }

        private static QuotaReading ParseQuota(Dictionary<string, object> message)
        {
            Dictionary<string, object> result = GetDictionary(message, "result");
            Dictionary<string, object> snapshot = GetDictionary(result, "rateLimits");

            List<QuotaWindow> windows = new List<QuotaWindow>();
            AddWindow(windows, snapshot, "primary");
            AddWindow(windows, snapshot, "secondary");
            if (windows.Count == 0)
            {
                throw new InvalidOperationException("Codex 未返回可用的额度窗口");
            }

            QuotaWindow limitingWindow = windows[0];
            for (int index = 1; index < windows.Count; index++)
            {
                if (windows[index].UsedPercent > limitingWindow.UsedPercent)
                {
                    limitingWindow = windows[index];
                }
            }

            QuotaReading reading = new QuotaReading();
            reading.Success = true;
            reading.UsedPercent = Clamp(limitingWindow.UsedPercent, 0, 100);
            reading.RemainingPercent = 100 - reading.UsedPercent;
            reading.WindowDurationMinutes = limitingWindow.WindowDurationMinutes;
            reading.ResetsAtUnixSeconds = limitingWindow.ResetsAtUnixSeconds;
            reading.PlanType = GetOptionalString(snapshot, "planType");
            reading.OrdinaryUsageAllowed = GetOptionalBoolean(result, "ordinaryUsageAllowed");
            return reading;
        }

        private static void AddWindow(List<QuotaWindow> windows, Dictionary<string, object> snapshot, string key)
        {
            if (!snapshot.ContainsKey(key) || snapshot[key] == null)
            {
                return;
            }

            Dictionary<string, object> source = snapshot[key] as Dictionary<string, object>;
            if (source == null || !source.ContainsKey("usedPercent"))
            {
                return;
            }

            QuotaWindow window = new QuotaWindow();
            window.UsedPercent = Convert.ToInt32(source["usedPercent"], CultureInfo.InvariantCulture);
            window.WindowDurationMinutes = GetOptionalInt64(source, "windowDurationMins");
            window.ResetsAtUnixSeconds = GetOptionalInt64(source, "resetsAt");
            windows.Add(window);
        }

        private static Dictionary<string, object> GetDictionary(Dictionary<string, object> source, string key)
        {
            if (source == null || !source.ContainsKey(key) || source[key] == null)
            {
                throw new InvalidOperationException("Codex 响应缺少字段：" + key);
            }
            Dictionary<string, object> value = source[key] as Dictionary<string, object>;
            if (value == null)
            {
                throw new InvalidOperationException("Codex 响应字段类型异常：" + key);
            }
            return value;
        }

        private static long? GetOptionalInt64(Dictionary<string, object> source, string key)
        {
            if (source == null || !source.ContainsKey(key) || source[key] == null)
            {
                return null;
            }
            return Convert.ToInt64(source[key], CultureInfo.InvariantCulture);
        }

        private static bool? GetOptionalBoolean(Dictionary<string, object> source, string key)
        {
            if (source == null || !source.ContainsKey(key) || source[key] == null)
            {
                return null;
            }
            return Convert.ToBoolean(source[key], CultureInfo.InvariantCulture);
        }

        private static string GetOptionalString(Dictionary<string, object> source, string key)
        {
            if (source == null || !source.ContainsKey(key) || source[key] == null)
            {
                return null;
            }
            return Convert.ToString(source[key], CultureInfo.InvariantCulture);
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }

    internal static class IconFactory
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr iconHandle);

        internal static Icon Create(string value, string badge, Color background)
        {
            using (Bitmap bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                graphics.Clear(Color.Transparent);

                using (GraphicsPath path = RoundedRectangle(new RectangleF(1, 1, 30, 30), 7f))
                using (SolidBrush backgroundBrush = new SolidBrush(background))
                using (Pen borderPen = new Pen(Color.FromArgb(160, 255, 255, 255), 1f))
                {
                    graphics.FillPath(backgroundBrush, path);
                    graphics.DrawPath(borderPen, path);
                }

                float valueFontSize = value.Length >= 3 ? 10.5f : 13.5f;
                using (Font valueFont = new Font("Segoe UI", valueFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Font badgeFont = new Font("Segoe UI", 7.5f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush textBrush = new SolidBrush(Color.White))
                using (SolidBrush badgeBrush = new SolidBrush(Color.FromArgb(215, 255, 255, 255)))
                using (StringFormat centered = new StringFormat())
                {
                    centered.Alignment = StringAlignment.Center;
                    centered.LineAlignment = StringAlignment.Center;
                    graphics.DrawString(value, valueFont, textBrush, new RectangleF(1, 5, 30, 25), centered);
                    graphics.DrawString(badge, badgeFont, badgeBrush, new PointF(3, 1));
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon temporary = Icon.FromHandle(handle))
                    {
                        return (Icon)temporary.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            float diameter = radius * 2f;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}

