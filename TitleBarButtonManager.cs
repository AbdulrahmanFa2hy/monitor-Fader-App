using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MonitorFader
{
    internal sealed class TitleBarButtonForm : Form
    {
        private bool _hovered;
        private bool _pressed;
        private bool _darkTitleBar;
        private Rectangle _currentRect;
        private readonly ToolTip _toolTip = new ToolTip();

        public IntPtr TargetHwnd { get; set; }
        public event Action<IntPtr, bool> Clicked;

        public TitleBarButtonForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Cursor = Cursors.Hand;
            _toolTip.SetToolTip(this, "Move Window to Next Monitor (Middle/Right-click for Previous)");
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED
                            | NativeMethods.WS_EX_TOOLWINDOW
                            | NativeMethods.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WindowHelper.RegisterInternalHandle(Handle);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_MOUSEACTIVATE)
            {
                int hitMsg = (int)((m.LParam.ToInt64() >> 16) & 0xFFFF);
                if (hitMsg == NativeMethods.WM_LBUTTONDOWN)
                {
                    m.Result = (IntPtr)NativeMethods.MA_ACTIVATE;
                    return;
                }
                m.Result = (IntPtr)NativeMethods.MA_NOACTIVATE;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (IsHandleCreated)
                WindowHelper.UnregisterInternalHandle(Handle);
            if (disposing)
                _toolTip.Dispose();
            base.Dispose(disposing);
        }

        public void ShowAt(Rectangle rect, bool darkTitleBar)
        {
            if (!IsHandleCreated)
            {
                var unused = Handle;
            }

            bool needRedraw = (_currentRect.Size != rect.Size) || (_darkTitleBar != darkTitleBar) || !Visible;
            bool moved = (_currentRect.Location != rect.Location);
            _currentRect = rect;
            _darkTitleBar = darkTitleBar;

            if (!Visible)
            {
                Show();
                NativeMethods.SetWindowPos(
                    Handle,
                    NativeMethods.HWND_TOPMOST,
                    rect.X, rect.Y, rect.Width, rect.Height,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
                needRedraw = true;
            }
            else if (moved || needRedraw)
            {
                NativeMethods.SetWindowPos(
                    Handle,
                    NativeMethods.HWND_TOPMOST,
                    rect.X, rect.Y, rect.Width, rect.Height,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            }

            if (needRedraw)
                RenderLayered();
        }

        public void HideButton()
        {
            TargetHwnd = IntPtr.Zero;
            _hovered = false;
            _pressed = false;
            if (IsHandleCreated && !IsDisposed)
            {
                if (NativeMethods.IsWindowVisible(Handle))
                {
                    NativeMethods.SetWindowPos(
                        Handle,
                        NativeMethods.HWND_TOPMOST,
                        0, 0, 0, 0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_HIDEWINDOW);
                }
                if (Visible)
                    Hide();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            RenderLayered();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            _pressed = false;
            RenderLayered();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right)
            {
                _pressed = true;
                RenderLayered();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool wasPressed = _pressed;
            _pressed = false;
            RenderLayered();

            if (wasPressed && ClientRectangle.Contains(e.Location))
            {
                IntPtr target = TargetHwnd;
                bool reverse = (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right)
                            || (ModifierKeys & Keys.Shift) == Keys.Shift;
                Capture = false;
                NativeMethods.ReleaseCapture();
                var h = Clicked;
                if (h != null && target != IntPtr.Zero) h(target, reverse);
            }
        }

        private void RenderLayered()
        {
            if (_currentRect.Width <= 0 || _currentRect.Height <= 0 || !IsHandleCreated)
                return;

            using (var bmp = new Bitmap(_currentRect.Width, _currentRect.Height, PixelFormat.Format32bppPArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                // Alpha=1 keeps the transparent hit target active without showing any rectangle box
                g.Clear(Color.FromArgb(1, 0, 0, 0));

                if (_pressed || _hovered)
                {
                    Color fill = _darkTitleBar
                        ? (_pressed ? Color.FromArgb(46, 255, 255, 255) : Color.FromArgb(26, 255, 255, 255))
                        : (_pressed ? Color.FromArgb(36, 0, 0, 0) : Color.FromArgb(20, 0, 0, 0));

                    float pad = Math.Max(1f, _currentRect.Height * 0.10f);
                    var hoverRect = new RectangleF(pad, pad, _currentRect.Width - pad * 2f, _currentRect.Height - pad * 2f);
                    using (var path = WindowHelper.CreateRoundedRectPath(hoverRect, Math.Max(3f, _currentRect.Height * 0.14f)))
                    using (var b = new SolidBrush(fill))
                    {
                        g.FillPath(b, path);
                    }
                }

                // Draw DisplayFusion-inspired dual-monitor move glyph
                float scale = Math.Max(1f, _currentRect.Height / 28f);
                float glyphW = 16f * scale;
                float glyphH = 12f * scale;
                float gx = (_currentRect.Width - glyphW) / 2f;
                float gy = (_currentRect.Height - glyphH) / 2f;

                Color strokeColor = _darkTitleBar
                    ? Color.FromArgb(235, 240, 244, 248)
                    : Color.FromArgb(235, 42, 46, 52);
                Color dimColor = _darkTitleBar
                    ? Color.FromArgb(150, 175, 185, 198)
                    : Color.FromArgb(150, 110, 118, 128);
                Color accentColor = _darkTitleBar
                    ? Color.FromArgb(255, 96, 178, 255)
                    : Color.FromArgb(255, 0, 103, 192);

                float stroke = Math.Max(1f, 1.25f * scale);

                // Secondary monitor (back-left)
                RectangleF backMon = new RectangleF(gx, gy + 2f * scale, 8.5f * scale, 8f * scale);
                // Primary/target monitor (front-right)
                RectangleF frontMon = new RectangleF(gx + 6.5f * scale, gy, 9.5f * scale, 9.5f * scale);

                using (var dimPen = new Pen(dimColor, stroke))
                using (var mainPen = new Pen(strokeColor, stroke))
                using (var backFill = new SolidBrush(_darkTitleBar ? Color.FromArgb(95, 50, 56, 66) : Color.FromArgb(95, 220, 226, 234)))
                using (var frontFill = new SolidBrush(_darkTitleBar ? Color.FromArgb(225, 34, 38, 46) : Color.FromArgb(235, 252, 253, 255)))
                using (var accentBrush = new SolidBrush(accentColor))
                {
                    g.FillRectangle(backFill, backMon);
                    g.DrawRectangle(dimPen, backMon.X, backMon.Y, backMon.Width, backMon.Height);

                    // Stand for left monitor
                    g.DrawLine(dimPen,
                        backMon.X + backMon.Width * 0.3f, backMon.Bottom + 1.5f * scale,
                        backMon.X + backMon.Width * 0.7f, backMon.Bottom + 1.5f * scale);

                    g.FillRectangle(frontFill, frontMon);
                    g.DrawRectangle(mainPen, frontMon.X, frontMon.Y, frontMon.Width, frontMon.Height);

                    // Stand for right monitor
                    g.DrawLine(mainPen,
                        frontMon.X + frontMon.Width * 0.3f, frontMon.Bottom + 1.5f * scale,
                        frontMon.X + frontMon.Width * 0.7f, frontMon.Bottom + 1.5f * scale);

                    // Inner mini window / arrow block inside target monitor
                    float innerW = 4.5f * scale;
                    float innerH = 3.8f * scale;
                    g.FillRectangle(accentBrush,
                        frontMon.X + (frontMon.Width - innerW) / 2f,
                        frontMon.Y + (frontMon.Height - innerH) / 2f,
                        innerW, innerH);
                }

                WindowHelper.UpdateLayeredBitmap(this, bmp, _currentRect.X, _currentRect.Y);
            }
        }
    }

    public sealed class TitleBarButtonManager : IDisposable
    {
        private const int MaxTrackedButtons = 8;

        private readonly AppSettings _settings;
        private readonly Func<IntPtr, bool> _isMonitorFaded;
        private readonly Dictionary<IntPtr, TitleBarButtonForm> _activeButtons = new Dictionary<IntPtr, TitleBarButtonForm>();
        private readonly Stack<TitleBarButtonForm> _pool = new Stack<TitleBarButtonForm>();
        private readonly Dictionary<IntPtr, bool> _darkCache = new Dictionary<IntPtr, bool>();
        private readonly Timer _syncTimer;
        private IntPtr _hookForeground = IntPtr.Zero;
        private IntPtr _hookLocation = IntPtr.Zero;
        private NativeMethods.WinEventDelegate _winEventProc;
        private bool _updating;

        public TitleBarButtonManager(AppSettings settings, Func<IntPtr, bool> isMonitorFaded)
        {
            _settings = settings;
            _isMonitorFaded = isMonitorFaded;

            _syncTimer = new Timer();
            _syncTimer.Interval = 60;
            _syncTimer.Tick += (s, e) => UpdateButtons();

            InstallHooks();
            ApplySettings();
        }

        private void InstallHooks()
        {
            _winEventProc = WinEventCallback;
            uint flags = NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS;
            _hookForeground = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _winEventProc,
                0, 0, flags);

            _hookLocation = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero,
                _winEventProc,
                0, 0, flags);
        }

        private void WinEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (!_settings.EnableTitleBarButton)
                return;

            if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND || (idObject == 0 && _activeButtons.ContainsKey(hwnd)))
            {
                if (!_updating)
                {
                    foreach (var btn in _activeButtons.Values)
                    {
                        if (!btn.IsDisposed && btn.IsHandleCreated)
                        {
                            try { btn.BeginInvoke(new Action(UpdateButtons)); }
                            catch { }
                            break;
                        }
                    }
                }
            }
        }

        public void ApplySettings()
        {
            if (_settings.EnableTitleBarButton)
            {
                if (!_syncTimer.Enabled)
                    _syncTimer.Start();
                UpdateButtons();
            }
            else
            {
                if (_syncTimer.Enabled)
                    _syncTimer.Stop();
                HideAllButtons();
            }
        }

        private TitleBarButtonForm AcquireButton()
        {
            while (_pool.Count > 0)
            {
                var f = _pool.Pop();
                if (!f.IsDisposed) return f;
            }

            var created = new TitleBarButtonForm();
            created.Clicked += OnButtonClicked;
            return created;
        }

        private void ReleaseButton(TitleBarButtonForm btn)
        {
            if (btn == null || btn.IsDisposed) return;
            btn.HideButton();
            if (_pool.Count < MaxTrackedButtons)
                _pool.Push(btn);
            else
                btn.Dispose();
        }

        private void OnButtonClicked(IntPtr targetHwnd, bool reverse)
        {
            if (targetHwnd != IntPtr.Zero && NativeMethods.IsWindow(targetHwnd))
            {
                WindowHelper.MoveWindowToMonitor(targetHwnd, reverse);
                _darkCache.Remove(targetHwnd);
                UpdateButtons();
            }
        }

        public void UpdateButtons()
        {
            if (_updating) return;
            _updating = true;
            try
            {
                if (!_settings.EnableTitleBarButton || MonitorManager.GetMonitors().Count < 2)
                {
                    HideAllButtons();
                    return;
                }

                var desired = new Dictionary<IntPtr, Rectangle>();
                var occluders = new List<Rectangle>();
                IntPtr fg = NativeMethods.GetForegroundWindow();

                NativeMethods.EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
                {
                    if (!NativeMethods.IsWindowVisible(hWnd) || NativeMethods.IsIconic(hWnd))
                        return true;

                    if (WindowHelper.IsInternalHandle(hWnd))
                        return true;

                    int cloaked = 0;
                    if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0)
                        return true;

                    NativeMethods.RECT extRect;
                    if (!GetVisibleWindowRect(hWnd, out extRect))
                        return true;

                    Rectangle winRect = new Rectangle(extRect.Left, extRect.Top, extRect.Width, extRect.Height);

                    if (desired.Count < MaxTrackedButtons && IsTitleBarEligible(hWnd))
                    {
                        Rectangle btnRect;
                        if (TryComputeButtonRect(hWnd, extRect, out btnRect))
                        {
                            bool occluded = false;
                            foreach (var occ in occluders)
                            {
                                if (occ.IntersectsWith(btnRect))
                                {
                                    occluded = true;
                                    break;
                                }
                            }
                            if (!occluded)
                                desired[hWnd] = btnRect;
                        }
                    }

                    // Exclude shell desktop hosts from acting as occluders
                    int exStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);
                    if ((exStyle & NativeMethods.WS_EX_TRANSPARENT) == 0)
                        occluders.Add(winRect);

                    return desired.Count < MaxTrackedButtons;
                }, IntPtr.Zero);

                var staleKeys = new List<IntPtr>();
                foreach (var kv in _activeButtons)
                {
                    if (!desired.ContainsKey(kv.Key))
                        staleKeys.Add(kv.Key);
                }
                foreach (var k in staleKeys)
                {
                    ReleaseButton(_activeButtons[k]);
                    _activeButtons.Remove(k);
                    _darkCache.Remove(k);
                }

                foreach (var kv in desired)
                {
                    TitleBarButtonForm btn;
                    bool isNew = false;
                    if (!_activeButtons.TryGetValue(kv.Key, out btn) || btn.IsDisposed)
                    {
                        btn = AcquireButton();
                        _activeButtons[kv.Key] = btn;
                        isNew = true;
                    }

                    btn.TargetHwnd = kv.Key;
                    bool dark;
                    if (isNew || !_darkCache.TryGetValue(kv.Key, out dark))
                    {
                        int sampleX = Math.Max(0, kv.Value.Left - 8);
                        int sampleY = kv.Value.Top + kv.Value.Height / 2;
                        dark = WindowHelper.IsScreenPointDark(sampleX, sampleY);
                        _darkCache[kv.Key] = dark;
                    }

                    btn.ShowAt(kv.Value, dark);
                }
            }
            finally
            {
                _updating = false;
            }
        }

        private static bool IsTitleBarEligible(IntPtr hWnd)
        {
            if (!WindowHelper.IsEligibleWindow(hWnd, false))
                return false;

            int style = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_STYLE);
            if ((style & NativeMethods.WS_CAPTION) != NativeMethods.WS_CAPTION)
                return false;

            return true;
        }

        private static uint GetDpiSafe(IntPtr hWnd)
        {
            try
            {
                uint dpi = NativeMethods.GetDpiForWindow(hWnd);
                return dpi > 0 ? dpi : 96;
            }
            catch
            {
                return 96;
            }
        }

        private static bool GetVisibleWindowRect(IntPtr hWnd, out NativeMethods.RECT rect)
        {
            int hr = NativeMethods.DwmGetWindowAttribute(
                hWnd,
                NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                out rect,
                Marshal.SizeOf(typeof(NativeMethods.RECT)));
            if (hr == 0 && rect.Width > 0 && rect.Height > 0)
                return true;

            return NativeMethods.GetWindowRect(hWnd, out rect) && rect.Width > 0 && rect.Height > 0;
        }

        private static bool TryComputeButtonRect(IntPtr hWnd, NativeMethods.RECT ext, out Rectangle btnRect)
        {
            btnRect = Rectangle.Empty;

            uint dpi = GetDpiSafe(hWnd);
            double scale = dpi / 96.0;
            int btnW = (int)Math.Round(38 * scale);
            int btnH = (int)Math.Round(28 * scale);
            int gap = (int)Math.Round(2 * scale);

            if (ext.Width < (int)Math.Round(240 * scale) || ext.Height < (int)Math.Round(60 * scale))
                return false;

            int clusterLeft = 0;
            int btnTop = ext.Top + (int)Math.Round(2 * scale);
            bool foundCluster = false;

            try
            {
                var ex = new NativeMethods.TITLEBARINFOEX();
                ex.cbSize = Marshal.SizeOf(typeof(NativeMethods.TITLEBARINFOEX));
                IntPtr res;
                if (NativeMethods.SendMessageTimeout(hWnd, NativeMethods.WM_GETTITLEBARINFOEX, IntPtr.Zero, ref ex, 2, 25, out res) != IntPtr.Zero)
                {
                    for (int i = 2; i <= 5; i++)
                    {
                        if (i == 4) continue;
                        NativeMethods.RECT r = ex.rgrect[i];
                        if (r.Width > 4 && r.Width < (int)(120 * scale)
                            && r.Height > 4 && r.Height < (int)(80 * scale)
                            && r.Left >= ext.Left && r.Right <= ext.Right + 4
                            && r.Top >= ext.Top - 4 && r.Top <= ext.Top + (int)(60 * scale))
                        {
                            clusterLeft = r.Left;
                            btnH = Math.Min((int)Math.Round(30 * scale), Math.Max((int)Math.Round(20 * scale), r.Height));
                            btnTop = r.Top + (r.Height - btnH) / 2;
                            foundCluster = true;
                            break;
                        }
                    }
                }
            }
            catch
            {
            }

            if (!foundCluster)
            {
                int style = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_STYLE);
                int buttonCount = 1;
                if ((style & NativeMethods.WS_MAXIMIZEBOX) != 0) buttonCount++;
                if ((style & NativeMethods.WS_MINIMIZEBOX) != 0) buttonCount++;
                if (buttonCount == 1) buttonCount = 3;

                int clusterWidth = (int)Math.Round(buttonCount * 46 * scale);
                clusterLeft = ext.Right - clusterWidth;
                btnTop = ext.Top + (int)Math.Round(2 * scale);
            }

            int btnLeft = clusterLeft - btnW - gap;
            if (btnLeft <= ext.Left + (int)Math.Round(40 * scale))
                return false;

            btnRect = new Rectangle(btnLeft, btnTop, btnW, btnH);
            return true;
        }

        private void HideAllButtons()
        {
            foreach (var btn in _activeButtons.Values)
            {
                ReleaseButton(btn);
            }
            _activeButtons.Clear();
            _darkCache.Clear();
        }

        public void Dispose()
        {
            _syncTimer.Stop();
            _syncTimer.Dispose();
            if (_hookForeground != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_hookForeground);
                _hookForeground = IntPtr.Zero;
            }
            if (_hookLocation != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_hookLocation);
                _hookLocation = IntPtr.Zero;
            }
            foreach (var btn in _activeButtons.Values)
            {
                if (btn != null && !btn.IsDisposed) btn.Dispose();
            }
            _activeButtons.Clear();
            while (_pool.Count > 0)
            {
                var btn = _pool.Pop();
                if (btn != null && !btn.IsDisposed) btn.Dispose();
            }
        }
    }
}
