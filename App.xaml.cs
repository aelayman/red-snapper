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
    private const uint VkS = 0x53;
    private const uint VkF = 0x46;

    private Mutex? singleInstance;
    private System.Windows.Forms.NotifyIcon? tray;
    private HwndSource? hotkeyWindow;
    private bool capturing;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        singleInstance = new Mutex(true, "RedSnapper.SingleInstance", out bool created);
        if (!created)
        {
            Shutdown();
            return;
        }

        BuildTray();
        RegisterHotkeys();

        bool openedFile = false;
        foreach (var arg in e.Args)
        {
            if (File.Exists(arg)) { OpenImageFile(arg); openedFile = true; }
        }

        if (!openedFile)
        {
            tray!.ShowBalloonTip(3000, "Red Snapper is running",
                "Ctrl+Shift+S captures a region, Ctrl+Shift+F the full screen.", System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (hotkeyWindow != null)
        {
            NativeMethods.UnregisterHotKey(hotkeyWindow.Handle, HotkeyRegion);
            NativeMethods.UnregisterHotKey(hotkeyWindow.Handle, HotkeyFullScreen);
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

    private void BuildTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Capture Region (Ctrl+Shift+S)", null, (_, _) => CaptureRegion());
        menu.Items.Add("Capture Full Screen (Ctrl+Shift+F)", null, (_, _) => CaptureFullScreen());
        menu.Items.Add("Open Image…", null, (_, _) => PickAndOpenImage());
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

    private void RegisterHotkeys()
    {
        var parameters = new HwndSourceParameters("RedSnapperHotkeys") { ParentWindow = NativeMethods.HWND_MESSAGE };
        hotkeyWindow = new HwndSource(parameters);
        hotkeyWindow.AddHook(WndProc);

        uint mods = NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT;
        bool ok = NativeMethods.RegisterHotKey(hotkeyWindow.Handle, HotkeyRegion, mods, VkS);
        ok &= NativeMethods.RegisterHotKey(hotkeyWindow.Handle, HotkeyFullScreen, mods, VkF);
        if (!ok)
        {
            tray?.ShowBalloonTip(5000, "Hotkey unavailable",
                "Another app is using Ctrl+Shift+S or Ctrl+Shift+F. Use the tray menu instead.",
                System.Windows.Forms.ToolTipIcon.Warning);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HotkeyRegion) CaptureRegion();
            else if (id == HotkeyFullScreen) CaptureFullScreen();
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
            var (x, y, w, h) = ScreenCapture.VirtualScreen();
            var shot = ScreenCapture.Capture(x, y, w, h);

            var selector = new RegionSelectorWindow(shot, x, y, w, h);
            selector.ShowDialog();
            if (selector.Result is Int32Rect region)
            {
                var crop = new CroppedBitmap(shot, region);
                crop.Freeze();
                OpenEditor(crop, null);
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
            OpenEditor(ScreenCapture.Capture(x, y, w, h), null);
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

    private static void OpenEditor(BitmapSource image, string? file)
    {
        var editor = new EditorWindow(image, file);
        editor.Show();
        editor.Activate();
    }
}
