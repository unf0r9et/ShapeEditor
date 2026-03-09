using System;
using System.Windows;

namespace ShapeEditor
{
    public class TrapezoidShape : ShapeBase
    {
        public TrapezoidShape()
        {
            SidesCount = 4;
        }

        public override string[] SideNames => new[] { "Верхняя", "Правая боковая", "Нижняя", "Левая боковая" };

        protected override Point[] GetDefaultVertices()
        {
            // Равнобокая трапеция, центр в (0,0)
            return new Point[]
            {
                new Point(-40, -40), // V0: левая верхняя
                new Point( 40, -40), // V1: правая верхняя
                new Point( 70,  40), // V2: правая нижняя
                new Point(-70,  40)  // V3: левая нижняя
            };
        }

        public override void SetEdgeLength(int edgeIndex, double newLength)
        {
            if (edgeIndex < 0 || edgeIndex >= Vertices.Length || newLength <= 0) return;
            if (EdgeLengthLocked != null && edgeIndex < EdgeLengthLocked.Count && EdgeLengthLocked[edgeIndex]) return;
            if (Vertices.Length != 4) { base.SetEdgeLength(edgeIndex, newLength); return; }

            Point[] nv = (Point[])Vertices.Clone();

            // Локальные минимальные изменения: боковые изменения меняют только Y нижних вершин; основания — X-координаты симметрично
            if (edgeIndex == 1)
            {
                Point top = nv[1];
                Point bottom = nv[2];
                double dx = bottom.X - top.X;
                double sq = newLength * newLength - dx * dx;
                if (sq < -1e-6) return;
                double dy = sq <= 0 ? 0 : Math.Sqrt(Math.Max(0, sq));
                double dir = Math.Sign(bottom.Y - top.Y); if (dir == 0) dir = 1;
                nv[2] = new Point(bottom.X, top.Y + dir * dy);
                if (!IsSimplePolygon(nv)) return;
                Vertices = nv;
                return;
            }

            if (edgeIndex == 3)
            {
                Point top = nv[0];
                Point bottom = nv[3];
                double dx = bottom.X - top.X;
                double sq = newLength * newLength - dx * dx;
                if (sq < -1e-6) return;
                double dy = sq <= 0 ? 0 : Math.Sqrt(Math.Max(0, sq));
                double dir = Math.Sign(bottom.Y - top.Y); if (dir == 0) dir = 1;
                nv[3] = new Point(bottom.X, top.Y + dir * dy);
                if (!IsSimplePolygon(nv)) return;
                Vertices = nv;
                return;
            }

            if (edgeIndex == 0)
            {
                double half = newLength / 2.0;
                double x0 = Vertices[0].X; // сохраняем левую точку верхнего основания
                double y = Vertices[0].Y;
                nv[0] = new Point(x0, y);
                nv[1] = new Point(x0 + newLength, y);
                if (!IsSimplePolygon(nv)) return;
                Vertices = nv;
                return;
            }

            if (edgeIndex == 2)
            {
                double half = newLength / 2.0;
                double x3 = Vertices[3].X; // сохраняем левую точку нижнего основания
                double y = Vertices[2].Y;
                nv[3] = new Point(x3, y);
                nv[2] = new Point(x3 + newLength, y);
                if (!IsSimplePolygon(nv)) return;
                Vertices = nv;
                return;
            }

            base.SetEdgeLength(edgeIndex, newLength);
        }

        /// <summary>
        /// Попытаться установить все длины рёбер трапеции одновременно.
        /// lengths: [верхняя, правая боковая, нижняя, левая боковая]
        /// Сохраняет параллельность оснований, позволяет неравнобедренную трапецию.
        /// </summary>
        public override bool TrySetEdgeLengths(double[] lengths)
        {
            if (lengths == null || lengths.Length != 4) return false;
            while (EdgeLengthLocked.Count < 4) EdgeLengthLocked.Add(false);

            double a = EdgeLengthLocked[0] ? GetEdgeLength(0) : lengths[0];   // верхняя
            double br = EdgeLengthLocked[1] ? GetEdgeLength(1) : lengths[1];  // правая боковая
            double c = EdgeLengthLocked[2] ? GetEdgeLength(2) : lengths[2];   // нижняя
            double bl = EdgeLengthLocked[3] ? GetEdgeLength(3) : lengths[3];  // левая боковая

            if (a <= 0 || br <= 0 || c <= 0 || bl <= 0) return false;

            Point[] nv = (Point[])Vertices.Clone();

            // Сохраняем левую X верхнего основания (Anchoring behavior) чтобы не «переставлять» фигуру неожиданно
            double x0 = Vertices[0].X;
            double x1 = x0 + a;
            double topY = Vertices[0].Y;

            nv[0] = new Point(x0, topY);
            nv[1] = new Point(x1, topY);

            // Решаем для смещения s = v3x - x0
            double alpha = c - a; // c - a
            double D = br * br - bl * bl;

            double s;
            if (Math.Abs(alpha) < 1e-9)
            {
                // a == c: базовые длины равны
                // Тогда можно попробовать сохранить v3x как есть
                s = nv[3].X - x0;
                // Но нужно проверить совместимость
            }
            else
            {
                s = (br * br - bl * bl - alpha * alpha) / (2.0 * alpha);
            }

            double v3x = x0 + s;
            double v2x = v3x + c;

            double dxR = v2x - x1;
            double dxL = v3x - x0;
            double sqR = br * br - dxR * dxR;
            double sqL = bl * bl - dxL * dxL;
            if (sqR < -1e-6 || sqL < -1e-6) return false;

            double dyR = sqR <= 0 ? 0 : Math.Sqrt(Math.Max(0, sqR));
            double dyL = sqL <= 0 ? 0 : Math.Sqrt(Math.Max(0, sqL));
            if (Math.Abs(dyR - dyL) > 1e-6) return false; // несовместимо
            double dy = dyR;

            double dir = Math.Sign(Vertices[2].Y - Vertices[0].Y); if (dir == 0) dir = 1;
            double bottomY = topY + dir * dy;

            nv[2] = new Point(v2x, bottomY);
            nv[3] = new Point(v3x, bottomY);

            if (!IsSimplePolygon(nv)) return false;

            Vertices = nv;
            return true;
        }
    }
}