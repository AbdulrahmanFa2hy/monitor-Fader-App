using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MonitorFader
{
    public enum FadeMode
    {
        FadeNonCurrentActiveWindow = 0,
        FadeNonCurrentMouseCursor = 1,
        FadeAllMonitors = 2
    }

    [DataContract]
    public class AppSettings
    {
        [DataMember] public bool Enabled { get; set; }
        [DataMember] public bool ExcludeFullscreen { get; set; }
        [DataMember] public int Mode { get; set; }
        [DataMember] public int HotkeyKeyCode { get; set; }
        [DataMember] public bool HotkeyExtended { get; set; }
        [DataMember] public bool HotkeyCtrl { get; set; }
        [DataMember] public bool HotkeyAlt { get; set; }
        [DataMember] public bool HotkeyShift { get; set; }
        [DataMember] public bool HotkeyWin { get; set; }
        [DataMember] public string FadeColorHex { get; set; }
        [DataMember] public int FadeTimeMs { get; set; }
        [DataMember] public int OpacityPercent { get; set; }
        [DataMember] public List<string> ExcludedMonitors { get; set; }
        [DataMember] public bool StartMinimized { get; set; }

        public AppSettings()
        {
            Enabled = false;
            ExcludeFullscreen = true;
            Mode = (int)FadeMode.FadeNonCurrentActiveWindow;
            HotkeyKeyCode = (int)Keys.Return;
            HotkeyExtended = true; // Num Enter by default, matching DisplayFusion screenshot
            HotkeyCtrl = false;
            HotkeyAlt = false;
            HotkeyShift = false;
            HotkeyWin = false;
            FadeColorHex = "#000000";
            FadeTimeMs = 500;
            OpacityPercent = 76;
            ExcludedMonitors = new List<string>();
            StartMinimized = false;
        }

        public FadeMode FadeModeEnum
        {
            get
            {
                if (Enum.IsDefined(typeof(FadeMode), Mode))
                    return (FadeMode)Mode;
                return FadeMode.FadeNonCurrentActiveWindow;
            }
            set { Mode = (int)value; }
        }

        public Color FadeColor
        {
            get
            {
                try
                {
                    return ColorTranslator.FromHtml(string.IsNullOrEmpty(FadeColorHex) ? "#000000" : FadeColorHex);
                }
                catch
                {
                    return Color.Black;
                }
            }
            set
            {
                FadeColorHex = string.Format("#{0:X2}{1:X2}{2:X2}", value.R, value.G, value.B);
            }
        }

        public string FormatHotkey()
        {
            if (HotkeyKeyCode == 0)
                return "None";

            var parts = new List<string>();
            if (HotkeyCtrl) parts.Add("Ctrl");
            if (HotkeyAlt) parts.Add("Alt");
            if (HotkeyShift) parts.Add("Shift");
            if (HotkeyWin) parts.Add("Win");

            Keys k = (Keys)HotkeyKeyCode;
            string keyName;
            if (k == Keys.Return && HotkeyExtended)
                keyName = "Num Enter";
            else if (k == Keys.Return)
                keyName = "Enter";
            else
                keyName = FormatKeyName(k);

            parts.Add(keyName);
            return string.Join(" + ", parts);
        }

        private static string FormatKeyName(Keys k)
        {
            switch (k)
            {
                case Keys.Oemtilde: return "`";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: return "\\";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.D0: case Keys.D1: case Keys.D2: case Keys.D3: case Keys.D4:
                case Keys.D5: case Keys.D6: case Keys.D7: case Keys.D8: case Keys.D9:
                    return ((int)k - (int)Keys.D0).ToString();
                case Keys.NumPad0: case Keys.NumPad1: case Keys.NumPad2: case Keys.NumPad3: case Keys.NumPad4:
                case Keys.NumPad5: case Keys.NumPad6: case Keys.NumPad7: case Keys.NumPad8: case Keys.NumPad9:
                    return "Num " + ((int)k - (int)Keys.NumPad0);
                case Keys.Multiply: return "Num *";
                case Keys.Add: return "Num +";
                case Keys.Subtract: return "Num -";
                case Keys.Decimal: return "Num .";
                case Keys.Divide: return "Num /";
                default: return k.ToString();
            }
        }

        private static string GetSettingsPath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MonitorFader");
            return Path.Combine(dir, "settings.json");
        }

        public static AppSettings Load()
        {
            try
            {
                string path = GetSettingsPath();
                if (!File.Exists(path))
                    return new AppSettings();

                byte[] bytes = File.ReadAllBytes(path);
                using (var ms = new MemoryStream(bytes))
                {
                    var ser = new DataContractJsonSerializer(typeof(AppSettings));
                    var loaded = ser.ReadObject(ms) as AppSettings;
                    if (loaded != null)
                    {
                        if (loaded.ExcludedMonitors == null)
                            loaded.ExcludedMonitors = new List<string>();
                        if (loaded.OpacityPercent < 1) loaded.OpacityPercent = 1;
                        if (loaded.OpacityPercent > 100) loaded.OpacityPercent = 100;
                        if (loaded.FadeTimeMs < 0) loaded.FadeTimeMs = 0;
                        if (loaded.FadeTimeMs > 10000) loaded.FadeTimeMs = 10000;
                        return loaded;
                    }
                }
            }
            catch
            {
                // Fall back to defaults on corrupt file
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                string path = GetSettingsPath();
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var ser = new DataContractJsonSerializer(typeof(AppSettings));
                using (var ms = new MemoryStream())
                {
                    ser.WriteObject(ms, this);
                    File.WriteAllBytes(path, ms.ToArray());
                }
            }
            catch
            {
                // Ignore save errors
            }
        }

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRunName = "MonitorFader";

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return false;
                    return key.GetValue(AppRunName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void SetAutoStart(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return;
                    if (enable)
                    {
                        string exePath = Application.ExecutablePath;
                        key.SetValue(AppRunName, "\"" + exePath + "\" --minimized");
                    }
                    else
                    {
                        key.DeleteValue(AppRunName, false);
                    }
                }
            }
            catch
            {
                // Ignore registry access issues
            }
        }
    }
}
