using System.Windows;

namespace ShapeEditor
{
    public class RectangleShape : ShapeBase
    {
        public RectangleShape()
        {
            SidesCount = 4;
        }

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
    }
}