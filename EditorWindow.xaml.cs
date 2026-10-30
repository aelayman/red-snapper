using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace RedSnapper;

public partial class EditorWindow : Window
{
    private static readonly (string Name, Color Color)[] Palette =
    {
        ("Red", Colors.Red), ("Orange", Colors.DarkOrange), ("Yellow", Colors.Gold),
        ("Green", Colors.LimeGreen), ("Blue", Colors.DodgerBlue), ("Purple", Colors.MediumPurple),
        ("Black", Colors.Black), ("White", Colors.White),
    };

    private readonly Stack<List<Annotation>> undo = new();
    private readonly Stack<List<Annotation>> redo = new();
    private string? path;
    private double zoom = 1;

    public EditorWindow(BitmapSource image, string? path = null)
    {
        InitializeComponent();
        this.path = path;

        View.Image = image;
        View.Changing += PushUndo;
        View.Changed += UpdateStatus;
        View.PromptText = Prompt;

        foreach (var (name, color) in Palette)
        {
            ColorBox.Items.Add(new ComboBoxItem
            {
                Tag = color,
                ToolTip = name,
                Content = new System.Windows.Shapes.Rectangle
                {
                    Width = 44, Height = 12, Fill = new SolidColorBrush(color),
                    Stroke = Brushes.Gray, StrokeThickness = 1,
                },
            });
        }
        ColorBox.SelectedIndex = 0;

        Title = path == null ? "Red Snapper Editor" : $"Red Snapper Editor - {System.IO.Path.GetFileName(path)}";
        Loaded += (_, _) => FitZoom();
        PreviewKeyDown += Window_PreviewKeyDown;
        UpdateStatus();
    }

    private void FitZoom()
    {
        var img = View.Image!;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double fit = Math.Min(
            Math.Max(100, Scroller.ActualWidth - 40) / img.PixelWidth,
            Math.Max(100, Scroller.ActualHeight - 40) / img.PixelHeight);
        SetZoom(Math.Min(1 / dpi, fit));
    }

    private void SetZoom(double value)
    {
        zoom = Math.Clamp(value, 0.1, 8);
        ZoomHost.LayoutTransform = new ScaleTransform(zoom, zoom);
        UpdateStatus();
    }

    private void Scroller_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        SetZoom(zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1));
        e.Handled = true;
    }

    private void UpdateStatus()
    {
        if (StatusText == null) return;
        var img = View.Image!;
        StatusText.Text = $"{img.PixelWidth} × {img.PixelHeight}px  |  {View.Annotations.Count} annotations  |  zoom {zoom:P0}  |  Ctrl+wheel zooms";
    }

    private List<Annotation> Snapshot() => View.Annotations.Select(a => a.Clone()).ToList();

    private void PushUndo()
    {
        undo.Push(Snapshot());
        redo.Clear();
    }

    private void Undo()
    {
        if (undo.Count == 0) return;
        redo.Push(Snapshot());
        View.ReplaceAnnotations(undo.Pop());
    }

    private void Redo()
    {
        if (redo.Count == 0) return;
        undo.Push(Snapshot());
        View.ReplaceAnnotations(redo.Pop());
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (e.Key is Key.Delete or Key.Back) { View.DeleteSelected(); e.Handled = true; }
        else if (ctrl && e.Key == Key.Z) { Undo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.Y) { Redo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.S) { Save(); e.Handled = true; }
        else if (ctrl && e.Key == Key.C) { CopyToClipboard(); e.Handled = true; }
        else if (e.Key == Key.Escape && View.Selected != null) { View.Selected = null; View.InvalidateVisual(); e.Handled = true; }
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        View.Tool = Enum.Parse<Tool>((string)((FrameworkElement)sender).Tag);
        View.Selected = null;
        View.InvalidateVisual();
    }

    private void Color_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (View == null || ColorBox.SelectedItem is not ComboBoxItem item) return;
        var color = (Color)item.Tag;
        View.StrokeColor = color;
        View.RecolorSelected(color);
    }

    private void Width_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (View == null) return;
        View.Thickness = e.NewValue;
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();
    private void Delete_Click(object sender, RoutedEventArgs e) => View.DeleteSelected();
    private void Copy_Click(object sender, RoutedEventArgs e) => CopyToClipboard();
    private void Save_Click(object sender, RoutedEventArgs e) => Save();
    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveAs();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*" };
        if (dlg.ShowDialog(this) == true) App.OpenImageFile(dlg.FileName);
    }

    private void CopyToClipboard()
    {
        try
        {
            Clipboard.SetImage(View.RenderToBitmap());
            StatusText.Text = "Copied to clipboard";
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            MessageBox.Show(this, "The clipboard is busy. Try again.", "Red Snapper");
        }
    }

    private void Save()
    {
        if (path == null)
        {
            var dir = LibraryFolder();
            Directory.CreateDirectory(dir);
            path = System.IO.Path.Combine(dir, $"Red Snapper {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");
        }
        WriteImage(path);
    }

    private void SaveAs()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "PNG|*.png|JPEG|*.jpg|Bitmap|*.bmp",
            InitialDirectory = Directory.CreateDirectory(LibraryFolder()).FullName,
            FileName = $"Red Snapper {DateTime.Now:yyyy-MM-dd HH.mm.ss}",
        };
        if (dlg.ShowDialog(this) != true) return;
        path = dlg.FileName;
        WriteImage(path);
    }

    private void WriteImage(string target)
    {
        try
        {
            BitmapEncoder encoder = System.IO.Path.GetExtension(target).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 92 },
                ".bmp" => new BmpBitmapEncoder(),
                _ => new PngBitmapEncoder(),
            };
            encoder.Frames.Add(BitmapFrame.Create(View.RenderToBitmap()));
            using var stream = File.Create(target);
            encoder.Save(stream);
            Title = $"Red Snapper Editor - {System.IO.Path.GetFileName(target)}";
            StatusText.Text = $"Saved to {target}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Could not save");
        }
    }

    private static string LibraryFolder() =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Red Snapper");

    private string? Prompt(string title)
    {
        var box = new TextBox { MinWidth = 300, Margin = new Thickness(12, 12, 12, 6) };
        var ok = new Button { Content = "OK", IsDefault = true, Width = 70, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 70 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 6, 12, 12) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel();
        panel.Children.Add(box);
        panel.Children.Add(buttons);

        var dialog = new Window
        {
            Title = title, Owner = this, Content = panel, SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
        };
        ok.Click += (_, _) => dialog.DialogResult = true;
        dialog.Loaded += (_, _) => box.Focus();
        return dialog.ShowDialog() == true && box.Text.Length > 0 ? box.Text : null;
    }
}
