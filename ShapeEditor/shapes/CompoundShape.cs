using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ShapeEditor.shapes
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
                // Рекурсивно пересчитываем для вложенных групп
                if (child is CompoundShape childCompound)
                {
                    childCompound.RecalculateBounds();
                }

                rawMinX = Math.Min(rawMinX, child.AnchorPoint.X + child.MinX);
                rawMaxX = Math.Max(rawMaxX, child.AnchorPoint.X + child.MaxX);
                rawMinY = Math.Min(rawMinY, child.AnchorPoint.Y + child.MinY);
                rawMaxY = Math.Max(rawMaxY, child.AnchorPoint.Y + child.MaxY);
            }

            MinX = rawMinX;
            MinY = rawMinY;
            MaxX = rawMaxX;
            MaxY = rawMaxY;
        }

        /// <summary>
        /// Сохраняет составную фигуру
        /// </summary>
        public override void Save(BinaryWriter writer)
        {
            base.Save(writer);

            // Не сохраняем границы - пересчитаем при загрузке
            writer.Write(ChildShapes.Count);
            foreach (var child in ChildShapes)
            {
                child.Save(writer);
            }
        }

        /// <summary>
        /// Загружает составную фигуру
        /// </summary>
        public override void Load(BinaryReader reader)
        {
            base.Load(reader);

            ChildShapes.Clear();
            int childCount = reader.ReadInt32();

            for (int i = 0; i < childCount; i++)
            {
                int typeNameLength = reader.ReadInt32();
                string typeName = new string(reader.ReadChars(typeNameLength));

                ShapeBase child = CreateShapeByType(typeName);
                if (child != null)
                {
                    child.Load(reader);
                    ChildShapes.Add(child);
                }
                else
                {
                    SkipShapeData(reader);
                }
            }


        }
        private ShapeBase CreateShapeByType(string typeName)
        {
            return typeName switch
            {
                "RectangleShape" => new RectangleShape(),
                "TriangleShape" => new TriangleShape(),
                "TrapezoidShape" => new TrapezoidShape(),
                "CircleShape" => new CircleShape(),
                "HexagonShape" => new HexagonShape(),
                "CustomShape" => new CustomShape(),
                "CompoundShape" => new CompoundShape(),
                _ => null
            };
        }

        private void SkipShapeData(BinaryReader reader)
        {
            reader.ReadInt32(); reader.ReadDouble(); reader.ReadDouble();
            reader.ReadDouble(); reader.ReadDouble();
            reader.ReadByte(); reader.ReadByte(); reader.ReadByte(); reader.ReadByte();
            int colorCount = reader.ReadInt32();
            for (int i = 0; i < colorCount; i++)
            {
                reader.ReadByte(); reader.ReadByte(); reader.ReadByte(); reader.ReadByte();
            }
            int thickCount = reader.ReadInt32();
            for (int i = 0; i < thickCount; i++) reader.ReadDouble();
            int lockCount = reader.ReadInt32();
            for (int i = 0; i < lockCount; i++) reader.ReadBoolean();
            int vertCount = reader.ReadInt32();
            for (int i = 0; i < vertCount; i++)
            {
                reader.ReadDouble(); reader.ReadDouble();
            }
            reader.ReadInt32();

            // Для составной фигуры пропускаем детей
            int childCount = reader.ReadInt32();
            for (int i = 0; i < childCount; i++)
            {
                int childTypeLen = reader.ReadInt32();
                reader.ReadChars(childTypeLen);
                SkipShapeData(reader);
            }
        }

        public override Canvas Build(double anchorWorldX, double anchorWorldY)
        {
            // Всегда пересчитываем границы перед построением
            RecalculateBounds();

            if (ChildShapes.Count == 0)
            {
                MinX = -20; MinY = -20;
                MaxX = 20; MaxY = 20;
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

            // Рисуем детей относительно левого верхнего угла контейнера
            foreach (var child in ChildShapes)
            {
                var childVisual = child.Build(0, 0);
                childVisual.Tag = child;

                // Позиция ребенка в локальных координатах контейнера
                double offsetX = (child.AnchorPoint.X + child.MinX - MinX) * Scale;
                double offsetY = (child.AnchorPoint.Y + child.MinY - MinY) * Scale;

                Canvas.SetLeft(childVisual, offsetX);
                Canvas.SetTop(childVisual, offsetY);
                // В CompoundShape.Build() при создании детей:
                bool isEditingThis = MainWindow.IsEditingThisChild(this, child);
                bool isHighlightedFromTree = MainWindow.IsHighlightedChild(this, child); 

                foreach (var sub in childVisual.Children.OfType<Ellipse>().Where(e => e.Tag?.ToString() == "Anchor"))
                {
                    sub.Visibility = isEditingThis || isHighlightedFromTree ? Visibility.Visible : Visibility.Collapsed;
                }
                foreach (var sub in childVisual.Children.OfType<Ellipse>().Where(e => e.Tag?.ToString() == "Anchor"))
                    sub.Visibility = isEditingThis ? Visibility.Visible : Visibility.Collapsed;

                container.Children.Add(childVisual);
            }

            // Рисуем якорь
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

            // Позиция контейнера: якорь должен оказаться в (anchorWorldX, anchorWorldY)
            double containerLeft = anchorWorldX - (AnchorPoint.X - MinX) * Scale;
            double containerTop = anchorWorldY - (AnchorPoint.Y - MinY) * Scale;

            Canvas.SetLeft(container, containerLeft);
            Canvas.SetTop(container, containerTop);

            return container;
        }
    }
}