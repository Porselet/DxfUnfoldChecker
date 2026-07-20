using System;
using System.Collections.Generic;
using System.Linq;

namespace DxfValid.Core
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
            // 1. ЖЕСТКАЯ ПРОВЕРКА ПАРАЛЛЕЛЬНОСТИ ЧЕРЕЗ СРАВНЕНИЕ УГЛОВ (Atan2)
            double v1X = s1.End.X - s1.Start.X;
            double v1Y = s1.End.Y - s1.Start.Y;
            double v2X = s2.End.X - s2.Start.X;
            double v2Y = s2.End.Y - s2.Start.Y;

            // Вычисляем углы наклона в градусах (-180...180)
            double angle1 = Math.Atan2(v1Y, v1X) * 180.0 / Math.PI;
            double angle2 = Math.Atan2(v2Y, v2X) * 180.0 / Math.PI;

            // Приводим углы к диапазону 0...180 градусов (направление вектора не имеет значения)
            if (angle1 < 0) angle1 += 180.0;
            if (angle1 >= 180.0) angle1 -= 180.0;

            if (angle2 < 0) angle2 += 180.0;
            if (angle2 >= 180.0) angle2 -= 180.0;

            // Находим чистую разницу между углами
            double angleDiff = Math.Abs(angle1 - angle2);
            if (angleDiff > 90.0) angleDiff = 180.0 - angleDiff; // Учитываем стыковку через 180 град.

            // ДОПУСК ПАРАЛЛЕЛЬНОСТИ: Если разница углов больше 0.5 градуса — линии НЕ параллельны!
            if (angleDiff > 0.5)
                return false;

            // 2. ПРОВЕРЯЕМ, ЛЕЖАТ ЛИ ОНИ НА ОДНОЙ И ТОЙ ЖЕ БЕСКОНЕЧНОЙ ПРЯМОЙ
            double lenS1 = s1.Length;
            if (lenS1 < 0.001) return false;

            // Расстояние от точки Start отрезка s2 до бесконечной прямой s1 (через площадь треугольника)
            double area = Math.Abs((s1.End.Y - s1.Start.Y) * s2.Start.X - (s1.End.X - s1.Start.X) * s2.Start.Y + s1.End.X * s1.Start.Y - s1.End.Y * s1.Start.X);
            double distanceToLine = area / lenS1;

            if (distanceToLine > tolerance)
                return false; // Линии параллельны, но смещены (эффект "рельс")

            // 3. ПРОВЕРЯЕМ ПЕРЕКРЫТИЕ ИНТЕРВАЛОВ ИХ ПРОЕКЦИЙ
            // Проецируем на ось X, если линии более горизонтальные, или на Y, если вертикальные
            bool useX = Math.Abs(v1X) > Math.Abs(v1Y);

            double min1 = useX ? Math.Min(s1.Start.X, s1.End.X) : Math.Min(s1.Start.Y, s1.End.Y);
            double max1 = useX ? Math.Max(s1.Start.X, s1.End.X) : Math.Max(s1.Start.Y, s1.End.Y);
            double min2 = useX ? Math.Min(s2.Start.X, s2.End.X) : Math.Min(s2.Start.Y, s2.End.Y);
            double max2 = useX ? Math.Max(s2.Start.X, s2.End.X) : Math.Max(s2.Start.Y, s2.End.Y);

            // Условие перекрытия двух отрезков на одной числовой оси с учетом инженерного допуска
            return (min1 < max2 - tolerance) && (min2 < max1 - tolerance);
        }
    }
}
