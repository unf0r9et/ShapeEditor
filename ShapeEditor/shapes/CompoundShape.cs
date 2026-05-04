using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Text.Json;

namespace ShapeEditor
{
    /// <summary>
    /// Составная фигура (группа). Содержит дочерние фигуры любого типа.
    /// </summary>
    public class CompoundShape : ShapeBase
    {
        public List<ShapeBase> ChildShapes { get; set; } = new();

        public CompoundShape()
        {
            SidesCount = 0;
            Fill = Brushes.Transparent;
        }

        public override string DisplayNameRu => "Группа";
        protected override Point[] GetDefaultVertices() => new Point[0];

        #region Управление детьми

        public void AddChildShape(ShapeBase child)
        {
            if (child == null || child is CompoundShape || ChildShapes.Contains(child))
                return;
            ChildShapes.Add(child);
        }

        public void RemoveChildShape(ShapeBase child)
        {
            ChildShapes.Remove(child);
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
                if (child is CompoundShape childCompound)
                    childCompound.RecalculateBounds();

                rawMinX = Math.Min(rawMinX, child.AnchorPoint.X + child.MinX);
                rawMaxX = Math.Max(rawMaxX, child.AnchorPoint.X + child.MaxX);
                rawMinY = Math.Min(rawMinY, child.AnchorPoint.Y + child.MinY);
                rawMaxY = Math.Max(rawMaxY, child.AnchorPoint.Y + child.MaxY);
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

                double offsetX = (child.AnchorPoint.X + child.MinX - MinX) * Scale;
                double offsetY = (child.AnchorPoint.Y + child.MinY - MinY) * Scale;

                Canvas.SetLeft(childVisual, offsetX);
                Canvas.SetTop(childVisual, offsetY);

                // Управление видимостью якорей детей
                bool isEditingThis = MainWindow.IsEditingThisChild(this, child);
                bool isHighlightedFromTree = MainWindow.IsHighlightedChild(this, child);

                foreach (var sub in childVisual.Children.OfType<Ellipse>().Where(e => e.Tag?.ToString() == "Anchor"))
                {
                    sub.Visibility = (isEditingThis || isHighlightedFromTree) ? Visibility.Visible : Visibility.Collapsed;
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
                IsHitTestVisible = true
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
            writer.WriteString("displayName", DisplayNameRu);
            writer.WriteNumber("scale", Scale);
            writer.WriteNumber("angle", Angle);
            writer.WriteNumber("anchorX", AnchorPoint.X);
            writer.WriteNumber("anchorY", AnchorPoint.Y);
            writer.WriteString("fill", GetColorHex(Fill));

            writer.WritePropertyName("childShapes");
            writer.WriteStartArray();
            foreach (var child in ChildShapes)
            {
                // Рекурсивно сохраняем каждого ребёнка
                child.SaveToJson(writer);
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

public override void LoadFromJson(JsonElement element)
        {
            //if (element.TryGetProperty("id", out var idProp)) Id = idProp.GetInt32();
            if (element.TryGetProperty("scale", out var sProp)) Scale = sProp.GetDouble();
            if (element.TryGetProperty("angle", out var aProp)) Angle = aProp.GetDouble();
            if (element.TryGetProperty("anchorX", out var axProp) && element.TryGetProperty("anchorY", out var ayProp))
                AnchorPoint = new Point(axProp.GetDouble(), ayProp.GetDouble());
            if (element.TryGetProperty("fill", out var fProp)) Fill = ParseColor(fProp.GetString());

            ChildShapes.Clear();
            if (element.TryGetProperty("childShapes", out var childrenProp))
            {
                foreach (var childElement in childrenProp.EnumerateArray())
                {
                    if (!childElement.TryGetProperty("type", out var typeProp))
                        continue;

                    string typeName = typeProp.GetString();
                    ShapeBase child = CreateShapeByType(typeName);
                    if (child != null)
                    {
                        child.LoadFromJson(childElement);
                        // Пересчитываем границы ребёнка (важно для корректной группировки)
                        PreBuildChild(child);
                        ChildShapes.Add(child);
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
            if (child is CompoundShape compound)
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
        private static ShapeBase CreateShapeByType(string typeName)
        {
            return typeName switch
            {
                "PolygonShape" => new PolygonShape(),
                "EllipseShape" => new EllipseShape(),
                "CustomShape" => new CustomShape(),
                "CompoundShape" => new CompoundShape(),
                _ => null
            };
        }

        #endregion
    }
}