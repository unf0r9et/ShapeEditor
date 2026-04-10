using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ShapeEditor
{
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

        public override Canvas Build(double anchorWorldX, double anchorWorldY)
        {
            if (ChildShapes.Count == 0)
            {
                MinX = -20; MinY = -20;
                Canvas empty = new Canvas { Width = 40, Height = 40 };
                Canvas.SetLeft(empty, anchorWorldX + MinX * Scale);
                Canvas.SetTop(empty, anchorWorldY + MinY * Scale);
                return empty;
            }

            // 1. Границы детей в локальных координатах фигуры
            double rawMinX = double.MaxValue, rawMaxX = double.MinValue;
            double rawMinY = double.MaxValue, rawMaxY = double.MinValue;

            foreach (var child in ChildShapes)
            {
                rawMinX = Math.Min(rawMinX, child.AnchorPoint.X + child.MinX);
                rawMaxX = Math.Max(rawMaxX, child.AnchorPoint.X + child.MaxX);
                rawMinY = Math.Min(rawMinY, child.AnchorPoint.Y + child.MinY);
                rawMaxY = Math.Max(rawMaxY, child.AnchorPoint.Y + child.MaxY);
            }

            // Сохраняем для хит-тестинга
            MinX = rawMinX; MinY = rawMinY;
            MaxX = rawMaxX; MaxY = rawMaxY;

            double width = (rawMaxX - rawMinX) * Scale;
            double height = (rawMaxY - rawMinY) * Scale;

            var container = new Canvas
            {
                Width = width,
                Height = height,
                Background = null
            };

            // 2. Рисуем детей относительно левого верхнего угла контейнера
            foreach (var child in ChildShapes)
            {
                var childVisual = child.Build(0, 0);
                childVisual.Tag = child;

                // Позиция ребенка: (его мировая позиция в локальной СК фигуры - rawMin) * Scale
                double offsetX = (child.AnchorPoint.X + child.MinX - rawMinX) * Scale;
                double offsetY = (child.AnchorPoint.Y + child.MinY - rawMinY) * Scale;

                Canvas.SetLeft(childVisual, offsetX);
                Canvas.SetTop(childVisual, offsetY);

                bool isEditingThis = MainWindow.IsEditingThisChild(this, child);
                foreach (var sub in childVisual.Children.OfType<Ellipse>().Where(e => e.Tag?.ToString() == "Anchor"))
                    sub.Visibility = isEditingThis ? Visibility.Visible : Visibility.Collapsed;

                container.Children.Add(childVisual);
            }

            // 3. 🔑 Рисуем якорь: его позиция зависит ТОЛЬКО от AnchorPoint
            var anchorDot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = Brushes.White,
                Stroke = Brushes.Purple,
                StrokeThickness = 1,
                Tag = "Anchor",
                IsHitTestVisible = true // Обязательно для Drag
            };

            // Якорь в координатах контейнера: (AnchorPoint - rawMin) * Scale
            Canvas.SetLeft(anchorDot, (AnchorPoint.X - rawMinX) * Scale - 5);
            Canvas.SetTop(anchorDot, (AnchorPoint.Y - rawMinY) * Scale - 5);
            container.Children.Add(anchorDot);

            // 4. 🔑🔑 КЛЮЧЕВОЕ: Позиция контейнера НЕ зависит от AnchorPoint!
            // anchorWorldX/Y — это позиция фигуры, она не должна меняться при перетаскивании якоря
            Canvas.SetLeft(container, anchorWorldX + MinX * Scale);
            Canvas.SetTop(container, anchorWorldY + MinY * Scale);

            return container;
        }
    }
}