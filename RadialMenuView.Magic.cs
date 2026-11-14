using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace ShortcutUI;

/// <summary>「魔法陣」スタイルの描画（回転するルーンの輪・八芒星・電流エフェクト）。</summary>
internal sealed partial class RadialMenuView
{
    /// <summary>エルダー・フサルク（ルーン文字）。</summary>
    const string Runes = "ᚠᚢᚦᚨᚱᚲᚷᚹᚺᚾᛁᛃᛇᛈᛉᛊᛏᛒᛖᛗᛚᛜᛞᛟ";
    static readonly Typeface RuneFace = new(new FontFamily("Segoe UI Historic, Segoe UI Symbol"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public bool IsMagic => design.Style == DesignSettings.MagicStyle;
    /// <summary>展開時の回転量（度）。魔法陣は大きく回りながら開く。</summary>
    public double OpenSpin => IsMagic ? -240 : -18;
    public double OpenSpinDuration => IsMagic ? 0.95 : 0.55;
    public double OpenScaleFrom => IsMagic ? 0.55 : 0.8;

    double Reveal(double start, double dur) => Ease.OutCubic((OpenT - start) / dur);

    // ================================================================ 魔法陣の背景

    void DrawMagicBackdrop(DrawingContext dc, Point c)
    {
        // 背後の淡い光
        double G = OuterR + 80;
        var glow = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = c, GradientOrigin = c, RadiusX = G, RadiusY = G };
        glow.GradientStops.Add(new GradientStop(WithA(AccentA, 0), 0.25));
        glow.GradientStops.Add(new GradientStop(WithA(AccentA, 46), (OuterR + 20) / G));
        glow.GradientStops.Add(new GradientStop(WithA(AccentB, 0), 1));
        dc.PushOpacity(Reveal(0.1, 0.6));
        dc.DrawEllipse(glow, null, c, G, G);
        dc.Pop();

        double rOuter = OuterR + 20, rRunes = OuterR + 31, rOuter2 = OuterR + 42, rTicks = OuterR + 50;

        // 外周の二重円: 描き込まれるように円弧が伸びる
        GlowArc(dc, c, rOuter, Time * 6, Reveal(0.0, 0.6), AccentB, 1.5, 0.9);
        GlowArc(dc, c, rOuter2, -Time * 4 + 180, Reveal(0.12, 0.6), AccentA, 1.3, 0.85);
        GlowArc(dc, c, rOuter2 + 4, -Time * 4 + 90, Reveal(0.2, 0.6), AccentB, 0.7, 0.5);

        // ルーン文字の輪
        int n = Runes.Length;
        double runeRot = Time * 9;
        for (int i = 0; i < n; i++)
        {
            double a = runeRot + i * 360.0 / n;
            double alpha = Reveal(0.22 + i * 0.018, 0.3);
            if (alpha <= 0.01) continue;
            var p = Polar(c, rRunes, a);
            var ft = Text(Runes[i].ToString(), 13, new SolidColorBrush(WithA(Lerp(AccentB, Colors.White, 0.35), (byte)(230 * alpha))), RuneFace);
            dc.PushTransform(new RotateTransform(a, p.X, p.Y));
            dc.DrawText(ft, new Point(p.X - ft.Width / 2, p.Y - ft.Height / 2));
            dc.Pop();
        }

        // 目盛りの輪
        double tickAlpha = Reveal(0.3, 0.5);
        if (tickAlpha > 0.01)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                for (int i = 0; i < 96; i++)
                {
                    double a = -Time * 3 + i * 3.75;
                    double len = i % 12 == 0 ? 9 : i % 3 == 0 ? 5 : 2.5;
                    ctx.BeginFigure(Polar(c, rTicks, a), false, false);
                    ctx.LineTo(Polar(c, rTicks + len, a), true, false);
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(WithA(AccentB, (byte)(170 * tickAlpha))), 1), geo);
        }

        // 八芒星 {8/3}: 区画の隙間からのぞく
        double starAlpha = Reveal(0.28, 0.55);
        if (starAlpha > 0.01)
        {
            double starRot = -Time * 7;
            var pts = new Point[8];
            for (int i = 0; i < 8; i++) pts[i] = Polar(c, rOuter, starRot + i * 45);
            var star = new StreamGeometry();
            using (var ctx = star.Open())
            {
                ctx.BeginFigure(pts[0], false, true);
                for (int k = 1; k < 8; k++) ctx.LineTo(pts[(k * 3) % 8], true, false);
            }
            star.Freeze();
            // 区画やラベルの邪魔にならないよう、光らせずに細く淡く描く
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(WithA(AccentA, (byte)(55 * starAlpha))), 0.8) { LineJoin = PenLineJoin.Round }, star);
            foreach (var p in pts)
            {
                dc.DrawEllipse(new SolidColorBrush(WithA(Color.FromRgb(14, 12, 28), (byte)(160 * starAlpha))),
                               new Pen(new SolidColorBrush(WithA(AccentB, (byte)(110 * starAlpha))), 1), p, 3.2, 3.2);
            }
        }

        // 内側の点線リング
        double innerAlpha = Reveal(0.05, 0.45);
        if (innerAlpha > 0.01)
        {
            var pen = new Pen(new SolidColorBrush(WithA(AccentB, (byte)(160 * innerAlpha))), 1.1) { DashStyle = new DashStyle(new double[] { 2, 3 }, Time * 4) };
            dc.DrawEllipse(null, pen, c, InnerR - 7, InnerR - 7);
        }
    }

    // ================================================================ 電流エフェクト

    /// <summary>
    /// 中央の円の縁を走る電流、カーソル周りの炎のような電流の輪、そしてそれらを結ぶ稲妻。
    /// 回転・拡大の変換の外（画面と同じ座標）で描く。
    /// </summary>
    void DrawElectric(DrawingContext dc, Point c)
    {
        double alpha = Reveal(0.3, 0.45);
        if (alpha <= 0.01) return;
        double k = Scale * SizeK;
        // 稲妻のちらつき: 約 30 回/秒で形を作り直す
        var rnd = new Random(unchecked((int)(Time * 30) * 7919 + 101));

        // ---- 中央の縁でビリビリする電流 ----
        double cr = (CenterR + 3 + CenterHover * 3) * k;
        for (int ring = 0; ring < 2; ring++)
        {
            var pts = new List<Point>();
            int n = 44;
            double phase = rnd.NextDouble() * 360;
            for (int i = 0; i < n; i++)
            {
                double a = phase + i * 360.0 / n;
                double r = cr + (rnd.NextDouble() * 2 - 1) * 2.6 * k + (rnd.NextDouble() < 0.12 ? rnd.NextDouble() * 5 * k : 0);
                pts.Add(Polar(c, r, a));
            }
            GlowPolyline(dc, pts, true, AccentB, (ring == 0 ? 1.3 : 0.8) * k, alpha * (ring == 0 ? 0.95 : 0.55));
        }
        // 縁から外へ飛ぶ小さな火花
        for (int i = 0; i < 3; i++)
        {
            double a = rnd.NextDouble() * 360;
            var from = Polar(c, cr, a);
            var to = Polar(c, cr + (7 + rnd.NextDouble() * 9) * k, a + (rnd.NextDouble() * 2 - 1) * 14);
            GlowPolyline(dc, Bolt(from, to, 3 * k, 3, rnd), false, AccentB, 0.7 * k, alpha * 0.8);
        }

        // ---- カーソルと結ぶ稲妻・カーソル周りの炎状の電流の輪 ----
        var v = CursorPos - c;
        double d = v.Length;
        double ringR = 13 * SizeK;
        if (d < cr + ringR * 0.6 || d > (OuterR + 160) * SizeK) return;

        double near = Ease.SmoothStep(cr + ringR * 0.6, cr + ringR * 2.2, d);   // 中央に近いうちはじわっと出す
        var dir = v / d;
        var start = c + dir * cr;
        var end = CursorPos - dir * ringR;
        double len = (end - start).Length;

        if (len > 2)
        {
            // メインの稲妻 + 細い副流
            GlowPolyline(dc, Bolt(start, end, Math.Min(26, len * 0.2), 6, rnd), false, AccentB, 1.5 * SizeK, alpha * near);
            GlowPolyline(dc, Bolt(start, end, Math.Min(34, len * 0.28), 5, rnd), false, AccentA, 0.7 * SizeK, alpha * near * 0.7);
            // 枝分かれ
            var main = Bolt(start, end, Math.Min(22, len * 0.18), 4, rnd);
            for (int b = 0; b < 2; b++)
            {
                if (rnd.NextDouble() < 0.35) continue;
                var from = main[1 + rnd.Next(Math.Max(1, main.Count - 2))];
                double ang = Math.Atan2(dir.Y, dir.X) + (rnd.NextDouble() * 2 - 1) * 1.2;
                double bl = (10 + rnd.NextDouble() * 22) * SizeK;
                var to = from + new Vector(Math.Cos(ang), Math.Sin(ang)) * bl;
                GlowPolyline(dc, Bolt(from, to, bl * 0.25, 3, rnd), false, AccentB, 0.6 * SizeK, alpha * near * 0.75);
            }
        }

        DrawFlameRing(dc, CursorPos, ringR, rnd, alpha * near);
    }

    /// <summary>カーソルを囲む、炎のように揺らめく電流の輪。</summary>
    void DrawFlameRing(DrawingContext dc, Point p, double r, Random rnd, double alpha)
    {
        if (alpha <= 0.01) return;

        // 内側の光
        var core = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = p, GradientOrigin = p, RadiusX = r * 2.2, RadiusY = r * 2.2 };
        core.GradientStops.Add(new GradientStop(WithA(AccentB, (byte)(110 * alpha)), 0));
        core.GradientStops.Add(new GradientStop(WithA(AccentA, (byte)(60 * alpha)), 0.45));
        core.GradientStops.Add(new GradientStop(WithA(AccentA, 0), 1));
        dc.DrawEllipse(core, null, p, r * 2.2, r * 2.2);

        // 炎の舌: 上向きほど長く、時間で揺らめく
        int n = 40;
        var flame = new List<Point>(n);
        for (int i = 0; i < n; i++)
        {
            double a = i * 360.0 / n;
            double up = (1 + Math.Cos(a * Math.PI / 180)) / 2;          // 真上で 1
            double wave = Math.Max(0, Math.Sin(a * Math.PI / 180 * 6 + Time * 11));
            double tongue = wave * (3 + 9 * up) * (0.6 + 0.4 * rnd.NextDouble());
            double jitter = (rnd.NextDouble() * 2 - 1) * 1.6;
            flame.Add(Polar(p, r + 2 + tongue * SizeK + jitter, a));
        }
        var flameGeo = Polyline(flame, true);
        var fill = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = p, GradientOrigin = p, RadiusX = r * 2, RadiusY = r * 2 };
        fill.GradientStops.Add(new GradientStop(WithA(AccentA, 0), 0.45));
        fill.GradientStops.Add(new GradientStop(WithA(AccentA, (byte)(90 * alpha)), 0.6));
        fill.GradientStops.Add(new GradientStop(WithA(AccentB, 0), 1));
        dc.DrawGeometry(fill, null, flameGeo);
        GlowGeometry(dc, flameGeo, AccentA, 1.0 * SizeK, alpha * 0.8);

        // 電流の輪（ギザギザの円 2 本）
        for (int ring = 0; ring < 2; ring++)
        {
            var pts = new List<Point>();
            double phase = rnd.NextDouble() * 360;
            for (int i = 0; i < 28; i++)
            {
                double a = phase + i * 360.0 / 28;
                pts.Add(Polar(p, r + (rnd.NextDouble() * 2 - 1) * 2.2 * SizeK, a));
            }
            GlowPolyline(dc, pts, true, AccentB, (ring == 0 ? 1.4 : 0.7) * SizeK, alpha * (ring == 0 ? 1 : 0.6));
        }
    }

    // ================================================================ ヘルパー

    /// <summary>中点変位法で稲妻の折れ線を作る。</summary>
    static List<Point> Bolt(Point a, Point b, double displace, int depth, Random rnd)
    {
        var pts = new List<Point> { a, b };
        for (int lvl = 0; lvl < depth; lvl++)
        {
            var next = new List<Point>(pts.Count * 2);
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var p = pts[i];
                var q = pts[i + 1];
                next.Add(p);
                var dv = q - p;
                var nrm = new Vector(-dv.Y, dv.X);
                if (nrm.Length > 1e-6) nrm.Normalize();
                next.Add(p + dv / 2 + nrm * ((rnd.NextDouble() * 2 - 1) * displace));
            }
            next.Add(pts[^1]);
            pts = next;
            displace *= 0.52;
        }
        return pts;
    }

    static Geometry Polyline(IList<Point> pts, bool closed)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(pts[0], false, closed);
            for (int i = 1; i < pts.Count; i++) ctx.LineTo(pts[i], true, false);
        }
        geo.Freeze();
        return geo;
    }

    void GlowPolyline(DrawingContext dc, IList<Point> pts, bool closed, Color color, double width, double alpha)
    {
        if (pts.Count < 2 || alpha <= 0.01) return;
        GlowGeometry(dc, Polyline(pts, closed), color, width, alpha);
    }

    /// <summary>太さと透明度の違う線を重ねて光っているように見せる。</summary>
    static void GlowGeometry(DrawingContext dc, Geometry geo, Color color, double width, double alpha)
    {
        alpha = Ease.Clamp01(alpha);
        if (alpha <= 0.01) return;
        Pen P(Color col, double w, double a) => new(new SolidColorBrush(WithA(col, (byte)(255 * Ease.Clamp01(a)))), w)
        { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, P(color, width * 6, alpha * 0.08), geo);
        dc.DrawGeometry(null, P(color, width * 3, alpha * 0.22), geo);
        dc.DrawGeometry(null, P(color, width, alpha * 0.95), geo);
        dc.DrawGeometry(null, P(Colors.White, Math.Max(0.5, width * 0.45), alpha * 0.85), geo);
    }

    /// <summary>円を始点から sweep (0〜1) の割合だけ描く。</summary>
    static void GlowArc(DrawingContext dc, Point c, double r, double startDeg, double sweep, Color color, double width, double alpha)
    {
        sweep = Ease.Clamp01(sweep);
        if (sweep <= 0.002) return;
        Geometry geo;
        if (sweep >= 0.999) geo = new EllipseGeometry(c, r, r);
        else
        {
            var sg = new StreamGeometry();
            using (var ctx = sg.Open())
            {
                double end = startDeg + 360 * sweep;
                ctx.BeginFigure(Polar(c, r, startDeg), false, false);
                ctx.ArcTo(Polar(c, r, end), new Size(r, r), 0, 360 * sweep > 180, SweepDirection.Clockwise, true, false);
            }
            geo = sg;
        }
        geo.Freeze();
        GlowGeometry(dc, geo, color, width, alpha);
    }
}
