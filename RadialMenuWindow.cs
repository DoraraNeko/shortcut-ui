using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ShortcutUI;

/// <summary>
/// ドーナツ型メニューを表示する透明なオーバーレイウィンドウ。アニメーションと操作を担当する。
/// </summary>
internal sealed class RadialMenuWindow : Window
{
    public enum ReleaseResult { Cancelled, Executed, Pinned }
    enum Mode { Idle, Gesture, Pinned, Closing }
    enum CloseKind { Cancel, Execute }

    const double WindowSizeDip = 620;
    const double SelectLimit = RadialMenuView.OuterR + 34;          // ここまでは区画を選択できる
    const double FadeStart = RadialMenuView.OuterR + 40;            // ここから薄くなり始め
    const double FadeEnd = RadialMenuView.OuterR + 150;             // ここで完全に消える

    readonly RadialMenuView view = new();
    readonly AppConfig config;
    readonly Stopwatch clock = Stopwatch.StartNew();
    IntPtr hwnd;
    Mode mode = Mode.Idle;
    Native.POINT centerPhys;
    IntPtr prevForeground;
    bool rendering;
    double lastFrame, openStart, pinStart, pinMaskFrom, closeStart, closeFromOpacity, closeFromScale;
    CloseKind closeKind;

    public event Action? Dismissed;
    public event Action? BecameIdle;
    public Action<string>? ShowError;
    public bool EditorOpen { get; private set; }

    double Now => clock.Elapsed.TotalSeconds;

    public RadialMenuWindow(AppConfig config)
    {
        this.config = config;
        Title = "Shortcut UI";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Left = Top = -20000;
        Content = view;
        view.Config = config;
        ApplyDesign();

        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            SetInteractive(false);
        };

        view.MouseLeftButtonUp += OnPinnedLeftUp;
        view.MouseRightButtonUp += OnPinnedRightUp;
    }

    // ================================================================ 公開操作

    /// <summary>長押しでメニューを展開する。</summary>
    public void OpenGesture(Native.POINT at)
    {
        PrepareOpen(at);
        mode = Mode.Gesture;
        view.Pinned = false;
        view.PinT = 0;
        SetInteractive(false);
    }

    /// <summary>編集（固定）モードでメニューを開く。</summary>
    public void OpenPinned(Native.POINT at)
    {
        PrepareOpen(at);
        EnterPinned(0);
    }

    public void SwitchPage() => SwitchPage(1);

    public void SwitchPage(int dir)
    {
        if (mode is not (Mode.Gesture or Mode.Pinned)) return;
        view.PrevPage = view.Page;
        int n = config.Pages.Count;
        if (n <= 1) return;
        view.Page = ((view.Page + dir) % n + n) % n;
        view.PageDir = dir >= 0 ? 1 : -1;
        view.PageT = 0;
        pageStart = Now;
    }
    double pageStart = -10;

    /// <summary>左ボタンが離された位置に応じて、固定・実行・キャンセルを決める。</summary>
    public ReleaseResult Release(Native.POINT at)
    {
        if (mode != Mode.Gesture) return ReleaseResult.Cancelled;
        var local = view.PointFromScreen(new Point(at.X, at.Y));
        int zone = HitZone(local, SelectLimit);

        if (zone == -1)
        {
            EnterPinned(view.MaskRadius);
            return ReleaseResult.Pinned;
        }
        if (zone >= 0 && ExecuteSlot(zone)) return ReleaseResult.Executed;
        BeginClose(CloseKind.Cancel, -1);
        return ReleaseResult.Cancelled;
    }

    /// <summary>長押し中の画面座標がどの区画にあるか（-1: 中央 / -2: 範囲外）。</summary>
    public int ZoneAtScreen(Native.POINT p)
    {
        if (mode != Mode.Gesture || PresentationSource.FromVisual(view) == null) return -2;
        return HitZone(view.PointFromScreen(new Point(p.X, p.Y)), SelectLimit);
    }

    /// <summary>長押し中に区画を右クリックしたとき: メニューを固定してその区画を編集する。</summary>
    public void PinAndEdit(int zone)
    {
        if (mode != Mode.Gesture) return;
        EnterPinned(view.MaskRadius);
        EditSlot(zone);
    }

    /// <summary>区画のショートカットを実行してメニューを閉じる。空の区画なら false。</summary>
    bool ExecuteSlot(int zone)
    {
        var slot = config.Pages[view.Page][zone];
        if (slot.IsEmpty) return false;
        BeginClose(CloseKind.Execute, zone);
        var s = slot.Clone();
        var target = prevForeground;
        // キー操作が元のウィンドウに届くよう、必要ならフォーカスを戻してから実行する
        bool restore = target != IntPtr.Zero && Native.GetForegroundWindow() != target;
        if (restore) Native.SetForegroundWindow(target);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(restore ? 60 : 1) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var err = ShortcutRunner.Run(s);
            if (err != null) ShowError?.Invoke(err);
        };
        timer.Start();
        return true;
    }

    public void ClosePinned()
    {
        if (mode != Mode.Pinned || EditorOpen) return;
        BeginClose(CloseKind.Cancel, -1);
    }

    /// <summary>画面座標がメニューの上にあるか。</summary>
    public bool ContainsScreenPoint(Native.POINT p)
    {
        if (mode == Mode.Idle || PresentationSource.FromVisual(view) == null) return false;
        var local = view.PointFromScreen(new Point(p.X, p.Y));
        return (local - view.Center).Length / Sz <= RadialMenuView.HitRadius;
    }

    DesignSettings D => config.Design;
    double Sz => D.Size;

    /// <summary>デザイン設定（色・サイズなど）を反映する。</summary>
    public void ApplyDesign()
    {
        view.Design = D;
        Width = Height = Math.Ceiling(WindowSizeDip * Sz);
        view.InvalidateVisual();
    }

    // ================================================================ 内部処理

    void PrepareOpen(Native.POINT at)
    {
        centerPhys = at;
        prevForeground = Native.GetForegroundWindow();
        MoveCenterTo(at);
        openStart = Now;
        pageStart = -10;
        view.Page = view.PrevPage = 0;
        view.PageT = 1;
        Array.Clear(view.Hover);
        view.CenterHover = 1;
        view.PointerAlpha = 0;
        view.Flash = -1;
        view.FlashT = 0;
        view.EditingSlot = -1;
        view.MaskEnabled = true;
        view.MaskRadius = 0;
        view.GlobalOpacity = 0;
        view.Scale = view.OpenScaleFrom;
        view.Rotation = view.OpenSpin;
        view.OpenT = 0;
        view.Visible = true;
        UpdatePositions();
        StartRendering();
    }

    void EnterPinned(double maskFrom)
    {
        mode = Mode.Pinned;
        pinStart = Now;
        pinMaskFrom = maskFrom;
        view.Pinned = true;
        view.PointerAlpha = 0;
        SetInteractive(true);   // フォーカスは奪わない（実行したキー操作が元のウィンドウに届くように）
    }

    void BeginClose(CloseKind kind, int flash)
    {
        mode = Mode.Closing;
        closeKind = kind;
        closeStart = Now;
        closeFromOpacity = view.GlobalOpacity;
        closeFromScale = view.Scale;
        view.Flash = flash;
        view.FlashT = 0;
        SetInteractive(false);
    }

    void GoIdle()
    {
        mode = Mode.Idle;
        view.Visible = false;
        view.Pinned = false;
        view.InvalidateVisual();
        StopRendering();
        SetInteractive(false);
    }

    void OnPinnedLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (mode != Mode.Pinned || EditorOpen) return;
        e.Handled = true;
        int zone = HitZone(e.GetPosition(view), RadialMenuView.HitRadius);
        if (zone == -1) ClosePinned();
        else if (zone >= 0 && !ExecuteSlot(zone)) EditSlot(zone);   // 空の区画はクリックで割り当て
    }

    void OnPinnedRightUp(object sender, MouseButtonEventArgs e)
    {
        if (mode != Mode.Pinned || EditorOpen) return;
        e.Handled = true;
        int zone = HitZone(e.GetPosition(view), RadialMenuView.HitRadius);
        if (zone >= 0) EditSlot(zone);
    }

    void EditSlot(int index)
    {
        EditorOpen = true;
        view.EditingSlot = index;
        int page = view.Page;
        try
        {
            double dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var dlg = new EditorWindow(config.Pages[page][index], page, index, centerPhys,
                                       (RadialMenuView.OuterR + RadialMenuView.PopOut) * dpiScale)
            { Owner = this };
            if (dlg.ShowDialog() == true && dlg.Result != null)
            {
                config.Pages[page][index] = dlg.Result;
                config.Save();
                IconCache.Clear();
            }
        }
        catch (Exception ex)
        {
            Log.Write("編集ダイアログでエラー", ex);
            ShowError?.Invoke("編集中にエラーが発生しました: " + ex.Message);
        }
        finally
        {
            EditorOpen = false;
            view.EditingSlot = -1;
        }
    }

    /// <summary>-1: 中央 / 0..7: 区画 / -2: 範囲外</summary>
    int HitZone(Point local, double limit)
    {
        var v = local - view.Center;
        double d = v.Length / Sz;
        if (d < RadialMenuView.InnerR - 4) return -1;
        if (d > limit) return -2;
        return SegmentAt(v);
    }

    static int SegmentAt(Vector v)
    {
        double a = AngleOf(v);
        return (int)Math.Floor(((a + RadialMenuView.SegAngle / 2) % 360) / RadialMenuView.SegAngle) % RadialMenuView.SlotCount;
    }

    static double AngleOf(Vector v)
    {
        double a = Math.Atan2(v.X, -v.Y) * 180 / Math.PI;
        return a < 0 ? a + 360 : a;
    }

    void MoveCenterTo(Native.POINT p)
    {
        if (hwnd == IntPtr.Zero) return;
        for (int pass = 0; pass < 2; pass++)  // DPI の異なるモニター間を移動するとサイズが変わるため 2 回合わせる
        {
            Native.GetWindowRect(hwnd, out var r);
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, p.X - w / 2, p.Y - h / 2, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
    }

    void SetInteractive(bool interactive)
    {
        if (hwnd == IntPtr.Zero) return;
        long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
        ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_LAYERED;
        ex |= Native.WS_EX_NOACTIVATE;
        if (interactive) ex &= ~Native.WS_EX_TRANSPARENT;
        else ex |= Native.WS_EX_TRANSPARENT;
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
    }

    void StartRendering()
    {
        if (rendering) return;
        rendering = true;
        lastFrame = Now;
        CompositionTarget.Rendering += OnFrame;
    }

    void StopRendering()
    {
        if (!rendering) return;
        rendering = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    void UpdatePositions()
    {
        if (PresentationSource.FromVisual(view) == null) return;
        Native.GetCursorPos(out var cp);
        view.Center = view.PointFromScreen(new Point(centerPhys.X, centerPhys.Y));
        view.CursorPos = view.PointFromScreen(new Point(cp.X, cp.Y));
    }

    // ================================================================ アニメーション

    void OnFrame(object? sender, EventArgs e)
    {
        double now = Now;
        double dt = Math.Clamp(now - lastFrame, 0, 0.1);
        lastFrame = now;
        UpdatePositions();

        double sp = D.AnimationSpeed;
        double t = (now - openStart) * sp;
        var v = view.CursorPos - view.Center;
        double d = v.Length / Sz;
        int hoverIdx = -2;

        // 展開: カーソルの先端からマスクが広がり、わずかに回転しながら拡大する
        double from = view.OpenScaleFrom;
        double openScale = from + (1 - from) * Ease.OutBack(t / 0.42, 1.1);
        double openRot = view.OpenSpin * (1 - Ease.OutQuint(t / view.OpenSpinDuration));
        view.Time = now;
        view.OpenT = t;
        double openFade = Ease.OutCubic(t / 0.16);

        switch (mode)
        {
            case Mode.Gesture:
            {
                view.MaskRadius = D.FadeRange * Sz * Ease.OutCubic(t / 0.5);
                view.Scale = openScale;
                view.Rotation = openRot;
                double distFade = 1 - Ease.SmoothStep(FadeStart, FadeEnd, d);
                view.GlobalOpacity = openFade * distFade;
                hoverIdx = HitZone(view.CursorPos, SelectLimit);
                Approach(ref view.PointerAlpha, d > RadialMenuView.InnerR - 4 && d < FadeEnd ? 1 : 0, dt, 14);
                if (distFade <= 0.001 && t > 0.12)
                {
                    GoIdle();
                    Dismissed?.Invoke();
                    return;
                }
                break;
            }
            case Mode.Pinned:
            {
                double pt = Ease.OutCubic((now - pinStart) * sp / 0.45);
                view.MaskRadius = Ease.Lerp(pinMaskFrom, 1500, pt);
                view.PinT = pt;
                view.Scale = openScale;
                view.Rotation = openRot;
                view.GlobalOpacity = openFade;
                hoverIdx = IsMouseOver || view.IsMouseOver ? HitZone(view.CursorPos, RadialMenuView.HitRadius) : -2;
                if (EditorOpen) hoverIdx = -2;
                Approach(ref view.PointerAlpha, 0, dt, 14);
                break;
            }
            case Mode.Closing:
            {
                double dur = closeKind == CloseKind.Execute ? 0.30 : 0.2;
                double ct = Ease.Clamp01((now - closeStart) * sp / dur);
                view.GlobalOpacity = closeFromOpacity * (1 - Ease.InOutCubic(ct));
                view.Scale = closeFromScale * (1 + (closeKind == CloseKind.Execute ? 0.06 : -0.06) * Ease.OutCubic(ct));
                view.FlashT = ct;
                hoverIdx = view.Flash;
                Approach(ref view.PointerAlpha, 0, dt, 18);
                if (ct >= 1)
                {
                    GoIdle();
                    BecameIdle?.Invoke();
                    return;
                }
                break;
            }
            default:
                StopRendering();
                return;
        }

        if (d > 4)
        {
            if (view.PointerAlpha < 0.05) view.PointerAngle = AngleOf(v);
            else ApproachAngle(ref view.PointerAngle, AngleOf(v), dt, 22);
        }

        double k = 1 - Math.Exp(-dt * 20);
        for (int i = 0; i < RadialMenuView.SlotCount; i++)
            view.Hover[i] += ((hoverIdx == i ? 1 : 0) - view.Hover[i]) * k;
        view.CenterHover += ((hoverIdx == -1 ? 1 : 0) - view.CenterHover) * k;

        view.PageT = Ease.Clamp01((now - pageStart) * sp / 0.34);
        view.InvalidateVisual();
    }

    static void Approach(ref double value, double target, double dt, double speed)
        => value += (target - value) * (1 - Math.Exp(-dt * speed));

    static void ApproachAngle(ref double value, double target, double dt, double speed)
    {
        double diff = ((target - value + 540) % 360) - 180;
        value = (value + diff * (1 - Math.Exp(-dt * speed)) + 360) % 360;
    }
}
