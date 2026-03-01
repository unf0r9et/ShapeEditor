using System.Windows;

namespace ShapeEditor
{
    public class HexagonShape : ShapeBase
    {
        public HexagonShape()
        {
            SidesCount = 6;
        }

        public override string[] SideNames => new[]
        {
            "Верхняя правая", "Правая", "Нижняя правая",
            "Нижняя левая", "Левая", "Верхняя левая"
        };

        protected override Point[] GetDefaultVertices()
        {
            // Правильный шестиугольник, центр в (0,0), радиус ~60
            return new Point[]
            {
                new Point( 60,   0),  // правая
                new Point( 30,  52),  // правая нижняя
                new Point(-30,  52),  // левая нижняя
                new Point(-60,   0),  // левая
                new Point(-30, -52),  // левая верхняя
                new Point( 30, -52)   // правая верхняя
            };
        }
    }
}