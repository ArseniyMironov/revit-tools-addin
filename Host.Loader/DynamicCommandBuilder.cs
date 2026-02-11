using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;

namespace Host.Loader
{
    public static class DynamicCommandBuilder
    {
        private static AssemblyBuilder _assemblyBuilder;
        private static ModuleBuilder _moduleBuilder;
        private static string _assemblyName = "ATPDynamicProxies";
        private static string _assemblyFileName = "ATPDynamicProxies.dll";
        private static string _saveDirectory;

        public static void Initialize()
        {
            _saveDirectory = Path.Combine(Path.GetTempPath(), "ATP", "Proxies", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_saveDirectory);

            var assemblyName = new AssemblyName("DynamicRevitCommands");

            _assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndSave, _saveDirectory);

            _moduleBuilder = _assemblyBuilder.DefineDynamicModule(_assemblyName, _assemblyFileName);
        }

        public static string GetProxyDllPath()
        {
            return Path.Combine(_saveDirectory, _assemblyFileName);
        }

        public static void SaveAssembly()
        {
            _assemblyBuilder.Save(_assemblyFileName);
        }

        // Создает новый класс, реализующий IExternalCommand, с атрибутом [Transaction(Manual)]
        public static Type CreateProxyCommandType(string pluginId)
        {
            // Имя класса будет уникальным: "ProxyCommand_{pluginId}"
            string typeName = $"ProxyCommand_{pluginId}";

            TypeBuilder typeBuilder = _moduleBuilder.DefineType(typeName, 
                TypeAttributes.Public | TypeAttributes.Class, 
                null, 
                new[] { typeof(IExternalCommand) });

            Type[] ctorParams = new Type[] { typeof(TransactionMode) };

            // Добавляем атрибут [Transaction(TransactionMode.Manual)]
            ConstructorInfo transAttrCtor = typeof(TransactionAttribute).GetConstructor(
                BindingFlags.Instance | BindingFlags.Public, 
                null, 
                ctorParams, 
                null);

            if (transAttrCtor == null)
            {
                throw new Exception("Не удалось найти конструктор для атрибута [Transaction]");
            }

            CustomAttributeBuilder transAttrBuilder = new CustomAttributeBuilder(transAttrCtor, 
                new object[] { TransactionMode.Manual });

            typeBuilder.SetCustomAttribute(transAttrBuilder);

            // Реализуем метод Execute
            MethodBuilder executeMethod = typeBuilder.DefineMethod("Execute", 
                MethodAttributes.Public | MethodAttributes.Virtual, 
                typeof(Result), 
                new[] { typeof(ExternalCommandData), typeof(string).MakeByRefType(), typeof(ElementSet) });

            ILGenerator il = executeMethod.GetILGenerator();

            // Пишем тело метода на IL
            // PluginManager.RunStatic(pluginId, commandData, ref message, elements);

            // 1. Загружаем строку pluginId
            il.Emit(OpCodes.Ldstr, pluginId);

            // 2. Загружаем аргументы метода Execute
            il.Emit(OpCodes.Ldarg_1); // commandData
            il.Emit(OpCodes.Ldarg_2); // ref message
            il.Emit(OpCodes.Ldarg_3); // elements

            // 3. Вызываем статический метод-мост (его напишем ниже в PluginManager)
            MethodInfo runMethod = typeof(PluginManager).GetMethod("RunStatic",
                BindingFlags.Public | BindingFlags.Static);

            if (runMethod == null) throw new Exception("Метод PluginManager.RunStatic не найден.");

            il.Emit(OpCodes.Call, runMethod);

            // 4. Возвращаем результат
            il.Emit(OpCodes.Ret);

            // Финализируем класс
            typeBuilder.DefineMethodOverride(executeMethod, typeof(IExternalCommand).GetMethod("Execute"));
            return typeBuilder.CreateType();
        }
    }
}
