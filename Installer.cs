using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Monitor Fader Setup")]
[assembly: AssemblyDescription("Installer for Monitor Fader")]
[assembly: AssemblyCompany("Monitor Fader")]
[assembly: AssemblyProduct("Monitor Fader Setup")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace MonitorFaderSetup
{
    internal static class Native
    {
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(Point pt, uint dwFlags);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        public const uint MONITOR_DEFAULTTONEAREST = 2;
        public const uint SHCNE_ASSOCCHANGED = 0x08000000;
        public const uint SHCNF_IDLIST = 0x0000;

        public static float GetTargetScale(out Screen screen)
        {
            screen = Screen.FromPoint(Cursor.Position);
            try
            {
                Point center = new Point(screen.Bounds.Left + screen.Bounds.Width / 2, screen.Bounds.Top + screen.Bounds.Height / 2);
                IntPtr hMon = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
                uint dx, dy;
                if (hMon != IntPtr.Zero && GetDpiForMonitor(hMon, 0, out dx, out dy) == 0 && dx >= 96)
                    return dx / 96f;
            }
            catch { }

            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    if (g.DpiX >= 96f) return g.DpiX / 96f;
                }
            }
            catch { }

            return 1f;
        }
    }

    internal static class ShortcutHelper
    {
        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("0000010b-0000-0000-C000-000000000046")]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        public static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string description, string iconPath)
        {
            string parent = Path.GetDirectoryName(shortcutPath);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                Directory.CreateDirectory(parent);

            IShellLinkW link = (IShellLinkW)new ShellLink();
            try
            {
                link.SetPath(targetPath);
                link.SetWorkingDirectory(workingDir);
                link.SetDescription(description ?? string.Empty);
                if (!string.IsNullOrEmpty(iconPath))
                    link.SetIconLocation(iconPath, 0);

                IPersistFile file = (IPersistFile)link;
                file.Save(shortcutPath, true);
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
        }
    }

    internal static class InstallEngine
    {
        public const string AppName = "Monitor Fader";
        public const string AppVersion = "1.0.0";
        private const string UninstallRegPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MonitorFader";
        private const string RunRegPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "MonitorFader";

        public static string DefaultInstallDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\MonitorFader");
            }
        }

        public static Icon LoadEmbeddedIcon()
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MonitorFader.ico"))
                {
                    if (s != null) return new Icon(s);
                }
            }
            catch { }
            try
            {
                return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }
            return SystemIcons.Application;
        }

        public static void StopRunningMonitorFader()
        {
            Process[] procs = Process.GetProcessesByName("MonitorFader");
            foreach (var p in procs)
            {
                try
                {
                    if (!p.HasExited)
                    {
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }
            Thread.Sleep(250);
        }

        public static void Install(string installDir, bool createStartMenu, bool createDesktop, bool autoStart, bool launchAfter)
        {
            StopRunningMonitorFader();

            if (!Directory.Exists(installDir))
                Directory.CreateDirectory(installDir);

            string exeDst = Path.Combine(installDir, "MonitorFader.exe");
            string icoDst = Path.Combine(installDir, "MonitorFader.ico");
            string uninstDst = Path.Combine(installDir, "Uninstall.exe");

            using (Stream exeStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MonitorFader.Payload.exe"))
            {
                if (exeStream == null)
                    throw new InvalidOperationException("Embedded MonitorFader.exe payload was not found.");
                using (var fs = new FileStream(exeDst, FileMode.Create, FileAccess.Write))
                {
                    exeStream.CopyTo(fs);
                }
            }

            using (Stream icoStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MonitorFader.ico"))
            {
                if (icoStream != null)
                {
                    using (var fs = new FileStream(icoDst, FileMode.Create, FileAccess.Write))
                    {
                        icoStream.CopyTo(fs);
                    }
                }
            }

            string selfPath = Application.ExecutablePath;
            if (!string.Equals(selfPath, uninstDst, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(selfPath, uninstDst, true);
            }

            if (createStartMenu)
            {
                string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                string startLnk = Path.Combine(startMenu, "Monitor Fader.lnk");
                ShortcutHelper.CreateShortcut(startLnk, exeDst, installDir, "Monitor Fader - Multi-Monitor Fading, Taskbar & Window Manager", exeDst);
            }

            if (createDesktop)
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string deskLnk = Path.Combine(desktop, "Monitor Fader.lnk");
                ShortcutHelper.CreateShortcut(deskLnk, exeDst, installDir, "Monitor Fader - Multi-Monitor Fading, Taskbar & Window Manager", exeDst);
            }

            using (var runKey = Registry.CurrentUser.OpenSubKey(RunRegPath, true) ?? Registry.CurrentUser.CreateSubKey(RunRegPath))
            {
                if (runKey != null)
                {
                    if (autoStart)
                        runKey.SetValue(RunValueName, "\"" + exeDst + "\" --minimized");
                    else
                        runKey.DeleteValue(RunValueName, false);
                }
            }

            long exeKb = Math.Max(1, new FileInfo(exeDst).Length / 1024);
            using (var uninstKey = Registry.CurrentUser.CreateSubKey(UninstallRegPath))
            {
                if (uninstKey != null)
                {
                    uninstKey.SetValue("DisplayName", AppName);
                    uninstKey.SetValue("DisplayVersion", AppVersion);
                    uninstKey.SetValue("Publisher", AppName);
                    uninstKey.SetValue("DisplayIcon", exeDst + ",0");
                    uninstKey.SetValue("InstallLocation", installDir);
                    uninstKey.SetValue("UninstallString", "\"" + uninstDst + "\" /uninstall");
                    uninstKey.SetValue("QuietUninstallString", "\"" + uninstDst + "\" /uninstall /silent");
                    uninstKey.SetValue("EstimatedSize", (int)exeKb, RegistryValueKind.DWord);
                    uninstKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    uninstKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
            }

            Native.SHChangeNotify(Native.SHCNE_ASSOCCHANGED, Native.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

            if (launchAfter)
            {
                Process.Start(new ProcessStartInfo(exeDst)
                {
                    WorkingDirectory = installDir,
                    UseShellExecute = true
                });
            }
        }

        public static void Uninstall()
        {
            StopRunningMonitorFader();

            string installDir = DefaultInstallDir;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(UninstallRegPath, false))
                {
                    if (key != null)
                    {
                        string loc = key.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrWhiteSpace(loc))
                            installDir = loc;
                    }
                }
            }
            catch { }

            try
            {
                using (var runKey = Registry.CurrentUser.OpenSubKey(RunRegPath, true))
                {
                    if (runKey != null) runKey.DeleteValue(RunValueName, false);
                }
            }
            catch { }

            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallRegPath, false); } catch { }

            try
            {
                string startLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Monitor Fader.lnk");
                if (File.Exists(startLnk)) File.Delete(startLnk);
            }
            catch { }

            try
            {
                string deskLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Monitor Fader.lnk");
                if (File.Exists(deskLnk)) File.Delete(deskLnk);
            }
            catch { }

            try
            {
                string exeDst = Path.Combine(installDir, "MonitorFader.exe");
                if (File.Exists(exeDst)) File.Delete(exeDst);
                string icoDst = Path.Combine(installDir, "MonitorFader.ico");
                if (File.Exists(icoDst)) File.Delete(icoDst);
            }
            catch { }

            Native.SHChangeNotify(Native.SHCNE_ASSOCCHANGED, Native.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

            // Schedule cleanup of Uninstall.exe & directory after this process exits
            try
            {
                if (Directory.Exists(installDir))
                {
                    string cmd = string.Format("/c ping 127.0.0.1 -n 3 > nul & rd /s /q \"{0}\"", installDir);
                    Process.Start(new ProcessStartInfo("cmd.exe", cmd)
                    {
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
            }
            catch { }
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly Icon _icon;
        private readonly float _scale;
        private TextBox _txtPath;
        private Button _btnBrowse;
        private CheckBox _chkStartMenu;
        private CheckBox _chkDesktop;
        private CheckBox _chkAutoStart;
        private CheckBox _chkLaunch;
        private ProgressBar _progress;
        private Label _lblStatus;
        private Button _btnInstall;
        private Button _btnCancel;
        private bool _installed;
        private bool _dark;

        private int S(int px)
        {
            return (int)Math.Round(px * _scale);
        }

        private Font F(float px, FontStyle style = FontStyle.Regular)
        {
            return new Font("Segoe UI", Math.Max(9f, px * _scale), style, GraphicsUnit.Pixel);
        }

        public SetupForm()
        {
            _icon = InstallEngine.LoadEmbeddedIcon();
            _dark = IsAppsDarkMode();

            Screen targetScreen;
            _scale = Native.GetTargetScale(out targetScreen);

            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Text = "Monitor Fader Setup";
            Icon = _icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(S(540), S(430));
            Font = F(13f);

            Rectangle wa = targetScreen.WorkingArea;
            Location = new Point(
                wa.Left + Math.Max(0, (wa.Width - Width) / 2),
                wa.Top + Math.Max(0, (wa.Height - Height) / 2));

            Color bg = _dark ? Color.FromArgb(26, 28, 34) : Color.FromArgb(247, 249, 252);
            Color cardBg = _dark ? Color.FromArgb(35, 38, 46) : Color.White;
            Color fg = _dark ? Color.FromArgb(236, 240, 246) : Color.FromArgb(28, 32, 38);
            Color muted = _dark ? Color.FromArgb(164, 174, 190) : Color.FromArgb(95, 104, 118);
            Color border = _dark ? Color.FromArgb(56, 62, 74) : Color.FromArgb(214, 220, 228);

            BackColor = bg;
            ForeColor = fg;

            int pad = S(24);
            int headerH = S(92);
            var header = new Panel { Location = new Point(0, 0), Size = new Size(ClientSize.Width, headerH), BackColor = cardBg };
            header.Paint += (s, e) =>
            {
                using (var pen = new Pen(border))
                    e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
            };
            Controls.Add(header);

            int iconSz = S(48);
            var pic = new PictureBox
            {
                Location = new Point(pad, (headerH - iconSz) / 2),
                Size = new Size(iconSz, iconSz),
                SizeMode = PictureBoxSizeMode.StretchImage,
                Image = new Icon(_icon, iconSz, iconSz).ToBitmap()
            };
            header.Controls.Add(pic);

            int titleX = pad + iconSz + S(16);
            var lblTitle = new Label
            {
                Text = "Install Monitor Fader",
                Font = F(19f, FontStyle.Bold),
                Location = new Point(titleX, S(18)),
                Size = new Size(ClientSize.Width - titleX - pad, S(28)),
                ForeColor = fg,
                UseMnemonic = false
            };
            header.Controls.Add(lblTitle);

            var lblSub = new Label
            {
                Text = "Version 1.0.0  •  Multi-Monitor Fading, TitleBar & Taskbars",
                Font = F(12.5f),
                Location = new Point(titleX, S(50)),
                Size = new Size(ClientSize.Width - titleX - pad, S(22)),
                ForeColor = muted,
                UseMnemonic = false
            };
            header.Controls.Add(lblSub);

            int y = headerH + S(20);
            var lblDir = new Label
            {
                Text = "Installation Folder:",
                Font = F(13f),
                Location = new Point(pad, y),
                Size = new Size(ClientSize.Width - pad * 2, S(22)),
                ForeColor = fg,
                UseMnemonic = false
            };
            Controls.Add(lblDir);

            y += S(26);
            int browseW = S(104);
            int gap = S(10);
            int rowH = S(30);
            _txtPath = new TextBox
            {
                Text = InstallEngine.DefaultInstallDir,
                Font = F(12.5f),
                Location = new Point(pad, y + S(2)),
                Size = new Size(ClientSize.Width - pad * 2 - browseW - gap, rowH - S(4)),
                BackColor = cardBg,
                ForeColor = fg,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_txtPath);

            _btnBrowse = new Button
            {
                Text = "Browse...",
                Font = F(12.5f),
                Location = new Point(ClientSize.Width - pad - browseW, y),
                Size = new Size(browseW, rowH),
                FlatStyle = FlatStyle.Flat,
                BackColor = cardBg,
                ForeColor = fg,
                UseMnemonic = false
            };
            _btnBrowse.FlatAppearance.BorderColor = border;
            _btnBrowse.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.SelectedPath = _txtPath.Text;
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                        _txtPath.Text = dlg.SelectedPath;
                }
            };
            Controls.Add(_btnBrowse);

            y += rowH + S(18);
            _chkStartMenu = CreateCheck("Create Start Menu shortcut", true, pad, y, fg);
            y += S(30);
            _chkDesktop = CreateCheck("Create Desktop shortcut", true, pad, y, fg);
            y += S(30);
            _chkAutoStart = CreateCheck("Start Monitor Fader automatically with Windows", true, pad, y, fg);
            y += S(30);
            _chkLaunch = CreateCheck("Launch Monitor Fader after installation", true, pad, y, fg);

            y += S(40);
            _progress = new ProgressBar
            {
                Location = new Point(pad, y),
                Size = new Size(ClientSize.Width - pad * 2, S(16)),
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            Controls.Add(_progress);

            _lblStatus = new Label
            {
                Text = "Ready to install.",
                Font = F(12.5f),
                Location = new Point(pad, y + S(24)),
                Size = new Size(ClientSize.Width - pad * 2, S(24)),
                ForeColor = muted,
                UseMnemonic = false
            };
            Controls.Add(_lblStatus);

            int btnW = S(104);
            int cancelW = S(98);
            int btnH = S(34);
            int btnY = ClientSize.Height - btnH - S(18);

            _btnInstall = new Button
            {
                Text = "Install",
                Location = new Point(ClientSize.Width - pad - cancelW - S(10) - btnW, btnY),
                Size = new Size(btnW, btnH),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 120, 212),
                ForeColor = Color.White,
                Font = F(13f, FontStyle.Bold),
                UseMnemonic = false
            };
            _btnInstall.FlatAppearance.BorderSize = 0;
            _btnInstall.Click += OnInstallClick;
            Controls.Add(_btnInstall);

            _btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(ClientSize.Width - pad - cancelW, btnY),
                Size = new Size(cancelW, btnH),
                FlatStyle = FlatStyle.Flat,
                BackColor = cardBg,
                ForeColor = fg,
                Font = F(13f),
                UseMnemonic = false
            };
            _btnCancel.FlatAppearance.BorderColor = border;
            _btnCancel.Click += (s, e) => Close();
            Controls.Add(_btnCancel);

            AcceptButton = _btnInstall;
            CancelButton = _btnCancel;

            ResumeLayout(false);
            PerformLayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_dark)
            {
                int useDark = 1;
                if (Native.DwmSetWindowAttribute(Handle, 20, ref useDark, sizeof(int)) != 0)
                    Native.DwmSetWindowAttribute(Handle, 19, ref useDark, sizeof(int));
            }
        }

        private CheckBox CreateCheck(string text, bool isChecked, int x, int y, Color fg)
        {
            var chk = new CheckBox
            {
                Text = text,
                Checked = isChecked,
                Location = new Point(x, y),
                Size = new Size(ClientSize.Width - x - S(24), S(24)),
                Font = F(13f),
                ForeColor = fg,
                UseMnemonic = false
            };
            Controls.Add(chk);
            return chk;
        }

        private void OnInstallClick(object sender, EventArgs e)
        {
            if (_installed)
            {
                Close();
                return;
            }

            string dir = (_txtPath.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(dir))
            {
                MessageBox.Show(this, "Please select a valid installation folder.", "Monitor Fader Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _btnInstall.Enabled = false;
            _btnBrowse.Enabled = false;
            _txtPath.Enabled = false;
            _progress.Value = 25;
            _lblStatus.Text = "Installing Monitor Fader...";
            Application.DoEvents();

            try
            {
                InstallEngine.Install(dir, _chkStartMenu.Checked, _chkDesktop.Checked, _chkAutoStart.Checked, _chkLaunch.Checked);
                _progress.Value = 100;
                _lblStatus.Text = "Installation completed successfully!";
                _installed = true;
                _btnInstall.Text = "Finish";
                _btnInstall.Enabled = true;
                _btnCancel.Visible = false;
            }
            catch (Exception ex)
            {
                _progress.Value = 0;
                _lblStatus.Text = "Installation failed.";
                _btnInstall.Enabled = true;
                _btnBrowse.Enabled = true;
                _txtPath.Enabled = true;
                MessageBox.Show(this, "Installation error:\n" + ex.Message, "Monitor Fader Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool IsAppsDarkMode()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    if (key != null)
                    {
                        object v = key.GetValue("AppsUseLightTheme");
                        if (v is int) return ((int)v) == 0;
                    }
                }
            }
            catch { }
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _icon != null && _icon != SystemIcons.Application)
                _icon.Dispose();
            base.Dispose(disposing);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool uninstall = false;
            bool silent = false;
            if (args != null)
            {
                foreach (string a in args)
                {
                    if (string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-uninstall", StringComparison.OrdinalIgnoreCase))
                        uninstall = true;
                    if (string.Equals(a, "/silent", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-silent", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "/S", StringComparison.OrdinalIgnoreCase))
                        silent = true;
                }
            }

            if (uninstall)
            {
                if (!silent)
                {
                    var res = MessageBox.Show("Are you sure you want to uninstall Monitor Fader?", "Monitor Fader Uninstall", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (res != DialogResult.Yes) return;
                }
                InstallEngine.Uninstall();
                if (!silent)
                    MessageBox.Show("Monitor Fader has been uninstalled.", "Monitor Fader Uninstall", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (silent)
            {
                InstallEngine.Install(InstallEngine.DefaultInstallDir, true, true, true, true);
                return;
            }

            Application.Run(new SetupForm());
        }
    }
}
