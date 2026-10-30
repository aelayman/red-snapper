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
    public bool Edited { get; set; }
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

    private static string EditedPathOf(HistoryEntry entry) =>
        Path.Combine(Folder, Path.GetFileNameWithoutExtension(entry.FileName) + ".edited.png");

    private static string ProjectPathOf(HistoryEntry entry) =>
        Path.Combine(Folder, Path.GetFileNameWithoutExtension(entry.FileName) + ".json");

    // The annotated version when there is one, otherwise the original capture.
    public static string DisplayPathOf(HistoryEntry entry) =>
        entry.Edited && File.Exists(EditedPathOf(entry)) ? EditedPathOf(entry) : PathOf(entry);

    public static List<Annotation> LoadAnnotations(HistoryEntry entry)
    {
        try
        {
            if (!File.Exists(ProjectPathOf(entry))) return new List<Annotation>();
            var data = JsonSerializer.Deserialize<List<AnnotationData>>(File.ReadAllText(ProjectPathOf(entry)));
            return data == null ? new List<Annotation>() : AnnotationStore.FromData(data);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new List<Annotation>();
        }
    }

    // Keeps the original untouched and stores the annotations plus a flattened copy for thumbnails.
    // With no annotations the entry goes back to showing the original.
    public static bool SaveEdit(HistoryEntry entry, IReadOnlyCollection<Annotation> annotations, BitmapSource rendered)
    {
        try
        {
            if (annotations.Count == 0)
            {
                File.Delete(ProjectPathOf(entry));
                File.Delete(EditedPathOf(entry));
                entry.Edited = false;
            }
            else
            {
                File.WriteAllText(ProjectPathOf(entry), JsonSerializer.Serialize(AnnotationStore.ToData(annotations)));
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rendered));
                using (var stream = File.Create(EditedPathOf(entry))) encoder.Save(stream);
                entry.Edited = true;
            }
            Save();
            Changed?.Invoke();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

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
    public static HistoryEntry? Add(BitmapSource image)
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
            _ = ReadTextAsync(entry, image);
            return entry;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // History is a convenience; never interrupt the capture itself.
            return null;
        }
    }

    private static async Task ReadTextAsync(HistoryEntry entry, BitmapSource image)
    {
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

    public static void Delete(HistoryEntry entry)
    {
        EnsureLoaded();
        Entries.Remove(entry);
        foreach (var file in new[] { PathOf(entry), EditedPathOf(entry), ProjectPathOf(entry) })
        {
            try { File.Delete(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
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
