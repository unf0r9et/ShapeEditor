namespace ShapeEditor;

/// <summary>Дополнительные ключи фабрики (пресеты), отдельные от базовых классов Polygon/Ellipse.</summary>
[ExportedShape("RectangleShape")]
public sealed class RectangleShape : PolygonShape
{
    public RectangleShape() : base(4) => InitializeAsRectangle(this);
}

[ExportedShape("TriangleShape")]
public sealed class TriangleShape : PolygonShape
{
    public TriangleShape() : base(3) => InitializeAsTriangle(this);
}

[ExportedShape("TrapezoidShape")]
public sealed class TrapezoidShape : PolygonShape
{
    public TrapezoidShape() : base(4) => InitializeAsTrapezoid(this);
}

[ExportedShape("HexagonShape")]
public sealed class HexagonShape : PolygonShape
{
    public HexagonShape() : base(6) => InitializeAsHexagon(this);
}

[ExportedShape("CircleShape")]
public sealed class CircleShape : EllipseShape
{
    public CircleShape()
    {
        IsCircle = true;
        MajorAxis = 100;
    }
}

[ExportedShape("CustomShape")]
public sealed class CustomShape : PolygonShape
{
    public CustomShape()
    {
        IsCustomSegmentShape = true;
    }
}
