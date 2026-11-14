using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShortcutUI;

/// <summary>Segoe Fluent Icons / MDL2 のグリフ。</summary>
internal static class Glyphs
{
    public const string Add = "", Pin = "", Edit = "", Keyboard = "",
        Globe = "", Folder = "", File = "", Delete = "", Lock = "",
        Terminal = "", Close = "";

    static readonly Dictionary<string, string> ByKeys = Build(new()
    {
        ["Win+E"] = Folder,
        ["Win+I"] = "",
        ["Win+Shift+S"] = "",
        ["Win+V"] = "",
        ["Ctrl+V"] = "",
        ["Ctrl+C"] = "",
        ["Ctrl+X"] = "",
        ["Ctrl+Z"] = "",
        ["Ctrl+Y"] = "",
        ["Ctrl+S"] = "",
        ["Ctrl+A"] = "",
        ["Ctrl+Shift+Escape"] = "",
        ["Win+D"] = "",
        ["Win+R"] = Terminal,
        ["Win+Tab"] = "",
        ["Win+OemPeriod"] = "",
        ["Win+L"] = Lock,
        ["Win+A"] = "",
        ["Win+N"] = "",
        ["Win+Ctrl+D"] = "",
        ["MediaPlayPause"] = "",
        ["MediaNextTrack"] = "",
        ["MediaPreviousTrack"] = "",
        ["VolumeMute"] = "",
        ["VolumeUp"] = "",
        ["VolumeDown"] = "",
    });

    static Dictionary<string, string> Build(Dictionary<string, string> src)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in src) d[KeyCombo.Parse(kv.Key).ToString()] = kv.Value;
        return d;
    }

    /// <summary>スロットの見た目を決める。画像があれば画像、なければグリフ。</summary>
    public static ImageSource? Resolve(Slot slot, out string glyph)
    {
        glyph = slot.Glyph;
        if (!string.IsNullOrEmpty(glyph)) return null;

        if (slot.Kind == SlotKind.Keys)
        {
            glyph = ByKeys.TryGetValue(KeyCombo.Parse(slot.Keys).ToString(), out var g) ? g : Keyboard;
            return null;
        }
        var img = IconCache.Get(slot);
        if (img != null) return img;
        var t = slot.Target.Trim();
        glyph = t.Contains("://") ? Globe : t.EndsWith("\\") || t.EndsWith("/") ? Folder : File;
        return null;
    }
}

internal static class IconCache
{
    static readonly Dictionary<string, ImageSource?> cache = new(StringComparer.OrdinalIgnoreCase);

    public static void Clear() => cache.Clear();

    public static ImageSource? Get(Slot slot)
    {
        if (slot.Kind != SlotKind.Launch || string.IsNullOrWhiteSpace(slot.Target)) return null;
        var path = Resolve(slot.Target);
        if (path == null) return null;
        if (cache.TryGetValue(path, out var img)) return img;
        img = Load(path);
        cache[path] = img;
        return img;
    }

    /// <summary>対象を実在するファイル / フォルダのフルパスに解決する。</summary>
    public static string? Resolve(string target)
    {
        try
        {
            var t = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
            if (t.Length == 0 || t.Contains("://") || t.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return null;
            if (File.Exists(t) || Directory.Exists(t)) return Path.GetFullPath(t);
            if (Path.IsPathRooted(t) || t.IndexOfAny(new[] { '\\', '/' }) >= 0) return null;

            var dirs = new List<string> { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
            dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries));
            foreach (var d in dirs)
            {
                try
                {
                    var p = Path.Combine(d.Trim(), t);
                    if (File.Exists(p)) return p;
                    if (!Path.HasExtension(p) && File.Exists(p + ".exe")) return p + ".exe";
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    static ImageSource? Load(string path)
    {
        IntPtr hIcon = IntPtr.Zero;
        try
        {
            var info = new Native.SHFILEINFO();
            uint size = (uint)Marshal.SizeOf<Native.SHFILEINFO>();
            if (Native.SHGetFileInfo(path, 0, ref info, size, Native.SHGFI_SYSICONINDEX) != IntPtr.Zero)
            {
                var iid = Native.IID_IImageList;
                if (Native.SHGetImageList(Native.SHIL_EXTRALARGE, ref iid, out var himl) == 0 && himl != IntPtr.Zero)
                    hIcon = Native.ImageList_GetIcon(himl, info.iIcon, Native.ILD_TRANSPARENT);
            }
            if (hIcon == IntPtr.Zero)
            {
                info = new Native.SHFILEINFO();
                Native.SHGetFileInfo(path, 0, ref info, size, Native.SHGFI_ICON | Native.SHGFI_LARGEICON);
                hIcon = info.hIcon;
            }
            if (hIcon == IntPtr.Zero) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch (Exception ex)
        {
            Log.Write($"アイコン取得に失敗: {path}", ex);
            return null;
        }
        finally
        {
            if (hIcon != IntPtr.Zero) Native.DestroyIcon(hIcon);
        }
    }
}
