using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace ShortcutUI;

/// <summary>"Win+Shift+S" のようなキーの組み合わせ。</summary>
public sealed class KeyCombo
{
    public bool Win, Ctrl, Shift, Alt;
    public Key Key = Key.None;

    public bool IsValid => Key != Key.None;

    public static KeyCombo Parse(string? text)
    {
        var combo = new KeyCombo();
        if (string.IsNullOrWhiteSpace(text)) return combo;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "win": case "windows": combo.Win = true; break;
                case "ctrl": case "control": combo.Ctrl = true; break;
                case "shift": combo.Shift = true; break;
                case "alt": combo.Alt = true; break;
                default:
                    if (Enum.TryParse<Key>(raw, true, out var k)) combo.Key = k;
                    break;
            }
        }
        return combo;
    }

    /// <summary>保存用の表記（例: Win+Shift+S）。</summary>
    public override string ToString() => string.Join("+", Parts(false));

    /// <summary>表示用の表記（例: Win + Shift + S）。</summary>
    public string ToDisplay() => string.Join(" + ", Parts(true));

    IEnumerable<string> Parts(bool display)
    {
        if (Win) yield return "Win";
        if (Ctrl) yield return "Ctrl";
        if (Shift) yield return "Shift";
        if (Alt) yield return "Alt";
        if (Key != Key.None) yield return display ? KeyName(Key) : Key.ToString();
    }

    public static string KeyName(Key k) => k switch
    {
        Key.Escape => "Esc",
        Key.Return => "Enter",
        Key.Back => "Backspace",
        Key.Delete => "Del",
        Key.Insert => "Ins",
        Key.Snapshot => "PrintScreen",
        Key.Prior => "PageUp",
        Key.Next => "PageDown",
        Key.Capital => "CapsLock",
        Key.Left => "←",
        Key.Right => "→",
        Key.Up => "↑",
        Key.Down => "↓",
        Key.OemPeriod => ".",
        Key.OemComma => ",",
        Key.OemMinus => "-",
        Key.OemPlus => ";",
        Key.Oem1 => ":",
        Key.Oem2 => "/",
        Key.Oem3 => "@",
        Key.Oem4 => "[",
        Key.Oem5 => "\\",
        Key.Oem6 => "]",
        Key.Oem7 => "^",
        Key.OemBackslash => "_",
        Key.MediaPlayPause => "再生/一時停止",
        Key.MediaNextTrack => "次の曲",
        Key.MediaPreviousTrack => "前の曲",
        Key.VolumeMute => "ミュート",
        Key.VolumeUp => "音量+",
        Key.VolumeDown => "音量-",
        >= Key.D0 and <= Key.D9 => ((int)(k - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (int)(k - Key.NumPad0),
        _ => k.ToString(),
    };
}
