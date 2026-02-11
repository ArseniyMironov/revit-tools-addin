using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Host.Loader
{
    // Эту команду мы привязываем к кнопке "Отделка стен"
    [Transaction(TransactionMode.Manual)]
    public class WallFinishLauncher : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Здесь мы жестко задаем ID плагина, который должна запустить эта кнопка
            var manager = new PluginManager();
            return manager.Run("WallFinisher", commandData, ref message, elements);
        }
    }
}
