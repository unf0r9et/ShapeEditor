using System.Windows;

namespace ShapeEditor
{
    public class TriangleShape : ShapeBase
    {
        public TriangleShape()
        {
            SidesCount = 3;
        }

        public override string[] SideNames => new[] { "Правая", "Нижняя", "Левая" };

        protected override Point[] GetDefaultVertices()
        {
            // Остриём вверх
            return new Point[]
            {
                new Point(0, -60),  // верхняя вершина
                new Point(60, 40),   // правая нижняя
                new Point(-60, 40)   // левая нижняя
            };
        }
    }
}