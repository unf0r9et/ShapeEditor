using System.IO;
using System.Windows;

namespace ShapeEditor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (!ShapeLoader.TryLoadShapesPlugin())
        {
            string tried = Path.Combine(AppContext.BaseDirectory, ShapeLoader.PluginSubFolder, ShapeLoader.PluginDllFileName);
            MessageBox.Show(
                "Не удалось загрузить плагин фигур (ShapesLibrary.dll).\n" +
                "Поместите сборку в папку «plugins» рядом с приложением или в каталог исполняемого файла.\n" +
                $"Ожидался путь: {tried}",
                "ShapeEditor",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        base.OnStartup(e);
    }
}
