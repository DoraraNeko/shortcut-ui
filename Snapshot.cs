using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShortcutUI;

/// <summary>
/// デザイン確認用: `ShortcutUI.exe --snapshot <dir>` でメニューの各状態を PNG に書き出す。
/// </summary>
internal static class Snapshot
{
    const int Size = 600;

    public static void Render(string dir)
    {
        Directory.CreateDirectory(dir);
        var cfg = AppConfig.CreateSample();
        var c = new Point(Size / 2.0, Size / 2.0);

        Save(dir, "1_reveal_early", cfg, v => { v.MaskRadius = 70; v.Scale = 0.84; v.Rotation = -14; v.GlobalOpacity = 0.85; v.CenterHover = 1; });
        Save(dir, "2_reveal_mid", cfg, v => { v.MaskRadius = 200; v.Scale = 0.95; v.Rotation = -6; v.CenterHover = 1; });
        Save(dir, "3_center", cfg, v => { v.CenterHover = 1; });
        Save(dir, "4_hover", cfg, v =>
        {
            v.CursorPos = RadialMenuView.Polar(c, 115, 45);
            v.Hover[1] = 1; v.PointerAngle = 45; v.PointerAlpha = 1;
        });
        Save(dir, "5_far", cfg, v =>
        {
            v.CursorPos = RadialMenuView.Polar(c, 245, 100);
            v.Hover[2] = 1; v.PointerAngle = 100; v.PointerAlpha = 1; v.GlobalOpacity = 0.55;
        });
        Save(dir, "6_page_switch", cfg, v => { v.PrevPage = 0; v.Page = 1; v.PageT = 0.45; v.CenterHover = 1; });
        Save(dir, "7_pinned", cfg, v => { v.Pinned = true; v.PinT = 1; v.MaskRadius = 1500; v.Hover[3] = 0.7; v.CursorPos = RadialMenuView.Polar(c, 110, 135); });
        Save(dir, "8_pinned_page2", cfg, v => { v.Pinned = true; v.PinT = 1; v.MaskRadius = 1500; v.Page = 1; v.PrevPage = 1; });

        // テーマ違い
        var sample = AppConfig.CreateSample();
        foreach (var (theme, extra) in new (string, Action<DesignSettings>)[]
                 {
                     ("snow", _ => { }),
                     ("sunset", d => { d.Size = 0.85; }),
                     ("forest", d => { d.ShowLabels = false; d.Opacity = 0.7; }),
                 })
        {
            var d = new DesignSettings();
            d.ApplyTheme(ThemePresets.All.First(t => t.Id == theme));
            extra(d);
            Save(dir, "theme_" + theme, sample, v =>
            {
                v.Design = d;
                v.CursorPos = RadialMenuView.Polar(c, 110, 45);
                v.Hover[1] = 1; v.PointerAngle = 45; v.PointerAlpha = 1;
            });
        }

        // 魔法陣スタイル
        foreach (var (name, setup) in new (string, Action<RadialMenuView>)[]
                 {
                     ("magic_open_early", v => { v.OpenT = 0.18; v.Rotation = -150; v.Scale = 0.7; v.MaskRadius = 140; v.CenterHover = 1; }),
                     ("magic_open_mid", v => { v.OpenT = 0.42; v.Rotation = -40; v.Scale = 0.93; v.MaskRadius = 270; v.CenterHover = 1; }),
                     ("magic_hover", v => { v.CursorPos = RadialMenuView.Polar(c, 115, 45); v.Hover[1] = 1; v.PointerAngle = 45; v.PointerAlpha = 1; }),
                     ("magic_far", v => { v.CursorPos = RadialMenuView.Polar(c, 215, 250); v.Hover[6] = 1; v.PointerAngle = 250; v.PointerAlpha = 1; v.GlobalOpacity = 0.85; }),
                 })
        {
            var d = new DesignSettings { Style = DesignSettings.MagicStyle };
            Save(dir, name, sample, v => { v.Design = d; v.Time = 1.3; setup(v); });
        }

        RenderWindowContent(new DesignWindow(sample, () => { }), Path.Combine(dir, "window_design.png"), 860);
        RenderWindowContent(new EditorWindow(new Slot(), 0, 0, default, 0), Path.Combine(dir, "window_editor.png"), 460);
    }

    static void RenderWindowContent(Window w, string path, double width)
    {
        var root = (FrameworkElement)w.Content;
        w.Content = null;
        var host = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x3A)) };
        host.Children.Add(root);
        host.Measure(new System.Windows.Size(width, double.PositiveInfinity));
        var h = Math.Ceiling(host.DesiredSize.Height);
        host.Arrange(new Rect(0, 0, width, h));
        host.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)width, (int)h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(host);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
        w.Close();
    }

    static void Save(string dir, string name, AppConfig cfg, Action<RadialMenuView> setup)
    {
        foreach (var (suffix, bg) in new (string, Brush)[] { ("dark", Wallpaper(false)), ("light", Wallpaper(true)) })
        {
            var view = new RadialMenuView { Config = cfg, Visible = true };
            view.Center = view.CursorPos = new Point(Size / 2.0, Size / 2.0);
            view.MaskRadius = 330;
            setup(view);
            var grid = new Grid { Width = Size, Height = Size, Background = bg };
            grid.Children.Add(view);
            grid.Measure(new System.Windows.Size(Size, Size));
            grid.Arrange(new Rect(0, 0, Size, Size));
            grid.UpdateLayout();

            var rtb = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(grid);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(Path.Combine(dir, $"{name}_{suffix}.png"));
            enc.Save(fs);
        }
    }

    static Brush Wallpaper(bool light)
    {
        var b = light
            ? new LinearGradientBrush(Color.FromRgb(0xE8, 0xEE, 0xF8), Color.FromRgb(0x9F, 0xC4, 0xE8), 35)
            : new LinearGradientBrush(Color.FromRgb(0x1B, 0x2A, 0x4A), Color.FromRgb(0x4E, 0x2F, 0x6E), 35);
        return b;
    }
}
