using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShortcutUI;

public enum SlotKind { None, Keys, Launch }

public sealed class Slot
{
    public string Name { get; set; } = "";
    public SlotKind Kind { get; set; } = SlotKind.None;
    /// <summary>Kind=Keys のとき: "Win+Shift+S" 形式。</summary>
    public string Keys { get; set; } = "";
    /// <summary>Kind=Launch のとき: パス / URL / コマンド。</summary>
    public string Target { get; set; } = "";
    public string Arguments { get; set; } = "";
    /// <summary>任意のアイコン（Segoe Fluent Icons のグリフ）。</summary>
    public string Glyph { get; set; } = "";

    [JsonIgnore] public bool IsEmpty => Kind == SlotKind.None;

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            return Kind switch
            {
                SlotKind.Keys => KeyCombo.Parse(Keys).ToDisplay(),
                SlotKind.Launch => AutoName(Target),
                _ => "",
            };
        }
    }

    public static string AutoName(string target)
    {
        var t = target.Trim().Trim('"');
        if (t.Length == 0) return "";
        if (Uri.TryCreate(t, UriKind.Absolute, out var uri) && !uri.IsFile && !string.IsNullOrEmpty(uri.Host))
            return uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host;
        var name = Path.GetFileNameWithoutExtension(t.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? t : name;
    }

    public Slot Clone() => (Slot)MemberwiseClone();

    public static Slot MakeKeys(string name, string keys, string glyph = "") =>
        new() { Name = name, Kind = SlotKind.Keys, Keys = keys, Glyph = glyph };

    public static Slot MakeLaunch(string name, string target, string args = "", string glyph = "") =>
        new() { Name = name, Kind = SlotKind.Launch, Target = target, Arguments = args, Glyph = glyph };
}

public sealed class AppConfig
{
    public const int PageCount = 2;
    public const int SlotsPerPage = 8;

    /// <summary>メニューが開くまでの長押し時間（ミリ秒）。</summary>
    public int HoldMilliseconds { get; set; } = 300;
    /// <summary>長押し判定中に許容するカーソル移動量（物理ピクセル）。</summary>
    public int MoveTolerance { get; set; } = 6;
    public List<List<Slot>> Pages { get; set; } = new();
    public DesignSettings Design { get; set; } = new();

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShortcutUI");
    public static string FilePath => Path.Combine(Dir, "config.json");

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppConfig Load()
    {
        if (File.Exists(FilePath))
        {
            try
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Options);
                if (cfg != null) { cfg.Normalize(); return cfg; }
            }
            catch (Exception ex)
            {
                Log.Write("設定ファイルの読み込みに失敗しました", ex);
                try { File.Copy(FilePath, Path.Combine(Dir, "config.broken.json"), true); } catch { }
            }
        }
        var d = CreateDefault();
        d.Save();
        return d;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) { Log.Write("設定ファイルの保存に失敗しました", ex); }
    }

    void Normalize()
    {
        Pages ??= new();
        while (Pages.Count < PageCount) Pages.Add(new());
        if (Pages.Count > PageCount) Pages.RemoveRange(PageCount, Pages.Count - PageCount);
        for (int p = 0; p < PageCount; p++)
        {
            Pages[p] ??= new();
            for (int i = 0; i < Pages[p].Count; i++) Pages[p][i] ??= new Slot();
            while (Pages[p].Count < SlotsPerPage) Pages[p].Add(new Slot());
            if (Pages[p].Count > SlotsPerPage) Pages[p].RemoveRange(SlotsPerPage, Pages[p].Count - SlotsPerPage);
        }
        Design ??= new DesignSettings();
        Design.Normalize();
        HoldMilliseconds = Math.Clamp(HoldMilliseconds, 120, 2000);
        MoveTolerance = Math.Clamp(MoveTolerance, 2, 40);
    }

    /// <summary>デザイン設定のプレビュー用サンプル。</summary>
    public static AppConfig CreateSample()
    {
        var cfg = new AppConfig
        {
            Pages =
            {
                new()
                {
                    Slot.MakeKeys("エクスプローラー", "Win+E"),
                    Slot.MakeKeys("設定", "Win+I"),
                    Slot.MakeKeys("スクリーンショット", "Win+Shift+S"),
                    Slot.MakeKeys("クリップボード履歴", "Win+V"),
                    Slot.MakeKeys("タスクマネージャー", "Ctrl+Shift+Escape"),
                    Slot.MakeLaunch("電卓", "calc.exe", "", "\uE8EF"),
                    Slot.MakeLaunch("メモ帳", "notepad.exe", "", "\uE70B"),
                    Slot.MakeKeys("タスクビュー", "Win+Tab"),
                },
            },
        };
        cfg.Normalize();
        return cfg;
    }

    /// <summary>初期状態: すべての区画が空。</summary>
    public static AppConfig CreateDefault()
    {
        var cfg = new AppConfig();
        cfg.Normalize();
        return cfg;
    }
}

internal static class Log
{
    public static void Write(string message, Exception? ex = null)
    {
        try
        {
            Directory.CreateDirectory(AppConfig.Dir);
            File.AppendAllText(Path.Combine(AppConfig.Dir, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{(ex != null ? Environment.NewLine + ex : "")}{Environment.NewLine}");
        }
        catch { }
    }
}
