using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace MonitorFader
{
    public class MonitorInfoItem
    {
        public IntPtr HMonitor { get; set; }
        public string DeviceName { get; set; }
        public int DisplayNumber { get; set; }
        public Rectangle Bounds { get; set; }
        public Rectangle WorkArea { get; set; }
        public bool IsPrimary { get; set; }
    }

    public static class MonitorManager
    {
        private static readonly uint CurrentProcessId = (uint)Process.GetCurrentProcess().Id;

        private static readonly HashSet<string> IgnoredShellClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Progman",
            "WorkerW",
            "Shell_TrayWnd",
            "Shell_SecondaryTrayWnd",
            "NotifyIconOverflowWindow",
            "Windows.UI.Core.CoreWindow"
        };

        public static List<MonitorInfoItem> GetMonitors()
        {
            var list = new List<MonitorInfoItem>();
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                delegate(IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.RECT lprcMonitor, IntPtr dwData)
                {
                    var mi = new NativeMethods.MONITORINFOEX();
                    mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFOEX));
                    if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                    {
                        var bounds = new Rectangle(
                            mi.rcMonitor.Left,
                            mi.rcMonitor.Top,
                            mi.rcMonitor.Width,
                            mi.rcMonitor.Height);
                        var work = new Rectangle(
                            mi.rcWork.Left,
                            mi.rcWork.Top,
                            mi.rcWork.Width,
                            mi.rcWork.Height);

                        list.Add(new MonitorInfoItem
                        {
                            HMonitor = hMonitor,
                            DeviceName = mi.szDevice ?? ("DISPLAY_" + (list.Count + 1)),
                            DisplayNumber = ParseDisplayNumber(mi.szDevice, list.Count + 1),
                            Bounds = bounds,
                            WorkArea = work,
                            IsPrimary = (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0
                        });
                    }
                    return true;
                }, IntPtr.Zero);

            list.Sort((a, b) => a.DisplayNumber.CompareTo(b.DisplayNumber));
            return list;
        }

        private static int ParseDisplayNumber(string deviceName, int fallback)
        {
            if (string.IsNullOrEmpty(deviceName))
                return fallback;

            int i = deviceName.Length - 1;
            while (i >= 0 && char.IsDigit(deviceName[i]))
                i--;

            if (i < deviceName.Length - 1)
            {
                int num;
                if (int.TryParse(deviceName.Substring(i + 1), out num) && num > 0)
                    return num;
            }
            return fallback;
        }

        public static IntPtr GetCurrentMonitorHandle(FadeMode mode)
        {
            if (mode == FadeMode.FadeNonCurrentMouseCursor)
            {
                NativeMethods.POINT pt;
                if (NativeMethods.GetCursorPos(out pt))
                    return NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
            }

            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                IntPtr mon = NativeMethods.MonitorFromWindow(fg, NativeMethods.MONITOR_DEFAULTTONEAREST);
                if (mon != IntPtr.Zero)
                    return mon;
            }

            NativeMethods.POINT fallbackPt;
            if (NativeMethods.GetCursorPos(out fallbackPt))
                return NativeMethods.MonitorFromPoint(fallbackPt, NativeMethods.MONITOR_DEFAULTTONEAREST);

            return IntPtr.Zero;
        }

        public static HashSet<IntPtr> GetMonitorsWithFullscreenWindow(List<MonitorInfoItem> monitors)
        {
            var result = new HashSet<IntPtr>();
            if (monitors == null || monitors.Count == 0)
                return result;

            var classBuf = new StringBuilder(128);

            NativeMethods.EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (!NativeMethods.IsWindowVisible(hWnd) || NativeMethods.IsIconic(hWnd))
                    return true;

                uint pid;
                NativeMethods.GetWindowThreadProcessId(hWnd, out pid);
                if (pid == CurrentProcessId)
                    return true;

                int cloaked = 0;
                if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return true;

                // Skip minimized or maximized windows (maximized windows on monitors without a taskbar
                // fill the entire monitor bounds, which is not the same as fullscreen)
                if (NativeMethods.IsZoomed(hWnd))
                    return true;

                int exStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);
                if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0 || (exStyle & NativeMethods.WS_EX_NOACTIVATE) != 0)
                    return true;

                int style = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_STYLE);
                if ((style & NativeMethods.WS_CAPTION) == NativeMethods.WS_CAPTION
                    && (style & NativeMethods.WS_THICKFRAME) != 0)
                    return true;

                classBuf.Clear();
                int len = NativeMethods.GetClassName(hWnd, classBuf, classBuf.Capacity);
                if (len > 0)
                {
                    string cls = classBuf.ToString();
                    if (IgnoredShellClasses.Contains(cls))
                        return true;
                }

                NativeMethods.RECT rect;
                int hr = NativeMethods.DwmGetWindowAttribute(
                    hWnd,
                    NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                    out rect,
                    Marshal.SizeOf(typeof(NativeMethods.RECT)));
                if (hr != 0)
                {
                    if (!NativeMethods.GetWindowRect(hWnd, out rect))
                        return true;
                }

                if (rect.Width <= 0 || rect.Height <= 0)
                    return true;

                foreach (var mon in monitors)
                {
                    if (result.Contains(mon.HMonitor))
                        continue;

                    // True fullscreen covers the entire monitor area
                    bool covers = rect.Left <= mon.Bounds.Left + 1
                               && rect.Top <= mon.Bounds.Top + 1
                               && rect.Right >= mon.Bounds.Right - 1
                               && rect.Bottom >= mon.Bounds.Bottom - 1;

                    if (covers)
                    {
                        IntPtr ownerMon = NativeMethods.MonitorFromWindow(hWnd, NativeMethods.MONITOR_DEFAULTTONULL);
                        if (ownerMon == mon.HMonitor || (rect.Width <= mon.Bounds.Width + 16 && rect.Height <= mon.Bounds.Height + 16))
                        {
                            result.Add(mon.HMonitor);
                        }
                    }
                }

                return result.Count < monitors.Count;
            }, IntPtr.Zero);

            return result;
        }
    }
}
