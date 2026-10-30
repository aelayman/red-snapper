using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RedSnapper;

public sealed class CanvasView : FrameworkElement
{
    private const double HandleSize = 9;

    public BitmapSource? Image { get; set; }
    public List<Annotation> Annotations { get; private set; } = new();
    public Annotation? Selected { get; set; }
    public Tool Tool { get; set; } = Tool.Select;
    public Color StrokeColor { get; set; } = Colors.Red;
    public double Thickness { get; set; } = 4;
    public ArrowHead Head { get; set; } = ArrowHead.Arrow;
    public bool Dashed { get; set; }
    public bool Filled { get; set; }

    public Func<string, string?>? PromptText { get; set; }
    public event Action? Changing;
    public event Action? Changed;

    private Annotation? draft;
    private Point dragStart;
    private Point lastPoint;
    private bool moving;
    private bool movedYet;
    private int resizeHandle = -1;
    private Rect? cropDraft;

    // Visible region in original-image coordinates; annotations always use those coordinates.
    public Rect CropArea
    {
        get
        {
            var full = new Rect(0, 0, Image!.PixelWidth, Image.PixelHeight);
            var crop = Annotations.OfType<CropAnnotation>().LastOrDefault();
            if (crop == null) return full;
            var r = Rect.Intersect(crop.Area, full);
            return r.IsEmpty || r.Width < 1 || r.Height < 1 ? full : r;
        }
    }

    public bool IsCropped => Annotations.OfType<CropAnnotation>().Any();

    public void ReplaceAnnotations(List<Annotation> list)
    {
        Annotations = list;
        Selected = null;
        Refresh();
        Changed?.Invoke();
    }

    public void ResetCrop()
    {
        if (!IsCropped) return;
        Changing?.Invoke();
        Annotations.RemoveAll(a => a is CropAnnotation);
        Refresh();
        Changed?.Invoke();
    }

    public void DeleteSelected()
    {
        if (Selected == null) return;
        Changing?.Invoke();
        Annotations.Remove(Selected);
        Selected = null;
        Refresh();
        Changed?.Invoke();
    }

    public void RecolorSelected(Color color)
    {
        if (Selected == null) return;
        Changing?.Invoke();
        Selected.Color = color;
        InvalidateVisual();
        Changed?.Invoke();
    }

    public void ApplyStyleToSelected()
    {
        if (Selected is not ShapeAnnotation s) return;
        Changing?.Invoke();
        s.Head = Head;
        s.Dashed = Dashed;
        s.Filled = Filled;
        InvalidateVisual();
        Changed?.Invoke();
    }

    public BitmapSource RenderToBitmap()
    {
        var area = CropArea;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            DrawContent(dc, includeSelection: false);

        var rtb = new RenderTargetBitmap((int)Math.Round(area.Width), (int)Math.Round(area.Height), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private void Refresh()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Image == null) return new Size(0, 0);
        var area = CropArea;
        return new Size(Math.Round(area.Width), Math.Round(area.Height));
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        DrawContent(dc, includeSelection: true);
    }

    private void DrawContent(DrawingContext dc, bool includeSelection)
    {
        if (Image == null) return;

        var area = CropArea;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, Math.Round(area.Width), Math.Round(area.Height))));
        dc.PushTransform(new TranslateTransform(-area.X, -area.Y));

        dc.DrawImage(Image, new Rect(0, 0, Image.PixelWidth, Image.PixelHeight));
        foreach (var a in Annotations) a.Draw(dc, Image);
        draft?.Draw(dc, Image);

        if (includeSelection && Selected != null)
        {
            var b = Selected.Bounds;
            b.Inflate(4, 4);
            var pen = new Pen(Brushes.DodgerBlue, 1.5) { DashStyle = DashStyles.Dash };
            dc.DrawRectangle(null, pen, b);

            foreach (var h in HandlePoints(Selected))
                dc.DrawRectangle(Brushes.White, new Pen(Brushes.DodgerBlue, 1.5), new Rect(h.X - HandleSize / 2, h.Y - HandleSize / 2, HandleSize, HandleSize));
        }

        if (includeSelection && cropDraft is Rect c)
        {
            var dim = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0));
            var full = new RectangleGeometry(new Rect(0, 0, Image.PixelWidth, Image.PixelHeight));
            dc.DrawGeometry(dim, null, new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(c)));
            dc.DrawRectangle(null, new Pen(Brushes.White, 1.5) { DashStyle = DashStyles.Dash }, c);
        }

        dc.Pop();
        dc.Pop();
    }

    private Point Pos(MouseEventArgs e)
    {
        var area = CropArea;
        return e.GetPosition(this) + new Vector(area.X, area.Y);
    }

    // Line-like shapes expose their two ends; box-like ones expose four corners (TL, TR, BL, BR).
    private static IReadOnlyList<Point> HandlePoints(Annotation a)
    {
        switch (a)
        {
            case ShapeAnnotation { Kind: ShapeKind.Line or ShapeKind.Arrow } s:
                return new[] { s.P1, s.P2 };
            case ShapeAnnotation s:
                return Corners(new Rect(s.P1, s.P2));
            case RedactAnnotation r:
                return Corners(new Rect(r.P1, r.P2));
            default:
                return Array.Empty<Point>();
        }
    }

    private static Point[] Corners(Rect r) =>
        new[] { r.TopLeft, r.TopRight, r.BottomLeft, r.BottomRight };

    private int HitHandle(Point p)
    {
        if (Selected == null) return -1;
        var pts = HandlePoints(Selected);
        for (int i = 0; i < pts.Count; i++)
            if ((p - pts[i]).Length <= HandleSize) return i;
        return -1;
    }

    private static void GetEnds(Annotation a, out Point p1, out Point p2)
    {
        switch (a)
        {
            case ShapeAnnotation s: (p1, p2) = (s.P1, s.P2); break;
            case RedactAnnotation r: (p1, p2) = (r.P1, r.P2); break;
            default: p1 = p2 = default; break;
        }
    }

    private static void SetEnds(Annotation a, Point p1, Point p2)
    {
        switch (a)
        {
            case ShapeAnnotation s: (s.P1, s.P2) = (p1, p2); break;
            case RedactAnnotation r: (r.P1, r.P2) = (p1, p2); break;
        }
    }

    private static bool IsLineLike(Annotation a) =>
        a is ShapeAnnotation { Kind: ShapeKind.Line or ShapeKind.Arrow };

    private void ResizeSelected(Point p)
    {
        if (Selected == null) return;
        GetEnds(Selected, out var p1, out var p2);

        if (IsLineLike(Selected))
        {
            if (resizeHandle == 0) p1 = p; else p2 = p;
        }
        else
        {
            var r = new Rect(p1, p2);
            (p1, p2) = (r.TopLeft, r.BottomRight);
            switch (resizeHandle)
            {
                case 0: p1 = p; break;
                case 1: p1 = new Point(p1.X, p.Y); p2 = new Point(p.X, p2.Y); break;
                case 2: p1 = new Point(p.X, p1.Y); p2 = new Point(p2.X, p.Y); break;
                default: p2 = p; break;
            }
            // Keep the dragged corner index stable after the box flips over itself.
            var nr = new Rect(p1, p2);
            (p1, p2) = (nr.TopLeft, nr.BottomRight);
            var moved = new[] { nr.TopLeft, nr.TopRight, nr.BottomLeft, nr.BottomRight };
            resizeHandle = Enumerable.Range(0, 4).OrderBy(i => (moved[i] - p).Length).First();
        }
        SetEnds(Selected, p1, p2);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Image == null) return;
        base.OnMouseLeftButtonDown(e);

        var p = Pos(e);
        dragStart = lastPoint = p;

        switch (Tool)
        {
            case Tool.Select:
                int handle = HitHandle(p);
                if (handle >= 0)
                {
                    resizeHandle = handle;
                    movedYet = false;
                    CaptureMouse();
                    return;
                }

                Selected = null;
                for (int i = Annotations.Count - 1; i >= 0; i--)
                {
                    if (!Annotations[i].HitTest(p)) continue;
                    Selected = Annotations[i];
                    moving = true;
                    movedYet = false;
                    CaptureMouse();
                    break;
                }
                InvalidateVisual();
                return;

            case Tool.Text:
                var text = PromptText?.Invoke("Annotation text");
                if (!string.IsNullOrEmpty(text))
                {
                    Changing?.Invoke();
                    Annotations.Add(new TextAnnotation
                    {
                        Position = p,
                        Text = text,
                        Color = StrokeColor,
                        Thickness = Thickness,
                        FontSize = 12 + Thickness * 3,
                    });
                    Changed?.Invoke();
                    InvalidateVisual();
                }
                return;

            case Tool.Step:
                Changing?.Invoke();
                int next = 1;
                foreach (var a in Annotations)
                    if (a is StepAnnotation s) next = Math.Max(next, s.Number + 1);
                Annotations.Add(new StepAnnotation { Center = p, Number = next, Color = StrokeColor, Thickness = Thickness });
                Changed?.Invoke();
                InvalidateVisual();
                return;

            case Tool.Crop:
                cropDraft = new Rect(p, p);
                CaptureMouse();
                return;

            default:
                draft = CreateDraft(p);
                CaptureMouse();
                return;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = Pos(e);

        if (!IsMouseCaptured)
        {
            if (Tool == Tool.Select)
                Cursor = HitHandle(p) >= 0 ? Cursors.SizeAll : null;
            else
                Cursor = Cursors.Cross;
            return;
        }

        if (resizeHandle >= 0 && Selected != null)
        {
            if (!movedYet && (p - dragStart).Length >= 2)
            {
                Changing?.Invoke();
                movedYet = true;
            }
            if (movedYet) ResizeSelected(p);
        }
        else if (moving && Selected != null)
        {
            var delta = p - lastPoint;
            if (!movedYet && (p - dragStart).Length >= 2)
            {
                Changing?.Invoke();
                movedYet = true;
            }
            if (movedYet) Selected.Move(delta);
            lastPoint = p;
        }
        else if (cropDraft != null)
        {
            var full = new Rect(0, 0, Image!.PixelWidth, Image.PixelHeight);
            cropDraft = Rect.Intersect(new Rect(dragStart, p), full);
        }
        else if (draft != null)
        {
            SetDraftEnd(p);
        }
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsMouseCaptured) return;
        ReleaseMouseCapture();

        if (resizeHandle >= 0)
        {
            resizeHandle = -1;
            if (movedYet) Changed?.Invoke();
            InvalidateVisual();
            return;
        }

        if (moving)
        {
            moving = false;
            if (movedYet) Changed?.Invoke();
            return;
        }

        if (cropDraft is Rect c)
        {
            cropDraft = null;
            if (!c.IsEmpty && c.Width >= 8 && c.Height >= 8)
            {
                Changing?.Invoke();
                Annotations.RemoveAll(a => a is CropAnnotation);
                Annotations.Add(new CropAnnotation { Area = c });
                Selected = null;
                Changed?.Invoke();
            }
            Refresh();
            return;
        }

        if (draft != null)
        {
            var finished = draft;
            draft = null;
            if (finished.Bounds.Width >= 3 || finished.Bounds.Height >= 3)
            {
                Changing?.Invoke();
                Annotations.Add(finished);
                Changed?.Invoke();
            }
            InvalidateVisual();
        }
    }

    private Annotation? CreateDraft(Point p)
    {
        switch (Tool)
        {
            case Tool.Arrow: return NewShape(ShapeKind.Arrow, p);
            case Tool.Line: return NewShape(ShapeKind.Line, p);
            case Tool.Rectangle: return NewShape(ShapeKind.Rectangle, p);
            case Tool.Ellipse: return NewShape(ShapeKind.Ellipse, p);
            case Tool.Highlight: return NewShape(ShapeKind.Highlight, p);
            case Tool.Pen:
                return new PenAnnotation { Points = new List<Point> { p }, Color = StrokeColor, Thickness = Thickness };
            case Tool.Pixelate:
                return new RedactAnnotation { Mode = RedactMode.Pixelate, P1 = p, P2 = p };
            case Tool.Blur:
                return new RedactAnnotation { Mode = RedactMode.Blur, P1 = p, P2 = p };
            case Tool.Solid:
                return new RedactAnnotation { Mode = RedactMode.Solid, P1 = p, P2 = p };
            default: return null;
        }
    }

    private ShapeAnnotation NewShape(ShapeKind kind, Point p) =>
        new() { Kind = kind, P1 = p, P2 = p, Color = StrokeColor, Thickness = Thickness, Head = Head, Dashed = Dashed, Filled = Filled };

    private void SetDraftEnd(Point p)
    {
        switch (draft)
        {
            case ShapeAnnotation s: s.P2 = p; break;
            case RedactAnnotation r: r.P2 = p; break;
            case PenAnnotation pen:
                if ((pen.Points[^1] - p).Length >= 1.5) pen.Points.Add(p);
                break;
        }
    }
}
