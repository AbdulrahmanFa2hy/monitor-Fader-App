using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MonitorFader
{
    public class WindowEntry
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; }
        public IntPtr MonitorHandle { get; set; }
        public bool IsMinimized { get; set; }
        public bool IsMaximized { get; set; }
        public bool IsForeground { get; set; }
        public string RealExePath { get; set; }
        public string GroupKey { get; set; }
    }

    public static class WindowHelper
    {
        private static readonly HashSet<IntPtr> InternalHandles = new HashSet<IntPtr>();
        private static readonly Dictionary<IntPtr, string> WindowExeCache = new Dictionary<IntPtr, string>();
        private static readonly Dictionary<IntPtr, string> WindowGroupKeyCache = new Dictionary<IntPtr, string>();
        private static readonly object HandleLock = new object();

        private static readonly HashSet<string> IgnoredClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Progman",
            "WorkerW",
            "Shell_TrayWnd",
            "Shell_SecondaryTrayWnd",
            "NotifyIconOverflowWindow",
            "TopLevelWindowForOverflowXamlIsland",
            "Windows.UI.Core.CoreWindow",
            "Xaml_WindowedPopupClass"
        };

        public static void RegisterInternalHandle(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            lock (HandleLock) { InternalHandles.Add(hWnd); }
        }

        public static void UnregisterInternalHandle(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            lock (HandleLock) { InternalHandles.Remove(hWnd); }
        }

        public static bool IsInternalHandle(IntPtr hWnd)
        {
            lock (HandleLock) { return InternalHandles.Contains(hWnd); }
        }

        public static bool IsEligibleWindow(IntPtr hWnd, bool allowToolOrOwned = false)
        {
            if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd) || !NativeMethods.IsWindowVisible(hWnd))
                return false;

            if (IsInternalHandle(hWnd))
                return false;

            int cloaked = 0;
            if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0)
                return false;

            var clsBuf = new StringBuilder(128);
            int clsLen = NativeMethods.GetClassName(hWnd, clsBuf, clsBuf.Capacity);
            if (clsLen > 0 && IgnoredClasses.Contains(clsBuf.ToString()))
                return false;

            int exStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);
            if ((exStyle & NativeMethods.WS_EX_NOACTIVATE) != 0)
                return false;

            if (!allowToolOrOwned)
            {
                bool isAppWindow = (exStyle & NativeMethods.WS_EX_APPWINDOW) != 0;
                if (!isAppWindow)
                {
                    if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
                        return false;
                    if (NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER) != IntPtr.Zero)
                        return false;
                }
            }

            if (NativeMethods.GetWindowTextLength(hWnd) <= 0)
                return false;

            return true;
        }

        public static string GetWindowTitle(IntPtr hWnd)
        {
            int len = NativeMethods.GetWindowTextLength(hWnd);
            if (len <= 0) return string.Empty;
            var sb = new StringBuilder(len + 2);
            NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static IntPtr GetWindowMonitor(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return IntPtr.Zero;

            if (NativeMethods.IsIconic(hWnd))
            {
                try
                {
                    var wp = new NativeMethods.WINDOWPLACEMENT
                    {
                        length = Marshal.SizeOf(typeof(NativeMethods.WINDOWPLACEMENT))
                    };
                    if (NativeMethods.GetWindowPlacement(hWnd, ref wp))
                    {
                        NativeMethods.RECT rc = wp.rcNormalPosition;
                        if (rc.Right > rc.Left && rc.Bottom > rc.Top)
                        {
                            // rcNormalPosition is in workspace coordinates relative to primary monitor work area origin
                            Point workOrigin = Screen.PrimaryScreen != null ? Screen.PrimaryScreen.WorkingArea.Location : Point.Empty;
                            rc.Left += workOrigin.X;
                            rc.Right += workOrigin.X;
                            rc.Top += workOrigin.Y;
                            rc.Bottom += workOrigin.Y;
                            IntPtr monFromPlace = NativeMethods.MonitorFromRect(ref rc, NativeMethods.MONITOR_DEFAULTTONEAREST);
                            if (monFromPlace != IntPtr.Zero)
                                return monFromPlace;
                        }
                    }
                }
                catch
                {
                }
            }

            return NativeMethods.MonitorFromWindow(hWnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        }

        public static List<WindowEntry> GetTaskbarWindows()
        {
            PruneDeadWindowCaches();

            var list = new List<WindowEntry>();
            IntPtr fg = NativeMethods.GetForegroundWindow();

            NativeMethods.EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (!IsEligibleWindow(hWnd, false))
                    return true;

                string title = GetWindowTitle(hWnd);
                if (string.IsNullOrWhiteSpace(title))
                    return true;

                IntPtr mon = GetWindowMonitor(hWnd);
                string realExe = ResolveRealExecutablePath(hWnd);
                string groupKey = GetWindowGroupKey(hWnd, realExe);
                list.Add(new WindowEntry
                {
                    Handle = hWnd,
                    Title = title,
                    MonitorHandle = mon,
                    IsMinimized = NativeMethods.IsIconic(hWnd),
                    IsMaximized = NativeMethods.IsZoomed(hWnd),
                    IsForeground = (hWnd == fg),
                    RealExePath = realExe,
                    GroupKey = groupKey
                });
                return true;
            }, IntPtr.Zero);

            return list;
        }

        private static void PruneDeadWindowCaches()
        {
            lock (HandleLock)
            {
                if (WindowExeCache.Count > 0)
                {
                    var dead = new List<IntPtr>();
                    foreach (var h in WindowExeCache.Keys)
                    {
                        if (!NativeMethods.IsWindow(h))
                            dead.Add(h);
                    }
                    foreach (var h in dead)
                        WindowExeCache.Remove(h);
                }

                if (WindowGroupKeyCache.Count > 0)
                {
                    var dead = new List<IntPtr>();
                    foreach (var h in WindowGroupKeyCache.Keys)
                    {
                        if (!NativeMethods.IsWindow(h))
                            dead.Add(h);
                    }
                    foreach (var h in dead)
                        WindowGroupKeyCache.Remove(h);
                }
            }
        }

        public static string ResolveRealExecutablePath(IntPtr hWnd)
        {
            string cached = null;
            lock (HandleLock)
            {
                WindowExeCache.TryGetValue(hWnd, out cached);
            }
            if (!string.IsNullOrEmpty(cached)
                && !string.Equals(Path.GetFileName(cached), "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            {
                return cached;
            }

            string exe = GetProcessExecutablePath(hWnd);
            if (string.IsNullOrEmpty(exe))
                return cached;

            string resolved = exe;
            if (string.Equals(Path.GetFileName(exe), "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            {
                uint hostPid;
                NativeMethods.GetWindowThreadProcessId(hWnd, out hostPid);
                string childExe = null;
                NativeMethods.EnumChildWindows(hWnd, delegate(IntPtr child, IntPtr lParam)
                {
                    uint cpid;
                    NativeMethods.GetWindowThreadProcessId(child, out cpid);
                    if (cpid != 0 && cpid != hostPid)
                    {
                        string candidate = GetProcessExecutablePathByPid(cpid);
                        if (!string.IsNullOrEmpty(candidate))
                        {
                            childExe = candidate;
                            return false;
                        }
                    }
                    return true;
                }, IntPtr.Zero);

                if (!string.IsNullOrEmpty(childExe))
                    resolved = childExe;
            }

            if (!string.IsNullOrEmpty(resolved))
            {
                lock (HandleLock)
                {
                    WindowExeCache[hWnd] = resolved;
                }
            }
            return resolved;
        }

        private static string GetProcessExecutablePathByPid(uint pid)
        {
            if (pid == 0) return null;
            IntPtr hProc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProc == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (NativeMethods.QueryFullProcessImageName(hProc, 0, sb, ref size))
                    return sb.ToString();
            }
            finally
            {
                NativeMethods.CloseHandle(hProc);
            }
            return null;
        }

        public static string GetAppUserModelId(IntPtr hWnd)
        {
            try
            {
                Guid iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
                NativeMethods.IPropertyStore store;
                if (NativeMethods.SHGetPropertyStoreForWindow(hWnd, ref iid, out store) == 0 && store != null)
                {
                    try
                    {
                        var key = new NativeMethods.PROPERTYKEY
                        {
                            fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
                            pid = 5 // PKEY_AppUserModel_ID
                        };
                        NativeMethods.PROPVARIANT pv;
                        if (store.GetValue(ref key, out pv) == 0)
                        {
                            try
                            {
                                if (pv.vt == 31 /* VT_LPWSTR */ && pv.p != IntPtr.Zero)
                                    return Marshal.PtrToStringUni(pv.p);
                            }
                            finally
                            {
                                NativeMethods.PropVariantClear(ref pv);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(store);
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        public static string GetWindowGroupKey(IntPtr hWnd, string realExePath)
        {
            string cached = null;
            lock (HandleLock)
            {
                WindowGroupKeyCache.TryGetValue(hWnd, out cached);
            }

            if (!string.IsNullOrEmpty(cached)
                && !cached.StartsWith("hwnd:", StringComparison.OrdinalIgnoreCase)
                && !cached.EndsWith("\\applicationframehost.exe", StringComparison.OrdinalIgnoreCase))
            {
                return cached;
            }

            string computed = null;
            string aumid = GetAppUserModelId(hWnd);
            if (!string.IsNullOrWhiteSpace(aumid))
                computed = "aumid:" + aumid.Trim().ToLowerInvariant();
            else if (!string.IsNullOrWhiteSpace(realExePath))
                computed = "exe:" + realExePath.Trim().ToLowerInvariant();
            else
                computed = "hwnd:" + hWnd.ToInt64();

            lock (HandleLock)
            {
                WindowGroupKeyCache[hWnd] = computed;
            }
            return computed;
        }

        public static Icon GetWindowIcon(IntPtr hWnd, bool preferBig = false)
        {
            try
            {
                IntPtr hIcon = IntPtr.Zero;
                IntPtr res;
                if (preferBig)
                {
                    NativeMethods.SendMessageTimeout(hWnd, NativeMethods.WM_GETICON, new IntPtr(NativeMethods.ICON_BIG), IntPtr.Zero, 2, 40, out res);
                    hIcon = res;
                    if (hIcon == IntPtr.Zero)
                        hIcon = NativeMethods.GetClassLongPtrSafe(hWnd, NativeMethods.GCLP_HICON);
                }

                if (hIcon == IntPtr.Zero)
                {
                    NativeMethods.SendMessageTimeout(hWnd, NativeMethods.WM_GETICON, new IntPtr(NativeMethods.ICON_SMALL2), IntPtr.Zero, 2, 40, out res);
                    hIcon = res;
                }
                if (hIcon == IntPtr.Zero)
                {
                    NativeMethods.SendMessageTimeout(hWnd, NativeMethods.WM_GETICON, new IntPtr(NativeMethods.ICON_SMALL), IntPtr.Zero, 2, 40, out res);
                    hIcon = res;
                }
                if (hIcon == IntPtr.Zero && !preferBig)
                {
                    NativeMethods.SendMessageTimeout(hWnd, NativeMethods.WM_GETICON, new IntPtr(NativeMethods.ICON_BIG), IntPtr.Zero, 2, 40, out res);
                    hIcon = res;
                }
                if (hIcon == IntPtr.Zero)
                    hIcon = NativeMethods.GetClassLongPtrSafe(hWnd, NativeMethods.GCLP_HICONSM);
                if (hIcon == IntPtr.Zero)
                    hIcon = NativeMethods.GetClassLongPtrSafe(hWnd, NativeMethods.GCLP_HICON);

                if (hIcon != IntPtr.Zero)
                {
                    using (var tmp = Icon.FromHandle(hIcon))
                    {
                        return (Icon)tmp.Clone();
                    }
                }

                string exe = GetProcessExecutablePath(hWnd);
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                    return Icon.ExtractAssociatedIcon(exe);
            }
            catch
            {
                // Ignore icon extraction errors for protected/system windows
            }
            return null;
        }

        public static Bitmap GetWindowIconBitmap(IntPtr hWnd, int size)
        {
            using (Icon ic = GetWindowIcon(hWnd, true))
            {
                if (ic == null) return null;
                return RenderIconToBitmap(ic, size);
            }
        }

        public static Bitmap GetAppIconBitmap(IntPtr hWnd, string realExePath, int size)
        {
            // 1) For File Explorer windows, prefer the classic File Explorer icon from explorer.exe
            if (!string.IsNullOrEmpty(realExePath)
                && string.Equals(Path.GetFileName(realExePath), "explorer.exe", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using (Icon expIcon = Icon.ExtractAssociatedIcon(realExePath))
                    {
                        if (expIcon != null)
                        {
                            Bitmap b = RenderIconToBitmap(expIcon, size);
                            if (b != null) return b;
                        }
                    }
                }
                catch { }
            }

            // 2) For UWP/MSIX apps (e.g. Sticky Notes, Settings, Calculator), resolve the native logo from AppxManifest.xml
            if (!string.IsNullOrEmpty(realExePath))
            {
                Bitmap uwpBmp = TryLoadAppxManifestLogo(realExePath, size);
                if (uwpBmp != null)
                    return uwpBmp;
            }

            // 3) Standard Win32 window icon (BIG -> SMALL -> Associated EXE icon)
            Bitmap winBmp = GetWindowIconBitmap(hWnd, size);
            if (winBmp != null)
                return winBmp;

            if (!string.IsNullOrEmpty(realExePath) && File.Exists(realExePath))
            {
                try
                {
                    using (Icon exeIcon = Icon.ExtractAssociatedIcon(realExePath))
                    {
                        if (exeIcon != null)
                            return RenderIconToBitmap(exeIcon, size);
                    }
                }
                catch { }
            }

            return null;
        }

        private static Bitmap RenderIconToBitmap(Icon ic, int size)
        {
            try
            {
                using (Bitmap src = ic.ToBitmap())
                {
                    var dst = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
                    using (var g = Graphics.FromImage(dst))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(src, 0, 0, size, size);
                    }
                    return dst;
                }
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap TryLoadAppxManifestLogo(string exePath, int size)
        {
            try
            {
                string dir = Path.GetDirectoryName(exePath);
                if (string.IsNullOrEmpty(dir)) return null;

                string manifest = Path.Combine(dir, "AppxManifest.xml");
                if (!File.Exists(manifest))
                {
                    string parent = Path.GetDirectoryName(dir);
                    if (!string.IsNullOrEmpty(parent) && File.Exists(Path.Combine(parent, "AppxManifest.xml")))
                    {
                        dir = parent;
                        manifest = Path.Combine(parent, "AppxManifest.xml");
                    }
                    else
                    {
                        return null;
                    }
                }

                string xml = File.ReadAllText(manifest);
                Match m = Regex.Match(xml, @"Square44x44Logo\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
                if (!m.Success)
                    m = Regex.Match(xml, @"Logo\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
                if (!m.Success)
                    return null;

                string relLogo = m.Groups[1].Value.Replace('/', '\\');
                string fullBase = Path.Combine(dir, relLogo);
                string assetDir = Path.GetDirectoryName(fullBase);
                string stem = Path.GetFileNameWithoutExtension(fullBase);
                string ext = Path.GetExtension(fullBase);
                if (string.IsNullOrEmpty(ext)) ext = ".png";

                string chosen = File.Exists(fullBase) ? fullBase : null;
                if (Directory.Exists(assetDir))
                {
                    string[] candidates = Directory.GetFiles(assetDir, stem + "*" + ext);
                    int bestScore = -1;
                    foreach (string c in candidates)
                    {
                        string name = Path.GetFileName(c).ToLowerInvariant();
                        if (name.Contains("contrast-black") || name.Contains("contrast-white"))
                            continue;

                        int score = 10;
                        if (name.Contains("altform-unplated")) score += 50;
                        if (name.Contains("targetsize-48")) score += 40;
                        else if (name.Contains("targetsize-32")) score += 30;
                        else if (name.Contains("scale-200")) score += 25;
                        else if (name.Contains("scale-150")) score += 20;
                        else if (name.Contains("targetsize-24")) score += 15;

                        if (score > bestScore)
                        {
                            bestScore = score;
                            chosen = c;
                        }
                    }
                }

                if (string.IsNullOrEmpty(chosen) || !File.Exists(chosen))
                    return null;

                using (var src = new Bitmap(chosen))
                {
                    var dst = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
                    using (var g = Graphics.FromImage(dst))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(src, 0, 0, size, size);
                    }
                    return dst;
                }
            }
            catch
            {
                return null;
            }
        }

        public static void UpdateLayeredBitmap(Form form, Bitmap bmp, int x, int y)
        {
            if (form == null || form.IsDisposed || !form.IsHandleCreated || bmp == null)
                return;

            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            try
            {
                hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
                oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

                var size = new NativeMethods.SIZE { cx = bmp.Width, cy = bmp.Height };
                var pointSource = new NativeMethods.POINT { X = 0, Y = 0 };
                var topPos = new NativeMethods.POINT { X = x, Y = y };
                var blend = new NativeMethods.BLENDFUNCTION
                {
                    BlendOp = NativeMethods.AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = NativeMethods.AC_SRC_ALPHA
                };

                NativeMethods.UpdateLayeredWindow(
                    form.Handle,
                    screenDc,
                    ref topPos,
                    ref size,
                    memDc,
                    ref pointSource,
                    0,
                    ref blend,
                    NativeMethods.ULW_ALPHA);
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
                if (oldBitmap != IntPtr.Zero)
                    NativeMethods.SelectObject(memDc, oldBitmap);
                if (hBitmap != IntPtr.Zero)
                    NativeMethods.DeleteObject(hBitmap);
                NativeMethods.DeleteDC(memDc);
            }
        }

        public static bool IsSystemLightTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("SystemUsesLightTheme");
                        if (val is int)
                            return ((int)val) == 1;
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        public static bool IsAppsDarkMode()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("AppsUseLightTheme");
                        if (val is int)
                            return ((int)val) == 0;
                    }
                }
            }
            catch
            {
            }
            return true;
        }

        public static void SetWindowDarkMode(IntPtr hWnd, bool dark)
        {
            if (hWnd == IntPtr.Zero) return;
            try
            {
                int useDark = dark ? 1 : 0;
                if (NativeMethods.DwmSetWindowAttribute(hWnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int)) != 0)
                {
                    NativeMethods.DwmSetWindowAttribute(hWnd, 19, ref useDark, sizeof(int));
                }
            }
            catch
            {
            }
        }

        public static bool IsScreenPointDark(int x, int y)
        {
            IntPtr dc = NativeMethods.GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return false;
            try
            {
                uint colorRef = NativeMethods.GetPixel(dc, x, y);
                if (colorRef == 0xFFFFFFFF) return false;
                int r = (int)(colorRef & 0xFF);
                int g = (int)((colorRef >> 8) & 0xFF);
                int b = (int)((colorRef >> 16) & 0xFF);
                double lum = 0.299 * r + 0.587 * g + 0.114 * b;
                return lum < 135.0;
            }
            catch
            {
                return false;
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, dc);
            }
        }

        public static void EnableRoundedCorners(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            try
            {
                int pref = NativeMethods.DWMWCP_ROUND;
                NativeMethods.DwmSetWindowAttribute(hWnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            }
            catch
            {
            }
        }

        public static GraphicsPath CreateRoundedRectPath(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
            if (d <= 0.5f)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static string GetProcessExecutablePath(IntPtr hWnd)
        {
            uint pid;
            NativeMethods.GetWindowThreadProcessId(hWnd, out pid);
            if (pid == 0) return null;

            IntPtr hProc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProc == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (NativeMethods.QueryFullProcessImageName(hProc, 0, sb, ref size))
                    return sb.ToString();
            }
            finally
            {
                NativeMethods.CloseHandle(hProc);
            }
            return null;
        }

        public static void MoveWindowToMonitor(IntPtr hWnd, bool reverse = false)
        {
            if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd))
                return;

            var monitors = MonitorManager.GetMonitors();
            if (monitors.Count < 2)
                return;

            IntPtr currentMon = NativeMethods.MonitorFromWindow(hWnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            int curIdx = monitors.FindIndex(m => m.HMonitor == currentMon);
            if (curIdx < 0) curIdx = 0;

            int nextIdx = (curIdx + (reverse ? -1 : 1) + monitors.Count) % monitors.Count;
            MonitorInfoItem src = monitors[curIdx];
            MonitorInfoItem dst = monitors[nextIdx];

            bool wasMinimized = NativeMethods.IsIconic(hWnd);
            bool wasMaximized = NativeMethods.IsZoomed(hWnd);

            NativeMethods.ReleaseCapture();
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);

            if (wasMinimized || wasMaximized)
            {
                NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
                if (NativeMethods.IsIconic(hWnd) || NativeMethods.IsZoomed(hWnd))
                {
                    IntPtr sysRes;
                    NativeMethods.SendMessageTimeout(
                        hWnd,
                        NativeMethods.WM_SYSCOMMAND,
                        new IntPtr(NativeMethods.SC_RESTORE),
                        IntPtr.Zero,
                        NativeMethods.SMTO_ABORTIFHUNG,
                        150,
                        out sysRes);
                }
            }

            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(hWnd, out rect))
                return;

            Rectangle srcWork = src.WorkArea;
            Rectangle dstWork = dst.WorkArea;

            double relX = srcWork.Width > 0 ? (double)(rect.Left - srcWork.Left) / srcWork.Width : 0.1;
            double relY = srcWork.Height > 0 ? (double)(rect.Top - srcWork.Top) / srcWork.Height : 0.1;
            double relW = srcWork.Width > 0 ? (double)rect.Width / srcWork.Width : 0.6;
            double relH = srcWork.Height > 0 ? (double)rect.Height / srcWork.Height : 0.6;

            int newW = Math.Max(220, Math.Min(dstWork.Width, (int)Math.Round(relW * dstWork.Width)));
            int newH = Math.Max(140, Math.Min(dstWork.Height, (int)Math.Round(relH * dstWork.Height)));

            int newX = dstWork.Left + (int)Math.Round(relX * dstWork.Width);
            int newY = dstWork.Top + (int)Math.Round(relY * dstWork.Height);

            newX = Math.Max(dstWork.Left, Math.Min(dstWork.Right - newW, newX));
            newY = Math.Max(dstWork.Top, Math.Min(dstWork.Bottom - newH, newY));

            if (wasMaximized)
            {
                // Place safely inside destination work area before maximizing so Windows maximizes on dst monitor
                int preW = Math.Min(rect.Width, Math.Max(300, dstWork.Width - 40));
                int preH = Math.Min(rect.Height, Math.Max(200, dstWork.Height - 40));
                int preX = dstWork.Left + (dstWork.Width - preW) / 2;
                int preY = dstWork.Top + (dstWork.Height - preH) / 2;
                NativeMethods.SetWindowPos(hWnd, IntPtr.Zero, preX, preY, preW, preH,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                NativeMethods.ShowWindow(hWnd, NativeMethods.SW_MAXIMIZE);
            }
            else
            {
                NativeMethods.SetWindowPos(hWnd, IntPtr.Zero, newX, newY, newW, newH,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                // Re-apply once in case Per-Monitor DPI scaling shifted dimensions during cross-monitor move
                NativeMethods.SetWindowPos(hWnd, IntPtr.Zero, newX, newY, newW, newH,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            }

            BringWindowToFront(hWnd);
        }

        public static void LogTaskbarDiag(string msg)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MonitorFader");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                string logPath = Path.Combine(dir, "taskbar_debug.log");
                string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}{2}", DateTime.Now, msg, Environment.NewLine);
                File.AppendAllText(logPath, line);
            }
            catch
            {
            }
        }

        private static IntPtr _pendingFollowUpHwnd;

        public static bool IsTopVisibleWindowOnMonitor(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd) || NativeMethods.IsIconic(hWnd))
                return false;

            NativeMethods.RECT targetRc;
            if (!NativeMethods.GetWindowRect(hWnd, out targetRc))
                return false;

            Rectangle targetRect = Rectangle.FromLTRB(targetRc.Left, targetRc.Top, targetRc.Right, targetRc.Bottom);
            IntPtr targetMon = GetWindowMonitor(hWnd);
            bool coveredByHigherWindow = false;

            NativeMethods.EnumWindows(delegate(IntPtr w, IntPtr lp)
            {
                if (w == hWnd)
                    return false;

                if (NativeMethods.IsIconic(w) || !IsEligibleWindow(w, false))
                    return true;

                if (GetWindowMonitor(w) != targetMon)
                    return true;

                NativeMethods.RECT wRc;
                if (NativeMethods.GetWindowRect(w, out wRc))
                {
                    Rectangle wRect = Rectangle.FromLTRB(wRc.Left, wRc.Top, wRc.Right, wRc.Bottom);
                    Rectangle inter = Rectangle.Intersect(targetRect, wRect);
                    if (inter.Width >= 16 && inter.Height >= 16)
                    {
                        coveredByHigherWindow = true;
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);

            return !coveredByHigherWindow;
        }

        public static void BringWindowToFront(IntPtr hWnd)
        {
            _pendingFollowUpHwnd = hWnd;
            ExecuteBringWindowToFrontCore(hWnd, false);

            // Schedule a 35ms follow-up pass on the UI message loop after mouse-up / async restore completes
            var followUpTimer = new Timer { Interval = 35 };
            followUpTimer.Tick += (s, e) =>
            {
                followUpTimer.Stop();
                followUpTimer.Dispose();
                if (_pendingFollowUpHwnd == hWnd && hWnd != IntPtr.Zero && NativeMethods.IsWindow(hWnd))
                {
                    IntPtr curFg = NativeMethods.GetForegroundWindow();
                    bool topOnMon = IsTopVisibleWindowOnMonitor(hWnd);
                    if (!topOnMon || curFg != hWnd || NativeMethods.IsIconic(hWnd))
                    {
                        ExecuteBringWindowToFrontCore(hWnd, true);
                    }
                }
            };
            followUpTimer.Start();
        }

        private static void ExecuteBringWindowToFrontCore(IntPtr hWnd, bool isFollowUpPass)
        {
            if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd))
                return;

            NativeMethods.ReleaseCapture();

            IntPtr targetHwnd = hWnd;
            IntPtr popup = NativeMethods.GetLastActivePopup(hWnd);
            if (popup != IntPtr.Zero && popup != hWnd && NativeMethods.IsWindow(popup) && NativeMethods.IsWindowVisible(popup))
            {
                targetHwnd = popup;
            }

            IntPtr fgBefore = NativeMethods.GetForegroundWindow();
            bool wasIconic = NativeMethods.IsIconic(hWnd);
            int initialExStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);
            bool targetWasTopMost = (initialExStyle & NativeMethods.WS_EX_TOPMOST) != 0;

            IntPtr targetMon = GetWindowMonitor(hWnd);
            var windowsAboveOnMon = new List<IntPtr>();
            NativeMethods.EnumWindows(delegate(IntPtr w, IntPtr lp)
            {
                if (w == hWnd || w == targetHwnd)
                    return false;
                if (!NativeMethods.IsIconic(w) && IsEligibleWindow(w, false) && GetWindowMonitor(w) == targetMon)
                {
                    windowsAboveOnMon.Add(w);
                }
                return true;
            }, IntPtr.Zero);

            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);

            uint pidUnused;
            uint fgThread = (fgBefore != IntPtr.Zero && !IsInternalHandle(fgBefore))
                ? NativeMethods.GetWindowThreadProcessId(fgBefore, out pidUnused)
                : 0;
            uint targetThread = NativeMethods.GetWindowThreadProcessId(targetHwnd, out pidUnused);
            uint curThread = NativeMethods.GetCurrentThreadId();

            bool attachedFg = false;
            bool attachedTarget = false;
            bool sentAlt = false;
            bool setFgOk = false;

            try
            {
                if (fgThread != 0 && fgThread != curThread)
                    attachedFg = NativeMethods.AttachThreadInput(curThread, fgThread, true);
                if (targetThread != 0 && targetThread != curThread && targetThread != fgThread)
                    attachedTarget = NativeMethods.AttachThreadInput(curThread, targetThread, true);

                NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETFOREGROUNDLOCKTIMEOUT, 0, IntPtr.Zero, 0);

                if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) == 0)
                {
                    NativeMethods.keybd_event((byte)NativeMethods.VK_MENU, 0, NativeMethods.KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                    sentAlt = true;
                }

                if (wasIconic)
                {
                    var wp = new NativeMethods.WINDOWPLACEMENT
                    {
                        length = Marshal.SizeOf(typeof(NativeMethods.WINDOWPLACEMENT))
                    };
                    bool restoreMax = false;
                    if (NativeMethods.GetWindowPlacement(hWnd, ref wp))
                    {
                        restoreMax = (wp.flags & NativeMethods.WPF_RESTORETOMAXIMIZED) != 0;
                    }

                    NativeMethods.ShowWindow(hWnd, restoreMax ? NativeMethods.SW_MAXIMIZE : NativeMethods.SW_RESTORE);
                    if (NativeMethods.IsIconic(hWnd))
                    {
                        IntPtr sysRes;
                        NativeMethods.SendMessageTimeout(
                            hWnd,
                            NativeMethods.WM_SYSCOMMAND,
                            new IntPtr(NativeMethods.SC_RESTORE),
                            IntPtr.Zero,
                            NativeMethods.SMTO_ABORTIFHUNG,
                            150,
                            out sysRes);
                    }
                }

                // Clear any stuck WS_EX_TOPMOST on other normal windows covering this monitor
                foreach (IntPtr above in windowsAboveOnMon)
                {
                    if (!NativeMethods.IsWindow(above)) continue;
                    int aboveEx = NativeMethods.GetWindowLong(above, NativeMethods.GWL_EXSTYLE);
                    if ((aboveEx & NativeMethods.WS_EX_TOPMOST) != 0)
                    {
                        NativeMethods.SetWindowPos(
                            above,
                            NativeMethods.HWND_NOTOPMOST,
                            0, 0, 0, 0,
                            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                    }
                }

                setFgOk = NativeMethods.SetForegroundWindow(targetHwnd);
                NativeMethods.BringWindowToTop(targetHwnd);
                NativeMethods.SetActiveWindow(targetHwnd);
                NativeMethods.SetFocus(targetHwnd);

                if (!targetWasTopMost)
                {
                    NativeMethods.SetWindowPos(
                        hWnd,
                        NativeMethods.HWND_TOPMOST,
                        0, 0, 0, 0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
                    NativeMethods.SetWindowPos(
                        hWnd,
                        NativeMethods.HWND_NOTOPMOST,
                        0, 0, 0, 0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
                }
                else
                {
                    NativeMethods.SetWindowPos(
                        hWnd,
                        NativeMethods.HWND_TOPMOST,
                        0, 0, 0, 0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
                }

                // Explicitly place any previously covering window on the same monitor behind hWnd in Z-order,
                // chaining each window after the previous one so their relative Z-order (MRU order) is strictly preserved.
                IntPtr insertAfter = hWnd;
                foreach (IntPtr above in windowsAboveOnMon)
                {
                    if (NativeMethods.IsWindow(above))
                    {
                        NativeMethods.SetWindowPos(
                            above,
                            insertAfter,
                            0, 0, 0, 0,
                            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                        insertAfter = above;
                    }
                }

                NativeMethods.BringWindowToTop(targetHwnd);
                setFgOk = NativeMethods.SetForegroundWindow(targetHwnd) || setFgOk;
            }
            finally
            {
                if (sentAlt)
                {
                    NativeMethods.keybd_event((byte)NativeMethods.VK_MENU, 0, NativeMethods.KEYEVENTF_EXTENDEDKEY | NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
                }
                if (attachedTarget)
                    NativeMethods.AttachThreadInput(curThread, targetThread, false);
                if (attachedFg)
                    NativeMethods.AttachThreadInput(curThread, fgThread, false);
            }

            if (NativeMethods.GetForegroundWindow() != targetHwnd && NativeMethods.GetForegroundWindow() != hWnd)
            {
                NativeMethods.SwitchToThisWindow(targetHwnd, true);
                NativeMethods.BringWindowToTop(targetHwnd);
                NativeMethods.SetForegroundWindow(targetHwnd);
            }

            IntPtr fgAfter = NativeMethods.GetForegroundWindow();
            bool topAfter = IsTopVisibleWindowOnMonitor(hWnd);
            NativeMethods.RECT rcAfter;
            NativeMethods.GetWindowRect(hWnd, out rcAfter);
            LogTaskbarDiag(string.Format(
                "BringWindowToFront(pass={0}) hWnd=0x{1:X} title='{2}' wasIconic={3} fgBefore=0x{4:X}('{5}') fgAfter=0x{6:X} setFgOk={7} aboveCount={8} topOnMonAfter={9} rect=({10},{11},{12},{13})",
                isFollowUpPass ? "FollowUp" : "Immediate",
                hWnd.ToInt64(),
                GetWindowTitle(hWnd),
                wasIconic,
                fgBefore.ToInt64(),
                GetWindowTitle(fgBefore),
                fgAfter.ToInt64(),
                setFgOk,
                windowsAboveOnMon.Count,
                topAfter,
                rcAfter.Left, rcAfter.Top, rcAfter.Right, rcAfter.Bottom));
        }

        public static void MinimizeWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd))
                return;

            if (_pendingFollowUpHwnd == hWnd)
                _pendingFollowUpHwnd = IntPtr.Zero;

            IntPtr targetMon = GetWindowMonitor(hWnd);

            // Capture the exact Z-order stack of visible windows on this monitor BEFORE minimizing hWnd,
            // so Windows cannot scramble their order during SW_MINIMIZE.
            var windowsBelowOnMon = new List<IntPtr>();
            NativeMethods.EnumWindows(delegate(IntPtr w, IntPtr lp)
            {
                if (w == hWnd || NativeMethods.IsIconic(w) || !IsEligibleWindow(w, false))
                    return true;
                if (GetWindowMonitor(w) == targetMon)
                    windowsBelowOnMon.Add(w);
                return true;
            }, IntPtr.Zero);

            NativeMethods.ReleaseCapture();
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_MINIMIZE);
            if (!NativeMethods.IsIconic(hWnd))
            {
                IntPtr sysRes;
                NativeMethods.SendMessageTimeout(
                    hWnd,
                    NativeMethods.WM_SYSCOMMAND,
                    new IntPtr(NativeMethods.SC_MINIMIZE),
                    IntPtr.Zero,
                    NativeMethods.SMTO_ABORTIFHUNG,
                    150,
                    out sysRes);
            }

            if (windowsBelowOnMon.Count > 0)
            {
                IntPtr nextTop = windowsBelowOnMon[0];
                NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
                NativeMethods.BringWindowToTop(nextTop);
                NativeMethods.SetForegroundWindow(nextTop);

                IntPtr insertAfter = nextTop;
                for (int i = 1; i < windowsBelowOnMon.Count; i++)
                {
                    IntPtr below = windowsBelowOnMon[i];
                    if (NativeMethods.IsWindow(below) && !NativeMethods.IsIconic(below))
                    {
                        NativeMethods.SetWindowPos(
                            below,
                            insertAfter,
                            0, 0, 0, 0,
                            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                        insertAfter = below;
                    }
                }
            }
            else
            {
                FocusNextVisibleWindow(hWnd, targetMon);
            }

            LogTaskbarDiag(string.Format(
                "MinimizeWindow hWnd=0x{0:X} title='{1}' isIconicAfter={2} nextTop=0x{3:X}('{4}')",
                hWnd.ToInt64(),
                GetWindowTitle(hWnd),
                NativeMethods.IsIconic(hWnd),
                windowsBelowOnMon.Count > 0 ? windowsBelowOnMon[0].ToInt64() : 0L,
                windowsBelowOnMon.Count > 0 ? GetWindowTitle(windowsBelowOnMon[0]) : string.Empty));
        }

        public static void FocusNextVisibleWindow(IntPtr excludeHwnd)
        {
            FocusNextVisibleWindow(excludeHwnd, IntPtr.Zero);
        }

        public static void FocusNextVisibleWindow(IntPtr excludeHwnd, IntPtr preferredMonitor)
        {
            IntPtr nextOnSameMon = IntPtr.Zero;
            IntPtr nextAny = IntPtr.Zero;

            NativeMethods.EnumWindows(delegate(IntPtr w, IntPtr lp)
            {
                if (w == excludeHwnd || NativeMethods.IsIconic(w) || !IsEligibleWindow(w, false))
                    return true;
                if (nextAny == IntPtr.Zero)
                    nextAny = w;
                if (preferredMonitor != IntPtr.Zero && GetWindowMonitor(w) == preferredMonitor)
                {
                    nextOnSameMon = w;
                    return false;
                }
                return preferredMonitor != IntPtr.Zero;
            }, IntPtr.Zero);

            IntPtr nextWin = nextOnSameMon != IntPtr.Zero ? nextOnSameMon : nextAny;
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            if (nextWin != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(nextWin);
            }
            else
            {
                IntPtr shell = NativeMethods.GetShellWindow();
                if (shell != IntPtr.Zero)
                    NativeMethods.SetForegroundWindow(shell);
            }
        }

        public static void ActivateOrToggleMinimize(IntPtr hWnd)
        {
            ActivateOrToggleMinimize(hWnd, IntPtr.Zero);
        }

        public static void ActivateOrToggleMinimize(IntPtr hWnd, IntPtr priorForeground)
        {
            if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd))
                return;

            bool isMinimized = NativeMethods.IsIconic(hWnd);
            bool isTopOnMon = !isMinimized && IsTopVisibleWindowOnMonitor(hWnd);

            if (!isMinimized && isTopOnMon)
            {
                LogTaskbarDiag(string.Format("ActivateOrToggleMinimize -> MINIMIZE hWnd=0x{0:X} title='{1}'", hWnd.ToInt64(), GetWindowTitle(hWnd)));
                MinimizeWindow(hWnd);
                return;
            }

            BringWindowToFront(hWnd);
        }

        public static void OpenNewInstance(IntPtr hWnd)
        {
            string exe = GetProcessExecutablePath(hWnd);
            if (string.IsNullOrEmpty(exe)) return;

            string fileName = Path.GetFileName(exe);
            if (string.Equals(fileName, "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            }
            catch
            {
                // Ignore launch failures
            }
        }

        public static void CloseWindow(IntPtr hWnd)
        {
            if (hWnd != IntPtr.Zero && NativeMethods.IsWindow(hWnd))
                NativeMethods.PostMessage(hWnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
