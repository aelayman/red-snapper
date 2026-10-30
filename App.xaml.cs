using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace RedSnapper;

public partial class App : Application
{
    private const int HotkeyRegion = 1;
    private const int HotkeyFullScreen = 2;
    private const int HotkeyPrintScreen = 3;
    private const int HotkeyHistory = 4;

    private System.Windows.Forms.ToolStripMenuItem? regionItem, fullItem, historyItem;

    private Mutex? singleInstance;
    private System.Windows.Forms.NotifyIcon? tray;
    private HwndSource? hotkeyWindow;
    private bool capturing;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            tray?.ShowBalloonTip(5000, "Red Snapper hit an error", args.Exception.Message, System.Windows.Forms.ToolTipIcon.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogError(args.ExceptionObject as Exception);

        singleInstance = new Mutex(true, "RedSnapper.SingleInstance", out bool created);
        if (!created)
        {
            Shutdown();
            return;
        }

        BuildTray();
        RegisterHotkeys();
        _ = HistoryService.CompletePendingOcrAsync();
        bool openedFile = false;
        foreach (var arg in e.Args)
        {
            if (File.Exists(arg)) { OpenImageFile(arg); openedFile = true; }
        }

        if (!openedFile)
        {
            tray!.ShowBalloonTip(3000, "Red Snapper is running", StartupHint(), System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (hotkeyWindow != null)
        {
            UnregisterHotkeys();
            hotkeyWindow.Dispose();
        }
        if (tray != null)
        {
            tray.Visible = false;
            tray.Dispose();
        }
        singleInstance?.Dispose();
        base.OnExit(e);
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static bool IsStartupEnabled()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("Red Snapper") != null;
    }

    private static void SetStartup(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            if (enable)
            {
                // Launch through the dotnet host: the apphost exe is blocked by some security policies.
                var dotnet = Environment.ProcessPath is { } host && host.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase)
                    ? host : Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files", "dotnet", "dotnet.exe");
                key.SetValue("Red Snapper", $"\"{dotnet}\" \"{Path.Combine(AppContext.BaseDirectory, "RedSnapper.dll")}\"");
            }
            else
            {
                key.DeleteValue("Red Snapper", false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            MessageBox.Show($"Could not change the startup setting:\n{ex.Message}", "Red Snapper");
        }
    }

    private static void LogError(Exception? ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Red Snapper");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"), $"{DateTime.Now:O}\n{ex}\n\n");
        }
        catch (IOException) { }
    }

    private void BuildTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        regionItem = new System.Windows.Forms.ToolStripMenuItem("Capture Region", null, (_, _) => CaptureRegion());
        fullItem = new System.Windows.Forms.ToolStripMenuItem("Capture Full Screen", null, (_, _) => CaptureFullScreen());
        historyItem = new System.Windows.Forms.ToolStripMenuItem("History…", null, (_, _) => HistoryWindow.ShowWindow());
        menu.Items.Add(regionItem);
        menu.Items.Add(fullItem);
        menu.Items.Add(historyItem);
        UpdateTrayText();
        menu.Items.Add("Open Image…", null, (_, _) => PickAndOpenImage());
        menu.Items.Add("Settings…", null, (_, _) => SettingsWindow.ShowWindow());
        var startup = new System.Windows.Forms.ToolStripMenuItem("Start with Windows") { Checked = IsStartupEnabled() };
        startup.Click += (_, _) =>
        {
            SetStartup(!startup.Checked);
            startup.Checked = IsStartupEnabled();
        };
        menu.Items.Add(startup);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());

        tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Red Snapper",
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.DoubleClick += (_, _) => CaptureRegion();
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var info = GetResourceStream(new Uri("app.ico", UriKind.Relative));
        if (info == null) return System.Drawing.SystemIcons.Application;
        using var stream = info.Stream;
        return new System.Drawing.Icon(stream);
    }

    private static string WithKey(string label, string hotkey) =>
        string.IsNullOrWhiteSpace(hotkey) ? label : $"{label} ({HotkeyText.Display(hotkey)})";

    private void UpdateTrayText()
    {
        var s = AppSettings.Current;
        if (regionItem != null) regionItem.Text = WithKey("Capture Region", !string.IsNullOrWhiteSpace(s.RegionHotkey) ? s.RegionHotkey : s.AltRegionHotkey);
        if (fullItem != null) fullItem.Text = WithKey("Capture Full Screen", s.FullScreenHotkey);
        if (historyItem != null) historyItem.Text = WithKey("History…", s.HistoryHotkey);
    }

    private static string StartupHint()
    {
        var s = AppSettings.Current;
        var region = !string.IsNullOrWhiteSpace(s.RegionHotkey) ? s.RegionHotkey : s.AltRegionHotkey;
        return string.IsNullOrWhiteSpace(region)
            ? "Right-click the tray icon to capture."
            : $"Press {HotkeyText.Display(region)} to capture a region.";
    }

    private void UnregisterHotkeys()
    {
        if (hotkeyWindow == null) return;
        foreach (int id in new[] { HotkeyRegion, HotkeyFullScreen, HotkeyPrintScreen, HotkeyHistory })
            NativeMethods.UnregisterHotKey(hotkeyWindow.Handle, id);
    }

    internal void ApplySettings()
    {
        RegisterHotkeys();
        UpdateTrayText();
    }

    private void RegisterHotkeys()
    {
        if (hotkeyWindow == null)
        {
            var parameters = new HwndSourceParameters("RedSnapperHotkeys") { ParentWindow = NativeMethods.HWND_MESSAGE };
            hotkeyWindow = new HwndSource(parameters);
            hotkeyWindow.AddHook(WndProc);
        }
        UnregisterHotkeys();

        var s = AppSettings.Current;
        var failed = new System.Collections.Generic.List<string>();
        void Register(int id, string hotkey)
        {
            if (!HotkeyText.TryParse(hotkey, out uint mods, out uint vk)) return;
            if (!NativeMethods.RegisterHotKey(hotkeyWindow.Handle, id, mods | NativeMethods.MOD_NOREPEAT, vk))
                failed.Add(HotkeyText.Display(hotkey));
        }
        Register(HotkeyPrintScreen, s.RegionHotkey);
        Register(HotkeyRegion, s.AltRegionHotkey);
        Register(HotkeyFullScreen, s.FullScreenHotkey);
        Register(HotkeyHistory, s.HistoryHotkey);

        if (failed.Count > 0)
        {
            tray?.ShowBalloonTip(6000, "Hotkey unavailable",
                $"Another app is using {string.Join(", ", failed)}. Change it in Settings or use the tray menu.",
                System.Windows.Forms.ToolTipIcon.Warning);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HotkeyRegion || id == HotkeyPrintScreen) CaptureRegion();
            else if (id == HotkeyFullScreen) CaptureFullScreen();
            else if (id == HotkeyHistory) HistoryWindow.ShowWindow();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private async void CaptureRegion()
    {
        if (capturing) return;
        capturing = true;
        try
        {
            // Let the tray menu finish closing before taking the screenshot.
            await Task.Delay(200);
            var windowRects = ScreenCapture.VisibleWindows();
            var (x, y, w, h) = ScreenCapture.VirtualScreen();
            var shot = ScreenCapture.Capture(x, y, w, h);

            var selector = new RegionSelectorWindow(shot, x, y, w, h, windowRects);
            selector.ShowDialog();
            if (selector.Result is Int32Rect region)
            {
                var crop = new CroppedBitmap(shot, region);
                crop.Freeze();
                var entry = HistoryService.Add(crop);
                OpenEditor(crop, null, entry);
            }
        }
        finally
        {
            capturing = false;
        }
    }

    private async void CaptureFullScreen()
    {
        if (capturing) return;
        capturing = true;
        try
        {
            await Task.Delay(200);
            var (x, y, w, h) = ScreenCapture.VirtualScreen();
            var full = ScreenCapture.Capture(x, y, w, h);
            var entry = HistoryService.Add(full);
            OpenEditor(full, null, entry);
        }
        finally
        {
            capturing = false;
        }
    }

    private static void PickAndOpenImage()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*",
        };
        if (dlg.ShowDialog() == true) OpenImageFile(dlg.FileName);
    }

    public static void OpenImageFile(string file)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(Path.GetFullPath(file));
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            OpenEditor(image, file);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or FileFormatException)
        {
            MessageBox.Show($"Could not open {file}:\n{ex.Message}", "Red Snapper");
        }
    }

    private static readonly System.Collections.Generic.Dictionary<Guid, EditorWindow> EntryEditors = new();

    public static void OpenHistoryEntry(HistoryEntry entry)
    {
        if (EntryEditors.TryGetValue(entry.Id, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(HistoryService.PathOf(entry));
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            OpenEditor(image, null, entry);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or FileFormatException)
        {
            MessageBox.Show($"Could not open this capture:\n{ex.Message}", "Red Snapper");
        }
    }

    private static void OpenEditor(BitmapSource image, string? file, HistoryEntry? entry = null)
    {
        var editor = new EditorWindow(image, file, entry);
        if (entry != null)
        {
            EntryEditors[entry.Id] = editor;
            editor.Closed += (_, _) => EntryEditors.Remove(entry.Id);
        }
        editor.Show();
        editor.Activate();
    }
}
