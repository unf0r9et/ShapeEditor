using System.IO;

namespace ShapeEditor;

/// <summary>Reflection-friendly entry: <see cref="ShapeLoader"/> locates this type in ShapesLibrary.dll.</summary>
public static class ShapePluginEntry
{
    public static IShapeFactory CreateFactory() => new ShapeFactoryImpl();

    private sealed class ShapeFactoryImpl : IShapeFactory
    {
        public ShapeBase? TryCreate(string persistedTypeName)
        {
            if (string.IsNullOrEmpty(persistedTypeName))
                return null;

            return persistedTypeName switch
            {
                "PolygonShape" => new PolygonShape(),
                "EllipseShape" => new EllipseShape(),
                "CustomShape" => new PolygonShape { IsCustomSegmentShape = true },
                "CompoundShape" => new CompoundShape(),
                "CircleShape" => new EllipseShape { IsCircle = true, MajorAxis = 100 },
                "RectangleShape" => PolygonShape.CreateRectangle(),
                "TriangleShape" => PolygonShape.CreateTriangle(),
                "TrapezoidShape" => PolygonShape.CreateTrapezoid(),
                "HexagonShape" => PolygonShape.CreateHexagon(),
                _ => null
            };
        }

        public ShapeBase Create(string persistedTypeName) =>
            TryCreate(persistedTypeName)
            ?? throw new InvalidDataException($"Неизвестный тип фигуры: {persistedTypeName}");
    }
}
