//using System;
//using System.Windows;

//namespace ShapeEditor
//{
//    public class TrapezoidShape : ShapeBase
//    {
//        public TrapezoidShape()
//        {
//            SidesCount = 4;
//        }

//        public override string DisplayNameRu => "Трапеция";
//        public override string[] SideNames => new[] { "Верхняя", "Правая боковая", "Нижняя", "Левая боковая" };

//        // Если true — при изменении одной боковой стороны зеркально применяем к другой
//        public bool EnforceIsosceles { get; set; } = false;

//        protected override Point[] GetDefaultVertices()
//        {
//            // Равнобокая трапеция, центр в (0,0)
//            return new Point[]
//            {
//                new Point(-40, -40), // V0: левая верхняя
//                new Point( 40, -40), // V1: правая верхняя
//                new Point( 70,  40), // V2: правая нижняя
//                new Point(-70,  40)  // V3: левая нижняя
//            };
//        }

//        private double[] GetCurrentEdgeLengths()
//        {
//            var arr = new double[4];
//            for (int i = 0; i < 4; i++) arr[i] = GetEdgeLength(i);
//            return arr;
//        }

//        public override void SetEdgeLength(int edgeIndex, double newLength)
//        {
//            if (edgeIndex < 0 || edgeIndex >= Vertices.Length || newLength <= 0) return;
//            if (EdgeLengthLocked != null && edgeIndex < EdgeLengthLocked.Count && EdgeLengthLocked[edgeIndex]) return;
//            if (Vertices.Length != 4) { base.SetEdgeLength(edgeIndex, newLength); return; }

//            // Для оснований (индексы 0 и 2) — используем простой подход
//            if (edgeIndex == 0)
//            {
//                double x0 = Vertices[0].X;
//                double y = Vertices[0].Y;
//                Point[] nv = (Point[])Vertices.Clone();
//                nv[0] = new Point(x0, y);
//                nv[1] = new Point(x0 + newLength, y);
//                if (IsSimplePolygon(nv)) { Vertices = nv; return; }
//            }
//            else if (edgeIndex == 2)
//            {
//                double x3 = Vertices[3].X;
//                double y = Vertices[2].Y;
//                Point[] nv = (Point[])Vertices.Clone();
//                nv[3] = new Point(x3, y);
//                nv[2] = new Point(x3 + newLength, y);
//                if (IsSimplePolygon(nv)) { Vertices = nv; return; }
//            }
//            // Для боковых сторон (индексы 1 и 3) — используем TrySetEdgeLengths
//            else if (edgeIndex == 1 || edgeIndex == 3)
//            {
//                double[] lengths = GetCurrentEdgeLengths();
//                lengths[edgeIndex] = newLength;

//                // Если требуется равнобедренность — применяем её
//                if (EnforceIsosceles)
//                {
//                    // При изменении правой боковой (индекс 1) — синхронизируем левую (индекс 3)
//                    if (edgeIndex == 1)
//                        lengths[3] = newLength;
//                    // При изменении левой боковой (индекс 3) — синхронизируем правую (индекс 1)
//                    else
//                        lengths[1] = newLength;
//                }

//                // Пробуем строгий атомарный вариант
//                if (TrySetEdgeLengths(lengths))
//                    return;

//                // Если строгий не прошёл, пробуем вычислить наилучшее приближение и применить его (расслабленная версия)
//                if (TryComputeTrapezoidVertices(lengths[0], lengths[1], lengths[2], lengths[3], 1e-3, out Point[] approx))
//                {
//                    if (IsSimplePolygon(approx))
//                    {
//                        Vertices = approx;
//                        return;
//                    }
//                }

//                // fallback — ничего не делаем
//                return;
//            }

//            // fallback
//            base.SetEdgeLength(edgeIndex, newLength);
//        }

//        /// <summary>
//        /// Попытаться установить все длины рёбер трапеции одновременно.
//        /// lengths: [верхняя, правая боковая, нижняя, левая боковая]
//        /// Численный, более устойчивый вариант: ищем смещение нижнего основания s методом бисиекции.
//        /// </summary>
//        public override bool TrySetEdgeLengths(double[] lengths)
//        {
//            if (lengths == null || lengths.Length != 4) return false;
//            while (EdgeLengthLocked.Count < 4) EdgeLengthLocked.Add(false);

//            // если какая-то длина заблокирована — используем текущую
//            double a = EdgeLengthLocked[0] ? GetEdgeLength(0) : lengths[0];   // верхняя
//            double br = EdgeLengthLocked[1] ? GetEdgeLength(1) : lengths[1];  // правая боковая
//            double c = EdgeLengthLocked[2] ? GetEdgeLength(2) : lengths[2];   // нижняя
//            double bl = EdgeLengthLocked[3] ? GetEdgeLength(3) : lengths[3];  // левая боковая

//            if (a <= 0 || br <= 0 || c <= 0 || bl <= 0) return false;

//            // Попробуем сначала строгую задачу
//            if (TryComputeTrapezoidVertices(a, br, c, bl, 1e-6, out Point[] nv))
//            {
//                if (!IsSimplePolygon(nv)) return false;
//                Vertices = nv;
//                return true;
//            }

//            // Если не нашлось строгого решения — пробуем более мягкий tolerance
//            if (TryComputeTrapezoidVertices(a, br, c, bl, 1e-3, out Point[] nv2))
//            {
//                if (!IsSimplePolygon(nv2)) return false;
//                Vertices = nv2;
//                return true;
//            }

//            // И ещё более мягкий
//            if (TryComputeTrapezoidVertices(a, br, c, bl, 1e-1, out Point[] nv3))
//            {
//                if (!IsSimplePolygon(nv3)) return false;
//                Vertices = nv3;
//                return true;
//            }

//            return false;
//        }

//        /// <summary>
//        /// Helper: вычисляет вершины трапеции для заданных длин.
//        /// Возвращает true, если найдено решение с невязкой <= tol. Если tol положителен, выполняется поиск s, минимизирующего |f|.
//        /// Если nv возвращён, содержит рассчитанные вершины: V0, V1, V2, V3.
//        /// </summary>
//        private bool TryComputeTrapezoidVertices(double a, double br, double c, double bl, double tol, out Point[] nvOut)
//        {
//            nvOut = null;
//            // копия кода из TrySetEdgeLengths, но возвращающая best solution
//            Point[] nv = (Point[])Vertices.Clone();
//            double x0 = Vertices[0].X;
//            double topY = Vertices[0].Y;
//            double x1 = x0 + a;

//            double low1 = -bl, high1 = bl;
//            double low2 = a - c - br, high2 = a - c + br;
//            double sLow = Math.Max(low1, low2);
//            double sHigh = Math.Min(high1, high2);

//            if (sLow > sHigh) return false;

//            double f(double s)
//            {
//                double v3x = x0 + s;
//                double v2x = v3x + c;
//                double dxR = v2x - x1;
//                double dxL = v3x - x0; // == s
//                double sqR = br * br - dxR * dxR;
//                double sqL = bl * bl - dxL * dxL;
//                if (sqR < 0 || sqL < 0) return double.NaN;
//                double dyR = Math.Sqrt(Math.Max(0, sqR));
//                double dyL = Math.Sqrt(Math.Max(0, sqL));
//                return dyR - dyL;
//            }

//            double fLow = f(sLow);
//            double fHigh = f(sHigh);

//            if (double.IsNaN(fLow) || double.IsNaN(fHigh)) return false;

//            double sFound = double.NaN;
//            if (fLow * fHigh <= 0)
//            {
//                double lo = sLow, hi = sHigh;
//                for (int iter = 0; iter < 60; iter++)
//                {
//                    double mid = 0.5 * (lo + hi);
//                    double fm = f(mid);
//                    if (double.IsNaN(fm)) break;
//                    if (Math.Abs(fm) < 1e-7) { sFound = mid; break; }
//                    if (f(lo) * fm <= 0) hi = mid; else lo = mid;
//                    sFound = mid;
//                }
//            }
//            else
//            {
//                int steps = 60;
//                double bestS = sLow;
//                double bestVal = Math.Abs(fLow);
//                for (int i = 1; i <= steps; i++)
//                {
//                    double s = sLow + (sHigh - sLow) * i / (double)steps;
//                    double v = f(s);
//                    if (double.IsNaN(v)) continue;
//                    if (Math.Abs(v) < bestVal) { bestVal = Math.Abs(v); bestS = s; }
//                }
//                sFound = bestS;
//                // previously we rejected if residual > tol; now accept best-found s to allow best-effort rebuild
//                // if (Math.Abs(f(sFound)) > tol) return false;
//            }

//            if (double.IsNaN(sFound)) return false;

//            double v3xFound = x0 + sFound;
//            double v2xFound = v3xFound + c;

//            double dxRf = v2xFound - x1;
//            double dxLf = v3xFound - x0;
//            double sqRf = br * br - dxRf * dxRf;
//            double sqLf = bl * bl - dxLf * dxLf;
//            if (sqRf < -1e-9 || sqLf < -1e-9) return false;

//            double dyR = sqRf <= 0 ? 0 : Math.Sqrt(Math.Max(0, sqRf));
//            double dyL = sqLf <= 0 ? 0 : Math.Sqrt(Math.Max(0, sqLf));
//            double dy = 0.5 * (dyR + dyL);

//            double dirSign = Math.Sign(Vertices[2].Y - Vertices[0].Y); if (dirSign == 0) dirSign = 1;
//            double bottomY = topY + dirSign * dy;

//            nv[0] = new Point(x0, topY);
//            nv[1] = new Point(x1, topY);
//            nv[3] = new Point(v3xFound, bottomY);
//            nv[2] = new Point(v2xFound, bottomY);

//            nvOut = nv;
//            return true;
//        }
//    }
//}