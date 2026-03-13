using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Core.Abstractions;
using System.Reflection;

namespace Plugins.WallFinisher
{
    [Transaction(TransactionMode.Manual)]
    [RevitPlugin(
        id:"WallFinisher", 
        name:"Отделка Стен", 
        loadType: PluginLoadType.OnClick,
        tabName:"ATP Tools", 
        panelName:"Архитектура",
        tooltip:"Окно",
        description:"Автоматическая штукатурка")]
    public class WallFinishCommand : IPluginCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version.ToString();

            TaskDialog.Show("Wall Finisher", $"Привет! Это уже версия плагина {version}, загруженная через Shadow Copy. И слово было Плагин!");

            return Result.Succeeded;
        }
    }
}
