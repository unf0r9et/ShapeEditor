using System;
using System.IO;
using System.Reflection;
using System.Windows;

namespace ShapeEditor;

/// <summary>
/// Loads ShapesLibrary.dll at runtime (no project reference to the plugin assembly).
/// Looks for <c>plugins/ShapesLibrary.dll</c> first, then the application directory.
/// </summary>
public static class ShapeLoader
{
    public const string PluginSubFolder = "plugins";
    public const string PluginDllFileName = "ShapesLibrary.dll";

    /// <summary>Attempt to load the shapes plugin. On failure, <see cref="ShapePluginContext.Factory"/> stays null.</summary>
    public static bool TryLoadShapesPlugin(string? pluginDirectory = null)
    {
        ShapePluginContext.Factory = null;
        try
        {
            string baseDir = AppContext.BaseDirectory;
            string path = Path.Combine(pluginDirectory ?? Path.Combine(baseDir, PluginSubFolder), PluginDllFileName);
            if (!File.Exists(path))
            {
                path = Path.Combine(baseDir, PluginDllFileName);
                if (!File.Exists(path))
                    return false;
            }

            Assembly asm = Assembly.LoadFrom(path);
            Type? entry = asm.GetType("ShapeEditor.ShapePluginEntry");
            if (entry == null)
                return false;

            MethodInfo? m = entry.GetMethod("CreateFactory", BindingFlags.Public | BindingFlags.Static);
            if (m == null)
                return false;

            if (m.Invoke(null, null) is not IShapeFactory factory)
                return false;

            ShapePluginContext.Factory = factory;
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
            $"Не удалось загрузить плагин фигур. Убедитесь, что {PluginDllFileName} находится в папке «{PluginSubFolder}» рядом с приложением.");

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
