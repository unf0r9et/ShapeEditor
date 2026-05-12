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
                "Не удалось загрузить сборку фигур (ShapesLibrary.dll).\n" +
                "Соберите решение и убедитесь, что DLL лежит в папке «plugins» или рядом с исполняемым файлом.\n" +
                $"Ожидался путь: {tried}",
                "ShapeEditor",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        base.OnStartup(e);
    }
}
