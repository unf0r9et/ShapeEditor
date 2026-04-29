using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Linq;
using Drawing = System.Drawing;
using System.IO;
using System.Reflection;
using Microsoft.Win32;
using System.Text.Json;           
using System.Globalization;   

namespace ShapeEditor
{
    public partial class MainWindow : Window
    {
        // Перетаскивание 
        private bool potentialDrag;
        private Point startMouse;
        private Canvas draggedShapeCanvas;
        private double startLeft;
        private double startTop;
        // Для редакта ребёнка 
        private bool _isEditingChildInPlace = false;
        private Canvas _originalParentVisual = null;    
        private Canvas _childNestedVisual = null;       
        private double _childWorldX, _childWorldY;      
        private Point _childOriginalAnchor;              
        // Перетаскивание якоря
        private bool draggingAnchor;
        private Point dragStartWorld;
        private Point originalAnchorPos;
        private Canvas anchorDragCanvas;

        private ShapeBase _currentShape;
        private Canvas _currentShapeVisual;
        private Canvas _selectedShapeVisual;
        private Rectangle _boundingBoxVisual;

        // массив 
        private ShapeBase[] _allShapes = new ShapeBase[0];
        private Canvas[] _allShapeVisuals = new Canvas[0];

        private bool _isProcessingMove;

        // панель параметров
        private Button _paramsShowButton;
        private StackPanel _paramsStackPanel;
        private bool _paramsPanelIsOpen = false;

        private List<TextBox> _colorTextBoxes = new();
        private List<Border> _colorSwatches = new();
        private List<TextBox> _thicknessTextBoxes = new();
        private Border _fillSwatch;
        private TextBox _fillTextBox;

        private List<TextBox> _vertexXBoxes = new();
        private List<TextBox> _vertexYBoxes = new();

        private List<TextBox> _vertexWorldXBoxes = new();
        private List<TextBox> _vertexWorldYBoxes = new();

        private TextBox _localAnchorXBox, _localAnchorYBox;
        private TextBox _worldAnchorXBox, _worldAnchorYBox;

        private Slider _scaleSlider;
        private TextBox _scaleTextBox;
        private Slider _angleSlider;
        private TextBox _angleTextBox;

        private TextBox _bboxBottomLeftX, _bboxBottomLeftY;
        private TextBox _bboxTopRightX, _bboxTopRightY;

        // Длины рёбер
        private List<TextBox> _edgeLengthBoxes = new();
        private List<CheckBox> _edgeLockBoxes = new();
        private CheckBox _isoscelesCheckBox;

        // Флаг, чтобы отличать программное обновление полей длины рёбер от пользовательского ввода
        private bool _isUpdatingEdgeLengthText;

        // Отложенные значения, введённые пользователем, применяются по потере фокуса или по кнопке Применить
        private List<double?> _pendingEdgeLengths = new();

        private bool _isCreatingCustomShape = false;
        private CustomShape _creatingCustomShape = null;
        private int _creatingNextIndex = 0;

        private TextBox _newSegmentLengthBox;
        private TextBox _newSegmentAngleBox;
        private Button _setSegmentButton;
        private Button _closeShapeButton;
        private Button _cancelCreateButton;

        private System.Windows.Shapes.Shape _segmentHighlight = null;
        private UIElement _segmentHighlightContainer = null;

        // Поля для работы с комплексными фигурами
        public CompoundShape _editingParentCompound = null; 
        private ShapeBase _childShapeBeforeEdit = null;      

        private List<Canvas> _selectedVisuals = new();


        private List<ShapeTreeItem> _treeRootItems = new();

        private const string FILE_EXTENSION = ".json";
        private const string FILE_FILTER = "JSON файлы (*.json)|*.json|Все файлы (*.*)|*.*";
        // private const string FILE_HEADER = "SHAPEEDITOR";
        private const int FILE_VERSION = 1;
        public MainWindow()
        {
            InitializeComponent();
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
            DrawCanvas.MouseLeftButtonDown += DrawCanvas_BackgroundClick;
        }

        private Point GetVertexWorldPosition(ShapeBase shape, Canvas visual, int vertexIndex)
        {
            if (shape == null || visual == null)
                return new Point(0, 0);

            var v = shape.Vertices[vertexIndex];
            var anchor = shape.AnchorPoint;

            double angleRad = shape.Angle * Math.PI / 180.0;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);

            // 1. Поворот вокруг якоря
            double dx = v.X - anchor.X;
            double dy = v.Y - anchor.Y;
            double rotatedX = anchor.X + dx * cos - dy * sin;
            double rotatedY = anchor.X + dx * sin + dy * cos; // исправлено здесь

            // 2. Масштабирование
            double scaledX = rotatedX * shape.Scale;
            double scaledY = rotatedY * shape.Scale;

            // 3. Позиция Canvas
            double canvasLeft = Canvas.GetLeft(visual);
            double canvasTop = Canvas.GetTop(visual);

            double minX = shape.MinX;
            double minY = shape.MinY;

            return new Point(canvasLeft + scaledX - minX,
                             canvasTop + scaledY - minY);
        }

        private void AddRectangle(object sender, RoutedEventArgs e) => AddShape(new RectangleShape());
        private void AddTriangle(object sender, RoutedEventArgs e) => AddShape(new TriangleShape());
        private void AddTrapezoid(object sender, RoutedEventArgs e) => AddShape(new TrapezoidShape());
        private void AddCircle(object sender, RoutedEventArgs e) => AddShape(new CircleShape());
        private void AddHexagon(object sender, RoutedEventArgs e) => AddShape(new HexagonShape());
        private void AddCustomShape(object sender, RoutedEventArgs e)
        {
            // Начинаем интерактивное создание кастомной фигуры
            var custom = new CustomShape();
            custom.Scale = 1.0;
            custom.Angle = 0;
            custom.Fill = Brushes.Transparent;

            // Добавляем в списки и создаём пустой визуал в центре
            DrawCanvas.UpdateLayout();
            //custom.Id = GetNextAvailableId(); // НАЗНАЧАЕМ ID ТУТ

            var visual = CreateShapeVisual(custom, DrawCanvas.ActualWidth / 2, DrawCanvas.ActualHeight / 2);
            DrawCanvas.Children.Add(visual);
            AddShapeToArray(custom, visual); 

            // Ставим в режим создания
            _isCreatingCustomShape = true;
            _creatingCustomShape = custom;
            _creatingNextIndex = 0;

            // Выбираем этот визуал 
            SelectShape(visual);
            RefreshShapesTree();
        }

        private void AddCompoundShape(object sender, RoutedEventArgs e)
        {
            var compound = new CompoundShape();
            // Список пуст изначально
            //compound.Id = GetNextAvailableId(); // НАЗНАЧАЕМ ID ТУТ

            var visual = CreateShapeVisual(compound, DrawCanvas.ActualWidth / 2, DrawCanvas.ActualHeight / 2);
            DrawCanvas.Children.Add(visual);
            AddShapeToArray(compound, visual); 

            SelectShape(visual);
            RefreshShapesTree();
        }

        private void DrawCanvas_BackgroundClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == DrawCanvas)
                ClearSelection();
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Пропускаем, если фокус на TextBox (чтобы не удалять фигуру при редактировании чисел)
            if (Keyboard.FocusedElement is TextBox) return;

            if (e.Key == Key.Delete && _selectedShapeVisual != null)
            {
                // Удаляем выделенную фигуру
                if (DrawCanvas.Children.Contains(_selectedShapeVisual))
                    DrawCanvas.Children.Remove(_selectedShapeVisual);

                if (_boundingBoxVisual != null && DrawCanvas.Children.Contains(_boundingBoxVisual))
                    DrawCanvas.Children.Remove(_boundingBoxVisual);

                // Удаляем из массива
                RemoveShapeFromArray(_currentShape);

                // Снимаем выделение
                _selectedShapeVisual = null;
                _currentShape = null;
                _currentShapeVisual = null;
                _boundingBoxVisual = null;

                // Обновляем панель параметров
                UpdateParamsPanelVisibility();
                RefreshShapesTree();
                e.Handled = true;
            }
        }
        private void ClearSelection()
        {
            if (_boundingBoxVisual != null)
                DrawCanvas.Children.Remove(_boundingBoxVisual);

            _boundingBoxVisual = null;
            _selectedShapeVisual = null;
            _currentShape = null;
            _currentShapeVisual = null;

            UpdateParamsPanelVisibility();
        }

        private void AddShape(ShapeBase shapeBase)
        {
            DrawCanvas.UpdateLayout();
            Point worldAnchor = new(DrawCanvas.ActualWidth / 2, DrawCanvas.ActualHeight / 2);

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
            //shapeBase.Id = GetNextAvailableId(); 

            var visual = CreateShapeVisual(shapeBase, worldAnchor.X, worldAnchor.Y);
            DrawCanvas.Children.Add(visual);
            AddShapeToArray(shapeBase, visual);

            SelectShape(visual);
            RefreshShapesTree();
        }

        private void SelectShape(Canvas shapeVisual)
        {
            ClearSelection();

            _selectedShapeVisual = shapeVisual;
            _currentShape = shapeVisual.Tag as ShapeBase;
            _currentShapeVisual = shapeVisual;

            ShowBoundingBox(shapeVisual);
            UpdateParamsPanelVisibility();

            // Пересобираем панель параметров под новую фигуру
            if (_paramsPanelIsOpen)
                RebuildParamsPanel();
        }

        private void ShowBoundingBox(Canvas visual)
        {
            // 1. Сначала ВСЕГДА удаляем старую рамку
            ClearBoundingBox();

            if (visual == null) return;

            _boundingBoxVisual = new Rectangle
            {
                // Берем реальные размеры Canvas
                Width = visual.ActualWidth > 0 ? visual.ActualWidth : visual.Width,
                Height = visual.ActualHeight > 0 ? visual.ActualHeight : visual.Height,
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };

            // Привязываем позицию рамки к позиции Canvas
            Canvas.SetLeft(_boundingBoxVisual, Canvas.GetLeft(visual));
            Canvas.SetTop(_boundingBoxVisual, Canvas.GetTop(visual));

            DrawCanvas.Children.Add(_boundingBoxVisual);
        }

        private void ClearBoundingBox()
        {
            if (_boundingBoxVisual != null && DrawCanvas.Children.Contains(_boundingBoxVisual))
            {
                DrawCanvas.Children.Remove(_boundingBoxVisual);
            }
            _boundingBoxVisual = null;
        }

        private Canvas CreateShapeVisual(ShapeBase shape, double anchorWorldX, double anchorWorldY)
        {
            var canvas = shape.Build(anchorWorldX, anchorWorldY);
            canvas.Tag = shape;
            canvas.MouseLeftButtonDown += ShapeCanvas_Down;
            canvas.MouseMove += ShapeCanvas_Move;
            canvas.MouseLeftButtonUp += ShapeCanvas_Up;

            // === РАЗНЫЕ ОБРАБОТЧИКИ ДЛЯ ЯКОРЯ ===
            foreach (UIElement child in canvas.Children)
            {
                if (child is Ellipse ellipse && ellipse.Tag?.ToString() == "Anchor")
                {
                    ellipse.Cursor = Cursors.SizeAll;

                    if (shape is CompoundShape)
                    {
                        ellipse.MouseLeftButtonDown += CompoundAnchor_Down;
                        ellipse.MouseMove += CompoundAnchor_Move;
                        ellipse.MouseLeftButtonUp += CompoundAnchor_Up;
                    }
                    else
                    {
                        ellipse.MouseLeftButtonDown += VertexAnchor_Down;
                        ellipse.MouseMove += VertexAnchor_Move;
                        ellipse.MouseLeftButtonUp += VertexAnchor_Up;
                    }
                }
            }

            return canvas;
        }

        private void RedrawPreservingAnchor()
        {
            if (_currentShape == null || _currentShapeVisual == null)
                return;

            // 🔐 Если редактируем ребёнка "на месте" — обновляем содержимое без замены канваса
            if (_isEditingChildInPlace && _childNestedVisual != null)
            {
                // 1. Запоминаем текущую мировую позицию и состояние (уникальные имена!)
                double childCurrentLeft = Canvas.GetLeft(_childNestedVisual);
                double childCurrentTop = Canvas.GetTop(_childNestedVisual);
                bool childWasSelected = (_selectedShapeVisual == _childNestedVisual);

                // 2. Удаляем старый визуал с холста
                if (DrawCanvas.Children.Contains(_childNestedVisual))
                    DrawCanvas.Children.Remove(_childNestedVisual);

                // 3. Создаём НОВЫЙ визуал через Build
                var newChildVisual = _currentShape.Build(_childWorldX, _childWorldY);
                newChildVisual.Tag = _currentShape;
                newChildVisual.MouseLeftButtonDown += ShapeCanvas_Down;
                newChildVisual.MouseMove += ShapeCanvas_Move;
                newChildVisual.MouseLeftButtonUp += ShapeCanvas_Up;

                // 4. Восстанавливаем обработчики якоря
                foreach (UIElement child in newChildVisual.Children)
                {
                    if (child is Ellipse ellipse && ellipse.Tag?.ToString() == "Anchor")
                    {
                        ellipse.Cursor = Cursors.SizeAll;
                        ellipse.MouseLeftButtonDown += VertexAnchor_Down;
                        ellipse.MouseMove += VertexAnchor_Move;
                        ellipse.MouseLeftButtonUp += VertexAnchor_Up;
                    }
                }

                // 5. Позиционируем новый визуал на том же месте
                Canvas.SetLeft(newChildVisual, childCurrentLeft);
                Canvas.SetTop(newChildVisual, childCurrentTop);
                DrawCanvas.Children.Add(newChildVisual);

                // 6. Обновляем ссылки (используем новые имена!)
                _childNestedVisual = newChildVisual;
                _currentShapeVisual = newChildVisual;
                if (childWasSelected) _selectedShapeVisual = newChildVisual;

                // 7. Обновляем UI
                ShowBoundingBox(newChildVisual);
                if (_paramsPanelIsOpen) RefreshParamsPanelValues();
                return;
            }



            Point worldAnchor = _currentShape.GetAnchorWorldPosition(_currentShapeVisual);
            bool wasSelected = (_selectedShapeVisual == _currentShapeVisual);

            // Удаляем старый Canvas (тот, который сейчас в _currentShapeVisual)
            if (DrawCanvas.Children.Contains(_currentShapeVisual))
                DrawCanvas.Children.Remove(_currentShapeVisual);

            if (_boundingBoxVisual != null && DrawCanvas.Children.Contains(_boundingBoxVisual))
                DrawCanvas.Children.Remove(_boundingBoxVisual);

            // Создаём новый
            var newVisual = CreateShapeVisual(_currentShape, worldAnchor.X, worldAnchor.Y);
            DrawCanvas.Children.Add(newVisual);

            // 🔴 ВАЖНО: обновляем ссылки
            // Заменяем ссылку на текущий визуал
            _currentShapeVisual = newVisual;

            // Если модель присутствует в списке всех фигур — обновляем соответствующий визуал в _allShapeVisuals
            // В методе RedrawPreservingAnchor:
            int idx = Array.IndexOf(_allShapes, _currentShape); // ИСПРАВЛЕНО

            // В методе OnCloseCreatingShape:
            int shapeIndex = Array.IndexOf(_allShapes, _creatingCustomShape); // ИСПРАВЛЕНО            if (idx >= 0)
            _allShapeVisuals[idx] = newVisual;

            if (wasSelected)
            {
                _selectedShapeVisual = newVisual;
                ShowBoundingBox(newVisual);
            }

            // 🔴 ВАЖНО: всегда обновляем панель
            if (_paramsPanelIsOpen)
                RefreshParamsPanelValues();
        }

        private void RefreshParamsPanelValues()
        {
            if (_currentShape == null || _currentShapeVisual == null) return;

            // Масштаб и угол
            if (_scaleSlider != null) _scaleSlider.Value = _currentShape.Scale;
            if (_scaleTextBox != null) _scaleTextBox.Text = _currentShape.Scale.ToString("0.00");
            if (_angleSlider != null) _angleSlider.Value = _currentShape.Angle;
            if (_angleTextBox != null) _angleTextBox.Text = ((int)_currentShape.Angle).ToString();

            // Локальный якорь
            if (_localAnchorXBox != null) _localAnchorXBox.Text = _currentShape.AnchorPoint.X.ToString("0");
            if (_localAnchorYBox != null) _localAnchorYBox.Text = _currentShape.AnchorPoint.Y.ToString("0");

            // Мировой якорь
            Point world = _currentShape.GetAnchorWorldPosition(_currentShapeVisual);
            if (_worldAnchorXBox != null) _worldAnchorXBox.Text = world.X.ToString("0");
            if (_worldAnchorYBox != null) _worldAnchorYBox.Text = world.Y.ToString("0");

            // Обновление координат вершин - находим их заново в визуальном дереве
            UpdateVertexCoordinates();

            // Обновление длин рёбер
            _isUpdatingEdgeLengthText = true;
            for (int i = 0; i < _edgeLengthBoxes.Count; i++)
            {
                double length = _currentShape.GetEdgeLength(i);
                // Если ребро заблокировано, не перезаписываем текст (чтобы не мешать вводу пользователя)
                bool locked = (i < _currentShape.EdgeLengthLocked.Count) ? _currentShape.EdgeLengthLocked[i] : false;
                // Не трогаем поле, если пользователь сейчас печатает в нём
                bool hasFocus = _edgeLengthBoxes[i].IsKeyboardFocused;
                if (!locked && !hasFocus)
                    _edgeLengthBoxes[i].Text = Math.Round(length).ToString("0");
            }
            _isUpdatingEdgeLengthText = false;

            // Обновление границ фигуры
            var (bl, tr) = GetBoundingBoxFromVertices();
            if (_bboxBottomLeftX != null) _bboxBottomLeftX.Text = bl.X.ToString("0");
            if (_bboxBottomLeftY != null) _bboxBottomLeftY.Text = bl.Y.ToString("0");
            if (_bboxTopRightX != null) _bboxTopRightX.Text = tr.X.ToString("0");
            if (_bboxTopRightY != null) _bboxTopRightY.Text = tr.Y.ToString("0");
        }

        private void UpdateVertexCoordinates()
        {
            if (_currentShape == null || _currentShapeVisual == null || _paramsStackPanel == null)
                return;

            if (_currentShape.Vertices == null || _currentShape.Vertices.Length == 0)
                return;

            foreach (var child in _paramsStackPanel.Children)
            {
                if (child is not Grid row || row.ColumnDefinitions.Count != 3)
                    continue;

                // локальные координаты 
                if (row.Children.Count > 1 && row.Children[1] is StackPanel localPanel)
                {
                    var textBoxes = localPanel.Children.OfType<TextBox>().ToList();
                    if (textBoxes.Count >= 2 && textBoxes[0].Tag is int vertexIndex)
                    {
                        if (vertexIndex >= 0 && vertexIndex < _currentShape.Vertices.Length)
                        {
                            textBoxes[0].Text = (_currentShape.Vertices[vertexIndex].X - _currentShape.AnchorPoint.X).ToString("0");
                            textBoxes[1].Text = (_currentShape.Vertices[vertexIndex].Y - _currentShape.AnchorPoint.Y).ToString("0");
                        }
                    }
                }

                // глобальные координаты
                if (row.Children.Count > 2 && row.Children[2] is StackPanel worldPanel)
                {
                    var worldTextBoxes = worldPanel.Children.OfType<TextBox>().ToList();
                    if (worldTextBoxes.Count >= 2 && worldTextBoxes[0].Tag is int vertexIndex)
                    {
                        if (vertexIndex >= 0 && vertexIndex < _currentShape.Vertices.Length)
                        {
                            Point worldPos = GetVertexWorldPosition(_currentShape, _currentShapeVisual, vertexIndex);
                            worldTextBoxes[0].Text = worldPos.X.ToString("0");
                            worldTextBoxes[1].Text = worldPos.Y.ToString("0");
                        }
                    }
                }
            }
        }

        private void VertexLocalCoordinate_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox tb || !int.TryParse(tb.Tag?.ToString(), out int idx)) return;

            // Находим парный TextBox (X или Y)
            if (tb.Parent is StackPanel panel && panel.Parent is Grid row)
            {
                // Ищем другой TextBox в этой же панели
                var textBoxes = panel.Children.OfType<TextBox>().ToList();
                if (textBoxes.Count == 2)
                {
                    if (double.TryParse(textBoxes[0].Text, out double newX) &&
                        double.TryParse(textBoxes[1].Text, out double newY))
                    {
                        // Обновляем вершину
                        var p = _currentShape.Vertices[idx];
                        p.X = newX + _currentShape.AnchorPoint.X;
                        p.Y = newY + _currentShape.AnchorPoint.Y;
                        _currentShape.Vertices[idx] = p;

                        RedrawPreservingAnchor();
                    }
                }
            }
        }

        // ────────────────────────────────────────────────
        // Панель параметров
        // ────────────────────────────────────────────────

        private void UpdateParamsPanelVisibility()
        {
            if (_selectedShapeVisual == null || _currentShape == null)
            {
                ParamsContainer.Visibility = Visibility.Collapsed;
                _paramsPanelIsOpen = false;
                return;
            }

            ParamsContainer.Visibility = Visibility.Visible;

            if (!_paramsPanelIsOpen)
            {
                if (_paramsShowButton == null)
                {
                    _paramsShowButton = new Button
                    {
                        Content = "Параметры фигуры",
                        Margin = new Thickness(0, 2, 0, 2),
                        Padding = new Thickness(0, 0, 0, 0),
                        FontSize = 16,
                        FontWeight = FontWeights.SemiBold
                    };
                    _paramsShowButton.Click += ParamsShowButton_Click;
                }

                ParamsContent.Content = _paramsShowButton;
            }
        }

        private void ParamsShowButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentShape == null) return;

            if (_paramsStackPanel == null)
                _paramsStackPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };

            RebuildParamsPanel();
            ParamsContent.Content = _paramsStackPanel;
            _paramsPanelIsOpen = true;
        }

        private void RebuildParamsPanel()
        {
            if (_paramsStackPanel == null || _currentShape == null) return;

            _paramsStackPanel.Children.Clear();

            // Очищаем все ссылки
            _colorTextBoxes.Clear();
            _colorSwatches.Clear();
            _thicknessTextBoxes.Clear();
            _vertexXBoxes.Clear();
            _vertexYBoxes.Clear();
            _edgeLengthBoxes.Clear();
            _edgeLockBoxes.Clear();
            _pendingEdgeLengths.Clear();
            _vertexWorldXBoxes.Clear(); // ← важно!
            _vertexWorldYBoxes.Clear(); // ← важно!

            bool isCircle = _currentShape is CircleShape;
            int sides = _currentShape.SidesCount;
            string[] names = _currentShape.SideNames ?? Array.Empty<string>();

            // prepare pending lengths
            for (int i = 0; i < sides; i++) _pendingEdgeLengths.Add(null);

            // Стороны (цвет + толщина)
            int count = isCircle ? 1 : sides;
            for (int i = 0; i < count; i++)
            {
                string label = isCircle ? "Окружность" : (i < names.Length ? names[i] : $"Сторона {i + 1}");

                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };

                row.Children.Add(new TextBlock
                {
                    Text = label + ":",
                    Width = 110,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.SemiBold
                });

                var colorTb = new TextBox
                {
                    IsReadOnly = true,
                    Width = 90,
                    Margin = new Thickness(6, 0, 0, 0),          // уменьшено
                    Text = GetColorHex(i < _currentShape.SideColors.Count ? _currentShape.SideColors[i] : Brushes.Black)
                };
                _colorTextBoxes.Add(colorTb);

                var swatch = new Border
                {
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(4, 0, 4, 0),          // уменьшено
                    Background = i < _currentShape.SideColors.Count ? _currentShape.SideColors[i] : Brushes.Black,
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Cursor = Cursors.Hand,
                    Tag = i
                };
                swatch.MouseLeftButtonDown += ColorSwatch_MouseDown;
                swatch.MouseLeftButtonDown += (s, ev) =>
                {
                    if (swatch.Tag is int idx)
                        HighlightEdge(idx);
                };
                _colorSwatches.Add(swatch);

                var thickLabel = new TextBlock
                {
                    Text = "толщина",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 4, 0)           // уменьшено
                };

                var thickTb = new TextBox
                {
                    Width = 30,
                    Margin = new Thickness(4, 0, 0, 0),
                    Text = i < _currentShape.SideThickness.Count ? _currentShape.SideThickness[i].ToString("0.#") : "3",
                    Tag = i
                };
                thickTb.TextChanged += ThicknessTextChanged;
                thickTb.GotFocus += (s, ev) =>
                {
                    if (thickTb.Tag is int idx)
                        HighlightEdge(idx);
                };

                thickTb.LostFocus += (s, ev) =>
                {
                    if (thickTb.Tag is int idx)
                        HighlightEdge(idx, false);
                };
                _thicknessTextBoxes.Add(thickTb);

                row.Children.Add(colorTb);
                row.Children.Add(swatch);
                row.Children.Add(thickLabel);
                row.Children.Add(thickTb);

                _paramsStackPanel.Children.Add(row);
            }

            // Для кастомных фигур — добавляем углы между отрезками
            if (_currentShape is CustomShape customShape)
            {
                _paramsStackPanel.Children.Add(new TextBlock
                {
                    Text = "Углы между отрезками:",
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 12, 0, 6)
                });

                for (int i = 0; i < customShape.Segments.Count; i++)
                {
                    int currentSegmentIndex = i;  // защита от замыкания

                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    row.Children.Add(new TextBlock
                    {
                        Text = $"Угол {i + 1} → {((i + 1) % customShape.Segments.Count) + 1}:",
                        Width = 150,
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    var angleTb = new TextBox
                    {
                        Width = 70,
                        Text = customShape.GetEdgeAngle(i).ToString("0.0"),
                        Tag = i
                    };

                    // Подсветка при фокусе
                    angleTb.GotFocus += (s, ev) =>
                    {
                        int seg1 = currentSegmentIndex;
                        int seg2 = (currentSegmentIndex + 1) % customShape.Segments.Count;
                        UpdateCustomSegmentHighlight(seg1, seg2);
                    };

                    // Очистка подсветки при потере фокуса
                    angleTb.LostFocus += (s, ev) =>
                    {
                        UpdateCustomSegmentHighlight();  // очистка
                        ApplyAngleChange(currentSegmentIndex, angleTb.Text);  // ← применение значения
                    };

                    // Применение по Enter
                    angleTb.KeyDown += (s, ev) =>
                    {
                        if (ev.Key == Key.Enter)
                        {
                            ApplyAngleChange(currentSegmentIndex, angleTb.Text);
                            ev.Handled = true;
                        }
                    };

                    // Опционально: проверка ввода в реальном времени (можно убрать, если не нужно)
                    angleTb.TextChanged += (s, ev) =>
                    {
                        // Можно добавить валидацию, но пока просто оставим
                    };

                    var lockCb = new CheckBox
                    {
                        Content = "Зафиксировать",
                        Margin = new Thickness(8, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        IsChecked = customShape.Segments[i].AngleLocked,
                        Tag = i
                    };

                    lockCb.Checked += (s, ev) =>
                    {
                        if (s is CheckBox cb && int.TryParse(cb.Tag?.ToString(), out int idx) && idx < customShape.Segments.Count)
                            customShape.Segments[idx].AngleLocked = true;
                    };

                    lockCb.Unchecked += (s, ev) =>
                    {
                        if (s is CheckBox cb && int.TryParse(cb.Tag?.ToString(), out int idx) && idx < customShape.Segments.Count)
                            customShape.Segments[idx].AngleLocked = false;
                    };

                    row.Children.Add(angleTb);
                    row.Children.Add(lockCb);
                    _paramsStackPanel.Children.Add(row);
                }
            }

            // Добавлено: блок для режима создания кастомной фигуры
            if (_currentShape is CustomShape sc && _isCreatingCustomShape && sc == _creatingCustomShape)
            {
                _paramsStackPanel.Children.Add(new TextBlock
                {
                    Text = "Создание кастомной фигуры (пошагово):",
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 8, 0, 6)
                });

                var inputRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6) };

                inputRow.Children.Add(new TextBlock { Text = "Длина:", Width = 60, VerticalAlignment = VerticalAlignment.Center });
                _newSegmentLengthBox = new TextBox { Width = 80, Margin = new Thickness(4, 0, 8, 0), Text = "100" };
                inputRow.Children.Add(_newSegmentLengthBox);

                inputRow.Children.Add(new TextBlock { Text = "Угол (°):", Width = 70, VerticalAlignment = VerticalAlignment.Center });
                _newSegmentAngleBox = new TextBox { Width = 80, Margin = new Thickness(4, 0, 8, 0), Text = "0" };
                inputRow.Children.Add(_newSegmentAngleBox);

                _setSegmentButton = new Button { Content = "Задать сегмент", Margin = new Thickness(6, 0, 0, 0) };
                _setSegmentButton.Click += OnSetNewSegment;
                inputRow.Children.Add(_setSegmentButton);

                _paramsStackPanel.Children.Add(inputRow);

                var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
                _closeShapeButton = new Button { Content = "Замкнуть фигуру", Background = Brushes.LightGreen, Margin = new Thickness(0, 0, 8, 0) };
                _closeShapeButton.Click += OnCloseCreatingShape;
                actionRow.Children.Add(_closeShapeButton);

                _cancelCreateButton = new Button { Content = "Отменить создание", Background = Brushes.LightCoral };
                _cancelCreateButton.Click += OnCancelCreatingShape;
                actionRow.Children.Add(_cancelCreateButton);

                _paramsStackPanel.Children.Add(actionRow);

                // Отображаем подсказку о том, какой сегмент редактируется (последний)
                _paramsStackPanel.Children.Add(new TextBlock
                {
                    Text = $"Добавлено сегментов: {sc.Segments.Count}",
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(0, 4, 0, 8)
                });
            }

            // Внутри RebuildParamsPanel, после обработки CustomShape:
            if (_currentShape is CompoundShape compound)
            {
                _paramsStackPanel.Children.Add(new TextBlock
                {
                    Text = "Состав группы:",
                    FontWeight = FontWeights.Bold,
                    FontSize = 14,
                    Margin = new Thickness(0, 10, 0, 5)
                });

                var ungroupBtn = new Button
                {
                    Content = "РАЗГРУППИРОВАТЬ",
                    Background = Brushes.LightSalmon,
                    Margin = new Thickness(0, 0, 0, 10),
                    Height = 30
                };
                ungroupBtn.Click += (s, e) => UngroupSelected();
                _paramsStackPanel.Children.Add(ungroupBtn);

                _paramsStackPanel.Children.Add(new TextBlock { Text = "Фигуры в группе:", FontWeight = FontWeights.Bold });

                // Список существующих фигур в группе
                var listContainer = new StackPanel();
                for (int i = 0; i < compound.ChildShapes.Count; i++)
                {
                    var child = compound.ChildShapes[i];
                    int index = i;  // ← это и есть уникальный номер в группе (1, 2, 3...)

                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    // 🔹 Кнопка с номером и русским именем
                    // Внутри RebuildParamsPanel, блок if (_currentShape is CompoundShape compound)

                    var nameBtn = new Button
                    {
                        Content = $"ID={child.Id}. {child.DisplayNameRu}",  // ← Глобальный ID + имя
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Background = Brushes.White,
                        Foreground = Brushes.Black,
                        BorderBrush = Brushes.LightGray,
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(5, 2, 5, 2),
                        FontSize = 12,
                        Cursor = Cursors.Hand,
                        ToolTip = $"Тип: {child.GetType().Name}\nГлобальный ID: {child.Id}"
                    };

                    nameBtn.Click += (s, e) => { HighlightChildInCompound(compound, index); };

                    // Кнопка редактирования
                    var editBtn = new Button
                    {
                        Content = "✎",
                        Margin = new Thickness(5, 0, 0, 0),
                        Width = 30,
                        Height = 25,
                        Padding = new Thickness(0),
                        FontSize = 14,
                        Cursor = Cursors.Hand
                    };
                    editBtn.Click += (s, e) => { StartEditingChild(compound, child); };

                    // Кнопка извлечения (не удаления!)
                    var delBtn = new Button
                    {
                        Content = "✕",
                        Margin = new Thickness(5, 0, 0, 0),
                        Width = 30,
                        Height = 25,
                        Padding = new Thickness(0),
                        FontSize = 14,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.Crimson,
                        Cursor = Cursors.Hand,
                        ToolTip = "Извлечь из группы"
                    };
                    delBtn.Click += (s, e) => {
                        // === Извлечение фигуры из группы (код из предыдущего ответа) ===
                        Point parentWorld = compound.GetAnchorWorldPosition(_currentShapeVisual);
                        double rad = compound.Angle * Math.PI / 180.0;
                        double cos = Math.Cos(rad), sin = Math.Sin(rad);

                        double lx = child.AnchorPoint.X * compound.Scale;
                        double ly = child.AnchorPoint.Y * compound.Scale;
                        double rx = lx * cos - ly * sin;
                        double ry = lx * sin + ly * cos;

                        double worldX = parentWorld.X + rx;
                        double worldY = parentWorld.Y + ry;

                        child.AnchorPoint = new Point(0, 0);
                        child.Scale *= compound.Scale;
                        child.Angle += compound.Angle;

                        compound.RemoveChildShape(child);

                        var childVisual = CreateShapeVisual(child, worldX, worldY);
                        DrawCanvas.Children.Add(childVisual);
                        //child.Id = GetNextAvailableId(); 

                        AddShapeToArray(child, childVisual);


                        RedrawPreservingAnchor();
                        RebuildParamsPanel();
                    };

                    Grid.SetColumn(nameBtn, 0);
                    Grid.SetColumn(editBtn, 1);
                    Grid.SetColumn(delBtn, 2);
                    row.Children.Add(nameBtn);
                    row.Children.Add(editBtn);
                    row.Children.Add(delBtn);

                    listContainer.Children.Add(row);
                }
                _paramsStackPanel.Children.Add(listContainer);
            }

            // Если мы сейчас редактируем "ребёнка", добавим кнопку "Вернуться в группу"
            if (_editingParentCompound != null)
            {
                var saveBtn = new Button
                {
                    Content = "СОХРАНИТЬ И ВЕРНУТЬСЯ В ГРУППУ",
                    Height = 40,
                    Margin = new Thickness(0, 20, 0, 10),
                    Background = Brushes.LightGreen,
                    FontWeight = FontWeights.Bold
                };
                saveBtn.Click += (s, e) => { StopEditingChild(); };
                _paramsStackPanel.Children.Insert(0, saveBtn); // В самый верх
            }

            // Заливка
            var fillRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 12) };
            fillRow.Children.Add(new TextBlock
            {
                Text = "Заливка:",
                Width = 110,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold
            });

            _fillTextBox = new TextBox
            {
                IsReadOnly = true,
                Width = 80,
                Margin = new Thickness(6, 0, 0, 0),
                Text = GetColorHex(_currentShape.Fill ?? Brushes.Transparent)
            };

            _fillSwatch = new Border
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(4, 0, 4, 0),
                Background = _currentShape.Fill ?? Brushes.Transparent,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Cursor = Cursors.Hand
            };
            _fillSwatch.MouseLeftButtonDown += FillSwatch_MouseDown;

            fillRow.Children.Add(_fillTextBox);
            fillRow.Children.Add(_fillSwatch);
            _paramsStackPanel.Children.Add(fillRow);

            // Длины рёбер (граней)
            if (!isCircle && sides > 0)
            {

                _paramsStackPanel.Children.Add(new TextBlock
                {
                    Text = $"Глобальный ID: {_currentShape.Id}",
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black,
                    FontFamily = new FontFamily("Consolas"),
                    Margin = new Thickness(0, 8, 0, 12)
                });

                for (int i = 0; i < sides; i++)
                {
                    string edgeLabel = i < names.Length ? names[i] : $"Ребро {i + 1}";

                    var edgeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };

                    edgeRow.Children.Add(new TextBlock
                    {
                        Text = edgeLabel + ":",
                        Width = 110,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontWeight = FontWeights.Bold
                    });

                    var edgeLengthLabel = new TextBlock
                    {
                        Text = "Длина:",
                        Width = 60,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(10, 0, 0, 0)
                    };
                    edgeRow.Children.Add(edgeLengthLabel);

                    double currentLength = _currentShape.GetEdgeLength(i);
                    var edgeLengthTb = new TextBox
                    {
                        Width = 70,
                        Margin = new Thickness(6, 0, 0, 0),
                        Text = currentLength.ToString("0.0"),
                        Tag = i
                    };
                    // Обработчики: сохраняем ввод в отложенные значения, применяем по потере фокуса или Enter
                    edgeLengthTb.TextChanged += EdgeLength_TextChanged;
                    edgeLengthTb.LostFocus += EdgeLength_LostFocus;
                    edgeLengthTb.KeyDown += EdgeLength_KeyDown;
                    edgeLengthTb.GotFocus += (s, ev) =>
                    {
                        if (edgeLengthTb.Tag is int idx)
                            HighlightEdge(idx);
                    };

                    edgeLengthTb.LostFocus += (s, ev) =>
                    {
                        if (edgeLengthTb.Tag is int idx)
                            HighlightEdge(idx, false);
                    };
                    edgeRow.Children.Add(edgeLengthTb);

                    var lockCb = new CheckBox
                    {
                        Content = "Lock",
                        Margin = new Thickness(6, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        IsChecked = (i < _currentShape.EdgeLengthLocked.Count) ? _currentShape.EdgeLengthLocked[i] : false,
                        Tag = i
                    };
                    lockCb.Checked += EdgeLockChanged;
                    lockCb.Unchecked += EdgeLockChanged;
                    edgeRow.Children.Add(lockCb);
                    _edgeLockBoxes.Add(lockCb);

                    _paramsStackPanel.Children.Add(edgeRow);
                    _edgeLengthBoxes.Add(edgeLengthTb);
                }

                // Чекбокс "Равнобедренная трапеция" только для трапеции
                if (_currentShape is TrapezoidShape trapezoid)
                {
                    var isoscelesCbRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
                    _isoscelesCheckBox = new CheckBox
                    {
                        Content = "Равнобедренная трапеция",
                        IsChecked = trapezoid.EnforceIsosceles,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(110, 0, 0, 0),
                        FontWeight = FontWeights.Bold
                    };
                    _isoscelesCheckBox.Checked += IsoscelesCheckChanged;
                    _isoscelesCheckBox.Unchecked += IsoscelesCheckChanged;
                    isoscelesCbRow.Children.Add(_isoscelesCheckBox);
                    _paramsStackPanel.Children.Add(isoscelesCbRow);

                    // Если установлено, синхронизируем значения сразу в UI (правая -> левая по умолчанию)
                    if (trapezoid.EnforceIsosceles)
                    {
                        SyncIsoscelesTextboxesFromUI();
                    }
                }

                // Кнопка применения новых длин рёбер
                var applyEdgesButton = new Button
                {
                    Content = "Применить длины рёбер",
                    Margin = new Thickness(110, 4, 0, 0),
                    Padding = new Thickness(8, 2, 8, 2)
                };
                applyEdgesButton.Click += ApplyEdgeLengths_Click;
                _paramsStackPanel.Children.Add(applyEdgesButton);
            }

            // Вершины
            if (!isCircle && sides > 0)
            {
                _paramsStackPanel.Children.Add(new TextBlock
                {
                    Text = "Вершины:",
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 14, 0, 6)
                });

                // Заголовки колонок
                var headerGrid = new Grid();
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });   // V1:
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });  // Локальные
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });  // Глобальные

                headerGrid.Children.Add(new TextBlock { Text = "Локальные", FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
                Grid.SetColumn(headerGrid.Children[0], 1);
                headerGrid.Children.Add(new TextBlock { Text = "Глобальные", FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
                Grid.SetColumn(headerGrid.Children[1], 2);
                _paramsStackPanel.Children.Add(headerGrid);

                for (int i = 0; i < sides; i++)
                {
                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                    row.Margin = new Thickness(0, 0, 0, 4);

                    // Метка V1:
                    row.Children.Add(new TextBlock
                    {
                        Text = $"V{i + 1}:",
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    // --- ЛОКАЛЬНЫЕ координаты (относительно якоря) ---
                    var localPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                    var xTb = new TextBox
                    {
                        Width = 45,
                        Margin = new Thickness(2, 0, 2, 0),
                        Text = (_currentShape.Vertices[i].X - _currentShape.AnchorPoint.X).ToString("0"),
                        Tag = i
                    };
                    xTb.TextChanged += VertexLocalCoordinate_TextChanged; _vertexXBoxes.Add(xTb);

                    var yTb = new TextBox
                    {
                        Width = 45,
                        Margin = new Thickness(2, 0, 2, 0),
                        Text = (_currentShape.Vertices[i].Y - _currentShape.AnchorPoint.Y).ToString("0"),
                        Tag = i
                    };
                    yTb.TextChanged += VertexLocalCoordinate_TextChanged;
                    _vertexYBoxes.Add(yTb);

                    localPanel.Children.Add(xTb);
                    localPanel.Children.Add(new TextBlock { Text = ",", Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center });
                    localPanel.Children.Add(yTb);
                    Grid.SetColumn(localPanel, 1);
                    row.Children.Add(localPanel);

                    // --- ГЛОБАЛЬНЫЕ координаты (на холсте, только чтение) ---
                    Point worldPos = GetVertexWorldPosition(_currentShape, _currentShapeVisual, i);
                    var worldPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                    var wxTb = new TextBox
                    {

                        Width = 42,
                        Margin = new Thickness(2, 0, 2, 0),
                        Text = worldPos.X.ToString("0"),
                        IsReadOnly = true,
                        Foreground = Brushes.Gray,
                        Tag = i
                    };
                    _vertexWorldXBoxes.Add(wxTb);

                    var wyTb = new TextBox
                    {
                        Width = 42,
                        Margin = new Thickness(2, 0, 2, 0),
                        Text = worldPos.Y.ToString("0"),
                        IsReadOnly = true,
                        Foreground = Brushes.Gray,
                        Tag = i
                    };
                    _vertexWorldYBoxes.Add(wyTb);

                    worldPanel.Children.Add(wxTb);
                    worldPanel.Children.Add(new TextBlock { Text = ",", Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center });
                    worldPanel.Children.Add(wyTb);
                    Grid.SetColumn(worldPanel, 2);
                    row.Children.Add(worldPanel);

                    _paramsStackPanel.Children.Add(row);
                }
            }

            // Точка привязки
            _paramsStackPanel.Children.Add(new TextBlock
            {
                Text = "Точка привязки",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 16, 0, 6)
            });

            var localRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            localRow.Children.Add(new TextBlock { Text = "Локальная:", Width = 85, VerticalAlignment = VerticalAlignment.Center });

            _localAnchorXBox = new TextBox
            {
                Width = 50,
                Margin = new Thickness(6, 0, 4, 0),
                Text = _currentShape.AnchorPoint.X.ToString("0")
            };
            _localAnchorXBox.TextChanged += LocalAnchorX_TextChanged;

            _localAnchorYBox = new TextBox
            {
                Width = 50,
                Margin = new Thickness(0, 0, 0, 0),
                Text = _currentShape.AnchorPoint.Y.ToString("0")
            };
            _localAnchorYBox.TextChanged += LocalAnchorY_TextChanged;

            localRow.Children.Add(_localAnchorXBox);
            localRow.Children.Add(new TextBlock { Text = ",", Margin = new Thickness(4, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            localRow.Children.Add(_localAnchorYBox);
            _paramsStackPanel.Children.Add(localRow);

            // Мировая точка привязки (теперь редактируемая)
            Point world = _currentShape.GetAnchorWorldPosition(_currentShapeVisual);
            var worldRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            worldRow.Children.Add(new TextBlock { Text = "Мировая:", Width = 85, VerticalAlignment = VerticalAlignment.Center });

            _worldAnchorXBox = new TextBox
            {
                Width = 50,
                Margin = new Thickness(6, 0, 4, 0),
                Text = world.X.ToString("0"),
                IsReadOnly = false,                   
                Background = Brushes.White,               
                BorderBrush = Brushes.Gray
            };
            _worldAnchorXBox.TextChanged += WorldAnchorX_TextChanged;   

            _worldAnchorYBox = new TextBox
            {
                Width = 50,
                Margin = new Thickness(0, 0, 0, 0),
                Text = world.Y.ToString("0"),
                IsReadOnly = false,
                Background = Brushes.White,
                BorderBrush = Brushes.Gray
            };
            _worldAnchorYBox.TextChanged += WorldAnchorY_TextChanged;   


            worldRow.Children.Add(_worldAnchorXBox);
            worldRow.Children.Add(new TextBlock { Text = ",", Margin = new Thickness(4, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            worldRow.Children.Add(_worldAnchorYBox);
            _paramsStackPanel.Children.Add(worldRow);

            // Масштаб и угол — уменьшенные горизонтальные отступы
            _paramsStackPanel.Children.Add(new TextBlock
            {
                Text = "Масштаб и поворот",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 8, 0, 6)
            });

            // Масштаб
            var scalePanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            scalePanel.Children.Add(new TextBlock { Text = "Масштаб", Margin = new Thickness(0, 0, 0, 2) });

            var scaleGrid = new Grid();
            scaleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            scaleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _scaleSlider = new Slider
            {
                Minimum = 0.1,
                Maximum = 30,
                Value = _currentShape.Scale
            };
            _scaleSlider.ValueChanged += (s, ev) =>
            {
                _currentShape.Scale = ev.NewValue;
                if (_scaleTextBox != null) _scaleTextBox.Text = ev.NewValue.ToString("0.00");
                RedrawPreservingAnchor();
            };

            _scaleTextBox = new TextBox
            {
                Width = 30,
                Margin = new Thickness(6, 0, 0, 0),          // ← здесь главное уменьшение: было 12 → стало 8
                Text = _currentShape.Scale.ToString("0.00"),
                VerticalAlignment = VerticalAlignment.Center
            };
            _scaleTextBox.TextChanged += (s, ev) =>
            {
                if (double.TryParse(_scaleTextBox.Text, out var v) && v >= 0.1 && v <= 30)
                {
                    _currentShape.Scale = v;
                    if (_scaleSlider != null) _scaleSlider.Value = v;
                    RedrawPreservingAnchor();
                }
            };

            Grid.SetColumn(_scaleSlider, 0);
            Grid.SetColumn(_scaleTextBox, 1);
            scaleGrid.Children.Add(_scaleSlider);
            scaleGrid.Children.Add(_scaleTextBox);
            scalePanel.Children.Add(scaleGrid);
            _paramsStackPanel.Children.Add(scalePanel);

            // Угол
            var anglePanel = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
            anglePanel.Children.Add(new TextBlock { Text = "Угол поворота (°)", Margin = new Thickness(0, 0, 0, 2) });

            var angleGrid = new Grid();
            angleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            angleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _angleSlider = new Slider
            {
                Minimum = -180,
                Maximum = 180,
                Value = _currentShape.Angle,
                TickFrequency = 15
            };
            _angleSlider.ValueChanged += (s, ev) =>
            {
                _currentShape.Angle = ev.NewValue;

                if (_angleTextBox != null)
                    _angleTextBox.Text = ((int)ev.NewValue).ToString();

                RedrawPreservingAnchor();

                if (_paramsPanelIsOpen)
                    RefreshParamsPanelValues();   
            };

            _angleTextBox = new TextBox
            {
                Width = 30,
                Margin = new Thickness(6, 0, 0, 0),         
                Text = ((int)_currentShape.Angle).ToString(),
                VerticalAlignment = VerticalAlignment.Center
            };
            _angleTextBox.TextChanged += (s, ev) =>
            {
                if (double.TryParse(_angleTextBox.Text, out var v))
                {
                    _currentShape.Angle = v;

                    if (_angleSlider != null)
                        _angleSlider.Value = v;

                    RedrawPreservingAnchor();

                    if (_paramsPanelIsOpen)
                        RefreshParamsPanelValues(); 
                }
            };

            Grid.SetColumn(_angleSlider, 0);
            Grid.SetColumn(_angleTextBox, 1);
            angleGrid.Children.Add(_angleSlider);
            angleGrid.Children.Add(_angleTextBox);
            anglePanel.Children.Add(angleGrid);
            _paramsStackPanel.Children.Add(anglePanel);

            // Границы фигуры (мировые координаты)
            _paramsStackPanel.Children.Add(new TextBlock
            {
                Text = "Границы фигуры (мировые):",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 16, 0, 8)
            });

            var (bl, tr) = GetBoundingBoxFromVertices();
            // Нижний-левый угол
            var blRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            blRow.Children.Add(new TextBlock { Text = "Низ-Лево:", Width = 85, VerticalAlignment = VerticalAlignment.Center });
            _bboxBottomLeftX = new TextBox
            {
                Width = 50,
                Margin = new Thickness(6, 0, 4, 0),
                Text = bl.X.ToString("0"),
                IsReadOnly = true,
                Foreground = Brushes.Gray,
            };
            _bboxBottomLeftY = new TextBox
            {
                Width = 50,
                Margin = new Thickness(0, 0, 0, 0),
                Text = bl.Y.ToString("0"),
                IsReadOnly = true,
                Foreground = Brushes.Gray,
            };
            blRow.Children.Add(_bboxBottomLeftX);
            blRow.Children.Add(new TextBlock { Text = ",", Margin = new Thickness(4, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            blRow.Children.Add(_bboxBottomLeftY);
            _paramsStackPanel.Children.Add(blRow);

            // Верхний-правый угол
            var trRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 0) };
            trRow.Children.Add(new TextBlock { Text = "Верх-Право:", Width = 85, VerticalAlignment = VerticalAlignment.Center });
            _bboxTopRightX = new TextBox
            {
                Width = 50,
                Margin = new Thickness(6, 0, 4, 0),
                Text = tr.X.ToString("0"),
                IsReadOnly = true,
                Foreground = Brushes.Gray,
            };
            _bboxTopRightY = new TextBox
            {
                Width = 50,
                Margin = new Thickness(0, 0, 0, 0),
                Text = tr.Y.ToString("0"),
                IsReadOnly = true,
                Foreground = Brushes.Gray,
            };
            trRow.Children.Add(_bboxTopRightX);
            trRow.Children.Add(new TextBlock { Text = ",", Margin = new Thickness(4, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            trRow.Children.Add(_bboxTopRightY);
            _paramsStackPanel.Children.Add(trRow);
        }

        private string GetColorHex(Brush brush)
        {
            if (brush is SolidColorBrush scb)
                return $"#{scb.Color.A:X2}{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B:X2}";
            return "#00FFFFFF";
        }

        private void ColorSwatch_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border swatch || swatch.Tag is not int idx) return;

            using var dialog = new System.Windows.Forms.ColorDialog();
            if (swatch.Background is SolidColorBrush scb)
                dialog.Color = Drawing.Color.FromArgb(scb.Color.A, scb.Color.R, scb.Color.G, scb.Color.B);

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var c = System.Windows.Media.Color.FromArgb(dialog.Color.A, dialog.Color.R, dialog.Color.G, dialog.Color.B);
                var brush = new SolidColorBrush(c);

                // базовый список цветов
                while (_currentShape.SideColors.Count <= idx) _currentShape.SideColors.Add(Brushes.Black);
                _currentShape.SideColors[idx] = brush;
                swatch.Background = brush;
                if (idx < _colorTextBoxes.Count)
                    _colorTextBoxes[idx].Text = GetColorHex(brush);

                // если это CustomShape — синхронизируем цвет сегмента
                if (_currentShape is CustomShape cs && idx < cs.Segments.Count)
                    cs.Segments[idx].Color = brush;

                RedrawPreservingAnchor();
            }
        }

        private void FillSwatch_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_fillSwatch == null) return;

            using var dialog = new System.Windows.Forms.ColorDialog();
            if (_fillSwatch.Background is SolidColorBrush scb)
                dialog.Color = Drawing.Color.FromArgb(scb.Color.A, scb.Color.R, scb.Color.G, scb.Color.B);

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var c = System.Windows.Media.Color.FromArgb(dialog.Color.A, dialog.Color.R, dialog.Color.G, dialog.Color.B);
                var brush = new SolidColorBrush(c);

                _currentShape.Fill = brush;
                _fillSwatch.Background = brush;
                if (_fillTextBox != null)
                    _fillTextBox.Text = GetColorHex(brush);

                RedrawPreservingAnchor();
            }
        }

        private void ThicknessTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox tb || tb.Tag is not int idx) return;
            if (double.TryParse(tb.Text, out double v) && v > 0)
            {
                while (_currentShape.SideThickness.Count <= idx)
                    _currentShape.SideThickness.Add(3);
                _currentShape.SideThickness[idx] = v;

                // синхронизация для CustomShape
                if (_currentShape is CustomShape cs && idx < cs.Segments.Count)
                    cs.Segments[idx].Thickness = v;

                RedrawPreservingAnchor();
            }
        }

        private void EdgeLength_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox tb || !int.TryParse(tb.Tag?.ToString(), out int edgeIndex)) return;
            if (_isUpdatingEdgeLengthText) return;

            // Сохраняем введённое целое значение
            if (int.TryParse(tb.Text, out int v) && v > 0)
            {
                while (_pendingEdgeLengths.Count <= edgeIndex) _pendingEdgeLengths.Add(null);
                _pendingEdgeLengths[edgeIndex] = v; // сохраняем как число (целое в double)

                // Если это трапеция и включена равнобедренность — синхронизируем парную сторону (1 <-> 3)
                if (_currentShape is TrapezoidShape trap && trap.EnforceIsosceles && (edgeIndex == 1 || edgeIndex == 3))
                {
                    int partner = edgeIndex == 1 ? 3 : 1;
                    bool partnerLocked = (partner < _currentShape.EdgeLengthLocked.Count) && _currentShape.EdgeLengthLocked[partner];
                    if (!partnerLocked && partner < _edgeLengthBoxes.Count)
                    {
                        _isUpdatingEdgeLengthText = true;
                        _edgeLengthBoxes[partner].Text = v.ToString();
                        _isUpdatingEdgeLengthText = false;

                        while (_pendingEdgeLengths.Count <= partner) _pendingEdgeLengths.Add(null);
                        _pendingEdgeLengths[partner] = v;
                    }
                }
            }
            else
            {
                if (_pendingEdgeLengths.Count > edgeIndex)
                    _pendingEdgeLengths[edgeIndex] = null;
            }
        }

        private void EdgeLength_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb || !int.TryParse(tb.Tag?.ToString(), out int edgeIndex)) return;
            if (_pendingEdgeLengths.Count <= edgeIndex) return;
            var val = _pendingEdgeLengths[edgeIndex];
            if (val == null) return;

            ApplyPendingEdgeLength(edgeIndex, val.Value);
            _pendingEdgeLengths[edgeIndex] = null;
            if (_paramsPanelIsOpen) RefreshParamsPanelValues();
        }

        private void EdgeLength_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (sender is not TextBox tb || !int.TryParse(tb.Tag?.ToString(), out int edgeIndex)) return;
            if (_pendingEdgeLengths.Count <= edgeIndex) return;
            var val = _pendingEdgeLengths[edgeIndex];
            if (val == null) return;

            ApplyPendingEdgeLength(edgeIndex, val.Value);
            _pendingEdgeLengths[edgeIndex] = null;
            if (_paramsPanelIsOpen) RefreshParamsPanelValues();
        }

        private void ApplyPendingEdgeLength(int edgeIndex, double value)
        {
            if (_currentShape == null) return;
            // respect lock
            bool locked = (edgeIndex < _currentShape.EdgeLengthLocked.Count) && _currentShape.EdgeLengthLocked[edgeIndex];
            if (locked) return;

            // For Triangle and Trapezoid: always try atomic application using TrySetEdgeLengths
            if (_currentShape is TriangleShape || _currentShape is TrapezoidShape)
            {
                double[] lengths = new double[_currentShape.Vertices.Length];
                for (int i = 0; i < lengths.Length; i++) lengths[i] = _currentShape.GetEdgeLength(i);
                // override with pending values (including the current one)
                for (int i = 0; i < _pendingEdgeLengths.Count && i < lengths.Length; i++)
                {
                    if (_pendingEdgeLengths[i].HasValue)
                        lengths[i] = _pendingEdgeLengths[i].Value;
                }

                // Ensure the just-edited value is included
                if (edgeIndex >= 0 && edgeIndex < lengths.Length)
                    lengths[edgeIndex] = value;

                // Если трапеция и включена равнобедренность — зеркалим при возможности
                if (_currentShape is TrapezoidShape trap && trap.EnforceIsosceles)
                {
                    int partner = (edgeIndex == 1) ? 3 : (edgeIndex == 3 ? 1 : -1);
                    if (partner >= 0)
                    {
                        // если партнёр не зафиксирован — синхронизируем
                        bool partnerLocked = (partner < _currentShape.EdgeLengthLocked.Count) && _currentShape.EdgeLengthLocked[partner];
                        if (!partnerLocked)
                            lengths[partner] = lengths[edgeIndex];
                    }
                }

                if (_currentShape.TrySetEdgeLengths(lengths))
                {
                    RedrawPreservingAnchor();
                    // clear applied pending entries
                    for (int i = 0; i < _pendingEdgeLengths.Count && i < lengths.Length; i++) _pendingEdgeLengths[i] = null;
                    return;
                }
                else
                {
                    // atomic apply failed - do not change geometry; leave user input visible
                    return;
                }
            }

            // Single-edge change for other shapes: apply directly using SetEdgeLength
            _currentShape.SetEdgeLength(edgeIndex, value);
            RedrawPreservingAnchor();
        }

        private void ApplyEdgeLengths_Click(object sender, RoutedEventArgs e)
        {
            if (_currentShape == null) return;

            int n = _edgeLengthBoxes.Count;
            // Собираем введённые значения
            double[] lengths = new double[_currentShape.Vertices.Length];
            for (int i = 0; i < _currentShape.Vertices.Length; i++)
            {
                if (i < _edgeLengthBoxes.Count && int.TryParse(_edgeLengthBoxes[i].Text, out int v) && v > 0)
                    lengths[i] = v;
                else
                    lengths[i] = _currentShape.GetEdgeLength(i);
            }

            // If trapezoid with enforce isosceles, mirror sides
            if (_currentShape is TrapezoidShape trap && trap.EnforceIsosceles)
            {
                // prefer right side value if present, otherwise left
                int rightIdx = 1, leftIdx = 3;
                bool leftLocked = (leftIdx < _currentShape.EdgeLengthLocked.Count) && _currentShape.EdgeLengthLocked[leftIdx];
                bool rightLocked = (rightIdx < _currentShape.EdgeLengthLocked.Count) && _currentShape.EdgeLengthLocked[rightIdx];

                if (!leftLocked && !rightLocked)
                {
                    if (int.TryParse(_edgeLengthBoxes[rightIdx].Text, out int vr)) lengths[leftIdx] = vr;
                    else if (int.TryParse(_edgeLengthBoxes[leftIdx].Text, out int vl)) lengths[rightIdx] = vl;
                }
                else if (!leftLocked && rightLocked)
                {
                    if (int.TryParse(_edgeLengthBoxes[rightIdx].Text, out int vr)) lengths[leftIdx] = vr;
                }
                else if (!rightLocked && leftLocked)
                {
                    if (int.TryParse(_edgeLengthBoxes[leftIdx].Text, out int vl)) lengths[rightIdx] = vl;
                }
            }

            // Если фигура поддерживает атомарное применение (треугольник/трапеция) — попробуем TrySetEdgeLengths
            if (_currentShape is TriangleShape || _currentShape is TrapezoidShape)
            {
                if (!_currentShape.TrySetEdgeLengths(lengths))
                {
                    // Невозможно применить набор — ничего не меняем
                    // Оставляем введённые значения видимыми, не перезаписываем их
                    return;
                }
            }
            else
            {
                // Иначе применяем по одному, учитывая блокировки
                for (int i = 0; i < _edgeLengthBoxes.Count; i++)
                {
                    if (i >= _currentShape.Vertices.Length) continue;

                    bool locked = (i < _currentShape.EdgeLengthLocked.Count) && _currentShape.EdgeLengthLocked[i];
                    if (locked) continue;

                    var tb = _edgeLengthBoxes[i];
                    if (double.TryParse(tb.Text, out double newLength) && newLength > 0)
                    {
                        _currentShape.SetEdgeLength(i, newLength);
                    }
                }
            }

            RedrawPreservingAnchor();
            if (_paramsPanelIsOpen)
                RefreshParamsPanelValues();
        }

        private void IsoscelesCheckChanged(object? sender, RoutedEventArgs e)
        {
            if (!(_currentShape is TrapezoidShape trap)) return;
            bool isChecked = _isoscelesCheckBox?.IsChecked == true;
            trap.EnforceIsosceles = isChecked;

            // При включении — синхронизируем значения боковых сторон в UI (при условии, что сторона не заблокирована)
            if (isChecked)
            {
                SyncIsoscelesTextboxesFromUI();
            }
        }

        private void SyncIsoscelesTextboxesFromUI()
        {
            if (_edgeLengthBoxes == null || _edgeLengthBoxes.Count < 4) return;
            if (!(_currentShape is TrapezoidShape)) return;

            int rightIdx = 1, leftIdx = 3;
            bool leftLocked = (leftIdx < _currentShape.EdgeLengthLocked.Count) && _currentShape.EdgeLengthLocked[leftIdx];
            bool rightLocked = (rightIdx < _currentShape.EdgeLengthLocked.Count) && _currentShape.EdgeLengthLocked[rightIdx];

            // prefer right value; if not a valid number, try left
            if (!rightLocked && !leftLocked)
            {
                if (int.TryParse(_edgeLengthBoxes[rightIdx].Text, out int v))
                {
                    _isUpdatingEdgeLengthText = true;
                    _edgeLengthBoxes[leftIdx].Text = v.ToString();
                    _isUpdatingEdgeLengthText = false;

                    while (_pendingEdgeLengths.Count <= leftIdx) _pendingEdgeLengths.Add(null);
                    _pendingEdgeLengths[leftIdx] = v;
                }
                else if (int.TryParse(_edgeLengthBoxes[leftIdx].Text, out int v2))
                {
                    _isUpdatingEdgeLengthText = true;
                    _edgeLengthBoxes[rightIdx].Text = v2.ToString();
                    _isUpdatingEdgeLengthText = false;

                    while (_pendingEdgeLengths.Count <= rightIdx) _pendingEdgeLengths.Add(null);
                    _pendingEdgeLengths[rightIdx] = v2;
                }
            }
            else if (!leftLocked && rightLocked)
            {
                if (int.TryParse(_edgeLengthBoxes[rightIdx].Text, out int v))
                {
                    _isUpdatingEdgeLengthText = true;
                    _edgeLengthBoxes[leftIdx].Text = v.ToString();
                    _isUpdatingEdgeLengthText = false;

                    while (_pendingEdgeLengths.Count <= leftIdx) _pendingEdgeLengths.Add(null);
                    _pendingEdgeLengths[leftIdx] = v;
                }
            }
            else if (!rightLocked && leftLocked)
            {
                if (int.TryParse(_edgeLengthBoxes[leftIdx].Text, out int v))
                {
                    _isUpdatingEdgeLengthText = true;
                    _edgeLengthBoxes[rightIdx].Text = v.ToString();
                    _isUpdatingEdgeLengthText = false;

                    while (_pendingEdgeLengths.Count <= rightIdx) _pendingEdgeLengths.Add(null);
                    _pendingEdgeLengths[rightIdx] = v;
                }
            }
        }

        private void VertexX_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox tb || !int.TryParse(tb.Tag?.ToString(), out int idx)) return;
            if (double.TryParse(tb.Text, out double v))
            {
                if (idx >= 0 && idx < _currentShape.Vertices.Length)
                {
                    var p = _currentShape.Vertices[idx];
                    // ВОЗВРАЩАЕМ АБСОЛЮТНУЮ КООРДИНАТУ (Смещение + Якорь)
                    p.X = v + _currentShape.AnchorPoint.X;
                    _currentShape.Vertices[idx] = p;
                    RedrawPreservingAnchor();
                }
            }
        }

        private void VertexY_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox tb || !int.TryParse(tb.Tag?.ToString(), out int idx)) return;
            if (double.TryParse(tb.Text, out double v))
            {
                if (idx >= 0 && idx < _currentShape.Vertices.Length)
                {
                    var p = _currentShape.Vertices[idx];
                    // ВОЗВРАЩАЕМ АБСОЛЮТНУЮ КООРДИНАТУ (Смещение + Якорь)
                    p.Y = v + _currentShape.AnchorPoint.Y;
                    _currentShape.Vertices[idx] = p;
                    RedrawPreservingAnchor();
                }
            }
        }

        private void LocalAnchorX_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox tb || !double.TryParse(tb.Text, out double v)) return;
            var p = _currentShape.AnchorPoint;
            p.X = v;
            _currentShape.AnchorPoint = p;
            RedrawPreservingAnchor();
        }

        private void LocalAnchorY_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox tb || !double.TryParse(tb.Text, out double v)) return;
            var p = _currentShape.AnchorPoint;
            p.Y = v;
            _currentShape.AnchorPoint = p;
            RedrawPreservingAnchor();
        }

        private void WorldAnchorX_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox tb || _currentShape == null || _currentShapeVisual == null) return;

            if (double.TryParse(tb.Text, out double newWorldX))
            {
                // Текущая мировая позиция якоря
                Point currentWorld = _currentShape.GetAnchorWorldPosition(_currentShapeVisual);

                // На сколько нужно сдвинуть Canvas по X
                double deltaX = newWorldX - currentWorld.X;

                // Перемещаем Canvas
                double currentLeft = Canvas.GetLeft(_currentShapeVisual);
                Canvas.SetLeft(_currentShapeVisual, currentLeft + deltaX);

                // Перемещаем bounding box, если он есть
                if (_boundingBoxVisual != null && _selectedShapeVisual == _currentShapeVisual)
                    Canvas.SetLeft(_boundingBoxVisual, Canvas.GetLeft(_boundingBoxVisual) + deltaX);

                // Обновляем отображение (на случай округления)
                tb.Text = newWorldX.ToString("0");
            }
        }

        private void WorldAnchorY_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox tb || _currentShape == null || _currentShapeVisual == null) return;

            if (double.TryParse(tb.Text, out double newWorldY))
            {
                Point currentWorld = _currentShape.GetAnchorWorldPosition(_currentShapeVisual);
                double deltaY = newWorldY - currentWorld.Y;

                double currentTop = Canvas.GetTop(_currentShapeVisual);
                Canvas.SetTop(_currentShapeVisual, currentTop + deltaY);

                if (_boundingBoxVisual != null && _selectedShapeVisual == _currentShapeVisual)
                    Canvas.SetTop(_boundingBoxVisual, Canvas.GetTop(_boundingBoxVisual) + deltaY);

                tb.Text = newWorldY.ToString("0");
            }
        }


        private void VertexAnchor_Down(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Ellipse ellipse) return;

            if (ellipse.Parent is Canvas shapeCanvas && shapeCanvas.Tag is ShapeBase shape)
            {
                _currentShape = shape;
                _currentShapeVisual = shapeCanvas;
                anchorDragCanvas = shapeCanvas;
            }
            else return;

            dragStartWorld = e.GetPosition(DrawCanvas);
            originalAnchorPos = _currentShape.AnchorPoint;
            draggingAnchor = true;
            ellipse.CaptureMouse();
            e.Handled = true;
        }

        private void VertexAnchor_Move(object sender, MouseEventArgs e)
        {
            if (!draggingAnchor || _currentShape == null || anchorDragCanvas == null) return;
            if (_isProcessingMove) return;

            _isProcessingMove = true;
            try
            {
                Point currentWorld = e.GetPosition(DrawCanvas);
                Vector deltaWorld = currentWorld - dragStartWorld;
                Vector deltaLocal = deltaWorld / _currentShape.Scale;

                _currentShape.AnchorPoint = new Point(
                    Math.Round(originalAnchorPos.X + deltaLocal.X),
                    Math.Round(originalAnchorPos.Y + deltaLocal.Y));

                RedrawPreservingAnchorWithMouseCapture();
            }
            finally
            {
                _isProcessingMove = false;
            }
        }

        private void RedrawPreservingAnchorWithMouseCapture()
        {
            if (_currentShape == null || _currentShapeVisual == null) return;
            Point worldAnchor = _currentShape.GetAnchorWorldPosition(_currentShapeVisual);
            bool wasSelected = (_selectedShapeVisual == _currentShapeVisual);

            if (DrawCanvas.Children.Contains(_currentShapeVisual))
                DrawCanvas.Children.Remove(_currentShapeVisual);

            if (_boundingBoxVisual != null && DrawCanvas.Children.Contains(_boundingBoxVisual))
                DrawCanvas.Children.Remove(_boundingBoxVisual);

            // Создаем новый визуал
            _currentShapeVisual = CreateShapeVisual(_currentShape, worldAnchor.X, worldAnchor.Y);
            DrawCanvas.Children.Add(_currentShapeVisual);

            // Обновляем глобальный список визуалов (ИСПРАВЛЕНО: используем _currentShapeVisual вместо newVisual)
            int idx = Array.IndexOf(_allShapes, _currentShape);
            if (idx >= 0)
                _allShapeVisuals[idx] = _currentShapeVisual;

            if (wasSelected)
            {
                _selectedShapeVisual = _currentShapeVisual;
                ShowBoundingBox(_currentShapeVisual);
            }

            if (_paramsPanelIsOpen) RefreshParamsPanelValues();

            // Восстанавливаем захват мыши
            foreach (UIElement child in _currentShapeVisual.Children)
            {
                if (child is Ellipse ellipse && ellipse.Tag?.ToString() == "Anchor")
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
            anchorDragCanvas = null;
            e.Handled = true;
        }

        private void ShapeCanvas_Down(object sender, MouseButtonEventArgs e)
        {
            if (draggingAnchor) return;

            if (sender is Canvas clickedCanvas)
            {
                // Проверяем, зажат ли Ctrl
                bool isCtrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

                if (isCtrlPressed)
                {
                    // Множественное выделение
                    if (_selectedVisuals.Contains(clickedCanvas))
                    {
                        _selectedVisuals.Remove(clickedCanvas);
                        // Если это была последняя выделенная фигура, снимаем рамку
                        if (_selectedShapeVisual == clickedCanvas) ClearSelection();
                    }
                    else
                    {
                        _selectedVisuals.Add(clickedCanvas);
                        SelectShape(clickedCanvas); // Показываем рамку на последней выбранной
                    }
                }
                else
                {
                    // Обычное выделение (сброс предыдущих)
                    _selectedVisuals.Clear();
                    _selectedVisuals.Add(clickedCanvas);
                    SelectShape(clickedCanvas);
                }
            }

            // Логика начала перетаскивания (оставляем вашу без изменений)
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
            if (!potentialDrag || draggedShapeCanvas == null || _currentShape == null) return;

            Point currentMouse = e.GetPosition(DrawCanvas);
            double mouseDeltaX = currentMouse.X - startMouse.X;
            double mouseDeltaY = currentMouse.Y - startMouse.Y;

            if (_editingParentCompound != null && _currentShape != _editingParentCompound)
            {
                // Перемещение ребенка внутри группы (с учетом вращения группы)
                double angleRad = -_editingParentCompound.Angle * Math.PI / 180.0;
                double cos = Math.Cos(angleRad);
                double sin = Math.Sin(angleRad);

                double localDX = (mouseDeltaX * cos - mouseDeltaY * sin) / _editingParentCompound.Scale;
                double localDY = (mouseDeltaX * sin + mouseDeltaY * cos) / _editingParentCompound.Scale;

                _currentShape.AnchorPoint = new Point(
                    originalAnchorPos.X + localDX,
                    originalAnchorPos.Y + localDY
                );

                RedrawPreservingAnchor();
            }
            else
            {
                // Перемещение обычной фигуры или ВСЕЙ группы
                double newLeft = startLeft + mouseDeltaX;
                double newTop = startTop + mouseDeltaY;

                Canvas.SetLeft(draggedShapeCanvas, newLeft);
                Canvas.SetTop(draggedShapeCanvas, newTop);

                if (_boundingBoxVisual != null)
                {
                    Canvas.SetLeft(_boundingBoxVisual, newLeft);
                    Canvas.SetTop(_boundingBoxVisual, newTop);
                }
            }

            if (_paramsPanelIsOpen) RefreshParamsPanelValues();
        }

        private void ShapeCanvas_Up(object sender, MouseButtonEventArgs e)
        {
            draggedShapeCanvas?.ReleaseMouseCapture();
            draggedShapeCanvas = null;
            potentialDrag = false;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private (Point bl, Point tr) GetBoundingBoxFromVertices()
        {
            if (_currentShape == null || _currentShapeVisual == null)
                return (new Point(0, 0), new Point(0, 0));

            var worldPoints = new List<Point>();

            for (int i = 0; i < _currentShape.Vertices.Length; i++)
                worldPoints.Add(GetVertexWorldPosition(_currentShape, _currentShapeVisual, i));

            // Защита: если нет вершин, возвращаем значения по умолчанию
            if (worldPoints.Count == 0)
                return (new Point(0, 0), new Point(0, 0));

            double minX = worldPoints.Min(p => p.X);
            double minY = worldPoints.Min(p => p.Y);
            double maxX = worldPoints.Max(p => p.X);
            double maxY = worldPoints.Max(p => p.Y);

            return (new Point(minX, minY), new Point(maxX, maxY));
        }

        private void EdgeLockChanged(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || !int.TryParse(cb.Tag?.ToString(), out int idx)) return;
            bool isLocked = cb.IsChecked == true;
            while (_currentShape.EdgeLengthLocked.Count <= idx)
                _currentShape.EdgeLengthLocked.Add(false);
            _currentShape.EdgeLengthLocked[idx] = isLocked;
        }

        private void OnSetNewSegment(object? sender, RoutedEventArgs e)
        {
            if (!_isCreatingCustomShape || _creatingCustomShape == null) return;
            if (!double.TryParse(_newSegmentLengthBox?.Text, out double length) || length <= 0) return;
            if (!double.TryParse(_newSegmentAngleBox?.Text, out double angle)) angle = 0;

            if (_creatingCustomShape.Segments.Count == 0)
            {
                // первый сегмент: угол задаёт направление этого сегмента (InitialDirection)
                _creatingCustomShape.InitialDirection = angle;
                _creatingCustomShape.AddSegment(length, 0); // пока angleToNext = 0
            }
            else
            {
                // угол вводится как угол текущего сегмента относительно предыдущего,
                // это означает: previous.AngleToNext = angle
                int prevIdx = _creatingCustomShape.Segments.Count - 1;
                _creatingCustomShape.Segments[prevIdx].AngleToNext = angle;
                // добавляем новый сегмент (её AngleToNext пока 0)
                _creatingCustomShape.AddSegment(length, 0);
            }

            // Обеспечиваем списки цветов/толщин и синхронизируем в сегменте
            while (_creatingCustomShape.SideColors.Count < _creatingCustomShape.Segments.Count)
                _creatingCustomShape.SideColors.Add(Brushes.Black);
            while (_creatingCustomShape.SideThickness.Count < _creatingCustomShape.Segments.Count)
                _creatingCustomShape.SideThickness.Add(3.0);
            var lastIdx = _creatingCustomShape.Segments.Count - 1;
            _creatingCustomShape.Segments[lastIdx].Color = _creatingCustomShape.SideColors[lastIdx];
            _creatingCustomShape.Segments[lastIdx].Thickness = _creatingCustomShape.SideThickness[lastIdx];

            // Обновляем визуал и подсветку последнего сегмента
            // Сохраняем центр якоря: при создании AnchorPoint оставляем в (0,0), а CreateShapeVisual при создании
            // уже выставлял визуал в центр экрана. После добавления сегментов RedrawPreservingAnchor сохранит позицию.
            RedrawPreservingAnchor();
            UpdateCustomSegmentHighlight(_creatingCustomShape.Segments.Count - 1);

            _creatingNextIndex = _creatingCustomShape.Segments.Count;
        }

        private void OnCloseCreatingShape(object? sender, RoutedEventArgs e)
        {
            if (!_isCreatingCustomShape || _creatingCustomShape == null) return;
            if (_creatingCustomShape.Segments.Count < 2)
            {
                MessageBox.Show("Нужно как минимум 2 отрезка, чтобы замкнуть фигуру.");
                return;
            }

            // Пересчитываем вершины и берём первую/последнюю
            var verts = _creatingCustomShape.Vertices;
            if (verts.Length < 2) return;
            Point first = verts[0];
            Point last = verts[verts.Length - 1];

            Vector toFirst = first - last;
            double len = toFirst.Length;
            if (len < 1e-6)
            {
                _isCreatingCustomShape = false;
                _creatingCustomShape = null;
                ClearCustomCreationState();
                RedrawPreservingAnchor();
                return;
            }

            // Получаем направление последнего сегмента
            double currentAngle = _creatingCustomShape.InitialDirection;
            for (int i = 0; i < _creatingCustomShape.Segments.Count - 1; i++)
                currentAngle += _creatingCustomShape.Segments[i].AngleToNext;

            double lastDirRad = currentAngle * Math.PI / 180.0;
            Vector lastDir = new Vector(Math.Cos(lastDirRad), Math.Sin(lastDirRad));
            Vector targetDir = toFirst;
            targetDir.Normalize();

            double dot = Vector.Multiply(lastDir, targetDir);
            dot = Math.Max(-1.0, Math.Min(1.0, dot));
            double ang = Math.Acos(dot) * 180.0 / Math.PI;
            double cross = lastDir.X * targetDir.Y - lastDir.Y * targetDir.X;
            if (cross < 0) ang = -ang;

            // Устанавливаем угол поворота ПОСЛЕ последнего существующего сегмента
            int lastSegIdx = _creatingCustomShape.Segments.Count - 1;
            _creatingCustomShape.Segments[lastSegIdx].AngleToNext = ang;

            // Добавляем последний (замыкающий) сегмент длиной len
            _creatingCustomShape.AddSegment(len, 0);
            _creatingCustomShape.IsClosed = true;

            // Синхронизируем цвета/толщины
            while (_creatingCustomShape.SideColors.Count < _creatingCustomShape.Segments.Count)
                _creatingCustomShape.SideColors.Add(Brushes.Black);
            while (_creatingCustomShape.SideThickness.Count < _creatingCustomShape.Segments.Count)
                _creatingCustomShape.SideThickness.Add(3.0);
            for (int i = 0; i < _creatingCustomShape.Segments.Count; i++)
            {
                _creatingCustomShape.Segments[i].Color = _creatingCustomShape.SideColors[i];
                _creatingCustomShape.Segments[i].Thickness = _creatingCustomShape.SideThickness[i];
            }

            // Центрируем якорь фигуры по её локальным границам
            var worldAnchor = _creatingCustomShape.GetAnchorWorldPosition(_currentShapeVisual);
            _creatingCustomShape.CenterAnchorToBounds();
            _creatingCustomShape.IsClosed = true;

            // Удаляем из холста любые существующие Canvas, связанные с этой моделью (чтобы избежать дубликата)
            var existing = DrawCanvas.Children.OfType<Canvas>().Where(c => ReferenceEquals(c.Tag, _creatingCustomShape)).ToList();
            foreach (var c in existing)
                DrawCanvas.Children.Remove(c);

            // Перерендерим визу и обновим ссылку в списках
            var newVisual = CreateShapeVisual(_creatingCustomShape, worldAnchor.X, worldAnchor.Y);

            int shapeIndex = Array.IndexOf(_allShapes, _creatingCustomShape); // ИСПРАВЛЕНО
            if (shapeIndex >= 0)
            {
                // заменяем в списке и добавляем на холст
                _allShapeVisuals[shapeIndex] = newVisual;
                DrawCanvas.Children.Add(newVisual);
                SelectShape(newVisual);
            }
            else
            {
                // fallback
                DrawCanvas.Children.Add(newVisual);
                RedrawPreservingAnchor();
            }

            // Завершаем режим создания
            _isCreatingCustomShape = false;
            _creatingCustomShape = null;
            ClearCustomCreationState();
            MessageBox.Show("Фигура замкнута.");
            RefreshShapesTree();
        }

private void OnCancelCreatingShape(object? sender, RoutedEventArgs e)
{
    if (!_isCreatingCustomShape || _creatingCustomShape == null) return;

    // 1. Сначала находим визуал, чтобы удалить его с экрана
    int idx = Array.IndexOf(_allShapes, _creatingCustomShape);
    if (idx >= 0)
    {
        var visual = _allShapeVisuals[idx];
        if (DrawCanvas.Children.Contains(visual)) 
            DrawCanvas.Children.Remove(visual);
    }

    // 2. Удаляем саму модель из массивов (ИСПРАВЛЕНО: используем наш метод вместо RemoveAt)
    RemoveShapeFromArray(_creatingCustomShape);

    _isCreatingCustomShape = false;
    _creatingCustomShape = null;
    ClearCustomCreationState();
    ClearSelection();
    RefreshShapesTree();
}
        private void UpdateCustomSegmentHighlight(params int[] indices)
        {
            // Удаляем старую подсветку
            if (_segmentHighlightContainer != null && _currentShapeVisual != null && _currentShapeVisual.Children.Contains(_segmentHighlightContainer))
                _currentShapeVisual.Children.Remove(_segmentHighlightContainer);
            _segmentHighlightContainer = null;

            if (_currentShape == null || _currentShapeVisual == null) return;
            if (!(_currentShape is CustomShape cs)) return;

            // контейнер для оверлеев
            var container = new Canvas { IsHitTestVisible = false };
            bool added = false;

            foreach (int idx in indices.Distinct())
            {
                // Ищем Polygon с нужным Tag среди прямых потомков Canvas
                foreach (var child in _currentShapeVisual.Children.OfType<System.Windows.Shapes.Polygon>())
                {
                    if (child.Tag is int t && t == idx)
                    {
                        // Копируем точки из найденного Polygon для подсветки
                        var overlay = new System.Windows.Shapes.Polygon
                        {
                            Points = new PointCollection(child.Points),
                            Stroke = Brushes.OrangeRed,
                            StrokeThickness = 2,
                            Fill = Brushes.Transparent,
                            IsHitTestVisible = false,
                            Opacity = 0.7
                        };
                        container.Children.Add(overlay);
                        added = true;
                        break;
                    }
                }
            }

            if (added)
            {
                _segmentHighlightContainer = container;
                _currentShapeVisual.Children.Add(_segmentHighlightContainer);
            }
        }

        private void ClearCustomCreationState()
        {
            _newSegmentLengthBox = null;
            _newSegmentAngleBox = null;
            _setSegmentButton = null;
            _closeShapeButton = null;
            _cancelCreateButton = null;

            if (_segmentHighlight != null && _currentShapeVisual != null && _currentShapeVisual.Children.Contains(_segmentHighlight))
                _currentShapeVisual.Children.Remove(_segmentHighlight);
            _segmentHighlight = null;

            // перестроим панель параметров (если открыта)
            if (_paramsPanelIsOpen) RebuildParamsPanel();
        }


        private void HighlightEdge(int edgeIndex, bool highlight = true)
        {
            if (_currentShapeVisual == null) return;

            // Удаляем предыдущую подсветку (общая часть для всех типов фигур)
            if (_segmentHighlightContainer != null && _currentShapeVisual.Children.Contains(_segmentHighlightContainer))
            {
                _currentShapeVisual.Children.Remove(_segmentHighlightContainer);
                _segmentHighlightContainer = null;
            }

            if (!highlight) return;

            var container = new Canvas { IsHitTestVisible = false };
            bool found = false;

            // Ищем элементы с нужным Tag == edgeIndex
            foreach (var child in _currentShapeVisual.Children.OfType<Shape>())
            {
                if (child.Tag is int tag && tag == edgeIndex)
                {
                    Shape overlay;

                    if (child is Polygon poly)
                    {
                        overlay = new Polygon
                        {
                            Points = new PointCollection(poly.Points),
                            Stroke = Brushes.OrangeRed,
                            StrokeThickness = 4,
                            Fill = Brushes.Transparent,
                            Opacity = 0.75,
                            IsHitTestVisible = false
                        };
                    }
                    else if (child is Polyline line)
                    {
                        overlay = new Polyline
                        {
                            Points = new PointCollection(line.Points),
                            Stroke = Brushes.OrangeRed,
                            StrokeThickness = 4,
                            Opacity = 0.85,
                            IsHitTestVisible = false
                        };
                    }
                    else if (child is Line ln)
                    {
                        overlay = new Line
                        {
                            X1 = ln.X1,
                            Y1 = ln.Y1,
                            X2 = ln.X2,
                            Y2 = ln.Y2,
                            Stroke = Brushes.OrangeRed,
                            StrokeThickness = 4,
                            Opacity = 0.85,
                            IsHitTestVisible = false
                        };
                    }
                    else if (child is Rectangle rect)   // ← добавляем поддержку прямоугольников
                    {
                        overlay = new Rectangle
                        {
                            Width = rect.Width,
                            Height = rect.Height,
                            Stroke = Brushes.OrangeRed,
                            StrokeThickness = 4,
                            Fill = Brushes.Transparent,
                            Opacity = 0.75,
                            IsHitTestVisible = false
                        };
                        Canvas.SetLeft(overlay, Canvas.GetLeft(rect));
                        Canvas.SetTop(overlay, Canvas.GetTop(rect));
                    }
                    else
                    {
                        continue;
                    }

                    container.Children.Add(overlay);
                    found = true;
                }
            }

            if (found)
            {
                _segmentHighlightContainer = container;
                _currentShapeVisual.Children.Add(container);
            }
        }

        private void ApplyAngleChange(int segmentIndex, string inputText)
        {
            if (_currentShape is not CustomShape customShape) return;
            if (segmentIndex < 0 || segmentIndex >= customShape.Segments.Count) return;

            if (double.TryParse(inputText, out double newAngle))
            {
                // Учитываем блокировку угла
                if (customShape.Segments[segmentIndex].AngleLocked)
                {
                    // Если заблокировано — возвращаем старое значение в поле
                    RefreshAngleTextBox(segmentIndex);
                    return;
                }

                customShape.SetEdgeAngle(segmentIndex, newAngle);
                RedrawPreservingAnchor();

                // Обновляем отображение (на случай округления или других эффектов)
                RefreshAngleTextBox(segmentIndex);
            }
            else
            {
                // Некорректный ввод — возвращаем старое значение
                RefreshAngleTextBox(segmentIndex);
            }
        }

        private void RefreshAngleTextBox(int segmentIndex)
        {
            if (_paramsStackPanel == null) return;

            // Ищем TextBox для данного сегмента
            foreach (var child in _paramsStackPanel.Children.OfType<StackPanel>())
            {
                if (child.Children.Count >= 2 && child.Children[1] is TextBox tb && tb.Tag is int idx && idx == segmentIndex)
                {
                    double currentAngle = ((CustomShape)_currentShape).GetEdgeAngle(segmentIndex);
                    tb.Text = currentAngle.ToString("0.0");
                    break;
                }
            }
        }


        private void AddChildToCompound(CompoundShape parent, string type)
        {
            ShapeBase newShape = type switch
            {
                "Прямоугольник" => new RectangleShape(),
                "Треугольник" => new TriangleShape(),
                "Трапеция" => new TrapezoidShape(),
                "Круг" => new CircleShape(),
                "Шестиугольник" => new HexagonShape(),
                _ => new RectangleShape()
            };

            // Настройка базовых свойств
            newShape.Fill = Brushes.Transparent;
            for (int i = 0; i < newShape.SidesCount; i++)
            {
                newShape.SideColors.Add(Brushes.Black);
                newShape.SideThickness.Add(2.0);
            }

            parent.AddChildShape(newShape);
            RedrawPreservingAnchor();
            RebuildParamsPanel();
        }

        private void StartEditingChild(CompoundShape parent, ShapeBase child)
        {
            // === 1. Вычисляем мировую позицию якоря ребёнка ===
            Point parentWorld = parent.GetAnchorWorldPosition(_currentShapeVisual);
            double rad = parent.Angle * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);

            double lx = child.AnchorPoint.X * parent.Scale;
            double ly = child.AnchorPoint.Y * parent.Scale;
            double rx = lx * cos - ly * sin;
            double ry = lx * sin + ly * cos;

            _childWorldX = parentWorld.X + rx;
            _childWorldY = parentWorld.Y + ry;
            _childOriginalAnchor = child.AnchorPoint;

            // === 2. Находим вложенный визуал ребёнка ===
            Canvas childVisual = null;
            foreach (var element in _currentShapeVisual.Children.OfType<Canvas>())
            {
                if (ReferenceEquals(element.Tag, child))
                {
                    childVisual = element;
                    break;
                }
            }
            if (childVisual == null) return;

            // === 3. Сохраняем состояние для возврата ===
            _isEditingChildInPlace = true;
            _originalParentVisual = _currentShapeVisual;
            _childNestedVisual = childVisual;

            // === 4. "Изолируем" визуал: переносим его на верхний уровень ===
            // Но НЕ создаём новый — используем тот же объект!
            _originalParentVisual.Children.Remove(childVisual);
            DrawCanvas.Children.Add(childVisual);

            // Позиционируем его в мировых координатах (как обычную фигуру)
            Canvas.SetLeft(childVisual, _childWorldX + child.MinX * child.Scale);
            Canvas.SetTop(childVisual, _childWorldY + child.MinY * child.Scale);

            // === 5. Обновляем ссылки для редактирования ===
            _editingParentCompound = parent;
            _currentShape = child;
            _currentShapeVisual = childVisual;
            _selectedShapeVisual = childVisual;

            // === 6. Обновляем UI ===
            ShowBoundingBox(childVisual);
            RebuildParamsPanel();
        }

        private void StopEditingChild()
        {
            if (!_isEditingChildInPlace || _editingParentCompound == null) return;

            var parent = _editingParentCompound;
            var child = _currentShape;  // та же модель, что и была

            // === 1. Получаем НОВУЮ мировую позицию после редактирования ===
            double newWorldX = Canvas.GetLeft(_childNestedVisual) - child.MinX * child.Scale;
            double newWorldY = Canvas.GetTop(_childNestedVisual) - child.MinY * child.Scale;

            // === 2. Возвращаем визуал обратно в родителя ===
            DrawCanvas.Children.Remove(_childNestedVisual);
            _originalParentVisual.Children.Add(_childNestedVisual);

            // === 3. Пересчитываем AnchorPoint ребёнка ОТНОСИТЕЛЬНО родителя ===
            Point parentWorld = parent.GetAnchorWorldPosition(_originalParentVisual);
            double rad = parent.Angle * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);

            double dx = newWorldX - parentWorld.X;
            double dy = newWorldY - parentWorld.Y;

            // Обратный поворот
            double rx = dx * cos + dy * sin;
            double ry = -dx * sin + dy * cos;

            // Локальные координаты с учётом масштаба родителя
            child.AnchorPoint = new Point(
                Math.Round(rx / parent.Scale, 2),
                Math.Round(ry / parent.Scale, 2)
            );

            // === 4. Сбрасываем состояние редактирования ===
            _isEditingChildInPlace = false;
            _editingParentCompound = null;
            _currentShape = parent;
            _currentShapeVisual = _originalParentVisual;
            _selectedShapeVisual = _originalParentVisual;

            // === 5. Пересобираем визуал родителя с обновлённым ребёнком ===
            ClearBoundingBox();
            RedrawPreservingAnchor();  // Перестроит родителя с новыми координатами ребёнка
            RebuildParamsPanel();
            RefreshShapesTree();
        }

        private void HighlightChildInCompound(CompoundShape parent, int childIndex)
        {
            if (childIndex < 0 || childIndex >= parent.ChildShapes.Count) return;
            var child = parent.ChildShapes[childIndex];

            foreach (var element in _currentShapeVisual.Children)
            {
                if (element is Canvas childCanvas && ReferenceEquals(childCanvas.Tag, child))
                {
                    // 🔹 Используем метод для вложенных визуалов
                    ShowBoundingBoxForNestedChild(childCanvas, _currentShapeVisual);
                    return;
                }
            }
        }

        private void GroupSelected()
        {
            if (_selectedVisuals.Count < 2) return;

            var compound = new CompoundShape();
            //compound.Id = GetNextAvailableId();

            // === 1. Считаем общие границы ВСЕХ фигур (с учётом вложенных) ===
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var vis in _selectedVisuals)
            {
                var shape = vis.Tag as ShapeBase;

                // 🔹 Если это группа — учитываем границы её детей
                if (shape is CompoundShape nestedCompound)
                {
                    foreach (var child in nestedCompound.ChildShapes)
                    {
                        Point childWorld = GetChildWorldPosition(nestedCompound, vis, child);
                        minX = Math.Min(minX, childWorld.X + child.MinX);
                        minY = Math.Min(minY, childWorld.Y + child.MinY);
                        maxX = Math.Max(maxX, childWorld.X + child.MaxX);
                        maxY = Math.Max(maxY, childWorld.Y + child.MaxY);
                    }
                }
                else
                {
                    Point worldPos = shape.GetAnchorWorldPosition(vis);
                    minX = Math.Min(minX, worldPos.X + shape.MinX);
                    minY = Math.Min(minY, worldPos.Y + shape.MinY);
                    maxX = Math.Max(maxX, worldPos.X + shape.MaxX);
                    maxY = Math.Max(maxY, worldPos.Y + shape.MaxY);
                }
            }

            Point groupWorldAnchor = new Point((minX + maxX) / 2, (minY + maxY) / 2);

            // === 2. «Распаковываем» группы и добавляем всех детей в новую группу ===
            foreach (var vis in _selectedVisuals.ToList())
            {
                var shape = vis.Tag as ShapeBase;

                if (shape is CompoundShape nestedCompound)
                {
                    // 🔹 Если это группа — добавляем её детей (не саму группу!)
                    foreach (var child in nestedCompound.ChildShapes.ToList())
                    {
                        Point childWorld = GetChildWorldPosition(nestedCompound, vis, child);

                        // Пересчитываем якорь относительно новой группы
                        child.AnchorPoint = new Point(
                            childWorld.X - groupWorldAnchor.X,
                            childWorld.Y - groupWorldAnchor.Y
                        );

                        compound.AddChildShape(child);
                    }

                    // Удаляем старую группу
                    nestedCompound.ChildShapes.Clear();
                    DrawCanvas.Children.Remove(vis);
                    RemoveShapeFromArray(compound);
                }
                else
                {
                    // 🔹 Обычная фигура — добавляем как есть
                    Point childWorldPos = shape.GetAnchorWorldPosition(vis);
                    shape.AnchorPoint = new Point(
                        childWorldPos.X - groupWorldAnchor.X,
                        childWorldPos.Y - groupWorldAnchor.Y
                    );

                    compound.AddChildShape(shape);
                    DrawCanvas.Children.Remove(vis);
                    RemoveShapeFromArray(shape);
                }
            }

            var groupVisual = CreateShapeVisual(compound, groupWorldAnchor.X, groupWorldAnchor.Y);
            DrawCanvas.Children.Add(groupVisual);
            AddShapeToArray(compound, groupVisual);

            _selectedVisuals.Clear();
            SelectShape(groupVisual);
            RefreshShapesTree();
        }

        // 🔹 Вспомогательный метод: вычисление мировой позиции ребёнка внутри вложенной группы
        private Point GetChildWorldPosition(CompoundShape parent, Canvas parentVisual, ShapeBase child)
        {
            Point parentWorld = parent.GetAnchorWorldPosition(parentVisual);
            double rad = parent.Angle * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);

            double lx = child.AnchorPoint.X * parent.Scale;
            double ly = child.AnchorPoint.Y * parent.Scale;
            double rx = lx * cos - ly * sin;
            double ry = lx * sin + ly * cos;

            return new Point(parentWorld.X + rx, parentWorld.Y + ry);
        }


        private void UngroupSelected()
        {
            if (!(_currentShape is CompoundShape compound)) return;

            Point groupWorldPos = _currentShape.GetAnchorWorldPosition(_currentShapeVisual);

            foreach (var child in compound.ChildShapes.ToList())
            {
                // Пересчитываем локальное смещение в мировое с учетом поворота и масштаба группы
                double angleRad = _currentShape.Angle * Math.PI / 180.0;
                double cos = Math.Cos(angleRad);
                double sin = Math.Sin(angleRad);

                double lx = child.AnchorPoint.X * _currentShape.Scale;
                double ly = child.AnchorPoint.Y * _currentShape.Scale;

                double worldX = groupWorldPos.X + (lx * cos - ly * sin);
                double worldY = groupWorldPos.Y + (lx * sin + ly * cos);

                // Применяем трансформации группы к ребенку
                child.AnchorPoint = new Point(0, 0); // Сбрасываем для удобства на холсте
                child.Scale *= _currentShape.Scale;
                child.Angle += _currentShape.Angle;
                //child.Id = GetNextAvailableId(); 

                // Возвращаем на холст как независимую фигуру
                var childVis = CreateShapeVisual(child, worldX, worldY);
                DrawCanvas.Children.Add(childVis);
                AddShapeToArray(child, childVis);
            }

            // Удаляем саму группу
            RemoveShapeFromArray(compound);
            DrawCanvas.Children.Remove(_currentShapeVisual);

            ClearSelection();
            RefreshShapesTree();
        }



        private void DrawCanvas_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // Группировать можно только если выбрано 2 и более объекта
            GroupMenu.IsEnabled = _selectedVisuals.Count >= 2;

            // Разгруппировать можно, только если выбрана ОДНА фигура и это группа (CompoundShape)
            UngroupMenu.IsEnabled = _selectedVisuals.Count == 1 && _currentShape is CompoundShape;
        }

        private void GroupMenuItem_Click(object sender, RoutedEventArgs e)
        {
            GroupSelected(); // Метод математики группировки, который мы обсуждали
        }

        private void UngroupMenuItem_Click(object sender, RoutedEventArgs e)
        {
            UngroupSelected(); // Метод возврата фигур на холст
        }

        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Удаляем всё, что было выбрано 
            var toDelete = _selectedVisuals.ToList();
            foreach (var vis in toDelete)
            {
                var shape = vis.Tag as ShapeBase;
                DrawCanvas.Children.Remove(vis);
                RemoveShapeFromArray(shape);
            }
            _selectedVisuals.Clear();
            ClearSelection();
            RefreshShapesTree();
        }

        public static bool IsEditingThisChild(CompoundShape parent, ShapeBase child)
        {
            var main = (MainWindow)Application.Current.MainWindow;
            return main._editingParentCompound == parent && main._currentShape == child;
        }

        // ==================== ФИНАЛЬНАЯ ВЕРСИЯ — ТОЛЬКО ТОЧКА ПРИВЯЗКИ ДВИГАЕТСЯ ====================

        private void CompoundAnchor_Down(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Ellipse ellipse) return;
            if (ellipse.Parent is not Canvas canvas || canvas.Tag is not CompoundShape compound) return;

            _currentShape = compound;
            _currentShapeVisual = canvas;
            anchorDragCanvas = canvas;

            dragStartWorld = e.GetPosition(DrawCanvas);
            originalAnchorPos = compound.AnchorPoint;   // запоминаем текущее положение якоря

            draggingAnchor = true;
            ellipse.CaptureMouse();
            e.Handled = true;
        }

        private void CompoundAnchor_Move(object sender, MouseEventArgs e)
        {
            if (!draggingAnchor || _currentShape is not CompoundShape compound || _currentShapeVisual == null)
                return;

            if (_isProcessingMove) return;
            _isProcessingMove = true;

            try
            {
                Point currentMouse = e.GetPosition(DrawCanvas);
                Vector deltaWorld = currentMouse - dragStartWorld;

                // Меняем только AnchorPoint
                Vector deltaLocal = deltaWorld / compound.Scale;
                compound.AnchorPoint = new Point(
                    Math.Round(originalAnchorPos.X + deltaLocal.X, 2),
                    Math.Round(originalAnchorPos.Y + deltaLocal.Y, 2));

                // Обновляем ТОЛЬКО позицию фиолетовой точки внутри Canvas
                UpdateOnlyGroupAnchorVisualPosition();
            }
            finally
            {
                _isProcessingMove = false;
            }
        }

        private void UpdateOnlyGroupAnchorVisualPosition()
        {
            if (_currentShapeVisual == null || _currentShape is not CompoundShape compound) return;

            var anchorEllipse = _currentShapeVisual.Children
                .OfType<Ellipse>()
                .FirstOrDefault(el => el.Tag?.ToString() == "Anchor");

            if (anchorEllipse == null) return;

            // 1. Вычисляем "сырые" границы детей (как в Build)
            double rawMinX = double.MaxValue, rawMaxX = double.MinValue;
            double rawMinY = double.MaxValue, rawMaxY = double.MinValue;

            foreach (var child in compound.ChildShapes)
            {
                rawMinX = Math.Min(rawMinX, child.AnchorPoint.X + child.MinX);
                rawMaxX = Math.Max(rawMaxX, child.AnchorPoint.X + child.MaxX);
                rawMinY = Math.Min(rawMinY, child.AnchorPoint.Y + child.MinY);
                rawMaxY = Math.Max(rawMaxY, child.AnchorPoint.Y + child.MaxY);
            }

            // 2. Центр группы в локальных координатах
            double centerX = (rawMinX + rawMaxX) / 2;
            double centerY = (rawMinY + rawMaxY) / 2;

            // 3. Позиция якоря внутри контейнера: (AnchorPoint - center) * Scale + half
            double halfW = _currentShapeVisual.Width / 2;
            double halfH = _currentShapeVisual.Height / 2;

            double anchorX = (compound.AnchorPoint.X - centerX) * compound.Scale + halfW;
            double anchorY = (compound.AnchorPoint.Y - centerY) * compound.Scale + halfH;

            Canvas.SetLeft(anchorEllipse, anchorX - 5); // -5 для центрирования самой точки (10px)
            Canvas.SetTop(anchorEllipse, anchorY - 5);

            if (_paramsPanelIsOpen)
                RefreshParamsPanelValues();
        }


        private void CompoundAnchor_Up(object sender, MouseButtonEventArgs e)
        {
            (sender as Ellipse)?.ReleaseMouseCapture();
            draggingAnchor = false;
            anchorDragCanvas = null;
            e.Handled = true;
        }





        private void ShowBoundingBoxForNestedChild(Canvas childVisual, Canvas parentVisual)
        {
            ClearBoundingBox();
            if (childVisual == null) return;

            // Мировая позиция родителя
            double parentLeft = Canvas.GetLeft(parentVisual);
            double parentTop = Canvas.GetTop(parentVisual);

            // Локальная позиция ребёнка внутри родителя
            double childLocalLeft = Canvas.GetLeft(childVisual);
            double childLocalTop = Canvas.GetTop(childVisual);

            // Итоговая мировая позиция
            double worldLeft = parentLeft + childLocalLeft;
            double worldTop = parentTop + childLocalTop;

            _boundingBoxVisual = new Rectangle
            {
                Width = childVisual.ActualWidth > 0 ? childVisual.ActualWidth : childVisual.Width,
                Height = childVisual.ActualHeight > 0 ? childVisual.ActualHeight : childVisual.Height,
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(_boundingBoxVisual, worldLeft);
            Canvas.SetTop(_boundingBoxVisual, worldTop);
            DrawCanvas.Children.Add(_boundingBoxVisual);
        }



        private void RefreshShapesTree()
        {
            _treeRootItems.Clear();

            foreach (var shape in _allShapes)
            {
                var item = new ShapeTreeItem(shape);
                if (shape is CompoundShape compound)
                {
                    foreach (var child in compound.ChildShapes)
                    {
                        item.Children.Add(new ShapeTreeItem(child));
                    }
                }
                _treeRootItems.Add(item);
            }

            ShapesTreeView.ItemsSource = null;
            ShapesTreeView.ItemsSource = _treeRootItems;
        }

        private void ShapesTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is not ShapeTreeItem treeItem || treeItem.Shape == null)
                return;

            HighlightShapeOnCanvas(treeItem.Shape);
        }

        private void HighlightShapeOnCanvas(ShapeBase shape)
        {
            ClearBoundingBox();

            // Ищем визуал этой фигуры
            var visual = _allShapeVisuals.FirstOrDefault(v => ReferenceEquals(v.Tag, shape));
            if (visual == null) return;

            if (shape is CompoundShape)
            {
                ShowBoundingBox(visual);                   
            }
            else
            {
                ShowBoundingBox(visual);
            }
        }

        private void HighlightShape(ShapeBase shape)
        {
            ClearBoundingBox();

            // Ищем визуал этой фигуры
            var visual = _allShapeVisuals.FirstOrDefault(v => v.Tag == shape);
            if (visual == null) return;

            if (shape is CompoundShape)
            {
                // Для группы используем обычный bounding box
                ShowBoundingBox(visual);
            }
            else
            {
                // Для обычной фигуры — стандартный bounding box
                ShowBoundingBox(visual);
            }
        }


        private void AddShapeToArray(ShapeBase shape, Canvas visual)
        {
            Array.Resize(ref _allShapes, _allShapes.Length + 1);
            Array.Resize(ref _allShapeVisuals, _allShapeVisuals.Length + 1);
            _allShapes[_allShapes.Length - 1] = shape;
            _allShapeVisuals[_allShapeVisuals.Length - 1] = visual;
        }

        private void RemoveShapeFromArray(ShapeBase shape)
        {
            int index = Array.IndexOf(_allShapes, shape);
            if (index < 0) return;

            ShapeBase[] newShapes = new ShapeBase[_allShapes.Length - 1];
            Canvas[] newVisuals = new Canvas[_allShapeVisuals.Length - 1];

            for (int i = 0, j = 0; i < _allShapes.Length; i++)
            {
                if (i == index) continue;
                newShapes[j] = _allShapes[i];
                newVisuals[j] = _allShapeVisuals[i];
                j++;
            }
            _allShapes = newShapes;
            _allShapeVisuals = newVisuals;
        }

        private int GetNextAvailableId()
        {
            int id = 1;
            // Пока в массиве есть фигура с таким ID, увеличиваем счетчик
            while (_allShapes.Any(s => s.Id == id))
            {
                id++;
            }
            return id;
        }



        // Обработчики меню
        private void SaveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = FILE_FILTER,
                DefaultExt = FILE_EXTENSION,
                AddExtension = true,
                Title = "Сохранить фигуры",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    SaveAllShapes(saveDialog.FileName);
                    MessageBox.Show($"Фигуры успешно сохранены в:\n{saveDialog.FileName}",
                                   "Сохранение", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при сохранении:\n{ex.Message}",
                                   "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LoadMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_allShapes.Length > 0)
            {
                var result = MessageBox.Show(
                    "На холсте есть фигуры. Загрузка удалит текущие фигуры.\nПродолжить?",
                    "Подтверждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            var openDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = FILE_FILTER,
                DefaultExt = FILE_EXTENSION,
                Title = "Загрузить фигуры",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (openDialog.ShowDialog() == true)
            {
                try
                {
                    LoadAllShapes(openDialog.FileName);
                    MessageBox.Show($"Фигуры успешно загружены из:\n{openDialog.FileName}",
                                   "Загрузка", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при загрузке:\n{ex.Message}",
                                   "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ClearCanvasMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_allShapes.Length == 0) return;
            var result = MessageBox.Show("Удалить все фигуры с холста?", "Подтверждение",
                                        MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes) ClearAllShapes();
        }

        // Методы сохранения/загрузки всех фигур
        private void SaveAllShapes(string filename)
        {
            var shapesToSave = _allShapes.Select(s => MapShapeToData(s)).ToList();

            var jsonFile = new JsonShapeFile
            {
                version = 1,
                shapes = shapesToSave
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            string jsonString = JsonSerializer.Serialize(jsonFile, options);
            File.WriteAllText(filename, jsonString);
        }

        // Вспомогательный метод для рекурсивного маппинга
        private JsonShapeData MapShapeToData(ShapeBase s)
        {
            var data = new JsonShapeData
            {
                type = s.GetType().Name,
                id = s.Id,
                // Получаем мировую позицию якоря
                worldX = (s is CompoundShape || _allShapes.Contains(s)) ?
                          s.GetAnchorWorldPosition(_allShapeVisuals[Array.IndexOf(_allShapes, s)]).X : 0,
                worldY = (s is CompoundShape || _allShapes.Contains(s)) ?
                          s.GetAnchorWorldPosition(_allShapeVisuals[Array.IndexOf(_allShapes, s)]).Y : 0,
                scale = s.Scale,
                angle = s.Angle,
                anchorX = s.AnchorPoint.X,
                anchorY = s.AnchorPoint.Y,
                fill = GetColorHex(s.Fill),
                sideColors = s.SideColors.Select(c => GetColorHex(c)).ToList(),
                sideThicknesses = s.SideThickness.ToList(),
                edgeLocks = s.EdgeLengthLocked.ToList(),
                vertices = s.Vertices?.Select(v => new JsonVertex { x = v.X, y = v.Y }).ToList()
            };

            // Специфика CustomShape
            if (s is CustomShape cs)
            {
                data.isClosed = cs.IsClosed;
                data.initialDirection = cs.InitialDirection;
                data.segments = cs.Segments.Select(seg => new JsonSegmentData
                {
                    name = seg.Name,
                    length = seg.Length,
                    thickness = seg.Thickness,
                    angleToNext = seg.AngleToNext,
                    angleLocked = seg.AngleLocked,
                    lengthLocked = seg.LengthLocked,
                    color = GetColorHex(seg.Color)
                }).ToList();
            }

            // Специфика CompoundShape (рекурсия)
            if (s is CompoundShape compound)
            {
                data.children = compound.ChildShapes.Select(child => MapShapeToData(child)).ToList();
            }

            return data;
        }

        private void LoadAllShapes(string filename)
        {
            string jsonString = File.ReadAllText(filename);
            var json = JsonSerializer.Deserialize<JsonShapeFile>(jsonString);

            ClearAllShapes();

            foreach (var shapeData in json.shapes)
            {
                ShapeBase shape = RestoreShapeFromData(shapeData);
                if (shape == null) continue;

                // Создаем визуал на основе восстановленных данных
                var visual = CreateShapeVisual(shape, shapeData.worldX, shapeData.worldY);
                DrawCanvas.Children.Add(visual);
                AddShapeToArray(shape, visual);
            }

            RefreshShapesTree();
        }

        private ShapeBase RestoreShapeFromData(JsonShapeData data)
        {
            ShapeBase shape = CreateShapeByType(data.type);
            if (shape == null) return null;

            // 1. Базовые свойства
            shape.Id = data.id;
            shape.Scale = data.scale;
            shape.Angle = data.angle;
            shape.AnchorPoint = new Point(data.anchorX, data.anchorY);
            shape.Fill = ParseColor(data.fill);

            if (data.sideColors != null) shape.SideColors = new List<Brush>(data.sideColors.Select(c => ParseColor(c)));
            if (data.sideThicknesses != null) shape.SideThickness = new List<double>(data.sideThicknesses);
            if (data.edgeLocks != null) shape.EdgeLengthLocked = new List<bool>(data.edgeLocks);

            // 2. Специфика CustomShape
            if (shape is CustomShape cs && data.segments != null)
            {
                cs.IsClosed = data.isClosed;
                cs.InitialDirection = data.initialDirection;
                cs.Segments = data.segments.Select(sd => new LineSegment
                {
                    Name = sd.name,
                    Length = sd.length,
                    Thickness = sd.thickness,
                    AngleToNext = sd.angleToNext,
                    AngleLocked = sd.angleLocked,
                    LengthLocked = sd.lengthLocked,
                    Color = ParseColor(sd.color)
                }).ToList();

                // Пересчитываем точки (в CustomShape.cs этот метод должен быть public или вызываться через Reflection)
                cs.GetType().GetMethod("RebuildVertices", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                          ?.Invoke(cs, null);
            }

            // 3. Специфика CompoundShape (Рекурсия)
            if (shape is CompoundShape compound && data.children != null)
            {
                foreach (var childData in data.children)
                {
                    var child = RestoreShapeFromData(childData);
                    if (child != null)
                        compound.AddChildShape(child);
                }
                // ВАЖНО: После добавления всех детей заставляем группу вычислить свои MinX/MaxX
                compound.RecalculateBounds();
            }

            // 4. Обычные фигуры (Прямоугольники, Треугольники и т.д.)
            if (data.vertices != null && data.vertices.Count > 0)
            {
                shape.Vertices = data.vertices.Select(v => new Point(v.x, v.y)).ToArray();
                // Принудительно вызываем метод обновления MinX/MaxX, который обычно в ShapeBase
                // Если в ShapeBase нет публичного метода для этого, можно вызвать Build(0,0) без добавления на холст
                shape.Build(0, 0);
            }

            return shape;
        }

        private void LoadShapeFromJson(ShapeBase shape, JsonShapeData data)
        {
            // Рекурсивная загрузка для детей составной фигуры
            if (shape is CompoundShape compound && data.children != null)
            {
                foreach (var childData in data.children)
                {
                    ShapeBase child = CreateShapeByType(childData.type);
                    LoadShapeFromJson(child, childData);
                    compound.AddChildShape(child);
                }
            }
        }

        private Brush ParseColor(string hex)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return Brushes.Black;

                // Используем стандартный конвертер WPF, он защищен от ошибок длины строки
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch
            {
                return Brushes.Black; // Возвращаем черный в случае ошибки формата
            }
        }
        /// <summary>
        /// Предварительно строит детей составной фигуры, чтобы вычислились их границы
        /// </summary>
        private void PreBuildCompoundChildren(CompoundShape compound)
        {
            foreach (var child in compound.ChildShapes)
            {
                // Рекурсивно для вложенных групп
                if (child is CompoundShape childCompound)
                {
                    PreBuildCompoundChildren(childCompound);
                }

                // Строим ребенка в (0,0) - это вычислит его MinX, MinY, MaxX, MaxY
                // но не добавляем на холст
                var tempVisual = child.Build(0, 0);
                // Визуал сразу удаляем, нам нужны только вычисленные границы
                tempVisual.Children.Clear();
            }
        }
        private ShapeBase CreateShapeByType(string typeName)
        {
            return typeName switch
            {
                "RectangleShape" => new RectangleShape(),
                "TriangleShape" => new TriangleShape(),
                "TrapezoidShape" => new TrapezoidShape(),
                "CircleShape" => new CircleShape(),
                "HexagonShape" => new HexagonShape(),
                "CustomShape" => new CustomShape(),
                "CompoundShape" => new CompoundShape(),
                _ => null
            };
        }

        private void SkipShapeData(BinaryReader reader)
        {
            reader.ReadInt32(); reader.ReadDouble(); reader.ReadDouble();
            reader.ReadDouble(); reader.ReadDouble();
            reader.ReadByte(); reader.ReadByte(); reader.ReadByte(); reader.ReadByte();
            int colorCount = reader.ReadInt32();
            for (int i = 0; i < colorCount; i++)
            {
                reader.ReadByte(); reader.ReadByte(); reader.ReadByte(); reader.ReadByte();
            }
            int thickCount = reader.ReadInt32();
            for (int i = 0; i < thickCount; i++) reader.ReadDouble();
            int lockCount = reader.ReadInt32();
            for (int i = 0; i < lockCount; i++) reader.ReadBoolean();
            int vertCount = reader.ReadInt32();
            for (int i = 0; i < vertCount; i++)
            {
                reader.ReadDouble(); reader.ReadDouble();
            }
            reader.ReadInt32();
        }

        private void ClearAllShapes()
        {
            foreach (var visual in _allShapeVisuals)
            {
                if (DrawCanvas.Children.Contains(visual))
                    DrawCanvas.Children.Remove(visual);
            }
            ClearBoundingBox();
            _allShapes = new ShapeBase[0];
            _allShapeVisuals = new Canvas[0];
            _selectedVisuals.Clear();
            _selectedShapeVisual = null;
            _currentShape = null;
            _currentShapeVisual = null;

            // Сброс ID через рефлексию
            var field = typeof(ShapeBase).GetField("_globalIdCounter",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static);
            field?.SetValue(null, 0);

            RefreshShapesTree();
            UpdateParamsPanelVisibility();
        }

        public static bool IsHighlightedChild(CompoundShape parent, ShapeBase child)
        {
            var main = (MainWindow)Application.Current.MainWindow;
            // Проверяем, выделен ли этот ребенок в TreeView прямо сейчас
            if (main.ShapesTreeView.SelectedItem is ShapeTreeItem item)
            {
                return ReferenceEquals(item.Shape, child) &&
                       main._editingParentCompound == parent;
            }
            return false;
        }

        public class JsonShapeFile
        {
            public int version { get; set; }
            public List<JsonShapeData> shapes { get; set; }
        }

        public class JsonShapeData
        {
            public string type { get; set; }
            public int id { get; set; }
            public double worldX { get; set; }
            public double worldY { get; set; }
            public double scale { get; set; }
            public double angle { get; set; }
            public double anchorX { get; set; }
            public double anchorY { get; set; }
            public string fill { get; set; }
            public List<string> sideColors { get; set; }
            public List<double> sideThicknesses { get; set; }
            public List<bool> edgeLocks { get; set; }
            public List<JsonVertex> vertices { get; set; }

            // Новые поля для CustomShape
            public bool isClosed { get; set; }
            public double initialDirection { get; set; }
            public List<JsonSegmentData> segments { get; set; }

            // Для CompoundShape
            public List<JsonShapeData> children { get; set; }
        }

        public class JsonSegmentData
        {
            public string name { get; set; }
            public double length { get; set; }
            public string color { get; set; }
            public double thickness { get; set; }
            public double angleToNext { get; set; }
            public bool angleLocked { get; set; }
            public bool lengthLocked { get; set; }
        }

        public class JsonVertex
        {
            public double x { get; set; }
            public double y { get; set; }
        }


    }

}