using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace RedSnapper;

// Plain-data form of an annotation so editable work can be saved next to a capture and reopened later.
public sealed class AnnotationData
{
    public string Type { get; set; } = "";
    public string Color { get; set; } = "#FFFF0000";
    public double Thickness { get; set; } = 4;
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public string? Kind { get; set; }
    public string? Text { get; set; }
    public double FontSize { get; set; }
    public int Number { get; set; }
    public string? Head { get; set; }
    public bool Dashed { get; set; }
    public bool Filled { get; set; }
    public List<double>? Points { get; set; }
}

internal static class AnnotationStore
{
    public static List<AnnotationData> ToData(IEnumerable<Annotation> annotations) =>
        annotations.Select(ToData).Where(d => d != null).Select(d => d!).ToList();

    public static List<Annotation> FromData(IEnumerable<AnnotationData> data) =>
        data.Select(FromData).Where(a => a != null).Select(a => a!).ToList();

    private static AnnotationData? ToData(Annotation a)
    {
        var d = new AnnotationData { Color = a.Color.ToString(), Thickness = a.Thickness };
        switch (a)
        {
            case ShapeAnnotation s:
                d.Type = "shape"; d.Kind = s.Kind.ToString();
                (d.Head, d.Dashed, d.Filled) = (s.Head.ToString(), s.Dashed, s.Filled);
                (d.X1, d.Y1, d.X2, d.Y2) = (s.P1.X, s.P1.Y, s.P2.X, s.P2.Y);
                break;
            case PenAnnotation pen:
                d.Type = "pen";
                d.Points = pen.Points.SelectMany(p => new[] { p.X, p.Y }).ToList();
                break;
            case CropAnnotation c:
                d.Type = "crop";
                (d.X1, d.Y1, d.X2, d.Y2) = (c.Area.Left, c.Area.Top, c.Area.Right, c.Area.Bottom);
                break;
            case RedactAnnotation r:
                d.Type = "redact"; d.Kind = r.Mode.ToString();
                (d.X1, d.Y1, d.X2, d.Y2) = (r.P1.X, r.P1.Y, r.P2.X, r.P2.Y);
                break;
            case TextAnnotation t:
                d.Type = "text"; d.Text = t.Text; d.FontSize = t.FontSize;
                (d.X1, d.Y1) = (t.Position.X, t.Position.Y);
                break;
            case StepAnnotation st:
                d.Type = "step"; d.Number = st.Number;
                (d.X1, d.Y1) = (st.Center.X, st.Center.Y);
                break;
            default:
                return null;
        }
        return d;
    }

    private static Annotation? FromData(AnnotationData d)
    {
        Color color;
        try { color = (Color)ColorConverter.ConvertFromString(d.Color); }
        catch (FormatException) { color = Colors.Red; }

        Annotation? a = d.Type switch
        {
            "shape" when Enum.TryParse<ShapeKind>(d.Kind, out var kind) =>
                new ShapeAnnotation
                {
                    Kind = kind, P1 = new Point(d.X1, d.Y1), P2 = new Point(d.X2, d.Y2),
                    Head = Enum.TryParse<ArrowHead>(d.Head, out var head) ? head : ArrowHead.Arrow,
                    Dashed = d.Dashed, Filled = d.Filled,
                },
            "pen" when d.Points != null =>
                new PenAnnotation { Points = Enumerable.Range(0, d.Points.Count / 2).Select(i => new Point(d.Points[i * 2], d.Points[i * 2 + 1])).ToList() },
            "crop" => new CropAnnotation { Area = new Rect(new Point(d.X1, d.Y1), new Point(d.X2, d.Y2)) },
            "redact" when Enum.TryParse<RedactMode>(d.Kind, out var mode) =>
                new RedactAnnotation { Mode = mode, P1 = new Point(d.X1, d.Y1), P2 = new Point(d.X2, d.Y2) },
            "text" => new TextAnnotation { Text = d.Text ?? "", FontSize = d.FontSize > 0 ? d.FontSize : 24, Position = new Point(d.X1, d.Y1) },
            "step" => new StepAnnotation { Number = d.Number, Center = new Point(d.X1, d.Y1) },
            _ => null,
        };
        if (a != null)
        {
            a.Color = color;
            a.Thickness = d.Thickness;
        }
        return a;
    }
}
