using System;
using System.Collections.Generic;
using System.Linq;
using netDxf;
using netDxf.Entities;

namespace DxfUnfoldChecker
{
    public class DxfExtractor
    {
        private const double MaxArcRadius = 10000.0;
        private const int ArcSegmentsCount = 16;

        public ExtractionResult ExtractAllSegments(string filePath)
        {
            var result = new ExtractionResult();
            DxfDocument dxf = DxfDocument.Load(filePath);

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

                    double step = (angleDiff * Math.PI / 180.0) / ArcSegmentsCount;
                    Point2D prevPoint = pStart;

                    for (int j = 1; j <= ArcSegmentsCount; j++)
                    {
                        double currentRad = startRad + (step * j);
                        double currX = arc.Center.X + arc.Radius * Math.Cos(currentRad) + offsetX;
                        double currY = arc.Center.Y + arc.Radius * Math.Sin(currentRad) + offsetY;
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


        }
    }
}
