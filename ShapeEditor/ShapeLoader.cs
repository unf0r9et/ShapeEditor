using System;

namespace ShapeEditor;

/// <summary>Инициализирует фабрику фигур из сборки ShapesLibrary (обычная ссылка на проект).</summary>
public static class ShapeLoader
{
    /// <summary>Регистрирует фабрику в <see cref="ShapePluginContext"/>.</summary>
    public static bool TryLoadShapesPlugin()
    {
        try
        {
            ShapePluginContext.Factory = ShapePluginEntry.CreateFactory();
            return true;
        }
        catch
        {
            ShapePluginContext.Factory = null;
            return false;
        }
    }

    public static IShapeFactory RequireFactory() =>
        ShapePluginContext.Factory
        ?? throw new InvalidOperationException(
            "Фабрика фигур не инициализирована. Вызовите ShapeLoader.TryLoadShapesPlugin() при старте приложения.");

    public static ShapeBase CreateFromPersistedType(string typeName) => RequireFactory().Create(typeName);

    public static ShapeBase CreateRectangle() => RequireFactory().Create("RectangleShape");

    public static ShapeBase CreateTriangle() => RequireFactory().Create("TriangleShape");

    public static ShapeBase CreateTrapezoid() => RequireFactory().Create("TrapezoidShape");

    public static ShapeBase CreateHexagon() => RequireFactory().Create("HexagonShape");

    public static ShapeBase CreateCircle() => RequireFactory().Create("CircleShape");

    public static ShapeBase CreateCompound() => RequireFactory().Create("CompoundShape");

    public static ShapeBase CreateCustomPolygon() => RequireFactory().Create("CustomShape");

    public static ShapeBase CreateEllipseToolbarDefault()
    {
        var s = RequireFactory().Create("EllipseShape");
        if (s is IEllipseShape e)
        {
            e.IsCircle = false;
            e.MajorAxis = 120;
            e.MinorAxis = 80;
            e.FocalDistance = 40;
        }

        return s;
    }
}
