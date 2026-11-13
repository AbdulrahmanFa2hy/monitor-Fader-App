using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MonitorFader
{
    internal sealed class TaskbarPreviewForm : Form
    {
        private sealed class PreviewCard
        {
            public WindowEntry Window;
            public Rectangle CardRect;
            public Rectangle CloseRect;
            public Rectangle ThumbRect;
            public IntPtr ThumbId;
            public Bitmap SmallIcon;
            public Bitmap FallbackIcon;
        }

        private readonly List<PreviewCard> _cards = new List<PreviewCard>();
        private readonly Timer _watchTimer;
        private int _hoveredCardIndex = -1;
        private int _hoveredCloseIndex = -1;
        private bool _lightTheme;
        private float _scale = 1f;
        private bool _isShown;
        private bool _isPinnedByClick;
        private bool _mouseEnteredPreview;
        private int _outsideSafeZoneTicks;
        private Rectangle _anchorButtonRect;

        public string ActiveGroupKey { get; private set; }
        public bool IsPinnedByClick { get { return _isPinnedByClick; } }
        public bool IsPreviewVisible { get { return _isShown && IsHandleCreated && !IsDisposed && NativeMethods.IsWindowVisible(Handle); } }
        public event Action<IntPtr> CloseRequested;
        public event Action<IntPtr> ActivateRequested;

        public TaskbarPreviewForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(36, 38, 44);

            _watchTimer = new Timer { Interval = 90 };
            _watchTimer.Tick += OnWatchTimerTick;
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
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW
                            | NativeMethods.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WindowHelper.RegisterInternalHandle(Handle);
            WindowHelper.EnableRoundedCorners(Handle);
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
            if (disposing)
            {
                _watchTimer.Stop();
                _watchTimer.Dispose();
            }
            ClearCards();
            if (IsHandleCreated)
                WindowHelper.UnregisterInternalHandle(Handle);
            base.Dispose(disposing);
        }

        private void ClearCards()
        {
            foreach (var c in _cards)
            {
                if (c.ThumbId != IntPtr.Zero)
                {
                    try { NativeMethods.DwmUnregisterThumbnail(c.ThumbId); } catch { }
                    c.ThumbId = IntPtr.Zero;
                }
                if (c.SmallIcon != null)
                {
                    c.SmallIcon.Dispose();
                    c.SmallIcon = null;
                }
                if (c.FallbackIcon != null)
                {
                    c.FallbackIcon.Dispose();
                    c.FallbackIcon = null;
                }
            }
            _cards.Clear();
            _hoveredCardIndex = -1;
            _hoveredCloseIndex = -1;
        }

        public void ShowGroupPreview(
            string groupKey,
            List<WindowEntry> windows,
            Rectangle buttonScreenRect,
            Rectangle monitorBounds,
            float scale,
            bool lightTheme,
            bool pinnedByClick = false)
        {
            if (windows == null || windows.Count == 0)
            {
                HidePreview();
                return;
            }

            if (!IsHandleCreated)
            {
                var unused = Handle;
            }

            ClearCards();
            ActiveGroupKey = groupKey;
            _scale = scale;
            _lightTheme = lightTheme;
            _isPinnedByClick = pinnedByClick;
            _mouseEnteredPreview = false;
            _outsideSafeZoneTicks = 0;
            _anchorButtonRect = buttonScreenRect;
            BackColor = lightTheme ? Color.FromArgb(246, 248, 250) : Color.FromArgb(34, 36, 42);

            int maxVisible = Math.Min(windows.Count, 7);
            int outerPad = (int)Math.Round(8 * scale);
            int cardGap = (int)Math.Round(6 * scale);
            int maxAvailW = Math.Max(200, monitorBounds.Width - 24);
            int baseCardW = (int)Math.Round(216 * scale);
            int cardW = Math.Max((int)Math.Round(130 * scale), Math.Min(baseCardW, (maxAvailW - outerPad * 2 - cardGap * (maxVisible - 1)) / maxVisible));

            int headerH = (int)Math.Round(28 * scale);
            int innerPad = (int)Math.Round(8 * scale);
            int thumbMaxW = cardW - innerPad * 2;
            int thumbMaxH = (int)Math.Round(122 * scale);
            int cardH = headerH + thumbMaxH + innerPad * 2;
            int totalW = outerPad * 2 + cardW * maxVisible + cardGap * (maxVisible - 1);
            int totalH = outerPad * 2 + cardH;

            int x = buttonScreenRect.Left + (buttonScreenRect.Width - totalW) / 2;
            x = Math.Max(monitorBounds.Left + 8, Math.Min(monitorBounds.Right - totalW - 8, x));
            int y = buttonScreenRect.Top - totalH - (int)Math.Round(10 * scale);
            y = Math.Max(monitorBounds.Top + 8, y);

            // Ensure WinForms Visible=true state without activation, then enforce topmost physical bounds
            Bounds = new Rectangle(x, y, totalW, totalH);
            if (!Visible)
                Show();
            NativeMethods.SetWindowPos(
                Handle,
                NativeMethods.HWND_TOPMOST,
                x, y, totalW, totalH,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            _isShown = true;

            int closeSz = (int)Math.Round(20 * scale);
            int smallIconSz = Math.Max(14, (int)Math.Round(14 * scale));

            for (int i = 0; i < maxVisible; i++)
            {
                WindowEntry win = windows[i];
                if (!NativeMethods.IsWindow(win.Handle)) continue;

                int cx = outerPad + i * (cardW + cardGap);
                int cy = outerPad;
                var cardRect = new Rectangle(cx, cy, cardW, cardH);
                var closeRect = new Rectangle(cx + cardW - closeSz - (int)Math.Round(4 * scale), cy + (headerH - closeSz) / 2, closeSz, closeSz);

                IntPtr thumbId = IntPtr.Zero;
                int regHr = NativeMethods.DwmRegisterThumbnail(Handle, win.Handle, out thumbId);

                int destW = thumbMaxW;
                int destH = thumbMaxH;
                NativeMethods.SIZE srcSize = new NativeMethods.SIZE { cx = 0, cy = 0 };
                bool hasSrcSize = false;
                if (regHr == 0 && thumbId != IntPtr.Zero)
                {
                    if (NativeMethods.DwmQueryThumbnailSourceSize(thumbId, out srcSize) == 0 && srcSize.cx > 0 && srcSize.cy > 0)
                    {
                        hasSrcSize = true;
                        double ratio = Math.Min((double)thumbMaxW / srcSize.cx, (double)thumbMaxH / srcSize.cy);
                        destW = Math.Max(40, (int)Math.Round(srcSize.cx * ratio));
                        destH = Math.Max(28, (int)Math.Round(srcSize.cy * ratio));
                    }
                }

                int thumbX = cx + (cardW - destW) / 2;
                int thumbY = cy + headerH + (thumbMaxH - destH) / 2 + innerPad / 2;
                var thumbRect = new Rectangle(thumbX, thumbY, destW, destH);

                bool thumbActive = false;
                if (regHr == 0 && thumbId != IntPtr.Zero)
                {
                    uint flags = NativeMethods.DWM_TNP_RECTDESTINATION
                               | NativeMethods.DWM_TNP_VISIBLE
                               | NativeMethods.DWM_TNP_OPACITY
                               | NativeMethods.DWM_TNP_SOURCECLIENTAREAONLY;
                    var props = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
                    {
                        dwFlags = flags,
                        rcDestination = new NativeMethods.RECT
                        {
                            Left = thumbRect.Left,
                            Top = thumbRect.Top,
                            Right = thumbRect.Right,
                            Bottom = thumbRect.Bottom
                        },
                        opacity = 255,
                        fVisible = true,
                        fSourceClientAreaOnly = false
                    };
                    if (hasSrcSize)
                    {
                        props.dwFlags |= NativeMethods.DWM_TNP_RECTSOURCE;
                        props.rcSource = new NativeMethods.RECT
                        {
                            Left = 0,
                            Top = 0,
                            Right = srcSize.cx,
                            Bottom = srcSize.cy
                        };
                    }
                    thumbActive = (NativeMethods.DwmUpdateThumbnailProperties(thumbId, ref props) == 0);
                }

                Bitmap fallbackIcon = null;
                if (!thumbActive)
                {
                    int bigSz = Math.Max(32, (int)Math.Round(40 * scale));
                    fallbackIcon = WindowHelper.GetAppIconBitmap(win.Handle, win.RealExePath, bigSz);
                }

                _cards.Add(new PreviewCard
                {
                    Window = win,
                    CardRect = cardRect,
                    CloseRect = closeRect,
                    ThumbRect = thumbRect,
                    ThumbId = thumbId,
                    SmallIcon = WindowHelper.GetWindowIconBitmap(win.Handle, smallIconSz),
                    FallbackIcon = fallbackIcon
                });
            }

            Invalidate();
            Update();
            _watchTimer.Start();
        }

        public bool IsCursorInSafeZone(Point cur)
        {
            if (!_isShown || IsDisposed || !IsHandleCreated)
                return false;

            Rectangle previewRect = Bounds;
            Rectangle inflatedPreview = Rectangle.Inflate(previewRect, 14, 14);
            Rectangle inflatedAnchor = Rectangle.Inflate(_anchorButtonRect, 10, 10);

            int bridgeLeft = Math.Min(previewRect.Left, _anchorButtonRect.Left) - 14;
            int bridgeRight = Math.Max(previewRect.Right, _anchorButtonRect.Right) + 14;
            int bridgeTop = previewRect.Top - 8;
            int bridgeBottom = _anchorButtonRect.Bottom + 10;
            var bridgeRect = new Rectangle(
                bridgeLeft,
                bridgeTop,
                Math.Max(1, bridgeRight - bridgeLeft),
                Math.Max(1, bridgeBottom - bridgeTop));

            return inflatedPreview.Contains(cur) || inflatedAnchor.Contains(cur) || bridgeRect.Contains(cur);
        }

        private void OnWatchTimerTick(object sender, EventArgs e)
        {
            if (!_isShown || IsDisposed || !IsHandleCreated)
            {
                _watchTimer.Stop();
                return;
            }

            Point cur = Cursor.Position;
            Rectangle previewRect = Bounds;
            if (previewRect.Contains(cur))
                _mouseEnteredPreview = true;

            bool inSafeZone = IsCursorInSafeZone(cur);
            if (inSafeZone)
                _outsideSafeZoneTicks = 0;
            else
                _outsideSafeZoneTicks++;

            if (_isPinnedByClick)
            {
                bool anyMouseBtn = (Control.MouseButtons & (MouseButtons.Left | MouseButtons.Right)) != 0;
                if (anyMouseBtn && !previewRect.Contains(cur) && !_anchorButtonRect.Contains(cur))
                {
                    HidePreview();
                    return;
                }
                if (_mouseEnteredPreview && !inSafeZone && _outsideSafeZoneTicks >= 3)
                {
                    HidePreview();
                    return;
                }
            }
            else
            {
                if (!inSafeZone && _outsideSafeZoneTicks >= 3)
                {
                    HidePreview();
                    return;
                }
            }
        }

        public void HidePreview()
        {
            _watchTimer.Stop();
            _isShown = false;
            _isPinnedByClick = false;
            _mouseEnteredPreview = false;
            _outsideSafeZoneTicks = 0;
            ClearCards();
            ActiveGroupKey = null;
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

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hitCard = -1;
            int hitClose = -1;
            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i].CardRect.Contains(e.Location))
                {
                    hitCard = i;
                    if (_cards[i].CloseRect.Contains(e.Location))
                        hitClose = i;
                    break;
                }
            }

            if (hitCard != _hoveredCardIndex || hitClose != _hoveredCloseIndex)
            {
                _hoveredCardIndex = hitCard;
                _hoveredCloseIndex = hitClose;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoveredCardIndex != -1 || _hoveredCloseIndex != -1)
            {
                _hoveredCardIndex = -1;
                _hoveredCloseIndex = -1;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;

            for (int i = 0; i < _cards.Count; i++)
            {
                PreviewCard card = _cards[i];
                if (card == null || card.Window == null) continue;

                if (card.CloseRect.Contains(e.Location))
                {
                    IntPtr hwnd = card.Window.Handle;
                    string title = card.Window.Title ?? string.Empty;
                    Capture = false;
                    NativeMethods.ReleaseCapture();
                    HidePreview();
                    WindowHelper.LogTaskbarDiag(string.Format("PreviewCardClick -> Close hwnd=0x{0:X} title='{1}'", hwnd.ToInt64(), title));
                    var h = CloseRequested;
                    if (h != null) h(hwnd);
                    return;
                }
                if (card.CardRect.Contains(e.Location))
                {
                    IntPtr hwnd = card.Window.Handle;
                    string title = card.Window.Title ?? string.Empty;
                    Capture = false;
                    NativeMethods.ReleaseCapture();
                    HidePreview();
                    WindowHelper.LogTaskbarDiag(string.Format("PreviewCardClick -> ActivateOrToggle hwnd=0x{0:X} title='{1}'", hwnd.ToInt64(), title));
                    var h = ActivateRequested;
                    if (h != null) h(hwnd);
                    return;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color bg = _lightTheme ? Color.FromArgb(246, 248, 250) : Color.FromArgb(34, 36, 42);
            Color outerBorder = _lightTheme ? Color.FromArgb(208, 213, 220) : Color.FromArgb(64, 68, 78);
            Color textCol = _lightTheme ? Color.FromArgb(28, 32, 38) : Color.FromArgb(238, 240, 244);

            g.Clear(bg);
            using (var border = new Pen(outerBorder, 1f))
            {
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            }

            float fontPx = Math.Max(10f, 11.5f * _scale);
            using (var titleFont = new Font("Segoe UI", fontPx, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(textCol))
            {
                var sf = new StringFormat
                {
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap,
                    LineAlignment = StringAlignment.Center
                };

                for (int i = 0; i < _cards.Count; i++)
                {
                    PreviewCard card = _cards[i];
                    bool isHovered = (i == _hoveredCardIndex);
                    bool isActiveWin = card.Window.IsForeground;

                    Color cardFill = isHovered
                        ? (_lightTheme ? Color.FromArgb(255, 255, 255) : Color.FromArgb(48, 52, 62))
                        : (_lightTheme ? Color.FromArgb(238, 241, 246) : Color.FromArgb(40, 43, 50));
                    Color cardBorder = isHovered || isActiveWin
                        ? (_lightTheme ? Color.FromArgb(0, 103, 192) : Color.FromArgb(96, 178, 255))
                        : (_lightTheme ? Color.FromArgb(215, 220, 226) : Color.FromArgb(56, 60, 70));

                    using (var path = WindowHelper.CreateRoundedRectPath(card.CardRect, 6 * _scale))
                    using (var fill = new SolidBrush(cardFill))
                    using (var pen = new Pen(cardBorder, 1f))
                    {
                        g.FillPath(fill, path);
                        g.DrawPath(pen, path);
                    }

                    int textLeft = card.CardRect.Left + (int)Math.Round(8 * _scale);
                    if (card.SmallIcon != null)
                    {
                        int iy = card.CardRect.Top + (int)Math.Round(7 * _scale);
                        g.DrawImage(card.SmallIcon, textLeft, iy, card.SmallIcon.Width, card.SmallIcon.Height);
                        textLeft += card.SmallIcon.Width + (int)Math.Round(6 * _scale);
                    }

                    int headerH = (int)Math.Round(28 * _scale);
                    var textRect = new RectangleF(textLeft, card.CardRect.Top + 1, Math.Max(20, card.CloseRect.Left - textLeft - 4 * _scale), headerH);
                    g.DrawString(card.Window.Title, titleFont, brush, textRect, sf);

                    if (i == _hoveredCloseIndex)
                    {
                        using (var cPath = WindowHelper.CreateRoundedRectPath(card.CloseRect, 4 * _scale))
                        using (var cb = new SolidBrush(Color.FromArgb(196, 43, 28)))
                        {
                            g.FillPath(cb, cPath);
                        }
                    }

                    int p = Math.Max(4, (int)(card.CloseRect.Width * 0.28f));
                    Color xColor = (i == _hoveredCloseIndex) ? Color.White : textCol;
                    using (var xPen = new Pen(xColor, Math.Max(1.2f, 1.35f * _scale)))
                    {
                        g.DrawLine(xPen, card.CloseRect.Left + p, card.CloseRect.Top + p, card.CloseRect.Right - p, card.CloseRect.Bottom - p);
                        g.DrawLine(xPen, card.CloseRect.Right - p, card.CloseRect.Top + p, card.CloseRect.Left + p, card.CloseRect.Bottom - p);
                    }

                    if (card.FallbackIcon != null)
                    {
                        int fx = card.ThumbRect.Left + (card.ThumbRect.Width - card.FallbackIcon.Width) / 2;
                        int fy = card.ThumbRect.Top + (card.ThumbRect.Height - card.FallbackIcon.Height) / 2;
                        g.DrawImage(card.FallbackIcon, fx, fy, card.FallbackIcon.Width, card.FallbackIcon.Height);
                    }
                }
            }
        }
    }

    internal sealed class SecondaryTaskbarForm : Form
    {
        private sealed class AppGroupSlot
        {
            public string GroupKey;
            public List<WindowEntry> Windows = new List<WindowEntry>();
            public WindowEntry PrimaryWindow;
            public bool IsAnyForeground;
            public bool IsAllMinimized;
            public Rectangle Bounds;
        }

        private readonly AppSettings _settings;
        private readonly Action _openSettingsAction;
        private readonly Action _refreshAction;
        private readonly Dictionary<string, Bitmap> _groupIconCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _groupOrder = new List<string>();
        private readonly Dictionary<string, List<IntPtr>> _groupKnownHandles = new Dictionary<string, List<IntPtr>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _groupMissingTicks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly List<IntPtr> _windowOrder = new List<IntPtr>();
        private readonly List<AppGroupSlot> _slots = new List<AppGroupSlot>();
        private readonly ToolTip _toolTip = new ToolTip();
        private readonly TaskbarPreviewForm _previewForm;
        private readonly Timer _hoverTimer;
        private readonly Timer _dragTimer;

        private MonitorInfoItem _monitor;
        private Rectangle _barRect;
        private Rectangle _startRect;
        private Rectangle _clockRect;
        private Rectangle _showDesktopRect;
        private string _hoveredGroupKey;
        private Rectangle _hoveredBtnRect;
        private bool _startHovered;
        private bool _clockHovered;
        private string _dragTargetGroupKey;
        private bool _appBarRegistered;
        private bool _isShown;
        private bool _lightTheme;
        private string _lastTimeText = string.Empty;
        private string _lastDateText = string.Empty;
        private string _lastTooltipText = string.Empty;
        private string _suppressHoverGroupKey;
        private float _scale = 1f;
        private IntPtr _fgBeforeBarClick;
        private int _fgCapturedTickMs;

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        public IntPtr MonitorHandle { get { return _monitor != null ? _monitor.HMonitor : IntPtr.Zero; } }
        public string DeviceName { get { return _monitor != null ? _monitor.DeviceName : string.Empty; } }

        public SecondaryTaskbarForm(MonitorInfoItem monitor, AppSettings settings, Action openSettingsAction, Action refreshAction)
        {
            _monitor = monitor;
            _settings = settings;
            _openSettingsAction = openSettingsAction;
            _refreshAction = refreshAction;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AllowDrop = true;

            _previewForm = new TaskbarPreviewForm();
            _previewForm.CloseRequested += WindowHelper.CloseWindow;
            _previewForm.ActivateRequested += ActivateWindowFromPreview;

            _hoverTimer = new Timer { Interval = 220 };
            _hoverTimer.Tick += OnHoverTimerTick;

            _dragTimer = new Timer { Interval = 400 };
            _dragTimer.Tick += OnDragTimerTick;

            UpdateMonitor(monitor);
        }

        private static void ActivateWindowFromPreview(IntPtr hWnd)
        {
            WindowHelper.ActivateOrToggleMinimize(hWnd);
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
                IntPtr fg = NativeMethods.GetForegroundWindow();
                if (fg != IntPtr.Zero && !WindowHelper.IsInternalHandle(fg))
                {
                    _fgBeforeBarClick = fg;
                    _fgCapturedTickMs = Environment.TickCount;
                }

                if (hitMsg == NativeMethods.WM_LBUTTONDOWN)
                {
                    Point clientPt = PointToClient(Cursor.Position);
                    var slotHit = HitTestSlot(clientPt);
                    if (slotHit != null)
                    {
                        m.Result = (IntPtr)NativeMethods.MA_ACTIVATE;
                        return;
                    }
                }

                m.Result = (IntPtr)NativeMethods.MA_NOACTIVATE;
                return;
            }
            base.WndProc(ref m);
        }

        public void UpdateMonitor(MonitorInfoItem monitor)
        {
            _monitor = monitor;
            _scale = GetMonitorScale(monitor.HMonitor);
            _lightTheme = WindowHelper.IsSystemLightTheme();

            if (!IsHandleCreated)
            {
                var unused = Handle;
            }

            RegisterOrUpdateAppBar();
            RenderBar();
        }

        private static float GetMonitorScale(IntPtr hMonitor)
        {
            try
            {
                uint dx, dy;
                if (GetDpiForMonitor(hMonitor, 0 /* MDT_EFFECTIVE_DPI */, out dx, out dy) == 0 && dx > 0)
                    return dx / 96f;
            }
            catch
            {
            }
            return 1f;
        }

        private Rectangle GetDesiredBarBounds()
        {
            int barHeight = Math.Max(36, (int)Math.Round(48 * _scale));
            Rectangle mb = _monitor.Bounds;
            return new Rectangle(mb.Left, mb.Bottom - barHeight, mb.Width, barHeight);
        }

        private void RegisterOrUpdateAppBar()
        {
            if (!IsHandleCreated) return;

            if (!_appBarRegistered)
            {
                var abdNew = new NativeMethods.APPBARDATA
                {
                    cbSize = Marshal.SizeOf(typeof(NativeMethods.APPBARDATA)),
                    hWnd = Handle,
                    uCallbackMessage = 0
                };
                NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref abdNew);
                _appBarRegistered = true;
            }

            Rectangle desired = GetDesiredBarBounds();
            var abd = new NativeMethods.APPBARDATA
            {
                cbSize = Marshal.SizeOf(typeof(NativeMethods.APPBARDATA)),
                hWnd = Handle,
                uEdge = NativeMethods.ABE_BOTTOM,
                rc = new NativeMethods.RECT
                {
                    Left = desired.Left,
                    Top = desired.Top,
                    Right = desired.Right,
                    Bottom = desired.Bottom
                }
            };

            NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref abd);
            int barHeight = desired.Height;
            abd.rc.Top = abd.rc.Bottom - barHeight;
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref abd);

            _barRect = new Rectangle(
                abd.rc.Left,
                abd.rc.Top,
                Math.Max(120, abd.rc.Right - abd.rc.Left),
                Math.Max(32, abd.rc.Bottom - abd.rc.Top));

            Bounds = _barRect;
            if (!Visible)
                Show();

            NativeMethods.SetWindowPos(
                Handle,
                NativeMethods.HWND_TOPMOST,
                _barRect.X, _barRect.Y, _barRect.Width, _barRect.Height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            _isShown = true;
        }

        public void UnregisterAppBar()
        {
            if (_appBarRegistered && IsHandleCreated)
            {
                var abd = new NativeMethods.APPBARDATA
                {
                    cbSize = Marshal.SizeOf(typeof(NativeMethods.APPBARDATA)),
                    hWnd = Handle
                };
                NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref abd);
                _appBarRegistered = false;
            }
        }

        public void SetBarVisibility(bool visible)
        {
            if (!IsHandleCreated) return;
            if (visible == _isShown) return;

            NativeMethods.SetWindowPos(
                Handle,
                NativeMethods.HWND_TOPMOST,
                _barRect.X, _barRect.Y, _barRect.Width, _barRect.Height,
                NativeMethods.SWP_NOACTIVATE | (visible ? NativeMethods.SWP_SHOWWINDOW : NativeMethods.SWP_HIDEWINDOW));
            _isShown = visible;
            if (visible)
                RenderBar();
            else
                _previewForm.HidePreview();
        }

        public void UpdateWindows(List<WindowEntry> allWindows, bool hasFullscreenOnThisMonitor, bool fullscreenFocusedOnThisMonitor)
        {
            _lightTheme = WindowHelper.IsSystemLightTheme();

            bool shouldBeVisible = true;
            if (hasFullscreenOnThisMonitor)
            {
                if (fullscreenFocusedOnThisMonitor)
                    shouldBeVisible = false;
                else
                    shouldBeVisible = _settings.TaskbarShowOverFullscreenOnFocusLoss;
            }
            SetBarVisibility(shouldBeVisible);
            if (!shouldBeVisible) return;

            // Preserve deterministic first-seen window ordering inside groups (never Z-order)
            _windowOrder.RemoveAll(h => !NativeMethods.IsWindow(h));
            var windowRank = new Dictionary<IntPtr, int>();
            for (int i = 0; i < _windowOrder.Count; i++)
                windowRank[_windowOrder[i]] = i;

            foreach (var w in allWindows)
            {
                if (!windowRank.ContainsKey(w.Handle))
                {
                    windowRank[w.Handle] = _windowOrder.Count;
                    _windowOrder.Add(w.Handle);
                }
            }

            bool relevantOnly = _settings.TaskbarMode == (int)TaskbarDisplayMode.RelevantWindows;
            var grouped = new Dictionary<string, AppGroupSlot>(StringComparer.OrdinalIgnoreCase);

            foreach (var w in allWindows)
            {
                if (relevantOnly && w.MonitorHandle != _monitor.HMonitor)
                    continue;

                string key = string.IsNullOrEmpty(w.GroupKey) ? ("hwnd:" + w.Handle.ToInt64()) : w.GroupKey;
                AppGroupSlot slot;
                if (!grouped.TryGetValue(key, out slot))
                {
                    slot = new AppGroupSlot
                    {
                        GroupKey = key,
                        PrimaryWindow = w,
                        IsAllMinimized = true
                    };
                    grouped[key] = slot;
                }

                slot.Windows.Add(w);
                if (w.IsForeground)
                    slot.IsAnyForeground = true;
                if (!w.IsMinimized)
                    slot.IsAllMinimized = false;
            }

            foreach (var slot in grouped.Values)
            {
                slot.Windows.Sort((a, b) =>
                {
                    int ra, rb;
                    if (!windowRank.TryGetValue(a.Handle, out ra)) ra = int.MaxValue;
                    if (!windowRank.TryGetValue(b.Handle, out rb)) rb = int.MaxValue;
                    return ra.CompareTo(rb);
                });

                WindowEntry fgWin = slot.Windows.Find(w => w.IsForeground);
                slot.PrimaryWindow = fgWin ?? slot.Windows[0];

                List<IntPtr> known;
                if (!_groupKnownHandles.TryGetValue(slot.GroupKey, out known))
                {
                    known = new List<IntPtr>();
                    _groupKnownHandles[slot.GroupKey] = known;
                }
                known.Clear();
                foreach (var w in slot.Windows)
                    known.Add(w.Handle);
            }

            // Maintain strictly fixed icon ordering like the native Windows taskbar.
            // Keep a short grace period if a group's HWND is still alive during transient focus/cloak/minimize states.
            var remainingOrder = new List<string>(_groupOrder.Count);
            foreach (string k in _groupOrder)
            {
                if (grouped.ContainsKey(k))
                {
                    _groupMissingTicks[k] = 0;
                    remainingOrder.Add(k);
                }
                else
                {
                    bool anyAliveOnThisBar = false;
                    List<IntPtr> known;
                    if (_groupKnownHandles.TryGetValue(k, out known))
                    {
                        foreach (IntPtr h in known)
                        {
                            if (NativeMethods.IsWindow(h)
                                && (!relevantOnly || WindowHelper.GetWindowMonitor(h) == _monitor.HMonitor))
                            {
                                anyAliveOnThisBar = true;
                                break;
                            }
                        }
                    }

                    int missCount = 0;
                    _groupMissingTicks.TryGetValue(k, out missCount);
                    missCount++;
                    _groupMissingTicks[k] = missCount;

                    if (anyAliveOnThisBar && missCount < 6)
                    {
                        remainingOrder.Add(k);
                    }
                    else
                    {
                        _groupMissingTicks.Remove(k);
                        _groupKnownHandles.Remove(k);
                    }
                }
            }
            _groupOrder.Clear();
            _groupOrder.AddRange(remainingOrder);

            foreach (string k in grouped.Keys)
            {
                if (!_groupOrder.Exists(item => string.Equals(item, k, StringComparison.OrdinalIgnoreCase)))
                    _groupOrder.Add(k);
            }

            // Prune closed group icons from cache only when removed from _groupOrder
            var activeOrderSet = new HashSet<string>(_groupOrder, StringComparer.OrdinalIgnoreCase);
            var staleIcons = new List<string>();
            foreach (string k in _groupIconCache.Keys)
            {
                if (!activeOrderSet.Contains(k))
                    staleIcons.Add(k);
            }
            foreach (string k in staleIcons)
            {
                if (_groupIconCache[k] != null) _groupIconCache[k].Dispose();
                _groupIconCache.Remove(k);
            }

            int barW = _barRect.Width;
            int barH = _barRect.Height;
            int padV = (int)Math.Round(4 * _scale);
            int padH = (int)Math.Round(8 * _scale);

            int startSz = barH - padV * 2;
            _startRect = new Rectangle(padH, padV, startSz, startSz);

            int desktopSliverW = Math.Max(5, (int)Math.Round(7 * _scale));
            _showDesktopRect = new Rectangle(barW - desktopSliverW, 0, desktopSliverW, barH);

            int clockW = (int)Math.Round(84 * _scale);
            _clockRect = new Rectangle(_showDesktopRect.Left - clockW - (int)Math.Round(4 * _scale), padV, clockW, barH - padV * 2);

            int availLeft = _startRect.Right + (int)Math.Round(6 * _scale);
            int availRight = _clockRect.Left - (int)Math.Round(8 * _scale);

            _slots.Clear();
            int gap = (int)Math.Round(4 * _scale);
            int iconSlotBase = barH - padV * 2; // Strictly fixed square tile width like Windows 11
            int x = availLeft;

            foreach (string key in _groupOrder)
            {
                AppGroupSlot slot;
                if (!grouped.TryGetValue(key, out slot))
                    continue;

                int slotW = iconSlotBase;
                if (x + slotW > availRight)
                    break;

                slot.Bounds = new Rectangle(x, padV, slotW, barH - padV * 2);
                _slots.Add(slot);
                x += slotW + gap;
            }

            DateTime now = DateTime.Now;
            _lastTimeText = now.ToString("t");
            _lastDateText = now.ToString("d");

            RenderBar();
        }

        private Bitmap GetCachedGroupIcon(AppGroupSlot slot)
        {
            Bitmap bmp;
            if (_groupIconCache.TryGetValue(slot.GroupKey, out bmp))
                return bmp;

            int iconSz = Math.Max(20, (int)Math.Round(24 * _scale));
            bmp = WindowHelper.GetAppIconBitmap(slot.PrimaryWindow.Handle, slot.PrimaryWindow.RealExePath, iconSz);
            _groupIconCache[slot.GroupKey] = bmp;
            return bmp;
        }

        private AppGroupSlot HitTestSlot(Point pt)
        {
            if (pt.Y < 0 || pt.Y > _barRect.Height)
                return null;

            foreach (var slot in _slots)
            {
                if (pt.X >= slot.Bounds.Left && pt.X < slot.Bounds.Right)
                    return slot;
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool startHit = _startRect.Contains(e.Location);
            bool clockHit = _clockRect.Contains(e.Location);
            var slot = HitTestSlot(e.Location);
            string hitKey = slot != null ? slot.GroupKey : null;

            bool changed = (startHit != _startHovered)
                        || (clockHit != _clockHovered)
                        || !string.Equals(hitKey, _hoveredGroupKey, StringComparison.OrdinalIgnoreCase);

            if (!string.Equals(hitKey, _hoveredGroupKey, StringComparison.OrdinalIgnoreCase))
                _suppressHoverGroupKey = null;

            _startHovered = startHit;
            _clockHovered = clockHit;
            _hoveredGroupKey = hitKey;

            if (slot != null)
            {
                _hoveredBtnRect = new Rectangle(
                    _barRect.Left + slot.Bounds.Left,
                    _barRect.Top + slot.Bounds.Top,
                    slot.Bounds.Width,
                    slot.Bounds.Height);

                if (_settings.TaskbarPreviewStyle == (int)TaskbarPreviewMode.TextTooltipOnly)
                {
                    _previewForm.HidePreview();
                    string tip = slot.Windows.Count == 1
                        ? slot.Windows[0].Title
                        : string.Format("{0} ({1} windows)", slot.Windows[0].Title, slot.Windows.Count);
                    if (_lastTooltipText != tip)
                    {
                        _lastTooltipText = tip;
                        _toolTip.SetToolTip(this, tip);
                    }
                }
                else if (_settings.TaskbarPreviewStyle == (int)TaskbarPreviewMode.WindowsModern)
                {
                    _toolTip.SetToolTip(this, null);
                    _lastTooltipText = string.Empty;

                    bool sameActive = _previewForm.IsPreviewVisible
                        && string.Equals(_previewForm.ActiveGroupKey, hitKey, StringComparison.OrdinalIgnoreCase);
                    bool suppressed = !string.IsNullOrEmpty(_suppressHoverGroupKey)
                        && string.Equals(_suppressHoverGroupKey, hitKey, StringComparison.OrdinalIgnoreCase);
                    bool mouseDown = (Control.MouseButtons & MouseButtons.Left) != 0;

                    if (!sameActive && !suppressed && !mouseDown && (changed || !_hoverTimer.Enabled))
                    {
                        _hoverTimer.Stop();
                        _hoverTimer.Interval = _previewForm.IsPreviewVisible ? 320 : 180;
                        _hoverTimer.Start();
                    }
                }
                else
                {
                    _toolTip.SetToolTip(this, null);
                    _previewForm.HidePreview();
                }
            }
            else
            {
                _hoverTimer.Stop();
                Point cur = Cursor.Position;
                if (!(!_previewForm.IsDisposed && _previewForm.IsPreviewVisible && (_previewForm.IsPinnedByClick || _previewForm.IsCursorInSafeZone(cur))))
                    _previewForm.HidePreview();

                string tip = startHit ? "Start" : (clockHit ? DateTime.Now.ToLongDateString() : (_showDesktopRect.Contains(e.Location) ? "Show Desktop" : null));
                if (_lastTooltipText != (tip ?? string.Empty))
                {
                    _lastTooltipText = tip ?? string.Empty;
                    _toolTip.SetToolTip(this, tip);
                }
            }

            if (changed) RenderBar();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _startHovered = false;
            _clockHovered = false;
            _hoveredGroupKey = null;
            _suppressHoverGroupKey = null;
            _hoverTimer.Stop();
            RenderBar();

            // Let TaskbarPreviewForm's watch timer handle hover-to-preview vertical bridge cleanly
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _hoverTimer.Stop();
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg != IntPtr.Zero && !WindowHelper.IsInternalHandle(fg))
            {
                _fgBeforeBarClick = fg;
                _fgCapturedTickMs = Environment.TickCount;
            }
        }

        private void OnHoverTimerTick(object sender, EventArgs e)
        {
            _hoverTimer.Stop();
            if (string.IsNullOrEmpty(_hoveredGroupKey) || _settings.TaskbarPreviewStyle != (int)TaskbarPreviewMode.WindowsModern)
                return;

            Point screenCur = Cursor.Position;
            if (!_previewForm.IsDisposed && _previewForm.IsPreviewVisible)
            {
                // Do not switch or replace the currently open preview if the mouse has already moved into (or toward) the preview cards!
                Rectangle inflatedPreview = Rectangle.Inflate(_previewForm.Bounds, 8, 8);
                if (inflatedPreview.Contains(screenCur) || screenCur.Y < _barRect.Top)
                    return;
            }

            Point clientCur = PointToClient(screenCur);
            var currentSlot = HitTestSlot(clientCur);
            if (currentSlot == null || !string.Equals(currentSlot.GroupKey, _hoveredGroupKey, StringComparison.OrdinalIgnoreCase))
                return;

            var slot = _slots.Find(s => string.Equals(s.GroupKey, _hoveredGroupKey, StringComparison.OrdinalIgnoreCase));
            if (slot != null)
            {
                _previewForm.ShowGroupPreview(slot.GroupKey, slot.Windows, _hoveredBtnRect, _monitor.Bounds, _scale, _lightTheme);
            }
        }

        protected override void OnDragOver(DragEventArgs drgevent)
        {
            base.OnDragOver(drgevent);
            drgevent.Effect = DragDropEffects.None;

            if (!_settings.TaskbarFocusOnDragHover)
            {
                _dragTargetGroupKey = null;
                _dragTimer.Stop();
                return;
            }

            Point clientPt = PointToClient(new Point(drgevent.X, drgevent.Y));
            var slot = HitTestSlot(clientPt);
            if (slot != null)
            {
                if (!string.Equals(_dragTargetGroupKey, slot.GroupKey, StringComparison.OrdinalIgnoreCase))
                {
                    _dragTargetGroupKey = slot.GroupKey;
                    _dragTimer.Stop();
                    _dragTimer.Start();
                }
            }
            else
            {
                _dragTargetGroupKey = null;
                _dragTimer.Stop();
            }
        }

        protected override void OnDragLeave(EventArgs e)
        {
            base.OnDragLeave(e);
            _dragTargetGroupKey = null;
            _dragTimer.Stop();
        }

        private void OnDragTimerTick(object sender, EventArgs e)
        {
            _dragTimer.Stop();
            if (string.IsNullOrEmpty(_dragTargetGroupKey))
                return;

            var slot = _slots.Find(s => string.Equals(s.GroupKey, _dragTargetGroupKey, StringComparison.OrdinalIgnoreCase));
            if (slot != null && slot.PrimaryWindow != null && NativeMethods.IsWindow(slot.PrimaryWindow.Handle))
            {
                WindowHelper.BringWindowToFront(slot.PrimaryWindow.Handle);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _hoverTimer.Stop();

            if (_startRect.Contains(e.Location))
            {
                Capture = false;
                NativeMethods.ReleaseCapture();
                _previewForm.HidePreview();
                if (e.Button == MouseButtons.Left)
                {
                    NativeMethods.keybd_event((byte)NativeMethods.VK_LWIN, 0, 0, UIntPtr.Zero);
                    NativeMethods.keybd_event((byte)NativeMethods.VK_LWIN, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
                }
                else if (e.Button == MouseButtons.Right)
                {
                    ShowTaskbarBarMenu(e.Location);
                }
                return;
            }

            if (_showDesktopRect.Contains(e.Location) && e.Button == MouseButtons.Left)
            {
                Capture = false;
                NativeMethods.ReleaseCapture();
                _previewForm.HidePreview();
                foreach (var slot in _slots)
                {
                    foreach (var w in slot.Windows)
                    {
                        if (!w.IsMinimized)
                            NativeMethods.ShowWindow(w.Handle, NativeMethods.SW_MINIMIZE);
                    }
                }
                return;
            }

            var slotHit = HitTestSlot(e.Location);
            if (slotHit == null)
            {
                Capture = false;
                NativeMethods.ReleaseCapture();
                _previewForm.HidePreview();
                if (e.Button == MouseButtons.Right)
                    ShowTaskbarBarMenu(e.Location);
                return;
            }

            IntPtr target = slotHit.PrimaryWindow.Handle;
            bool shiftHeld = (ModifierKeys & Keys.Shift) == Keys.Shift;

            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && shiftHeld))
            {
                Capture = false;
                NativeMethods.ReleaseCapture();
                _previewForm.HidePreview();
                int behavior = _settings.TaskbarMiddleClickBehavior;
                if (behavior == (int)TaskbarMiddleClickMode.OpenNewInstance)
                    WindowHelper.OpenNewInstance(target);
                else if (behavior == (int)TaskbarMiddleClickMode.CloseWindow)
                    WindowHelper.CloseWindow(target);
            }
            else if (e.Button == MouseButtons.Left)
            {
                if (slotHit.Windows.Count == 1)
                {
                    _suppressHoverGroupKey = slotHit.GroupKey;
                    Capture = false;
                    NativeMethods.ReleaseCapture();
                    _previewForm.HidePreview();
                    IntPtr priorFg = unchecked(Environment.TickCount - _fgCapturedTickMs) < 1500
                        ? _fgBeforeBarClick
                        : IntPtr.Zero;
                    _fgBeforeBarClick = IntPtr.Zero;
                    WindowHelper.LogTaskbarDiag(string.Format(
                        "TaskbarSlotClick group='{0}' target=0x{1:X} title='{2}' priorFg=0x{3:X}",
                        slotHit.GroupKey, target.ToInt64(), slotHit.PrimaryWindow.Title, priorFg.ToInt64()));
                    WindowHelper.ActivateOrToggleMinimize(target, priorFg);
                }
                else
                {
                    Capture = false;
                    NativeMethods.ReleaseCapture();
                    // Multiple windows in this app group (e.g. File Explorer, Sticky Notes)
                    if (_settings.TaskbarPreviewStyle == (int)TaskbarPreviewMode.WindowsModern)
                    {
                        bool alreadyShowingThis = !_previewForm.IsDisposed
                            && _previewForm.IsPreviewVisible
                            && _previewForm.IsPinnedByClick
                            && string.Equals(_previewForm.ActiveGroupKey, slotHit.GroupKey, StringComparison.OrdinalIgnoreCase);

                        if (alreadyShowingThis)
                        {
                            _suppressHoverGroupKey = slotHit.GroupKey;
                            _previewForm.HidePreview();
                        }
                        else
                        {
                            var btnRect = new Rectangle(
                                _barRect.Left + slotHit.Bounds.Left,
                                _barRect.Top + slotHit.Bounds.Top,
                                slotHit.Bounds.Width,
                                slotHit.Bounds.Height);
                            _previewForm.ShowGroupPreview(slotHit.GroupKey, slotHit.Windows, btnRect, _monitor.Bounds, _scale, _lightTheme, true);
                        }
                    }
                    else
                    {
                        ShowGroupContextMenu(slotHit, e.Location);
                    }
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                _suppressHoverGroupKey = slotHit.GroupKey;
                Capture = false;
                NativeMethods.ReleaseCapture();
                _previewForm.HidePreview();
                if (slotHit.Windows.Count == 1)
                    ShowWindowContextMenu(slotHit.PrimaryWindow, e.Location);
                else
                    ShowGroupContextMenu(slotHit, e.Location);
            }
        }

        private void ApplyMenuTheme(ContextMenuStrip menu)
        {
            if (!_lightTheme)
            {
                menu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColorTable());
                Color fg = Color.FromArgb(236, 239, 244);
                menu.ForeColor = fg;
                foreach (ToolStripItem item in menu.Items)
                    item.ForeColor = fg;
            }
        }

        private void ShowTaskbarBarMenu(Point pt)
        {
            var menu = new ContextMenuStrip();
            var settingsItem = new ToolStripMenuItem("Monitor Fader Settings...");
            settingsItem.Click += (s, e) => { if (_openSettingsAction != null) _openSettingsAction(); };
            var refreshItem = new ToolStripMenuItem("Refresh Taskbars");
            refreshItem.Click += (s, e) => { if (_refreshAction != null) _refreshAction(); };
            menu.Items.Add(settingsItem);
            menu.Items.Add(refreshItem);
            ApplyMenuTheme(menu);
            menu.Show(this, pt);
        }

        private void ShowWindowContextMenu(WindowEntry win, Point pt)
        {
            var menu = new ContextMenuStrip();
            var focusItem = new ToolStripMenuItem(win.IsMinimized ? "Restore" : "Switch To");
            focusItem.Click += (s, e) => WindowHelper.BringWindowToFront(win.Handle);

            var moveItem = new ToolStripMenuItem("Move to Next Monitor");
            moveItem.Click += (s, e) => WindowHelper.MoveWindowToMonitor(win.Handle, false);

            var minItem = new ToolStripMenuItem("Minimize");
            minItem.Enabled = !win.IsMinimized;
            minItem.Click += (s, e) => WindowHelper.MinimizeWindow(win.Handle);

            var maxItem = new ToolStripMenuItem(win.IsMaximized ? "Restore Size" : "Maximize");
            maxItem.Click += (s, e) => NativeMethods.ShowWindow(win.Handle, win.IsMaximized ? NativeMethods.SW_RESTORE : NativeMethods.SW_MAXIMIZE);

            var newInstItem = new ToolStripMenuItem("Open New Instance");
            newInstItem.Click += (s, e) => WindowHelper.OpenNewInstance(win.Handle);

            var closeItem = new ToolStripMenuItem("Close Window");
            closeItem.Click += (s, e) => WindowHelper.CloseWindow(win.Handle);

            menu.Items.Add(focusItem);
            menu.Items.Add(moveItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(minItem);
            menu.Items.Add(maxItem);
            menu.Items.Add(newInstItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(closeItem);
            ApplyMenuTheme(menu);
            menu.Show(this, pt);
        }

        private void ShowGroupContextMenu(AppGroupSlot group, Point pt)
        {
            var menu = new ContextMenuStrip();
            foreach (var win in group.Windows)
            {
                WindowEntry captured = win;
                string label = win.Title.Length > 48 ? win.Title.Substring(0, 45) + "..." : win.Title;
                var item = new ToolStripMenuItem(label);
                if (win.IsForeground)
                    item.Font = new Font(item.Font, FontStyle.Bold);
                item.Click += (s, e) => ActivateWindowFromPreview(captured.Handle);
                menu.Items.Add(item);
            }

            menu.Items.Add(new ToolStripSeparator());

            var newInstItem = new ToolStripMenuItem("Open New Instance");
            newInstItem.Click += (s, e) => WindowHelper.OpenNewInstance(group.PrimaryWindow.Handle);

            var moveAllItem = new ToolStripMenuItem("Move All to Next Monitor");
            moveAllItem.Click += (s, e) =>
            {
                foreach (var w in group.Windows)
                    WindowHelper.MoveWindowToMonitor(w.Handle, false);
            };

            var minAllItem = new ToolStripMenuItem("Minimize All");
            minAllItem.Click += (s, e) =>
            {
                foreach (var w in group.Windows)
                    NativeMethods.ShowWindow(w.Handle, NativeMethods.SW_MINIMIZE);
            };

            var closeAllItem = new ToolStripMenuItem("Close All Windows");
            closeAllItem.Click += (s, e) =>
            {
                foreach (var w in group.Windows)
                    WindowHelper.CloseWindow(w.Handle);
            };

            menu.Items.Add(newInstItem);
            menu.Items.Add(moveAllItem);
            menu.Items.Add(minAllItem);
            menu.Items.Add(closeAllItem);
            ApplyMenuTheme(menu);
            menu.Show(this, pt);
        }

        private void RenderBar()
        {
            if (!_isShown || !IsHandleCreated || _barRect.Width <= 0 || _barRect.Height <= 0)
                return;

            int barW = _barRect.Width;
            int barH = _barRect.Height;
            int bgAlpha = Math.Max(14, Math.Min(255, (int)Math.Round(2.40 * _settings.TaskbarOpacityPercent)));
            float radius = 5f * _scale;

            Color bgCol = _lightTheme
                ? Color.FromArgb(bgAlpha, 238, 241, 246)
                : Color.FromArgb(bgAlpha, 26, 28, 33);
            Color topBorderCol = _lightTheme
                ? Color.FromArgb(Math.Min(255, bgAlpha + 10), 205, 210, 218)
                : Color.FromArgb(Math.Min(255, bgAlpha + 10), 58, 62, 70);

            using (var bmp = new Bitmap(barW, barH, PixelFormat.Format32bppPArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                g.Clear(bgCol);
                using (var topLine = new Pen(topBorderCol, 1f))
                {
                    g.DrawLine(topLine, 0, 0, barW, 0);
                }

                // Start button pill
                if (_startHovered)
                {
                    Color startHoverCol = _lightTheme ? Color.FromArgb(26, 0, 0, 0) : Color.FromArgb(26, 255, 255, 255);
                    using (var path = WindowHelper.CreateRoundedRectPath(_startRect, radius))
                    using (var b = new SolidBrush(startHoverCol))
                    {
                        g.FillPath(b, path);
                    }
                }

                // Windows 11 4-pane logo
                float logoPad = Math.Max(8f, _startRect.Width * 0.27f);
                float gap = Math.Max(1.5f, 1.8f * _scale);
                float pane = (_startRect.Width - logoPad * 2f - gap) / 2f;
                float lx = _startRect.Left + logoPad;
                float ly = _startRect.Top + logoPad;
                using (var topPaneBrush = new SolidBrush(Color.FromArgb(255, 96, 194, 255)))
                using (var botPaneBrush = new SolidBrush(Color.FromArgb(255, 0, 120, 212)))
                {
                    g.FillRectangle(topPaneBrush, lx, ly, pane, pane);
                    g.FillRectangle(topPaneBrush, lx + pane + gap, ly, pane, pane);
                    g.FillRectangle(botPaneBrush, lx, ly + pane + gap, pane, pane);
                    g.FillRectangle(botPaneBrush, lx + pane + gap, ly + pane + gap, pane, pane);
                }

                // Icon-only grouped taskbar slots (Windows 11 style)
                foreach (var slot in _slots)
                {
                    Rectangle r = slot.Bounds;
                    bool isHovered = string.Equals(_hoveredGroupKey, slot.GroupKey, StringComparison.OrdinalIgnoreCase);
                    bool isGrouped = slot.Windows.Count > 1;

                    // Subtle stacked shadow plate behind grouped icons (File Explorer, Sticky Notes, etc.)
                    if (isGrouped)
                    {
                        var stackRect = new RectangleF(r.X + 4 * _scale, r.Y + 2 * _scale, r.Width - 4 * _scale, r.Height - 4 * _scale);
                        Color stackBorder = _lightTheme ? Color.FromArgb(45, 0, 0, 0) : Color.FromArgb(35, 255, 255, 255);
                        using (var spath = WindowHelper.CreateRoundedRectPath(stackRect, radius))
                        using (var spen = new Pen(stackBorder, 1f))
                        {
                            g.DrawPath(spen, spath);
                        }
                    }

                    RectangleF mainRect = isGrouped
                        ? new RectangleF(r.X, r.Y, r.Width - 3 * _scale, r.Height)
                        : new RectangleF(r.X, r.Y, r.Width, r.Height);

                    Color pillFill = Color.Transparent;
                    Color pillBorder = Color.Transparent;

                    if (slot.IsAnyForeground)
                    {
                        pillFill = _lightTheme
                            ? Color.FromArgb(isHovered ? 235 : 210, 255, 255, 255)
                            : Color.FromArgb(isHovered ? 46 : 34, 255, 255, 255);
                        pillBorder = _lightTheme
                            ? Color.FromArgb(60, 0, 0, 0)
                            : Color.FromArgb(28, 255, 255, 255);
                    }
                    else if (isHovered)
                    {
                        pillFill = _lightTheme
                            ? Color.FromArgb(140, 255, 255, 255)
                            : Color.FromArgb(24, 255, 255, 255);
                        pillBorder = _lightTheme
                            ? Color.FromArgb(32, 0, 0, 0)
                            : Color.FromArgb(16, 255, 255, 255);
                    }

                    if (pillFill.A > 0)
                    {
                        using (var path = WindowHelper.CreateRoundedRectPath(mainRect, radius))
                        using (var fillBrush = new SolidBrush(pillFill))
                        using (var borderPen = new Pen(pillBorder, 1f))
                        {
                            g.FillPath(fillBrush, path);
                            g.DrawPath(borderPen, path);
                        }
                    }

                    // Bottom pill indicator
                    float indH = Math.Max(2.5f, 3f * _scale);
                    float indW = slot.IsAnyForeground
                        ? 16f * _scale
                        : (isGrouped ? 10f * _scale : (slot.IsAllMinimized ? 4f * _scale : 6f * _scale));
                    float indX = mainRect.X + (mainRect.Width - indW) / 2f;
                    float indY = mainRect.Bottom - indH - 1.5f * _scale;
                    Color indColor = slot.IsAnyForeground
                        ? (_lightTheme ? Color.FromArgb(255, 0, 103, 192) : Color.FromArgb(255, 96, 205, 255))
                        : (_lightTheme ? Color.FromArgb(180, 110, 118, 128) : Color.FromArgb(185, 155, 163, 175));

                    var indRect = new RectangleF(indX, indY, indW, indH);
                    using (var indPath = WindowHelper.CreateRoundedRectPath(indRect, indH / 2f))
                    using (var indBrush = new SolidBrush(indColor))
                    {
                        g.FillPath(indBrush, indPath);
                    }

                    // Centered crisp app icon
                    Bitmap icBmp = GetCachedGroupIcon(slot);
                    if (icBmp != null)
                    {
                        int iconSz = Math.Max(20, (int)Math.Round(24 * _scale));
                        int ix = (int)Math.Round(mainRect.X + (mainRect.Width - iconSz) / 2f);
                        int iy = (int)Math.Round(mainRect.Y + (mainRect.Height - iconSz) / 2f - 1.5f * _scale);
                        g.DrawImage(icBmp, ix, iy, iconSz, iconSz);
                    }
                }

                // Clock hover pill
                if (_clockHovered)
                {
                    Color clockHoverCol = _lightTheme ? Color.FromArgb(24, 0, 0, 0) : Color.FromArgb(22, 255, 255, 255);
                    using (var path = WindowHelper.CreateRoundedRectPath(_clockRect, radius))
                    using (var b = new SolidBrush(clockHoverCol))
                    {
                        g.FillPath(b, path);
                    }
                }

                float clockPx = Math.Max(10f, 11.5f * _scale);
                Color clockColor = _lightTheme ? Color.FromArgb(30, 34, 40) : Color.FromArgb(236, 239, 244);
                using (var clockFont = new Font("Segoe UI", clockPx, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var clockBrush = new SolidBrush(clockColor))
                {
                    var csf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
                    float halfH = _clockRect.Height / 2f;
                    var timeRect = new RectangleF(_clockRect.X, _clockRect.Y + 2 * _scale, _clockRect.Width - 8 * _scale, halfH - 1 * _scale);
                    var dateRect = new RectangleF(_clockRect.X, _clockRect.Y + halfH - 1 * _scale, _clockRect.Width - 8 * _scale, halfH - 1 * _scale);
                    g.DrawString(_lastTimeText, clockFont, clockBrush, timeRect, csf);
                    g.DrawString(_lastDateText, clockFont, clockBrush, dateRect, csf);
                }

                Color sepColor = _lightTheme ? Color.FromArgb(90, 0, 0, 0) : Color.FromArgb(70, 255, 255, 255);
                using (var sepPen = new Pen(sepColor, 1f))
                {
                    g.DrawLine(sepPen, _showDesktopRect.Left, 8 * _scale, _showDesktopRect.Left, barH - 8 * _scale);
                }

                WindowHelper.UpdateLayeredBitmap(this, bmp, _barRect.X, _barRect.Y);
            }
        }

        protected override void Dispose(bool disposing)
        {
            UnregisterAppBar();
            if (IsHandleCreated)
                WindowHelper.UnregisterInternalHandle(Handle);

            if (disposing)
            {
                _hoverTimer.Stop();
                _hoverTimer.Dispose();
                _dragTimer.Stop();
                _dragTimer.Dispose();
                _toolTip.Dispose();
                if (_previewForm != null && !_previewForm.IsDisposed)
                    _previewForm.Dispose();
                foreach (var ic in _groupIconCache.Values)
                {
                    if (ic != null) ic.Dispose();
                }
                _groupIconCache.Clear();
            }
            base.Dispose(disposing);
        }
    }

    public sealed class TaskbarManager : IDisposable
    {
        private readonly AppSettings _settings;
        private readonly Action _openSettingsAction;
        private readonly Dictionary<string, SecondaryTaskbarForm> _bars = new Dictionary<string, SecondaryTaskbarForm>(StringComparer.OrdinalIgnoreCase);
        private readonly Timer _refreshTimer;

        public TaskbarManager(AppSettings settings, Action openSettingsAction)
        {
            _settings = settings;
            _openSettingsAction = openSettingsAction;

            _refreshTimer = new Timer();
            _refreshTimer.Interval = 250;
            _refreshTimer.Tick += (s, e) => Tick();

            ApplySettings();
        }

        public void ApplySettings()
        {
            if (_settings.EnableMultiMonitorTaskbars)
            {
                SyncMonitors();
                if (!_refreshTimer.Enabled)
                    _refreshTimer.Start();
                Tick();
            }
            else
            {
                _refreshTimer.Stop();
                ClearAllBars();
            }
        }

        public void RefreshMonitors()
        {
            if (_settings.EnableMultiMonitorTaskbars)
                SyncMonitors();
        }

        private void SyncMonitors()
        {
            var monitors = MonitorManager.GetMonitors();
            var secondaryDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var mon in monitors)
            {
                if (mon.IsPrimary)
                    continue;

                secondaryDevices.Add(mon.DeviceName);
                SecondaryTaskbarForm bar;
                if (!_bars.TryGetValue(mon.DeviceName, out bar) || bar.IsDisposed)
                {
                    bar = new SecondaryTaskbarForm(mon, _settings, _openSettingsAction, RefreshMonitors);
                    _bars[mon.DeviceName] = bar;
                }
                else
                {
                    bar.UpdateMonitor(mon);
                }
            }

            var toRemove = new List<string>();
            foreach (var k in _bars.Keys)
            {
                if (!secondaryDevices.Contains(k))
                    toRemove.Add(k);
            }
            foreach (var k in toRemove)
            {
                _bars[k].Dispose();
                _bars.Remove(k);
            }
        }

        private void Tick()
        {
            if (!_settings.EnableMultiMonitorTaskbars || _bars.Count == 0)
                return;

            var monitors = MonitorManager.GetMonitors();
            var secondarySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in monitors)
            {
                if (!m.IsPrimary) secondarySet.Add(m.DeviceName);
            }
            if (secondarySet.Count != _bars.Count)
                SyncMonitors();

            var windows = WindowHelper.GetTaskbarWindows();
            var fullscreenMonitors = MonitorManager.GetMonitorsWithFullscreenWindow(monitors);
            IntPtr fg = NativeMethods.GetForegroundWindow();
            IntPtr fgMon = fg != IntPtr.Zero
                ? NativeMethods.MonitorFromWindow(fg, NativeMethods.MONITOR_DEFAULTTONULL)
                : IntPtr.Zero;

            foreach (var bar in _bars.Values)
            {
                bool hasFs = fullscreenMonitors.Contains(bar.MonitorHandle);
                bool fsFocused = hasFs && (fgMon == bar.MonitorHandle);
                bar.UpdateWindows(windows, hasFs, fsFocused);
            }
        }

        private void ClearAllBars()
        {
            foreach (var bar in _bars.Values)
            {
                if (!bar.IsDisposed)
                    bar.Dispose();
            }
            _bars.Clear();
        }

        public void Dispose()
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            ClearAllBars();
        }
    }
}
