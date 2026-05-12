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
    public class EllipseShape : ShapeBase, IEllipseShape
    {
        public int ApproximationPoints { get; set; } = 60;
        public override string DisplayNameEn => IsCircle ? "Circle" : "Ellipse";
        // === ВНУТРЕННИЕ ПОЛНЫЕ ОСИ ===
        // _axisX — полная ось по X (горизонтальная)
        // _axisY — полная ось по Y (вертикальная)
        private double _axisX = 120;   // начальная горизонтальная
        private double _axisY = 89.4;    // начальная вертикальная
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
            bool shouldShowHelpers = ShapeCompositionHost.ShouldShowEllipseBuildHelpers?.Invoke(this) ?? true;

            // 2. Геометрия
            double width = _axisX * Scale;
            double height = _axisY * Scale;
            double angleRad = Angle * Math.PI / 180.0;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);

            double a = width / 2;
            double b = height / 2;

            // Смещение якоря относительно центра (в масштабе)
            double ax = AnchorPoint.X * Scale;
            double ay = AnchorPoint.Y * Scale;

            // --- МАТЕМАТИКА ОРБИТАЛЬНОГО ВРАЩЕНИЯ ---
            // Вектор от якоря к центру фигуры в не повернутом состоянии: (-ax, -ay)
            // Поворачиваем этот вектор на текущий угол, чтобы найти положение центра относительно якоря
            double rotatedVecX = -ax * cos + ay * sin;
            double rotatedVecY = -ax * sin - ay * cos;

            // Расчет Bounding Box самого эллипса (вокруг его собственного центра)
            double halfW = Math.Sqrt(a * a * cos * cos + b * b * sin * sin);
            double halfH = Math.Sqrt(a * a * sin * sin + b * b * cos * cos);

            // Создаем Canvas строго под размер повернутого эллипса
            var canvas = new Canvas { Width = halfW * 2, Height = halfH * 2, Tag = this };
            double cx = canvas.Width / 2;
            double cy = canvas.Height / 2;

            // Отрисовка эллипса (вращается вокруг СВОЕГО центра внутри Canvas)
            var rotateTransform = new RotateTransform(Angle, width / 2, height / 2);

            void SetupShape(Shape shape)
            {
                shape.Width = width;
                shape.Height = height;
                shape.RenderTransform = rotateTransform;
                Canvas.SetLeft(shape, cx - width / 2);
                Canvas.SetTop(shape, cy - height / 2);
                canvas.Children.Add(shape);
            }

            SetupShape(new Ellipse { Fill = Fill });
            SetupShape(new Ellipse
            {
                Stroke = SideColors.Count > 0 ? SideColors[0] : Brushes.Black,
                StrokeThickness = SideThickness.Count > 0 ? SideThickness[0] : 3.0,
                Tag = 0
            });

            // 3. Вспомогательные точки
            if (shouldShowHelpers)
            {
                // Фокусы (вращаются вокруг центра эллипса)
                if (!IsCircle)
                {
                    double c = _focalDistance * Scale;
                    double f1lx = FociOnYAxis ? 0 : -c;
                    double f1ly = FociOnYAxis ? -c : 0;
                    double f2lx = FociOnYAxis ? 0 : c;
                    double f2ly = FociOnYAxis ? c : 0;

                    void DrawFocus(double fx, double fy, string tag)
                    {
                        // Поворот фокусов относительно центра эллипса
                        double rfx = fx * cos - fy * sin;
                        double rfy = fx * sin + fy * cos;
                        var f = new Ellipse { Width = 12, Height = 12, Fill = Brushes.Yellow, Stroke = Brushes.OrangeRed, StrokeThickness = 2, IsHitTestVisible = false,
                            Tag = tag,
                            Visibility = Visibility.Collapsed
                        };
                        Canvas.SetLeft(f, cx + rfx - 6);
                        Canvas.SetTop(f, cy + rfy - 6);
                        canvas.Children.Add(f);

                    }
                    DrawFocus(f1lx, f1ly, "Focus1");
                    DrawFocus(f2lx, f2ly, "Focus2");
                }

                // Якорь (фиолетовая точка)
                // Его положение в Canvas: центр эллипса + повернутый вектор (ax, ay)
                double curAnchorLocalX = ax * cos - ay * sin;
                double curAnchorLocalY = ax * sin + ay * cos;
                var anchorDot = new Ellipse { Width = 10, Height = 10, Fill = Brushes.White, Stroke = Brushes.Purple, StrokeThickness = 1, Tag = "Anchor" };
                Canvas.SetLeft(anchorDot, cx + curAnchorLocalX - 5);
                Canvas.SetTop(anchorDot, cy + curAnchorLocalY - 5);
                canvas.Children.Add(anchorDot);
            }

            // --- 4. ФИНАЛЬНОЕ ПОЗИЦИОНИРОВАНИЕ ---
            // Чтобы эллипс вращался вокруг якоря, нам нужно, чтобы при изменении Angle 
            // MinX и MinY менялись так, будто центр Canvas движется по орбите.

            // Новое значение MinX/MinY для формулы (anchorWorldX - ax + MinX)
            MinX = rotatedVecX + ax - halfW;
            MaxX = rotatedVecX + ax + halfW;
            MinY = rotatedVecY + ay - halfH;
            MaxY = rotatedVecY + ay + halfH;

            // Применяем вашу формулу из изначального кода
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
            writer.WriteString("displayName", DisplayNameEn);
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
            //if (element.TryGetProperty("id", out var idProp)) Id = idProp.GetInt32();
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

            // При наличии focusWorld (смещения фокусов от якоря в мировых осях при сохранении) поле не используется —
            // восстановление только из focalDistance, осей, угла, якоря и scale.

            UpdateFociFromParameters();
        }

        #endregion

        // EllipseShape.cs

        /// <summary>
        /// Обновляет эллипс по новым мировым координатам фокусов.
        /// Якорь всегда сбрасывается в центр (0,0).
        /// Возвращает новую мировую позицию центра.
        /// </summary>
        // EllipseShape.cs

        public Point UpdateFromWorldFociFixedCenter(Point f1W, Point f2W)
        {
            // Якорь всегда в центре
            AnchorPoint = new Point(0, 0);

            // Новый центр в мире — строго между новыми точками фокусов
            Point newCenterW = new Point((f1W.X + f2W.X) / 2.0, (f1W.Y + f2W.Y) / 2.0);

            // Расстояние между фокусами
            double dx = f2W.X - f1W.X;
            double dy = f2W.Y - f1W.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            // Новое локальное фокусное расстояние c
            double newC = (dist / 2.0) / Scale;

            // Если фокусы разнесли дальше текущей большой оси — расширяем её
            if (newC >= MajorAxis / 2.0)
                MajorAxis = (newC + 5.0) * 2.0;

            // Apply through property so minor axis is recalculated from the entered foci.
            // This preserves the exact focus pair instead of snapping c back from old axes.
            FocalDistance = newC;

            // Угол поворота линии фокусов
            double newAngleRad = Math.Atan2(dy, dx);
            double newAngleDeg = newAngleRad * 180.0 / Math.PI;

            // For vertical major axis, convert world F1->F2 direction to local Y-axis reference.
            // Using +90 flips focus identity (F1/F2) when editing Y values; -90 preserves it.
            if (FociOnYAxis) newAngleDeg -= 90;

            // Keep geometric angle consistent with world coordinates from Atan2.
            // Negative sign mirrors the entered foci line.
            Angle = newAngleDeg;

            return newCenterW;
        }
        public void UpdateFromWorldFoci(Point f1W, Point f2W, Point anchorW)
        {
            // 1. Новый центр в мире — середина между фокусами
            Point centerW = new Point((f1W.X + f2W.X) / 2.0, (f1W.Y + f2W.Y) / 2.0);

            // 2. Новое фокусное расстояние (дистанция между фокусами пополам)
            double dx = f2W.X - f1W.X;
            double dy = f2W.Y - f1W.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            double newC = (dist / 2.0) / Scale;

            // Автоматически расширяем большую ось, если фокусы разнесли слишком далеко
            if (newC >= MajorAxis / 2.0)
                MajorAxis = (newC + 5.0) * 2.0;

            // Apply through property so minor axis is recalculated from the entered foci.
            FocalDistance = newC;

            // 3. Новый угол наклона
            // Atan2 дает угол линии F1->F2. В нашей модели 0 градусов — это горизонталь.
            double newAngleRad = Math.Atan2(dy, dx);
            double newAngleDeg = newAngleRad * 180.0 / Math.PI;

            // Если в модели фокусы на Y, корректируем на 90 градусов
            // Same conversion as in UpdateFromWorldFociFixedCenter: keep focus ordering stable.
            if (FociOnYAxis) newAngleDeg -= 90;

            // Keep geometric angle consistent with world coordinates from Atan2.
            Angle = newAngleDeg;

            // 4. Пересчет локального AnchorPoint
            // Нам нужно, чтобы старый мировой якорь (anchorW) имел те же координаты в мире,
            // но теперь эллипс повернут на новый Angle и стоит в новом centerW.

            // Вектор от нового центра к мировому якорю
            double vax = anchorW.X - centerW.X;
            double vay = anchorW.Y - centerW.Y;

            // Разворачиваем этот вектор обратно на новый Angle, чтобы получить локальные оси
            double rad = Angle * Math.PI / 180.0;
            double invCos = Math.Cos(-rad);
            double invSin = Math.Sin(-rad);

            // Локальный якорь (относительно центра)
            AnchorPoint = new Point(
                (vax * invCos - vay * invSin) / Scale,
                (vax * invSin + vay * invCos) / Scale
            );

            // Minor axis already synchronized by FocalDistance setter.
        }

        protected override bool UsesPolygonEdgeSnap() => false;
    }
}