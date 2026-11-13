using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MonitorFader
{
    public sealed class SettingsForm : Form
    {
        private readonly AppSettings _settings;
        private readonly OverlayManager _overlayManager;
        private readonly HotkeyHook _hotkeyHook;

        private CheckBox _chkEnable;
        private CheckBox _chkExcludeFullscreen;
        private ComboBox _cmbMode;
        private TextBox _txtHotkey;
        private Button _btnSetHotkey;
        private Button _btnClearHotkey;
        private Panel _pnlColor;
        private Button _btnPickColor;
        private NumericUpDown _numFadeTime;
        private OpacitySlider _opacitySlider;
        private MonitorLayoutControl _monitorLayout;
        private CheckBox _chkAutoStart;

        private NotifyIcon _trayIcon;
        private ToolStripMenuItem _trayEnableItem;
        private Icon _appIcon;
        private bool _forceExit;
        private bool _updatingUi;

        public bool IsDropdownOpen
        {
            get { return _cmbMode != null && _cmbMode.DroppedDown; }
        }

        public SettingsForm(AppSettings settings, bool startMinimized)
        {
            _settings = settings;
            _appIcon = CreateAppIcon();

            SuspendLayout();
            Text = "Monitor Fader";
            Icon = _appIcon;
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(604, 528);
            BackColor = Color.FromArgb(248, 248, 248);

            BuildUi();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();
            SetupTray();

            _overlayManager = new OverlayManager(_settings, () => this);
            _overlayManager.MonitorsChanged += () =>
            {
                if (!IsDisposed)
                    BeginInvoke(new Action(RefreshMonitorLayout));
            };

            _hotkeyHook = new HotkeyHook(_settings, this);
            _hotkeyHook.HotkeyPressed += ToggleFadingFromHotkey;

            LoadSettingsIntoUi();
            RefreshMonitorLayout();
            _overlayManager.ApplySettingsNow();

            if (startMinimized)
            {
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
            }
        }

        private void BuildUi()
        {
            int left = 18;
            int width = ClientSize.Width - left * 2;
            int y = 14;

            _chkEnable = new CheckBox
            {
                Text = "Enable Monitor Fading",
                AutoSize = true,
                Location = new Point(left, y)
            };
            _chkEnable.CheckedChanged += OnEnableChanged;
            Controls.Add(_chkEnable);
            y += 26;

            _chkExcludeFullscreen = new CheckBox
            {
                Text = "Exclude fullscreen windows from being faded",
                AutoSize = true,
                Location = new Point(left, y)
            };
            _chkExcludeFullscreen.CheckedChanged += (s, e) =>
            {
                if (_updatingUi) return;
                _settings.ExcludeFullscreen = _chkExcludeFullscreen.Checked;
                _settings.Save();
                _overlayManager.ApplySettingsNow();
            };
            Controls.Add(_chkExcludeFullscreen);
            y += 30;

            Controls.Add(new Label
            {
                Text = "Monitor Fading Mode:",
                AutoSize = true,
                Location = new Point(left, y)
            });
            y += 20;

            _cmbMode = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(left, y),
                Width = 360
            };
            _cmbMode.Items.Add("Fade Non-Current Monitors (Active Window)");
            _cmbMode.Items.Add("Fade Non-Current Monitors (Mouse Cursor)");
            _cmbMode.Items.Add("Fade All Monitors");
            _cmbMode.SelectedIndexChanged += (s, e) =>
            {
                if (_updatingUi) return;
                _settings.Mode = _cmbMode.SelectedIndex;
                _settings.Save();
                _overlayManager.ApplySettingsNow();
            };
            Controls.Add(_cmbMode);
            y += 34;

            Controls.Add(new Label
            {
                Text = "Toggle Monitor Fading:",
                AutoSize = true,
                Location = new Point(left, y)
            });
            y += 20;

            _txtHotkey = new TextBox
            {
                ReadOnly = true,
                BackColor = Color.White,
                Location = new Point(left, y),
                Width = 175
            };
            Controls.Add(_txtHotkey);

            _btnSetHotkey = new Button
            {
                Text = "Set HotKey",
                Location = new Point(left + 183, y - 1),
                Size = new Size(88, 25)
            };
            _btnSetHotkey.Click += OnSetHotkeyClick;
            Controls.Add(_btnSetHotkey);

            _btnClearHotkey = new Button
            {
                Text = "Clear",
                Location = new Point(left + 277, y - 1),
                Size = new Size(64, 25)
            };
            _btnClearHotkey.Click += (s, e) =>
            {
                _settings.HotkeyKeyCode = 0;
                _settings.HotkeyExtended = false;
                _settings.HotkeyCtrl = false;
                _settings.HotkeyAlt = false;
                _settings.HotkeyShift = false;
                _settings.HotkeyWin = false;
                _txtHotkey.Text = _settings.FormatHotkey();
                _settings.Save();
            };
            Controls.Add(_btnClearHotkey);
            y += 34;

            Controls.Add(new Label
            {
                Text = "Monitor Fading Color:",
                AutoSize = true,
                Location = new Point(left, y)
            });
            y += 20;

            _pnlColor = new Panel
            {
                Location = new Point(left, y),
                Size = new Size(54, 24),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _pnlColor.Click += OnPickColorClick;
            Controls.Add(_pnlColor);

            _btnPickColor = new Button
            {
                Text = "Choose Color...",
                Location = new Point(left + 62, y - 1),
                Size = new Size(105, 25)
            };
            _btnPickColor.Click += OnPickColorClick;
            Controls.Add(_btnPickColor);
            y += 34;

            Controls.Add(new Label
            {
                Text = "Fade Time (ms):",
                AutoSize = true,
                Location = new Point(left, y)
            });
            y += 20;

            _numFadeTime = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 10000,
                Increment = 50,
                Location = new Point(left, y),
                Width = 130
            };
            _numFadeTime.ValueChanged += (s, e) =>
            {
                if (_updatingUi) return;
                _settings.FadeTimeMs = (int)_numFadeTime.Value;
                _settings.Save();
            };
            Controls.Add(_numFadeTime);
            y += 32;

            _opacitySlider = new OpacitySlider
            {
                Location = new Point(left, y),
                Size = new Size(width, 82)
            };
            _opacitySlider.ValueChanged += (s, e) =>
            {
                if (_updatingUi) return;
                _settings.OpacityPercent = _opacitySlider.Value;
                _settings.Save();
                _overlayManager.ApplySettingsNow();
            };
            Controls.Add(_opacitySlider);
            y += 92;

            Controls.Add(new Label
            {
                Text = "Excluded Monitors (click a monitor to exclude/include):",
                AutoSize = true,
                Location = new Point(left, y)
            });
            y += 20;

            _monitorLayout = new MonitorLayoutControl
            {
                Location = new Point(left, y),
                Size = new Size(width, 118)
            };
            _monitorLayout.MonitorToggled += OnMonitorExclusionToggled;
            Controls.Add(_monitorLayout);
            y += 128;

            _chkAutoStart = new CheckBox
            {
                Text = "Start with Windows",
                AutoSize = true,
                Location = new Point(left, y + 4)
            };
            _chkAutoStart.CheckedChanged += (s, e) =>
            {
                if (_updatingUi) return;
                AppSettings.SetAutoStart(_chkAutoStart.Checked);
            };
            Controls.Add(_chkAutoStart);

            var btnMinimize = new Button
            {
                Text = "Hide to Tray",
                Size = new Size(100, 28),
                Location = new Point(ClientSize.Width - left - 188, y)
            };
            btnMinimize.Click += (s, e) => HideToTray();
            Controls.Add(btnMinimize);

            var btnExit = new Button
            {
                Text = "Exit",
                Size = new Size(80, 28),
                Location = new Point(ClientSize.Width - left - 80, y)
            };
            btnExit.Click += (s, e) => ExitApplication();
            Controls.Add(btnExit);
        }

        private void SetupTray()
        {
            var menu = new ContextMenuStrip();

            _trayEnableItem = new ToolStripMenuItem("Enable Monitor Fading");
            _trayEnableItem.CheckOnClick = false;
            _trayEnableItem.Click += (s, e) => SetEnabled(!_settings.Enabled);

            var openItem = new ToolStripMenuItem("Settings...");
            openItem.Font = new Font(openItem.Font, FontStyle.Bold);
            openItem.Click += (s, e) => ShowFromTray();

            var refreshItem = new ToolStripMenuItem("Refresh Monitors");
            refreshItem.Click += (s, e) =>
            {
                _overlayManager.RefreshMonitors();
                RefreshMonitorLayout();
            };

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => ExitApplication();

            menu.Items.Add(_trayEnableItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(openItem);
            menu.Items.Add(refreshItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            _trayIcon = new NotifyIcon
            {
                Icon = _appIcon,
                Text = "Monitor Fader",
                ContextMenuStrip = menu,
                Visible = true
            };
            _trayIcon.DoubleClick += (s, e) => ShowFromTray();
        }

        private void LoadSettingsIntoUi()
        {
            _updatingUi = true;
            try
            {
                _chkEnable.Checked = _settings.Enabled;
                _trayEnableItem.Checked = _settings.Enabled;
                _chkExcludeFullscreen.Checked = _settings.ExcludeFullscreen;

                int modeIdx = _settings.Mode;
                if (modeIdx < 0 || modeIdx >= _cmbMode.Items.Count) modeIdx = 0;
                _cmbMode.SelectedIndex = modeIdx;

                _txtHotkey.Text = _settings.FormatHotkey();
                _pnlColor.BackColor = _settings.FadeColor;
                _numFadeTime.Value = Math.Max(_numFadeTime.Minimum, Math.Min(_numFadeTime.Maximum, _settings.FadeTimeMs));
                _opacitySlider.Value = _settings.OpacityPercent;
                _chkAutoStart.Checked = AppSettings.IsAutoStartEnabled();
                UpdateTrayTooltip();
            }
            finally
            {
                _updatingUi = false;
            }
        }

        private void RefreshMonitorLayout()
        {
            _monitorLayout.SetData(_overlayManager.GetCurrentMonitors(), _settings.ExcludedMonitors);
        }

        private void OnEnableChanged(object sender, EventArgs e)
        {
            if (_updatingUi) return;
            SetEnabled(_chkEnable.Checked);
        }

        private void ToggleFadingFromHotkey()
        {
            SetEnabled(!_settings.Enabled);
        }

        private void SetEnabled(bool enabled)
        {
            _settings.Enabled = enabled;
            _settings.Save();

            _updatingUi = true;
            _chkEnable.Checked = enabled;
            _trayEnableItem.Checked = enabled;
            _updatingUi = false;

            UpdateTrayTooltip();
            _overlayManager.ApplySettingsNow();
        }

        private void UpdateTrayTooltip()
        {
            if (_trayIcon != null)
            {
                string status = _settings.Enabled ? "Active (" + _settings.OpacityPercent + "%)" : "Off";
                _trayIcon.Text = "Monitor Fader - " + status;
            }
        }

        private void OnSetHotkeyClick(object sender, EventArgs e)
        {
            using (var dlg = new HotkeyCaptureDialog(_hotkeyHook))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _settings.HotkeyKeyCode = (int)dlg.CapturedKey;
                    _settings.HotkeyExtended = dlg.CapturedExtended;
                    _settings.HotkeyCtrl = dlg.CapturedCtrl;
                    _settings.HotkeyAlt = dlg.CapturedAlt;
                    _settings.HotkeyShift = dlg.CapturedShift;
                    _settings.HotkeyWin = dlg.CapturedWin;
                    _txtHotkey.Text = _settings.FormatHotkey();
                    _settings.Save();
                }
            }
        }

        private void OnPickColorClick(object sender, EventArgs e)
        {
            using (var dlg = new ColorDialog())
            {
                dlg.FullOpen = true;
                dlg.Color = _settings.FadeColor;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _settings.FadeColor = dlg.Color;
                    _pnlColor.BackColor = dlg.Color;
                    _settings.Save();
                    _overlayManager.ApplySettingsNow();
                }
            }
        }

        private void OnMonitorExclusionToggled(string deviceName)
        {
            int idx = _settings.ExcludedMonitors.FindIndex(x => string.Equals(x, deviceName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                _settings.ExcludedMonitors.RemoveAt(idx);
            else
                _settings.ExcludedMonitors.Add(deviceName);

            _settings.Save();
            RefreshMonitorLayout();
            _overlayManager.ApplySettingsNow();
        }

        public void ShowFromTray()
        {
            Show();
            ShowInTaskbar = true;
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            RefreshMonitorLayout();
        }

        private void HideToTray()
        {
            Hide();
            ShowInTaskbar = false;
        }

        private void ExitApplication()
        {
            _forceExit = true;
            Close();
            Application.Exit();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!ShowInTaskbar && WindowState == FormWindowState.Minimized)
                Hide();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_forceExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_hotkeyHook != null) _hotkeyHook.Dispose();
                if (_overlayManager != null) _overlayManager.Dispose();
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }
                if (_appIcon != null) _appIcon.Dispose();
            }
            base.Dispose(disposing);
        }

        private static Icon CreateAppIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // Dimmed secondary monitor (left/back)
                using (var dimBrush = new SolidBrush(Color.FromArgb(40, 44, 52)))
                using (var dimPen = new Pen(Color.FromArgb(120, 130, 145), 1.5f))
                {
                    g.FillRectangle(dimBrush, 2, 7, 16, 13);
                    g.DrawRectangle(dimPen, 2, 7, 16, 13);
                }

                // Bright active monitor (right/front)
                using (var activeBrush = new SolidBrush(Color.FromArgb(56, 132, 244)))
                using (var activePen = new Pen(Color.White, 1.5f))
                using (var standBrush = new SolidBrush(Color.FromArgb(200, 210, 225)))
                {
                    g.FillRectangle(activeBrush, 11, 11, 19, 14);
                    g.DrawRectangle(activePen, 11, 11, 19, 14);
                    g.FillRectangle(standBrush, 18, 25, 5, 3);
                    g.FillRectangle(standBrush, 15, 28, 11, 2);
                }

                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (Icon tmp = Icon.FromHandle(hIcon))
                    {
                        return (Icon)tmp.Clone();
                    }
                }
                finally
                {
                    NativeMethods.DestroyIcon(hIcon);
                }
            }
        }
    }
}
