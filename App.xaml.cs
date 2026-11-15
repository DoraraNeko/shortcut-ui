using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace ShortcutUI;

public partial class App : System.Windows.Application
{
    Mutex? mutex;
    AppConfig config = null!;
    RadialMenuWindow? menu;
    GestureController? gesture;
    WinForms.NotifyIcon? tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length >= 2 && e.Args[0] == "--snapshot")
        {
            Snapshot.Render(e.Args[1]);
            Shutdown();
            return;
        }

        mutex = new Mutex(true, @"Local\ShortcutUI.SingleInstance", out bool created);
        if (!created)
        {
            System.Windows.MessageBox.Show("Shortcut UI はすでに起動しています。", "Shortcut UI");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Write("未処理の例外", ex.Exception);
            ex.Handled = true;
        };

        config = AppConfig.Load();
        menu = new RadialMenuWindow(config);
        menu.Show();
        menu.ShowError = msg => tray?.ShowBalloonTip(4000, "Shortcut UI", msg, WinForms.ToolTipIcon.Warning);

        try
        {
            gesture = new GestureController(menu, config);
        }
        catch (Exception ex)
        {
            Log.Write("起動に失敗", ex);
            System.Windows.MessageBox.Show(ex.Message, "Shortcut UI", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        SetupTray();
    }

    void SetupTray()
    {
        tray = new WinForms.NotifyIcon
        {
            Icon = TrayIcon.Create(),
            Text = "Shortcut UI — デスクトップを長押し",
            Visible = true,
        };

        var cms = new WinForms.ContextMenuStrip();
        cms.Items.Add("ショートカットを編集", null, (_, _) => OpenEditor());
        cms.Items.Add("デザイン設定…", null, (_, _) => OpenDesign());

        var addPage = new WinForms.ToolStripMenuItem("ページを追加");
        addPage.Click += (_, _) => AddPage();
        cms.Items.Add(addPage);
        cms.Opening += (_, _) =>
        {
            addPage.Text = $"ページを追加（現在 {config.Pages.Count} ページ）";
            addPage.Enabled = config.Pages.Count < AppConfig.MaxPageCount;
        };

        var pause = new WinForms.ToolStripMenuItem("一時停止") { CheckOnClick = true };
        pause.CheckedChanged += (_, _) => { if (gesture != null) gesture.Paused = pause.Checked; };
        cms.Items.Add(pause);

        var startup = new WinForms.ToolStripMenuItem("Windows 起動時に実行") { Checked = StartupRegistration.IsEnabled };
        startup.Click += (_, _) =>
        {
            StartupRegistration.IsEnabled = !StartupRegistration.IsEnabled;
            startup.Checked = StartupRegistration.IsEnabled;
        };
        cms.Items.Add(startup);

        cms.Items.Add("設定フォルダーを開く", null, (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{AppConfig.FilePath}\"") { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("設定フォルダーを開けません", ex); }
        });
        cms.Items.Add(new WinForms.ToolStripSeparator());
        cms.Items.Add("終了", null, (_, _) => Shutdown());

        tray.ContextMenuStrip = cms;
        tray.MouseDoubleClick += (_, _) => OpenEditor();
        tray.ShowBalloonTip(3500, "Shortcut UI",
            "デスクトップを長押しするとメニューが開きます。中央で離すと編集モードになります。",
            WinForms.ToolTipIcon.None);
    }

    void AddPage()
    {
        if (!config.AddPage())
        {
            tray?.ShowBalloonTip(3000, "Shortcut UI", $"ページは最大 {AppConfig.MaxPageCount} ページまでです。", WinForms.ToolTipIcon.Info);
            return;
        }
        config.Save();
        tray?.ShowBalloonTip(3000, "Shortcut UI",
            $"ページ {config.Pages.Count} を追加しました。メニューを開いてホイールで切り替え、区画を右クリックして割り当てられます。",
            WinForms.ToolTipIcon.None);
    }

    DesignWindow? designWindow;

    void OpenDesign()
    {
        if (designWindow != null) { designWindow.Activate(); return; }
        designWindow = new DesignWindow(config, () => menu?.ApplyDesign());
        designWindow.Closed += (_, _) => designWindow = null;
        designWindow.Show();
        designWindow.Activate();
    }

    void OpenEditor()
    {
        Native.GetCursorPos(out var cp);
        var wa = WinForms.Screen.FromPoint(new Drawing.Point(cp.X, cp.Y)).WorkingArea;
        gesture?.OpenEditor(new Native.POINT(wa.Left + wa.Width / 2, wa.Top + wa.Height / 2));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        gesture?.Dispose();
        if (tray != null) { tray.Visible = false; tray.Dispose(); }
        mutex?.Dispose();
        base.OnExit(e);
    }
}

internal static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "ShortcutUI";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(Name, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(Name, false);
        }
    }
}

internal static class TrayIcon
{
    /// <summary>アプリのアイコン（Assets/app.ico）からトレイ用の小さいサイズを取り出す。</summary>
    public static Drawing.Icon Create()
    {
        var res = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        using var stream = res.Stream;
        return new Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize);
    }
}
