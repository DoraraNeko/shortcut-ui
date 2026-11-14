using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ShortcutUI;

/// <summary>サークルのデザインを変更する設定画面（ライブプレビュー付き）。</summary>
public partial class DesignWindow : Window
{
    readonly AppConfig config;
    readonly Action onSaved;
    DesignSettings working;
    readonly RadialMenuView preview = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    double replayStart, lastFrame;
    bool loading;
    RadioButton? customSwatch;

    static readonly Point PreviewCenter = new(270, 270);

    internal DesignWindow(AppConfig config, Action onSaved)
    {
        InitializeComponent();
        this.config = config;
        this.onSaved = onSaved;
        working = config.Design.Clone();

        SizeSlider.TickFrequency = 0.05;
        OpacitySlider.TickFrequency = 0.05;
        FadeSlider.TickFrequency = 10;
        SpeedSlider.TickFrequency = 0.1;
        foreach (var s in new[] { SizeSlider, OpacitySlider, FadeSlider, SpeedSlider }) s.IsSnapToTickEnabled = true;

        BuildThemeSwatches();

        preview.Config = config.Pages.Exists(p => p.Exists(s => !s.IsEmpty)) ? config : AppConfig.CreateSample();
        preview.Visible = true;
        preview.Center = preview.CursorPos = PreviewCenter;
        PreviewHost.Children.Add(preview);
        UpdateBackground();

        LoadControls();

        Root.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) =>
        {
            Replay();
            CompositionTarget.Rendering += OnFrame;
        };
        Closed += (_, _) => CompositionTarget.Rendering -= OnFrame;
    }

    // ---------------------------------------------------------------- 画面の構築

    void BuildThemeSwatches()
    {
        foreach (var t in ThemePresets.All)
            ThemePanel.Children.Add(MakeSwatch(t.Name, t, ColorOf(t.AccentA), ColorOf(t.AccentB), t.Light));
        customSwatch = MakeSwatch("カスタム", null, Colors.Gray, Colors.Gray, false);
        ThemePanel.Children.Add(customSwatch);
    }

    RadioButton MakeSwatch(string name, ThemePreset? preset, Color a, Color b, bool light)
    {
        var dot = new Ellipse { Width = 30, Height = 30 };
        var grad = new LinearGradientBrush(a, b, 45);
        if (light) { dot.Fill = Brushes.White; dot.Stroke = grad; dot.StrokeThickness = 6; }
        else dot.Fill = grad;

        var panel = new StackPanel();
        panel.Children.Add(dot);
        panel.Children.Add(new TextBlock { Text = name, FontSize = 11, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });

        var rb = new RadioButton { Content = panel, GroupName = "Theme", Style = (Style)FindResource("Swatch"), Tag = preset };
        rb.Checked += (_, _) =>
        {
            if (loading || rb.Tag is not ThemePreset p) return;
            working.ApplyTheme(p);
            LoadControls();
        };
        return rb;
    }

    /// <summary>working の値を各コントロールに反映する。</summary>
    void LoadControls()
    {
        loading = true;
        AccentABox.Text = working.AccentA;
        AccentBBox.Text = working.AccentB;
        StyleStandard.IsChecked = working.Style != DesignSettings.MagicStyle;
        StyleMagic.IsChecked = working.Style == DesignSettings.MagicStyle;
        SegDark.IsChecked = !working.LightSegments;
        SegLight.IsChecked = working.LightSegments;
        LabelsOn.IsChecked = working.ShowLabels;
        LabelsOff.IsChecked = !working.ShowLabels;
        SizeSlider.Value = working.Size;
        OpacitySlider.Value = working.Opacity;
        FadeSlider.Value = working.FadeRange;
        SpeedSlider.Value = working.AnimationSpeed;
        loading = false;
        SyncThemeSelection();
        Refresh();
    }

    /// <summary>現在の色がどのテーマに一致するかを判定して選択状態を合わせる。</summary>
    void SyncThemeSelection()
    {
        loading = true;
        RadioButton? match = null;
        foreach (var child in ThemePanel.Children)
        {
            if (child is RadioButton rb && rb.Tag is ThemePreset p &&
                string.Equals(p.AccentA, working.AccentA, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.AccentB, working.AccentB, StringComparison.OrdinalIgnoreCase) &&
                p.Light == working.LightSegments)
            {
                match = rb;
                break;
            }
        }
        if (match?.Tag is ThemePreset mp) working.Theme = mp.Id;
        else working.Theme = DesignSettings.CustomTheme;
        (match ?? customSwatch)!.IsChecked = true;
        loading = false;
    }

    void Refresh()
    {
        SwatchA.Background = new SolidColorBrush(working.AccentAColor);
        SwatchB.Background = new SolidColorBrush(working.AccentBColor);
        if (customSwatch?.Content is StackPanel sp && sp.Children[0] is Ellipse el)
        {
            var g = new LinearGradientBrush(working.AccentAColor, working.AccentBColor, 45);
            if (working.LightSegments) { el.Fill = Brushes.White; el.Stroke = g; el.StrokeThickness = 6; }
            else { el.Fill = g; el.Stroke = null; }
        }

        SizeValue.Text = $"{working.Size * 100:0}%";
        OpacityValue.Text = $"{working.Opacity * 100:0}%";
        FadeValue.Text = working.FadeRange switch
        {
            < 270 => "強く透ける",
            < 400 => "標準",
            < 500 => "弱め",
            _ => "ほとんど透けない",
        };
        SpeedValue.Text = $"{working.AnimationSpeed:0.0}×";

        preview.Design = working;
        preview.InvalidateVisual();
    }

    // ---------------------------------------------------------------- 入力

    void OnAccentChanged(object sender, TextChangedEventArgs e)
    {
        if (loading) return;
        if (DesignSettings.TryParseColor(AccentABox.Text, out _)) working.AccentA = Hex(AccentABox.Text);
        if (DesignSettings.TryParseColor(AccentBBox.Text, out _)) working.AccentB = Hex(AccentBBox.Text);
        SyncThemeSelection();
        Refresh();
    }

    void OnOptionChanged(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        var oldStyle = working.Style;
        working.Style = StyleMagic.IsChecked == true ? DesignSettings.MagicStyle : DesignSettings.StandardStyle;
        working.LightSegments = SegLight.IsChecked == true;
        working.ShowLabels = LabelsOn.IsChecked == true;
        SyncThemeSelection();
        Refresh();
        if (oldStyle != working.Style) Replay();
    }

    void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (loading || working == null) return;
        working.Size = SizeSlider.Value;
        working.Opacity = OpacitySlider.Value;
        working.FadeRange = FadeSlider.Value;
        working.AnimationSpeed = SpeedSlider.Value;
        Refresh();
    }

    void OnBgChanged(object sender, RoutedEventArgs e) => UpdateBackground();

    void UpdateBackground()
    {
        if (PreviewFrame == null || BgLight == null) return;
        PreviewFrame.Background = BgLight.IsChecked == true
            ? new LinearGradientBrush(Color.FromRgb(0xE8, 0xEE, 0xF8), Color.FromRgb(0x9F, 0xC4, 0xE8), 35)
            : new LinearGradientBrush(Color.FromRgb(0x1B, 0x2A, 0x4A), Color.FromRgb(0x4E, 0x2F, 0x6E), 35);
    }

    void OnReplay(object sender, RoutedEventArgs e) => Replay();

    void OnReset(object sender, RoutedEventArgs e)
    {
        working = new DesignSettings();
        LoadControls();
        Replay();
    }

    void OnSave(object sender, RoutedEventArgs e)
    {
        working.Normalize();
        config.Design = working.Clone();
        config.Save();
        onSaved();
        Close();
    }

    void OnCancel(object sender, RoutedEventArgs e) => Close();

    // ---------------------------------------------------------------- プレビューのアニメーション

    void Replay()
    {
        replayStart = clock.Elapsed.TotalSeconds;
        Array.Clear(preview.Hover);
        preview.CenterHover = 1;
        preview.PointerAlpha = 0;
    }

    /// <summary>展開アニメーションを再生した後、カーソルが区画をゆっくり巡回する様子を見せる。</summary>
    void OnFrame(object? sender, EventArgs e)
    {
        double now = clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - lastFrame, 0, 0.1);
        lastFrame = now;
        double sp = working.AnimationSpeed, sz = working.Size;
        double t = (now - replayStart) * sp;

        preview.Time = now;
        preview.OpenT = t;
        preview.MaskRadius = working.FadeRange * sz * Ease.OutCubic(t / 0.5);
        double from = preview.OpenScaleFrom;
        preview.Scale = from + (1 - from) * Ease.OutBack(t / 0.42, 1.1);
        preview.Rotation = preview.OpenSpin * (1 - Ease.OutQuint(t / preview.OpenSpinDuration));
        preview.GlobalOpacity = Ease.OutCubic(t / 0.16);

        // 展開後、カーソルを中心から区画へ動かして時計回りに巡回させる
        double orbitT = Math.Max(0, t - (preview.IsMagic ? 1.1 : 0.8));
        double reach = Ease.InOutCubic(orbitT / 0.6);
        double angle = (orbitT * 38) % 360;
        double r = 112 * sz * reach;
        preview.CursorPos = RadialMenuView.Polar(PreviewCenter, r, angle);

        int hover = reach < 0.5 ? -1 : (int)Math.Floor(((angle + RadialMenuView.SegAngle / 2) % 360) / RadialMenuView.SegAngle) % RadialMenuView.SlotCount;
        double k = 1 - Math.Exp(-dt * 20);
        for (int i = 0; i < RadialMenuView.SlotCount; i++)
            preview.Hover[i] += ((hover == i ? 1 : 0) - preview.Hover[i]) * k;
        preview.CenterHover += ((hover == -1 ? 1 : 0) - preview.CenterHover) * k;
        preview.PointerAngle = angle;
        preview.PointerAlpha += ((reach > 0.5 ? 1 : 0) - preview.PointerAlpha) * k;
        preview.InvalidateVisual();
    }

    static string Hex(string text) => "#" + text.Trim().TrimStart('#').ToUpperInvariant();
    static Color ColorOf(string hex) => DesignSettings.TryParseColor(hex, out var c) ? c : Colors.Gray;
}
