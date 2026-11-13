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

        public event EventHandler ValueChanged;

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

            using (var borderPen = new Pen(Color.FromArgb(215, 215, 215)))
            {
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }

            // Track background line
            using (var trackPen = new Pen(Color.FromArgb(190, 190, 190), 2f * s))
            {
                g.DrawLine(trackPen, TrackLeft, TrackY, TrackRight, TrackY);
            }

            // Active fill up to thumb
            int thumbX = ValueToX(_value);
            using (var fillPen = new Pen(Color.FromArgb(60, 120, 216), 2.5f * s))
            {
                g.DrawLine(fillPen, TrackLeft, TrackY, thumbX, TrackY);
            }

            // Ticks and labels
            using (var minorPen = new Pen(Color.FromArgb(198, 198, 198), 1f))
            using (var majorPen = new Pen(Color.FromArgb(150, 150, 150), 1f))
            using (var labelFont = new Font("Segoe UI", 7.5f))
            using (var labelBrush = new SolidBrush(Color.FromArgb(115, 115, 115)))
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

            // Thumb circle
            int r = (int)Math.Round(7 * s);
            int ir = (int)Math.Round(3 * s);
            var thumbRect = new Rectangle(thumbX - r, TrackY - r, r * 2, r * 2);
            using (var outerBrush = new SolidBrush(Color.White))
            using (var outerPen = new Pen(Color.FromArgb(95, 95, 95), 1.5f * s))
            using (var innerBrush = new SolidBrush(Color.FromArgb(75, 75, 75)))
            {
                g.FillEllipse(outerBrush, thumbRect);
                g.DrawEllipse(outerPen, thumbRect);
                g.FillEllipse(innerBrush, thumbX - ir, TrackY - ir, ir * 2, ir * 2);
            }

            // Bottom-left opacity text
            using (var boldFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            using (var textBrush = new SolidBrush(Color.FromArgb(45, 45, 45)))
            {
                g.DrawString("Opacity: " + _value + "%", boldFont, textBrush, 8f * s, Height - 20f * s);
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

    public sealed class HotkeyCaptureDialog : Form
    {
        private readonly HotkeyHook _hook;
        public Keys CapturedKey { get; private set; }
        public bool CapturedExtended { get; private set; }
        public bool CapturedCtrl { get; private set; }
        public bool CapturedAlt { get; private set; }
        public bool CapturedShift { get; private set; }
        public bool CapturedWin { get; private set; }

        public HotkeyCaptureDialog(HotkeyHook hook)
        {
            _hook = hook;
            SuspendLayout();
            Text = "Set HotKey";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(310, 115);

            var lbl = new Label
            {
                Text = "Press any key or key combination...\n(Supports Num Enter, F-keys, Ctrl/Alt/Shift combos)",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 70,
                Font = new Font("Segoe UI", 9.5f)
            };

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Width = 85,
                Height = 28,
                Location = new Point(112, 76)
            };

            Controls.Add(lbl);
            Controls.Add(btnCancel);
            CancelButton = btnCancel;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();
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
