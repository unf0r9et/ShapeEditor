using System;
using System.Windows;
using System.Windows.Media;

namespace ShapeEditor
{
    /// <summary>
    /// Представляет один отрезок (сегмент) фигуры с независимыми параметрами.
    /// </summary>
    public class LineSegment
    {
        public string Name { get; set; } = "Сегмент";
        
        // Параметры отрезка
        public double Length { get; set; } = 100;      // длина в локальных координатах
        public Brush Color { get; set; } = Brushes.Black;
        public double Thickness { get; set; } = 3.0;
        
        // Угол между этим отрезком и СЛЕДУЮЩИМ
        public double AngleToNext { get; set; } = 0;    // в градусах (внешний угол)
        public bool AngleLocked { get; set; } = false;
        
        public bool LengthLocked { get; set; } = false;
        
        public LineSegment Clone()
        {
            return new LineSegment
            {
                Name = this.Name,
                Length = this.Length,
                Color = this.Color.Clone(),
                Thickness = this.Thickness,
                AngleToNext = this.AngleToNext,
                AngleLocked = this.AngleLocked,
                LengthLocked = this.LengthLocked
            };
        }
    }
}