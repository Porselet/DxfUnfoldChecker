using IxMilia.Dxf;
using IxMilia.Dxf.Entities;
using netDxf;
using netDxf.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using static netDxf.Entities.HatchBoundaryPath;

namespace DxfValid.Core
{
    public class DxfExtractor
    {
        private const double MaxArcRadius = 10000.0;
        private const int ArcSegmentsCount = 16;
        private const int CircleSegmentsCount = 32;

        public ExtractionResult ExtractAllSegments(string filePath)
        {
            try
            {
                // ПОПЫТКА 1: Пробуем прочитать файл через быстрый netDxf
                return ExtractUsingNetDxf(filePath);
            }
            catch (Exception ex) when (ex.Message.Contains("AutoCad12") || ex.Message.Contains("version not supported"))
            {
                // ПОПЫТКА 2: Мягко переключаемся на IxMilia, если версия слишком старая
                return ExtractUsingIxMilia(filePath);
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка разбора файла: {ex.Message}", ex);
            }
        }

        // ====================================================================
        // ДВИЖОК №2: РЕЗЕРВНЫЙ ПАРСЕР ДЛЯ AUTOCAD R12 (IxMilia)
        // ====================================================================
        private ExtractionResult ExtractUsingIxMilia(string filePath)
        {
            var result = new ExtractionResult();

            // IxMilia нативно загружает AutoCAD R12 без ошибок
            DxfFile dxf = DxfFile.Load(filePath);

            foreach (var entity in dxf.Entities)
            {
                // 1. Обрабатываем линии AutoCAD R12
                if (entity is DxfLine line)
                {
                    var start = new Point2D(line.P1.X, line.P1.Y);
                    var end = new Point2D(line.P2.X, line.P2.Y);
                    var seg = new AnalyticSegment(start, end);
                    result.Segments.Add(seg);
                    result.CleanlinessSegments.Add(seg);
                }
                // 2. Обрабатываем окружности AutoCAD R12
                else if (entity is DxfCircle circle)
                {
                    double step = (2.0 * Math.PI) / CircleSegmentsCount;
                    double startX = circle.Center.X + circle.Radius * Math.Cos(0);
                    double startY = circle.Center.Y + circle.Radius * Math.Sin(0);
                    var firstPoint = new Point2D(startX, startY);
                    Point2D prevPoint = firstPoint;

                    for (int j = 1; j < CircleSegmentsCount; j++)
                    {
                        double currentRad = step * j;
                        double currX = circle.Center.X + circle.Radius * Math.Cos(currentRad);
                        double currY = circle.Center.Y + circle.Radius * Math.Sin(currentRad);
                        var currPoint = new Point2D(currX, currY);

                        var seg = new AnalyticSegment(prevPoint, currPoint);
                        result.Segments.Add(seg); result.CleanlinessSegments.Add(seg);
                        prevPoint = currPoint;
                    }
                    var lastSeg = new AnalyticSegment(prevPoint, firstPoint);
                    result.Segments.Add(lastSeg); result.CleanlinessSegments.Add(lastSeg);
                }// 3. Обрабатываем дуги AutoCAD R12
                else if (entity is DxfArc arc)
                {
                    double startRad = arc.StartAngle * Math.PI / 180.0; double endRad = arc.EndAngle * Math.PI / 180.0;
                    double startX = arc.Center.X + arc.Radius * Math.Cos(startRad);
                    double startY = arc.Center.Y + arc.Radius * Math.Sin(startRad);
                    double endX = arc.Center.X + arc.Radius * Math.Cos(endRad);
                    double endY = arc.Center.Y + arc.Radius * Math.Sin(endRad);
                    var pStart = new Point2D(startX, startY);
                    var pEnd = new Point2D(endX, endY);
                    if (arc.Radius > MaxArcRadius)
                    {
                        result.Warnings.Add(new GeometryWarning(WarningType.ArcLinearized, arc.Layer ?? "0", $"[R12-Движок] Дуга с R={arc.Radius:F1} мм программно выпрямлена."));
                        var seg = new AnalyticSegment(pStart, pEnd); result.Segments.Add(seg); result.CleanlinessSegments.Add(seg);
                    }
                    else
                    {
                        double angleDiff = arc.EndAngle - arc.StartAngle;
                        if (angleDiff < 0) angleDiff += 360;

                        result.CleanlinessSegments.Add(new AnalyticSegment(pStart, pEnd));

                        // ИСПРАВЛЕНО: Считаем количество шагов адаптивно под размер дуги для R12!
                        int adaptiveCount = CalculateAdaptiveArcSegments(arc.Radius, arc.StartAngle, arc.EndAngle);
                        double step = (angleDiff * Math.PI / 180.0) / adaptiveCount;
                        Point2D prevPoint = pStart;

                        for (int j = 1; j <= adaptiveCount; j++)
                        {
                            double currentRad = startRad + (step * j);
                            double currX = arc.Center.X + arc.Radius * Math.Cos(currentRad);
                            double currY = arc.Center.Y + arc.Radius * Math.Sin(currentRad);
                            var currPoint = new Point2D(currX, currY);

                            result.Segments.Add(new AnalyticSegment(prevPoint, currPoint));
                            prevPoint = currPoint;
                        }
                    }
                }
            }
            return result;
        }



        public ExtractionResult ExtractUsingNetDxf(string filePath)
        {
            var result = new ExtractionResult();
            DxfDocument dxf = DxfDocument.Load(filePath);
            var debugArcsList = dxf.Entities.Arcs.ToList();
            // 1. Извлекаем из ModelSpace
            ExtractFromEntityCollection(dxf.Entities.All, result, "ModelSpace");

            // 2. Извлекаем из Блоков
            foreach (var insert in dxf.Entities.Inserts)
            {
                double offsetX = insert.Position.X;
                double offsetY = insert.Position.Y;

                result.Warnings.Add(new GeometryWarning(
                    WarningType.BlockExploded,
                    insert.Layer.Name,
                    $"Раскрыт блок '{insert.Block.Name}' в точке ({offsetX:F2}; {offsetY:F2}). Извлечено геометрии: {insert.Block.Entities.Count} шт."
                ));

                ExtractFromEntityCollection(insert.Block.Entities, result, insert.Layer.Name, offsetX, offsetY);
            }

            return result;
        }

        // ИСПРАВЛЕНО: Принимаем System.Collections.IEnumerable, чтобы подходил любой тип коллекции netDxf
        private void ExtractFromEntityCollection(System.Collections.IEnumerable entities, ExtractionResult result, string defaultLayer, double offsetX = 0, double offsetY = 0)
        {
            // Обрабатываем обычные линии
            foreach (var line in entities.OfType<netDxf.Entities.Line>())
            {
                var start = new Point2D(line.StartPoint.X + offsetX, line.StartPoint.Y + offsetY);
                var end = new Point2D(line.EndPoint.X + offsetX, line.EndPoint.Y + offsetY);
                result.Segments.Add(new AnalyticSegment(start, end));
            }

            // Обрабатываем полилинии
            foreach (var poly in entities.OfType<Polyline2D>())
            {
                for (int i = 0; i < poly.Vertexes.Count - 1; i++)
                {
                    var start = new Point2D(poly.Vertexes[i].Position.X + offsetX, poly.Vertexes[i].Position.Y + offsetY);
                    var end = new Point2D(poly.Vertexes[i + 1].Position.X + offsetX, poly.Vertexes[i + 1].Position.Y + offsetY);
                    result.Segments.Add(new AnalyticSegment(start, end));
                }
                if (poly.IsClosed && poly.Vertexes.Count > 1)
                {
                    var start = new Point2D(poly.Vertexes.Last().Position.X + offsetX, poly.Vertexes.Last().Position.Y + offsetY);
                    var end = new Point2D(poly.Vertexes.First().Position.X + offsetX, poly.Vertexes.First().Position.Y + offsetY);
                    result.Segments.Add(new AnalyticSegment(start, end));
                }
            }

            // Обрабатываем дуги

            foreach (var arc in entities.OfType<netDxf.Entities.Arc>())
            {
                string currentLayer = arc.Layer?.Name ?? defaultLayer;

                double startRad = arc.StartAngle * Math.PI / 180.0;
                double endRad = arc.EndAngle * Math.PI / 180.0;

                double startX = arc.Center.X + arc.Radius * Math.Cos(startRad) + offsetX;
                double startY = arc.Center.Y + arc.Radius * Math.Sin(startRad) + offsetY;
                double endX = arc.Center.X + arc.Radius * Math.Cos(endRad) + offsetX;
                double endY = arc.Center.Y + arc.Radius * Math.Sin(endRad) + offsetY;

                var pStart = new Point2D(startX, startY);
                var pEnd = new Point2D(endX, endY);

                if (arc.Radius > MaxArcRadius)
                {
                    double deltaAngle = Math.Abs(arc.EndAngle - arc.StartAngle);
                    result.Warnings.Add(new GeometryWarning(
                        WarningType.ArcLinearized,
                        currentLayer,
                        $"Дуга с R={arc.Radius:F1} мм и углами {arc.StartAngle:F4}°...{arc.EndAngle:F4}° (Δ={deltaAngle:F6}°) программно заменена прямой линией во избежание тригонометрических ошибок WPF."
                    ));

                    result.Segments.Add(new AnalyticSegment(pStart, pEnd));
                }
                else
                {
                    double angleDiff = arc.EndAngle - arc.StartAngle;
                    if (angleDiff < 0) angleDiff += 360;

                    // БЫЛО: result.CleanlinessSegments.Add(new AnalyticSegment(pStart, pEnd)); // Строило ложную тетиву

                    // СТАЛО: Чтобы не было ложных наложений на полуокружностях, для инспектора чистоты
                    // мы берем среднюю точку дуги (на половине её углового диапазона) 
                    // и строим микро-отрезок, который никогда не совпадет со встречной полуокружностью!
                    double midAngleRad = (arc.StartAngle + angleDiff / 2.0) * Math.PI / 180.0;
                    double midX = arc.Center.X + arc.Radius * Math.Cos(midAngleRad);
                    double midY = arc.Center.Y + arc.Radius * Math.Sin(midAngleRad);
                    var pMid = new Point2D(midX, midY);

                    // Добавляем в инспектор чистоты излом дуги (от старта к середине и от середины к финишу)
                    result.CleanlinessSegments.Add(new AnalyticSegment(pStart, pMid));
                    result.CleanlinessSegments.Add(new AnalyticSegment(pMid, pEnd));

                    // ИСПРАВЛЕНО: Считаем количество шагов адаптивно под размер дуги!
                    int adaptiveCount = CalculateAdaptiveArcSegments(arc.Radius, arc.StartAngle, arc.EndAngle);
                    double step = (angleDiff * Math.PI / 180.0) / adaptiveCount;
                    Point2D prevPoint = pStart;

                    for (int j = 1; j <= adaptiveCount; j++)
                    {
                        double currentRad = startRad + (step * j);
                        double currX = arc.Center.X + arc.Radius * Math.Cos(currentRad);
                        double currY = arc.Center.Y + arc.Radius * Math.Sin(currentRad);
                        var currPoint = new Point2D(currX, currY);

                        result.Segments.Add(new AnalyticSegment(prevPoint, currPoint));
                        prevPoint = currPoint;
                    }
                }
            }
            // ====================================================================
            // ИСПРАВЛЕНО: Добавлена обработка полноценных окружностей (CIRCLES)
            // ====================================================================
            foreach (var circle in entities.OfType<Circle>())
            {
                // Дробим окружность на 32 сегмента для высокой точности (можно поставить 16 или 64)
                const int circleSegmentsCount = 32;
                double step = (2.0 * Math.PI) / circleSegmentsCount;

                // Вычисляем самую первую точку на окружности (угол 0 радиан)
                double startX = circle.Center.X + circle.Radius * Math.Cos(0) + offsetX;
                double startY = circle.Center.Y + circle.Radius * Math.Sin(0) + offsetY;
                var firstPoint = new Point2D(startX, startY);

                Point2D prevPoint = firstPoint;

                // Строим цепочку хорд по всему периметру окружности
                for (int j = 1; j < circleSegmentsCount; j++)
                {
                    double currentRad = step * j;
                    double currX = circle.Center.X + circle.Radius * Math.Cos(currentRad) + offsetX;
                    double currY = circle.Center.Y + circle.Radius * Math.Sin(currentRad) + offsetY;
                    var currPoint = new Point2D(currX, currY);

                    // Добавляем кусочек окружности как прямой сегмент
                    result.Segments.Add(new AnalyticSegment(prevPoint, currPoint));
                    prevPoint = currPoint;
                }

                // Замыкаем окружность: соединяем последнюю точку с самой первой
                result.Segments.Add(new AnalyticSegment(prevPoint, firstPoint));
            }

            // ====================================================================
            // НОВОЕ: ОБРАБОТКА СПЛАЙНОВ (SPLINES) — ВОЗВРАЩАЕМ 765 ЭЛЕМЕНТОВ ИЗ NX!
            // ====================================================================
            foreach (var spline in entities.OfType<netDxf.Entities.Spline>())
            {
                // Превращаем сложный сплайн в плоскую полилинию. 
                // Параметр 10 означает, что каждая кривая разобьется на 10 точных хорд.
                var polyRepresentation = spline.ToPolyline2D(10);

                if (polyRepresentation != null && polyRepresentation.Vertexes.Count() > 1)
                {
                    // Пробегаем по сгенерированным вершинам и превращаем их в наши AnalyticSegment
                    for (int i = 0; i < polyRepresentation.Vertexes.Count() - 1; i++)
                    {
                        var start = new Point2D(polyRepresentation.Vertexes[i].Position.X, polyRepresentation.Vertexes[i].Position.Y);
                        var end = new Point2D(polyRepresentation.Vertexes[i + 1].Position.X, polyRepresentation.Vertexes[i + 1].Position.Y);
                        var seg = new AnalyticSegment(start, end);

                        result.Segments.Add(seg);
                        result.CleanlinessSegments.Add(seg);
                    }

                    // Если сплайн был замкнутым в NX, соединяем конец с началом
                    if (spline.IsClosed)
                    {
                        var start = new Point2D(polyRepresentation.Vertexes.Last().Position.X, polyRepresentation.Vertexes.Last().Position.Y);
                        var end = new Point2D(polyRepresentation.Vertexes.First().Position.X, polyRepresentation.Vertexes.First().Position.Y);
                        var seg = new AnalyticSegment(start, end);

                        result.Segments.Add(seg);
                        result.CleanlinessSegments.Add(seg);
                    }
                }
            }



        }


        private int CalculateAdaptiveArcSegments(double radius, double startAngle, double endAngle)
        {
            double angleDiff = endAngle - startAngle;
            if (angleDiff < 0) angleDiff += 360;

            // Считаем полную физическую длину дуги в мм
            double arcLength = radius * (angleDiff * Math.PI / 180.0);

            // АДАПТИВНЫЙ ИНЖЕНЕРНЫЙ ПОДХОД:
            if (arcLength < 1.0) return 2;  // Микро-дуги (до 1 мм) делим всего на 2 части
            if (arcLength < 5.0) return 4;  // Мелкие скругления (до 5 мм) — на 4 части
            if (arcLength < 20.0) return 8;  // Средние — на 8 частей
            return 16;                       // Крупные дуги — на 16 частей
        }

    }
}
