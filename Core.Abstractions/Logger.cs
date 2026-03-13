using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Abstractions
{
    public static class Logger
    {
        private static string _logFilePath;
        private static readonly ConcurrentQueue<string> _logQueue = new ConcurrentQueue<string>();
        private static readonly int _flushIntervalMs = 5000;
        private static CancellationTokenSource _cts;
        private static string _revitUserName = "Unknown";

        public static void Initialize(string logDirectory, string revitUserName)
        {
            if (string.IsNullOrWhiteSpace(logDirectory))
                return;

            _revitUserName = string.IsNullOrWhiteSpace(revitUserName) ? Environment.UserName : revitUserName;

            try
            {
                Directory.CreateDirectory(logDirectory);
                // Создаем новый файл каждый месяц для удобства
                _logFilePath = Path.Combine(logDirectory, $"UsageLog_{DateTime.Now:yyyy-MM}.txt");

                _cts = new CancellationTokenSource();
                Task.Run(() => FlushLoop(_cts.Token));
            }
            catch (Exception ex)
            {
                // Записываем реальную причину сбоя в Temp
                string errorPath = Path.Combine(Path.GetTempPath(), "Logger_Init_Error.txt");
                File.WriteAllText(errorPath, $"Не удалось создать папку логов: {logDirectory}\n{ex.Message}");
            }
        }

        public static void Info(string pluginId, string action)
        {
            string user = Environment.UserName;
            _logQueue.Enqueue($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [INFO]  [{_revitUserName}] [{pluginId}] {action}");
        }

        public static void Error(string pluginId, string message, Exception ex = null)
        {
            string user = Environment.UserName;
            string exMsg = ex != null ? $" | {ex.Message}" : "";
            _logQueue.Enqueue($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [ERROR] [{_revitUserName}] [{pluginId}] {message}{exMsg}");
        }

        private static async Task FlushLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_flushIntervalMs, token);
                    Flush();
                }
                catch (TaskCanceledException) 
                { 
                    break; 
                }
            }
            Flush();
        }

        public static void Shutdown()
        {
            _cts?.Cancel();
            Flush();
        }

        private static void Flush()
        {
            if (_logQueue.IsEmpty || string.IsNullOrEmpty(_logFilePath))
                return;

            var sb = new StringBuilder();
            while (_logQueue.TryDequeue(out string log))
            {
                sb.AppendLine(log);
            }

            if (sb.Length > 0)
            {
                try
                {
                    File.AppendAllText(_logFilePath, sb.ToString(), Encoding.UTF8);
                }
                catch
                {
                    // Если файл временно заблокирован другим процессом, записи за эти 5 секунд теряются.
                    // Это допустимая жертва для телеметрии, чтобы исключить блокировку потоков.
                }
            }
        }

        /// <summary>
        /// Обновляет имя пользователя (используется для подхвата логина Revit после полной загрузки)
        /// </summary>
        public static void SetRevitUserName(string realUserName)
        {
            if (!string.IsNullOrWhiteSpace(realUserName))
            {
                _revitUserName = realUserName;
            }
        }
    }
}
