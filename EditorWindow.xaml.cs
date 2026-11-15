using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WinForms = System.Windows.Forms;

namespace ShortcutUI;

public partial class EditorWindow : Window
{
    public Slot? Result { get; private set; }

    readonly Native.POINT anchor;
    readonly double anchorRadiusPx;
    Key capturedKey = Key.None;
    string glyph;
    bool suppressGlyphReset;
    /// <summary>最後に自動で入れた表示名。表示名がこれと同じ（または空）の間は自動更新を続ける。</summary>
    string lastAutoName = "";

    static readonly (string Name, string Keys)[] KeyPresetList =
    {
        ("エクスプローラー", "Win+E"),
        ("設定", "Win+I"),
        ("スクリーンショット", "Win+Shift+S"),
        ("クリップボード履歴", "Win+V"),
        ("タスクマネージャー", "Ctrl+Shift+Escape"),
        ("デスクトップ表示", "Win+D"),
        ("タスクビュー", "Win+Tab"),
        ("ファイル名を指定して実行", "Win+R"),
        ("新しい仮想デスクトップ", "Win+Ctrl+D"),
        ("絵文字", "Win+OemPeriod"),
        ("コピー", "Ctrl+C"),
        ("貼り付け", "Ctrl+V"),
        ("元に戻す", "Ctrl+Z"),
        ("再生 / 一時停止", "MediaPlayPause"),
        ("次の曲", "MediaNextTrack"),
        ("ミュート", "VolumeMute"),
    };

    static readonly (string Name, string Target, string Args, string Glyph)[] LaunchPresetList =
    {
        ("メモ帳", "notepad.exe", "", ""),
        ("電卓", "calc.exe", "", ""),
        ("ペイント", "mspaint.exe", "", ""),
        ("ターミナル", "wt.exe", "", Glyphs.Terminal),
        ("コマンドプロンプト", "cmd.exe", "", ""),
        ("ダウンロード", @"%USERPROFILE%\Downloads", "", ""),
        ("ドキュメント", @"%USERPROFILE%\Documents", "", ""),
        ("ごみ箱", "explorer.exe", "shell:RecycleBinFolder", Glyphs.Delete),
        ("PCをロック", "rundll32.exe", "user32.dll,LockWorkStation", Glyphs.Lock),
        ("Google", "https://www.google.com", "", Glyphs.Globe),
    };

    internal EditorWindow(Slot slot, int page, int index, Native.POINT anchor, double anchorRadiusPx)
    {
        InitializeComponent();
        this.anchor = anchor;
        this.anchorRadiusPx = anchorRadiusPx;
        glyph = slot.Glyph;

        SubTitle.Text = $"ページ {page + 1}  ·  区画 {index + 1}";
        NameBox.Text = slot.Name;
        ClearButton.Visibility = slot.IsEmpty ? Visibility.Collapsed : Visibility.Visible;

        var combo = KeyCombo.Parse(slot.Keys);
        ModWin.IsChecked = combo.Win;
        ModCtrl.IsChecked = combo.Ctrl;
        ModShift.IsChecked = combo.Shift;
        ModAlt.IsChecked = combo.Alt;
        capturedKey = combo.Key;

        // 既存の表示名が自動の名前と同じなら、パスを変えたときに追従させる
        if (slot.Kind == SlotKind.Launch) lastAutoName = Slot.AutoName(slot.Target);
        suppressGlyphReset = true;
        TargetBox.Text = slot.Target;
        suppressGlyphReset = false;
        ArgsBox.Text = slot.Arguments;

        if (slot.Kind == SlotKind.Launch) KindLaunch.IsChecked = true; else KindKeys.IsChecked = true;

        foreach (var p in KeyPresetList)
        {
            var b = new Button { Content = p.Name, Style = (Style)FindResource("Chip"), ToolTip = KeyCombo.Parse(p.Keys).ToDisplay() };
            b.Click += (_, _) => ApplyKeyPreset(p.Name, p.Keys);
            KeyPresets.Children.Add(b);
        }
        foreach (var p in LaunchPresetList)
        {
            var b = new Button { Content = p.Name, Style = (Style)FindResource("Chip"), ToolTip = (p.Target + " " + p.Args).Trim() };
            b.Click += (_, _) => ApplyLaunchPreset(p.Name, p.Target, p.Args, p.Glyph);
            LaunchPresets.Children.Add(b);
        }

        UpdatePreview();
        Root.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        KeyDown += OnWindowKeyDown;
        ContentRendered += (_, _) => PlaceAndFadeIn();
    }

    // ---------------------------------------------------------------- 配置

    void PlaceAndFadeIn()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.GetWindowRect(hwnd, out var r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        var wa = WinForms.Screen.FromPoint(new System.Drawing.Point(anchor.X, anchor.Y)).WorkingArea;

        int gap = (int)(8 * VisualTreeHelper.GetDpi(this).DpiScaleX);
        int x = anchor.X + (int)anchorRadiusPx + gap;
        if (x + w > wa.Right) x = anchor.X - (int)anchorRadiusPx - gap - w;
        if (x < wa.Left) x = Math.Max(wa.Left, Math.Min(wa.Right - w, anchor.X - w / 2));
        int y = Math.Max(wa.Top, Math.Min(wa.Bottom - h, anchor.Y - h / 2));
        Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        if (KindKeys.IsChecked == true) NameBox.Focus(); else TargetBox.Focus();
    }

    // ---------------------------------------------------------------- 入力

    void OnKindChanged(object sender, RoutedEventArgs e)
    {
        if (KeysPanel == null || LaunchPanel == null) return;
        bool keys = KindKeys.IsChecked == true;
        KeysPanel.Visibility = keys ? Visibility.Visible : Visibility.Collapsed;
        LaunchPanel.Visibility = keys ? Visibility.Collapsed : Visibility.Visible;
        HeaderGlyph.Text = keys ? Glyphs.Keyboard : "";
        ErrorText.Visibility = Visibility.Collapsed;
    }

    void OnKeyBoxKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key,
        };
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None)
            return;

        var mods = Keyboard.Modifiers;
        if (mods != ModifierKeys.None)
        {
            ModCtrl.IsChecked = mods.HasFlag(ModifierKeys.Control);
            ModShift.IsChecked = mods.HasFlag(ModifierKeys.Shift);
            ModAlt.IsChecked = mods.HasFlag(ModifierKeys.Alt);
            if (mods.HasFlag(ModifierKeys.Windows)) ModWin.IsChecked = true;
        }
        capturedKey = key;
        glyph = "";
        UpdatePreview();
    }

    void OnModClick(object sender, RoutedEventArgs e)
    {
        glyph = "";
        UpdatePreview();
    }

    void OnTargetChanged(object sender, TextChangedEventArgs e)
    {
        if (!suppressGlyphReset) glyph = "";
        if (NameBox == null) return;

        // 表示名が空か、前回自動で入れた名前のままなら、ファイル名（拡張子なし）で更新する。
        // ユーザーが表示名を書き換えた後は上書きしない（空にすると自動に戻る）。
        var current = NameBox.Text.Trim();
        if (current.Length == 0 || current == lastAutoName)
        {
            var auto = Slot.AutoName(TargetBox.Text);
            NameBox.Text = auto;
            lastAutoName = auto;
        }
    }

    void ApplyKeyPreset(string name, string keys)
    {
        var c = KeyCombo.Parse(keys);
        ModWin.IsChecked = c.Win;
        ModCtrl.IsChecked = c.Ctrl;
        ModShift.IsChecked = c.Shift;
        ModAlt.IsChecked = c.Alt;
        capturedKey = c.Key;
        glyph = "";
        NameBox.Text = name;
        lastAutoName = name;   // プリセット名は自動の名前として扱う
        UpdatePreview();
    }

    void ApplyLaunchPreset(string name, string target, string args, string g)
    {
        NameBox.Text = "";   // 自動入力を許可してからパスを入れ、プリセット名で上書きする
        suppressGlyphReset = true;
        TargetBox.Text = target;
        suppressGlyphReset = false;
        ArgsBox.Text = args;
        NameBox.Text = name;
        lastAutoName = name;  // プリセット名も「自動の名前」として扱い、パス変更時は追従させる
        glyph = g;
    }

    KeyCombo CurrentCombo() => new()
    {
        Win = ModWin.IsChecked == true,
        Ctrl = ModCtrl.IsChecked == true,
        Shift = ModShift.IsChecked == true,
        Alt = ModAlt.IsChecked == true,
        Key = capturedKey,
    };

    void UpdatePreview()
    {
        KeyBox.Text = capturedKey == Key.None ? "キーを押す…" : KeyCombo.KeyName(capturedKey);
        var c = CurrentCombo();
        ComboPreview.Text = c.IsValid ? c.ToDisplay() : "—";
        ErrorText.Visibility = Visibility.Collapsed;
    }

    void OnBrowseFile(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "起動するファイルを選択",
            Filter = "すべてのファイル (*.*)|*.*|アプリケーション (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd",
            DereferenceLinks = false,
        };
        if (dlg.ShowDialog(this) == true) TargetBox.Text = dlg.FileName;   // 表示名は OnTargetChanged で自動入力
    }

    void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "開くフォルダを選択" };
        if (dlg.ShowDialog(this) == true) TargetBox.Text = dlg.FolderName;   // 表示名は OnTargetChanged で自動入力
    }

    void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { OnCancel(this, e); e.Handled = true; }
        else if (e.Key == Key.Enter) { OnSave(this, e); e.Handled = true; }
    }

    // ---------------------------------------------------------------- 確定

    void OnSave(object sender, RoutedEventArgs e)
    {
        var slot = new Slot { Name = NameBox.Text.Trim(), Glyph = glyph };
        if (KindKeys.IsChecked == true)
        {
            var c = CurrentCombo();
            if (!c.IsValid) { ShowError("キーを入力してください。"); return; }
            slot.Kind = SlotKind.Keys;
            slot.Keys = c.ToString();
        }
        else
        {
            var target = TargetBox.Text.Trim();
            if (target.Length == 0) { ShowError("起動する対象を入力してください。"); return; }
            slot.Kind = SlotKind.Launch;
            slot.Target = target;
            slot.Arguments = ArgsBox.Text.Trim();
        }
        Result = slot;
        DialogResult = true;
    }

    void OnClear(object sender, RoutedEventArgs e)
    {
        Result = new Slot();
        DialogResult = true;
    }

    void OnCancel(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
    }

    void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
