using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MonitorFader
{
    internal sealed class FadeOverlayForm : Form
    {
        public IntPtr MonitorHandle { get; set; }
        public string DeviceName { get; set; }
        public Rectangle PhysicalBounds { get; set; }
        public double CurrentOpacity { get; set; }
        public double TargetOpacity { get; set; }
        public bool IsShown { get; set; }

        public FadeOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = 0.01;
            DoubleBuffered = false;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WindowHelper.RegisterInternalHandle(Handle);
        }

        protected override void Dispose(bool disposing)
        {
            if (IsHandleCreated)
                WindowHelper.UnregisterInternalHandle(Handle);
            base.Dispose(disposing);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED
                            | NativeMethods.WS_EX_TRANSPARENT
                            | NativeMethods.WS_EX_TOOLWINDOW
                            | NativeMethods.WS_EX_NOACTIVATE
                            | NativeMethods.WS_EX_TOPMOST;
                return cp;
            }
        }

        public void EnsureHandleAndBounds(Rectangle bounds, bool show)
        {
            PhysicalBounds = bounds;
            if (!IsHandleCreated)
                CreateHandle();

            uint flags = NativeMethods.SWP_NOACTIVATE | (show ? NativeMethods.SWP_SHOWWINDOW : NativeMethods.SWP_HIDEWINDOW);
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height, flags);
            IsShown = show;
        }

        public void SetHidden()
        {
            if (IsHandleCreated && IsShown)
            {
                NativeMethods.SetWindowPos(
                    Handle,
                    NativeMethods.HWND_TOPMOST,
                    PhysicalBounds.X,
                    PhysicalBounds.Y,
                    PhysicalBounds.Width,
                    PhysicalBounds.Height,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_HIDEWINDOW);
                IsShown = false;
            }
        }
    }

    public sealed class OverlayManager : IDisposable
    {
        private readonly AppSettings _settings;
        private readonly Dictionary<string, FadeOverlayForm> _overlays = new Dictionary<string, FadeOverlayForm>(StringComparer.OrdinalIgnoreCase);
        private readonly Timer _pollTimer;
        private readonly Timer _animTimer;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private List<MonitorInfoItem> _monitors = new List<MonitorInfoItem>();
        private long _lastTickMs;
        private Func<Form> _settingsFormProvider;
        private bool _settingsWasTopmost;

        public event Action MonitorsChanged;

        public OverlayManager(AppSettings settings, Func<Form> settingsFormProvider)
        {
            _settings = settings;
            _settingsFormProvider = settingsFormProvider;

            _pollTimer = new Timer();
            _pollTimer.Interval = 100;
            _pollTimer.Tick += (s, e) => EvaluateTargets();

            _animTimer = new Timer();
            _animTimer.Interval = 15;
            _animTimer.Tick += (s, e) => StepAnimation();

            _stopwatch.Start();
            _lastTickMs = _stopwatch.ElapsedMilliseconds;

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            RefreshMonitors();
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            RefreshMonitors();
            var handler = MonitorsChanged;
            if (handler != null) handler();
        }

        public List<MonitorInfoItem> GetCurrentMonitors()
        {
            return new List<MonitorInfoItem>(_monitors);
        }

        public bool IsMonitorCurrentlyFaded(IntPtr hMonitor)
        {
            foreach (var overlay in _overlays.Values)
            {
                if (!overlay.IsDisposed && overlay.MonitorHandle == hMonitor && overlay.IsShown && overlay.TargetOpacity > 0.01)
                    return true;
            }
            return false;
        }

        public void RefreshMonitors()
        {
            _monitors = MonitorManager.GetMonitors();
            var activeDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var mon in _monitors)
            {
                activeDevices.Add(mon.DeviceName);
                FadeOverlayForm overlay;
                if (!_overlays.TryGetValue(mon.DeviceName, out overlay) || overlay.IsDisposed)
                {
                    overlay = new FadeOverlayForm();
                    overlay.MonitorHandle = mon.HMonitor;
                    overlay.DeviceName = mon.DeviceName;
                    overlay.BackColor = _settings.FadeColor;
                    overlay.EnsureHandleAndBounds(mon.Bounds, false);
                    _overlays[mon.DeviceName] = overlay;
                }
                else
                {
                    overlay.MonitorHandle = mon.HMonitor;
                    overlay.PhysicalBounds = mon.Bounds;
                    if (overlay.IsShown)
                        overlay.EnsureHandleAndBounds(mon.Bounds, true);
                }
            }

            var toRemove = new List<string>();
            foreach (var kv in _overlays)
            {
                if (!activeDevices.Contains(kv.Key))
                    toRemove.Add(kv.Key);
            }
            foreach (var key in toRemove)
            {
                _overlays[key].Dispose();
                _overlays.Remove(key);
            }

            ApplySettingsNow();
        }

        public void ApplySettingsNow()
        {
            Color c = _settings.FadeColor;
            foreach (var overlay in _overlays.Values)
            {
                if (!overlay.IsDisposed && overlay.BackColor != c)
                    overlay.BackColor = c;
            }

            if (_settings.Enabled)
            {
                if (!_pollTimer.Enabled)
                    _pollTimer.Start();
            }
            else
            {
                if (_pollTimer.Enabled)
                    _pollTimer.Stop();
            }

            EvaluateTargets();
        }

        private void EvaluateTargets()
        {
            if (_monitors.Count == 0)
                return;

            double desiredMaxOpacity = Math.Max(0.01, Math.Min(1.0, _settings.OpacityPercent / 100.0));
            IntPtr activeMonitor = IntPtr.Zero;
            HashSet<IntPtr> fullscreenMonitors = null;

            if (_settings.Enabled)
            {
                if (_settings.FadeModeEnum != FadeMode.FadeAllMonitors)
                    activeMonitor = MonitorManager.GetCurrentMonitorHandle(_settings.FadeModeEnum);

                if (_settings.ExcludeFullscreen)
                    fullscreenMonitors = MonitorManager.GetMonitorsWithFullscreenWindow(_monitors);
            }

            bool needsAnimation = false;
            var excluded = new HashSet<string>(_settings.ExcludedMonitors ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            foreach (var mon in _monitors)
            {
                FadeOverlayForm overlay;
                if (!_overlays.TryGetValue(mon.DeviceName, out overlay) || overlay.IsDisposed)
                    continue;

                bool shouldFade = _settings.Enabled
                    && !excluded.Contains(mon.DeviceName)
                    && (_settings.FadeModeEnum == FadeMode.FadeAllMonitors || mon.HMonitor != activeMonitor)
                    && (fullscreenMonitors == null || !fullscreenMonitors.Contains(mon.HMonitor));

                overlay.TargetOpacity = shouldFade ? desiredMaxOpacity : 0.0;

                if (Math.Abs(overlay.CurrentOpacity - overlay.TargetOpacity) > 0.001)
                    needsAnimation = true;
            }

            EnsureSettingsFormAboveOverlay();

            if (needsAnimation && !_animTimer.Enabled)
            {
                _lastTickMs = _stopwatch.ElapsedMilliseconds;
                _animTimer.Start();
                StepAnimation();
            }
        }

        private void StepAnimation()
        {
            long now = _stopwatch.ElapsedMilliseconds;
            double dtMs = Math.Max(1.0, now - _lastTickMs);
            _lastTickMs = now;

            int fadeTime = _settings.FadeTimeMs;
            double maxAlpha = Math.Max(0.01, Math.Min(1.0, _settings.OpacityPercent / 100.0));
            bool anyAnimating = false;

            foreach (var overlay in _overlays.Values)
            {
                if (overlay.IsDisposed) continue;

                double diff = overlay.TargetOpacity - overlay.CurrentOpacity;
                if (Math.Abs(diff) <= 0.002)
                {
                    overlay.CurrentOpacity = overlay.TargetOpacity;
                }
                else
                {
                    if (fadeTime <= 0)
                    {
                        overlay.CurrentOpacity = overlay.TargetOpacity;
                    }
                    else
                    {
                        double step = (dtMs / fadeTime) * maxAlpha;
                        if (step < 0.001) step = 0.001;
                        if (Math.Abs(diff) <= step)
                            overlay.CurrentOpacity = overlay.TargetOpacity;
                        else
                            overlay.CurrentOpacity += Math.Sign(diff) * step;
                        anyAnimating = true;
                    }
                }

                if (overlay.CurrentOpacity <= 0.002 && overlay.TargetOpacity <= 0.002)
                {
                    overlay.CurrentOpacity = 0.0;
                    overlay.SetHidden();
                }
                else
                {
                    if (!overlay.IsShown)
                    {
                        overlay.Opacity = Math.Max(0.01, overlay.CurrentOpacity);
                        overlay.EnsureHandleAndBounds(overlay.PhysicalBounds, true);
                    }
                    else
                    {
                        overlay.Opacity = Math.Max(0.01, Math.Min(1.0, overlay.CurrentOpacity));
                    }
                }
            }

            EnsureSettingsFormAboveOverlay();

            if (!anyAnimating)
                _animTimer.Stop();
        }

        private void EnsureSettingsFormAboveOverlay()
        {
            if (_settingsFormProvider == null) return;
            var form = _settingsFormProvider();
            if (form == null || form.IsDisposed || !form.Visible)
            {
                _settingsWasTopmost = false;
                return;
            }

            var settingsForm = form as SettingsForm;
            if (settingsForm != null && settingsForm.IsDropdownOpen)
                return;

            bool isActive = Form.ActiveForm == form || NativeMethods.GetForegroundWindow() == form.Handle;
            bool wantTopmost = _settings.Enabled && isActive;
            if (wantTopmost == _settingsWasTopmost)
                return;

            if (wantTopmost)
            {
                NativeMethods.SetWindowPos(
                    form.Handle,
                    NativeMethods.HWND_TOPMOST,
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                _settingsWasTopmost = true;
            }
            else
            {
                NativeMethods.SetWindowPos(
                    form.Handle,
                    new IntPtr(-2), // HWND_NOTOPMOST
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                _settingsWasTopmost = false;
            }
        }

        public void Dispose()
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _pollTimer.Stop();
            _pollTimer.Dispose();
            _animTimer.Stop();
            _animTimer.Dispose();

            foreach (var overlay in _overlays.Values)
            {
                if (!overlay.IsDisposed)
                    overlay.Dispose();
            }
            _overlays.Clear();
        }
    }
}
