using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace RedSnapper;

// Full-screen overlay showing a frozen screenshot; the user drags out a region.
// The canvas is sized in physical pixels and the window content is scaled by
// 1/DPI so that mouse coordinates map directly onto screenshot pixels.
public sealed class RegionSelectorWindow : Window
{
    private readonly int vx, vy, vw, vh;
    private readonly Grid host = new();
    private readonly Canvas canvas = new();
    private readonly System.Windows.Shapes.Path dim = new() { Fill = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), IsHitTestVisible = false };
    private readonly Rectangle border = new() { Stroke = Brushes.DodgerBlue, StrokeThickness = 1.5, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly Border labelBox = new() { Background = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)), Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(3), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly TextBlock label = new() { Foreground = Brushes.White, FontSize = 12 };

    private Point? start;

    public Int32Rect? Result { get; private set; }

    public RegionSelectorWindow(BitmapSource shot, int x, int y, int w, int h)
    {
        vx = x; vy = y; vw = w; vh = h;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;

        canvas.Width = w;
        canvas.Height = h;
        canvas.Children.Add(new System.Windows.Controls.Image { Source = shot, Width = w, Height = h, Stretch = Stretch.Fill });
        canvas.Children.Add(dim);
        canvas.Children.Add(border);
        labelBox.Child = label;
        canvas.Children.Add(labelBox);
        UpdateDim(Rect.Empty);

        host.HorizontalAlignment = HorizontalAlignment.Left;
        host.VerticalAlignment = VerticalAlignment.Top;
        host.Children.Add(canvas);
        Content = host;

        Loaded += (_, _) => ApplyScale();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        PlaceWindow();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ApplyScale();
        Dispatcher.BeginInvoke(new Action(PlaceWindow));
    }

    private void PlaceWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, vx, vy, vw, vh, NativeMethods.SWP_SHOWWINDOW);
    }

    private void ApplyScale()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        host.LayoutTransform = new ScaleTransform(1 / dpi.DpiScaleX, 1 / dpi.DpiScaleY);
    }

    private void UpdateDim(Rect selection)
    {
        Geometry geo = new RectangleGeometry(new Rect(0, 0, vw, vh));
        if (!selection.IsEmpty)
            geo = new CombinedGeometry(GeometryCombineMode.Exclude, geo, new RectangleGeometry(selection));
        dim.Data = geo;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        Activate();
        Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Close();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        Close();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        start = e.GetPosition(canvas);
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (start is not Point s) return;

        var rect = Clamp(new Rect(s, e.GetPosition(canvas)));
        UpdateDim(rect);

        border.Visibility = Visibility.Visible;
        Canvas.SetLeft(border, rect.X);
        Canvas.SetTop(border, rect.Y);
        border.Width = rect.Width;
        border.Height = rect.Height;

        label.Text = $"{(int)rect.Width} × {(int)rect.Height}";
        labelBox.Visibility = Visibility.Visible;
        Canvas.SetLeft(labelBox, rect.X);
        Canvas.SetTop(labelBox, Math.Max(0, rect.Y - 26));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (start is not Point s) return;
        ReleaseMouseCapture();

        var rect = Clamp(new Rect(s, e.GetPosition(canvas)));
        start = null;
        if (rect.Width >= 3 && rect.Height >= 3)
        {
            Result = new Int32Rect((int)Math.Round(rect.X), (int)Math.Round(rect.Y),
                (int)Math.Round(rect.Width), (int)Math.Round(rect.Height));
            Close();
        }
    }

    private Rect Clamp(Rect r)
    {
        var clipped = Rect.Intersect(r, new Rect(0, 0, vw, vh));
        return clipped.IsEmpty ? new Rect(0, 0, 0, 0) : clipped;
    }
}
