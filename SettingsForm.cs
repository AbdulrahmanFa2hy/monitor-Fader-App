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
        private readonly TitleBarButtonManager _titleBarManager;
        private readonly TaskbarManager _taskbarManager;
        private readonly HotkeyHook _hotkeyHook;

        // Sidebar navigation
        private Panel _sidebarPanel;
        private Panel _sepV;
        private Panel _sepH;
        private Button _navFading;
        private Button _navTitleBar;
        private Button _navTaskbar;
        private ThemedComboBox _cmbTheme;
        private Panel _pageFading;
        private Panel _pageTitleBar;
        private Panel _pageTaskbar;

        // Monitor Fading controls
        private CheckBox _chkEnable;
        private CheckBox _chkExcludeFullscreen;
        private ThemedComboBox _cmbMode;
        private TextBox _txtHotkey;
        private Button _btnSetHotkey;
        private Button _btnClearHotkey;
        private Panel _pnlColor;
        private Button _btnPickColor;
        private NumericUpDown _numFadeTime;
        private OpacitySlider _opacitySlider;
        private MonitorLayoutControl _monitorLayout;

        // TitleBar Buttons controls
        private CheckBox _chkEnableTitleBarBtn;
        private Label _lblTitleBarDesc;

        // Taskbar controls
        private CheckBox _chkEnableTaskbar;
        private ThemedComboBox _cmbTaskbarMode;
        private ThemedComboBox _cmbMiddleClick;
        private ThemedComboBox _cmbPreviewStyle;
        private CheckBox _chkTaskbarOverFullscreen;
        private CheckBox _chkTaskbarDragFocus;
        private OpacitySlider _taskbarOpacitySlider;

        // Footer
        private CheckBox _chkAutoStart;
        private Button _btnMinimize;
        private Button _btnExit;

        private NotifyIcon _trayIcon;
        private ToolStripMenuItem _trayEnableItem;
        private ToolStripMenuItem _trayTaskbarItem;
        private ToolStripMenuItem _trayTitleBarItem;
        private Icon _appIcon;
        private bool _forceExit;
        private bool _updatingUi;
        private int _currentSection;
        private bool _isDarkMode;

        public bool IsDropdownOpen
        {
            get
            {
                return (_cmbMode != null && _cmbMode.DroppedDown)
                    || (_cmbTaskbarMode != null && _cmbTaskbarMode.DroppedDown)
                    || (_cmbMiddleClick != null && _cmbMiddleClick.DroppedDown)
                    || (_cmbPreviewStyle != null && _cmbPreviewStyle.DroppedDown)
                    || (_cmbTheme != null && _cmbTheme.DroppedDown);
            }
        }

        public SettingsForm(AppSettings settings, bool startMinimized)
        {
            _settings = settings;
            _appIcon = CreateAppIcon();

            if (!_settings.AutoStartInitialized)
            {
                AppSettings.SetAutoStart(true);
                _settings.AutoStartInitialized = true;
                _settings.Save();
            }

            SuspendLayout();
            Text = "Monitor Fader";
            Icon = _appIcon;
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(760, 560);
            BackColor = Color.FromArgb(248, 248, 248);

            BuildUi();
            AutoScaleMode = AutoScaleMode.None;
            ResumeLayout(false);
            PerformLayout();

            SetupTray();

            _overlayManager = new OverlayManager(_settings, () => this);
            _titleBarManager = new TitleBarButtonManager(_settings, hMon => _overlayManager.IsMonitorCurrentlyFaded(hMon));
            _taskbarManager = new TaskbarManager(_settings, () => ShowFromTray());

            _overlayManager.MonitorsChanged += () =>
            {
                if (!IsDisposed)
                {
                    BeginInvoke(new Action(() =>
                    {
                        RefreshMonitorLayout();
                        _taskbarManager.RefreshMonitors();
                        _titleBarManager.UpdateButtons();
                    }));
                }
            };

            _hotkeyHook = new HotkeyHook(_settings, this);
            _hotkeyHook.HotkeyPressed += ToggleFadingFromHotkey;

            LoadSettingsIntoUi();
            RefreshMonitorLayout();
            ApplyTheme();
            _overlayManager.ApplySettingsNow();
            _titleBarManager.ApplySettings();
            _taskbarManager.ApplySettings();
            SelectSection(0);

            if (startMinimized)
            {
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
            }
        }

        private void BuildUi()
        {
            int sidebarW = 156;
            int footerH = 46;
            int contentLeft = sidebarW + 1;
            int contentW = ClientSize.Width - contentLeft;
            int contentH = ClientSize.Height - footerH;

            _sidebarPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(sidebarW, contentH),
                BackColor = Color.White
            };
            Controls.Add(_sidebarPanel);

            _sepV = new Panel
            {
                Location = new Point(sidebarW, 0),
                Size = new Size(1, contentH),
                BackColor = Color.FromArgb(220, 220, 220)
            };
            Controls.Add(_sepV);

            _navFading = CreateNavButton("Monitor Fading", 10);
            _navFading.Click += (s, e) => SelectSection(0);
            _sidebarPanel.Controls.Add(_navFading);

            _navTitleBar = CreateNavButton("TitleBar Buttons", 48);
            _navTitleBar.Click += (s, e) => SelectSection(1);
            _sidebarPanel.Controls.Add(_navTitleBar);

            _navTaskbar = CreateNavButton("Taskbar", 86);
            _navTaskbar.Click += (s, e) => SelectSection(2);
            _sidebarPanel.Controls.Add(_navTaskbar);

            _sidebarPanel.Controls.Add(new Label
            {
                Text = "Theme:",
                AutoSize = true,
                Location = new Point(10, contentH - 54)
            });

            _cmbTheme = new ThemedComboBox
            {
                Location = new Point(10, contentH - 34),
                Width = sidebarW - 20
            };
            _cmbTheme.Items.Add("Auto (System)");
            _cmbTheme.Items.Add("Dark");
            _cmbTheme.Items.Add("Light");
            _cmbTheme.SelectedIndexChanged += (s, e) =>
            {
                if (_updatingUi) return;
                _settings.ThemeMode = _cmbTheme.SelectedIndex;
                _settings.Save();
                ApplyTheme();
            };
            _sidebarPanel.Controls.Add(_cmbTheme);

            _pageFading = new Panel
            {
                Location = new Point(contentLeft, 0),
                Size = new Size(contentW, contentH),
                BackColor = Color.FromArgb(248, 248, 248)
            };
            BuildFadingPage(_pageFading);
            Controls.Add(_pageFading);

            _pageTitleBar = new Panel
            {
                Location = new Point(contentLeft, 0),
                Size = new Size(contentW, contentH),
                BackColor = Color.FromArgb(248, 248, 248)
            };
            BuildTitleBarPage(_pageTitleBar);
            Controls.Add(_pageTitleBar);

            _pageTaskbar = new Panel
            {
                Location = new Point(contentLeft, 0),
                Size = new Size(contentW, contentH),
                BackColor = Color.FromArgb(248, 248, 248)
            };
            BuildTaskbarPage(_pageTaskbar);
            Controls.Add(_pageTaskbar);

            _sepH = new Panel
            {
                Location = new Point(0, contentH),
                Size = new Size(ClientSize.Width, 1),
                BackColor = Color.FromArgb(220, 220, 220)
            };
            Controls.Add(_sepH);

            int footerY = contentH + 9;
            _chkAutoStart = new CheckBox
            {
                Text = "Start with Windows",
                AutoSize = true,
                Location = new Point(16, footerY + 4)
            };
            _chkAutoStart.CheckedChanged += (s, e) =>
            {
                if (_updatingUi) return;
                AppSettings.SetAutoStart(_chkAutoStart.Checked);
            };
            Controls.Add(_chkAutoStart);

            _btnMinimize = new Button
            {
                Text = "Hide to Tray",
                Size = new Size(100, 28),
                Location = new Point(ClientSize.Width - 204, footerY)
            };
            _btnMinimize.Click += (s, e) => HideToTray();
            Controls.Add(_btnMinimize);

            _btnExit = new Button
            {
                Text = "Exit",
                Size = new Size(80, 28),
                Location = new Point(ClientSize.Width - 96, footerY)
            };
            _btnExit.Click += (s, e) => ExitApplication();
            Controls.Add(_btnExit);
        }

        private Button CreateNavButton(string text, int y)
        {
            var btn = new Button
            {
                Text = "  " + text,
                TextAlign = ContentAlignment.MiddleLeft,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(6, y),
                Size = new Size(144, 34),
                Cursor = Cursors.Hand,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(40, 40, 40)
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private void SelectSection(int idx)
        {
            _currentSection = idx;
            _pageFading.Visible = (idx == 0);
            _pageTitleBar.Visible = (idx == 1);
            _pageTaskbar.Visible = (idx == 2);

            StyleNavButton(_navFading, idx == 0);
            StyleNavButton(_navTitleBar, idx == 1);
            StyleNavButton(_navTaskbar, idx == 2);
        }

        private void StyleNavButton(Button btn, bool active)
        {
            if (_isDarkMode)
            {
                btn.BackColor = active ? Color.FromArgb(42, 54, 72) : Color.FromArgb(30, 32, 38);
                btn.ForeColor = active ? Color.FromArgb(108, 186, 255) : Color.FromArgb(215, 220, 228);
            }
            else
            {
                btn.BackColor = active ? Color.FromArgb(229, 241, 251) : Color.White;
                btn.ForeColor = active ? Color.FromArgb(16, 85, 165) : Color.FromArgb(45, 45, 45);
            }
            btn.Font = new Font("Segoe UI", 9f, active ? FontStyle.Bold : FontStyle.Regular);
        }

        private bool ResolveIsDarkMode()
        {
            if (_settings.ThemeMode == (int)AppThemeMode.Dark) return true;
            if (_settings.ThemeMode == (int)AppThemeMode.Light) return false;
            return WindowHelper.IsAppsDarkMode();
        }

        private void ApplyTheme()
        {
            _isDarkMode = ResolveIsDarkMode();

            Color formBg = _isDarkMode ? Color.FromArgb(30, 32, 38) : Color.FromArgb(248, 248, 248);
            Color contentBg = _isDarkMode ? Color.FromArgb(24, 26, 31) : Color.FromArgb(248, 248, 248);
            Color sidebarBg = _isDarkMode ? Color.FromArgb(30, 32, 38) : Color.White;
            Color sepCol = _isDarkMode ? Color.FromArgb(48, 52, 62) : Color.FromArgb(220, 220, 220);
            Color textCol = _isDarkMode ? Color.FromArgb(236, 239, 244) : Color.FromArgb(30, 34, 40);
            Color mutedTextCol = _isDarkMode ? Color.FromArgb(168, 176, 190) : Color.FromArgb(70, 70, 70);
            Color inputBg = _isDarkMode ? Color.FromArgb(37, 40, 48) : Color.White;
            Color btnBg = _isDarkMode ? Color.FromArgb(44, 48, 58) : Color.FromArgb(240, 240, 240);
            Color btnBorder = _isDarkMode ? Color.FromArgb(64, 70, 84) : Color.FromArgb(190, 195, 204);

            BackColor = formBg;
            ForeColor = textCol;
            _sidebarPanel.BackColor = sidebarBg;
            _sepV.BackColor = sepCol;
            _sepH.BackColor = sepCol;

            _pageFading.BackColor = contentBg;
            _pageTitleBar.BackColor = contentBg;
            _pageTaskbar.BackColor = contentBg;

            ApplyThemeRecursive(this, textCol, mutedTextCol, inputBg, btnBg, btnBorder);

            _opacitySlider.DarkMode = _isDarkMode;
            _taskbarOpacitySlider.DarkMode = _isDarkMode;
            SelectSection(_currentSection);

            if (IsHandleCreated)
                WindowHelper.SetWindowDarkMode(Handle, _isDarkMode);

            if (_trayIcon != null && _trayIcon.ContextMenuStrip != null)
            {
                var menu = _trayIcon.ContextMenuStrip;
                if (_isDarkMode)
                {
                    menu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColorTable());
                    menu.ForeColor = textCol;
                    foreach (ToolStripItem item in menu.Items)
                        item.ForeColor = textCol;
                }
                else
                {
                    menu.Renderer = new ToolStripProfessionalRenderer();
                    menu.ForeColor = SystemColors.ControlText;
                    foreach (ToolStripItem item in menu.Items)
                        item.ForeColor = SystemColors.ControlText;
                }
            }
        }

        private void ApplyThemeRecursive(Control parent, Color textCol, Color mutedTextCol, Color inputBg, Color btnBg, Color btnBorder)
        {
            foreach (Control c in parent.Controls)
            {
                if (c == _navFading || c == _navTitleBar || c == _navTaskbar || c == _pnlColor)
                    continue;

                var combo = c as ThemedComboBox;
                if (combo != null)
                {
                    combo.DarkMode = _isDarkMode;
                    continue;
                }

                var btn = c as Button;
                if (btn != null)
                {
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.BackColor = btnBg;
                    btn.ForeColor = textCol;
                    btn.FlatAppearance.BorderColor = btnBorder;
                    btn.FlatAppearance.BorderSize = 1;
                    continue;
                }

                if (c is TextBox || c is NumericUpDown)
                {
                    c.BackColor = inputBg;
                    c.ForeColor = textCol;
                    continue;
                }

                if (c == _lblTitleBarDesc)
                {
                    c.ForeColor = mutedTextCol;
                    continue;
                }

                if (c is Label || c is CheckBox)
                {
                    c.ForeColor = textCol;
                }

                if (c.HasChildren)
                    ApplyThemeRecursive(c, textCol, mutedTextCol, inputBg, btnBg, btnBorder);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WindowHelper.SetWindowDarkMode(Handle, ResolveIsDarkMode());
        }

        private void BuildFadingPage(Panel p)
        {
            int left = 18;
            int width = p.Width - left * 2;
            int y = 14;

            _chkEnable = new CheckBox
            {
                Text = "Enable Monitor Fading",
                AutoSize = true,
                Location = new Point(left, y)
            };
            _chkEnable.CheckedChanged += OnEnableChanged;
            p.Controls.Add(_chkEnable);
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
            p.Controls.Add(_chkExcludeFullscreen);
            y += 30;

            p.Controls.Add(new Label
            {
                Text = "Monitor Fading Mode:",
                AutoSize = true,
                Location = new Point(left, y)
            });
            y += 20;

            _cmbMode = new ThemedComboBox
            {
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
            p.Controls.Add(_cmbMode);
            y += 34;

            p.Controls.Add(new Label
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
            p.Controls.Add(_txtHotkey);

            _btnSetHotkey = new Button
            {
                Text = "Set HotKey",
                Location = new Point(left + 183, y - 1),
                Size = new Size(88, 25)
            };
            _btnSetHotkey.Click += OnSetHotkeyClick;
            p.Controls.Add(_btnSetHotkey);

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
            p.Controls.Add(_btnClearHotkey);
            y += 34;

            p.Controls.Add(new Label
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
            p.Controls.Add(_pnlColor);

            _btnPickColor = new Button
            {
                Text = "Choose Color...",
                Location = new Point(left + 62, y - 1),
                Size = new Size(105, 25)
            };
            _btnPickColor.Click += OnPickColorClick;
            p.Controls.Add(_btnPickColor);
            y += 34;

            p.Controls.Add(new Label
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
            p.Controls.Add(_numFadeTime);
            y += 32;

            _opacitySlider = new OpacitySlider
            {
                Location = new Point(left, y),
                Size = new Size(width, 82),
                LabelPrefix = "Opacity"
            };
            _opacitySlider.ValueChanged += (s, e) =>
            {
                if (_updatingUi) return;
                _settings.OpacityPercent = _opacitySlider.Value;
                _settings.Save();
                UpdateTrayTooltip();
                _overlayManager.ApplySettingsNow();
            };
            p.Controls.Add(_opacitySlider);
            y += 92;

            p.Controls.Add(new Label
            {
                Text = "Excluded Monitors (click a monitor to exclude/include):",
                AutoSize = true,
                Location = new Point(left, y)
            });
            y += 20;

            _monitorLayout = new MonitorLayoutControl
            {
                Location = new Point(left, y),
                Size = new Size(width, 122)
            };
            _monitorLayout.MonitorToggled += OnMonitorExclusionToggled;
            p.Controls.Add(_monitorLayout);
        }

        private void BuildTitleBarPage(Panel p)
        {
            int left = 18;
            int width = p.Width - left * 2;
            int y = 18;

            _chkEnableTitleBarBtn = new CheckBox
            {
                Text = "Enable TitleBar \"Move Window to Next Monitor\" Button",
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(left, y)
            };
            _chkEnableTitleBarBtn.CheckedChanged += (s, e) =>
            {
                if (_updatingUi) return;
                SetTitleBarEnabled(_chkEnableTitleBarBtn.Checked);
            };
            p.Controls.Add(_chkEnableTitleBarBtn);
            y += 34;

            _lblTitleBarDesc = new Label
            {
                Location = new Point(left + 4, y),
                Size = new Size(width - 8, 120),
                ForeColor = Color.FromArgb(70, 70, 70),
                Text = "Adds a quick monitor-switch button next to the Minimize button on the title bar of open applications.\n\n"
                     + "• Left-click: Moves the window to the next monitor (preserving maximized state or proportional size).\n"
                     + "• Middle-click / Right-click / Shift + Left-click: Moves the window to the previous monitor.\n"
                     + "• Appears automatically when 2 or more monitors are connected."
            };
            p.Controls.Add(_lblTitleBarDesc);
        }

        private void BuildTaskbarPage(Panel p)
        {
            int left = 18;
            int width = p.Width - left * 2;
            int y = 14;

            _chkEnableTaskbar = new CheckBox
            {
                Text = "Enable Multi-Monitor Taskbars (show a taskbar on each monitor)",
                AutoSize = true,
                Location = new Point(left, y)
            };
            _chkEnableTaskbar.CheckedChanged += (s, e) =>
            {
                if (_updatingUi) return;
                SetTaskbarEnabled(_chkEnableTaskbar.Checked);
            };
            p.Controls.Add(_chkEnableTaskbar);
            y += 30;

            p.Controls.Add(new Label
            {
                Text = "Taskbar Mode:",
                AutoSize = true,
                Location = new Point(left, y)
            });
            y += 20;

            _cmbTaskbarMode = new ThemedComboBox
            {
                Location = new Point(left, y),
                Width = width
            };
            _cmbTaskbarMode.Items.Add("All taskbars show relevant windows");
            _cmbTaskbarMode.Items.Add("All taskbars show all windows");
            _cmbTaskbarMode.SelectedIndexChanged += (s, e) =>
            {
                if (_updatingUi) return;
                _settings.TaskbarMode = _cmbTaskbarMode.SelectedIndex;
                _settings.Save();
            };
            p.Controls.Add(_cmbTaskbarMode);
            y += 34;

            p.Controls.Add(new Label
            {
                Text = "Taskbar Button Middle-Click (or Shift + Left-Click) Behavior:",
                AutoSize = true,
                Location = new Point(left, y)
            });
            y += 20;

            _cmbMiddleClick = new ThemedComboBox
            {
                Location = new Point(left, y),
                Width = width
            };
            _cmbMiddleClick.Items.Add("Open a new instance");
            _cmbMiddleClick.Items.Add("Close window");
            _cmbMiddleClick.Items.Add("Do nothing");
            _cmbMiddleClick.SelectedIndexChanged += (s, e) =>
            {
                if (_updatingUi) return;
                _settings.TaskbarMiddleClickBehavior = _cmbMiddleClick.SelectedIndex;
                _settings.Save();
            };
            p.Controls.Add(_cmbMiddleClick);
            y += 34;

            p.Controls.Add(new Label
            {
                Text = "Window Preview Style:",
                AutoSize = true,
                Location = new Point(left, y)
            });
            y += 20;

            _cmbPreviewStyle = new ThemedComboBox
            {
                Location = new Point(left, y),
                Width = width
            };
            _cmbPreviewStyle.Items.Add("Windows modern style");
            _cmbPreviewStyle.Items.Add("Text tooltip only");
            _cmbPreviewStyle.Items.Add("Off");
            _cmbPreviewStyle.SelectedIndexChanged += (s, e) =>
            {
                if (_updatingUi) return;
                _settings.TaskbarPreviewStyle = _cmbPreviewStyle.SelectedIndex;
                _settings.Save();
            };
            p.Controls.Add(_cmbPreviewStyle);
            y += 36;

            _chkTaskbarOverFullscreen = new CheckBox
            {
                Text = "Show taskbar in front of full screen applications when they lose focus",
                AutoSize = true,
                Location = new Point(left, y)
            };
            _chkTaskbarOverFullscreen.CheckedChanged += (s, e) =>
            {
                if (_updatingUi) return;
                _settings.TaskbarShowOverFullscreenOnFocusLoss = _chkTaskbarOverFullscreen.Checked;
                _settings.Save();
            };
            p.Controls.Add(_chkTaskbarOverFullscreen);
            y += 26;

            _chkTaskbarDragFocus = new CheckBox
            {
                Text = "Focus window when mouse is dragged over Taskbar button",
                AutoSize = true,
                Location = new Point(left, y)
            };
            _chkTaskbarDragFocus.CheckedChanged += (s, e) =>
            {
                if (_updatingUi) return;
                _settings.TaskbarFocusOnDragHover = _chkTaskbarDragFocus.Checked;
                _settings.Save();
            };
            p.Controls.Add(_chkTaskbarDragFocus);
            y += 34;

            _taskbarOpacitySlider = new OpacitySlider
            {
                Location = new Point(left, y),
                Size = new Size(width, 82),
                LabelPrefix = "Taskbar Opacity"
            };
            _taskbarOpacitySlider.ValueChanged += (s, e) =>
            {
                if (_updatingUi) return;
                _settings.TaskbarOpacityPercent = _taskbarOpacitySlider.Value;
                _settings.Save();
            };
            p.Controls.Add(_taskbarOpacitySlider);
        }

        private void SetupTray()
        {
            var menu = new ContextMenuStrip();

            _trayEnableItem = new ToolStripMenuItem("Enable Monitor Fading");
            _trayEnableItem.Click += (s, e) => SetEnabled(!_settings.Enabled);

            _trayTitleBarItem = new ToolStripMenuItem("Enable TitleBar Button");
            _trayTitleBarItem.Click += (s, e) => SetTitleBarEnabled(!_settings.EnableTitleBarButton);

            _trayTaskbarItem = new ToolStripMenuItem("Enable Multi-Monitor Taskbars");
            _trayTaskbarItem.Click += (s, e) => SetTaskbarEnabled(!_settings.EnableMultiMonitorTaskbars);

            var openItem = new ToolStripMenuItem("Settings...");
            openItem.Font = new Font(openItem.Font, FontStyle.Bold);
            openItem.Click += (s, e) => ShowFromTray();

            var refreshItem = new ToolStripMenuItem("Refresh Monitors");
            refreshItem.Click += (s, e) =>
            {
                _overlayManager.RefreshMonitors();
                _taskbarManager.RefreshMonitors();
                _titleBarManager.UpdateButtons();
                RefreshMonitorLayout();
            };

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => ExitApplication();

            menu.Items.Add(_trayEnableItem);
            menu.Items.Add(_trayTitleBarItem);
            menu.Items.Add(_trayTaskbarItem);
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

                _chkEnableTitleBarBtn.Checked = _settings.EnableTitleBarButton;
                _trayTitleBarItem.Checked = _settings.EnableTitleBarButton;

                _chkEnableTaskbar.Checked = _settings.EnableMultiMonitorTaskbars;
                _trayTaskbarItem.Checked = _settings.EnableMultiMonitorTaskbars;

                int tbMode = _settings.TaskbarMode;
                if (tbMode < 0 || tbMode >= _cmbTaskbarMode.Items.Count) tbMode = 0;
                _cmbTaskbarMode.SelectedIndex = tbMode;

                int midMode = _settings.TaskbarMiddleClickBehavior;
                if (midMode < 0 || midMode >= _cmbMiddleClick.Items.Count) midMode = 0;
                _cmbMiddleClick.SelectedIndex = midMode;

                int prevMode = _settings.TaskbarPreviewStyle;
                if (prevMode < 0 || prevMode >= _cmbPreviewStyle.Items.Count) prevMode = 0;
                _cmbPreviewStyle.SelectedIndex = prevMode;

                int themeIdx = _settings.ThemeMode;
                if (themeIdx < 0 || themeIdx >= _cmbTheme.Items.Count) themeIdx = 0;
                _cmbTheme.SelectedIndex = themeIdx;

                _chkTaskbarOverFullscreen.Checked = _settings.TaskbarShowOverFullscreenOnFocusLoss;
                _chkTaskbarDragFocus.Checked = _settings.TaskbarFocusOnDragHover;
                _taskbarOpacitySlider.Value = _settings.TaskbarOpacityPercent;

                _chkAutoStart.Checked = AppSettings.IsAutoStartEnabled();
                UpdateTrayTooltip();
                UpdateTaskbarControlsEnabled();
            }
            finally
            {
                _updatingUi = false;
            }
        }

        private void UpdateTaskbarControlsEnabled()
        {
            bool on = _settings.EnableMultiMonitorTaskbars;
            _cmbTaskbarMode.Enabled = on;
            _cmbMiddleClick.Enabled = on;
            _cmbPreviewStyle.Enabled = on;
            _chkTaskbarOverFullscreen.Enabled = on;
            _chkTaskbarDragFocus.Enabled = on;
            _taskbarOpacitySlider.Enabled = on;
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

        private void SetTitleBarEnabled(bool enabled)
        {
            _settings.EnableTitleBarButton = enabled;
            _settings.Save();

            _updatingUi = true;
            _chkEnableTitleBarBtn.Checked = enabled;
            _trayTitleBarItem.Checked = enabled;
            _updatingUi = false;

            _titleBarManager.ApplySettings();
        }

        private void SetTaskbarEnabled(bool enabled)
        {
            _settings.EnableMultiMonitorTaskbars = enabled;
            _settings.Save();

            _updatingUi = true;
            _chkEnableTaskbar.Checked = enabled;
            _trayTaskbarItem.Checked = enabled;
            UpdateTaskbarControlsEnabled();
            _updatingUi = false;

            _taskbarManager.ApplySettings();
        }

        private void UpdateTrayTooltip()
        {
            if (_trayIcon != null)
            {
                string status = _settings.Enabled ? "Fading On (" + _settings.OpacityPercent + "%)" : "Fading Off";
                _trayIcon.Text = "Monitor Fader - " + status;
            }
        }

        private void OnSetHotkeyClick(object sender, EventArgs e)
        {
            using (var dlg = new HotkeyCaptureDialog(_hotkeyHook, _isDarkMode))
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
                if (_titleBarManager != null) _titleBarManager.Dispose();
                if (_taskbarManager != null) _taskbarManager.Dispose();
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

                using (var dimBrush = new SolidBrush(Color.FromArgb(40, 44, 52)))
                using (var dimPen = new Pen(Color.FromArgb(120, 130, 145), 1.5f))
                {
                    g.FillRectangle(dimBrush, 2, 7, 16, 13);
                    g.DrawRectangle(dimPen, 2, 7, 16, 13);
                }

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
