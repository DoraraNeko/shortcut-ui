using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ShortcutUI;

internal static class Ease
{
    public static double Clamp01(double t) => t < 0 ? 0 : t > 1 ? 1 : t;
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
    public static double OutCubic(double t) { t = Clamp01(t); return 1 - Math.Pow(1 - t, 3); }
    public static double OutQuint(double t) { t = Clamp01(t); return 1 - Math.Pow(1 - t, 5); }
    public static double InOutCubic(double t) { t = Clamp01(t); return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2; }
    public static double OutBack(double t, double s = 1.25) { t = Clamp01(t) - 1; return 1 + (s + 1) * t * t * t + s * t * t; }
    public static double SmoothStep(double e0, double e1, double x) { var t = Clamp01((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); }
}

/// <summary>
/// ドーナツ型メニューの描画のみを担当する。状態は <see cref="RadialMenuWindow"/> が毎フレーム設定する。
/// 角度は真上を 0° とした時計回り。
/// </summary>
internal sealed partial class RadialMenuView : FrameworkElement
{
    public const int SlotCount = AppConfig.SlotsPerPage;
    public const double SegAngle = 360.0 / SlotCount;
    public const double CenterR = 36, InnerR = 52, OuterR = 164, PopOut = 9, GapPx = 4.5, CornerPx = 7;
    public const double HitRadius = OuterR + 26;

    // ---- 状態 ----
    public AppConfig? Config;
    public bool Visible;
    public Point Center, CursorPos;
    public bool MaskEnabled = true;
    public double MaskRadius = 320;
    public double GlobalOpacity = 1, Scale = 1, Rotation;
    public int Page, PrevPage;
    public double PageT = 1;
    public int PageDir = 1;
    public readonly double[] Hover = new double[SlotCount];
    public double CenterHover;
    public double PointerAngle, PointerAlpha;
    public bool Pinned;
    public double PinT;
    public int Flash = -1;
    public double FlashT;
    public int EditingSlot = -1;
    /// <summary>連続アニメーション用の経過時間（秒）。</summary>
    public double Time;
    /// <summary>展開からの経過時間（速度倍率込み）。</summary>
    public double OpenT = 10;

    // ---- 配色 ----
    static readonly Typeface TextFace = new(new FontFamily("Segoe UI Variable Text, Yu Gothic UI, Segoe UI"),
        FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    static readonly Typeface IconFace = new(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    static readonly Brush HitBrush = Freeze(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));

    // ---- デザイン設定から作るパレット ----
    DesignSettings design = new();
    Color AccentA, AccentB, TextBase, FillInner, FillMid, FillOuter, HaloColor;
    byte HaloAlpha;
    Pen SegStroke = null!;
    Brush CenterFill = null!, ChipFill = null!;
    double SizeK = 1;

    public DesignSettings Design
    {
        get => design;
        set { design = value ?? new DesignSettings(); BuildPalette(); }
    }

    void BuildPalette()
    {
        var d = design;
        AccentA = d.AccentAColor;
        AccentB = d.AccentBColor;
        SizeK = d.Size;
        double o = d.Opacity;
        byte A(double alpha) => (byte)Math.Round(Math.Clamp(alpha * o, 0, 255));
        if (d.LightSegments)
        {
            TextBase = Color.FromRgb(0x1C, 0x1F, 0x2A);
            FillInner = Color.FromArgb(A(246), 255, 255, 255);
            FillMid = Color.FromArgb(A(238), 245, 246, 250);
            FillOuter = Color.FromArgb(A(230), 234, 236, 243);
            SegStroke = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(30, 20, 24, 40)), 1) { LineJoin = PenLineJoin.Round });
            CenterFill = Freeze(new SolidColorBrush(Color.FromArgb(A(250), 255, 255, 255)));
            ChipFill = Freeze(new SolidColorBrush(Color.FromArgb(A(240), 250, 250, 253)));
            HaloColor = Color.FromRgb(10, 14, 30);
            HaloAlpha = 44;
        }
        else
        {
            TextBase = Colors.White;
            FillInner = Color.FromArgb(A(236), 40, 43, 58);
            FillMid = Color.FromArgb(A(226), 22, 24, 34);
            FillOuter = Color.FromArgb(A(214), 16, 17, 25);
            SegStroke = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), 1) { LineJoin = PenLineJoin.Round });
            CenterFill = Freeze(new SolidColorBrush(Color.FromArgb(A(236), 22, 24, 33)));
            ChipFill = Freeze(new SolidColorBrush(Color.FromArgb(A(222), 20, 22, 31)));
            HaloColor = Color.FromRgb(4, 6, 14);
            HaloAlpha = 70;
        }
        if (d.Style == DesignSettings.MagicStyle)
        {
            // 魔法陣: 下の模様が透けて見える半透明の区画
            TextBase = Colors.White;
            FillInner = Color.FromArgb(A(170), 30, 24, 58);
            FillMid = Color.FromArgb(A(150), 18, 14, 38);
            FillOuter = Color.FromArgb(A(130), 12, 10, 26);
            SegStroke = Freeze(new Pen(new SolidColorBrush(WithA(AccentB, 120)), 1) { LineJoin = PenLineJoin.Round });
            CenterFill = Freeze(new SolidColorBrush(Color.FromArgb(A(235), 14, 12, 28)));
            ChipFill = Freeze(new SolidColorBrush(Color.FromArgb(A(225), 16, 13, 32)));
            HaloColor = Color.FromRgb(6, 4, 16);
            HaloAlpha = 60;
        }
        HaloAlpha = (byte)(HaloAlpha * (0.4 + 0.6 * o));
    }

    double pixelsPerDip = 1;

    public RadialMenuView()
    {
        BuildPalette();
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
    }

    // ================================================================ 描画

    protected override void OnRender(DrawingContext dc)
    {
        if (!Visible || Config == null || GlobalOpacity <= 0.002) return;
        pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var c = Center;

        // 固定モードではドーナツ全体でクリックを受け取れるよう、ほぼ透明な面を敷く
        if (Pinned) dc.DrawEllipse(HitBrush, null, c, HitRadius * SizeK, HitRadius * SizeK);

        dc.PushOpacity(GlobalOpacity);
        if (MaskEnabled) dc.PushOpacityMask(MakeMask());
        dc.PushTransform(new ScaleTransform(Scale * SizeK, Scale * SizeK, c.X, c.Y));
        dc.PushTransform(new RotateTransform(Rotation, c.X, c.Y));

        DrawHalo(dc, c);
        if (IsMagic) DrawMagicBackdrop(dc, c);
        DrawSegments(dc, c);
        DrawPointer(dc, c);

        double t = Ease.InOutCubic(PageT);
        if (PageT < 1)
        {
            DrawPage(dc, c, PrevPage, 1 - t, PageDir * 22 * t, false);
            DrawPage(dc, c, Page, t, -PageDir * 22 * (1 - t), true);
        }
        else DrawPage(dc, c, Page, 1, 0, true);

        DrawCenter(dc, c);
        DrawRipple(dc, c);
        if (Pinned && PinT > 0.01) DrawPinnedChrome(dc, c);

        dc.Pop(); dc.Pop();
        if (IsMagic) DrawElectric(dc, c);
        if (MaskEnabled) dc.Pop();
        dc.Pop();
    }

    /// <summary>カーソル位置を中心に、離れるほど透明になるマスク。</summary>
    Brush MakeMask()
    {
        var r = Math.Max(1, MaskRadius);
        var b = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = CursorPos,
            GradientOrigin = CursorPos,
            RadiusX = r,
            RadiusY = r,
        };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.36));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(170, 255, 255, 255), 0.62));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(60, 255, 255, 255), 0.84));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        return b;
    }

    /// <summary>どんな壁紙の上でも読めるよう、リングの背後に柔らかい影を落とす。</summary>
    void DrawHalo(DrawingContext dc, Point c)
    {
        double R = OuterR + 70;
        var b = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = c, GradientOrigin = c, RadiusX = R, RadiusY = R };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), (InnerR - 24) / R));
        b.GradientStops.Add(new GradientStop(WithA(HaloColor, HaloAlpha), (InnerR + 30) / R));
        b.GradientStops.Add(new GradientStop(WithA(HaloColor, (byte)(HaloAlpha * 0.8)), (OuterR + 6) / R));
        b.GradientStops.Add(new GradientStop(WithA(HaloColor, 0), 1));
        dc.DrawEllipse(b, null, c, R, R);
    }

    void DrawSegments(DrawingContext dc, Point c)
    {
        double Rf = OuterR + PopOut;
        var fill = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = c, GradientOrigin = c, RadiusX = Rf, RadiusY = Rf };
        fill.GradientStops.Add(new GradientStop(FillInner, InnerR / Rf));
        fill.GradientStops.Add(new GradientStop(FillMid, 0.78));
        fill.GradientStops.Add(new GradientStop(FillOuter, 1));

        for (int i = 0; i < SlotCount; i++)
        {
            double h = SlotHover(i);
            double a = i * SegAngle;
            double r0 = InnerR + h * 2, r1 = OuterR + h * PopOut;
            var geo = Wedge(c, r0, r1, a - SegAngle / 2, a + SegAngle / 2);
            dc.DrawGeometry(fill, null, geo);

            if (h > 0.005)
            {
                var accent = new LinearGradientBrush
                {
                    MappingMode = BrushMappingMode.Absolute,
                    StartPoint = Polar(c, r0, a),
                    EndPoint = Polar(c, r1, a),
                };
                accent.GradientStops.Add(new GradientStop(WithA(AccentA, 235), 0));
                accent.GradientStops.Add(new GradientStop(WithA(AccentB, 235), 1));
                dc.PushOpacity(IsMagic ? h * 0.62 : h);
                dc.DrawGeometry(accent, null, geo);
                // 外周のハイライト
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1.2) { LineJoin = PenLineJoin.Round }, geo);
                dc.Pop();
            }
            dc.DrawGeometry(null, SegStroke, geo);
            if (IsMagic && h > 0.01) GlowGeometry(dc, geo, AccentB, 1.1, h * 0.8);
        }
    }

    /// <summary>カーソルの方向を示す細いアーク。</summary>
    void DrawPointer(DrawingContext dc, Point c)
    {
        if (PointerAlpha <= 0.01) return;
        double r = OuterR + PopOut + 9;
        double span = 13;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(Polar(c, r, PointerAngle - span), false, false);
            ctx.ArcTo(Polar(c, r, PointerAngle + span), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        }
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = Polar(c, r, PointerAngle - span),
            EndPoint = Polar(c, r, PointerAngle + span),
        };
        brush.GradientStops.Add(new GradientStop(WithA(AccentA, 0), 0));
        brush.GradientStops.Add(new GradientStop(WithA(AccentB, 255), 0.5));
        brush.GradientStops.Add(new GradientStop(WithA(AccentA, 0), 1));
        dc.PushOpacity(PointerAlpha);
        dc.DrawGeometry(null, new Pen(brush, 2.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geo);
        dc.Pop();
    }

    void DrawPage(DrawingContext dc, Point c, int page, double alpha, double rotOffset, bool current)
    {
        if (alpha <= 0.01 || Config == null) return;
        var slots = Config.Pages[page];
        for (int i = 0; i < SlotCount; i++)
        {
            var slot = slots[i];
            double h = current ? SlotHover(i) : 0;
            double a = i * SegAngle + rotOffset;
            double r = (InnerR + OuterR) / 2 + 6 + h * PopOut * 0.5;
            var p = Polar(c, r, a);

            // 全体の回転（展開アニメーション）に逆らってアイコンは常に正立させる
            dc.PushTransform(new RotateTransform(-Rotation, p.X, p.Y));
            if (slot.IsEmpty)
            {
                double ea = alpha * (Pinned ? 0.5 : 0.26) * (1 + 0.8 * h);
                var g = Text(Glyphs.Add, 15, TextA(ea), IconFace);
                dc.DrawText(g, new Point(p.X - g.Width / 2, p.Y - g.Height / 2 - (Pinned && design.ShowLabels ? 7 : 0)));
                if (Pinned && design.ShowLabels)
                {
                    var l = Text("未設定", 10.5, TextA(ea * 0.9), TextFace, 76);
                    dc.DrawText(l, new Point(p.X - l.Width / 2, p.Y + 6));
                }
            }
            else
            {
                double iconY = design.ShowLabels ? p.Y - 9 : p.Y;
                var img = Glyphs.Resolve(slot, out var glyph);
                if (img != null)
                {
                    dc.PushOpacity(alpha);
                    dc.DrawImage(img, new Rect(p.X - 13, iconY - 13, 26, 26));
                    dc.Pop();
                }
                else
                {
                    var g = Text(glyph, 20, TextA(alpha * (0.86 + 0.14 * h), h), IconFace);
                    dc.DrawText(g, new Point(p.X - g.Width / 2, iconY - g.Height / 2));
                }
                if (design.ShowLabels)
                {
                    var label = Text(slot.DisplayName, 10.5, TextA(alpha * (0.70 + 0.30 * h), h), TextFace, 78);
                    dc.DrawText(label, new Point(p.X - label.Width / 2, p.Y + 8));
                }
            }
            dc.Pop();
        }
    }

    void DrawCenter(DrawingContext dc, Point c)
    {
        double h = CenterHover;
        double r = CenterR + h * 3;
        dc.DrawEllipse(CenterFill, SegStroke, c, r, r);
        if (h > 0.005)
        {
            var ring = new LinearGradientBrush(AccentA, AccentB, 45);
            dc.PushOpacity(h);
            dc.DrawEllipse(null, new Pen(ring, 1.6), c, r, r);
            dc.Pop();
        }

        dc.PushTransform(new RotateTransform(-Rotation, c.X, c.Y));
        // 中央のアイコン（長押し中: ピン＝離すと固定 / 固定中: 閉じる）
        string glyph = Pinned ? Glyphs.Close : Glyphs.Pin;
        double ga = 0.38 + 0.62 * h;
        var col = Lerp(TextBase, AccentB, h);
        var g = Text(glyph, 14, new SolidColorBrush(WithA(col, (byte)(255 * ga))), IconFace);
        dc.DrawText(g, new Point(c.X - g.Width / 2, c.Y - 7 - g.Height / 2));

        // ページインジケーター
        double t = Ease.InOutCubic(PageT);
        double PillW(int p) => p == Page ? Ease.Lerp(5, 15, t) : (p == PrevPage && PageT < 1 ? Ease.Lerp(15, 5, t) : 5);
        double w0 = PillW(0), w1 = PillW(1), gap = 4;
        double x = c.X - (w0 + gap + w1) / 2, y = c.Y + 11;
        DrawPill(dc, x, y, w0);
        DrawPill(dc, x + w0 + gap, y, w1);
        dc.Pop();
    }

    void DrawPill(DrawingContext dc, double x, double y, double w)
    {
        double k = Ease.Clamp01((w - 5) / 10);
        var col = Lerp(WithA(TextBase, 80), WithA(AccentB, 255), k);
        dc.DrawRoundedRectangle(new SolidColorBrush(col), null, new Rect(x, y - 2.5, w, 5), 2.5, 2.5);
    }

    void DrawRipple(DrawingContext dc, Point c)
    {
        if (Flash < 0 || FlashT <= 0 || FlashT >= 1) return;
        var p = Polar(c, (InnerR + OuterR) / 2 + 6, Flash * SegAngle);
        double e = Ease.OutCubic(FlashT);
        double rr = 14 + 70 * e;
        var pen = new Pen(new SolidColorBrush(WithA(AccentB, (byte)(220 * (1 - FlashT)))), 2.2 * (1 - e) + 0.6);
        dc.DrawEllipse(null, pen, p, rr, rr);
    }

    void DrawPinnedChrome(DrawingContext dc, Point c)
    {
        double a = PinT;
        var title = Text($"ページ {Page + 1} / {AppConfig.PageCount}", 12, TextA(0.92 * a), TextFace);
        DrawChip(dc, title, new Point(c.X, c.Y - OuterR - PopOut - 30), a);

        var hint = Text("クリックで実行  ·  右クリックで編集  ·  ホイールでページ切替  ·  中央で閉じる", 11, TextA(0.68 * a), TextFace);
        DrawChip(dc, hint, new Point(c.X, c.Y + OuterR + PopOut + 30), a);
    }

    void DrawChip(DrawingContext dc, FormattedText text, Point center, double a)
    {
        double w = text.Width + 28, h = text.Height + 12;
        var rect = new Rect(center.X - w / 2, center.Y - h / 2, w, h);
        dc.PushOpacity(a);
        dc.DrawRoundedRectangle(ChipFill, SegStroke, rect, h / 2, h / 2);
        dc.Pop();
        dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    // ================================================================ ヘルパー

    double SlotHover(int i) => Math.Max(Hover[i], i == EditingSlot ? 1 : 0);

    public static Point Polar(Point c, double r, double angleDeg)
    {
        double rad = angleDeg * Math.PI / 180;
        return new Point(c.X + r * Math.Sin(rad), c.Y - r * Math.Cos(rad));
    }

    /// <summary>角を丸めた扇形（ドーナツの 1 区画）。</summary>
    static Geometry Wedge(Point c, double r0, double r1, double a0, double a1)
    {
        const double Deg = 180 / Math.PI;
        double g0 = GapPx / 2 / r0 * Deg, g1 = GapPx / 2 / r1 * Deg;
        double k = CornerPx;
        double k0 = k / r0 * Deg, k1 = k / r1 * Deg;

        Point A = Polar(c, r0, a0 + g0), B = Polar(c, r1, a0 + g1), C = Polar(c, r1, a1 - g1), D = Polar(c, r0, a1 - g0);
        static Point Cut(Point from, Point to, double len)
        {
            var v = to - from; double l = v.Length;
            return l < 1e-6 ? from : from + v * (Math.Min(len, l / 2) / l);
        }

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            var start = Cut(A, B, k);
            ctx.BeginFigure(start, true, true);
            ctx.LineTo(Cut(B, A, k), true, true);
            ctx.QuadraticBezierTo(B, Polar(c, r1, a0 + g1 + k1), true, true);
            ctx.ArcTo(Polar(c, r1, a1 - g1 - k1), new Size(r1, r1), 0, false, SweepDirection.Clockwise, true, true);
            ctx.QuadraticBezierTo(C, Cut(C, D, k), true, true);
            ctx.LineTo(Cut(D, C, k), true, true);
            ctx.QuadraticBezierTo(D, Polar(c, r0, a1 - g0 - k0), true, true);
            ctx.ArcTo(Polar(c, r0, a0 + g0 + k0), new Size(r0, r0), 0, false, SweepDirection.Counterclockwise, true, true);
            ctx.QuadraticBezierTo(A, start, true, true);
        }
        geo.Freeze();
        return geo;
    }

    FormattedText Text(string s, double size, Brush brush, Typeface face, double maxWidth = 0)
    {
        var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush, pixelsPerDip);
        if (maxWidth > 0)
        {
            ft.MaxTextWidth = maxWidth;
            ft.MaxLineCount = 1;
            ft.Trimming = TextTrimming.CharacterEllipsis;
        }
        return ft;
    }

    /// <summary>文字色（ホバー量 h に応じて白へ寄せる）。</summary>
    Brush TextA(double a, double h = 0) => new SolidColorBrush(WithA(Lerp(TextBase, Colors.White, h), (byte)(255 * Ease.Clamp01(a))));
    static Color WithA(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
    static Color Lerp(Color a, Color b, double t)
    {
        t = Ease.Clamp01(t);
        return Color.FromArgb((byte)Ease.Lerp(a.A, b.A, t), (byte)Ease.Lerp(a.R, b.R, t), (byte)Ease.Lerp(a.G, b.G, t), (byte)Ease.Lerp(a.B, b.B, t));
    }
    static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
