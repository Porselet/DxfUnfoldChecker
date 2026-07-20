using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32; // Содержит OpenFolderDialog
using DxfValid.Core;

namespace DxfValid
{
    public partial class MainWindow : Window
    {
        private readonly BatchValidator _batchValidator = new();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
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
                var results = _batchValidator.ValidateFolder(selectedPath);
                DgFolderSummary.ItemsSource = results;

                TxtBatchStatusBar.Text = $"Успешно проверено файлов: {results.Count}. Текстовый отчет 'Валидация_Комплекта_Отчет.txt' сохранен в папку.";
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
    }
}
