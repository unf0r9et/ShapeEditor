//using ShapeEditor.shapes;
//using System;
//using System.Windows;

//namespace ShapeEditor
//{
//    public class TriangleShape : ShapeBase
//    {
//        public TriangleShape()
//        {
//            SidesCount = 3;
//        }

//        // TriangleShape.cs
//        public override string DisplayNameRu => "Треугольник";


//        public override string[] SideNames => new[] { "Правая", "Нижняя", "Левая" };

//        protected override Point[] GetDefaultVertices()
//        {
//            // Остриём вверх
//            return new Point[]
//            {
//                new Point(0, -60),  // верхняя вершина (V0)
//                new Point(60, 40),   // правая нижняя (V1)
//                new Point(-60, 40)   // левая нижняя (V2)
//            };
//        }

//        // Вспомогательная: пересечение двух окружностей
//        private bool TryIntersectCircles(Point p0, double r0, Point p1, double r1, out Point inter1, out Point inter2)
//        {
//            inter1 = inter2 = new Point();
//            double dx = p1.X - p0.X;
//            double dy = p1.Y - p0.Y;
//            double d = Math.Sqrt(dx * dx + dy * dy);
//            if (d < 1e-9) return false;
//            // no solutions if too far or one contains the other
//            if (d > r0 + r1 + 1e-6) return false;
//            if (d < Math.Abs(r0 - r1) - 1e-6) return false;

//            double a = (r0 * r0 - r1 * r1 + d * d) / (2 * d);
//            double h2 = r0 * r0 - a * a;
//            double h = h2 <= 0 ? 0 : Math.Sqrt(Math.Max(0, h2));

//            double xm = p0.X + a * (dx) / d;
//            double ym = p0.Y + a * (dy) / d;

//            double rx = -dy * (h / d);
//            double ry = dx * (h / d);

//            inter1 = new Point(xm + rx, ym + ry);
//            inter2 = new Point(xm - rx, ym - ry);
//            return true;
//        }

//        public override bool TrySetEdgeLengths(double[] lengths)
//        {
//            if (lengths == null || lengths.Length != 3) return false;

//            while (EdgeLengthLocked.Count < 3) EdgeLengthLocked.Add(false);

//            double a = lengths[0]; // V0-V1 (right)
//            double b = lengths[1]; // V1-V2 (bottom)
//            double c = lengths[2]; // V2-V0 (left)

//            // respect locks for lengths
//            if (EdgeLengthLocked[0]) a = GetEdgeLength(0);
//            if (EdgeLengthLocked[1]) b = GetEdgeLength(1);
//            if (EdgeLengthLocked[2]) c = GetEdgeLength(2);

//            if (a <= 0 || b <= 0 || c <= 0) return false;

//            // triangle inequality
//            if (a + b <= c || a + c <= b || b + c <= a) return false;

//            Point[] nv = (Point[])Vertices.Clone();

//            bool lock0 = EdgeLengthLocked[0];
//            bool lock1 = EdgeLengthLocked[1];
//            bool lock2 = EdgeLengthLocked[2];

//            // If any edge is locked, treat its endpoints as fixed positions (do not move them)
//            // Edge 0: V0-V1, Edge1: V1-V2, Edge2: V2-V0

//            if (lock1 && !lock0 && !lock2)
//            {
//                // V1 and V2 fixed; compute V0 as intersection of circles centered at V1 (r=a) and V2 (r=c)
//                Point p1 = Vertices[1];
//                Point p2 = Vertices[2];
//                if (!TryIntersectCircles(p1, a, p2, c, out Point i1, out Point i2)) return false;
//                // choose intersection closest to previous V0
//                Point prev = Vertices[0];
//                Point chosen = (Distance(prev, i1) <= Distance(prev, i2)) ? i1 : i2;
//                nv[0] = chosen;
//                // verify simple
//                if (!IsSimplePolygon(nv)) return false;
//                Vertices = nv;
//                return true;
//            }

//            if (lock0 && !lock1 && !lock2)
//            {
//                // V0 and V1 fixed; compute V2 as intersection of circles centered at V0 (r=c) and V1 (r=b)
//                Point p0 = Vertices[0];
//                Point p1 = Vertices[1];
//                if (!TryIntersectCircles(p0, c, p1, b, out Point i1, out Point i2)) return false;
//                Point prev = Vertices[2];
//                Point chosen = (Distance(prev, i1) <= Distance(prev, i2)) ? i1 : i2;
//                nv[2] = chosen;
//                if (!IsSimplePolygon(nv)) return false;
//                Vertices = nv;
//                return true;
//            }

//            if (lock2 && !lock0 && !lock1)
//            {
//                // V2 and V0 fixed; compute V1 as intersection of circles centered at V0 (r=a) and V2 (r=b)
//                Point p0 = Vertices[0];
//                Point p2 = Vertices[2];
//                if (!TryIntersectCircles(p0, a, p2, b, out Point i1, out Point i2)) return false;
//                Point prev = Vertices[1];
//                Point chosen = (Distance(prev, i1) <= Distance(prev, i2)) ? i1 : i2;
//                nv[1] = chosen;
//                if (!IsSimplePolygon(nv)) return false;
//                Vertices = nv;
//                return true;
//            }

//            if (lock0 && lock1 && !lock2)
//            {
//                // V0 and V1 fixed (edge0 locked and edge1 locked implies V0,V1,V2 perhaps fixed?)
//                // Actually V0 and V1 fixed define V2 by circle intersection with radii b (from V1) and c (from V0)
//                Point p0 = Vertices[0];
//                Point p1 = Vertices[1];
//                if (!TryIntersectCircles(p0, c, p1, b, out Point i1, out Point i2)) return false;
//                Point prev = Vertices[2];
//                Point chosen = (Distance(prev, i1) <= Distance(prev, i2)) ? i1 : i2;
//                nv[2] = chosen;
//                if (!IsSimplePolygon(nv)) return false;
//                Vertices = nv;
//                return true;
//            }

//            if (lock1 && lock2 && !lock0)
//            {
//                // V1 and V2 fixed -> compute V0
//                Point p1 = Vertices[1];
//                Point p2 = Vertices[2];
//                if (!TryIntersectCircles(p1, a, p2, c, out Point i1, out Point i2)) return false;
//                Point prev = Vertices[0];
//                Point chosen = (Distance(prev, i1) <= Distance(prev, i2)) ? i1 : i2;
//                nv[0] = chosen;
//                if (!IsSimplePolygon(nv)) return false;
//                Vertices = nv;
//                return true;
//            }

//            if (lock0 && lock2 && !lock1)
//            {
//                // V0 and V2 fixed -> compute V1
//                Point p0 = Vertices[0];
//                Point p2 = Vertices[2];
//                if (!TryIntersectCircles(p0, a, p2, b, out Point i1, out Point i2)) return false;
//                Point prev = Vertices[1];
//                Point chosen = (Distance(prev, i1) <= Distance(prev, i2)) ? i1 : i2;
//                nv[1] = chosen;
//                if (!IsSimplePolygon(nv)) return false;
//                Vertices = nv;
//                return true;
//            }

//            // If all three locked, nothing to do (ensure lengths compatible)
//            if (lock0 && lock1 && lock2)
//            {
//                // lengths must match current
//                if (Math.Abs(GetEdgeLength(0) - a) > 1e-6) return false;
//                if (Math.Abs(GetEdgeLength(1) - b) > 1e-6) return false;
//                if (Math.Abs(GetEdgeLength(2) - c) > 1e-6) return false;
//                return true;
//            }

//            // No locks: place base V1-V2 horizontally centered at current base center
//            // Use analytic construction to avoid numerical ambiguity and to support equilateral triangles reliably.
//            double halfB = b / 2.0;
//            double curCenterX = (Vertices[1].X + Vertices[2].X) / 2.0;
//            double baseY = (Vertices[1].Y + Vertices[2].Y) / 2.0;

//            // Place base endpoints
//            Point v1 = new Point(curCenterX + halfB, baseY);
//            Point v2 = new Point(curCenterX - halfB, baseY);

//            // Analytic x-coordinate of V0 relative to base left (-halfB)
//            // Using law of cosines: x0 = (c^2 - a^2) / (2*b)
//            double x0_rel = (c * c - a * a) / (2.0 * b);
//            // convert to world coordinates: base left is curCenterX - halfB
//            double baseLeftX = curCenterX - halfB;
//            double x0_world = baseLeftX + x0_rel;

//            double dx = x0_world - v1.X; // difference to right base point
//            // height squared from v1 to V0
//            double h2 = a * a - dx * dx;
//            if (h2 < -1e-6) return false;
//            double h = h2 <= 0 ? 0 : Math.Sqrt(Math.Max(0, h2));

//            // Place V0 above base (choose sign that minimizes movement from previous V0)
//            Point candidateUp = new Point(x0_world, baseY - h);
//            Point candidateDown = new Point(x0_world, baseY + h);
//            Point prevV0 = Vertices[0];
//            Point chosenV0 = (Distance(prevV0, candidateUp) <= Distance(prevV0, candidateDown)) ? candidateUp : candidateDown;

//            nv[0] = chosenV0;
//            nv[1] = v1;
//            nv[2] = v2;

//            if (!IsSimplePolygon(nv)) return false;
//            Vertices = nv;
//            return true;
//        }

//        private static double Distance(Point a, Point b)
//        {
//            double dx = a.X - b.X; double dy = a.Y - b.Y; return Math.Sqrt(dx * dx + dy * dy);
//        }
//    }
//}