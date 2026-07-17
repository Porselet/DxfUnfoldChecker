using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DxfUnfoldChecker
{
    public enum FileValidationStatus { Perfect, Warning, Defective }

    public class FileValidationSummary
    {
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public FileValidationStatus Status { get; set; }
        public string SummaryMessage { get; set; } = string.Empty;

        // Кэшируем результаты для передачи в дочернее окно просмотра
        public ExtractionResult Extraction { get; set; } = new();
        public TopologyResult Topology { get; set; } = new();
        public CleanlinessReport Cleanliness { get; set; } = new();
    }

    public class BatchValidator
    {
        private readonly DxfExtractor _extractor = new();
        private readonly TopologyEngine _topologyEngine = new();
        private readonly CleanlinessChecker _cleanlinessChecker = new();

        public List<FileValidationSummary> ValidateFolder(string folderPath)
        {
            var summaries = new List<FileValidationSummary>();
            string[] files = Directory.GetFiles(folderPath, "*.dxf", SearchOption.TopDirectoryOnly);

            StringBuilder reportText = new StringBuilder();
            reportText.AppendLine($"==========================================================================");
            reportText.AppendLine($"ОТЧЕТ О ВАЛИДАЦИИ КОМПЛЕКТА РАЗВЕРТОК ИЗ SIEMENS NX");
            reportText.AppendLine($"Папка: {folderPath}");
            reportText.AppendLine($"Дата проверки: {DateTime.Now}");
            reportText.AppendLine($"==========================================================================\n");

            int totalFiles = files.Length;
            int perfectCount = 0;
            int warningCount = 0;
            int defectiveCount = 0;

            foreach (var file in files)
            {
                try
                {
                    var extractResult = _extractor.ExtractAllSegments(file);
                    var topologyResult = _topologyEngine.BuildContours(extractResult.Segments);
                    var cleanlinessResult = _cleanlinessChecker.CheckAll(extractResult.Segments);

                    var summary = new FileValidationSummary
                    {
                        FileName = Path.GetFileName(file),
                        FullPath = file,
                        Extraction = extractResult,
                        Topology = topologyResult,
                        Cleanliness = cleanlinessResult
                    };

                    if (topologyResult.OpenContours.Count > 0 || cleanlinessResult.OverlappingDefects.Count > 0)
                    {
                        summary.Status = FileValidationStatus.Defective;
                        summary.SummaryMessage = $"❌ БРАК: зазоров контура — {topologyResult.OpenContours.Count}, наложений линий — {cleanlinessResult.OverlappingDefects.Count}";
                        defectiveCount++;
                    }
                    else if (extractResult.Warnings.Count > 0 || cleanlinessResult.MicroGarbageCount > 0)
                    {
                        summary.Status = FileValidationStatus.Warning;
                        summary.SummaryMessage = $"⚠️ ВНИМАНИЕ: выпрямлено дуг — {extractResult.Warnings.Count}, микро-мусора — {cleanlinessResult.MicroGarbageCount}";
                        warningCount++;
                    }
                    else
                    {
                        summary.Status = FileValidationStatus.Perfect;
                        summary.SummaryMessage = "🟢 ГОДЕН: Чертеж идеален";
                        perfectCount++;
                    }

                    summaries.Add(summary);
                    reportText.AppendLine($"Файл: {summary.FileName} | Статус: {summary.Status} | {summary.SummaryMessage}");
                }
                catch (Exception ex)
                {
                    summaries.Add(new FileValidationSummary
                    {
                        FileName = Path.GetFileName(file),
                        FullPath = file,
                        Status = FileValidationStatus.Defective,
                        SummaryMessage = $"💥 Ошибка структуры DXF: {ex.Message}"
                    });
                    defectiveCount++;
                    reportText.AppendLine($"Файл: {Path.GetFileName(file)} | СБОЙ ЧТЕНИЯ: {ex.Message}");
                }
            }

            // Добавляем статистику в конец отчета
            reportText.AppendLine($"\n--------------------------------------------------------------------------");
            reportText.AppendLine($"ИТОГО ОБРАБОТАНО ФАЙЛОВ: {totalFiles}");
            reportText.AppendLine($"  - Годных (Зеленый статус): {perfectCount}");
            reportText.AppendLine($"  - С замечаниями (Желтый статус): {warningCount}");
            reportText.AppendLine($"  - С дефектами/Брак (Красный статус): {defectiveCount}");
            reportText.AppendLine($"--------------------------------------------------------------------------");

            // Сохраняем текстовый отчет автоматически
            try
            {
                string reportPath = Path.Combine(folderPath, "Валидация_Комплекта_Отчет.txt");
                File.WriteAllText(reportPath, reportText.ToString());
            }
            catch { /* Игнорируем ошибки записи, если файл занят */ }

            return summaries;
        }
    }
}
