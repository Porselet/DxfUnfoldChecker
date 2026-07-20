using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DxfValid.Core
{
    public enum FileValidationStatus { Perfect, Warning, Defective }

    public class FileValidationSummary
    {
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public FileValidationStatus Status { get; set; }
        public string SummaryMessage { get; set; } = string.Empty;
        public string DxfVersion { get; set; } = "Неизвестно";
        public ExtractionResult Extraction { get; set; } = new();
        public TopologyResult Topology { get; set; } = new();
        public CleanlinessReport Cleanliness { get; set; } = new();
    }

    public class BatchValidator
    {
        // Создаем экземпляр нашего нового пофайлового валидатора
        private readonly DxfSingleFileValidator _singleValidator = new();

        public List<FileValidationSummary> ValidateFolder(string folderPath)
        {
            var summaries = new List<FileValidationSummary>();

            // Рекурсивный поиск DXF-файлов во всех поддиректориях
            string[] files = Directory.GetFiles(folderPath, "*.dxf", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                try
                {
                    // Вызываем единый пофайловый метод нашей библиотеки (допуск 0.05 мм)
                    FileValidationSummary summary = _singleValidator.ValidateFile(file, tolerance: 0.05);

                    // Рассчитываем относительный путь, чтобы в таблице WPF красиво отображались папки
                    string relativePath = Path.GetRelativePath(folderPath, file);
                    summary.FileName = relativePath;

                    summaries.Add(summary);
                }
                catch
                {
                    // Если файл тотально поврежден на диске и не читается
                    summaries.Add(new FileValidationSummary
                    {
                        FileName = Path.GetFileName(file),
                        FullPath = file,
                        Status = FileValidationStatus.Defective,
                        SummaryMessage = "💥 Критическая ошибка: Файл поврежден или заблокирован"
                    });
                }
            }

            return summaries;
        }
    }
}

