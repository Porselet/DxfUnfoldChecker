using System;
using System.Collections.Generic;
using System.Linq;

namespace DxfUnfoldChecker
{
    // Структура для описания найденной грязи/наложения
    public record DefectSegment(AnalyticSegment Segment, string Description);

    public class CleanlinessReport
    {
        // Список сегментов, которые признаны наложениями/дубликатами
        public List<DefectSegment> OverlappingDefects { get; set; } = new();
        // Количество изолированных микро-кусочков (мусора)
        public int MicroGarbageCount { get; set; }
    }

    public class CleanlinessChecker
    {
        public CleanlinessReport CheckAll(List<AnalyticSegment> allSegments, double tolerance = 0.05)
        {
            var report = new CleanlinessReport();

            // Защита: если линий слишком мало, проверять нечего
            if (allSegments.Count < 2) return report;

            // Храним индексы уже проверенных как дефектные линий, чтобы не дублировать отчеты
            var detectedIndexes = new HashSet<int>();

            // Сравниваем каждый отрезок с каждым
            for (int i = 0; i < allSegments.Count; i++)
            {
                var segA = allSegments[i];

                // Параллельно отлавливаем микро-мусор короче допуска
                if (segA.Length < tolerance)
                {
                    report.MicroGarbageCount++;
                    continue;
                }

                for (int j = i + 1; j < allSegments.Count; j++)
                {
                    var segB = allSegments[j];
                    if (segB.Length < tolerance) continue;

                    // Если нашли наложение
                    if (AreSegmentsOverlapping(segA, segB, tolerance))
                    {
                        if (!detectedIndexes.Contains(j))
                        {
                            detectedIndexes.Add(j);
                            report.OverlappingDefects.Add(new DefectSegment(
                                segB,
                                $"Наложение линии: пересекается с элементом №{i + 1} на расстоянии меньше {tolerance} мм"
                            ));
                        }
                    }
                }
            }

            return report;
        }

        private bool AreSegmentsOverlapping(AnalyticSegment s1, AnalyticSegment s2, double tolerance)
        {
            // 1. Проверяем коллинеарность (лежат ли на параллельных прямых)
            // Вектор первого отрезка
            double v1X = s1.End.X - s1.Start.X;
            double v1Y = s1.End.Y - s1.Start.Y;
            // Вектор второго отрезка
            double v2X = s2.End.X - s2.Start.X;
            double v2Y = s2.End.Y - s2.Start.Y;

            // Векторное произведение (cross product) для проверки параллельности
            double crossProduct = v1X * v2Y - v1Y * v2X;
            double lenS1 = s1.Length;
            double lenS2 = s2.Length;

            // Нормализуем по длине отрезков, чтобы допуск не плыл от масштаба
            if (Math.Abs(crossProduct) / (lenS1 * lenS2) > 0.001)
                return false; // Линии явно не параллельны

            // 2. Проверяем, лежат ли они на ОДНОЙ И ТОЙ ЖЕ бесконечной прямой
            // Расстояние от точки Start отрезка S2 до прямой S1 через площадь треугольника
            double area = Math.Abs((s1.End.Y - s1.Start.Y) * s2.Start.X - (s1.End.X - s1.Start.X) * s2.Start.Y + s1.End.X * s1.Start.Y - s1.End.Y * s1.Start.X);
            double distanceToLine = area / lenS1;

            if (distanceToLine > tolerance)
                return false; // Линии параллельны, но смещены друг относительно друга (рельсы)

            // 3. Линии на одной прямой. Проверяем, перекрываются ли их проекции (интервалы)
            // Проецируем на ось X, если линии более горизонтальные, или на Y, если вертикальные
            bool useX = Math.Abs(v1X) > Math.Abs(v1Y);

            double min1 = useX ? Math.Min(s1.Start.X, s1.End.X) : Math.Min(s1.Start.Y, s1.End.Y);
            double max1 = useX ? Math.Max(s1.Start.X, s1.End.X) : Math.Max(s1.Start.Y, s1.End.Y);
            double min2 = useX ? Math.Min(s2.Start.X, s2.End.X) : Math.Min(s2.Start.Y, s2.End.Y);
            double max2 = useX ? Math.Max(s2.Start.X, s2.End.X) : Math.Max(s2.Start.Y, s2.End.Y);

            // Условие перекрытия двух интервалов на оси с учетом допуска
            return (min1 < max2 - tolerance) && (min2 < max1 - tolerance);
        }
    }
}
