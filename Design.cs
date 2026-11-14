using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace ShortcutUI;

/// <summary>メニュー（サークル）の見た目の設定。既定値は最初のデザイン。</summary>
public sealed class DesignSettings
{
    public const string CustomTheme = "custom";
    public const string StandardStyle = "standard", MagicStyle = "magic";

    /// <summary>サークルの形: standard（標準） / magic（魔法陣）。</summary>
    public string Style { get; set; } = StandardStyle;

    public string Theme { get; set; } = "aurora";
    public string AccentA { get; set; } = "#7B61FF";
    public string AccentB { get; set; } = "#36C5FF";
    /// <summary>区画を明るい色で描く。</summary>
    public bool LightSegments { get; set; }
    /// <summary>大きさの倍率（0.8〜1.25）。</summary>
    public double Size { get; set; } = 1.0;
    /// <summary>区画の不透明度（0.4〜1.0）。</summary>
    public double Opacity { get; set; } = 1.0;
    /// <summary>カーソルから消えるまでの距離（DIP）。小さいほど強く透ける。</summary>
    public double FadeRange { get; set; } = 330;
    /// <summary>アニメーション速度の倍率（0.5〜2.0）。</summary>
    public double AnimationSpeed { get; set; } = 1.0;
    public bool ShowLabels { get; set; } = true;

    [JsonIgnore] public Color AccentAColor => ParseColor(AccentA, Color.FromRgb(0x7B, 0x61, 0xFF));
    [JsonIgnore] public Color AccentBColor => ParseColor(AccentB, Color.FromRgb(0x36, 0xC5, 0xFF));

    public DesignSettings Clone() => (DesignSettings)MemberwiseClone();

    public void Normalize()
    {
        Theme = string.IsNullOrWhiteSpace(Theme) ? "aurora" : Theme;
        if (Style != MagicStyle) Style = StandardStyle;
        if (!TryParseColor(AccentA, out _)) AccentA = "#7B61FF";
        if (!TryParseColor(AccentB, out _)) AccentB = "#36C5FF";
        Size = Math.Clamp(Size, 0.8, 1.25);
        Opacity = Math.Clamp(Opacity, 0.4, 1.0);
        FadeRange = Math.Clamp(FadeRange, 200, 600);
        AnimationSpeed = Math.Clamp(AnimationSpeed, 0.5, 2.0);
    }

    public void ApplyTheme(ThemePreset t)
    {
        Theme = t.Id;
        AccentA = t.AccentA;
        AccentB = t.AccentB;
        LightSegments = t.Light;
    }

    public static bool TryParseColor(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().TrimStart('#');
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        color = Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    static Color ParseColor(string text, Color fallback) => TryParseColor(text, out var c) ? c : fallback;
}

public sealed record ThemePreset(string Id, string Name, string AccentA, string AccentB, bool Light);

public static class ThemePresets
{
    public static readonly IReadOnlyList<ThemePreset> All = new[]
    {
        new ThemePreset("aurora", "オーロラ", "#7B61FF", "#36C5FF", false),
        new ThemePreset("sunset", "サンセット", "#FF5F8F", "#FFB25B", false),
        new ThemePreset("forest", "フォレスト", "#13C39A", "#9BE15D", false),
        new ThemePreset("ocean", "オーシャン", "#2563EB", "#38E1F2", false),
        new ThemePreset("rose", "ローズ", "#D946EF", "#FF7EB3", false),
        new ThemePreset("mono", "モノクロ", "#F2F3F5", "#8A909C", false),
        new ThemePreset("snow", "スノー", "#7B61FF", "#36C5FF", true),
        new ThemePreset("sakura", "さくら", "#F06292", "#FFB4C8", true),
    };
}
