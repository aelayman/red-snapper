using System;

namespace RedSnapper;

internal static class Program
{
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    [STAThread]
    private static void Main(string[] args)
    {
        // The app is launched through dotnet.exe, whose manifest wins over ours, so ask for
        // per-monitor DPI here. Without it Windows stretches the screen on mixed-DPI monitors
        // and captures come out soft.
        NativeMethods.SetProcessDpiAwarenessContext(PerMonitorAwareV2);

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
