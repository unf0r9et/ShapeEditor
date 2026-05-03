using ShapeEditor;
using System.Collections.Generic;

namespace ShapeEditor
{
    public class ShapeTreeItem
    {
        public ShapeBase Shape { get; set; }
        public string DisplayText { get; set; }
        public List<ShapeTreeItem> Children { get; set; } = new List<ShapeTreeItem>();

        public ShapeTreeItem(ShapeBase shape)
        {
            Shape = shape;
            DisplayText = $"ID={shape.Id}. {shape.DisplayNameRu}";
        }
    }
}