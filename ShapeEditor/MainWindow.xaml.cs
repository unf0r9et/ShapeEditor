using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ShapeEditor
{
    public partial class MainWindow : Window
    {
        // === Перетаскивание всей фигуры ===
        private bool potentialDrag;
        private Point startMouse;
        private Canvas draggedShapeCanvas;
        private double startLeft;
        private double startTop;

        // === Перетаскивание якоря (оригинальные имена, как в вашем коде) ===
        private bool draggingAnchor;
        private Point dragStartWorld;          // ← одно объявление!
        private Point originalAnchorPos;
        private Canvas anchorDragCanvas;       // ← одно объявление, без подчёркивания!

        // === Текущая фигура и окно параметров ===
        private ShapeBase _currentShape;
        private Canvas _currentShapeVisual;
        private ShapeParamsWindow _currentParamsWindow;

        // === Коллекции всех фигур на холсте ===
        private List<ShapeBase> _allShapes = new();
        private List<Canvas> _allShapeVisuals = new();
        private Canvas _selectedShapeVisual;
        private Rectangle _boundingBoxVisual;

        // === Флаги ===
        private bool _isProcessingMove;

        public MainWindow()
        {
            InitializeComponent();
            DrawCanvas.MouseLeftButtonDown += DrawCanvas_BackgroundClick;
        }

        private void AddRectangle(object sender, RoutedEventArgs e) => AddShape(new RectangleShape());
        private void AddTriangle(object sender, RoutedEventArgs e) => AddShape(new TriangleShape());
        private void AddTrapezoid(object sender, RoutedEventArgs e) => AddShape(new TrapezoidShape());
        private void AddCircle(object sender, RoutedEventArgs e) => AddShape(new CircleShape());
        private void AddHexagon(object sender, RoutedEventArgs e) => AddShape(new HexagonShape());

        // Новый метод: снятие выделения при клике на пустое место
        private void DrawCanvas_BackgroundClick(object sender, MouseButtonEventArgs e)
        {
            // Проверяем, что клик был именно по Canvas, а не по фигуре внутри
            if (e.OriginalSource == DrawCanvas)
            {
                // Снимаем выделение
                if (_boundingBoxVisual != null)
                    DrawCanvas.Children.Remove(_boundingBoxVisual);
                _boundingBoxVisual = null;
                _selectedShapeVisual = null;

                // Сбрасываем текущую фигуру (но не удаляем её!)
                _currentShape = null;
                _currentShapeVisual = null;
            }
        }
        private void AddShape(ShapeBase shapeBase)
        {
            DrawCanvas.UpdateLayout();

            Point worldAnchor = new(
                DrawCanvas.ActualWidth / 2,
                DrawCanvas.ActualHeight / 2);

            // Дефолтные параметры
            shapeBase.Scale = 1.0;
            shapeBase.Angle = 0;
            shapeBase.Fill = Brushes.Transparent;
            shapeBase.SideColors.Clear();
            shapeBase.SideThickness.Clear();
            for (int i = 0; i < shapeBase.SidesCount; i++)
            {
                shapeBase.SideColors.Add(Brushes.Black);
                shapeBase.SideThickness.Add(3.0);
            }

            var visual = CreateShapeVisual(shapeBase, worldAnchor.X, worldAnchor.Y);
            DrawCanvas.Children.Add(visual);

            _allShapes.Add(shapeBase);
            _allShapeVisuals.Add(visual);

            SelectShape(visual);
        }

        private void SelectShape(Canvas shapeVisual)
        {
            if (_boundingBoxVisual != null)
                DrawCanvas.Children.Remove(_boundingBoxVisual);

            _selectedShapeVisual = shapeVisual;
            ShowBoundingBox(shapeVisual);
        }

        private void ShowBoundingBox(Canvas shapeVisual)
        {
            var shape = shapeVisual.Tag as ShapeBase;
            if (shape == null) return;

            double width = shapeVisual.Width;
            double height = shapeVisual.Height;
            double left = Canvas.GetLeft(shapeVisual);
            double top = Canvas.GetTop(shapeVisual);

            // ← Используем Rectangle вместо Border для поддержки пунктира
            _boundingBoxVisual = new Rectangle
            {
                Width = width,
                Height = height,
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(_boundingBoxVisual, left);
            Canvas.SetTop(_boundingBoxVisual, top);
            DrawCanvas.Children.Add(_boundingBoxVisual);
        }

        private Canvas CreateShapeVisual(ShapeBase shape, double anchorWorldX, double anchorWorldY)
        {
            var canvas = shape.Build(anchorWorldX, anchorWorldY);
            canvas.Tag = shape;

            // События для перетаскивания всей фигуры
            canvas.MouseLeftButtonDown += ShapeCanvas_Down;
            canvas.MouseMove += ShapeCanvas_Move;
            canvas.MouseLeftButtonUp += ShapeCanvas_Up;

            // 🔥 Вешаем события ТОЛЬКО на точку привязки (якорь)
            foreach (UIElement child in canvas.Children)
            {
                if (child is Ellipse ellipse && ellipse.Tag is string tag && tag == "Anchor")
                {
                    ellipse.MouseLeftButtonDown += VertexAnchor_Down;
                    ellipse.MouseMove += VertexAnchor_Move;
                    ellipse.MouseLeftButtonUp += VertexAnchor_Up;
                    ellipse.Cursor = System.Windows.Input.Cursors.SizeAll;
                }
                // Вершины (Ellipse с Tag int) — игнорируем, события не вешаем
            }

            return canvas;
        }

        // ← ЕДИНСТВЕННЫЙ метод RedrawPreservingAnchor (без дублей!)
        private void RedrawPreservingAnchor()
        {
            if (_currentShape == null || _currentShapeVisual == null) return;

            // Сохраняем мировые координаты якоря ДО удаления
            Point worldAnchor = _currentShape.GetAnchorWorldPosition(_currentShapeVisual);
            bool wasSelected = (_selectedShapeVisual == _currentShapeVisual);

            // 🔥 Безопасное удаление: проверяем, что элемент действительно есть в коллекции
            if (DrawCanvas.Children.Contains(_currentShapeVisual))
                DrawCanvas.Children.Remove(_currentShapeVisual);

            if (_boundingBoxVisual != null && DrawCanvas.Children.Contains(_boundingBoxVisual))
                DrawCanvas.Children.Remove(_boundingBoxVisual);

            // Создаём новый визуал
            _currentShapeVisual = CreateShapeVisual(_currentShape, worldAnchor.X, worldAnchor.Y);

            // 🔥 Добавляем только если ещё не добавлен (защита от дублей)
            if (!DrawCanvas.Children.Contains(_currentShapeVisual))
                DrawCanvas.Children.Add(_currentShapeVisual);

            // Восстанавливаем выделение
            if (wasSelected)
            {
                _selectedShapeVisual = _currentShapeVisual;
                ShowBoundingBox(_currentShapeVisual);
            }
        }

        private void VertexAnchor_Down(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Ellipse ellipse) return;

            // Находим фигуру, к которой принадлежит этот якорь
            if (ellipse is FrameworkElement fe && fe.Parent is Canvas shapeCanvas && shapeCanvas.Tag is ShapeBase shape)
            {
                _currentShape = shape;
                _currentShapeVisual = shapeCanvas;
                anchorDragCanvas = shapeCanvas;  // ← без подчёркивания!
            }
            else return;

            if (_currentShape == null) return;

            dragStartWorld = e.GetPosition(DrawCanvas);
            originalAnchorPos = _currentShape.AnchorPoint;
            draggingAnchor = true;

            ellipse.CaptureMouse();
            e.Handled = true;
        }

        private void VertexAnchor_Move(object sender, MouseEventArgs e)
        {
            if (!draggingAnchor || _currentShape == null || anchorDragCanvas == null)
                return;

            if (_isProcessingMove) return;
            _isProcessingMove = true;

            try
            {
                Point currentWorld = e.GetPosition(DrawCanvas);
                Vector deltaWorld = currentWorld - dragStartWorld;
                Vector deltaLocal = deltaWorld / _currentShape.Scale;

                // Обновляем локальные координаты якоря
                _currentShape.AnchorPoint = new Point(
                    Math.Round(originalAnchorPos.X + deltaLocal.X),
                    Math.Round(originalAnchorPos.Y + deltaLocal.Y));

                // Обновляем окно параметров, если открыто
                if (_currentParamsWindow != null)
                {
                    _currentParamsWindow.UpdateLocalAnchor(_currentShape.AnchorPoint);
                    _currentParamsWindow.UpdateWorldAnchor(
                        _currentShape.GetAnchorWorldPosition(_currentShapeVisual));
                }

                // 🔥 Перерисовываем с сохранением захвата мыши
                RedrawPreservingAnchorWithMouseCapture();
            }
            finally
            {
                _isProcessingMove = false;
            }
        }

        // 🔥 НОВАЯ версия RedrawPreservingAnchor для перетаскивания якоря
        private void RedrawPreservingAnchorWithMouseCapture()
        {
            if (_currentShape == null || _currentShapeVisual == null) return;

            Point worldAnchor = _currentShape.GetAnchorWorldPosition(_currentShapeVisual);
            bool wasSelected = (_selectedShapeVisual == _currentShapeVisual);

            // Удаляем старые элементы
            if (DrawCanvas.Children.Contains(_currentShapeVisual))
                DrawCanvas.Children.Remove(_currentShapeVisual);
            if (_boundingBoxVisual != null && DrawCanvas.Children.Contains(_boundingBoxVisual))
                DrawCanvas.Children.Remove(_boundingBoxVisual);

            // Создаём новый визуал
            var oldCanvas = _currentShapeVisual;
            _currentShapeVisual = CreateShapeVisual(_currentShape, worldAnchor.X, worldAnchor.Y);

            if (!DrawCanvas.Children.Contains(_currentShapeVisual))
                DrawCanvas.Children.Add(_currentShapeVisual);

            // Восстанавливаем выделение
            if (wasSelected)
            {
                _selectedShapeVisual = _currentShapeVisual;
                ShowBoundingBox(_currentShapeVisual);
            }

            // 🔥 КЛЮЧЕВОЕ: передаём захват мыши на НОВУЮ красную точку
            foreach (UIElement child in _currentShapeVisual.Children)
            {
                if (child is Ellipse ellipse && ellipse.Tag is string tag && tag == "Anchor")
                {
                    ellipse.CaptureMouse();
                    break;
                }
            }
        }

        private void VertexAnchor_Up(object sender, MouseButtonEventArgs e)
        {
            (sender as Ellipse)?.ReleaseMouseCapture();
            draggingAnchor = false;
            anchorDragCanvas = null;  // ← без подчёркивания!
            e.Handled = true;
        }

        private void ShapeCanvas_Down(object sender, MouseButtonEventArgs e)
        {
            // Если тянем якорь — игнорируем этот обработчик
            if (draggingAnchor) return;

            if (e.ClickCount == 2 && sender is Canvas canvas && canvas.Tag is ShapeBase shape)
            {
                _currentShape = shape;
                _currentShapeVisual = canvas;
                OpenEditWindow(-2);
                e.Handled = true;
                return;
            }

            if (sender is Canvas clickedCanvas)
            {
                SelectShape(clickedCanvas);
            }

            draggedShapeCanvas = sender as Canvas;
            if (draggedShapeCanvas == null) return;

            potentialDrag = true;
            startMouse = e.GetPosition(DrawCanvas);
            startLeft = Canvas.GetLeft(draggedShapeCanvas);
            startTop = Canvas.GetTop(draggedShapeCanvas);

            draggedShapeCanvas.CaptureMouse();
        }

        private void ShapeCanvas_Move(object sender, MouseEventArgs e)
        {
            if (!potentialDrag || draggedShapeCanvas == null) return;

            Point pos = e.GetPosition(DrawCanvas);
            double newLeft = startLeft + (pos.X - startMouse.X);
            double newTop = startTop + (pos.Y - startMouse.Y);

            Canvas.SetLeft(draggedShapeCanvas, newLeft);
            Canvas.SetTop(draggedShapeCanvas, newTop);

            if (_boundingBoxVisual != null && draggedShapeCanvas == _selectedShapeVisual)
            {
                Canvas.SetLeft(_boundingBoxVisual, newLeft);
                Canvas.SetTop(_boundingBoxVisual, newTop);
            }
        }

        private void ShapeCanvas_Up(object sender, MouseButtonEventArgs e)
        {
            draggedShapeCanvas?.ReleaseMouseCapture();
            draggedShapeCanvas = null;
            potentialDrag = false;
        }

        private void OpenEditWindow(int selectedIndex)
        {
            if (_currentShape == null || _currentShapeVisual == null) return;

            Point worldAnchor = _currentShape.GetAnchorWorldPosition(_currentShapeVisual);

            var win = new ShapeParamsWindow(
                _currentShape.SidesCount,
                _currentShape.SideNames,
                _currentShape.SideColors,
                _currentShape.SideThickness,
                _currentShape.Scale,
                _currentShape.Fill,
                _currentShape.Vertices,
                _currentShape.AnchorPoint,
                _currentShape.Angle,
                _currentShape is CircleShape,
                selectedIndex,
                worldAnchor);

            _currentParamsWindow = win;

            if (win.ShowDialog() == true)
            {
                _currentShape.SideColors = win.Colors;
                _currentShape.SideThickness = win.Thicknesses;
                _currentShape.Fill = win.Fill;
                _currentShape.Scale = win.Scale;
                _currentShape.Angle = win.Angle;
                _currentShape.Vertices = win.Vertices ?? _currentShape.Vertices;
                _currentShape.AnchorPoint = win.AnchorPoint;

                RedrawPreservingAnchor();
            }

            _currentParamsWindow = null;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_currentShape == null || _currentShapeVisual == null) return;

            _currentShape.Scale = e.NewValue;
            ScaleValueText.Text = e.NewValue.ToString("0.00");
            RedrawPreservingAnchor();
        }

        private void AngleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_currentShape == null || _currentShapeVisual == null) return;

            _currentShape.Angle = e.NewValue;
            AngleValueText.Text = $"{(int)e.NewValue}°";
            RedrawPreservingAnchor();
        }
    }
}