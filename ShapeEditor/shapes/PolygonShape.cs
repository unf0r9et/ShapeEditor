using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.IO;
using System.Text.Json;

namespace ShapeEditor
{
    /// <summary>
    /// Базовый класс для всех многоугольников.
    /// Через него строятся: прямоугольники, треугольники, трапеции, шестиугольники, произвольные многоугольники.
    /// </summary>
    public class PolygonShape : ShapeBase
    {
        private string _displayNameRuOverride;
        private string[] _sideNamesOverride;
        public bool EnforceIsosceles { get; set; } = false;
        public string PolygonType { get; set; } = "Custom";
        public PolygonShape() : base() { }

        public PolygonShape(int sidesCount)
        {
            SidesCount = sidesCount;
            Vertices = GetDefaultVertices();
            EdgeLengthLocked = new List<bool>(Vertices.Length);
            for (int i = 0; i < Vertices.Length; i++) EdgeLengthLocked.Add(false);
        }

        public override string DisplayNameRu => _displayNameRuOverride ?? $"{SidesCount}-угольник";
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
            writer.WriteStartObject();
            writer.WriteString("type", "PolygonShape");
            writer.WriteNumber("id", Id);
            writer.WriteString("displayName", DisplayNameRu);
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
            if (element.TryGetProperty("id", out var idProp)) Id = idProp.GetInt32();
            if (element.TryGetProperty("scale", out var sProp)) Scale = sProp.GetDouble();
            if (element.TryGetProperty("angle", out var aProp)) Angle = aProp.GetDouble();
            if (element.TryGetProperty("anchorX", out var axProp) && element.TryGetProperty("anchorY", out var ayProp))
                AnchorPoint = new Point(axProp.GetDouble(), ayProp.GetDouble());
            if (element.TryGetProperty("fill", out var fProp)) Fill = ParseColor(fProp.GetString());
            if (element.TryGetProperty("sidesCount", out var scProp)) SidesCount = scProp.GetInt32();
            if (element.TryGetProperty("enforceIsosceles", out var eiProp)) EnforceIsosceles = eiProp.GetBoolean();

            SideColors.Clear();
            if (element.TryGetProperty("sideColors", out var colorsProp))
                foreach (var c in colorsProp.EnumerateArray()) SideColors.Add(ParseColor(c.GetString()));

            SideThickness.Clear();
            if (element.TryGetProperty("sideThicknesses", out var thickProp))
                foreach (var t in thickProp.EnumerateArray()) SideThickness.Add(t.GetDouble());

            EdgeLengthLocked.Clear();
            if (element.TryGetProperty("edgeLocks", out var locksProp))
                foreach (var l in locksProp.EnumerateArray()) EdgeLengthLocked.Add(l.GetBoolean());

            if (element.TryGetProperty("vertices", out var vertsProp))
            {
                var verts = new List<Point>();
                foreach (var v in vertsProp.EnumerateArray())
                    verts.Add(new Point(v.GetProperty("x").GetDouble(), v.GetProperty("y").GetDouble()));
                Vertices = verts.ToArray();
            }

            if (element.TryGetProperty("sideNames", out var namesProp))
            {
                var names = new List<string>();
                foreach (var n in namesProp.EnumerateArray()) names.Add(n.GetString());
                _sideNamesOverride = names.ToArray();
            }

            if (element.TryGetProperty("displayName", out var dnProp))
                _displayNameRuOverride = dnProp.GetString();
        }
    }
}