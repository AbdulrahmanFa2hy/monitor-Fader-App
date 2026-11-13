using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MonitorFader
{
    public sealed class HotkeyHook : IDisposable
    {
        private IntPtr _hookId = IntPtr.Zero;
        private readonly NativeMethods.LowLevelKeyboardProc _proc;
        private readonly Control _marshalControl;
        private readonly AppSettings _settings;
        private bool _hotkeyHeld;

        public event Action HotkeyPressed;
        public Action<Keys, bool, bool, bool, bool, bool> CaptureCallback { get; set; }

        public HotkeyHook(AppSettings settings, Control marshalControl)
        {
            _settings = settings;
            _marshalControl = marshalControl;
            _proc = HookCallback;
            Install();
        }

        private void Install()
        {
            using (var curProcess = Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                _hookId = NativeMethods.SetWindowsHookEx(
                    NativeMethods.WH_KEYBOARD_LL,
                    _proc,
                    NativeMethods.GetModuleHandle(curModule.ModuleName),
                    0);
            }
        }

        private static bool IsModifierKey(Keys k)
        {
            return k == Keys.LControlKey || k == Keys.RControlKey || k == Keys.ControlKey
                || k == Keys.LMenu || k == Keys.RMenu || k == Keys.Menu
                || k == Keys.LShiftKey || k == Keys.RShiftKey || k == Keys.ShiftKey
                || k == Keys.LWin || k == Keys.RWin;
        }

        private static bool IsKeyDown(int vKey)
        {
            return (NativeMethods.GetAsyncKeyState(vKey) & 0x8000) != 0;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                bool isKeyDown = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
                bool isKeyUp = msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP;

                var info = (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                Keys key = (Keys)info.vkCode;
                bool extended = (info.flags & NativeMethods.LLKHF_EXTENDED) != 0;

                if (CaptureCallback != null)
                {
                    if (isKeyDown && !IsModifierKey(key))
                    {
                        bool ctrl = IsKeyDown(NativeMethods.VK_CONTROL);
                        bool alt = IsKeyDown(NativeMethods.VK_MENU);
                        bool shift = IsKeyDown(NativeMethods.VK_SHIFT);
                        bool win = IsKeyDown(NativeMethods.VK_LWIN) || IsKeyDown(NativeMethods.VK_RWIN);

                        var cb = CaptureCallback;
                        if (_marshalControl != null && !_marshalControl.IsDisposed)
                        {
                            _marshalControl.BeginInvoke(new Action(() => cb(key, extended, ctrl, alt, shift, win)));
                        }
                        return (IntPtr)1;
                    }
                    return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                if (_settings.HotkeyKeyCode != 0 && (int)key == _settings.HotkeyKeyCode)
                {
                    // For Return/Enter, distinguish regular Enter vs Num Enter using LLKHF_EXTENDED
                    bool extMatch = (key != Keys.Return) || (extended == _settings.HotkeyExtended);
                    if (extMatch)
                    {
                        if (isKeyUp)
                        {
                            _hotkeyHeld = false;
                        }
                        else if (isKeyDown)
                        {
                            bool ctrl = IsKeyDown(NativeMethods.VK_CONTROL);
                            bool alt = IsKeyDown(NativeMethods.VK_MENU);
                            bool shift = IsKeyDown(NativeMethods.VK_SHIFT);
                            bool win = IsKeyDown(NativeMethods.VK_LWIN) || IsKeyDown(NativeMethods.VK_RWIN);

                            if (ctrl == _settings.HotkeyCtrl
                                && alt == _settings.HotkeyAlt
                                && shift == _settings.HotkeyShift
                                && win == _settings.HotkeyWin)
                            {
                                if (!_hotkeyHeld)
                                {
                                    _hotkeyHeld = true;
                                    if (_marshalControl != null && !_marshalControl.IsDisposed)
                                    {
                                        _marshalControl.BeginInvoke(new Action(() =>
                                        {
                                            var handler = HotkeyPressed;
                                            if (handler != null) handler();
                                        }));
                                    }
                                }
                                return (IntPtr)1;
                            }
                        }
                    }
                }
            }

            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }
    }
}
