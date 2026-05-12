using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.IO;
using System.Text.Json;
using System.Globalization;

namespace ShapeEditor
{
    /// <summary>
    /// Базовый класс для всех многоугольников.
    /// Через него строятся: прямоугольники, треугольники, трапеции, шестиугольники, произвольные многоугольники.
    /// </summary>
    public class PolygonShape : ShapeBase
    {
        public class CustomSegment
        {
            public string Name { get; set; } = "Сегмент";

            // Параметры отрезка (в локальных координатах, немасштабированных)
            public double Length { get; set; } = 100;
            public Brush Color { get; set; } = Brushes.Black;
            public double Thickness { get; set; } = 3.0;

            // Угол между этим отрезком и СЛЕДУЮЩИМ (в градусах)
            public double AngleToNext { get; set; } = 0;
            public bool AngleLocked { get; set; } = false;
            public bool LengthLocked { get; set; } = false;

            public CustomSegment Clone()
            {
                return new CustomSegment
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

        private string _displayNameRuOverride;
        private string[] _sideNamesOverride;
        public bool EnforceIsosceles { get; set; } = false;
        public string PolygonType { get; set; } = "Custom";

        // ===== CustomShape-like (segment-driven) mode =====
        // Transplanted from CustomShape.cs/LineSegment.cs.
        public bool IsCustomSegmentShape { get; set; } = false;
        public double InitialDirection { get; set; } = 0.0;
        public bool IsClosed { get; set; } = false;

        private List<CustomSegment> _segments = new();
        public List<CustomSegment> Segments
        {
            get => _segments;
            set
            {
                _segments = value ?? new List<CustomSegment>();
            }
        }

        public PolygonShape() : base() { }

        public PolygonShape(int sidesCount)
        {
            SidesCount = sidesCount;
            Vertices = GetDefaultVertices();
            EdgeLengthLocked = new List<bool>(Vertices.Length);
            for (int i = 0; i < Vertices.Length; i++) EdgeLengthLocked.Add(false);
        }

        public override string DisplayNameEn => IsCustomSegmentShape
            ? "CustomShape"
            : DisplayNameRuOverride switch
            {
                "Прямоугольник" => "Rectangle",
                "Треугольник" => "Triangle",
                "Трапеция" => "Trapezoid",
                "Шестиугольник" => "Hexagon",
                _ => "Polygon"
            };

        public override string DisplayNameRu => IsCustomSegmentShape ? "Пользовательская" : base.DisplayNameRu;
        public override string[] SideNames => _sideNamesOverride ?? base.SideNames;

        public string DisplayNameRuOverride { get => _displayNameRuOverride; set => _displayNameRuOverride = value; }
        public string[] SideNamesOverride { get => _sideNamesOverride; set => _sideNamesOverride = value; }

        protected override Point[] GetDefaultVertices()
        {
            if (SidesCount < 3) return new Point[0];
            var verts = new Point[SidesCount];
            double radius = 60;
            for (int i = 0; i < SidesCount; i++)
            {
                double angle = -Math.PI / 2 + 2 * Math.PI * i / SidesCount;
                verts[i] = new Point(radius * Math.Cos(angle), radius * Math.Sin(angle));
            }
            return verts;
        }

        public override Canvas Build(double anchorWorldX, double anchorWorldY)
        {
            if (!IsCustomSegmentShape)
                return base.Build(anchorWorldX, anchorWorldY);

            // Render logic transplanted from CustomShape.cs (segment-driven polyline/polygon).
            if (Vertices == null || Vertices.Length < 2)
            {
                double size = 40;
                MinX = -size / 2; MinY = -size / 2;
                MaxX = size / 2; MaxY = size / 2;

                var placeholder = new Canvas { Width = size, Height = size };
                var anchorDot = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = Brushes.White,
                    Stroke = Brushes.Purple,
                    StrokeThickness = 1,
                    Tag = "Anchor"
                };
                Canvas.SetLeft(anchorDot, size / 2 - 5);
                Canvas.SetTop(anchorDot, size / 2 - 5);
                placeholder.Children.Add(anchorDot);

                Canvas.SetLeft(placeholder, anchorWorldX - AnchorPoint.X * Scale + MinX);
                Canvas.SetTop(placeholder, anchorWorldY - AnchorPoint.Y * Scale + MinY);
                return placeholder;
            }

            double angleRad = Angle * Math.PI / 180.0;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);

            var transformed = new List<Point>(Vertices.Length);
            for (int i = 0; i < Vertices.Length; i++)
            {
                double dx = Vertices[i].X - AnchorPoint.X;
                double dy = Vertices[i].Y - AnchorPoint.Y;
                double rx = AnchorPoint.X + dx * cos - dy * sin;
                double ry = AnchorPoint.Y + dx * sin + dy * cos;
                transformed.Add(new Point(rx * Scale, ry * Scale));
            }

            bool isClosed = IsClosed && transformed.Count >= 3;
            Point[] verts;
            if (isClosed)
                verts = transformed.Take(transformed.Count - 1).ToArray();
            else
                verts = transformed.ToArray();

            int n = verts.Length;
            if (n < 2) return new Canvas();

            if (isClosed && n >= 3)
                return BuildClosedPolygon(verts, anchorWorldX, anchorWorldY);
            else
                return BuildOpenPolyline(verts, anchorWorldX, anchorWorldY);
        }

        private Canvas BuildClosedPolygon(Point[] verts, double anchorWorldX, double anchorWorldY)
        {
            int n = verts.Length;
            double[] thick = new double[n];
            Brush[] colors = new Brush[n];
            for (int i = 0; i < n; i++)
            {
                thick[i] = i < Segments.Count ? Segments[i].Thickness : 3.0;
                colors[i] = i < Segments.Count ? Segments[i].Color : Brushes.Black;
            }

            Vector[] e = new Vector[n];
            Vector[] nVec = new Vector[n];
            for (int i = 0; i < n; i++)
            {
                var a = verts[i];
                var b = verts[(i + 1) % n];
                Vector v = b - a;
                double len = v.Length;
                if (len < 1e-6) len = 1;
                e[i] = v / len;
                nVec[i] = new Vector(e[i].Y, -e[i].X);
            }

            Point[] outer = new Point[n];
            Point[] inner = new Point[n];
            for (int i = 0; i < n; i++)
            {
                int prev = (i - 1 + n) % n;
                double dPrev = thick[prev] / 2.0;
                double dCurr = thick[i] / 2.0;
                Vector C = dCurr * nVec[i] - dPrev * nVec[prev];
                double det = e[prev].X * e[i].Y - e[prev].Y * e[i].X;

                if (Math.Abs(det) < 1e-6)
                {
                    outer[i] = verts[i] + dPrev * nVec[prev];
                    inner[i] = verts[i] - dPrev * nVec[prev];
                }
                else
                {
                    double u = (C.X * e[i].Y - C.Y * e[i].X) / det;
                    outer[i] = verts[i] + dPrev * nVec[prev] + u * e[prev];
                    inner[i] = verts[i] - dPrev * nVec[prev] - u * e[prev];
                }
            }

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (var p in outer) { minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y); }
            foreach (var p in inner) { minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y); }

            MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
            double width = maxX - minX;
            double height = maxY - minY;
            var canvas = new Canvas { Width = Math.Max(1, width), Height = Math.Max(1, height) };

            // Fill
            var fillPoly = new Polygon
            {
                Points = new PointCollection(inner.Select(p => new Point(p.X - minX, p.Y - minY))),
                Fill = Fill,
                Stroke = null,
                Tag = -1
            };
            canvas.Children.Add(fillPoly);

            // Sides
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
                    Fill = colors[i] ?? Brushes.Black,
                    Stroke = null,
                    Tag = i
                };
                canvas.Children.Add(sidePoly);
            }

            // Anchor
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
            Canvas.SetLeft(anchorDot, anchorLocalX - minX - 5);
            Canvas.SetTop(anchorDot, anchorLocalY - minY - 5);
            canvas.Children.Add(anchorDot);

            Canvas.SetLeft(canvas, anchorWorldX - anchorLocalX + minX);
            Canvas.SetTop(canvas, anchorWorldY - anchorLocalY + minY);
            return canvas;
        }

        private Canvas BuildOpenPolyline(Point[] verts, double anchorWorldX, double anchorWorldY)
        {
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            double maxThickness = 0;
            for (int i = 0; i < verts.Length - 1; i++)
            {
                var p1 = verts[i]; var p2 = verts[i + 1];
                minX = Math.Min(minX, Math.Min(p1.X, p2.X)); maxX = Math.Max(maxX, Math.Max(p1.X, p2.X));
                minY = Math.Min(minY, Math.Min(p1.Y, p2.Y)); maxY = Math.Max(maxY, Math.Max(p1.Y, p2.Y));
                maxThickness = Math.Max(maxThickness, i < Segments.Count ? Segments[i].Thickness : 3.0);
            }

            minX -= maxThickness; minY -= maxThickness;
            maxX += maxThickness; maxY += maxThickness;
            MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;

            double width = maxX - minX;
            double height = maxY - minY;
            var canvas = new Canvas { Width = Math.Max(1, width), Height = Math.Max(1, height) };

            for (int i = 0; i < verts.Length - 1; i++)
            {
                Point p1 = verts[i], p2 = verts[i + 1];
                Vector dir = p2 - p1;
                double lenv = dir.Length;
                if (lenv < 0.01) continue;
                dir.Normalize();
                Vector perp = new Vector(-dir.Y, dir.X);
                double thickness = i < Segments.Count ? Segments[i].Thickness : 3.0;
                Vector offsetV = perp * (thickness / 2.0);

                var poly = new Polygon
                {
                    Points = new PointCollection
                    {
                        new Point(p1.X - offsetV.X - minX, p1.Y - offsetV.Y - minY),
                        new Point(p1.X + offsetV.X - minX, p1.Y + offsetV.Y - minY),
                        new Point(p2.X + offsetV.X - minX, p2.Y + offsetV.Y - minY),
                        new Point(p2.X - offsetV.X - minX, p2.Y - offsetV.Y - minY)
                    },
                    Fill = i < Segments.Count ? Segments[i].Color : Brushes.Black,
                    Tag = i
                };
                canvas.Children.Add(poly);
            }

            // Anchor
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
            Canvas.SetLeft(anchorDot, anchorLocalX - minX - 5);
            Canvas.SetTop(anchorDot, anchorLocalY - minY - 5);
            canvas.Children.Add(anchorDot);

            Canvas.SetLeft(canvas, anchorWorldX - anchorLocalX + minX);
            Canvas.SetTop(canvas, anchorWorldY - anchorLocalY + minY);
            return canvas;
        }

        // ==================== Custom segment editor API ====================

        public void AddSegment(double length, double internalAngle = 180)
        {
            var segment = new CustomSegment
            {
                Name = $"Отрезок {Segments.Count + 1}",
                Length = length,
                Color = Brushes.Black,
                Thickness = 3.0
            };

            if (Segments.Count == 0)
                segment.AngleToNext = 0;
            else
                segment.AngleToNext = internalAngle;

            Segments.Add(segment);
            SidesCount++;
            while (EdgeLengthLocked.Count < SidesCount) EdgeLengthLocked.Add(false);
            RebuildVertices();
        }

        public void RemoveSegment(int index)
        {
            if (index >= 0 && index < Segments.Count)
            {
                Segments.RemoveAt(index);
                SidesCount--;
                if (index < EdgeLengthLocked.Count)
                    EdgeLengthLocked.RemoveAt(index);
                RebuildVertices();
            }
        }

        public void SetEdgeAngle(int edgeIndex, double newAngleDegrees)
        {
            if (edgeIndex >= 0 && edgeIndex < Segments.Count)
            {
                if (Segments[edgeIndex].AngleLocked) return;
                Segments[edgeIndex].AngleToNext = newAngleDegrees;
                RebuildVertices();
            }
        }

        public double GetEdgeAngle(int edgeIndex)
        {
            return edgeIndex >= 0 && edgeIndex < Segments.Count ? Segments[edgeIndex].AngleToNext : 0;
        }

        public void RebuildVertices()
        {
            if (Segments.Count == 0)
            {
                Vertices = new Point[0];
                return;
            }

            var vertices = new List<Point>();
            Point currentPos = new Point(0, 0);
            double currentAngle = 0;
            vertices.Add(currentPos);

            for (int i = 0; i < Segments.Count; i++)
            {
                var segment = Segments[i];
                double angleRad = currentAngle * Math.PI / 180.0;
                Point nextPos = new Point(
                    currentPos.X + segment.Length * Math.Cos(angleRad),
                    currentPos.Y + segment.Length * Math.Sin(angleRad)
                );
                vertices.Add(nextPos);
                currentPos = nextPos;
                currentAngle += 180 - segment.AngleToNext;
            }

            Vertices = vertices.ToArray();
        }

        public Vector CenterAnchorToBounds()
        {
            if (Vertices == null || Vertices.Length == 0) return new Vector(0, 0);

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (var v in Vertices)
            {
                minX = Math.Min(minX, v.X); maxX = Math.Max(maxX, v.X);
                minY = Math.Min(minY, v.Y); maxY = Math.Max(maxY, v.Y);
            }

            Point newAnchor = new Point((minX + maxX) / 2.0, (minY + maxY) / 2.0);

            for (int i = 0; i < Vertices.Length; i++)
            {
                Vertices[i] = new Point(
                    Vertices[i].X - newAnchor.X,
                    Vertices[i].Y - newAnchor.Y
                );
            }

            Vector anchorShift = new Vector(newAnchor.X - AnchorPoint.X, newAnchor.Y - AnchorPoint.Y);
            AnchorPoint = new Point(0, 0);
            return anchorShift;
        }

        // ==================== ФАБРИЧНЫЕ МЕТОДЫ ====================

        public static PolygonShape CreateRectangle(double width = 120, double height = 80)
        {
            var shape = new PolygonShape(4);
            shape._displayNameRuOverride = "Прямоугольник";
            shape._sideNamesOverride = new[] { "Верхняя", "Правая", "Нижняя", "Левая" };
            double halfW = width / 2.0, halfH = height / 2.0;
            shape.Vertices = new Point[]
            {
                new Point(-halfW, -halfH), new Point(halfW, -halfH),
                new Point(halfW, halfH), new Point(-halfW, halfH)
            };
            return shape;
        }

        public static PolygonShape CreateTriangle(double baseWidth = 120, double height = 100)
        {
            var shape = new PolygonShape(3);
            shape._displayNameRuOverride = "Треугольник";
            shape._sideNamesOverride = new[] { "Правая", "Нижняя", "Левая" };
            double halfW = baseWidth / 2.0, halfH = height / 2.0;
            shape.Vertices = new Point[]
            {
                new Point(0, -halfH), new Point(halfW, halfH), new Point(-halfW, halfH)
            };
            return shape;
        }

        public static PolygonShape CreateTrapezoid(double topWidth = 80, double bottomWidth = 140, double height = 80)
        {
            var shape = new PolygonShape(4);
            shape._displayNameRuOverride = "Трапеция";
            shape._sideNamesOverride = new[] { "Верхняя", "Правая боковая", "Нижняя", "Левая боковая" };
            double halfTop = topWidth / 2.0, halfBot = bottomWidth / 2.0, halfH = height / 2.0;
            shape.Vertices = new Point[]
            {
                new Point(-halfTop, -halfH), new Point(halfTop, -halfH),
                new Point(halfBot, halfH), new Point(-halfBot, halfH)
            };
            return shape;
        }

        public static PolygonShape CreateHexagon(double radius = 60)
        {
            var shape = new PolygonShape(6);
            shape._displayNameRuOverride = "Шестиугольник";
            shape._sideNamesOverride = new[] { "Верхняя правая", "Правая", "Нижняя правая", "Нижняя левая", "Левая", "Верхняя левая" };
            var verts = new Point[6];
            for (int i = 0; i < 6; i++)
            {
                double angle = -Math.PI / 2 + 2 * Math.PI * i / 6;
                verts[i] = new Point(radius * Math.Cos(angle), radius * Math.Sin(angle));
            }
            shape.Vertices = verts;
            return shape;
        }

        public static PolygonShape CreateRegularPolygon(int sides, double radius = 60)
        {
            var shape = new PolygonShape(sides);
            shape._displayNameRuOverride = $"{sides}-угольник";
            var verts = new Point[sides];
            for (int i = 0; i < sides; i++)
            {
                double angle = -Math.PI / 2 + 2 * Math.PI * i / sides;
                verts[i] = new Point(radius * Math.Cos(angle), radius * Math.Sin(angle));
            }
            shape.Vertices = verts;
            return shape;
        }

        // ==================== СПЕЦИАЛИЗИРОВАННАЯ ЛОГИКА РЁБЕР ====================

        public override void SetEdgeLength(int edgeIndex, double newLength)
        {
            if (IsCustomSegmentShape)
            {
                if (edgeIndex >= 0 && edgeIndex < Segments.Count && newLength > 0)
                {
                    if (edgeIndex < EdgeLengthLocked.Count && EdgeLengthLocked[edgeIndex]) return;
                    Segments[edgeIndex].Length = newLength;
                    RebuildVertices();
                }
                return;
            }

            if (edgeIndex < 0 || edgeIndex >= Vertices.Length || newLength <= 0) return;
            if (EdgeLengthLocked != null && edgeIndex < EdgeLengthLocked.Count && EdgeLengthLocked[edgeIndex]) return;
            if (Vertices.Length < 3) { base.SetEdgeLength(edgeIndex, newLength); return; }

            if (SidesCount == 4 && DisplayNameRu == "Прямоугольник") { SetRectangleEdgeLength(edgeIndex, newLength); return; }
            if (SidesCount == 4 && DisplayNameRu == "Трапеция") { SetTrapezoidEdgeLength(edgeIndex, newLength); return; }
            if (SidesCount == 3) { SetTriangleEdgeLength(edgeIndex, newLength); return; }
            base.SetEdgeLength(edgeIndex, newLength);
        }

        public override bool TrySetEdgeLengths(double[] lengths)
        {
            if (IsCustomSegmentShape)
            {
                if (lengths == null || lengths.Length != Segments.Count) return false;
                while (EdgeLengthLocked.Count < Segments.Count) EdgeLengthLocked.Add(false);

                for (int i = 0; i < lengths.Length; i++)
                {
                    if (lengths[i] <= 0) return false;
                    if (EdgeLengthLocked[i]) return false;
                }

                for (int i = 0; i < lengths.Length; i++)
                    Segments[i].Length = lengths[i];

                RebuildVertices();
                return true;
            }

            if (lengths == null || lengths.Length != Vertices.Length) return false;
            if (SidesCount == 3) return TrySetTriangleEdgeLengths(lengths);
            if (SidesCount == 4 && DisplayNameRu == "Трапеция") return TrySetTrapezoidEdgeLengths(lengths);
            var backup = (Point[])Vertices.Clone();
            for (int i = 0; i < lengths.Length; i++)
            {
                if (EdgeLengthLocked != null && i < EdgeLengthLocked.Count && EdgeLengthLocked[i]) continue;
                SetEdgeLength(i, lengths[i]);
            }
            return true;
        }

        // --- ПРЯМОУГОЛЬНИК ---
        private void SetRectangleEdgeLength(int edgeIndex, double newLength)
        {
            Point[] nv = (Point[])Vertices.Clone();
            double cx = (Vertices[0].X + Vertices[1].X + Vertices[2].X + Vertices[3].X) / 4.0;
            double cy = (Vertices[0].Y + Vertices[1].Y + Vertices[2].Y + Vertices[3].Y) / 4.0;
            Point c = new Point(cx, cy);
            Vector u = Vertices[1] - Vertices[0]; double curW = u.Length; u.Normalize();
            Vector v = new Vector(-u.Y, u.X);
            if (Vector.Multiply(v, Vertices[2] - Vertices[1]) < 0) v = -v;
            double curH = Math.Abs(Vector.Multiply(Vertices[2] - Vertices[0], v));
            double tw = curW, th = curH;
            if (edgeIndex == 0 || edgeIndex == 2) tw = newLength; else th = newLength;
            double hw = tw / 2.0, hh = th / 2.0;
            nv[0] = c - hw * u - hh * v; nv[1] = c + hw * u - hh * v;
            nv[2] = c + hw * u + hh * v; nv[3] = c - hw * u + hh * v;
            if (!IsSimplePolygon(nv)) return; Vertices = nv;
        }

        // --- ТРЕУГОЛЬНИК ---
        private void SetTriangleEdgeLength(int edgeIndex, double newLength)
        {
            var lengths = new double[3];
            for (int i = 0; i < 3; i++) lengths[i] = GetEdgeLength(i);
            lengths[edgeIndex] = newLength;
            TrySetTriangleEdgeLengths(lengths);
        }

        private bool TrySetTriangleEdgeLengths(double[] lengths)
        {
            if (lengths == null || lengths.Length != 3) return false;
            for (int i = 0; i < 3; i++)
                if (EdgeLengthLocked.Count > i && EdgeLengthLocked[i]) lengths[i] = GetEdgeLength(i);
            double a = lengths[0], b = lengths[1], c = lengths[2];
            if (a <= 0 || b <= 0 || c <= 0) return false;
            if (a + b <= c || a + c <= b || b + c <= a) return false;
            Point[] nv = (Point[])Vertices.Clone();
            double hb = b / 2.0, ccx = (Vertices[1].X + Vertices[2].X) / 2.0, by = (Vertices[1].Y + Vertices[2].Y) / 2.0;
            Point v1 = new Point(ccx + hb, by), v2 = new Point(ccx - hb, by);
            double x0r = (c * c - a * a) / (2.0 * b), blx = ccx - hb, x0w = blx + x0r;
            double dx = x0w - v1.X, h2 = a * a - dx * dx;
            if (h2 < -1e-6) return false;
            double h = h2 <= 0 ? 0 : Math.Sqrt(Math.Max(0, h2));
            Point cup = new Point(x0w, by - h), cdn = new Point(x0w, by + h);
            Point pv0 = Vertices[0], cv0 = Dist(pv0, cup) <= Dist(pv0, cdn) ? cup : cdn;
            nv[0] = cv0; nv[1] = v1; nv[2] = v2;
            if (!IsSimplePolygon(nv)) return false; Vertices = nv; return true;
        }

        private static double Dist(Point a, Point b) { double dx = a.X - b.X, dy = a.Y - b.Y; return Math.Sqrt(dx * dx + dy * dy); }

        // --- ТРАПЕЦИЯ ---
        private void SetTrapezoidEdgeLength(int edgeIndex, double newLength)
        {
            var lengths = new double[4];
            for (int i = 0; i < 4; i++) lengths[i] = GetEdgeLength(i);
            lengths[edgeIndex] = newLength;
            if (EnforceIsosceles && (edgeIndex == 1 || edgeIndex == 3))
            {
                int partner = edgeIndex == 1 ? 3 : 1;
                if (!(EdgeLengthLocked.Count > partner && EdgeLengthLocked[partner])) lengths[partner] = newLength;
            }
            TrySetTrapezoidEdgeLengths(lengths);
        }

        private bool TrySetTrapezoidEdgeLengths(double[] lengths)
        {
            if (lengths == null || lengths.Length != 4) return false;
            for (int i = 0; i < 4; i++) if (EdgeLengthLocked.Count > i && EdgeLengthLocked[i]) lengths[i] = GetEdgeLength(i);
            double a = lengths[0], br = lengths[1], c = lengths[2], bl = lengths[3];
            if (a <= 0 || br <= 0 || c <= 0 || bl <= 0) return false;
            Point[] nv = (Point[])Vertices.Clone();
            double x0 = Vertices[0].X, ty = Vertices[0].Y, x1 = x0 + a;
            double sl = Math.Max(-bl, a - c - br), sh = Math.Min(bl, a - c + br);
            if (sl > sh) return false;
            double f(double s)
            {
                double v3x = x0 + s, v2x = v3x + c, dxR = v2x - x1, dxL = v3x - x0;
                double sqR = br * br - dxR * dxR, sqL = bl * bl - dxL * dxL;
                if (sqR < 0 || sqL < 0) return double.NaN;
                return Math.Sqrt(Math.Max(0, sqR)) - Math.Sqrt(Math.Max(0, sqL));
            }
            double fl = f(sl), fh = f(sh);
            if (double.IsNaN(fl) || double.IsNaN(fh)) return false;
            double sf = double.NaN;
            if (fl * fh <= 0)
            {
                double lo = sl, hi = sh;
                for (int it = 0; it < 60; it++)
                {
                    double mid = 0.5 * (lo + hi), fm = f(mid);
                    if (double.IsNaN(fm)) break;
                    if (Math.Abs(fm) < 1e-7) { sf = mid; break; }
                    if (f(lo) * fm <= 0) hi = mid; else lo = mid;
                    sf = mid;
                }
            }
            else
            {
                double bs = sl, bv = Math.Abs(fl);
                for (int i = 1; i <= 60; i++)
                {
                    double s = sl + (sh - sl) * i / 60.0, v = f(s);
                    if (double.IsNaN(v)) continue;
                    if (Math.Abs(v) < bv) { bv = Math.Abs(v); bs = s; }
                }
                sf = bs;
            }
            if (double.IsNaN(sf)) return false;
            double v3xf = x0 + sf, v2xf = v3xf + c;
            double dxRf = v2xf - x1, dxLf = v3xf - x0;
            double sqRf = br * br - dxRf * dxRf, sqLf = bl * bl - dxLf * dxLf;
            if (sqRf < -1e-9 || sqLf < -1e-9) return false;
            double dyR = sqRf <= 0 ? 0 : Math.Sqrt(Math.Max(0, sqRf));
            double dyL = sqLf <= 0 ? 0 : Math.Sqrt(Math.Max(0, sqLf));
            double dy = 0.5 * (dyR + dyL);
            double ds = Math.Sign(Vertices[2].Y - Vertices[0].Y); if (ds == 0) ds = 1;
            double by = ty + ds * dy;
            nv[0] = new Point(x0, ty); nv[1] = new Point(x1, ty);
            nv[3] = new Point(v3xf, by); nv[2] = new Point(v2xf, by);
            if (!IsSimplePolygon(nv)) return false; Vertices = nv; return true;
        }

        // ==================== JSON СОХРАНЕНИЕ / ЗАГРУЗКА ====================

        public override void SaveToJson(Utf8JsonWriter writer)
        {
            if (IsCustomSegmentShape)
            {
                writer.WriteStartObject();
                writer.WriteString("type", "CustomShape");
                writer.WriteNumber("id", Id);
                writer.WriteString("displayName", DisplayNameEn);
                writer.WriteNumber("scale", Scale);
                writer.WriteNumber("angle", Angle);
                writer.WriteNumber("anchorX", AnchorPoint.X);
                writer.WriteNumber("anchorY", AnchorPoint.Y);
                writer.WriteString("fill", GetColorHex(Fill));
                writer.WriteNumber("initialDirection", InitialDirection);
                writer.WriteBoolean("isClosed", IsClosed);

                writer.WritePropertyName("sideColors");
                writer.WriteStartArray();
                foreach (var c in SideColors) writer.WriteStringValue(GetColorHex(c));
                writer.WriteEndArray();

                writer.WritePropertyName("sideThicknesses");
                writer.WriteStartArray();
                foreach (var t in SideThickness) writer.WriteNumberValue(t);
                writer.WriteEndArray();

                writer.WritePropertyName("edgeLocks");
                writer.WriteStartArray();
                foreach (var l in EdgeLengthLocked) writer.WriteBooleanValue(l);
                writer.WriteEndArray();

                writer.WritePropertyName("vertices");
                writer.WriteStartArray();
                foreach (var v in Vertices)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("x", v.X);
                    writer.WriteNumber("y", v.Y);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();

                writer.WritePropertyName("segments");
                writer.WriteStartArray();
                foreach (var segment in Segments)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", segment.Name);
                    writer.WriteNumber("length", segment.Length);
                    writer.WriteNumber("thickness", segment.Thickness);
                    writer.WriteNumber("angleToNext", segment.AngleToNext);
                    writer.WriteBoolean("angleLocked", segment.AngleLocked);
                    writer.WriteBoolean("lengthLocked", segment.LengthLocked);
                    var c = segment.Color is SolidColorBrush sc ? sc.Color : Colors.Black;
                    writer.WriteString("color", $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}");
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();

                writer.WriteEndObject();
                return;
            }

            writer.WriteStartObject();
            writer.WriteString("type", "PolygonShape");
            writer.WriteNumber("id", Id);
            writer.WriteString("displayName", DisplayNameEn);
            writer.WriteNumber("scale", Scale);
            writer.WriteNumber("angle", Angle);
            writer.WriteNumber("anchorX", AnchorPoint.X);
            writer.WriteNumber("anchorY", AnchorPoint.Y);
            writer.WriteString("fill", GetColorHex(Fill));
            writer.WriteNumber("sidesCount", SidesCount);
            writer.WriteBoolean("enforceIsosceles", EnforceIsosceles);

            writer.WritePropertyName("sideColors");
            writer.WriteStartArray();
            foreach (var c in SideColors) writer.WriteStringValue(GetColorHex(c));
            writer.WriteEndArray();

            writer.WritePropertyName("sideThicknesses");
            writer.WriteStartArray();
            foreach (var t in SideThickness) writer.WriteNumberValue(t);
            writer.WriteEndArray();

            writer.WritePropertyName("edgeLocks");
            writer.WriteStartArray();
            foreach (var l in EdgeLengthLocked) writer.WriteBooleanValue(l);
            writer.WriteEndArray();

            writer.WritePropertyName("vertices");
            writer.WriteStartArray();
            foreach (var v in Vertices)
            {
                writer.WriteStartObject();
                writer.WriteNumber("x", v.X);
                writer.WriteNumber("y", v.Y);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            if (_sideNamesOverride != null)
            {
                writer.WritePropertyName("sideNames");
                writer.WriteStartArray();
                foreach (var n in _sideNamesOverride) writer.WriteStringValue(n);
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        public override void LoadFromJson(JsonElement element)
        {
            static bool TryReadInt32(JsonElement obj, string propertyName, out int value)
            {
                value = 0;
                if (!obj.TryGetProperty(propertyName, out var prop)) return false;
                return prop.TryGetInt32(out value);
            }

            static bool TryReadDouble(JsonElement obj, string propertyName, out double value)
            {
                value = 0;
                if (!obj.TryGetProperty(propertyName, out var prop)) return false;

                if (prop.ValueKind == JsonValueKind.Number)
                    return prop.TryGetDouble(out value);

                if (prop.ValueKind == JsonValueKind.String)
                    return double.TryParse(prop.GetString(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

                return false;
            }

            static bool TryReadBoolean(JsonElement obj, string propertyName, out bool value)
            {
                value = false;
                if (!obj.TryGetProperty(propertyName, out var prop)) return false;

                if (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False)
                {
                    value = prop.GetBoolean();
                    return true;
                }

                if (prop.ValueKind == JsonValueKind.String)
                    return bool.TryParse(prop.GetString(), out value);

                return false;
            }

            // CustomShape-like JSON persisted as type="CustomShape"
            if (element.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "CustomShape")
            {
                IsCustomSegmentShape = true;

                if (TryReadInt32(element, "id", out var id)) Id = id;
                if (TryReadDouble(element, "scale", out var scale)) Scale = scale;
                if (TryReadDouble(element, "angle", out var angle)) Angle = angle;
                if (TryReadDouble(element, "anchorX", out var anchorX) && TryReadDouble(element, "anchorY", out var anchorY))
                    AnchorPoint = new Point(anchorX, anchorY);
                if (element.TryGetProperty("fill", out var fProp) && fProp.ValueKind == JsonValueKind.String)
                    Fill = ParseColor(fProp.GetString());
                if (TryReadDouble(element, "initialDirection", out var initialDirection)) InitialDirection = initialDirection;
                if (TryReadBoolean(element, "isClosed", out var isClosed)) IsClosed = isClosed;

                SideColors.Clear();
                if (element.TryGetProperty("sideColors", out var colorsProp) && colorsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in colorsProp.EnumerateArray())
                    {
                        if (c.ValueKind == JsonValueKind.String)
                            SideColors.Add(ParseColor(c.GetString()));
                    }
                }

                SideThickness.Clear();
                if (element.TryGetProperty("sideThicknesses", out var thickProp) && thickProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in thickProp.EnumerateArray())
                    {
                        if (t.TryGetDouble(out var thickness))
                            SideThickness.Add(thickness);
                    }
                }

                EdgeLengthLocked.Clear();
                if (element.TryGetProperty("edgeLocks", out var locksProp) && locksProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var l in locksProp.EnumerateArray())
                    {
                        if (l.ValueKind == JsonValueKind.True || l.ValueKind == JsonValueKind.False)
                            EdgeLengthLocked.Add(l.GetBoolean());
                    }
                }

                Segments.Clear();
                SidesCount = 0;
                if (element.TryGetProperty("segments", out var segProp) && segProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in segProp.EnumerateArray())
                    {
                        if (s.ValueKind != JsonValueKind.Object) continue;

                        var segment = new CustomSegment
                        {
                            Name = s.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                                ? nameProp.GetString()
                                : "Сегмент",
                            Length = s.TryGetProperty("length", out var lenProp) && lenProp.TryGetDouble(out var lengthValue)
                                ? lengthValue
                                : 100,
                            Thickness = s.TryGetProperty("thickness", out var thProp) && thProp.TryGetDouble(out var thicknessValue)
                                ? thicknessValue
                                : 3.0,
                            AngleToNext = s.TryGetProperty("angleToNext", out var anProp) && anProp.TryGetDouble(out var angleToNextValue)
                                ? angleToNextValue
                                : 0,
                            AngleLocked = s.TryGetProperty("angleLocked", out var alProp) &&
                                          (alProp.ValueKind == JsonValueKind.True || alProp.ValueKind == JsonValueKind.False) &&
                                          alProp.GetBoolean(),
                            LengthLocked = s.TryGetProperty("lengthLocked", out var llProp) &&
                                           (llProp.ValueKind == JsonValueKind.True || llProp.ValueKind == JsonValueKind.False) &&
                                           llProp.GetBoolean()
                        };
                        if (s.TryGetProperty("color", out var colorProp) && colorProp.ValueKind == JsonValueKind.String)
                            segment.Color = ParseColor(colorProp.GetString());
                        Segments.Add(segment);
                        SidesCount++;
                    }
                }

                // НЕ вызываем RebuildVertices() здесь! Вершины должны быть восстановлены из JSON, если они есть.
                if (element.TryGetProperty("vertices", out var vertsProp) && vertsProp.ValueKind == JsonValueKind.Array)
                {
                    var verts = new List<Point>();
                    foreach (var v in vertsProp.EnumerateArray())
                    {
                        if (v.ValueKind != JsonValueKind.Object) continue;
                        if (!v.TryGetProperty("x", out var xProp) || !xProp.TryGetDouble(out var x)) continue;
                        if (!v.TryGetProperty("y", out var yProp) || !yProp.TryGetDouble(out var y)) continue;
                        verts.Add(new Point(x, y));
                    }

                    if (verts.Count > 0)
                        Vertices = verts.ToArray();
                }
                else
                {
                    RebuildVertices();
                }

                return;
            }

            IsCustomSegmentShape = false;

            //if (element.TryGetProperty("id", out var idProp)) Id = idProp.GetInt32();
            if (TryReadDouble(element, "scale", out var scaleDefault)) Scale = scaleDefault;
            if (TryReadDouble(element, "angle", out var angleDefault)) Angle = angleDefault;
            if (TryReadDouble(element, "anchorX", out var defaultAnchorX) && TryReadDouble(element, "anchorY", out var defaultAnchorY))
                AnchorPoint = new Point(defaultAnchorX, defaultAnchorY);
            if (element.TryGetProperty("fill", out var fillProp) && fillProp.ValueKind == JsonValueKind.String)
                Fill = ParseColor(fillProp.GetString());
            if (TryReadInt32(element, "sidesCount", out var sidesCount)) SidesCount = sidesCount;
            if (TryReadBoolean(element, "enforceIsosceles", out var enforceIsosceles)) EnforceIsosceles = enforceIsosceles;

            SideColors.Clear();
            if (element.TryGetProperty("sideColors", out var colorsPropDefault) && colorsPropDefault.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in colorsPropDefault.EnumerateArray())
                {
                    if (c.ValueKind == JsonValueKind.String)
                        SideColors.Add(ParseColor(c.GetString()));
                }
            }

            SideThickness.Clear();
            if (element.TryGetProperty("sideThicknesses", out var thickPropDefault) && thickPropDefault.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in thickPropDefault.EnumerateArray())
                {
                    if (t.TryGetDouble(out var thickness))
                        SideThickness.Add(thickness);
                }
            }

            EdgeLengthLocked.Clear();
            if (element.TryGetProperty("edgeLocks", out var locksPropDefault) && locksPropDefault.ValueKind == JsonValueKind.Array)
            {
                foreach (var l in locksPropDefault.EnumerateArray())
                {
                    if (l.ValueKind == JsonValueKind.True || l.ValueKind == JsonValueKind.False)
                        EdgeLengthLocked.Add(l.GetBoolean());
                }
            }

            if (element.TryGetProperty("vertices", out var vertsPropDefault) && vertsPropDefault.ValueKind == JsonValueKind.Array)
            {
                var verts = new List<Point>();
                foreach (var v in vertsPropDefault.EnumerateArray())
                {
                    if (v.ValueKind != JsonValueKind.Object) continue;
                    if (!v.TryGetProperty("x", out var xProp) || !xProp.TryGetDouble(out var x)) continue;
                    if (!v.TryGetProperty("y", out var yProp) || !yProp.TryGetDouble(out var y)) continue;
                    verts.Add(new Point(x, y));
                }

                if (verts.Count > 0)
                    Vertices = verts.ToArray();
            }

            if (element.TryGetProperty("sideNames", out var namesProp) && namesProp.ValueKind == JsonValueKind.Array)
            {
                var names = new List<string>();
                foreach (var n in namesProp.EnumerateArray())
                {
                    if (n.ValueKind == JsonValueKind.String)
                        names.Add(n.GetString());
                }
                _sideNamesOverride = names.ToArray();
            }

            if (element.TryGetProperty("displayName", out var dnProp) && dnProp.ValueKind == JsonValueKind.String)
                _displayNameRuOverride = dnProp.GetString();
        }
    }
}