using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ShapeEditor
{
    public abstract class ShapeBase
    {
        public int SidesCount { get; protected set; }

        public List<Brush> SideColors { get; set; } = new();
        public List<double> SideThickness { get; set; } = new();

        public Brush Fill { get; set; } = Brushes.Transparent;
        public double Scale { get; set; } = 1.0;
        public double Angle { get; set; } = 0; // угол поворота в градусах

        // Вершины в локальных координатах относительно точки привязки (немасштабированные)
        public Point[] Vertices { get; set; }

        // Точка привязки в локальных координатах (немасштабированная)
        public Point AnchorPoint { get; set; } = new Point(0, 0);

        // Минимальные координаты bounding box последнего построения (в масштабированных координатах)
        public double MinX { get; protected set; }
        public double MinY { get; protected set; }

        protected abstract Point[] GetDefaultVertices();

        public virtual string[] SideNames
        {
            get
            {
                string[] names = new string[SidesCount];
                for (int i = 0; i < SidesCount; i++)
                    names[i] = $"Сторона {i + 1}";
                return names;
            }
        }

        protected ShapeBase()
        {
            Vertices = GetDefaultVertices();
        }

        /// <summary>
        /// Возвращает текущие (повёрнутые) вершины без масштаба.
        /// </summary>
        private Point[] GetRotatedVertices()
        {
            int n = Vertices.Length;
            double angleRad = Angle * Math.PI / 180.0;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);
            Point[] rotated = new Point[n];
            for (int i = 0; i < n; i++)
            {
                double dx = Vertices[i].X - AnchorPoint.X;
                double dy = Vertices[i].Y - AnchorPoint.Y;
                rotated[i] = new Point(
                    AnchorPoint.X + dx * cos - dy * sin,
                    AnchorPoint.Y + dx * sin + dy * cos);
            }
            return rotated;
        }

        /// <summary>
        /// Притягивает точку к центральной линии ближайшего ребра (с учётом текущего поворота),
        /// если она находится в пределах половины толщины этого ребра.
        /// Для круга возвращает исходную точку.
        /// </summary>
        public virtual Point SnapToEdgeCenter(Point point)
        {
            if (this is CircleShape)
                return point;

            if (Vertices.Length < 2)
                return point;

            Point[] rotatedVerts = GetRotatedVertices();

            double bestDist = double.MaxValue;
            Point bestPoint = point;
            bool found = false;

            for (int i = 0; i < rotatedVerts.Length; i++)
            {
                Point v1 = rotatedVerts[i];
                Point v2 = rotatedVerts[(i + 1) % rotatedVerts.Length];
                double thickness = i < SideThickness.Count ? SideThickness[i] : 3.0;
                double halfThick = thickness / 2.0;

                Vector ab = v2 - v1;
                Vector ap = point - v1;
                double t = (ap.X * ab.X + ap.Y * ab.Y) / ab.LengthSquared;
                if (t < 0) t = 0;
                if (t > 1) t = 1;
                Point closest = v1 + t * ab;
                double dist = (point - closest).Length;

                if (dist <= halfThick)
                {
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestPoint = closest;
                        found = true;
                    }
                }
            }

            return found ? bestPoint : point;
        }

        // Проверка, находится ли точка (в немасштабированных локальных координатах) внутри фигуры с учётом текущего масштаба и поворота
        public virtual bool IsPointInside(Point localPoint)
        {
            if (Vertices.Length < 3) return false;

            // Получаем повёрнутые и масштабированные вершины
            Point[] rotatedVerts = GetRotatedVertices();
            Point[] scaledVertices = new Point[rotatedVerts.Length];
            for (int i = 0; i < rotatedVerts.Length; i++)
                scaledVertices[i] = new Point(rotatedVerts[i].X * Scale, rotatedVerts[i].Y * Scale);

            Point scaledPoint = new Point(localPoint.X * Scale, localPoint.Y * Scale);

            // Основная проверка (ray casting)
            bool inside = PointInPolygon(scaledPoint, scaledVertices);
            if (inside) return true;

            // Дополнительная проверка – лежит ли точка на ребре (с epsilon = 1e-6)
            const double epsilon = 1e-6;
            for (int i = 0; i < scaledVertices.Length; i++)
            {
                Point a = scaledVertices[i];
                Point b = scaledVertices[(i + 1) % scaledVertices.Length];
                double dist = DistanceToSegment(scaledPoint, a, b);
                if (dist < epsilon) return true;
            }
            return false;
        }

        // Алгоритм определения принадлежности точки полигону (ray casting)
        private bool PointInPolygon(Point p, Point[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i, i++)
            {
                if (((polygon[i].Y > p.Y) != (polygon[j].Y > p.Y)) &&
                    (p.X < (polygon[j].X - polygon[i].X) * (p.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private double DistanceToSegment(Point p, Point a, Point b)
        {
            Vector ab = b - a;
            Vector ap = p - a;
            double t = (ap.X * ab.X + ap.Y * ab.Y) / ab.LengthSquared;
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            Point closest = a + t * ab;
            return (p - closest).Length;
        }

        // Возвращает мировые координаты точки привязки по текущему положению canvas
        public Point GetAnchorWorldPosition(Canvas canvas)
        {
            double left = Canvas.GetLeft(canvas);
            double top = Canvas.GetTop(canvas);
            double anchorLocalX = AnchorPoint.X * Scale;
            double anchorLocalY = AnchorPoint.Y * Scale;
            return new Point(left + anchorLocalX - MinX, top + anchorLocalY - MinY);
        }

        public virtual Canvas Build(double anchorWorldX, double anchorWorldY)
        {
            int n = Vertices.Length;
            if (n < 3) return new Canvas();

            double[] thick = new double[n];
            Brush[] colors = new Brush[n];
            for (int i = 0; i < n; i++)
            {
                thick[i] = i < SideThickness.Count ? SideThickness[i] : 3.0;
                colors[i] = i < SideColors.Count ? SideColors[i] : Brushes.Black;
            }

            // Поворачиваем вершины вокруг точки привязки
            double angleRad = Angle * Math.PI / 180.0;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);
            Point[] rotatedVertices = new Point[n];
            for (int i = 0; i < n; i++)
            {
                double dx = Vertices[i].X - AnchorPoint.X;
                double dy = Vertices[i].Y - AnchorPoint.Y;
                rotatedVertices[i] = new Point(
                    AnchorPoint.X + dx * cos - dy * sin,
                    AnchorPoint.Y + dx * sin + dy * cos);
            }

            // Масштабируем повёрнутые вершины
            Point[] vertices = new Point[n];
            for (int i = 0; i < n; i++)
                vertices[i] = new Point(rotatedVertices[i].X * Scale, rotatedVertices[i].Y * Scale);

            // Единичные векторы сторон и внешние нормали
            Vector[] e = new Vector[n];
            Vector[] nVec = new Vector[n];
            for (int i = 0; i < n; i++)
            {
                Vector v = vertices[(i + 1) % n] - vertices[i];
                double len = v.Length;
                if (len < 1e-6) len = 1;
                e[i] = v / len;
                nVec[i] = new Vector(e[i].Y, -e[i].X);
            }

            // Внешние и внутренние точки
            Point[] outer = new Point[n];
            Point[] inner = new Point[n];
            for (int i = 0; i < n; i++)
            {
                int prev = (i - 1 + n) % n;
                double dPrev = thick[prev] / 2;
                double dCurr = thick[i] / 2;

                Vector C = dCurr * nVec[i] - dPrev * nVec[prev];
                double det = e[prev].X * e[i].Y - e[prev].Y * e[i].X;

                if (Math.Abs(det) < 1e-6)
                {
                    outer[i] = vertices[i] + dPrev * nVec[prev];
                    inner[i] = vertices[i] - dPrev * nVec[prev];
                }
                else
                {
                    double u = (C.X * e[i].Y - C.Y * e[i].X) / det;
                    outer[i] = vertices[i] + dPrev * nVec[prev] + u * e[prev];
                    inner[i] = vertices[i] - dPrev * nVec[prev] - u * e[prev];
                }
            }

            // Bounding box всех точек
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (var p in outer)
            {
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }
            foreach (var p in inner)
            {
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }

            double width = maxX - minX;
            double height = maxY - minY;

            // Сохраняем смещение
            MinX = minX;
            MinY = minY;

            Canvas canvas = new Canvas { Width = width, Height = height };

            // Заливка
            Polygon fillPoly = new Polygon
            {
                Points = new PointCollection(),
                Fill = Fill,
                Stroke = null,
                Tag = -1 // заливка
            };
            foreach (var p in inner)
                fillPoly.Points.Add(new Point(p.X - minX, p.Y - minY));
            canvas.Children.Add(fillPoly);

            // Стороны
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                var sidePoly = new Polygon
                {
                    Points = new PointCollection
                    {
                        new Point(outer[i].X - minX, outer[i].Y - minY),
                        new Point(outer[next].X - minX, outer[next].Y - minY),
                        new Point(inner[next].X - minX, inner[next].Y - minY),
                        new Point(inner[i].X - minX, inner[i].Y - minY)
                    },
                    Fill = colors[i],
                    Stroke = null,
                    Tag = i // индекс стороны
                };
                canvas.Children.Add(sidePoly);
            }

            // Точка привязки
            double anchorLocalX = AnchorPoint.X * Scale;
            double anchorLocalY = AnchorPoint.Y * Scale;
            Ellipse anchorDot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = Brushes.Red,
                Stroke = Brushes.DarkRed,
                StrokeThickness = 1,
                Tag = "Anchor"
            };
            Canvas.SetLeft(anchorDot, anchorLocalX - minX - 5);
            Canvas.SetTop(anchorDot, anchorLocalY - minY - 5);
            canvas.Children.Add(anchorDot);

            // Вершины (кроме круга) – невидимые, но кликабельные
            if (!(this is CircleShape))
            {
                for (int i = 0; i < n; i++)
                {
                    Ellipse vertexDot = new Ellipse
                    {
                        Width = 8,
                        Height = 8,
                        Fill = Brushes.Transparent,
                        Stroke = Brushes.Transparent,
                        StrokeThickness = 0,
                        Tag = i
                    };
                    Canvas.SetLeft(vertexDot, vertices[i].X - minX - 4);
                    Canvas.SetTop(vertexDot, vertices[i].Y - minY - 4);
                    canvas.Children.Add(vertexDot);
                }
            }

            // Позиционируем canvas так, чтобы точка привязки попала в anchorWorld
            Canvas.SetLeft(canvas, anchorWorldX - anchorLocalX + minX);
            Canvas.SetTop(canvas, anchorWorldY - anchorLocalY + minY);

            return canvas;
        }
    }
}