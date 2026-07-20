using System;

namespace DxfValid.Core
{
    // Наша чистая 2D точка для аналитики
    public record Point2D(double X, double Y)
    {
        // Метод сравнения двух точек с учетом зазора/допуска
        public bool IsCloseTo(Point2D other, double tolerance)
        {
            double dx = X - other.X;
            double dy = Y - other.Y;
            return (dx * dx + dy * dy) <= (tolerance * tolerance);
        }
    }

    // Универсальный отрезок, в который превратится ВСЯ геометрия чертежа
    public record AnalyticSegment(Point2D Start, Point2D End)
    {
        // Длина отрезка для отсечения микро-мусора
        public double Length => Math.Sqrt((End.X - Start.X) * (End.X - Start.X) + (End.Y - Start.Y) * (End.Y - Start.Y));
    }


    // Тип предупреждения для классификации
    public enum WarningType
    {
        ArcLinearized,  // Дуга превращена в линию из-за огромного радиуса
        BlockExploded   // Извлечены элементы из скрытого блока
    }

    // Объект сообщения для инженера
    public record GeometryWarning(WarningType Type, string Layer, string Message);

    // Результат работы первого класса (Экстрактора)
    public class ExtractionResult
    {
        public List<AnalyticSegment> Segments { get; set; } = new();
        public List<GeometryWarning> Warnings { get; set; } = new();

        // ДОБАВЬТЕ ЭТУ СТРОКУ (Сюда складываются сегменты без мелкой нарезки дуг)
        public List<AnalyticSegment> CleanlinessSegments { get; set; } = new();
    }

    // Добавьте это в самый конец файла GeometryStructures.cs inside namespace DxfValid
    public record UiDefectRow(string Type, string Description);


}
