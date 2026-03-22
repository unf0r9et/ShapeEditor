using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Linq;

namespace ShapeEditor
{
    /// <summary>
    /// Комплексная фигура, состоящая из нескольких простых фигур
    /// </summary>
    public class CompoundShape : ShapeBase
    {
        public List<ShapeBase> ChildShapes { get; set; } = new();
        
        // Для CompoungShape вершины генерируются из всех детских фигур
        public CompoundShape()
        {
            SidesCount = 0; // Комплексная фигура не имеет собственных сторон
        }

        protected override Point[] GetDefaultVertices() => new Point[0];

        /// <summary>
        /// Добавляет детскую фигуру в комплекс
        /// </summary>
        public void AddChildShape(ShapeBase child)
        {
            if (child == null || child is CompoundShape) return;
            ChildShapes.Add(child);
            UpdateBounds();
        }

        /// <summary>
        /// Удаляет детскую фигуру
        /// </summary>
        public void RemoveChildShape(ShapeBase child)
        {
            ChildShapes.Remove(child);
            UpdateBounds();
        }

        /// <summary>
        /// Обновляет границы на основе всех детских фигур
        /// </summary>
        private void UpdateBounds()
        {
            if (ChildShapes.Count == 0)
            {
                Vertices = new Point[0];
                return;
            }

            // Собираем все вершины из детских фигур
            var allVertices = new List<Point>();
            foreach (var child in ChildShapes)
            {
                if (child.Vertices != null)
                    allVertices.AddRange(child.Vertices);
            }

            if (allVertices.Count == 0)
            {
                Vertices = new Point[0];
            }
            else
            {
                Vertices = allVertices.ToArray();
            }
        }

        /// <summary>
        /// Проверка, находится ли точка внутри ЛЮБОЙ детской фигуры
        /// </summary>
        public override bool IsPointInside(Point localPoint)
        {
            return ChildShapes.Any(child => child.IsPointInside(localPoint));
        }

        /// <summary>
        /// Применяем трансформацию (масштаб, поворот, позицию) ко всем детским фигурам
        /// </summary>
        public override Canvas Build(double anchorWorldX, double anchorWorldY)
        {
            Canvas container = new Canvas { Width = 1, Height = 1 };

            foreach (var child in ChildShapes)
            {
                // Применяем масштаб и поворот комплекса к каждому ребёнку
                double childScale = child.Scale * Scale;
                double childAngle = child.Angle + Angle;

                // Детская точка привязки в мировых координатах комплекса
                // Сначала применяем трансформацию к якорю ребёнка
                double dxChild = child.AnchorPoint.X - AnchorPoint.X;
                double dyChild = child.AnchorPoint.Y - AnchorPoint.Y;

                double angleRad = Angle * Math.PI / 180.0;
                double cos = Math.Cos(angleRad);
                double sin = Math.Sin(angleRad);

                // Поворачиваем локальное смещение якоря ребёнка
                double rotatedDx = dxChild * cos - dyChild * sin;
                double rotatedDy = dxChild * sin + dyChild * cos;

                // Масштабируем
                rotatedDx *= Scale;
                rotatedDy *= Scale;

                // Мировая позиция якоря ребёнка
                double childWorldAnchorX = anchorWorldX + rotatedDx;
                double childWorldAnchorY = anchorWorldY + rotatedDy;

                // Строим детскую фигуру
                var childVisual = child.Build(childWorldAnchorX, childWorldAnchorY);

                // Помечаем дочерний visual, чтобы его можно было найти из MainWindow (Tag = соответствующая ShapeBase)
                childVisual.Tag = child;

                container.Children.Add(childVisual);
            }

            // Обновляем границы контейнера
            if (ChildShapes.Count > 0)
            {
                double minX = double.MaxValue, maxX = double.MinValue;
                double minY = double.MaxValue, maxY = double.MinValue;

                foreach (var child in ChildShapes)
                {
                    minX = Math.Min(minX, child.MinX);
                    maxX = Math.Max(maxX, child.MaxX);
                    minY = Math.Min(minY, child.MinY);
                    maxY = Math.Max(maxY, child.MaxY);
                }

                double width = maxX - minX;
                double height = maxY - minY;

                container.Width = width > 0 ? width : 1;
                container.Height = height > 0 ? height : 1;

                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;

                Canvas.SetLeft(container, anchorWorldX - MinX);
                Canvas.SetTop(container, anchorWorldY - MinY);
            }

            return container;
        }
    }
}