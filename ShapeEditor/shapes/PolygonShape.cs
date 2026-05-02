using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.IO;

namespace ShapeEditor.shapes
{
    /// <summary>
    /// Базовый класс для всех многоугольников: прямоугольник, треугольник, трапеция, шестиугольник и т.д.
    /// </summary>
    public class PolygonShape : ShapeBase
    {
        public PolygonShape()
        {
        }

        public override string DisplayNameRu => "Многоугольник";

        protected override Point[] GetDefaultVertices() => new Point[0];

        public override bool IsPointInside(Point localPoint)
        {
            if (Vertices == null || Vertices.Length < 3) return false;
            return PointInPolygon(localPoint, Vertices);
        }

        // --- Статические фабричные методы для создания конкретных фигур ---

        /// <summary>
        /// Создаёт прямоугольник с заданными шириной и высотой
        /// </summary>
        public static PolygonShape CreateRectangle(double width, double height)
        {
            double halfW = width / 2.0;
            double halfH = height / 2.0;
            var shape = new PolygonShape
            {
                SidesCount = 4,
                Vertices = new Point[]
                {
                    new Point(-halfW, -halfH), // левый верхний
                    new Point(halfW, -halfH),  // правый верхний
                    new Point(halfW, halfH),   // правый нижний
                    new Point(-halfW, halfH)   // левый нижний
                }
            };
            shape.InitializeDefaults();
            return shape;
        }

        /// <summary>
        /// Создаёт равносторонний треугольник с заданной стороной
        /// </summary>
        public static PolygonShape CreateTriangle(double sideLength)
        {
            double h = sideLength * Math.Sqrt(3) / 2.0;
            double halfSide = sideLength / 2.0;
            var shape = new PolygonShape
            {
                SidesCount = 3,
                Vertices = new Point[]
                {
                    new Point(0, -h * 2.0 / 3.0),      // верхняя вершина
                    new Point(halfSide, h / 3.0),       // правая нижняя
                    new Point(-halfSide, h / 3.0)       // левая нижняя
                }
            };
            shape.InitializeDefaults();
            return shape;
        }

        /// <summary>
        /// Создаёт трапецию с заданными параметрами
        /// </summary>
        public static PolygonShape CreateTrapezoid(double topWidth, double bottomWidth, double height)
        {
            double halfTop = topWidth / 2.0;
            double halfBottom = bottomWidth / 2.0;
            double halfH = height / 2.0;
            var shape = new PolygonShape
            {
                SidesCount = 4,
                Vertices = new Point[]
                {
                    new Point(-halfTop, -halfH),   // левая верхняя
                    new Point(halfTop, -halfH),    // правая верхняя
                    new Point(halfBottom, halfH),  // правая нижняя
                    new Point(-halfBottom, halfH)  // левая нижняя
                }
            };
            shape.InitializeDefaults();
            return shape;
        }

        /// <summary>
        /// Создаёт правильный шестиугольник с заданным радиусом
        /// </summary>
        public static PolygonShape CreateHexagon(double radius)
        {
            var vertices = new List<Point>();
            for (int i = 0; i < 6; i++)
            {
                double angle = i * Math.PI / 3.0; // 60°
                vertices.Add(new Point(radius * Math.Cos(angle), radius * Math.Sin(angle)));
            }
            var shape = new PolygonShape
            {
                SidesCount = 6,
                Vertices = vertices.ToArray()
            };
            shape.InitializeDefaults();
            return shape;
        }

        /// <summary>
        /// Создаёт правильный многоугольник с N сторонами
        /// </summary>
        public static PolygonShape CreateRegularPolygon(int sides, double radius)
        {
            if (sides < 3) sides = 3;
            var vertices = new List<Point>();
            for (int i = 0; i < sides; i++)
            {
                double angle = 2.0 * Math.PI * i / sides - Math.PI / 2.0; // начинаем сверху
                vertices.Add(new Point(radius * Math.Cos(angle), radius * Math.Sin(angle)));
            }
            var shape = new PolygonShape
            {
                SidesCount = sides,
                Vertices = vertices.ToArray()
            };
            shape.InitializeDefaults();
            return shape;
        }

        private void InitializeDefaults()
        {
            SideColors.Clear();
            SideThickness.Clear();
            EdgeLengthLocked.Clear();
            for (int i = 0; i < SidesCount; i++)
            {
                SideColors.Add(Brushes.Black);
                SideThickness.Add(3.0);
                EdgeLengthLocked.Add(false);
            }
        }

        // --- Переопределённые методы для работы с рёбрами ---

        public override void SetEdgeLength(int edgeIndex, double newLength)
        {
            if (edgeIndex < 0 || edgeIndex >= Vertices.Length || newLength <= 0) return;
            if (EdgeLengthLocked != null && edgeIndex < EdgeLengthLocked.Count && EdgeLengthLocked[edgeIndex]) return;

            int n = Vertices.Length;
            Point p1 = Vertices[edgeIndex];
            Point p2 = Vertices[(edgeIndex + 1) % n];

            Vector diff = p2 - p1;
            double currentLength = diff.Length;
            if (currentLength < 1e-6) return;

            Vector direction = diff / currentLength;
            Point newP2 = p1 + direction * newLength;

            Point[] newVerts = (Point[])Vertices.Clone();
            newVerts[(edgeIndex + 1) % n] = newP2;

            if (!IsSimplePolygon(newVerts)) return;
            Vertices = newVerts;
        }

        public override bool TrySetEdgeLengths(double[] lengths)
        {
            if (lengths == null || lengths.Length != Vertices.Length) return false;

            var backup = (Point[])Vertices.Clone();
            for (int i = 0; i < lengths.Length; i++)
            {
                if (lengths[i] <= 0) return false;
                if (EdgeLengthLocked != null && i < EdgeLengthLocked.Count && EdgeLengthLocked[i])
                    continue;
                SetEdgeLength(i, lengths[i]);
            }
            return true;
        }

        // --- Рендеринг ---

        public override Canvas Build(double anchorWorldX, double anchorWorldY)
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

            // Поворот вокруг якоря
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

            // Масштабирование
            Point[] scaledVerts = new Point[n];
            for (int i = 0; i < n; i++)
                scaledVerts[i] = new Point(rotatedVertices[i].X * Scale, rotatedVertices[i].Y * Scale);

            // Единичные векторы и нормали
            Vector[] e = new Vector[n];
            Vector[] nVec = new Vector[n];
            for (int i = 0; i < n; i++)
            {
                Vector v = scaledVerts[(i + 1) % n] - scaledVerts[i];
                double len = v.Length;
                if (len < 1e-6) len = 1;
                e[i] = v / len;
                nVec[i] = new Vector(e[i].Y, -e[i].X);
            }

            // Внешние и внутренние точки для толщины линий
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
                    outer[i] = scaledVerts[i] + dPrev * nVec[prev];
                    inner[i] = scaledVerts[i] - dPrev * nVec[prev];
                }
                else
                {
                    double u = (C.X * e[i].Y - C.Y * e[i].X) / det;
                    outer[i] = scaledVerts[i] + dPrev * nVec[prev] + u * e[prev];
                    inner[i] = scaledVerts[i] - dPrev * nVec[prev] - u * e[prev];
                }
            }

            // Bounding box
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
                    Fill = colors[i],
                    Stroke = null,
                    Tag = i
                };
                canvas.Children.Add(sidePoly);
            }

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
            Canvas.SetLeft(anchorDot, anchorLocalX - minX - 5);
            Canvas.SetTop(anchorDot, anchorLocalY - minY - 5);
            canvas.Children.Add(anchorDot);

            // Вершины (невидимые, кликабельные)
            for (int i = 0; i < n; i++)
            {
                var vertexDot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = Brushes.Transparent,
                    Stroke = Brushes.Transparent,
                    StrokeThickness = 0,
                    Tag = i
                };
                Canvas.SetLeft(vertexDot, scaledVerts[i].X - minX - 4);
                Canvas.SetTop(vertexDot, scaledVerts[i].Y - minY - 4);
                canvas.Children.Add(vertexDot);
            }

            // Позиционирование
            Canvas.SetLeft(canvas, anchorWorldX - anchorLocalX + minX);
            Canvas.SetTop(canvas, anchorWorldY - anchorLocalY + minY);

            return canvas;
        }

        // --- Сохранение / Загрузка ---

        public override void Save(BinaryWriter writer)
        {
            // Тип
            string typeName = GetType().Name;
            writer.Write(typeName.Length);
            writer.Write(typeName.ToCharArray());

            // Базовые параметры
            writer.Write(Id);
            writer.Write(Scale);
            writer.Write(Angle);
            writer.Write(AnchorPoint.X);
            writer.Write(AnchorPoint.Y);

            // Заливка
            var fillColor = Fill is SolidColorBrush scb ? scb.Color : Colors.Transparent;
            writer.Write(fillColor.A); writer.Write(fillColor.R);
            writer.Write(fillColor.G); writer.Write(fillColor.B);

            // Цвета сторон
            writer.Write(SideColors.Count);
            for (int i = 0; i < SideColors.Count; i++)
            {
                var c = SideColors[i] is SolidColorBrush sc ? sc.Color : Colors.Black;
                writer.Write(c.A); writer.Write(c.R);
                writer.Write(c.G); writer.Write(c.B);
            }

            // Толщины
            writer.Write(SideThickness.Count);
            for (int i = 0; i < SideThickness.Count; i++)
                writer.Write(SideThickness[i]);

            // Блокировки
            writer.Write(EdgeLengthLocked.Count);
            for (int i = 0; i < EdgeLengthLocked.Count; i++)
                writer.Write(EdgeLengthLocked[i]);

            // Вершины
            writer.Write(Vertices.Length);
            for (int i = 0; i < Vertices.Length; i++)
            {
                writer.Write(Vertices[i].X);
                writer.Write(Vertices[i].Y);
            }

            writer.Write(SidesCount);

            // Имя фигуры (для идентификации типа при загрузке)
            writer.Write(DisplayNameRu.Length);
            writer.Write(DisplayNameRu.ToCharArray());
        }

        public override void Load(BinaryReader reader)
        {
            Id = reader.ReadInt32();
            var field = typeof(ShapeBase).GetField("_globalIdCounter",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
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

            // Читаем DisplayNameRu (для совместимости)
            int nameLen = reader.ReadInt32();
            if (nameLen > 0) reader.ReadChars(nameLen);
        }
    }
}