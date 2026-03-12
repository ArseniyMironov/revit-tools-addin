using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Core.Abstractions
{
    public class Toast
    {
        /// <summary>
        /// Показывает немодальное всплывающее окно в правом нижнем углу экрана, которое исчезает само.
        /// </summary>
        /// <param name="message">Текст сообщения</param>
        /// <param name="seconds">Через сколько секунд закрыть окно</param>
        public static void Show(string mesage, int seconds = 4)
        {
            // Проверяем, что вызов идет из UI-потока Revit (Application.Current всегда существует в Revit)
            System.Threading.Thread thread = new System.Threading.Thread(() =>
            {
                var win = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = new SolidColorBrush(Color.FromArgb(230, 40, 40, 40)),
                    Topmost = true,
                    ShowActivated = false,
                    Width = 350,
                    SizeToContent = SizeToContent.Height,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    BorderBrush = Brushes.DarkRed,
                    BorderThickness = new Thickness(2, 0, 0, 0)
                };

                var workArea = SystemParameters.WorkArea;
                win.Left = workArea.Right - win.Width - 20;
                win.Top = workArea.Bottom - 100 - 20;

                var textBlock = new TextBlock
                {
                    Text = mesage,
                    Foreground = Brushes.White,
                    Margin = new Thickness(15),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 14
                };
                win.Content = textBlock;

                var timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(seconds)
                };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    win.Close();
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                };

                win.Show();
                timer.Start();

                // Запускаем движок отрисовки WPF для этого потока
                Dispatcher.Run();
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }
    }
}
