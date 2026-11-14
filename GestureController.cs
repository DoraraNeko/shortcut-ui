using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ShortcutUI;

/// <summary>
/// グローバルマウスフックでデスクトップ上の左クリック長押しを検出し、メニューを制御する。
/// </summary>
internal sealed class GestureController : IDisposable
{
    enum State { Idle, Pending, Active, Releasing, Dismissed, Pinned }

    readonly RadialMenuWindow menu;
    readonly AppConfig config;
    readonly Native.LowLevelMouseProc proc;   // GC されないよう保持
    readonly DispatcherTimer holdTimer;
    IntPtr hook;
    State state = State.Idle;
    Native.POINT downPt;
    bool swallowLeftUp, swallowRightUp;

    public bool Paused { get; set; }

    public GestureController(RadialMenuWindow menu, AppConfig config)
    {
        this.menu = menu;
        this.config = config;
        holdTimer = new DispatcherTimer(DispatcherPriority.Input);
        holdTimer.Tick += OnHold;

        menu.Dismissed += () => { if (state == State.Active) state = State.Dismissed; };
        menu.BecameIdle += () =>
        {
            if (state == State.Pinned) state = swallowLeftUp ? State.Dismissed : State.Idle;
            swallowRightUp = false;
        };

        proc = HookProc;
        hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, proc, Native.GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero)
            throw new InvalidOperationException("マウスフックを設定できませんでした (error " + Marshal.GetLastWin32Error() + ")");
    }

    /// <summary>タスクトレイなどから編集モードでメニューを開く。</summary>
    public void OpenEditor(Native.POINT at)
    {
        if (state != State.Idle) return;
        state = State.Pinned;
        swallowLeftUp = swallowRightUp = false;
        menu.OpenPinned(at);
    }

    IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
            if (info.dwExtraInfo != Native.InjectMarker)
            {
                try
                {
                    if (Handle((int)wParam, info.pt, info.mouseData)) return (IntPtr)1; // 入力を握りつぶす
                }
                catch (Exception ex) { Log.Write("フック処理でエラー", ex); }
            }
        }
        return Native.CallNextHookEx(hook, nCode, wParam, lParam);
    }

    static int WheelDir(uint mouseData) => (short)(mouseData >> 16) < 0 ? 1 : -1;

    bool Handle(int msg, Native.POINT pt, uint mouseData)
    {
        switch (state)
        {
            case State.Idle:
                if (msg == Native.WM_LBUTTONDOWN && !Paused && IsDesktopAt(pt))
                {
                    state = State.Pending;
                    downPt = pt;
                    holdTimer.Interval = TimeSpan.FromMilliseconds(config.HoldMilliseconds);
                    holdTimer.Start();
                }
                return false;

            case State.Pending:
                if (msg == Native.WM_MOUSEMOVE)
                {
                    int dx = pt.X - downPt.X, dy = pt.Y - downPt.Y, tol = config.MoveTolerance;
                    if (dx * dx + dy * dy > tol * tol) CancelPending();
                }
                else if (msg is Native.WM_LBUTTONUP or Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN)
                {
                    CancelPending();
                }
                return false;

            case State.Active:
                switch (msg)
                {
                    case Native.WM_MOUSEWHEEL:
                        int dir = WheelDir(mouseData);
                        menu.Dispatcher.BeginInvoke(new Action(() => menu.SwitchPage(dir)));
                        return true;
                    case Native.WM_RBUTTONDOWN:
                        // 押したまま区画を右クリック → その区画を編集
                        int zone = menu.ZoneAtScreen(pt);
                        if (zone >= 0)
                        {
                            state = State.Pinned;
                            swallowLeftUp = swallowRightUp = true;
                            menu.Dispatcher.BeginInvoke(new Action(() => menu.PinAndEdit(zone)));
                        }
                        return true;
                    case Native.WM_RBUTTONUP:
                    case Native.WM_LBUTTONDOWN:
                        return true;
                    case Native.WM_LBUTTONUP:
                        state = State.Releasing;
                        var p = pt;
                        menu.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            var r = menu.Release(p);
                            state = r == RadialMenuWindow.ReleaseResult.Pinned ? State.Pinned : State.Idle;
                        }));
                        return true;
                }
                return false;

            case State.Releasing:
                return msg is Native.WM_RBUTTONDOWN or Native.WM_RBUTTONUP or Native.WM_MOUSEWHEEL;

            case State.Dismissed:
                if (msg == Native.WM_LBUTTONUP) { state = State.Idle; return true; }
                return msg is Native.WM_RBUTTONDOWN or Native.WM_RBUTTONUP or Native.WM_LBUTTONDOWN or Native.WM_MOUSEWHEEL;

            case State.Pinned:
                // 長押し中に右クリック編集へ移った場合、後から来るボタン解放は握りつぶす
                if (msg == Native.WM_LBUTTONUP && swallowLeftUp) { swallowLeftUp = false; return true; }
                if (msg == Native.WM_RBUTTONUP && swallowRightUp) { swallowRightUp = false; return true; }
                if (msg == Native.WM_MOUSEWHEEL && !menu.EditorOpen && menu.ContainsScreenPoint(pt))
                {
                    int d = WheelDir(mouseData);
                    menu.Dispatcher.BeginInvoke(new Action(() => menu.SwitchPage(d)));
                    return true;
                }
                if (msg is Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN
                    && !menu.EditorOpen && !menu.ContainsScreenPoint(pt))
                {
                    menu.Dispatcher.BeginInvoke(new Action(menu.ClosePinned));
                }
                return false;
        }
        return false;
    }

    void CancelPending()
    {
        holdTimer.Stop();
        state = State.Idle;
    }

    void OnHold(object? sender, EventArgs e)
    {
        holdTimer.Stop();
        if (state != State.Pending) return;
        state = State.Active;
        menu.OpenGesture(downPt);

        // デスクトップ側の押下状態（範囲選択など）を解除するため、左ボタンの解放を送る。
        // 実際の解放イベントは後でフック側で握りつぶす。
        var inputs = new[]
        {
            new Native.INPUT
            {
                type = Native.INPUT_MOUSE,
                U = new Native.InputUnion
                {
                    mi = new Native.MOUSEINPUT { dwFlags = Native.MOUSEEVENTF_LEFTUP, dwExtraInfo = Native.InjectMarker },
                },
            },
        };
        Native.SendInput(1, inputs, Marshal.SizeOf<Native.INPUT>());
    }

    static bool IsDesktopAt(Native.POINT pt)
    {
        var h = Native.WindowFromPoint(pt);
        if (h == IntPtr.Zero) return false;
        var root = Native.GetAncestor(h, Native.GA_ROOT);
        var cls = Native.ClassNameOf(root == IntPtr.Zero ? h : root);
        return cls is "Progman" or "WorkerW";
    }

    public void Dispose()
    {
        holdTimer.Stop();
        if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
    }
}
