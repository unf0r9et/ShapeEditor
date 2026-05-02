using ShapeEditor.shapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ShapeEditor
{
    public class CustomShape : ShapeBase
    {
        public List<LineSegment> Segments { get; set; } = new();

        // Направление первого сегмента (в градусах)
        public double InitialDirection { get; set; } = 0.0;

        public CustomShape()
        {
            SidesCount = 0;
            Vertices = new Point[0];
        }
        public override string DisplayNameRu => "Произвольная";
        protected override Point[] GetDefaultVertices() => new Point[0];
        public bool IsClosed { get; set; } = false;

        public void AddSegment(double length, double internalAngle = 180)
        {
            var segment = new LineSegment
            {
                Name = $"Отрезок {Segments.Count + 1}",
                Length = length,
                Color = Brushes.Black,
                Thickness = 3.0
            };

            if (Segments.Count == 0)
            {
                // Первый сегмент: направление 0°, угол поворота пока 0 (задастся при добавлении второго)
                segment.AngleToNext = 0;
            }
            else
            {
                // Сохраняем внутренний угол как есть, пересчет в направление в RebuildVertices
                segment.AngleToNext = internalAngle;
            }

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

        public override void SetEdgeLength(int edgeIndex, double newLength)
        {
            if (edgeIndex >= 0 && edgeIndex < Segments.Count && newLength > 0)
            {
                if (edgeIndex < EdgeLengthLocked.Count && EdgeLengthLocked[edgeIndex]) return;
                Segments[edgeIndex].Length = newLength;
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

            // Начальное направление - вдоль оси X (0 градусов)
            double currentAngle = 0;

            vertices.Add(currentPos);

            for (int i = 0; i < Segments.Count; i++)
            {
                var segment = Segments[i];

                // Двигаемся в текущем направлении
                double angleRad = currentAngle * Math.PI / 180.0;
                Point nextPos = new Point(
                    currentPos.X + segment.Length * Math.Cos(angleRad),
                    currentPos.Y + segment.Length * Math.Sin(angleRad)
                );

                vertices.Add(nextPos);
                currentPos = nextPos;

                // Поворачиваем направление на (180 - внутренний_угол)
                // Для i=0 (первый сегмент) AngleToNext хранит угол для второго сегмента
                currentAngle += (180 - segment.AngleToNext);
            }

            Vertices = vertices.ToArray();
        }

        public override bool TrySetEdgeLengths(double[] lengths)
        {
            if (lengths == null || lengths.Length != Segments.Count) return false;
            while (EdgeLengthLocked.Count < Segments.Count) EdgeLengthLocked.Add(false);

            for (int i = 0; i < lengths.Length; i++)
            {
                if (lengths[i] <= 0) return false;
                if (EdgeLengthLocked[i]) return false;
            }

            for (int i = 0; i < lengths.Length; i++)
            {
                Segments[i].Length = lengths[i];
            }
            RebuildVertices();
            return true;
        }

        /// <summary>
        /// Центрирует точку привязки в центре локальных границ (min/max по вершинам).
        /// </summary>
        /// <summary>
        /// Центрирует якорь по границам фигуры и возвращает смещение для корректировки позиции Canvas
        /// </summary>
        public Vector CenterAnchorToBounds()
        {
            if (Vertices == null || Vertices.Length == 0) return new Vector(0, 0);

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (var v in Vertices)
            {
                minX = Math.Min(minX, v.X);
                maxX = Math.Max(maxX, v.X);
                minY = Math.Min(minY, v.Y);
                maxY = Math.Max(maxY, v.Y);
            }

            Point newAnchor = new Point((minX + maxX) / 2.0, (minY + maxY) / 2.0);

            // ?? Сдвигаем вершины относительно нового якоря, чтобы они стали локальными
            // Теперь AnchorPoint в (0,0) — это центр, а вершины вокруг него
            for (int i = 0; i < Vertices.Length; i++)
            {
                Vertices[i] = new Point(
                    Vertices[i].X - newAnchor.X,
                    Vertices[i].Y - newAnchor.Y
                );
            }

            // Возвращаем, на сколько сместился центр (для корректировки Canvas)
            Vector anchorShift = new Vector(newAnchor.X - AnchorPoint.X, newAnchor.Y - AnchorPoint.Y);

            // Новый AnchorPoint — центр (0,0) в новой системе координат
            AnchorPoint = new Point(0, 0);

            return anchorShift;
        }

        public override Canvas Build(double anchorWorldX, double anchorWorldY)
        {
            // Если нет ребер — создаём минимальный интерактивный canvas с точкой якоря,
            // чтобы фигуру можно было выбрать/перемещать и чтобы мировые координаты работали.
            if (Vertices == null || Vertices.Length < 2)
            {
                double size = 40;
                MinX = -size / 2;
                MinY = -size / 2;
                MaxX = size / 2;
                MaxY = size / 2;

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
                // AnchorPoint may be (0,0) — place dot in center
                Canvas.SetLeft(anchorDot, size / 2 - 5);
                Canvas.SetTop(anchorDot, size / 2 - 5);
                placeholder.Children.Add(anchorDot);

                // position so that anchorWorld corresponds to local anchor (0,0)
                Canvas.SetLeft(placeholder, anchorWorldX - (AnchorPoint.X * Scale) + MinX);
                Canvas.SetTop(placeholder, anchorWorldY - (AnchorPoint.Y * Scale) + MinY);

                return placeholder;
            }

            // Преобразуем вершины (поворот + масштаб относительно AnchorPoint)
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

            //// Detect closed polygon: if last equals first (within eps) then treat as closed polygon
            //bool isClosed = false;
            //if (transformed.Count >= 2)
            //{
            //    var first = transformed[0];
            //    var last = transformed[transformed.Count - 1];
            //    if ((first - last).Length < 1e-6)
            //        isClosed = true;
            //}
            bool isClosed = this.IsClosed && transformed.Count >= 3;

            // If closed, remove duplicate last for polygon processing
            Point[] verts;
            if (isClosed)
            {
                verts = transformed.Take(transformed.Count - 1).ToArray();
            }
            else
            {
                verts = transformed.ToArray();
            }

            int n = verts.Length;
            if (n < 2) return new Canvas();

            if (isClosed && n >= 3)
            {
                // thickness per side (side i between verts[i] and verts[(i+1)%n])
                double[] thick = new double[n];
                Brush[] colors = new Brush[n];
                for (int i = 0; i < n; i++)
                {
                    thick[i] = i < Segments.Count ? Segments[i].Thickness : 3.0;
                    colors[i] = i < Segments.Count ? Segments[i].Color : Brushes.Black;
                }

                // unit vectors and normals
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

                // Bounding box of all points
                double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
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

                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;

                double width = maxX - minX;
                double height = maxY - minY;
                var canvas = new Canvas { Width = Math.Max(1, width), Height = Math.Max(1, height) };

                // Fill polygon (inner)
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

                // Anchor dot (scale already applied to verts)
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
            else
            {
                // Open polyline or not enough verts -> draw per-segment rectangles
                double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
                double maxThickness = 0;
                for (int i = 0; i < verts.Length - 1; i++)
                {
                    var p1 = verts[i];
                    var p2 = verts[i + 1];
                    minX = Math.Min(minX, Math.Min(p1.X, p2.X));
                    maxX = Math.Max(maxX, Math.Max(p1.X, p2.X));
                    minY = Math.Min(minY, Math.Min(p1.Y, p2.Y));
                    maxY = Math.Max(maxY, Math.Max(p1.Y, p2.Y));
                    maxThickness = Math.Max(maxThickness, i < Segments.Count ? Segments[i].Thickness : 3.0);
                }

                minX -= maxThickness;
                minY -= maxThickness;
                maxX += maxThickness;
                maxY += maxThickness;

                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;

                double width = maxX - minX;
                double height = maxY - minY;
                var canvas = new Canvas { Width = Math.Max(1, width), Height = Math.Max(1, height) };

                for (int i = 0; i < verts.Length - 1; i++)
                {
                    Point p1 = verts[i];
                    Point p2 = verts[i + 1];

                    Vector dir = p2 - p1;
                    double lenv = dir.Length;
                    if (lenv < 0.01) continue;
                    dir.Normalize();
                    Vector perp = new Vector(-dir.Y, dir.X);

                    double thickness = (i < Segments.Count) ? Segments[i].Thickness : 3.0;
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
                        Fill = (i < Segments.Count) ? Segments[i].Color : Brushes.Black,
                        Tag = i
                    };
                    canvas.Children.Add(poly);
                }

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
        }

        public override void Save(BinaryWriter writer)
        {
            base.Save(writer);
            writer.Write(InitialDirection);
            writer.Write(IsClosed);
            writer.Write(Segments.Count);
            foreach (var segment in Segments)
            {
                writer.Write(segment.Name.Length);
                writer.Write(segment.Name.ToCharArray());  // <-- Исправлено
                writer.Write(segment.Length);
                writer.Write(segment.Thickness);
                writer.Write(segment.AngleToNext);
                writer.Write(segment.AngleLocked);
                writer.Write(segment.LengthLocked);
                var c = segment.Color is SolidColorBrush sc ? sc.Color : Colors.Black;
                writer.Write(c.A);
                writer.Write(c.R);
                writer.Write(c.G);
                writer.Write(c.B);
            }
        }
        public override void Load(BinaryReader reader)
        {
            base.Load(reader);
            InitialDirection = reader.ReadDouble();
            IsClosed = reader.ReadBoolean();
            int segmentCount = reader.ReadInt32();
            Segments.Clear();
            SidesCount = 0;
            for (int i = 0; i < segmentCount; i++)
            {
                var segment = new LineSegment();
                int nameLen = reader.ReadInt32();
                segment.Name = new string(reader.ReadChars(nameLen));
                segment.Length = reader.ReadDouble();
                segment.Thickness = reader.ReadDouble();
                segment.AngleToNext = reader.ReadDouble();
                segment.AngleLocked = reader.ReadBoolean();
                segment.LengthLocked = reader.ReadBoolean();
                byte a = reader.ReadByte();
                byte r = reader.ReadByte();
                byte g = reader.ReadByte();
                byte b = reader.ReadByte();
                segment.Color = new SolidColorBrush(Color.FromArgb(a, r, g, b));
                Segments.Add(segment);
                SidesCount++;
            }
            RebuildVertices();
        }
    }
}