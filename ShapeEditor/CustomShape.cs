using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Text.Json;

namespace ShapeEditor
{
    /// <summary>
    /// Пользовательская фигура из отрезков (ломаная линия).
    /// Может быть замкнутой (IsClosed) или разомкнутой.
    /// </summary>
    public class CustomShape : ShapeBase
    {
        public List<LineSegment> Segments { get; set; } = new();
        public double InitialDirection { get; set; } = 0.0;
        public bool IsClosed { get; set; } = false;

        public CustomShape()
        {
            SidesCount = 0;
            Vertices = new Point[0];
        }

        public override string DisplayNameRu => "Пользовательская";
        protected override Point[] GetDefaultVertices() => new Point[0];

        #region Управление сегментами

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
                Segments[i].Length = lengths[i];
            RebuildVertices();
            return true;
        }

        #endregion

        #region Центрирование

        /// <summary>
        /// Центрирует якорь по границам фигуры. Возвращает смещение.
        /// </summary>
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

        #endregion

        #region Рендеринг

        public override Canvas Build(double anchorWorldX, double anchorWorldY)
        {
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

                Canvas.SetLeft(placeholder, anchorWorldX - (AnchorPoint.X * Scale) + MinX);
                Canvas.SetTop(placeholder, anchorWorldY - (AnchorPoint.Y * Scale) + MinY);
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

            bool isClosed = this.IsClosed && transformed.Count >= 3;
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

            // Заливка
            var fillPoly = new Polygon
            {
                Points = new PointCollection(inner.Select(p => new Point(p.X - minX, p.Y - minY))),
                Fill = Fill,
                Stroke = null,
                Tag = -1
            };
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
                    Fill = colors[i] ?? Brushes.Black,
                    Stroke = null,
                    Tag = i
                };
                canvas.Children.Add(sidePoly);
            }

            // Якорь
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

        #endregion

        #region JSON Сохранение / Загрузка

        public override void SaveToJson(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "CustomShape");
            writer.WriteNumber("id", Id);
            writer.WriteString("displayName", DisplayNameRu);
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

            // === СОХРАНЯЕМ ВЕРШИНЫ ===
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
        }

        public override void LoadFromJson(JsonElement element)
        {
            if (element.TryGetProperty("id", out var idProp)) Id = idProp.GetInt32();
            if (element.TryGetProperty("scale", out var sProp)) Scale = sProp.GetDouble();
            if (element.TryGetProperty("angle", out var aProp)) Angle = aProp.GetDouble();
            if (element.TryGetProperty("anchorX", out var axProp) && element.TryGetProperty("anchorY", out var ayProp))
                AnchorPoint = new Point(axProp.GetDouble(), ayProp.GetDouble());
            if (element.TryGetProperty("fill", out var fProp)) Fill = ParseColor(fProp.GetString());
            if (element.TryGetProperty("initialDirection", out var idrProp)) InitialDirection = idrProp.GetDouble();
            if (element.TryGetProperty("isClosed", out var icProp)) IsClosed = icProp.GetBoolean();

            SideColors.Clear();
            if (element.TryGetProperty("sideColors", out var colorsProp))
                foreach (var c in colorsProp.EnumerateArray()) SideColors.Add(ParseColor(c.GetString()));

            SideThickness.Clear();
            if (element.TryGetProperty("sideThicknesses", out var thickProp))
                foreach (var t in thickProp.EnumerateArray()) SideThickness.Add(t.GetDouble());

            EdgeLengthLocked.Clear();
            if (element.TryGetProperty("edgeLocks", out var locksProp))
                foreach (var l in locksProp.EnumerateArray()) EdgeLengthLocked.Add(l.GetBoolean());

            Segments.Clear();
            SidesCount = 0;
            if (element.TryGetProperty("segments", out var segProp))
            {
                foreach (var s in segProp.EnumerateArray())
                {
                    var segment = new LineSegment
                    {
                        Name = s.GetProperty("name").GetString(),
                        Length = s.GetProperty("length").GetDouble(),
                        Thickness = s.GetProperty("thickness").GetDouble(),
                        AngleToNext = s.GetProperty("angleToNext").GetDouble(),
                        AngleLocked = s.GetProperty("angleLocked").GetBoolean(),
                        LengthLocked = s.GetProperty("lengthLocked").GetBoolean()
                    };
                    var c = s.GetProperty("color").GetString();
                    segment.Color = ParseColor(c);
                    Segments.Add(segment);
                    SidesCount++;
                }
            }
            // НЕ вызываем RebuildVertices() здесь! 
            // Вершины будут восстановлены из сохранённых данных через SaveToJson,
            // который сохраняет Vertices через вызов Build.
            // Вместо этого загружаем сохранённые вершины если они есть.
            if (element.TryGetProperty("vertices", out var vertsProp))
            {
                var verts = new List<Point>();
                foreach (var v in vertsProp.EnumerateArray())
                    verts.Add(new Point(v.GetProperty("x").GetDouble(), v.GetProperty("y").GetDouble()));
                Vertices = verts.ToArray();
            }
            else
            {
                // Fallback: если vertices нет в JSON (старый формат)
                RebuildVertices();
            }
        }

        #endregion
    }
}