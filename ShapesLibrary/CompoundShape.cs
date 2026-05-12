using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Text.Json;

namespace ShapeEditor;

/// <summary>
/// Составная фигура (группа). Содержит дочерние фигуры любого типа.
/// </summary>
[ExportedShape("CompoundShape")]
public class CompoundShape : ShapeBase, ICompoundShape
{
        public List<ShapeBase> ChildShapes { get; set; } = new();

        /// <summary>
        /// World-space offsets of child anchor positions relative to the group's anchor.
        /// Keyed by child Id to remain stable across UI rebuilds/serialization.
        /// </summary>
        public Dictionary<int, Point> ChildAnchorOffsets { get; private set; } = new();

        public CompoundShape()
        {
            SidesCount = 0;
            Fill = Brushes.Transparent;
        }
        public override string DisplayNameEn => "Group";
        public override string DisplayNameRu => "Группа";
        protected override Point[] GetDefaultVertices() => new Point[0];

        #region Управление детьми

        public void AddChildShape(ShapeBase child)
        {
            if (child == null || child is ICompoundShape || ChildShapes.Contains(child))
                return;
            ChildShapes.Add(child);
            if (!ChildAnchorOffsets.ContainsKey(child.Id))
                ChildAnchorOffsets[child.Id] = new Point(0, 0);
        }

        public void RemoveChildShape(ShapeBase child)
        {
            ChildShapes.Remove(child);
            if (child != null)
                ChildAnchorOffsets.Remove(child.Id);
        }

        public Point GetChildAnchorOffsetOrFallback(ShapeBase child)
        {
            if (child == null) return new Point(0, 0);
            if (ChildAnchorOffsets.TryGetValue(child.Id, out var p))
                return p;
            // Backward-compatibility: older groups encoded child offset in AnchorPoint.
            return child.AnchorPoint;
        }

        public override bool IsPointInside(Point localPoint)
        {
            return ChildShapes.Any(child => child.IsPointInside(localPoint));
        }

        /// <summary>
        /// Пересчитывает границы на основе детей
        /// </summary>
        public void RecalculateBounds()
        {
            if (ChildShapes.Count == 0)
            {
                MinX = -20; MinY = -20;
                MaxX = 20; MaxY = 20;
                return;
            }

            double rawMinX = double.MaxValue, rawMaxX = double.MinValue;
            double rawMinY = double.MaxValue, rawMaxY = double.MinValue;

            foreach (var child in ChildShapes)
            {
                if (child is ICompoundShape childCompound)
                    childCompound.RecalculateBounds();

                var offset = GetChildAnchorOffsetOrFallback(child);
                rawMinX = Math.Min(rawMinX, offset.X + child.MinX);
                rawMaxX = Math.Max(rawMaxX, offset.X + child.MaxX);
                rawMinY = Math.Min(rawMinY, offset.Y + child.MinY);
                rawMaxY = Math.Max(rawMaxY, offset.Y + child.MaxY);
            }

            MinX = rawMinX; MinY = rawMinY;
            MaxX = rawMaxX; MaxY = rawMaxY;
        }

        #endregion

        #region Рендеринг

        public override Canvas Build(double anchorWorldX, double anchorWorldY)
        {
            RecalculateBounds();

            if (ChildShapes.Count == 0)
            {
                MinX = -20; MinY = -20; MaxX = 20; MaxY = 20;
                Canvas empty = new Canvas { Width = 40, Height = 40 };
                Canvas.SetLeft(empty, anchorWorldX + MinX * Scale);
                Canvas.SetTop(empty, anchorWorldY + MinY * Scale);
                return empty;
            }

            double width = (MaxX - MinX) * Scale;
            double height = (MaxY - MinY) * Scale;

            var container = new Canvas
            {
                Width = Math.Max(1, width),
                Height = Math.Max(1, height),
                Background = null
            };

            foreach (var child in ChildShapes)
            {
                var childVisual = child.Build(0, 0);
                childVisual.Tag = child;

                var childOffset = GetChildAnchorOffsetOrFallback(child);
                double offsetX = (childOffset.X + child.MinX - MinX) * Scale;
                double offsetY = (childOffset.Y + child.MinY - MinY) * Scale;

                Canvas.SetLeft(childVisual, offsetX);
                Canvas.SetTop(childVisual, offsetY);

                // Управление видимостью якорей детей
                bool isEditingThis = CompoundShapeHost.IsEditingThisChild?.Invoke(this, child) == true;
                bool isHighlightedFromTree = CompoundShapeHost.IsHighlightedChild?.Invoke(this, child) == true;

                // Теперь ищем не только "Anchor", но и "Focus1", "Focus2"
                foreach (var sub in childVisual.Children.OfType<Ellipse>())
                {
                    string tag = sub.Tag?.ToString();
                    if (tag == "Anchor" || tag == "Focus1" || tag == "Focus2")
                    {
                        // Показываем точки только если мы редактируем конкретно эту фигуру 
                        // или выбрали её в дереве объектов. В остальных случаях — скрываем.
                        sub.Visibility = (isEditingThis || isHighlightedFromTree) ? Visibility.Visible : Visibility.Collapsed;
                    }
                }

                container.Children.Add(childVisual);
            }

            // Якорь группы
            var anchorDot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = Brushes.White,
                Stroke = Brushes.Purple,
                StrokeThickness = 1,
                Tag = "Anchor",
                Visibility = Visibility.Collapsed
            };

            double anchorVisualX = (AnchorPoint.X - MinX) * Scale - 5;
            double anchorVisualY = (AnchorPoint.Y - MinY) * Scale - 5;

            Canvas.SetLeft(anchorDot, anchorVisualX);
            Canvas.SetTop(anchorDot, anchorVisualY);
            container.Children.Add(anchorDot);

            double containerLeft = anchorWorldX - (AnchorPoint.X - MinX) * Scale;
            double containerTop = anchorWorldY - (AnchorPoint.Y - MinY) * Scale;

            Canvas.SetLeft(container, containerLeft);
            Canvas.SetTop(container, containerTop);

            return container;
        }

        #endregion

        #region JSON Сохранение / Загрузка

        public override void SaveToJson(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "CompoundShape");
            writer.WriteNumber("id", Id);
            writer.WriteString("displayName", DisplayNameEn);
            writer.WriteNumber("scale", Scale);
            writer.WriteNumber("angle", Angle);
            writer.WriteNumber("anchorX", AnchorPoint.X);
            writer.WriteNumber("anchorY", AnchorPoint.Y);
            writer.WriteString("fill", GetColorHex(Fill));

            // Сохраняем смещения в том же порядке, в котором идут фигуры
            writer.WritePropertyName("childOffsets");
            writer.WriteStartArray();
            foreach (var child in ChildShapes)
            {
                var p = GetChildAnchorOffsetOrFallback(child);
                writer.WriteStartObject();
                // childId оставляем для структуры, но при загрузке будем ориентироваться на порядок
                writer.WriteNumber("childId", child.Id);
                writer.WriteNumber("x", p.X);
                writer.WriteNumber("y", p.Y);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WritePropertyName("childShapes");
            writer.WriteStartArray();
            foreach (var child in ChildShapes)
            {
                child.SaveToJson(writer);
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        public override void LoadFromJson(JsonElement element)
        {
            // ID не трогаем, как вы и просили (пусть пересчитывается в MainWindow/Constructor)
            if (element.TryGetProperty("scale", out var sProp)) Scale = sProp.GetDouble();
            if (element.TryGetProperty("angle", out var aProp)) Angle = aProp.GetDouble();
            if (element.TryGetProperty("anchorX", out var axProp) && element.TryGetProperty("anchorY", out var ayProp))
                AnchorPoint = new Point(axProp.GetDouble(), ayProp.GetDouble());
            if (element.TryGetProperty("fill", out var fProp)) Fill = ParseColor(fProp.GetString());

            ChildShapes.Clear();
            ChildAnchorOffsets.Clear();

            // 1. Сначала читаем все смещения в список (чтобы сохранить их порядок)
            var tempOffsets = new List<Point>();
            if (element.TryGetProperty("childOffsets", out var offsetsProp))
            {
                foreach (var o in offsetsProp.EnumerateArray())
                {
                    double x = o.TryGetProperty("x", out var xProp) ? xProp.GetDouble() : 0;
                    double y = o.TryGetProperty("y", out var yProp) ? yProp.GetDouble() : 0;
                    tempOffsets.Add(new Point(x, y));
                }
            }

            // 2. Читаем фигуры
            if (element.TryGetProperty("childShapes", out var childrenProp))
            {
                var childrenArray = childrenProp.EnumerateArray().ToList();
                for (int i = 0; i < childrenArray.Count; i++)
                {
                    var childElement = childrenArray[i];
                    if (!childElement.TryGetProperty("type", out var typeProp))
                        continue;

                    string? typeName = typeProp.GetString();
                    if (string.IsNullOrEmpty(typeName)) continue;
                    ShapeBase? child = CreateShapeByType(typeName);
                    if (child != null)
                    {
                        child.LoadFromJson(childElement);

                        // Важно: вызываем PreBuildChild ДО добавления в группу, 
                        // чтобы у ребенка рассчитались MinX/MaxX
                        PreBuildChild(child);

                        // Добавляем в группу
                        AddChildShape(child);

                        // 3. ПРИВЯЗКА ПО ИНДЕКСУ:
                        // Берем i-тое смещение и привязываем его к НОВОМУ Id ребенка
                        if (i < tempOffsets.Count)
                        {
                            ChildAnchorOffsets[child.Id] = tempOffsets[i];
                        }
                    }
                }
            }

            RecalculateBounds();
        }
        /// <summary>
        /// Предварительно строит фигуру в (0,0) чтобы вычислились MinX/MinY/MaxX/MaxY.
        /// </summary>
        private static void PreBuildChild(ShapeBase child)
        {
            if (child is ICompoundShape compound)
            {
                // Рекурсивно для вложенных групп
                foreach (var grandChild in compound.ChildShapes)
                    PreBuildChild(grandChild);
                compound.RecalculateBounds();
            }
            else
            {
                // Строим в (0,0) — вычисляются границы, но не добавляем на холст
                var temp = child.Build(0, 0);
            }
        }

        /// <summary>
        /// Фабрика создания фигур по типу (для десериализации)
        /// </summary>
        private static ShapeBase? CreateShapeByType(string typeName)
        {
            return ShapePluginContext.Factory?.TryCreate(typeName);
        }

        #endregion
}