# Red Snapper

A Windows (WPF / .NET 8) screen capture and annotation tool, based on [Snappilot](https://github.com/shipiit/snappilot), the open-source
macOS screen capture and annotation tool (MIT licensed). This is a ground-up rewrite, since the original
relies on Apple-only frameworks (SwiftUI, ScreenCaptureKit, Vision).

## Features (MVP)

- Runs in the system tray. Global hotkeys:
  - **Ctrl+Shift+S**: capture a region
  - **Ctrl+Shift+F**: capture all screens
- Region selector over a frozen screenshot (Esc / right-click cancels)
- Annotation editor: select/move, arrow, line, rectangle, ellipse, highlight, text, numbered steps,
  pixelate, solid redact. Color and width controls. Undo/redo (Ctrl+Z / Ctrl+Y), Delete.
- Copy to clipboard (Ctrl+C), Save (Ctrl+S, to `Pictures\Red Snapper`), Save As (PNG/JPEG/BMP)
- Open existing images (File picker or command-line argument)
- Ctrl+mouse wheel to zoom

## Not yet ported

Screen recording, OCR/text search, searchable library, scroll capture, tasks/meeting notes, GIF export.

## Build and run

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet run
```

## Known limitations

- Region capture on multi-monitor setups with different DPI scaling is untested.
- Pixelate is applied to the base screenshot, not to other annotations beneath it.

## License

MIT, matching the original project. See the upstream repository for its copyright notice.
