using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32; // Содержит OpenFolderDialog
using DxfValid.Core;
using System.IO;
using System.Linq;

namespace DxfValid
{
    public partial class MainWindow : Window
    {
        private readonly BatchValidator _batchValidator = new();
        public string AutoStartFolderPath { get; set; } = string.Empty;
        public string AutoStartFilePath { get; set; } = string.Empty;

        public MainWindow()
        {
            InitializeComponent();
        }


        private async Task RunFolderValidationAsync(string folderPath)
        {
            var progressWin = new ProgressWindow();
            progressWin.Owner = this; // Привязываем к главному окну, чтобы оно было по центру

            // 2. Настраиваем Си-шный "колбэк" (Progress), который слушает фоновый поток
            var progressHandler = new Progress<ProgressStatus>(status =>
            {
                // Старые подвязки шкал (оставляем без изменений)
                progressWin.PBar.Maximum = status.Total;
                progressWin.PBar.Value = status.Current;

                // 1. ИСПРАВЛЕНО: Теперь пишем не просто цифры, а имя текущего DXF чертежа!
                progressWin.TxtStatus.Text = $"Обработка: {status.Current} из {status.Total}\nФайл: {status.FileName}";

                // Расчет процентов (оставляем без изменений)
                double percent = ((double)status.Current / status.Total) * 100;
                progressWin.TxtPercent.Text = $"{Math.Round(percent)}%";
            });


            // 3. Показываем окно прогресса (не блокируя поток, чтобы оно обновлялось)
            progressWin.Show();

            // 4. ЗАПУСКАЕМ ТЯЖЕЛЫЙ ЦИКЛ В ФОНЕ! 
            // Task.Run уносит расчеты на другое ядро процессора, а await ждет окончания без зависания UI
            var results = await Task.Run(() => _batchValidator.ValidateFolder(folderPath, progressHandler));

            // 1. Разрешаем окну закрыться, так как фоновый поток завершил работу
            progressWin.IsScanFinished = true;

            // 2. Теперь закрываем его программно (этот вызов сработает успешно)
            progressWin.Close();

            // 6. Выводим готовый отчет в нашу таблицу (как и раньше)
            DgFolderSummary.ItemsSource = results;

            TxtBatchStatusBar.Text = $"Успешно проверено файлов: {results.Count}.";

        }
        private async void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            // Используем нативный встроенный диалог выбора папки .NET
            OpenFolderDialog folderDialog = new OpenFolderDialog
            {
                Title = "Выберите папку, содержащую комплект DXF разверток"
            };

            if (folderDialog.ShowDialog() == true)
            {
                string selectedPath = folderDialog.FolderName;
                TxtFolderPath.Text = selectedPath;

                // Запускаем пакетный анализ
                await RunFolderValidationAsync(selectedPath);
                // 1. Создаем экземпляр нашего нового окна прогресса
            }
        }

        private void DgFolderSummary_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Ловим строку, по которой кликнули
            if (DgFolderSummary.SelectedItem is FileValidationSummary selectedFile)
            {
                // Открываем новое отдельное окно графического просмотра, передавая ему все данные анализа
                DetailViewerWindow detailWindow = new DetailViewerWindow(selectedFile);
                detailWindow.Owner = this; // Чтобы окно не пряталось за главное
                detailWindow.ShowDialog();
            }
        }

        private void MenuOpenFolderSmart_Click(object sender, RoutedEventArgs e)
        {
            if (DgFolderSummary.SelectedItems.Count == 0) return;
            // 1. Собираем все выделенные инженером строки
            var selectedFiles = DgFolderSummary.SelectedItems.Cast<FileValidationSummary>().ToList();
            if (selectedFiles.Count == 0) return;

            // 2. ГРУППИРУЕМ файлы по их родительским папкам!
            var groupedByFolder = selectedFiles.GroupBy(f => Path.GetDirectoryName(f.FullPath));

            foreach (var folderGroup in groupedByFolder)
            {
                string folderPath = folderGroup.Key; // Путь к общей папке
                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) continue;

                // Создаем PIDL для самой папки (это корень будущего окна)
                IntPtr folderPidl = NativeMethods.ILCreateFromPath(folderPath);
                if (folderPidl == IntPtr.Zero) continue;

                // Создаем массив PIDL для всех выделенных файлов в ЭТОЙ конкретной папке
                var filePidls = new List<IntPtr>();
                foreach (var file in folderGroup)
                {
                    if (File.Exists(file.FullPath))
                    {
                        IntPtr filePidl = NativeMethods.ILCreateFromPath(file.FullPath);
                        if (filePidl != IntPtr.Zero) filePidls.Add(filePidl);
                    }
                }

                if (filePidls.Count > 0)
                {
                    // 3. ВЫЗЫВАЕМ МАГИЮ: Windows открывает ОДНО окно папки и выделяет ВСЕ файлы из списка!
                    NativeMethods.SHOpenFolderAndSelectItems(folderPidl, (uint)filePidls.Count, filePidls.ToArray(), 0);
                }

                // 4. Очищаем за собой память ядра Windows (важный инженерный тон)
                NativeMethods.ILFree(folderPidl);
                foreach (var pidl in filePidls) NativeMethods.ILFree(pidl);
            }
        }

        private async void Window_ContentRendered(object sender, EventArgs e)
        {
            // Сценарий 1: Если передали ключ папки - запускаем наш пакетный скан
            if (!string.IsNullOrEmpty(AutoStartFolderPath))
            {
                // Вызываем тот же самый асинхронный код, который мы писали для кнопки
                // (С созданием ProgressWindow, Task.Run и выводом результатов)
                await RunFolderValidationAsync(AutoStartFolderPath);
            }
            // Сценарий 2: Если передали ключ ОДНОГО файла - сразу открываем окно деталей
            else if (!string.IsNullOrEmpty(AutoStartFilePath))
            {
                if (System.IO.File.Exists(AutoStartFilePath))
                {
                    try
                    {
                        // Быстро проверяем один файл через Core-библиотеку
                        var singleValidator = new DxfValid.Core.DxfSingleFileValidator();
                        var summary = singleValidator.ValidateFile(AutoStartFilePath, tolerance: 0.05);

                        // И мгновенно распахиваем наше окно детальной графики!
                        DetailViewerWindow detailWindow = new DetailViewerWindow(summary);
                        detailWindow.Owner = this;
                        detailWindow.ShowDialog();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка авто-открытия файла: {ex.Message}", "Ошибка CLI", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

    }
}
