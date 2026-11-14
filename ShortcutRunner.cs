using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace ShortcutUI;

internal static class ShortcutRunner
{
    /// <summary>スロットを実行する。失敗した場合はエラーメッセージを返す。</summary>
    public static string? Run(Slot slot)
    {
        try
        {
            switch (slot.Kind)
            {
                case SlotKind.Keys:
                    var combo = KeyCombo.Parse(slot.Keys);
                    if (!combo.IsValid) return $"「{slot.DisplayName}」のキーが設定されていません。";
                    SendKeys(combo);
                    return null;

                case SlotKind.Launch:
                    var target = Environment.ExpandEnvironmentVariables(slot.Target.Trim().Trim('"'));
                    if (target.Length == 0) return $"「{slot.DisplayName}」の対象が設定されていません。";
                    var psi = new ProcessStartInfo
                    {
                        FileName = target,
                        Arguments = Environment.ExpandEnvironmentVariables(slot.Arguments ?? ""),
                        UseShellExecute = true,
                    };
                    if (File.Exists(target)) psi.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(target)) ?? "";
                    Process.Start(psi)?.Dispose();
                    return null;
            }
            return null;
        }
        catch (Exception ex)
        {
            Log.Write($"実行に失敗: {slot.DisplayName}", ex);
            return $"「{slot.DisplayName}」を実行できませんでした: {ex.Message}";
        }
    }

    const ushort VK_LWIN = 0x5B, VK_CONTROL = 0x11, VK_SHIFT = 0x10, VK_MENU = 0x12;

    public static void SendKeys(KeyCombo combo)
    {
        var mods = new List<ushort>();
        if (combo.Win) mods.Add(VK_LWIN);
        if (combo.Ctrl) mods.Add(VK_CONTROL);
        if (combo.Shift) mods.Add(VK_SHIFT);
        if (combo.Alt) mods.Add(VK_MENU);
        var main = (ushort)KeyInterop.VirtualKeyFromKey(combo.Key);

        var inputs = new List<Native.INPUT>();
        foreach (var m in mods) inputs.Add(KeyInput(m, false));
        inputs.Add(KeyInput(main, false));
        inputs.Add(KeyInput(main, true));
        for (int i = mods.Count - 1; i >= 0; i--) inputs.Add(KeyInput(mods[i], true));

        var arr = inputs.ToArray();
        Native.SendInput((uint)arr.Length, arr, Marshal.SizeOf<Native.INPUT>());
    }

    static Native.INPUT KeyInput(ushort vk, bool up)
    {
        uint flags = up ? Native.KEYEVENTF_KEYUP : 0;
        if (IsExtended(vk)) flags |= Native.KEYEVENTF_EXTENDEDKEY;
        return new Native.INPUT
        {
            type = Native.INPUT_KEYBOARD,
            U = new Native.InputUnion
            {
                ki = new Native.KEYBDINPUT
                {
                    wVk = vk,
                    wScan = (ushort)Native.MapVirtualKey(vk, 0),
                    dwFlags = flags,
                    dwExtraInfo = Native.InjectMarker,
                },
            },
        };
    }

    static bool IsExtended(ushort vk) =>
        vk is >= 0x21 and <= 0x28   // PageUp/Down, End, Home, 矢印
        or 0x2C or 0x2D or 0x2E     // PrintScreen, Insert, Delete
        or 0x5B or 0x5C or 0x5D     // Win, Apps
        or 0x6F or 0x90             // Num /, NumLock
        or 0xA3 or 0xA5             // 右Ctrl, 右Alt
        or >= 0xA6 and <= 0xB7;     // ブラウザ・メディアキー
}
