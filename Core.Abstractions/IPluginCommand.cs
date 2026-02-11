using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace Core.Abstractions
{
    /// <summary>
     /// Единый интерфейс для всех загружаемых команд.
     /// Вместо IExternalCommand мы используем свой, чтобы контролировать запуск.
     /// </summary>
    public interface IPluginCommand
    {
        Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements);
    }

    // Атрибут для каждой команды
    [AttributeUsage(AttributeTargets.Class, Inherited = false,  AllowMultiple = false)]
    public class RevitPluginAttribute : Attribute
    {
        public string Id { get; }
        public string Name { get; }
        public string TabName { get; }
        public string PanelName { get; }
        public string ImageResource { get; }
        public string Tooltip { get; }
        public string Description { get; }

        public RevitPluginAttribute(string id, string name, string tabName, string panelName, string tooltip, string descriprtion)
        {
            Id = id;
            Name = name;
            TabName = tabName;
            PanelName = panelName;
            Tooltip = tooltip;
            Description = descriprtion;
        }
    }
}
