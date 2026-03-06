using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Core.Abstractions;
using System;

namespace Plugins.WallFinisher
{
    [RevitPlugin(
        id: "WallFinisherStartup", 
        name: "Фоновый контроль труб",
        loadType: PluginLoadType.Startup,
        tabName: "ATP Tools",
        panelName: "Архитектура",
        description: "Тест холодной загрузки")]
    public class TestStartupApp : IPluginApplication
    {
        private PipeCreationUpdater _updater;
        public Result OnStartup(UIControlledApplication application)
        {
            // 1. Инициализируем наш IUpdater (передаем ему ID текущего AddIn)
            _updater = new PipeCreationUpdater(application.ActiveAddInId);

            // 2. Регистрируем Updater в ядре Revit
            UpdaterRegistry.RegisterUpdater(_updater);

            // 3. Настраиваем триггер: реагировать только на категорию "Трубы" (OST_PipeCurves)
            ElementCategoryFilter pipeFilter = new ElementCategoryFilter(BuiltInCategory.OST_PipeCurves);

            // 4. Добавляем триггер: реагировать только на ДОБАВЛЕНИЕ (создание) элементов
            UpdaterRegistry.AddTrigger(_updater.GetUpdaterId(), pipeFilter, Element.GetChangeTypeElementAddition());

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded; 
        }
    }

    /// <summary>
    /// Внутренний класс логики самого Апдейтера
    /// </summary>
    public class PipeCreationUpdater : IUpdater
    {
        private UpdaterId _updaterId;
        public PipeCreationUpdater(AddInId addInId)
        {
            // UpdaterId требует жестко заданного GUID. Генерируем любой уникальный.
            _updaterId = new UpdaterId(addInId, new Guid("8F123A45-B123-4567-8901-C2345D678E90"));
        }

        // Этот метод вызывается ядром Revit АВТОМАТИЧЕСКИ, когда срабатывает триггер
        public void Execute(UpdaterData data)
        {
            Document doc = data.GetDocument();

            // Получаем ID только что созданных элементов
            var addedElementIds = data.GetAddedElementIds();

            if (addedElementIds.Count > 0)
            {
                // Примечание: TaskDialog внутри IUpdater блокирует транзакцию. 
                // Для production-плагинов лучше использовать немодальные окна или логирование.
                // Для теста это отличный способ визуально убедиться, что код отработал.
                TaskDialog.Show("IUpdater", $"Внимание! В модель добавлено новых труб: {addedElementIds.Count} шт.");
            }
        }

        public string GetAdditionalInformation() => "Контролирует создание труб";
        public ChangePriority GetChangePriority() => ChangePriority.MEPFixtures;

        public UpdaterId GetUpdaterId() => _updaterId;
        public string GetUpdaterName() => "Pipe Creation Notifier";
    }
}
