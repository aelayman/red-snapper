using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RedSnapper;

public partial class SettingsWindow : Window
{
    private const string KeyboardKey = @"Control Panel\Keyboard";
    private const string SnippingValue = "PrintScreenKeyForSnippingEnabled";

    private static SettingsWindow? open;

    public static void ShowWindow()
    {
        if (open == null)
        {
            open = new SettingsWindow();
            open.Closed += (_, _) => open = null;
            open.Show();
        }
        open.Activate();
    }

    private SettingsWindow()
    {
        InitializeComponent();
        var s = AppSettings.Current;
        SetHotkey(RegionBox, s.RegionHotkey);
        SetHotkey(AltRegionBox, s.AltRegionHotkey);
        SetHotkey(FullBox, s.FullScreenHotkey);
        SetHotkey(HistoryBox, s.HistoryHotkey);
        FolderBox.Text = s.SaveFolder;
        SnippingBox.IsChecked = IsPrintScreenFree();
    }

    private TextBox[] HotkeyBoxes => new[] { RegionBox, AltRegionBox, FullBox, HistoryBox };

    private static void SetHotkey(TextBox box, string value)
    {
        box.Tag = value;
        box.Text = HotkeyText.Display(value);
    }

    private static bool IsPrintScreenFree()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(KeyboardKey);
        return key?.GetValue(SnippingValue) is int v && v == 0;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        var mods = Keyboard.Modifiers;

        if (key == Key.Tab && mods == ModifierKeys.None) return;
        e.Handled = true;

        if (HotkeyText.IsModifier(key)) return;
        if (key == Key.Back && mods == ModifierKeys.None)
        {
            SetHotkey(box, "");
            ErrorText.Visibility = Visibility.Collapsed;
            return;
        }
        if (!HotkeyText.IsAllowed(mods, key))
        {
            ShowError("Include Ctrl, Alt, Shift or Win with that key (Print Screen and F1–F12 can be used alone).");
            return;
        }

        ErrorText.Visibility = Visibility.Collapsed;
        SetHotkey(box, HotkeyText.FromKey(mods, key));
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose the default folder for saved screenshots",
            SelectedPath = string.IsNullOrWhiteSpace(FolderBox.Text) ? AppSettings.DefaultSaveFolder : FolderBox.Text,
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) FolderBox.Text = dialog.SelectedPath;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var d = new AppSettings();
        SetHotkey(RegionBox, d.RegionHotkey);
        SetHotkey(AltRegionBox, d.AltRegionHotkey);
        SetHotkey(FullBox, d.FullScreenHotkey);
        SetHotkey(HistoryBox, d.HistoryHotkey);
        FolderBox.Text = "";
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var chosen = HotkeyBoxes.Select(b => (string)b.Tag).Where(t => t.Length > 0).ToList();
        if (chosen.Count != chosen.Distinct().Count())
        {
            ShowError("Each hotkey can only be used once.");
            return;
        }

        var folder = FolderBox.Text.Trim();
        if (folder.Length > 0)
        {
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                ShowError($"Can't use that folder: {ex.Message}");
                return;
            }
        }

        SetPrintScreenFree(SnippingBox.IsChecked == true);

        AppSettings.Update(new AppSettings
        {
            RegionHotkey = (string)RegionBox.Tag,
            AltRegionHotkey = (string)AltRegionBox.Tag,
            FullScreenHotkey = (string)FullBox.Tag,
            HistoryHotkey = (string)HistoryBox.Tag,
            SaveFolder = folder,
        });
        ((App)Application.Current).ApplySettings();
        Close();
    }

    private static void SetPrintScreenFree(bool free)
    {
        if (free == IsPrintScreenFree()) return;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(KeyboardKey);
            key.SetValue(SnippingValue, free ? 0 : 1, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            MessageBox.Show($"Could not change the Print Screen setting in Windows:\n{ex.Message}", "Red Snapper");
        }
    }
}
