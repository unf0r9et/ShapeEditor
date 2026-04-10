using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ShapeEditor
{
    public class CircleShape : ShapeBase
    {
        public CircleShape()
        {
            SidesCount = 20;
        }
        public override string DisplayNameRu => "Круг";
        protected override Point[] GetDefaultVertices() => new Point[0];

        public override bool IsPointInside(Point localPoint)
        {
            // радиус в немасштабированных координатах = 50 (половина базового размера 100)
            double radius = 50;
            return localPoint.X * localPoint.X + localPoint.Y * localPoint.Y <= radius * radius;
        }

        public override Canvas Build(double anchorWorldX, double anchorWorldY)
        {
            double baseSize = 100;
            double scaledSize = baseSize * Scale;
            double half = scaledSize / 2;

            var canvas = new Canvas
            {
                Width = scaledSize,
                Height = scaledSize
            };

            // Заливка
            var fillEllipse = new Ellipse
            {
                Width = scaledSize,
                Height = scaledSize,
                Fill = Fill,
                Stroke = null,
                Tag = -1 // заливка
            };
            Canvas.SetLeft(fillEllipse, 0);
            Canvas.SetTop(fillEllipse, 0);
            canvas.Children.Add(fillEllipse);

            // Обводка
            var color = SideColors.Count > 0 ? SideColors[0] : Brushes.Black;
            var thickness = SideThickness.Count > 0 ? SideThickness[0] : 3.0;
            var ellipse = new Ellipse
            {
                Width = scaledSize,
                Height = scaledSize,
                Stroke = color,
                StrokeThickness = thickness,
                Fill = Brushes.Transparent,
                StrokeLineJoin = PenLineJoin.Round,
                Tag = 0 // сторона с индексом 0
            };
            Canvas.SetLeft(ellipse, 0);
            Canvas.SetTop(ellipse, 0);
            canvas.Children.Add(ellipse);

            // Точка привязки
            double anchorLocalX = AnchorPoint.X * Scale;
            double anchorLocalY = AnchorPoint.Y * Scale;
            var anchorDot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = Brushes.White,
                Stroke = Brushes.Purple,
                StrokeThickness = 1,
                Tag = "Anchor"
            };
            Canvas.SetLeft(anchorDot, anchorLocalX + half - 5);
            Canvas.SetTop(anchorDot, anchorLocalY + half - 5);
            canvas.Children.Add(anchorDot);

            // Минимальные координаты bounding box (левый верхний угол canvas соответствует (-half, -half))
            MinX = -half;
            MinY = -half;

            // Позиционируем canvas так, чтобы точка привязки попала в anchorWorldX, anchorWorldY
            Canvas.SetLeft(canvas, anchorWorldX - anchorLocalX + MinX);
            Canvas.SetTop(canvas, anchorWorldY - anchorLocalY + MinY);

            return canvas;
        }
    }
}