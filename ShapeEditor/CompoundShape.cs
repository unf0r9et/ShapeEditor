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
            SidesCount = 0;
            Fill = Brushes.Transparent; // По умолчанию прозрачный
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
        public void UpdateBounds()
        {
            if (ChildShapes.Count == 0)
            {
                MinX = -20; MaxX = 20; MinY = -20; MaxY = 20;
                return;
            }

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            foreach (var child in ChildShapes)
            {
                // У каждой фигуры есть свои MinX/MinY после Build
                // Но нам нужны координаты относительно якоря ГРУППЫ
                minX = Math.Min(minX, child.AnchorPoint.X + child.MinX);
                maxX = Math.Max(maxX, child.AnchorPoint.X + child.MaxX);
                minY = Math.Min(minY, child.AnchorPoint.Y + child.MinY);
                maxY = Math.Max(maxY, child.AnchorPoint.Y + child.MaxY);
            }

            MinX = minX - AnchorPoint.X;
            MaxX = maxX - AnchorPoint.X;
            MinY = minY - AnchorPoint.Y;
            MaxY = maxY - AnchorPoint.Y;
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
            // Сначала считаем границы всех детей
            UpdateBounds();

            // Создаем контейнер. ВАЖНО: он сам по себе не имеет размера для клика, 
            // поэтому мы добавим прозрачную подложку в методе MainWindow
            Canvas container = new Canvas { IsHitTestVisible = true };

            foreach (var child in ChildShapes)
            {
                // Масштабируем и поворачиваем позицию ребенка относительно якоря группы
                double angleRad = Angle * Math.PI / 180.0;
                double cos = Math.Cos(angleRad);
                double sin = Math.Sin(angleRad);

                // Вектор от якоря группы до якоря ребенка
                double dx = child.AnchorPoint.X - AnchorPoint.X;
                double dy = child.AnchorPoint.Y - AnchorPoint.Y;

                // Поворот и масштаб вектора
                double rx = (dx * cos - dy * sin) * Scale;
                double ry = (dx * sin + dy * cos) * Scale;

                // Мировая позиция якоря ребенка
                double childWorldX = anchorWorldX + rx;
                double childWorldY = anchorWorldY + ry;

                // Сохраняем масштаб и угол группы в ребенка (визуально)
                double originalScale = child.Scale;
                double originalAngle = child.Angle;

                child.Scale *= Scale;
                child.Angle += Angle;

                var childVisual = child.Build(childWorldX, childWorldY);
                childVisual.Tag = child;
                container.Children.Add(childVisual);

                // Возвращаем настройки ребенка назад, чтобы не испортить модель
                child.Scale = originalScale;
                child.Angle = originalAngle;
            }

            return container;
        }
    }
}