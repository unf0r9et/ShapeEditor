using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ShapeEditor;

/// <summary>
/// Регистрация фигур через <see cref="AddLibrary"/> (сканирование атрибутов <see cref="ExportedShapeAttribute"/>).
/// <see cref="DllOpen"/> — загрузка произвольной .dll с диска (для расширений, собранных против той же модели типов).
/// При старте <see cref="TryLoadShapesPlugin"/> регистрирует типы из сборки <c>ShapesLibrary</c>, на которую ссылается приложение.
/// </summary>
public static class ShapeLoader
{
    public const string PluginSubFolder = "plugins";
    public const string PluginDllFileName = "ShapesLibrary.dll";

    private static readonly Dictionary<string, Type> TypeRegistry = new(StringComparer.Ordinal);

    /// <summary>Сбрасывает реестр типов и фабрику (перед повторной загрузкой DLL).</summary>
    public static void ClearShapeTypeRegistry()
    {
        TypeRegistry.Clear();
        ShapePluginContext.Factory = null;
    }

    /// <summary>Загружает сборку с диска. Не регистрирует типы фигур.</summary>
    /// <param name="path">Полный или относительный путь к .dll</param>
    /// <param name="errorMessage">Текст ошибки при неудаче</param>
    /// <returns>Сборка или <c>null</c></returns>
    public static Assembly? DllOpen(string path, out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            errorMessage = "Путь к сборке не задан.";
            return null;
        }

        string full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            errorMessage = $"Файл сборки не найден: {full}";
            return null;
        }

        try
        {
            return Assembly.LoadFrom(full);
        }
        catch (BadImageFormatException ex)
        {
            errorMessage = $"Неверный формат сборки: {ex.Message}";
            return null;
        }
        catch (FileLoadException ex)
        {
            errorMessage = $"Не удалось загрузить файл сборки: {ex.Message}";
            return null;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Обходит публичные типы сборки, отбирает неабстрактные классы, наследующие <see cref="ShapeBase"/>,
    /// с атрибутом <see cref="ExportedShapeAttribute"/> и публичным конструктором без параметров, и регистрирует их по имени для JSON/фабрики.
    /// </summary>
    public static bool AddLibrary(Assembly assembly, out string? errorMessage)
    {
        errorMessage = null;
        if (assembly is null)
        {
            errorMessage = "Сборка не задана (null).";
            return false;
        }

        Type shapeBase = typeof(ShapeBase);
        List<(string Key, Type Type)> batch = new();

        Type[] exported;
        try
        {
            exported = assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            errorMessage = DescribeLoaderExceptions(ex);
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"Не удалось прочитать типы сборки: {ex.Message}";
            return false;
        }

        foreach (Type t in exported)
        {
            if (!t.IsClass || t.IsAbstract)
                continue;

            if (!shapeBase.IsAssignableFrom(t))
                continue;

            object[] attrs = t.GetCustomAttributes(typeof(ExportedShapeAttribute), inherit: false);
            if (attrs.Length == 0)
                continue;

            ConstructorInfo? ctor = t.GetConstructor(Type.EmptyTypes);
            if (ctor is null || !ctor.IsPublic)
            {
                errorMessage =
                    $"Тип «{t.FullName}» помечен [ExportedShape], но нужен публичный конструктор без параметров.";
                return false;
            }

            foreach (object obj in attrs)
            {
                if (obj is not ExportedShapeAttribute a)
                    continue;

                string key = a.PersistedTypeName;
                if (string.IsNullOrWhiteSpace(key))
                {
                    errorMessage = $"У типа «{t.FullName}» указано пустое имя в [ExportedShape].";
                    return false;
                }

                batch.Add((key, t));
            }
        }

        foreach ((string key, Type t) in batch)
        {
            if (TypeRegistry.TryGetValue(key, out Type? existing))
            {
                errorMessage =
                    $"Имя фигуры «{key}» уже зарегистрировано ({existing.FullName}); повтор в типе {t.FullName}.";
                return false;
            }
        }

        foreach ((string key, Type t) in batch)
            TypeRegistry[key] = t;

        ShapePluginContext.Factory = new ReflectionShapeFactory(new Dictionary<string, Type>(TypeRegistry, StringComparer.Ordinal));
        return true;
    }

    private static string DescribeLoaderExceptions(ReflectionTypeLoadException ex)
    {
        if (ex.LoaderExceptions is null || ex.LoaderExceptions.Length == 0)
            return ex.Message;

        List<string> parts = new();
        foreach (Exception? le in ex.LoaderExceptions)
        {
            if (le != null)
                parts.Add(le.Message);
        }

        return parts.Count > 0 ? string.Join("; ", parts) : ex.Message;
    }

    /// <summary>
    /// Регистрирует фигуры из сборки <c>ShapesLibrary</c>, на которую ссылается приложение (одна библиотека: <see cref="ShapeBase"/> + реализации).
    /// Параметр <paramref name="pluginDirectory"/> зарезервирован; подмена DLL через отдельный путь без смены ссылки проекта не поддерживается.
    /// </summary>
    public static bool TryLoadShapesPlugin(string? pluginDirectory = null)
    {
        _ = pluginDirectory;
        ClearShapeTypeRegistry();
        return AddLibrary(typeof(ShapeBase).Assembly, out _);
    }

    public static IShapeFactory RequireFactory() =>
        ShapePluginContext.Factory
        ?? throw new InvalidOperationException(
            $"Не удалось загрузить фигуры. Положите {PluginDllFileName} в папку «{PluginSubFolder}» или рядом с приложением.");

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
