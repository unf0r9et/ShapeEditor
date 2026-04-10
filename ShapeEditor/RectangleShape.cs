using System;
using System.Windows;

namespace ShapeEditor
{
    public class RectangleShape : ShapeBase
    {
        public RectangleShape()
        {
            SidesCount = 4;
        }
        // RectangleShape.cs
        public override string DisplayNameRu => "Прямоугольник";
        public override string[] SideNames => new[] { "Верхняя", "Правая", "Нижняя", "Левая" };

        protected override Point[] GetDefaultVertices()
        {
            return new Point[]
            {
                new Point(-60, -40), // левый верхний
                new Point( 60, -40), // правый верхний
                new Point( 60,  40), // правый нижний
                new Point(-60,  40)  // левый нижний
            };
        }

        public override void SetEdgeLength(int edgeIndex, double newLength)
        {
            if (edgeIndex < 0 || edgeIndex >= Vertices.Length || newLength <= 0) return;
            if (Vertices.Length != 4) { base.SetEdgeLength(edgeIndex, newLength); return; }

            // respect locks
            if (EdgeLengthLocked != null && edgeIndex < EdgeLengthLocked.Count && EdgeLengthLocked[edgeIndex]) return;

            // Стороны: 0 top (V0-V1), 1 right (V1-V2), 2 bottom (V2-V3), 3 left (V3-V0)
            Point[] nv = (Point[])Vertices.Clone();

            if (edgeIndex == 0 || edgeIndex == 2)
            {
                // меняем верх/низ, сохраняем центр и высоту
                double half = newLength / 2.0;
                // центральная ось определяется по текущим топ/боттом
                double centerX = (Vertices[0].X + Vertices[1].X) / 2.0;
                double topY = Vertices[0].Y;
                double bottomY = Vertices[2].Y; // сохраняем высоту
                double halfH = (bottomY - topY) / 2.0;

                nv[0] = new Point(centerX - half, topY);
                nv[1] = new Point(centerX + half, topY);
                nv[2] = new Point(centerX + half, topY + 2 * halfH);
                nv[3] = new Point(centerX - half, topY + 2 * halfH);

                if (!IsSimplePolygon(nv)) return;
                Vertices = nv;
                return;
            }
            else
            {
                // меняем левую/правую — сохраняем центр по Y и ширину
                double half = newLength / 2.0;
                double centerY = (Vertices[1].Y + Vertices[2].Y) / 2.0;
                double leftX = Vertices[0].X;
                double rightX = Vertices[1].X;
                double halfW = (rightX - leftX) / 2.0;

                nv[1] = new Point(rightX, centerY - halfW + half);
                nv[2] = new Point(rightX, centerY + halfW - half);
                nv[0] = new Point(leftX, centerY - halfW + half);
                nv[3] = new Point(leftX, centerY + halfW - half);

                // The above is incorrect in general; simplify: when changing side length on rectangle, rebuild rectangle keeping center.
                double currentWidth = (Vertices[1] - Vertices[0]).Length;
                double currentHeight = (Vertices[2] - Vertices[1]).Length;
                if (currentWidth < 1e-6 || currentHeight < 1e-6) return;

                double targetWidth = currentWidth;
                double targetHeight = currentHeight;

                if (edgeIndex == 0 || edgeIndex == 2) targetWidth = newLength;
                else targetHeight = newLength;

                // center
                Point c = new Point((Vertices[0].X + Vertices[1].X + Vertices[2].X + Vertices[3].X) / 4.0,
                                    (Vertices[0].Y + Vertices[1].Y + Vertices[2].Y + Vertices[3].Y) / 4.0);
                // take width axis from top edge
                Vector u = Vertices[1] - Vertices[0];
                u.Normalize();
                Vector v = new Vector(-u.Y, u.X);
                if (Vector.Multiply(v, Vertices[2] - Vertices[1]) < 0) v = -v;

                double halfWn = targetWidth / 2.0;
                double halfHn = targetHeight / 2.0;

                Point[] rect = new Point[4];
                rect[0] = c - halfWn * u - halfHn * v;
                rect[1] = c + halfWn * u - halfHn * v;
                rect[2] = c + halfWn * u + halfHn * v;
                rect[3] = c - halfWn * u + halfHn * v;

                if (!IsSimplePolygon(rect)) return;
                Vertices = rect;
                return;
            }
        }
    }
}