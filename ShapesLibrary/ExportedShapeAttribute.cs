namespace ShapeEditor;

/// <summary>
/// Marks a <see cref="ShapeBase"/>-derived type as loadable under the given persisted JSON / factory key.
/// The type must have a public parameterless constructor.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ExportedShapeAttribute : Attribute
{
    public string PersistedTypeName { get; }

    public ExportedShapeAttribute(string persistedTypeName) =>
        PersistedTypeName = persistedTypeName ?? throw new ArgumentNullException(nameof(persistedTypeName));
}
