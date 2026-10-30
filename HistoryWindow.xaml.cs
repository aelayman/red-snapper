using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace RedSnapper;

public partial class HistoryWindow : Window
{
    private sealed class Item
    {
        public Item(HistoryEntry entry)
        {
            Entry = entry;
            When = entry.Created.ToString("ddd MMM d, h:mm tt");
            var text = string.Join(" ", entry.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            Snippet = !entry.OcrDone ? "Reading text…" : text.Length == 0 ? "No text found" : text;

            var thumb = new BitmapImage();
            thumb.BeginInit();
            thumb.UriSource = new Uri(HistoryService.DisplayPathOf(entry));
            thumb.DecodePixelWidth = 440;
            thumb.CacheOption = BitmapCacheOption.OnLoad;
            thumb.EndInit();
            thumb.Freeze();
            Thumbnail = thumb;
        }

        public HistoryEntry Entry { get; }
        public string When { get; }
        public string Snippet { get; }
        public BitmapSource Thumbnail { get; }
    }

    private static HistoryWindow? current;

    public static void ShowWindow()
    {
        if (current == null)
        {
            current = new HistoryWindow();
            current.Closed += (_, _) => current = null;
            current.Show();
        }
        if (current.WindowState == WindowState.Minimized) current.WindowState = WindowState.Normal;
        current.Activate();
        current.SearchBox.Focus();
    }

    private HistoryWindow()
    {
        InitializeComponent();
        HistoryService.Changed += Refresh;
        Closed += (_, _) => HistoryService.Changed -= Refresh;
        Refresh();
        UpdateButtons();
    }

    private HistoryEntry? Selected => (List.SelectedItem as Item)?.Entry;

    private void Refresh()
    {
        var selectedId = Selected?.Id;
        var query = SearchBox.Text;
        var items = new List<Item>();
        foreach (var entry in HistoryService.Search(query))
        {
            try { items.Add(new Item(entry)); }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException) { }
        }

        List.ItemsSource = items;
        List.SelectedItem = items.FirstOrDefault(i => i.Entry.Id == selectedId);
        CountText.Text = items.Count == 1 ? "1 capture" : $"{items.Count} captures";
        EmptyText.Text = items.Count > 0 ? ""
            : query.Length > 0 ? "No captures match your search."
            : "No captures yet. Press Print Screen to take one.";
    }

    private void UpdateButtons()
    {
        bool has = Selected != null;
        OpenButton.IsEnabled = has;
        CopyTextButton.IsEnabled = has;
        DeleteButton.IsEnabled = has;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(List, d) != null) OpenSelected();
    }

    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OpenSelected();
        else if (e.Key == Key.Delete) DeleteSelected();
    }

    private void Open_Click(object sender, RoutedEventArgs e) => OpenSelected();

    private void OpenSelected()
    {
        if (Selected is { } entry) App.OpenHistoryEntry(entry);
    }

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry) return;
        if (string.IsNullOrWhiteSpace(entry.Text))
        {
            MessageBox.Show(this, "No text was found in this capture.", "Red Snapper");
            return;
        }
        System.Windows.Clipboard.SetText(entry.Text);
    }

    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    private void DeleteSelected()
    {
        if (Selected is not { } entry) return;
        if (MessageBox.Show(this, "Delete this capture from history?", "Red Snapper",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
            HistoryService.Delete(entry);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(HistoryService.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{HistoryService.Folder}\"") { UseShellExecute = true });
    }
}
