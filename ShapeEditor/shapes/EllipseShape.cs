using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Text.Json;

namespace ShapeEditor
{
    /// <summary>
    /// Эллипс и окружность.
    /// _axisX — полная ось по X (горизонтальная)
    /// _axisY — полная ось по Y (вертикальная)
    /// Большая ось определяется автоматически: max(_axisX, _axisY)
    /// Фокусы всегда на большей оси.
    /// </summary>
    public class EllipseShape : ShapeBase
    {
        public int ApproximationPoints { get; set; } = 60;

        // === ВНУТРЕННИЕ ПОЛНЫЕ ОСИ ===
        // _axisX — полная ось по X (горизонтальная)
        // _axisY — полная ось по Y (вертикальная)
        private double _axisX = 100;   // начальная горизонтальная
        private double _axisY = 60;    // начальная вертикальная
        private double _focalDistance = 40; // c = sqrt(|(axisX/2)² - (axisY/2)²|)

        // === ПУБЛИЧНЫЕ СВОЙСТВА ДЛЯ UI ===

        /// <summary>
        /// Полная ось по X (горизонтальная).
        /// При изменении: пересчитывается c, определяется ориентация фокусов.
        /// </summary>
        public double MajorAxis
        {
            get => _axisX;
            set
            {
                double newAxisX = Math.Max(value, 2);

                if (IsCircle)
                {
                    _axisX = _axisY = newAxisX;
                    _focalDistance = 0;
                    FociOnYAxis = false;
                    return;
                }

                _axisX = newAxisX;
                RecalculateFocalDistanceFromAxes();
            }
        }

        /// <summary>
        /// Полная ось по Y (вертикальная).
        /// При изменении: пересчитывается c, определяется ориентация фокусов.
        /// Если axisY > axisX — фокусы переходят на вертикальную ось.
        /// </summary>
        public double MinorAxis
        {
            get => _axisY;
            set
            {
                double newAxisY = Math.Max(value, 2);

                if (IsCircle) return; // Круг меняется только через MajorAxis

                _axisY = newAxisY;
                RecalculateFocalDistanceFromAxes();
            }
        }

        /// <summary>
        /// Фокусное расстояние c.
        /// При установке: пересчитывается МЕНЬШАЯ ось (та, которая сейчас меньше).
        /// </summary>
        public double FocalDistance
        {
            get => _focalDistance;
            set
            {
                if (IsCircle)
                {
                    _focalDistance = 0;
                    return;
                }

                double semiX = _axisX / 2.0;
                double semiY = _axisY / 2.0;
                double semiMajor = Math.Max(semiX, semiY);
                double newC = Math.Max(0, value);

                // c не может быть >= большой полуоси
                if (newC >= semiMajor)
                    newC = semiMajor - 0.1;

                _focalDistance = newC;

                // Пересчитываем МЕНЬШУЮ полуось: b = sqrt(a² - c²)
                double newSemiMinor = Math.Sqrt(Math.Max(0, semiMajor * semiMajor - newC * newC));

                // Меняем ту ось, которая СЕЙЧАС меньше
                if (semiX <= semiY)
                    _axisX = newSemiMinor * 2;  // X была меньше → меняем X
                else
                    _axisY = newSemiMinor * 2;  // Y была меньше → меняем Y

                // Ориентация определяется автоматически
                FociOnYAxis = _axisY > _axisX;
            }
        }

        /// <summary>
        /// Смещение фокуса от центра (то же что и FocalDistance)
        /// </summary>
        public double FocusOffset
        {
            get => _focalDistance;
            set => _focalDistance = value;
        }

        /// <summary>
        /// true = фокусы на оси Y (вертикальная ось больше), false = на оси X (горизонтальная больше).
        /// Определяется автоматически: FociOnYAxis = axisY > axisX.
        /// </summary>
        public bool FociOnYAxis { get; private set; } = false;

        public bool IsCircle { get; set; } = false;

        public EllipseShape()
        {
            SidesCount = 1;
            RecalculateFocalDistanceFromAxes();
        }

        public override string DisplayNameRu => IsCircle ? "Круг" : "Эллипс";

        /// <summary>
        /// Пересчитывает c из текущих осей: c = sqrt(|(axisX/2)² - (axisY/2)²|)
        /// </summary>
        private void RecalculateFocalDistanceFromAxes()
        {
            double semiX = _axisX / 2.0;
            double semiY = _axisY / 2.0;

            if (IsCircle || Math.Abs(semiX - semiY) < 1e-6)
            {
                _focalDistance = 0;
                FociOnYAxis = false;
                if (IsCircle) _axisX = _axisY = Math.Max(_axisX, _axisY);
                return;
            }

            _focalDistance = Math.Sqrt(Math.Abs(semiX * semiX - semiY * semiY));
            FociOnYAxis = semiY > semiX;  // вертикальная больше → фокусы на Y
        }

        /// <summary>
        /// Обновление после загрузки JSON — синхронизация
        /// </summary>
        public void UpdateFociFromParameters()
        {
            if (IsCircle)
            {
                _axisX = _axisY = Math.Max(_axisX, _axisY);
                _focalDistance = 0;
                FociOnYAxis = false;
                return;
            }

            double semiX = _axisX / 2.0;
            double semiY = _axisY / 2.0;
            double geometricC = Math.Sqrt(Math.Abs(semiX * semiX - semiY * semiY));

            if (Math.Abs(_focalDistance - geometricC) > 1e-6)
                _focalDistance = geometricC;

            FociOnYAxis = semiY > semiX;
        }

        /// <summary>
        /// Вычисляет глобальные координаты фокусов.
        /// Фокусы на оси с БОЛЬШЕЙ полуосью.
        /// </summary>
        public (Point f1, Point f2) GetGlobalFocusPositions(double centerWorldX, double centerWorldY)
        {
            if (IsCircle)
                return (new Point(centerWorldX, centerWorldY), new Point(centerWorldX, centerWorldY));

            double angleRad = Angle * Math.PI / 180.0;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);

            double c = _focalDistance * Scale;

            double f1lx, f1ly, f2lx, f2ly;

            if (FociOnYAxis)
            {
                // Большая ось вертикальная — фокусы на Y
                f1lx = 0; f1ly = -c;
                f2lx = 0; f2ly = c;
            }
            else
            {
                // Большая ось горизонтальная — фокусы на X
                f1lx = -c; f1ly = 0;
                f2lx = c; f2ly = 0;
            }

            double f1rx = f1lx * cos - f1ly * sin;
            double f1ry = f1lx * sin + f1ly * cos;
            double f2rx = f2lx * cos - f2ly * sin;
            double f2ry = f2lx * sin + f2ly * cos;

            return (
                new Point(centerWorldX + f1rx, centerWorldY + f1ry),
                new Point(centerWorldX + f2rx, centerWorldY + f2ry)
            );
        }

        protected override Point[] GetDefaultVertices()
        {
            return GenerateEllipseVertices();
        }

        private Point[] GenerateEllipseVertices()
        {
            var points = new Point[ApproximationPoints];
            double semiX = _axisX / 2.0;
            double semiY = _axisY / 2.0;
            for (int i = 0; i < ApproximationPoints; i++)
            {
                double angle = 2 * Math.PI * i / ApproximationPoints;
                points[i] = new Point(semiX * Math.Cos(angle), semiY * Math.Sin(angle));
            }
            return points;
        }

        public override bool IsPointInside(Point localPoint)
        {
            double semiX = _axisX / 2.0;
            double semiY = _axisY / 2.0;
            if (semiX < 1e-6 || semiY < 1e-6) return false;
            double nx = localPoint.X / semiX;
            double ny = localPoint.Y / semiY;
            return nx * nx + ny * ny <= 1.0;
        }

        public override Canvas Build(double anchorWorldX, double anchorWorldY)
        {
            // Полные оси для отрисовки
            double width = _axisX * Scale;
            double height = _axisY * Scale;

            var canvas = new Canvas { Width = width, Height = height };

            double angleRad = Angle * Math.PI / 180.0;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);

            // === Заливка ===
            var fillEllipse = new Ellipse
            {
                Width = width,
                Height = height,
                Fill = Fill,
                Stroke = null,
                Tag = -1
            };
            fillEllipse.RenderTransform = new RotateTransform(Angle, width / 2, height / 2);
            Canvas.SetLeft(fillEllipse, 0);
            Canvas.SetTop(fillEllipse, 0);
            canvas.Children.Add(fillEllipse);

            // === Обводка ===
            var color = SideColors.Count > 0 ? SideColors[0] : Brushes.Black;
            var thickness = SideThickness.Count > 0 ? SideThickness[0] : 3.0;
            var strokeEllipse = new Ellipse
            {
                Width = width,
                Height = height,
                Stroke = color,
                StrokeThickness = thickness,
                Fill = Brushes.Transparent,
                StrokeLineJoin = PenLineJoin.Round,
                Tag = 0
            };
            strokeEllipse.RenderTransform = new RotateTransform(Angle, width / 2, height / 2);
            Canvas.SetLeft(strokeEllipse, 0);
            Canvas.SetTop(strokeEllipse, 0);
            canvas.Children.Add(strokeEllipse);

            // === Фокусы ===
            if (!IsCircle)
            {
                double c = _focalDistance * Scale;
                double cx = width / 2;
                double cy = height / 2;

                // Фокусы на оси с БОЛЬШЕЙ полуосью
                double f1lx, f1ly, f2lx, f2ly;

                if (FociOnYAxis)
                {
                    // Большая ось вертикальная — фокусы на Y
                    f1lx = 0; f1ly = -c;
                    f2lx = 0; f2ly = c;
                }
                else
                {
                    // Большая ось горизонтальная — фокусы на X
                    f1lx = -c; f1ly = 0;
                    f2lx = c; f2ly = 0;
                }

                // Поворачиваем вокруг центра
                double f1x = f1lx * cos - f1ly * sin + cx - 6;
                double f1y = f1lx * sin + f1ly * cos + cy - 6;

                var f1 = new Ellipse
                {
                    Width = 12,
                    Height = 12,
                    Fill = Brushes.Yellow,
                    Stroke = Brushes.OrangeRed,
                    StrokeThickness = 2,
                    Tag = "Focus1",
                    ToolTip = "Фокус 1",
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(f1, f1x);
                Canvas.SetTop(f1, f1y);
                canvas.Children.Add(f1);

                double f2x = f2lx * cos - f2ly * sin + cx - 6;
                double f2y = f2lx * sin + f2ly * cos + cy - 6;

                var f2 = new Ellipse
                {
                    Width = 12,
                    Height = 12,
                    Fill = Brushes.Yellow,
                    Stroke = Brushes.OrangeRed,
                    StrokeThickness = 2,
                    Tag = "Focus2",
                    ToolTip = "Фокус 2",
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(f2, f2x);
                Canvas.SetTop(f2, f2y);
                canvas.Children.Add(f2);
            }

            // === Точка привязки (центр) ===
            double ax = AnchorPoint.X * Scale;
            double ay = AnchorPoint.Y * Scale;
            var anchorDot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = Brushes.White,
                Stroke = Brushes.Purple,
                StrokeThickness = 1,
                Tag = "Anchor"
            };
            Canvas.SetLeft(anchorDot, ax + width / 2 - 5);
            Canvas.SetTop(anchorDot, ay + height / 2 - 5);
            canvas.Children.Add(anchorDot);

            // Границы
            MinX = -width / 2; MinY = -height / 2;
            MaxX = width / 2; MaxY = height / 2;

            // Позиционирование
            Canvas.SetLeft(canvas, anchorWorldX - ax + MinX);
            Canvas.SetTop(canvas, anchorWorldY - ay + MinY);

            return canvas;
        }

        public override void SetEdgeLength(int edgeIndex, double newLength)
        {
            if (edgeIndex == 0) MajorAxis = newLength;
        }

        public override double GetEdgeLength(int edgeIndex)
        {
            if (edgeIndex == 0) return _axisX;
            return 0;
        }

        #region JSON

        public override void SaveToJson(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "EllipseShape");
            writer.WriteNumber("id", Id);
            writer.WriteString("displayName", DisplayNameRu);
            writer.WriteNumber("scale", Scale);
            writer.WriteNumber("angle", Angle);
            writer.WriteNumber("anchorX", AnchorPoint.X);
            writer.WriteNumber("anchorY", AnchorPoint.Y);
            writer.WriteString("fill", GetColorHex(Fill));

            // Сохраняем ВНУТРЕННИЕ оси (не "большая/малая", а X/Y!)
            writer.WriteNumber("axisX", _axisX);
            writer.WriteNumber("axisY", _axisY);
            writer.WriteNumber("focalDistance", _focalDistance);
            writer.WriteBoolean("fociOnYAxis", FociOnYAxis);
            writer.WriteBoolean("isCircle", IsCircle);
            writer.WriteNumber("approximationPoints", ApproximationPoints);

            writer.WritePropertyName("sideColors");
            writer.WriteStartArray();
            foreach (var c in SideColors) writer.WriteStringValue(GetColorHex(c));
            writer.WriteEndArray();

            writer.WritePropertyName("sideThicknesses");
            writer.WriteStartArray();
            foreach (var t in SideThickness) writer.WriteNumberValue(t);
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        public override void LoadFromJson(JsonElement element)
        {
            if (element.TryGetProperty("id", out var idProp)) Id = idProp.GetInt32();
            if (element.TryGetProperty("scale", out var sProp)) Scale = sProp.GetDouble();
            if (element.TryGetProperty("angle", out var aProp)) Angle = aProp.GetDouble();
            if (element.TryGetProperty("anchorX", out var axProp) && element.TryGetProperty("anchorY", out var ayProp))
                AnchorPoint = new Point(axProp.GetDouble(), ayProp.GetDouble());
            if (element.TryGetProperty("fill", out var fProp)) Fill = ParseColor(fProp.GetString());

            // Загружаем ВНУТРЕННИЕ оси
            if (element.TryGetProperty("axisX", out var aXProp)) _axisX = aXProp.GetDouble();
            if (element.TryGetProperty("axisY", out var aYProp)) _axisY = aYProp.GetDouble();
            if (element.TryGetProperty("focalDistance", out var fdProp)) _focalDistance = fdProp.GetDouble();
            if (element.TryGetProperty("fociOnYAxis", out var foyProp)) FociOnYAxis = foyProp.GetBoolean();
            if (element.TryGetProperty("isCircle", out var icProp)) IsCircle = icProp.GetBoolean();
            if (element.TryGetProperty("approximationPoints", out var apProp)) ApproximationPoints = apProp.GetInt32();

            SideColors.Clear();
            if (element.TryGetProperty("sideColors", out var colorsProp))
                foreach (var c in colorsProp.EnumerateArray()) SideColors.Add(ParseColor(c.GetString()));

            SideThickness.Clear();
            if (element.TryGetProperty("sideThicknesses", out var thickProp))
                foreach (var t in thickProp.EnumerateArray()) SideThickness.Add(t.GetDouble());

            UpdateFociFromParameters();
        }

        #endregion
    }
}