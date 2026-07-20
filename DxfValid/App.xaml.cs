using System.Configuration;
using System.Data;
using System.Windows;

namespace DxfValid
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void App_Startup(object sender, StartupEventArgs e)
        {
            // 1. ПРОВЕРКА: Если запустили с ключами командной строки
            if (e.Args.Length >= 2)
            {
                string key = e.Args[0].ToLower();
                string path = e.Args[1];

                // СЦЕНАРИЙ А: Передали ПАПКУ -> открываем Главное Окно и запускаем авто-скан
                if (key == "-folder")
                {
                    MainWindow mainWindow = new MainWindow();
                    mainWindow.AutoStartFolderPath = path;
                    mainWindow.Show();
                    return; // Завершаем метод, приложение живет в главном окне
                }

                // СЦЕНАРИЙ Б: Передали ОДИН ФАЙЛ -> Главное окно ИГНОРИРУЕМ!
                else if (key == "-file")
                {
                    if (System.IO.File.Exists(path))
                    {
                        try
                        {
                            // Быстро прогоняем файл через Core-библиотеку
                            var singleValidator = new DxfValid.Core.DxfSingleFileValidator();
                            var summary = singleValidator.ValidateFile(path, tolerance: 0.05);

                            // Создаем и открываем СТРОГО окно деталей
                            DetailViewerWindow detailWindow = new DetailViewerWindow(summary);

                            // ВАЖНО: говорим WPF, что закрытие ЭТОГО окна завершит всю программу
                            this.MainWindow = detailWindow;

                            detailWindow.ShowDialog();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Ошибка авто-открытия файла: {ex.Message}", "Ошибка CLI", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }

                    // Как только инженер закроет окно деталей, закрываем всё приложение
                    Shutdown();
                    return;
                }
            }

            // 2. СЦЕНАРИЙ В: Обычный запуск без ключей (просто открываем пустое главное окно)
            MainWindow normalWindow = new MainWindow();
            normalWindow.Show();
        }

    }

}
