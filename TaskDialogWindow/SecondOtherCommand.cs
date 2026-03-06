using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Core.Abstractions;
using System.Reflection;

namespace TaskDialogWindow
{
    [Transaction(TransactionMode.Manual)]
    [RevitPlugin(
        id:"SecoondOther",
        name:"Oh? no2",
        loadType: PluginLoadType.OnClick,
        tabName:"ATP Tools",
        panelName:"Архитектура",
        tooltip:"Окно",
        description:"Автоматическая окно!")]
    public class SecondOtherCommand : IPluginCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version.ToString();

            TaskDialog.Show("Oh no", $"Команда во втором проекте, подгруженная через общий хост! Обновлённая уже до версии {version} через Shadow Copy и синхронизированная с json файлом!");

            return Result.Succeeded;
        }
    }
}
