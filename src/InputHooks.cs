using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KanjiPop
{
    public sealed class Hotkey
    {
        public bool Ctrl, Alt, Shift, Win;
        public int Vk;

        public bool IsPlainCopy => Ctrl && !Alt && !Shift && !Win && Vk == (int)Keys.C;

        public static Hotkey Parse(string spec)
        {
            var h = new Hotkey();
            foreach (var raw in spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (raw.ToLowerInvariant())
                {
                    case "ctrl": case "control": h.Ctrl = true; break;
                    case "alt": h.Alt = true; break;
                    case "shift": h.Shift = true; break;
                    case "win": case "windows": h.Win = true; break;
                    default:
                        if (Enum.TryParse<Keys>(raw, true, out var k)) h.Vk = (int)k;
                        else throw new FormatException($"Unknown key '{raw}' in hotkey '{spec}'");
                        break;
                }
            }
            if (h.Vk == 0) throw new FormatException($"No key in hotkey '{spec}'");
            return h;
        }
    }

    public sealed class TriggerInfo
    {
        /// <summary>The app must send Ctrl+C itself (hotkey is not Ctrl+C).</summary>
        public bool InjectCopy;
        public uint SeqBefore;
        public Hotkey Key = new();
    }

    /// <summary>Global low-level keyboard + mouse hooks. Must be created on a thread with a message loop.</summary>
    public sealed class InputHooks : IDisposable
    {
        readonly Native.HookProc _kbProc, _msProc;
        IntPtr _kb, _ms;
        readonly Hotkey _hk;
        readonly Config _cfg;
        bool _held, _enabled = true;
        public void SetEnabled(bool on) { _enabled = on; _lastTap = 0; }
        long _lastTap;
        uint _seqAtFirstTap;

        public Func<bool> PopupVisible = () => false;
        public event Action<TriggerInfo>? Triggered;
        public event Action? EscapePressed;
        public event Action<int, int>? MouseClicked;

        public InputHooks(Config cfg)
        {
            _cfg = cfg;
            _hk = Hotkey.Parse(cfg.Hotkey);
            _kbProc = KbCb;
            _msProc = MsCb;
            var mod = Native.GetModuleHandle(null);
            _kb = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _kbProc, mod, 0);
            _ms = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _msProc, mod, 0);
        }

        static bool IsModifierVk(int vk) => vk is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C;

        bool ModsMatch() =>
            Native.IsDown(0x11) == _hk.Ctrl && Native.IsDown(0x12) == _hk.Alt &&
            Native.IsDown(0x10) == _hk.Shift && (Native.IsDown(0x5B) || Native.IsDown(0x5C)) == _hk.Win;

        IntPtr KbCb(int n, IntPtr w, IntPtr l)
        {
            if (n >= 0)
            {
                var k = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(l);
                if (k.dwExtraInfo != Native.OwnMagic)
                {
                    int msg = w.ToInt32();
                    if (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN)
                    {
                        if (OnKeyDown((int)k.vkCode)) return (IntPtr)1;
                    }
                    else if ((msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP) && k.vkCode == _hk.Vk)
                        _held = false;
                }
            }
            return Native.CallNextHookEx(_kb, n, w, l);
        }

        /// <returns>true to swallow the key.</returns>
        bool OnKeyDown(int vk)
        {
            if (!_enabled) return false;
            if (IsModifierVk(vk)) return false;
            if (vk == (int)Keys.Escape)
            {
                if (PopupVisible()) EscapePressed?.Invoke();
                return false;
            }
            if (vk != _hk.Vk) { _lastTap = 0; return false; }
            if (_held) return !_hk.IsPlainCopy;      // auto-repeat
            _held = true;
            if (!ModsMatch()) { _lastTap = 0; return false; }

            bool swallow = !_hk.IsPlainCopy;
            long now = Environment.TickCount64;
            if (_cfg.DoubleTap)
            {
                if (_lastTap != 0 && now - _lastTap <= _cfg.DoubleTapMs)
                {
                    _lastTap = 0;
                    Fire(_seqAtFirstTap);
                }
                else
                {
                    _lastTap = now;
                    _seqAtFirstTap = Native.GetClipboardSequenceNumber();
                }
                return swallow && _lastTap == 0;
            }
            Fire(Native.GetClipboardSequenceNumber());
            return swallow;
        }

        void Fire(uint seqBefore) =>
            Triggered?.Invoke(new TriggerInfo { InjectCopy = !_hk.IsPlainCopy, SeqBefore = seqBefore, Key = _hk });

        IntPtr MsCb(int n, IntPtr w, IntPtr l)
        {
            if (n >= 0)
            {
                int msg = w.ToInt32();
                if (msg == Native.WM_LBUTTONDOWN || msg == Native.WM_RBUTTONDOWN || msg == Native.WM_MBUTTONDOWN)
                {
                    var m = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(l);
                    MouseClicked?.Invoke(m.x, m.y);
                }
            }
            return Native.CallNextHookEx(_ms, n, w, l);
        }

        /// <summary>Send Ctrl+C for hotkeys that are not Ctrl+C themselves.</summary>
        public static void InjectCopy(Hotkey hk)
        {
            var held = new System.Collections.Generic.List<int>();
            if (hk.Alt && Native.IsDown(0x12)) held.Add(0x12);
            if (hk.Shift && Native.IsDown(0x10)) held.Add(0x10);
            if (hk.Win && Native.IsDown(0x5B)) held.Add(0x5B);
            foreach (var m in held) Native.SendKeys((m, false));
            bool ctrlHeld = Native.IsDown(0x11);
            if (!ctrlHeld) Native.SendKeys((0x11, true));
            Native.SendKeys((0x43, true), (0x43, false));
            if (!ctrlHeld) Native.SendKeys((0x11, false));
            foreach (var m in held) Native.SendKeys((m, true));
        }

        public void Dispose()
        {
            if (_kb != IntPtr.Zero) Native.UnhookWindowsHookEx(_kb);
            if (_ms != IntPtr.Zero) Native.UnhookWindowsHookEx(_ms);
            _kb = _ms = IntPtr.Zero;
        }
    }
}
