using ShapeEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ShapeEditor
{
    /// <summary>
    /// Абстрактный базовый класс для всех фигур редактора.
    /// Поддерживает только два типа базовых фигур: PolygonShape (многоугольники) и EllipseShape (эллипсы/окружности).
    /// Каждая фигура самостоятельно реализует SaveToJson/LoadFromJson.
    /// </summary>
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
        public double MaxX { get; protected set; }
        public double MaxY { get; protected set; }

        public List<bool> EdgeLengthLocked { get; set; } = new();

        // Отображаемое имя на русском (переопределяется в наследниках)
        public virtual string DisplayNameRu => "Фигура";

        private static int _globalIdCounter = 0;

        // Уникальный числовой ID (присваивается при создании)
        public int Id { get; set; }

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
            EdgeLengthLocked = new List<bool>(Vertices.Length);
            for (int i = 0; i < Vertices.Length; i++) EdgeLengthLocked.Add(false);
            Id = ++_globalIdCounter;
        }

        protected abstract Point[] GetDefaultVertices();

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
        /// Притягивает точку к центральной линии ближайшего ребра.
        /// </summary>
        public virtual Point SnapToEdgeCenter(Point point)
        {
            if (this is EllipseShape)
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

        // Проверка, находится ли точка внутри фигуры
        public virtual bool IsPointInside(Point localPoint)
        {
            if (Vertices.Length < 3) return false;

            Point[] rotatedVerts = GetRotatedVertices();
            Point[] scaledVertices = new Point[rotatedVerts.Length];
            for (int i = 0; i < rotatedVerts.Length; i++)
                scaledVertices[i] = new Point(rotatedVerts[i].X * Scale, rotatedVerts[i].Y * Scale);

            Point scaledPoint = new Point(localPoint.X * Scale, localPoint.Y * Scale);

            bool inside = PointInPolygon(scaledPoint, scaledVertices);
            if (inside) return true;

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

        protected static bool SegmentsIntersect(Point a1, Point a2, Point b1, Point b2)
        {
            bool OnSegment(Point p, Point q, Point r)
            {
                return q.X <= Math.Max(p.X, r.X) && q.X >= Math.Min(p.X, r.X) &&
                       q.Y <= Math.Max(p.Y, r.Y) && q.Y >= Math.Min(p.Y, r.Y);
            }

            int Orientation(Point p, Point q, Point r)
            {
                double val = (q.Y - p.Y) * (r.X - q.X) - (q.X - p.X) * (r.Y - q.Y);
                if (Math.Abs(val) < 1e-9) return 0;
                return (val > 0) ? 1 : 2;
            }

            int o1 = Orientation(a1, a2, b1);
            int o2 = Orientation(a1, a2, b2);
            int o3 = Orientation(b1, b2, a1);
            int o4 = Orientation(b1, b2, a2);

            if (o1 != o2 && o3 != o4) return true;

            if (o1 == 0 && OnSegment(a1, b1, a2)) return true;
            if (o2 == 0 && OnSegment(a1, b2, a2)) return true;
            if (o3 == 0 && OnSegment(b1, a1, b2)) return true;
            if (o4 == 0 && OnSegment(b1, a2, b2)) return true;

            return false;
        }

        protected bool IsSimplePolygon(Point[] verts)
        {
            int n = verts.Length;
            for (int i = 0; i < n; i++)
            {
                Point a1 = verts[i];
                Point a2 = verts[(i + 1) % n];
                for (int j = i + 1; j < n; j++)
                {
                    if (Math.Abs(i - j) <= 1) continue;
                    if (i == 0 && j == n - 1) continue;

                    Point b1 = verts[j];
                    Point b2 = verts[(j + 1) % n];

                    if (SegmentsIntersect(a1, a2, b1, b2))
                        return false;
                }
            }
            return true;
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

        /// <summary>
        /// Возвращает координаты углов ограничивающего прямоугольника в мировых координатах холста.
        /// </summary>
        public (Point bottomLeft, Point topRight) GetBoundingBoxCorners(Canvas canvas)
        {
            double left = Canvas.GetLeft(canvas);
            double top = Canvas.GetTop(canvas);
            double width = MaxX - MinX;
            double height = MaxY - MinY;

            Point bottomLeft = new Point(left, top + height);
            Point topRight = new Point(left + width, top);

            return (bottomLeft, topRight);
        }

        /// <summary>
        /// Возвращает длину ребра с указанным индексом (в немасштабированных локальных координатах)
        /// </summary>
        public virtual double GetEdgeLength(int edgeIndex)
        {
            if (edgeIndex < 0 || edgeIndex >= Vertices.Length)
                return 0;

            Point p1 = Vertices[edgeIndex];
            Point p2 = Vertices[(edgeIndex + 1) % Vertices.Length];

            Vector diff = p2 - p1;
            return diff.Length;
        }

        /// <summary>
        /// Изменяет длину ребра, перемещая вторую вершину (конец ребра)
        /// </summary>
        public virtual void SetEdgeLength(int edgeIndex, double newLength)
        {
            if (edgeIndex < 0 || edgeIndex >= Vertices.Length || newLength <= 0)
                return;

            if (EdgeLengthLocked != null && edgeIndex < EdgeLengthLocked.Count && EdgeLengthLocked[edgeIndex])
                return;

            int n = Vertices.Length;
            Point p1 = Vertices[edgeIndex];
            Point p2 = Vertices[(edgeIndex + 1) % n];

            Vector diff = p2 - p1;
            double currentLength = diff.Length;

            if (currentLength < 1e-6)
                return;

            Vector direction = diff / currentLength;
            Point newP2 = p1 + direction * newLength;

            Point[] newVerts = (Point[])Vertices.Clone();
            newVerts[(edgeIndex + 1) % n] = newP2;
            if (!IsSimplePolygon(newVerts))
                return;

            Vertices[(edgeIndex + 1) % n] = newP2;
        }

        /// <summary>
        /// Попытаться установить длины всех рёбер одновременно.
        /// </summary>
        public virtual bool TrySetEdgeLengths(double[] lengths)
        {
            if (lengths == null || lengths.Length != Vertices.Length) return false;

            for (int i = 0; i < lengths.Length; i++)
            {
                if (lengths[i] <= 0) return false;
                if (EdgeLengthLocked != null && i < EdgeLengthLocked.Count && EdgeLengthLocked[i])
                    continue;
            }

            var backup = (Point[])Vertices.Clone();
            for (int i = 0; i < lengths.Length; i++)
            {
                if (EdgeLengthLocked != null && i < EdgeLengthLocked.Count && EdgeLengthLocked[i])
                    continue;
                SetEdgeLength(i, lengths[i]);
            }

            return true;
        }

        /// <summary>
        /// Строит визуальное представление фигуры на Canvas.
        /// </summary>
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

            // Масштабируем
            Point[] vertices = new Point[n];
            for (int i = 0; i < n; i++)
                vertices[i] = new Point(rotatedVertices[i].X * Scale, rotatedVertices[i].Y * Scale);

            // Единичные векторы и нормали
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

            // Bounding box
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

            MinX = minX; MinY = minY;
            MaxX = maxX; MaxY = maxY;

            Canvas canvas = new Canvas { Width = width, Height = height };

            // Заливка
            Polygon fillPoly = new Polygon
            {
                Points = new PointCollection(),
                Fill = Fill,
                Stroke = null,
                Tag = -1
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
                    Tag = i
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
                Fill = Brushes.White,
                Stroke = Brushes.Purple,
                StrokeThickness = 1,
                Tag = "Anchor"
            };
            Canvas.SetLeft(anchorDot, anchorLocalX - minX - 5);
            Canvas.SetTop(anchorDot, anchorLocalY - minY - 5);
            canvas.Children.Add(anchorDot);

            // Вершины — невидимые, но кликабельные
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

            // Позиционируем canvas
            Canvas.SetLeft(canvas, anchorWorldX - anchorLocalX + minX);
            Canvas.SetTop(canvas, anchorWorldY - anchorLocalY + minY);

            return canvas;
        }

        // ============================================================
        // АБСТРАКТНЫЕ МЕТОДЫ СОХРАНЕНИЯ / ЗАГРУЗКИ JSON
        // Каждая фигура реализует самостоятельно
        // ============================================================

        /// <summary>
        /// Сохраняет фигуру в JSON-формате через Utf8JsonWriter.
        /// Каждый наследник реализует самостоятельно.
        /// </summary>
        public abstract void SaveToJson(Utf8JsonWriter writer);

        /// <summary>
        /// Загружает фигуру из JSON-элемента.
        /// Каждый наследник реализует самостоятельно.
        /// </summary>
        public abstract void LoadFromJson(JsonElement element);

        /// <summary>
        /// Сохраняет фигуру в JSON-файл (вспомогательный метод).
        /// </summary>
        public void SaveToFile(string filename)
        {
            using var stream = File.Create(filename);
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            SaveToJson(writer);
            writer.Flush();
        }

        /// <summary>
        /// Загружает фигуру из JSON-файла (вспомогательный статический метод).
        /// </summary>
        public static ShapeBase LoadFromFile(string filename)
        {
            string jsonString = File.ReadAllText(filename);
            using var doc = JsonDocument.Parse(jsonString);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
                throw new InvalidDataException("JSON не содержит поле 'type'");

            string typeName = typeProp.GetString();
            ShapeBase shape = typeName switch
            {
                "PolygonShape" => new PolygonShape(),
                "EllipseShape" => new EllipseShape(),
                "CustomShape" => new CustomShape(),
                "CompoundShape" => new CompoundShape(),
                _ => throw new InvalidDataException($"Неизвестный тип фигуры: {typeName}")
            };

            shape.LoadFromJson(root);
            return shape;
        }

        // ============================================================
        // УСТАРЕВШИЕ МЕТОДЫ БИНАРНОЙ СЕРИАЛИЗАЦИИ (для совместимости)
        // ============================================================

        public virtual void Save(BinaryWriter writer)
        {
            string typeName = GetType().Name;
            writer.Write(typeName.Length);
            writer.Write(typeName.ToCharArray());

            writer.Write(Id);
            writer.Write(Scale);
            writer.Write(Angle);
            writer.Write(AnchorPoint.X);
            writer.Write(AnchorPoint.Y);

            var fillColor = Fill is SolidColorBrush scb ? scb.Color : Colors.Transparent;
            writer.Write(fillColor.A);
            writer.Write(fillColor.R);
            writer.Write(fillColor.G);
            writer.Write(fillColor.B);

            writer.Write(SideColors.Count);
            for (int i = 0; i < SideColors.Count; i++)
            {
                var c = SideColors[i] is SolidColorBrush sc ? sc.Color : Colors.Black;
                writer.Write(c.A); writer.Write(c.R); writer.Write(c.G); writer.Write(c.B);
            }

            writer.Write(SideThickness.Count);
            for (int i = 0; i < SideThickness.Count; i++)
                writer.Write(SideThickness[i]);

            writer.Write(EdgeLengthLocked.Count);
            for (int i = 0; i < EdgeLengthLocked.Count; i++)
                writer.Write(EdgeLengthLocked[i]);

            writer.Write(Vertices.Length);
            for (int i = 0; i < Vertices.Length; i++)
            {
                writer.Write(Vertices[i].X);
                writer.Write(Vertices[i].Y);
            }

            writer.Write(SidesCount);
        }

        public virtual void Load(BinaryReader reader)
        {
            Id = reader.ReadInt32();
            var field = typeof(ShapeBase).GetField("_globalIdCounter",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static);
            if (field != null)
            {
                int currentCounter = (int)field.GetValue(null);
                if (Id > currentCounter) field.SetValue(null, Id);
            }

            Scale = reader.ReadDouble();
            Angle = reader.ReadDouble();
            AnchorPoint = new Point(reader.ReadDouble(), reader.ReadDouble());

            byte a = reader.ReadByte(), r = reader.ReadByte(), g = reader.ReadByte(), b = reader.ReadByte();
            Fill = new SolidColorBrush(Color.FromArgb(a, r, g, b));

            int colorCount = reader.ReadInt32();
            SideColors.Clear();
            for (int i = 0; i < colorCount; i++)
            {
                byte ca = reader.ReadByte(), cr = reader.ReadByte(), cg = reader.ReadByte(), cb = reader.ReadByte();
                SideColors.Add(new SolidColorBrush(Color.FromArgb(ca, cr, cg, cb)));
            }

            int thickCount = reader.ReadInt32();
            SideThickness.Clear();
            for (int i = 0; i < thickCount; i++)
                SideThickness.Add(reader.ReadDouble());

            int lockCount = reader.ReadInt32();
            EdgeLengthLocked.Clear();
            for (int i = 0; i < lockCount; i++)
                EdgeLengthLocked.Add(reader.ReadBoolean());

            int vertCount = reader.ReadInt32();
            Vertices = new Point[vertCount];
            for (int i = 0; i < vertCount; i++)
                Vertices[i] = new Point(reader.ReadDouble(), reader.ReadDouble());

            SidesCount = reader.ReadInt32();
        }

        // Вспомогательные методы для цветов
        protected static string GetColorHex(Brush brush)
        {
            if (brush is SolidColorBrush scb)
                return $"#{scb.Color.A:X2}{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B:X2}";
            return "#00FFFFFF";
        }

        protected static Brush ParseColor(string hex)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return Brushes.Black;
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch { return Brushes.Black; }
        }
    }
}