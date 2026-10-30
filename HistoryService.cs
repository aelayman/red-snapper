using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace RedSnapper;

public sealed class HistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Created { get; set; } = DateTime.Now;
    public string FileName { get; set; } = "";
    public string Text { get; set; } = "";
    public bool OcrDone { get; set; }
}

internal static class HistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly List<HistoryEntry> Entries = new();
    private static bool loaded;

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Red Snapper", "History");

    private static string IndexPath => Path.Combine(Folder, "index.json");

    public static event Action? Changed;

    public static string PathOf(HistoryEntry entry) => Path.Combine(Folder, entry.FileName);

    public static IReadOnlyList<HistoryEntry> Search(string query)
    {
        EnsureLoaded();
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Entries
            .Where(e => terms.All(t =>
                e.Text.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                e.Created.ToString("yyyy-MM-dd HH:mm").Contains(t, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(e => e.Created)
            .ToList();
    }

    // Saves the capture to the library right away, then reads its text in the background so it becomes searchable.
    public static async void Add(BitmapSource image)
    {
        try
        {
            EnsureLoaded();
            Directory.CreateDirectory(Folder);
            var entry = new HistoryEntry { FileName = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.png" };
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(PathOf(entry))) encoder.Save(stream);

            Entries.Add(entry);
            Save();
            Changed?.Invoke();

            try
            {
                entry.Text = await OcrService.RecognizeAsync(image) ?? "";
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                entry.Text = "";
            }
            entry.OcrDone = true;
            Save();
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // History is a convenience; never interrupt the capture itself.
        }
    }

    public static void Delete(HistoryEntry entry)
    {
        EnsureLoaded();
        Entries.Remove(entry);
        try { File.Delete(PathOf(entry)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Save();
        Changed?.Invoke();
    }

    // Reads text for captures that were interrupted before OCR finished (for example the app was closed).
    public static async Task CompletePendingOcrAsync()
    {
        EnsureLoaded();
        foreach (var entry in Entries.Where(e => !e.OcrDone).ToList())
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(PathOf(entry));
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                entry.Text = await OcrService.RecognizeAsync(image) ?? "";
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException
                                          or System.Runtime.InteropServices.COMException)
            {
                entry.Text = "";
            }
            entry.OcrDone = true;
        }
        Save();
        Changed?.Invoke();
    }

    private static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        try
        {
            if (File.Exists(IndexPath))
            {
                var items = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(IndexPath));
                if (items != null) Entries.AddRange(items.Where(i => File.Exists(PathOf(i))));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(IndexPath, JsonSerializer.Serialize(Entries, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
