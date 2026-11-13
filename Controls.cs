using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MonitorFader
{
    public sealed class OpacitySlider : Control
    {
        private int _value = 76;
        private bool _dragging;
        private string _labelPrefix = "Opacity";
        private bool _darkMode;

        public event EventHandler ValueChanged;

        public bool DarkMode
        {
            get { return _darkMode; }
            set
            {
                if (_darkMode != value)
                {
                    _darkMode = value;
                    BackColor = _darkMode ? Color.FromArgb(35, 38, 46) : Color.FromArgb(242, 242, 242);
                    Invalidate();
                }
            }
        }

        public string LabelPrefix
        {
            get { return _labelPrefix; }
            set
            {
                _labelPrefix = string.IsNullOrEmpty(value) ? "Opacity" : value;
                Invalidate();
            }
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int clamped = Math.Max(1, Math.Min(100, value));
                if (_value != clamped)
                {
                    _value = clamped;
                    Invalidate();
                    var h = ValueChanged;
                    if (h != null) h(this, EventArgs.Empty);
                }
            }
        }

        public OpacitySlider()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint
                | ControlStyles.Selectable,
                true);
            BackColor = Color.FromArgb(242, 242, 242);
            Size = new Size(560, 82);
            Cursor = Cursors.Hand;
        }

        private float _dpiScale = 1f;

        private float DpiScale
        {
            get { return _dpiScale; }
        }

        private void UpdateDpiScale()
        {
            if (IsHandleCreated)
            {
                using (var g = CreateGraphics())
                    _dpiScale = Math.Max(1f, g.DpiX / 96f);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateDpiScale();
        }

        private int TrackLeft { get { return (int)Math.Round(18 * _dpiScale); } }
        private int TrackRight { get { return Width - (int)Math.Round(18 * _dpiScale); } }
        private int TrackY { get { return (int)Math.Round(16 * _dpiScale); } }

        private int ValueToX(int val)
        {
            double ratio = (val - 1) / 99.0;
            return TrackLeft + (int)Math.Round(ratio * (TrackRight - TrackLeft));
        }

        private int XToValue(int x)
        {
            if (TrackRight <= TrackLeft) return 1;
            double ratio = (double)(x - TrackLeft) / (TrackRight - TrackLeft);
            ratio = Math.Max(0.0, Math.Min(1.0, ratio));
            return 1 + (int)Math.Round(ratio * 99.0);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                Value = XToValue(e.X);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                Value = XToValue(e.X);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down)
                return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down)
                Value = _value - 1;
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up)
                Value = _value + 1;
            else if (e.KeyCode == Keys.PageDown)
                Value = _value - 5;
            else if (e.KeyCode == Keys.PageUp)
                Value = _value + 5;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            _dpiScale = Math.Max(1f, g.DpiX / 96f);
            float s = _dpiScale;

            Color borderColor = _darkMode ? Color.FromArgb(56, 61, 71) : Color.FromArgb(215, 215, 215);
            Color trackColor = _darkMode ? Color.FromArgb(72, 78, 92) : Color.FromArgb(190, 190, 190);
            Color fillColor = _darkMode ? Color.FromArgb(96, 181, 255) : Color.FromArgb(60, 120, 216);
            Color minorTickColor = _darkMode ? Color.FromArgb(68, 74, 86) : Color.FromArgb(198, 198, 198);
            Color majorTickColor = _darkMode ? Color.FromArgb(108, 116, 134) : Color.FromArgb(150, 150, 150);
            Color tickTextColor = _darkMode ? Color.FromArgb(152, 162, 179) : Color.FromArgb(115, 115, 115);
            Color thumbOuter = _darkMode ? Color.FromArgb(45, 50, 60) : Color.White;
            Color thumbBorder = _darkMode ? Color.FromArgb(140, 152, 170) : Color.FromArgb(95, 95, 95);
            Color thumbCore = _darkMode ? Color.FromArgb(96, 181, 255) : Color.FromArgb(75, 75, 75);
            Color valueTextColor = _darkMode ? Color.FromArgb(236, 239, 244) : Color.FromArgb(45, 45, 45);

            using (var borderPen = new Pen(borderColor))
            {
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }

            using (var trackPen = new Pen(trackColor, 2f * s))
            {
                g.DrawLine(trackPen, TrackLeft, TrackY, TrackRight, TrackY);
            }

            int thumbX = ValueToX(_value);
            using (var fillPen = new Pen(fillColor, 2.5f * s))
            {
                g.DrawLine(fillPen, TrackLeft, TrackY, thumbX, TrackY);
            }

            using (var minorPen = new Pen(minorTickColor, 1f))
            using (var majorPen = new Pen(majorTickColor, 1f))
            using (var labelFont = new Font("Segoe UI", 7.5f))
            using (var labelBrush = new SolidBrush(tickTextColor))
            {
                for (int v = 1; v <= 100; v++)
                {
                    int x = ValueToX(v);
                    bool isMajor = (v == 1 || v % 5 == 0);
                    int tickTop = TrackY + (int)Math.Round(6 * s);
                    int tickBottom = isMajor ? TrackY + (int)Math.Round(14 * s) : TrackY + (int)Math.Round(10 * s);
                    g.DrawLine(isMajor ? majorPen : minorPen, x, tickTop, x, tickBottom);

                    if (isMajor)
                    {
                        string text = v + "%";
                        var state = g.Save();
                        g.TranslateTransform(x, TrackY + 17f * s);
                        g.RotateTransform(90);
                        g.DrawString(text, labelFont, labelBrush, 0, -6f * s);
                        g.Restore(state);
                    }
                }
            }

            int r = (int)Math.Round(7 * s);
            int ir = (int)Math.Round(3 * s);
            var thumbRect = new Rectangle(thumbX - r, TrackY - r, r * 2, r * 2);
            using (var outerBrush = new SolidBrush(thumbOuter))
            using (var outerPen = new Pen(thumbBorder, 1.5f * s))
            using (var innerBrush = new SolidBrush(thumbCore))
            {
                g.FillEllipse(outerBrush, thumbRect);
                g.DrawEllipse(outerPen, thumbRect);
                g.FillEllipse(innerBrush, thumbX - ir, TrackY - ir, ir * 2, ir * 2);
            }

            using (var boldFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            using (var textBrush = new SolidBrush(valueTextColor))
            {
                g.DrawString(_labelPrefix + ": " + _value + "%", boldFont, textBrush, 8f * s, Height - 20f * s);
            }
        }
    }

    public sealed class MonitorLayoutControl : Control
    {
        private List<MonitorInfoItem> _monitors = new List<MonitorInfoItem>();
        private HashSet<string> _excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RectangleF> _monitorRects = new Dictionary<string, RectangleF>(StringComparer.OrdinalIgnoreCase);
        private string _hoveredDevice;
        private readonly ToolTip _toolTip = new ToolTip();

        public event Action<string> MonitorToggled;

        public MonitorLayoutControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);
            BackColor = Color.Black;
            Size = new Size(560, 125);
            Cursor = Cursors.Hand;
        }

        public void SetData(List<MonitorInfoItem> monitors, IEnumerable<string> excludedDevices)
        {
            _monitors = monitors ?? new List<MonitorInfoItem>();
            _excluded = new HashSet<string>(excludedDevices ?? new string[0], StringComparer.OrdinalIgnoreCase);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string hit = HitTest(e.Location);
            if (!string.Equals(hit, _hoveredDevice, StringComparison.OrdinalIgnoreCase))
            {
                _hoveredDevice = hit;
                if (hit != null)
                {
                    MonitorInfoItem m = _monitors.Find(x => string.Equals(x.DeviceName, hit, StringComparison.OrdinalIgnoreCase));
                    if (m != null)
                    {
                        bool isEx = _excluded.Contains(m.DeviceName);
                        _toolTip.SetToolTip(this, string.Format(
                            "Monitor {0}{1} ({2}x{3}) - Click to {4}",
                            m.DisplayNumber,
                            m.IsPrimary ? " [Primary]" : "",
                            m.Bounds.Width,
                            m.Bounds.Height,
                            isEx ? "Include in Fading" : "Exclude from Fading"));
                    }
                }
                else
                {
                    _toolTip.SetToolTip(this, null);
                }
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoveredDevice != null)
            {
                _hoveredDevice = null;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            string hit = HitTest(e.Location);
            if (hit != null)
            {
                var h = MonitorToggled;
                if (h != null) h(hit);
            }
        }

        private string HitTest(Point pt)
        {
            foreach (var kv in _monitorRects)
            {
                if (kv.Value.Contains(pt))
                    return kv.Key;
            }
            return null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Black);
            _monitorRects.Clear();

            if (_monitors.Count == 0)
            {
                using (var f = new Font("Segoe UI", 9f))
                using (var b = new SolidBrush(Color.Gray))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("No monitors detected", f, b, ClientRectangle, sf);
                }
                return;
            }

            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;
            foreach (var m in _monitors)
            {
                if (m.Bounds.Left < minX) minX = m.Bounds.Left;
                if (m.Bounds.Top < minY) minY = m.Bounds.Top;
                if (m.Bounds.Right > maxX) maxX = m.Bounds.Right;
                if (m.Bounds.Bottom > maxY) maxY = m.Bounds.Bottom;
            }

            float totalW = Math.Max(1, maxX - minX);
            float totalH = Math.Max(1, maxY - minY);
            float pad = 14f;
            float availW = Math.Max(10, Width - pad * 2);
            float availH = Math.Max(10, Height - pad * 2);
            float scale = Math.Min(availW / totalW, availH / totalH);

            float drawnW = totalW * scale;
            float drawnH = totalH * scale;
            float offsetX = (Width - drawnW) / 2f;
            float offsetY = (Height - drawnH) / 2f;

            foreach (var m in _monitors)
            {
                float x = offsetX + (m.Bounds.Left - minX) * scale;
                float y = offsetY + (m.Bounds.Top - minY) * scale;
                float w = m.Bounds.Width * scale;
                float h = m.Bounds.Height * scale;
                var rect = new RectangleF(x, y, w, h);
                _monitorRects[m.DeviceName] = rect;

                bool isExcluded = _excluded.Contains(m.DeviceName);
                bool isHovered = string.Equals(_hoveredDevice, m.DeviceName, StringComparison.OrdinalIgnoreCase);

                Color fillColor = isExcluded
                    ? Color.FromArgb(45, 18, 18)
                    : (isHovered ? Color.FromArgb(24, 34, 48) : Color.FromArgb(10, 10, 10));

                Color borderColor = isExcluded
                    ? Color.FromArgb(215, 70, 70)
                    : (isHovered ? Color.FromArgb(100, 165, 245) : Color.FromArgb(55, 55, 55));

                using (var fill = new SolidBrush(fillColor))
                using (var pen = new Pen(borderColor, isExcluded ? 1.8f : 1.2f))
                {
                    g.FillRectangle(fill, rect);
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                }

                float fontSize = Math.Max(10f, Math.Min(26f, rect.Height * 0.36f));
                FontStyle style = m.IsPrimary ? FontStyle.Underline : FontStyle.Regular;
                using (var numFont = new Font("Segoe UI", fontSize, style))
                using (var textBrush = new SolidBrush(isExcluded ? Color.FromArgb(235, 120, 120) : Color.FromArgb(110, 110, 110)))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(m.DisplayNumber.ToString(), numFont, textBrush, rect, sf);
                }

                if (isExcluded)
                {
                    using (var tagFont = new Font("Segoe UI", 7f, FontStyle.Bold))
                    using (var tagBrush = new SolidBrush(Color.FromArgb(235, 95, 95)))
                    {
                        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Far };
                        var tagRect = new RectangleF(rect.X, rect.Y, rect.Width, rect.Height - 3);
                        g.DrawString("EXCLUDED", tagFont, tagBrush, tagRect, sf);
                    }
                }
            }
        }
    }

    public sealed class ThemedComboBox : ComboBox
    {
        private bool _darkMode;

        public bool DarkMode
        {
            get { return _darkMode; }
            set
            {
                _darkMode = value;
                BackColor = _darkMode ? Color.FromArgb(37, 40, 48) : Color.White;
                ForeColor = _darkMode ? Color.FromArgb(236, 239, 244) : Color.FromArgb(30, 34, 40);
                Invalidate();
            }
        }

        public ThemedComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count)
                return;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color bg = _darkMode
                ? (selected ? Color.FromArgb(48, 66, 92) : Color.FromArgb(37, 40, 48))
                : (selected ? Color.FromArgb(229, 241, 251) : Color.White);
            Color fg = Enabled
                ? (_darkMode ? Color.FromArgb(236, 239, 244) : Color.FromArgb(30, 34, 40))
                : (_darkMode ? Color.FromArgb(110, 116, 128) : Color.FromArgb(150, 150, 150));

            using (var b = new SolidBrush(bg))
            {
                e.Graphics.FillRectangle(b, e.Bounds);
            }

            string text = GetItemText(Items[e.Index]);
            TextRenderer.DrawText(
                e.Graphics,
                text,
                Font,
                new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x000F /* WM_PAINT */)
            {
                using (var g = Graphics.FromHwnd(Handle))
                {
                    Color borderCol = _darkMode ? Color.FromArgb(62, 68, 80) : Color.FromArgb(190, 195, 204);
                    Color arrowCol = Enabled
                        ? (_darkMode ? Color.FromArgb(200, 206, 216) : Color.FromArgb(80, 85, 95))
                        : (_darkMode ? Color.FromArgb(95, 100, 110) : Color.FromArgb(160, 160, 160));
                    Color boxFill = _darkMode ? Color.FromArgb(37, 40, 48) : Color.White;

                    int dropW = SystemInformation.VerticalScrollBarWidth + 2;
                    var dropRect = new Rectangle(Width - dropW - 1, 1, dropW, Height - 2);
                    using (var fill = new SolidBrush(boxFill))
                    using (var border = new Pen(borderCol))
                    using (var arrowPen = new Pen(arrowCol, 1.5f))
                    {
                        g.FillRectangle(fill, dropRect);
                        g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

                        int cx = dropRect.X + dropRect.Width / 2;
                        int cy = Height / 2;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.DrawLine(arrowPen, cx - 4, cy - 2, cx, cy + 2);
                        g.DrawLine(arrowPen, cx, cy + 2, cx + 4, cy - 2);
                    }
                }
            }
        }
    }

    public sealed class DarkMenuColorTable : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin { get { return Color.FromArgb(34, 37, 44); } }
        public override Color MenuStripGradientEnd { get { return Color.FromArgb(34, 37, 44); } }
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(34, 37, 44); } }
        public override Color MenuBorder { get { return Color.FromArgb(60, 65, 76); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(65, 92, 130); } }
        public override Color MenuItemSelected { get { return Color.FromArgb(48, 58, 74); } }
        public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(48, 58, 74); } }
        public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(48, 58, 74); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(34, 37, 44); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(34, 37, 44); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(34, 37, 44); } }
        public override Color SeparatorDark { get { return Color.FromArgb(58, 63, 74); } }
        public override Color SeparatorLight { get { return Color.FromArgb(58, 63, 74); } }
        public override Color CheckBackground { get { return Color.FromArgb(48, 80, 120); } }
        public override Color CheckSelectedBackground { get { return Color.FromArgb(58, 96, 145); } }
    }

    public sealed class HotkeyCaptureDialog : Form
    {
        private readonly HotkeyHook _hook;
        private readonly bool _darkMode;
        public Keys CapturedKey { get; private set; }
        public bool CapturedExtended { get; private set; }
        public bool CapturedCtrl { get; private set; }
        public bool CapturedAlt { get; private set; }
        public bool CapturedShift { get; private set; }
        public bool CapturedWin { get; private set; }

        public HotkeyCaptureDialog(HotkeyHook hook, bool darkMode)
        {
            _hook = hook;
            _darkMode = darkMode;
            SuspendLayout();
            Text = "Set HotKey";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(310, 115);
            BackColor = darkMode ? Color.FromArgb(26, 28, 34) : Color.FromArgb(248, 248, 248);
            ForeColor = darkMode ? Color.FromArgb(236, 239, 244) : Color.FromArgb(30, 34, 40);

            var lbl = new Label
            {
                Text = "Press any key or key combination...\n(Supports Num Enter, F-keys, Ctrl/Alt/Shift combos)",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 70,
                ForeColor = ForeColor,
                Font = new Font("Segoe UI", 9.5f)
            };

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Width = 85,
                Height = 28,
                Location = new Point(112, 76),
                FlatStyle = FlatStyle.Flat,
                BackColor = darkMode ? Color.FromArgb(44, 48, 58) : Color.FromArgb(240, 240, 240),
                ForeColor = ForeColor
            };
            btnCancel.FlatAppearance.BorderColor = darkMode ? Color.FromArgb(68, 74, 88) : Color.FromArgb(190, 195, 204);

            Controls.Add(lbl);
            Controls.Add(btnCancel);
            CancelButton = btnCancel;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WindowHelper.SetWindowDarkMode(Handle, _darkMode);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _hook.CaptureCallback = (key, ext, ctrl, alt, shift, win) =>
            {
                _hook.CaptureCallback = null;
                CapturedKey = key;
                CapturedExtended = ext;
                CapturedCtrl = ctrl;
                CapturedAlt = alt;
                CapturedShift = shift;
                CapturedWin = win;
                DialogResult = DialogResult.OK;
                Close();
            };
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _hook.CaptureCallback = null;
            base.OnFormClosed(e);
        }
    }
}
