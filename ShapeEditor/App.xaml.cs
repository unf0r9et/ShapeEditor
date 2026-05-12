using System.Windows;

namespace ShapeEditor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (!ShapeLoader.TryLoadShapesPlugin())
        {
            MessageBox.Show(
                "Не удалось инициализировать фабрику фигур (ShapesLibrary). Проверьте сборку решения.",
                "ShapeEditor",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        base.OnStartup(e);
    }
}
