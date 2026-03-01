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
                new Point(-40, -40), // левая верхняя
                new Point( 40, -40), // правая верхняя
                new Point( 70,  40), // правая нижняя
                new Point(-70,  40)  // левая нижняя
            };
        }
    }
}