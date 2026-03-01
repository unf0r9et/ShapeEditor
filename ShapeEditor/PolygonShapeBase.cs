using System.Windows.Media;
using System.Windows.Shapes;

namespace ShapeEditor
{
    public abstract class PolygonShapeBase : ShapeBase
    {
        protected Polygon CreatePolygon(PointCollection points)
        {
            return new Polygon
            {
                Points = points,
                Stroke = SideColors.Count > 0 ? SideColors[0] : Brushes.DarkRed,
                StrokeThickness = SideThickness.Count > 0 ? SideThickness[0] : 3,
                Fill = Brushes.Transparent
            };
        }
    }
}