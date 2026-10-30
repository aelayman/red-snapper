using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RedSnapper;

public sealed class CanvasView : FrameworkElement
{
    public BitmapSource? Image { get; set; }
    public List<Annotation> Annotations { get; private set; } = new();
    public Annotation? Selected { get; set; }
    public Tool Tool { get; set; } = Tool.Select;
    public Color StrokeColor { get; set; } = Colors.Red;
    public double Thickness { get; set; } = 4;

    public Func<string, string?>? PromptText { get; set; }
    public event Action? Changing;
    public event Action? Changed;

    private Annotation? draft;
    private Point dragStart;
    private Point lastPoint;
    private bool moving;
    private bool movedYet;

    public void ReplaceAnnotations(List<Annotation> list)
    {
        Annotations = list;
        Selected = null;
        InvalidateVisual();
        Changed?.Invoke();
    }

    public void DeleteSelected()
    {
        if (Selected == null) return;
        Changing?.Invoke();
        Annotations.Remove(Selected);
        Selected = null;
        InvalidateVisual();
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

    public BitmapSource RenderToBitmap()
    {
        int w = Image!.PixelWidth, h = Image.PixelHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            DrawContent(dc, includeSelection: false);

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    protected override Size MeasureOverride(Size availableSize) =>
        Image == null ? new Size(0, 0) : new Size(Image.PixelWidth, Image.PixelHeight);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        DrawContent(dc, includeSelection: true);
    }

    private void DrawContent(DrawingContext dc, bool includeSelection)
    {
        if (Image == null) return;

        dc.DrawImage(Image, new Rect(0, 0, Image.PixelWidth, Image.PixelHeight));
        foreach (var a in Annotations) a.Draw(dc, Image);
        draft?.Draw(dc, Image);

        if (includeSelection && Selected != null)
        {
            var b = Selected.Bounds;
            b.Inflate(4, 4);
            var pen = new Pen(Brushes.DodgerBlue, 1.5) { DashStyle = DashStyles.Dash };
            dc.DrawRectangle(null, pen, b);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Image == null) return;
        base.OnMouseLeftButtonDown(e);

        var p = e.GetPosition(this);
        dragStart = lastPoint = p;

        switch (Tool)
        {
            case Tool.Select:
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

            default:
                draft = CreateDraft(p);
                CaptureMouse();
                return;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsMouseCaptured) return;

        var p = e.GetPosition(this);
        if (moving && Selected != null)
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

        if (moving)
        {
            moving = false;
            if (movedYet) Changed?.Invoke();
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
            case Tool.Pixelate:
                return new RedactAnnotation { Mode = RedactMode.Pixelate, P1 = p, P2 = p };
            case Tool.Solid:
                return new RedactAnnotation { Mode = RedactMode.Solid, P1 = p, P2 = p };
            default: return null;
        }
    }

    private ShapeAnnotation NewShape(ShapeKind kind, Point p) =>
        new() { Kind = kind, P1 = p, P2 = p, Color = StrokeColor, Thickness = Thickness };

    private void SetDraftEnd(Point p)
    {
        switch (draft)
        {
            case ShapeAnnotation s: s.P2 = p; break;
            case RedactAnnotation r: r.P2 = p; break;
        }
    }
}
