using System;
using System.Collections.Generic;

namespace KanjiPop
{
    /// <summary>A key combination such as "Cmd+C" or "Cmd+Shift+J" (letters, digits and Space; at least one modifier).</summary>
    public sealed class Hotkey
    {
        public bool Cmd, Ctrl, Alt, Shift;
        public int Code = -1;   // macOS virtual key code

        public bool IsPlainCopy => Cmd && !Ctrl && !Alt && !Shift && Code == 8;

        public ulong FlagMask =>
            (Cmd ? Mac.FlagCmd : 0) | (Ctrl ? Mac.FlagCtrl : 0) | (Alt ? Mac.FlagAlt : 0) | (Shift ? Mac.FlagShift : 0);

        const ulong AllMods = Mac.FlagCmd | Mac.FlagCtrl | Mac.FlagAlt | Mac.FlagShift;

        public bool ModsMatch(ulong flags) => (flags & AllMods) == FlagMask;

        // ANSI keyboard layout key codes
        static readonly Dictionary<string, int> Codes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = 0, ["S"] = 1, ["D"] = 2, ["F"] = 3, ["H"] = 4, ["G"] = 5, ["Z"] = 6, ["X"] = 7, ["C"] = 8, ["V"] = 9,
            ["B"] = 11, ["Q"] = 12, ["W"] = 13, ["E"] = 14, ["R"] = 15, ["Y"] = 16, ["T"] = 17,
            ["1"] = 18, ["2"] = 19, ["3"] = 20, ["4"] = 21, ["6"] = 22, ["5"] = 23, ["9"] = 25, ["7"] = 26, ["8"] = 28, ["0"] = 29,
            ["O"] = 31, ["U"] = 32, ["I"] = 34, ["P"] = 35, ["L"] = 37, ["J"] = 38, ["K"] = 40, ["N"] = 45, ["M"] = 46,
            ["Space"] = 49,
        };

        public static bool IsKeyName(string name) => Codes.ContainsKey(name);

        public static Hotkey Parse(string spec)
        {
            var h = new Hotkey();
            foreach (var raw in spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (raw.ToLowerInvariant())
                {
                    case "cmd": case "command": case "meta": case "win": case "windows": case "super": h.Cmd = true; break;
                    case "ctrl": case "control": h.Ctrl = true; break;
                    case "alt": case "option": case "opt": h.Alt = true; break;
                    case "shift": h.Shift = true; break;
                    default:
                        if (Codes.TryGetValue(raw, out var c)) h.Code = c;
                        else throw new FormatException($"Unknown key '{raw}' in hotkey '{spec}'");
                        break;
                }
            }
            if (h.Code < 0) throw new FormatException($"No key in hotkey '{spec}'");
            if (h.FlagMask == 0) throw new FormatException("A hotkey needs at least one modifier (Cmd, Ctrl, Option or Shift).");
            return h;
        }
    }

    public sealed class TriggerInfo
    {
        /// <summary>The app must post Cmd+C itself (hotkey is not Cmd+C).</summary>
        public bool InjectCopy;
        public long SeqBefore;
    }

    /// <summary>
    /// Global key/mouse listener built on a listen-only CGEventTap (needs the Input Monitoring permission).
    /// The tap is attached to the main run loop, so callbacks arrive on the UI thread.
    /// </summary>
    public sealed class HotkeyService : IDisposable
    {
        Config _cfg;
        Hotkey _hk;
        readonly Mac.TapCallback _callback; // keep a reference so the delegate is not collected
        IntPtr _tap;
        bool _enabled = true;
        long _lastTap;
        long _seqAtFirstTap;

        public Func<bool> PopupVisible = () => false;
        /// <summary>Return true to ignore the hotkey right now (paused app / paused globally).</summary>
        public Func<bool>? Paused;
        public event Action<TriggerInfo>? Triggered;
        public event Action? EscapePressed;
        /// <summary>Mouse click at (x, y) in points.</summary>
        public event Action<double, double>? MouseClicked;

        public bool IsRunning => _tap != IntPtr.Zero || !Mac.IsMac;

        public HotkeyService(Config cfg)
        {
            _cfg = cfg;
            _hk = Hotkey.Parse(cfg.Hotkey);
            _callback = OnEvent;
        }

        public void SetEnabled(bool on) { _enabled = on; _lastTap = 0; }

        /// <summary>Apply new settings without touching the tap. Throws FormatException for an invalid hotkey.</summary>
        public void Reconfigure(Config cfg)
        {
            var hk = Hotkey.Parse(cfg.Hotkey);
            _cfg = cfg; _hk = hk; _lastTap = 0;
        }

        /// <summary>Try to install the tap. False means macOS denied it (permission missing).</summary>
        public bool Start()
        {
            if (!Mac.IsMac || _tap != IntPtr.Zero) return true;
            ulong mask = (1UL << (int)Mac.EvKeyDown) | (1UL << (int)Mac.EvLeftDown) | (1UL << (int)Mac.EvRightDown) | (1UL << (int)Mac.EvOtherDown);
            _tap = Mac.CreateTap(mask, _callback);
            return _tap != IntPtr.Zero;
        }

        IntPtr OnEvent(IntPtr proxy, uint type, IntPtr ev, IntPtr info)
        {
            try
            {
                if (type == Mac.EvTapTimeout || type == Mac.EvTapUserDisabled) { Mac.EnableTap(_tap); return ev; }
                if (type == Mac.EvKeyDown)
                {
                    if (!Mac.IsOwn(ev)) OnKeyDown(Mac.KeyCode(ev), Mac.Flags(ev), Mac.IsAutorepeat(ev));
                }
                else if (type == Mac.EvLeftDown || type == Mac.EvRightDown || type == Mac.EvOtherDown)
                {
                    var p = Mac.Location(ev);
                    MouseClicked?.Invoke(p.X, p.Y);
                }
            }
            catch { /* never let an exception cross the native boundary */ }
            return ev;
        }

        void OnKeyDown(long code, ulong flags, bool repeat)
        {
            if (!_enabled) return;
            if (code == 53) // Escape
            {
                if (PopupVisible()) EscapePressed?.Invoke();
                return;
            }
            if (code != _hk.Code) { _lastTap = 0; return; }
            if (repeat) return;
            if (!_hk.ModsMatch(flags)) { _lastTap = 0; return; }
            if (Paused?.Invoke() == true) { _lastTap = 0; return; }

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
                    _seqAtFirstTap = Mac.ChangeCount();
                }
                return;
            }
            Fire(Mac.ChangeCount());
        }

        void Fire(long seqBefore) =>
            Triggered?.Invoke(new TriggerInfo { InjectCopy = !_hk.IsPlainCopy, SeqBefore = seqBefore });

        // The tap lives as long as the process; just stop reacting.
        public void Dispose() { _enabled = false; }
    }
}
