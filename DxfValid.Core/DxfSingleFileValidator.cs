using System;
using System.IO;

namespace DxfValid.Core
{
    public class DxfSingleFileValidator
    {
        private readonly DxfExtractor _extractor = new();
        private readonly TopologyEngine _topologyEngine = new();
        private readonly CleanlinessChecker _cleanlinessChecker = new();

        /// <summary>
        /// Проводит полную геометрическую и топологическую валидацию одного DXF-файла.
        /// </summary>
        /// <param name="filePath">Полный путь к DXF-файлу развертки</param>
        /// <param name="tolerance">Инженерный допуск стыковки контуров в мм (по умолчанию 0.05 мм)</param>
        /// <returns>Полный структурированный отчет со всеми геометрическими полями</returns>
        public FileValidationSummary ValidateFile(string filePath, double tolerance = 0.05)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Файл не найден по пути: {filePath}");

            // 1. Читаем версию DXF из заголовка файла
            string currentVersion = DetectDxfVersion(filePath);

            // 2. Извлекаем сегменты (netDxf / IxMilia для R12 + линеаризация сплайнов)
            var extractResult = _extractor.ExtractAllSegments(filePath);

            // 3. Собираем топологию контуров ( Closed / Open )
            var topologyResult = _topologyEngine.BuildContours(extractResult.Segments, tolerance);

            // 4. Проверяем чистоту геометрии на наложения линий
            var cleanlinessResult = _cleanlinessChecker.CheckAll(extractResult.CleanlinessSegments, tolerance);

            // 5. Формируем итоговую карточку со всеми полями, доступными для визуализации
            var summary = new FileValidationSummary
            {
                FileName = Path.GetFileName(filePath),
                FullPath = filePath,
                DxfVersion = currentVersion,
                Extraction = extractResult, // Сырые линии и предупреждения из NX
                Topology = topologyResult,   // Все замкнутые/разомкнутые контуры для отрисовки
                Cleanliness = cleanlinessResult // Все бирюзовые линии наложений
            };

            // 6. Выносим автоматический вердикт по статусу
            if (topologyResult.OpenContours.Count > 0 || cleanlinessResult.OverlappingDefects.Count > 0)
            {
                summary.Status = FileValidationStatus.Defective;
                summary.SummaryMessage = $"❌ БРАК: зазоров — {topologyResult.OpenContours.Count}, наложений — {cleanlinessResult.OverlappingDefects.Count}";
            }
            else if (currentVersion == "AutoCAD R12 (Устарел)")
            {
                summary.Status = FileValidationStatus.Warning;
                summary.SummaryMessage = $"⚠️ УСТАРЕЛ: Формат AutoCAD R12. Геометрия замкнута.";
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

            return summary;
        }

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
                            reader.ReadLine(); // Пропускаем строку тегов
                            string versionValue = reader.ReadLine()?.Trim();

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
