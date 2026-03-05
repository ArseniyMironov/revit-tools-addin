using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Core.Abstractions;
using System.Reflection;

namespace Plugins.WallFinisher
{
    [Transaction(TransactionMode.Manual)]
    [RevitPlugin(
        id:"SecondTest",
        name:"Oh? no",
        tabName:"ATP Tools",
        panelName:"Архитектура",
        tooltip:"Окно",
        description:"Автоматическая окно!")]
    public class SecondTestCommand : IPluginCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version.ToString();

            TaskDialog.Show("Успешный успех", $"Вторая команда в одном проекте, подгруженная через общий хост! Обновлённая до версии {version}, через ShadowCopy");

            return Result.Succeeded;
        }
    }
}
