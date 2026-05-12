using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace ShapeEditor;

/// <summary>Creates concrete shapes from persisted JSON type names (implemented by the dynamically loaded plugin).</summary>
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

public interface IEllipseShape
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
}

public interface IPolygonShape
{
    bool IsCustomSegmentShape { get; set; }
    double InitialDirection { get; set; }
    bool IsClosed { get; set; }
    List<PolygonCustomSegment> Segments { get; set; }
    string PolygonType { get; set; }
    bool EnforceIsosceles { get; set; }
    string DisplayNameRuOverride { get; set; }
    string[]? SideNamesOverride { get; set; }
    void AddSegment(double length, double internalAngle = 180);
    void RemoveSegment(int index);
    void SetEdgeAngle(int edgeIndex, double newAngleDegrees);
    double GetEdgeAngle(int edgeIndex);
    void RebuildVertices();
    Vector CenterAnchorToBounds();
}

public interface ICompoundShape
{
    List<ShapeBase> ChildShapes { get; }
    Dictionary<int, Point> ChildAnchorOffsets { get; }
    void AddChildShape(ShapeBase child);
    void RemoveChildShape(ShapeBase child);
    Point GetChildAnchorOffsetOrFallback(ShapeBase child);
    void RecalculateBounds();
}
