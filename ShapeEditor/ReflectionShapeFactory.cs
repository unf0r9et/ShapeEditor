using System.Collections.Generic;
using System.IO;

namespace ShapeEditor;

/// <summary>Creates shapes from types discovered via <see cref="ShapeLoader.AddLibrary"/>.</summary>
internal sealed class ReflectionShapeFactory : IShapeFactory
{
    private readonly Dictionary<string, Type> _types;

    public ReflectionShapeFactory(Dictionary<string, Type> types) =>
        _types = new Dictionary<string, Type>(types, StringComparer.Ordinal);

    public ShapeBase? TryCreate(string persistedTypeName)
    {
        if (string.IsNullOrEmpty(persistedTypeName))
            return null;

        if (!_types.TryGetValue(persistedTypeName, out Type? t))
            return null;

        try
        {
            object? o = Activator.CreateInstance(t);
            return o as ShapeBase;
        }
        catch
        {
            return null;
        }
    }

    public ShapeBase Create(string persistedTypeName) =>
        TryCreate(persistedTypeName)
        ?? throw new InvalidDataException($"Неизвестный тип фигуры: {persistedTypeName}");
}
