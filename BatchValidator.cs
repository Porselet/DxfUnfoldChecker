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
        public string DxfVersion { get; set; } = "Неизвестно";
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

            // Ищем DXF-файлы рекурсивно во всех вложенных подпапках
            string[] files = Directory.GetFiles(folderPath, "*.dxf", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                try
                {
                    // 1. Читаем версию DXF из заголовка файла
                    string currentVersion = DetectDxfVersion(file);

                    // 2. Запускаем геометрический конвейер анализа
                    var extractResult = _extractor.ExtractAllSegments(file);
                    var topologyResult = _topologyEngine.BuildContours(extractResult.Segments);

                    // Важно: передаем отфильтрованную коллекцию для проверки наложений!
                    var cleanlinessResult = _cleanlinessChecker.CheckAll(extractResult.CleanlinessSegments, tolerance: 0.05);

                    // Вычисляем относительный путь для красивого отображения папок в таблице
                    string relativePath = Path.GetRelativePath(folderPath, file);

                    var summary = new FileValidationSummary
                    {
                        FileName = relativePath,
                        FullPath = file,
                        DxfVersion = currentVersion,
                        Extraction = extractResult,
                        Topology = topologyResult,
                        Cleanliness = cleanlinessResult
                    };

                    // 3. Выносим автоматический инженерный вердикт
                    if (topologyResult.OpenContours.Count > 0 || cleanlinessResult.OverlappingDefects.Count > 0)
                    {
                        summary.Status = FileValidationStatus.Defective;
                        summary.SummaryMessage = $"❌ БРАК: зазоров — {topologyResult.OpenContours.Count}, наложений — {cleanlinessResult.OverlappingDefects.Count}";
                    }
                    else if (currentVersion == "AutoCAD R12 (Устарел)")
                    {
                        summary.Status = FileValidationStatus.Warning;
                        summary.SummaryMessage = $"⚠️ ПРЕДУПРЕЖДЕНИЕ: Требуется пересохранить в AutoCAD 2010! Геометрия замкнута.";

                        extractResult.Warnings.Add(new GeometryWarning(
                            WarningType.ArcLinearized, "Файл",
                            "Внимание! Файл сохранен в формате AutoCAD R12. Станочники требуют AutoCAD 2010. Отправьте деталь конструкторам на конвертацию."
                        ));
                    }
                    else if (extractResult.Warnings.Count > 0 || cleanlinessResult.MicroGarbageCount > 0)
                    {
                        summary.Status = FileValidationStatus.Warning;
                        summary.SummaryMessage = $"⚠️ ЗАМЕЧАНИЯ: выпрямлено дуг — {extractResult.Warnings.Count}, микро-мусора — {cleanlinessResult.MicroGarbageCount}";
                    }
                    else
                    {
                        summary.Status = FileValidationStatus.Perfect;
                        summary.SummaryMessage = "🟢 ГОДЕН: Чертеж идеален";
                    }

                    summaries.Add(summary);
                }
                catch (Exception ex)
                {
                    // Если файл тотально поврежден — помечаем как брак структуры
                    summaries.Add(new FileValidationSummary
                    {
                        FileName = Path.GetFileName(file),
                        FullPath = file,
                        Status = FileValidationStatus.Defective,
                        SummaryMessage = $"💥 Ошибка структуры DXF: {ex.Message}"
                    });
                }
            }

            return summaries;
        }

        // БЫСТРЫЙ И НАДЕЖНЫЙ ДЕТЕКТОР ВЕРСИЙ DXF
        private string DetectDxfVersion(string filePath)
        {
            try
            {
                using (var reader = new StreamReader(filePath))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Trim() == "$ACADVER")
                        {
                            string versionValue = reader.ReadLine()?.Trim(); // Следующая строка — код версии
                            versionValue = reader.ReadLine()?.Trim();        // CAD пишет значение через строку тегов

                            return versionValue switch
                            {
                                "AC1006" => "AutoCAD R10",
                                "AC1009" => "AutoCAD R12",
                                "AC1012" => "AutoCAD R13",
                                "AC1013" => "AutoCAD R13",
                                "AC1014" => "AutoCAD R14",
                                "AC1015" => "AutoCAD 2000",
                                "AC1018" => "AutoCAD 2004",
                                "AC1021" => "AutoCAD 2007",
                                "AC1024" => "AutoCAD 2010",
                                "AC1027" => "AutoCAD 2013",
                                "AC1032" => "AutoCAD 2018",
                                _ => $"AutoCAD Код:{versionValue}"
                            };
                        }
                    }
                }
            }
            catch { }
            return "Не определена";
        }
    }
}
