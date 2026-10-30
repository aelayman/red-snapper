using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;

namespace RedSnapper;

// Hotkeys are stored as text such as "Ctrl+Shift+S" or "Snapshot" (the Print Screen key); empty means disabled.
internal sealed class AppSettings
{
    public string RegionHotkey { get; set; } = "Snapshot";
    public string AltRegionHotkey { get; set; } = "Ctrl+Shift+S";
    public string FullScreenHotkey { get; set; } = "Ctrl+Shift+F";
    public string HistoryHotkey { get; set; } = "Ctrl+Shift+H";
    public string SaveFolder { get; set; } = "";

    public static AppSettings Current { get; private set; } = Load();

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Red Snapper", "settings.json");

    public static string DefaultSaveFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Red Snapper");

    public string EffectiveSaveFolder => string.IsNullOrWhiteSpace(SaveFolder) ? DefaultSaveFolder : SaveFolder;

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new AppSettings();
    }

    public static void Update(AppSettings settings)
    {
        Current = settings;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

internal static class HotkeyText
{
    private const uint Alt = 0x1, Ctrl = 0x2, Shift = 0x4, Win = 0x8;

    public static bool TryParse(string? text, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+');
        foreach (var part in parts.Take(parts.Length - 1))
        {
            switch (part)
            {
                case "Ctrl": modifiers |= Ctrl; break;
                case "Shift": modifiers |= Shift; break;
                case "Alt": modifiers |= Alt; break;
                case "Win": modifiers |= Win; break;
                default: return false;
            }
        }
        if (!Enum.TryParse<Key>(parts[^1], out var key)) return false;
        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }

    public static string FromKey(ModifierKeys mods, Key key)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    public static string Display(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "" : text.Replace("Snapshot", "Print Screen").Replace("Return", "Enter");

    // Bare keys are only allowed for the function and Print Screen keys, so normal typing isn't hijacked.
    public static bool IsAllowed(ModifierKeys mods, Key key) =>
        mods != ModifierKeys.None || key == Key.Snapshot || (key >= Key.F1 && key <= Key.F12);

    public static bool IsModifier(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.System or Key.None or Key.ImeProcessed or Key.DeadCharProcessed;
}
