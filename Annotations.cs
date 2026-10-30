using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RedSnapper;

public enum Tool { Select, Arrow, Line, Rectangle, Ellipse, Highlight, Text, Step, Pixelate, Solid }

public abstract class Annotation
{
    public Color Color { get; set; } = Colors.Red;
    public double Thickness { get; set; } = 4;

    public abstract Rect Bounds { get; }
    public abstract void Move(Vector delta);
    public abstract void Draw(DrawingContext dc, BitmapSource baseImage);

    public virtual bool HitTest(Point p)
    {
        var b = Bounds;
        b.Inflate(6, 6);
        return b.Contains(p);
    }

    public Annotation Clone() => (Annotation)MemberwiseClone();

    protected Brush MakeBrush(byte? alpha = null)
    {
        var c = alpha is byte a ? Color.FromArgb(a, Color.R, Color.G, Color.B) : Color;
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }
}

public enum ShapeKind { Line, Arrow, Rectangle, Ellipse, Highlight }

public sealed class ShapeAnnotation : Annotation
{
    public ShapeKind Kind { get; set; }
    public Point P1 { get; set; }
    public Point P2 { get; set; }

    private Rect Box => new(P1, P2);

    public override Rect Bounds
    {
        get
        {
            var b = Box;
            if (Kind is ShapeKind.Line or ShapeKind.Arrow)
                b.Inflate(Thickness, Thickness);
            return b;
        }
    }

    public override void Move(Vector delta)
    {
        P1 += delta;
        P2 += delta;
    }

    public override bool HitTest(Point p)
    {
        if (Kind is ShapeKind.Line or ShapeKind.Arrow)
            return DistanceToSegment(p, P1, P2) <= Thickness + 6;
        return base.HitTest(p);
    }

    public override void Draw(DrawingContext dc, BitmapSource baseImage)
    {
        var brush = MakeBrush();
        var pen = new Pen(brush, Thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

        switch (Kind)
        {
            case ShapeKind.Line:
                dc.DrawLine(pen, P1, P2);
                break;
            case ShapeKind.Rectangle:
                dc.DrawRectangle(null, pen, Box);
                break;
            case ShapeKind.Ellipse:
                var b = Box;
                dc.DrawEllipse(null, pen, new Point(b.X + b.Width / 2, b.Y + b.Height / 2), b.Width / 2, b.Height / 2);
                break;
            case ShapeKind.Highlight:
                dc.DrawRectangle(MakeBrush(0x66), null, Box);
                break;
            case ShapeKind.Arrow:
                DrawArrow(dc, brush, pen);
                break;
        }
    }

    private void DrawArrow(DrawingContext dc, Brush brush, Pen pen)
    {
        var v = P2 - P1;
        double len = v.Length;
        if (len < 1) return;

        v.Normalize();
        var normal = new Vector(-v.Y, v.X);
        double headLen = 10 + 3 * Thickness;
        double headHalfWidth = headLen * 0.5;
        var baseCenter = P2 - v * headLen * 0.8;

        dc.DrawLine(pen, P1, baseCenter);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(P2, true, true);
            ctx.LineTo(baseCenter + normal * headHalfWidth, true, false);
            ctx.LineTo(baseCenter - normal * headHalfWidth, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(brush, null, geo);
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        double lenSq = ab.LengthSquared;
        if (lenSq < 1e-6) return (p - a).Length;
        double t = Math.Clamp(Vector.Multiply(p - a, ab) / lenSq, 0, 1);
        return (p - (a + ab * t)).Length;
    }
}

public sealed class TextAnnotation : Annotation
{
    public Point Position { get; set; }
    public string Text { get; set; } = "";
    public double FontSize { get; set; } = 24;

    private FormattedText Format() => new(
        Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
        new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
        FontSize, MakeBrush(), 1.0);

    public override Rect Bounds
    {
        get
        {
            var ft = Format();
            return new Rect(Position, new Size(ft.Width, ft.Height));
        }
    }

    public override void Move(Vector delta) => Position += delta;

    public override void Draw(DrawingContext dc, BitmapSource baseImage) => dc.DrawText(Format(), Position);
}

public sealed class StepAnnotation : Annotation
{
    public Point Center { get; set; }
    public int Number { get; set; } = 1;

    private double Radius => 12 + Thickness * 1.5;

    public override Rect Bounds => new(Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);

    public override void Move(Vector delta) => Center += delta;

    public override void Draw(DrawingContext dc, BitmapSource baseImage)
    {
        dc.DrawEllipse(MakeBrush(), new Pen(Brushes.White, 2), Center, Radius, Radius);
        var ft = new FormattedText(
            Number.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            Radius * 1.1, Brushes.White, 1.0);
        dc.DrawText(ft, new Point(Center.X - ft.Width / 2, Center.Y - ft.Height / 2));
    }
}

public enum RedactMode { Pixelate, Solid }

public sealed class RedactAnnotation : Annotation
{
    public RedactMode Mode { get; set; }
    public Point P1 { get; set; }
    public Point P2 { get; set; }

    private BitmapSource? cachedSource;
    private Int32Rect cachedRect;
    private BitmapSource? cachedPixels;

    public override Rect Bounds => new(P1, P2);

    public override void Move(Vector delta)
    {
        P1 += delta;
        P2 += delta;
    }

    public override void Draw(DrawingContext dc, BitmapSource baseImage)
    {
        var box = Bounds;
        if (Mode == RedactMode.Solid)
        {
            dc.DrawRectangle(Brushes.Black, null, box);
            return;
        }

        var rect = Clamp(box, baseImage.PixelWidth, baseImage.PixelHeight);
        if (rect.Width < 1 || rect.Height < 1) return;

        if (cachedPixels == null || !ReferenceEquals(cachedSource, baseImage) || !cachedRect.Equals(rect))
        {
            cachedPixels = Pixelate(baseImage, rect);
            cachedSource = baseImage;
            cachedRect = rect;
        }

        dc.DrawImage(cachedPixels, new Rect(rect.X, rect.Y, rect.Width, rect.Height));
    }

    private static Int32Rect Clamp(Rect r, int maxW, int maxH)
    {
        int x = (int)Math.Max(0, Math.Floor(r.X));
        int y = (int)Math.Max(0, Math.Floor(r.Y));
        int right = (int)Math.Min(maxW, Math.Ceiling(r.Right));
        int bottom = (int)Math.Min(maxH, Math.Ceiling(r.Bottom));
        return new Int32Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    private static BitmapSource Pixelate(BitmapSource src, Int32Rect rect)
    {
        var converted = new FormatConvertedBitmap(new CroppedBitmap(src, rect), PixelFormats.Bgra32, null, 0);
        int w = rect.Width, h = rect.Height, stride = w * 4;
        var px = new byte[stride * h];
        converted.CopyPixels(px, stride, 0);

        const int block = 12;
        for (int by = 0; by < h; by += block)
        {
            for (int bx = 0; bx < w; bx += block)
            {
                int bw = Math.Min(block, w - bx), bh = Math.Min(block, h - by);
                long b = 0, g = 0, r = 0;
                for (int y = 0; y < bh; y++)
                    for (int x = 0; x < bw; x++)
                    {
                        int i = (by + y) * stride + (bx + x) * 4;
                        b += px[i]; g += px[i + 1]; r += px[i + 2];
                    }

                int n = bw * bh;
                byte ab = (byte)(b / n), ag = (byte)(g / n), ar = (byte)(r / n);
                for (int y = 0; y < bh; y++)
                    for (int x = 0; x < bw; x++)
                    {
                        int i = (by + y) * stride + (bx + x) * 4;
                        px[i] = ab; px[i + 1] = ag; px[i + 2] = ar; px[i + 3] = 255;
                    }
            }
        }

        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, stride);
        result.Freeze();
        return result;
    }
}
