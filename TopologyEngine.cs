using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;



namespace DxfUnfoldChecker
{
    public class Contour
    {
        public List<AnalyticSegment> Segments { get; set; } = new();
        public bool IsClosed { get; set; }
    }

    public class TopologyResult
    {
        public List<Contour> ClosedContours { get; set; } = new();
        public List<Contour> OpenContours { get; set; } = new();
    }


    public class TopologyEngine
    {
        public TopologyResult BuildContours(List<AnalyticSegment> inputSegments, double tolerance = 0.015)
        {
            var result = new TopologyResult();
            // Делаем копию списка, чтобы оригинальный листинг не пострадал
            var remainingSegments = new List<AnalyticSegment>(inputSegments);

            // Вместо удаления всего, что меньше допуска (0.05),
            // отсекаем ТОЛЬКО чистые математические нули (точки), чтобы граф не зациклился
            remainingSegments.RemoveAll(s => s.Start.X == s.End.X && s.Start.Y == s.End.Y);


            while (remainingSegments.Count > 0)
            {
                var currentContour = new Contour();

                // Берем первый попавшийся отрезок как стартовую нить контура
                var currentSeg = remainingSegments[0];
                currentContour.Segments.Add(currentSeg);
                remainingSegments.RemoveAt(0);

                Point2D contourStart = currentSeg.Start;
                Point2D currentNeedle = currentSeg.End; // Точка, для которой ищем продолжение

                bool contourIsGrowing = true;

                while (contourIsGrowing)
                {
                    // Проверяем: может контур уже замкнулся сам на себя?
                    if (currentNeedle.IsCloseTo(contourStart, tolerance))
                    {
                        currentContour.IsClosed = true;
                        break;
                    }

                    // Ищем следующий отрезок, который стыкуется с нашей "иглой"
                    int nextSegIndex = -1;
                    bool needReverse = false;

                    for (int i = 0; i < remainingSegments.Count; i++)
                    {
                        var candidate = remainingSegments[i];

                        // Вариант 1: Стыкуется Старт отрезка (идеальный случай)
                        if (candidate.Start.IsCloseTo(currentNeedle, tolerance))
                        {
                            nextSegIndex = i;
                            needReverse = false;
                            break;
                        }
                        // Вариант 2: Стыкуется Конец отрезка (отрезок перевернут задом наперед)
                        if (candidate.End.IsCloseTo(currentNeedle, tolerance))
                        {
                            nextSegIndex = i;
                            needReverse = true;
                            break;
                        }
                    }

                    if (nextSegIndex != -1)
                    {
                        var foundSegment = remainingSegments[nextSegIndex];
                        remainingSegments.RemoveAt(nextSegIndex);

                        if (needReverse)
                        {
                            // Разворачиваем отрезок, чтобы контур шел строго в одном направлении
                            foundSegment = new AnalyticSegment(foundSegment.End, foundSegment.Start);
                        }

                        currentContour.Segments.Add(foundSegment);
                        currentNeedle = foundSegment.End; // Двигаем рабочую точку вперед
                    }
                    else
                    {
                        // Стыковок больше нет — контур оборвался! Тупик или зазор
                        currentContour.IsClosed = false;
                        contourIsGrowing = false;
                    }
                }

                // Раскладываем результат по правильным корзинам
                if (currentContour.IsClosed)
                {
                    result.ClosedContours.Add(currentContour);
                }
                else
                {
                    result.OpenContours.Add(currentContour);
                }
            }

            return result;
        }
    }



}


