using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DxfUnfoldChecker
{
    public partial class DetailViewerWindow : Window
    {
        private System.Windows.Point _panStart;
        private bool _isPanning = false;

        // НОВЫЙ СЛОЙ: Контейнер для маркеров разрыва, чтобы скрывать их мгновенно
        private Canvas _markersLayer;

        public DetailViewerWindow(FileValidationSummary fileSummary)
        {
            InitializeComponent();

            TxtFileName.Text = fileSummary.FileName;
            LstWarnings.ItemsSource = fileSummary.Extraction.Warnings;

            PopulateLocalDefects(fileSummary.Topology, fileSummary.Cleanliness);
            RenderFileGeometry(fileSummary.Topology, fileSummary.Cleanliness);
        }

        // --- ЛОГИКА УПРАВЛЕНИЯ ГАЛОЧКОЙ ВИДИМОСТИ МАРКЕРОВ ---
        private void ChkVizMarkers_Checked(object sender, RoutedEventArgs e)
        {
            if (_markersLayer != null)
                _markersLayer.Visibility = Visibility.Visible;
        }

        private void ChkVizMarkers_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_markersLayer != null)
                _markersLayer.Visibility = Visibility.Collapsed; // Полностью скрываем слой с экрана
        }

        // --- ЛОГИКА ИНТЕРАКТИВНОГО УПРАВЛЕНИЯ ЧЕРТЕЖОМ (ZOOM & PAN) ---
        private void CadCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            System.Windows.Point mouseInWindow = e.GetPosition(CadScrollViewer);
            System.Windows.Point mouseInCanvas = e.GetPosition(CadCanvas);

            double zoomFactor = e.Delta > 0 ? 1.1 : 0.9;
            double newScaleX = CanvasScale.ScaleX * zoomFactor;

            if (newScaleX < 0.1 || newScaleX > 50) return;

            CanvasScale.CenterX = 0;
            CanvasScale.CenterY = 0;

            CanvasScale.ScaleX = newScaleX;
            CanvasScale.ScaleY = newScaleX * -1.0;

            CanvasTranslate.X = mouseInWindow.X - (mouseInCanvas.X * CanvasScale.ScaleX);
            CanvasTranslate.Y = mouseInWindow.Y - (mouseInCanvas.Y * CanvasScale.ScaleY);

            e.Handled = true;
        }

        private void CadCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _panStart = e.GetPosition(CadScrollViewer);
                _isPanning = true;
                MouseCatchGrid.CaptureMouse();
            }
        }

        private void CadCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isPanning = false;
                MouseCatchGrid.ReleaseMouseCapture();
            }
        }

        private void CadCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                System.Windows.Point currentPos = e.GetPosition(CadScrollViewer);
                Vector delta = currentPos - _panStart;
                _panStart = currentPos;

                CanvasTranslate.X += delta.X;
                CanvasTranslate.Y += delta.Y;
            }
        }

        // --- НАПОЛНЕНИЕ ТАБЛИЦЫ И РЕНДЕРИНГ ГЕОМЕТРИИ ---
        private void PopulateLocalDefects(TopologyResult topology, CleanlinessReport cleanliness)
        {
            var uiRows = new List<UiDefectRow>();
            if (topology.OpenContours.Count > 0)
            {
                uiRows.Add(new UiDefectRow("РАЗРЫВ", $"Найдено {topology.OpenContours.Count} зазоров/незамкнутых петель."));
            }
            foreach (var defect in cleanliness.OverlappingDefects)
            {
                uiRows.Add(new UiDefectRow("НАЛОЖЕНИЕ", defect.Description));
            }
            DgDefects.ItemsSource = uiRows;
        }

        private void RenderFileGeometry(TopologyResult topology, CleanlinessReport cleanliness)
        {
            CadCanvas.Children.Clear();

            // Инициализируем наш отдельный слой для маркеров-мишеней
            _markersLayer = new Canvas
            {
                Width = CadCanvas.Width,
                Height = CadCanvas.Height,
                Background = Brushes.Transparent
            };

            var allPoints = new List<Point2D>();
            foreach (var c in topology.ClosedContours) foreach (var s in c.Segments) { allPoints.Add(s.Start); allPoints.Add(s.End); }
            foreach (var c in topology.OpenContours) foreach (var s in c.Segments) { allPoints.Add(s.Start); allPoints.Add(s.End); }

            if (allPoints.Count == 0) return;

            double minX = allPoints.Min(p => p.X);
            double minY = allPoints.Min(p => p.Y);
            double maxX = allPoints.Max(p => p.X);
            double maxY = allPoints.Max(p => p.Y);

            double padding = 60;
            CadCanvas.Width = (maxX - minX) + (padding * 2);
            CadCanvas.Height = (maxY - minY) + (padding * 2);

            // Синхронизируем размер слоя маркеров с основным холстом
            _markersLayer.Width = CadCanvas.Width;
            _markersLayer.Height = CadCanvas.Height;

            CanvasScale.CenterX = 0; CanvasScale.CenterY = 0;
            CanvasScale.ScaleX = 1.0; CanvasScale.ScaleY = -1.0;
            CanvasTranslate.X = 0; CanvasTranslate.Y = 0;

            System.Windows.Point ToCanvasPoint(Point2D pt)
            {
                return new System.Windows.Point(pt.X - minX + padding, pt.Y - minY + padding);
            }

            // 1. Рисуем замкнутые контуры (Серые)
            foreach (var contour in topology.ClosedContours)
            {
                foreach (var seg in contour.Segments)
                {
                    DrawLocalLine(ToCanvasPoint(seg.Start), ToCanvasPoint(seg.End), Brushes.LightGray, 1.2);
                }
            }

            // 2. Рисуем разомкнутые контуры (Красные) + МАРКЕРЫ
            foreach (var contour in topology.OpenContours)
            {
                foreach (var seg in contour.Segments)
                {
                    DrawLocalLine(ToCanvasPoint(seg.Start), ToCanvasPoint(seg.End), Brushes.Red, 1.8);
                }

                if (contour.Segments.Count > 0)
                {
                    var trueStart = ToCanvasPoint(contour.Segments.First().Start);
                    var trueEnd = ToCanvasPoint(contour.Segments.Last().End);

                    // ВАЖНО: Передаем точки разрыва в метод, который запишет их в отдельный слой
                    DrawTargetMarker(trueStart);
                    DrawTargetMarker(trueEnd);
                }
            }

            // 3. Рисуем наложения линий (Циан)
            foreach (var defect in cleanliness.OverlappingDefects)
            {
                DrawLocalLine(ToCanvasPoint(defect.Segment.Start), ToCanvasPoint(defect.Segment.End), Brushes.Cyan, 2.2);
            }

            // В самом конце добавляем слой маркеров ПОВЕРХ всей геометрии на главный Canvas
            CadCanvas.Children.Add(_markersLayer);

            // Проверяем текущее состояние галочки при загрузке (если инженер открыл окно, а галочка была снята)
            if (ChkVizMarkers != null && ChkVizMarkers.IsChecked == false)
            {
                _markersLayer.Visibility = Visibility.Collapsed;
            }
        }

        private void DrawTargetMarker(System.Windows.Point pos)
        {
            // ИСПРАВЛЕНО: Добавляем круги не на CadCanvas, а в наш изолированный слой _markersLayer
            Ellipse outerRing = new Ellipse
            {
                Width = 12,
                Height = 12,
                Stroke = Brushes.Magenta,
                StrokeThickness = 2,
                Fill = Brushes.Yellow
            };
            Canvas.SetLeft(outerRing, pos.X - 6);
            Canvas.SetTop(outerRing, pos.Y - 6);
            _markersLayer.Children.Add(outerRing);

            Ellipse centerDot = new Ellipse { Width = 4, Height = 4, Fill = Brushes.Red };
            Canvas.SetLeft(centerDot, pos.X - 2);
            Canvas.SetTop(centerDot, pos.Y - 2);
            _markersLayer.Children.Add(centerDot);
        }

        private void DrawLocalLine(System.Windows.Point p1, System.Windows.Point p2, Brush brush, double thickness)
        {
            System.Windows.Shapes.Line line = new System.Windows.Shapes.Line { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = brush, StrokeThickness = thickness };
            CadCanvas.Children.Add(line);
        }
    }
}
