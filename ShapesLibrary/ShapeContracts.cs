using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ShapeEditor;

/// <summary>
/// Общие члены ShapeBase, доступные через интерфейсы фигур.
/// Реализуется автоматически любым типом, унаследованным от <see cref="ShapeBase"/>.
/// </summary>
public interface IShapeView
{
    int Id { get; set; }
    int SidesCount { get; }
    string DisplayNameRu { get; }
    string DisplayNameEn { get; }
    string[] SideNames { get; }

    double Scale { get; set; }
    double Angle { get; set; }
    Point AnchorPoint { get; set; }
    Brush Fill { get; set; }
    Point[] Vertices { get; set; }

    double MinX { get; }
    double MinY { get; }
    double MaxX { get; }
    double MaxY { get; }

    List<Brush> SideColors { get; set; }
    List<double> SideThickness { get; set; }
    List<bool> EdgeLengthLocked { get; set; }

    Point GetAnchorWorldPosition(Canvas canvas);
    (Point bottomLeft, Point topRight) GetBoundingBoxCorners(Canvas canvas);
    Canvas Build(double anchorWorldX, double anchorWorldY);
    bool IsPointInside(Point localPoint);
    Point SnapToEdgeCenter(Point point);
    double GetEdgeLength(int edgeIndex);
    void SetEdgeLength(int edgeIndex, double newLength);
    bool TrySetEdgeLengths(double[] lengths);
    void SaveToJson(Utf8JsonWriter writer);
    void LoadFromJson(JsonElement element);
    void SaveToFile(string filename);
}

/// <summary>Создаёт экземпляры фигур по строковому имени типа из JSON (реализация в ShapesLibrary).</summary>
public interface IShapeFactory
{
    ShapeBase? TryCreate(string persistedTypeName);

    ShapeBase Create(string persistedTypeName);
}

/// <summary>Set by <see cref="ShapeLoader"/> after the shapes plugin loads.</summary>
public static class ShapePluginContext
{
    public static IShapeFactory? Factory { get; set; }

    public static bool IsLoaded => Factory != null;
}

/// <summary>Lets the host app wire group/ellipse UI visibility without referencing the shapes plugin.</summary>
public static class CompoundShapeHost
{
    public static Func<ShapeBase, ShapeBase, bool>? IsEditingThisChild { get; set; }

    public static Func<ShapeBase, ShapeBase, bool>? IsHighlightedChild { get; set; }
}

/// <summary>Ellipse helper visibility when the ellipse is a child of a group.</summary>
public static class ShapeCompositionHost
{
    public static Func<ShapeBase, bool>? ShouldShowEllipseBuildHelpers { get; set; }
}

public sealed class PolygonCustomSegment
{
    public string Name { get; set; } = "Сегмент";
    public double Length { get; set; } = 100;
    public Brush Color { get; set; } = Brushes.Black;
    public double Thickness { get; set; } = 3.0;
    public double AngleToNext { get; set; }
    public bool AngleLocked { get; set; }
    public bool LengthLocked { get; set; }

    public PolygonCustomSegment Clone()
    {
        Brush c = Color is SolidColorBrush scb
            ? new SolidColorBrush(scb.Color)
            : Brushes.Black;
        return new PolygonCustomSegment
        {
            Name = Name,
            Length = Length,
            Color = c,
            Thickness = Thickness,
            AngleToNext = AngleToNext,
            AngleLocked = AngleLocked,
            LengthLocked = LengthLocked
        };
    }
}

public interface IEllipseShape : IShapeView
{
    bool IsCircle { get; set; }
    double MajorAxis { get; set; }
    double MinorAxis { get; set; }
    double FocalDistance { get; set; }
    double FocusOffset { get; set; }
    bool FociOnYAxis { get; }
    int ApproximationPoints { get; set; }
    void UpdateFociFromParameters();
    (Point f1, Point f2) GetGlobalFocusPositions(double centerWorldX, double centerWorldY);
    Point UpdateFromWorldFociFixedCenter(Point f1W, Point f2W);
    void UpdateFromWorldFoci(Point f1W, Point f2W, Point anchorW);
}

public interface IPolygonShape : IShapeView
{
    bool IsCustomSegmentShape { get; set; }
    double InitialDirection { get; set; }
    bool IsClosed { get; set; }
    List<PolygonCustomSegment> Segments { get; set; }
    string PolygonType { get; set; }
    bool EnforceIsosceles { get; set; }
    string? DisplayNameRuOverride { get; set; }
    string[]? SideNamesOverride { get; set; }
    void AddSegment(double length, double internalAngle = 180);
    void RemoveSegment(int index);
    void SetEdgeAngle(int edgeIndex, double newAngleDegrees);
    double GetEdgeAngle(int edgeIndex);
    void RebuildVertices();
    Vector CenterAnchorToBounds();
}

public interface ICompoundShape : IShapeView
{
    List<ShapeBase> ChildShapes { get; }
    Dictionary<int, Point> ChildAnchorOffsets { get; }
    void AddChildShape(ShapeBase child);
    void RemoveChildShape(ShapeBase child);
    Point GetChildAnchorOffsetOrFallback(ShapeBase child);
    void RecalculateBounds();
}
