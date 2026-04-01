using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Linq;
using Drawing = System.Drawing;

namespace ShapeEditor
{
    public partial class MainWindow : Window
    {
        // Перетаскивание всей фигуры
        private bool potentialDrag;
        private Point startMouse;
        private Canvas draggedShapeCanvas;
        private double startLeft;
        private double startTop;

        // Перетаскивание якоря
        private bool draggingAnchor;
        private Point dragStartWorld;
        private Point originalAnchorPos;
        private Canvas anchorDragCanvas;

        // Текущая фигура и визуал
        private ShapeBase _currentShape;
        private Canvas _currentShapeVisual;
        private Canvas _selectedShapeVisual;
        private Rectangle _boundingBoxVisual;

        private List<ShapeBase> _allShapes = new();
        private List<Canvas> _allShapeVisuals = new();

        private bool _isProcessingMove;

        // Панель параметров
        private Button _paramsShowButton;
        private StackPanel _paramsStackPanel;
        private bool _paramsPanelIsOpen = false;

        // Ссылки на динамические элементы (обновляются при каждой перестройке)
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

        // Длины рёбер (граней)
        private List<TextBox> _edgeLengthBoxes = new();
        private List<CheckBox> _edgeLockBoxes = new();
        private CheckBox _isoscelesCheckBox;

        // Флаг, чтобы отличать программное обновление полей длины рёбер от пользовательского ввода
        private bool _isUpdatingEdgeLengthText;

        // Отложенные значения, введённые пользователем, применяются по потере фокуса или по кнопке Применить
        private List<double?> _pendingEdgeLengths = new();

        // Добавлено в начало класса MainWindow (поля для режима создания кастомной фигуры)
        private bool _isCreatingCustomShape = false;
        private CustomShape _creatingCustomShape = null;
        private int _creatingNextIndex = 0;

        // Контролы панели параметров для создания сегмента
        private TextBox _newSegmentLengthBox;
        private TextBox _newSegmentAngleBox;
        private Button _setSegmentButton;
        private Button _closeShapeButton;
        private Button _cancelCreateButton;

        // Визуал подсветки редактируемого (или последнего) сегмента
        private System.Windows.Shapes.Shape _segmentHighlight = null;
        private UIElement _segmentHighlightContainer = null;

        // Поля для работы с комплексными фигурами
        private CompoundShape _editingParentCompound = null; // Родитель, если мы внутри группы
        private ShapeBase _childShapeBeforeEdit = null;      // Копия или ссылка для отмены (опционально)
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
            var visual = CreateShapeVisual(custom, DrawCanvas.ActualWidth / 2, DrawCanvas.ActualHeight / 2);
            DrawCanvas.Children.Add(visual);
            _allShapes.Add(custom);
            _allShapeVisuals.Add(visual);

            // Ставим в режим создания
            _isCreatingCustomShape = true;
            _creatingCustomShape = custom;
            _creatingNextIndex = 0;

            // Выбираем этот визуал — чтобы в панели отобразились элементы создания
            SelectShape(visual);
        }

        private void AddCompoundShape(object sender, RoutedEventArgs e)
        {
            var compound = new CompoundShape();
            // Список пуст изначально!

            var visual = CreateShapeVisual(compound, DrawCanvas.ActualWidth / 2, DrawCanvas.ActualHeight / 2);
            DrawCanvas.Children.Add(visual);
            _allShapes.Add(compound);
            _allShapeVisuals.Add(visual);
            SelectShape(visual);
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

                // Удаляем из списков
                _allShapeVisuals.Remove(_selectedShapeVisual);
                _allShapes.Remove(_currentShape);

                // Снимаем выделение
                _selectedShapeVisual = null;
                _currentShape = null;
                _currentShapeVisual = null;
                _boundingBoxVisual = null;

                // Обновляем панель параметров
                UpdateParamsPanelVisibility();

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

            var visual = CreateShapeVisual(shapeBase, worldAnchor.X, worldAnchor.Y);
            DrawCanvas.Children.Add(visual);
            _allShapes.Add(shapeBase);
            _allShapeVisuals.Add(visual);

            SelectShape(visual);
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

            foreach (UIElement child in canvas.Children)
            {
                if (child is Ellipse ellipse && ellipse.Tag is string tag && tag == "Anchor")
                {
                    ellipse.MouseLeftButtonDown += VertexAnchor_Down;
                    ellipse.MouseMove += VertexAnchor_Move;
                    ellipse.MouseLeftButtonUp += VertexAnchor_Up;
                    ellipse.Cursor = Cursors.SizeAll;
                }
            }

            return canvas;
        }

        private void RedrawPreservingAnchor()
        {
            if (_currentShape == null || _currentShapeVisual == null)
                return;

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
            int idx = _allShapes.IndexOf(_currentShape);
            if (idx >= 0)
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

            // Проходим по всем дочерним элементам панели параметров
            foreach (var child in _paramsStackPanel.Children)
            {
                if (child is Grid row)
                {
                    // Ищем строки с координатами вершин (они содержат 3 колонки)
                    if (row.ColumnDefinitions.Count == 3)
                    {
                        // Ищем локальные координаты (колонка 1)
                        if (row.Children.Count > 1 && row.Children[1] is StackPanel localPanel)
                        {
                            // Ищем TextBox'ы с координатами
                            var textBoxes = localPanel.Children.OfType<TextBox>().ToList();
                            if (textBoxes.Count >= 2)
                            {
                                // Получаем индекс вершины из Tag первого TextBox
                                if (textBoxes[0].Tag is int vertexIndex)
                                {
                                    // Обновляем локальные координаты
                                    textBoxes[0].Text = (_currentShape.Vertices[vertexIndex].X - _currentShape.AnchorPoint.X).ToString("0");
                                    textBoxes[1].Text = (_currentShape.Vertices[vertexIndex].Y - _currentShape.AnchorPoint.Y).ToString("0");
                                }
                            }
                        }

                        // Ищем глобальные координаты (колонка 2)
                        if (row.Children.Count > 2 && row.Children[2] is StackPanel worldPanel)
                        {
                            var worldTextBoxes = worldPanel.Children.OfType<TextBox>().ToList();
                            if (worldTextBoxes.Count >= 2 && worldTextBoxes[0].Tag is int vertexIndex)
                            {
                                Point worldPos = GetVertexWorldPosition(_currentShape, _currentShapeVisual, vertexIndex);
                                worldTextBoxes[0].Text = worldPos.X.ToString("0");
                                worldTextBoxes[1].Text = worldPos.Y.ToString("0");
                            }
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

                // Кнопки добавления новых фигур в группу
                var addButtonsRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
                string[] types = { "Прямоугольник", "Треугольник", "Трапеция", "Круг", "Шестиугольник" };
                foreach (var type in types)
                {
                    var btn = new Button { Content = "+" + type, Margin = new Thickness(2), Padding = new Thickness(4, 2, 4, 2) };
                    btn.Click += (s, e) => { AddChildToCompound(compound, type); };
                    addButtonsRow.Children.Add(btn);
                }
                _paramsStackPanel.Children.Add(addButtonsRow);

                // Список существующих фигур в группе
                var listContainer = new StackPanel();
                for (int i = 0; i < compound.ChildShapes.Count; i++)
                {
                    var child = compound.ChildShapes[i];
                    int index = i;

                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    // Внутри цикла создания списка фигур:
                    var nameBtn = new Button
                    {
                        Content = $"{index + 1}. {child.GetType().Name.Replace("Shape", "")}",
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Background = Brushes.White,
                        Foreground = Brushes.Black, // Явный цвет текста
                        BorderBrush = Brushes.LightGray,
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(5, 2, 5, 2)
                    };

                    // Подсветка при клике на имя в списке
                    nameBtn.Click += (s, e) => { HighlightChildInCompound(compound, index); };

                    var editBtn = new Button { Content = "✎", Margin = new Thickness(5, 0, 0, 0), Width = 25 };
                    editBtn.Click += (s, e) => { StartEditingChild(compound, child); };

                    var delBtn = new Button { Content = "X", Margin = new Thickness(5, 0, 0, 0), Width = 25, Foreground = Brushes.Red };
                    delBtn.Click += (s, e) => {
                        compound.RemoveChildShape(child);
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
                    Text = "Длины рёбер:",
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 14, 0, 6)
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
                IsReadOnly = false,                       // ← теперь можно редактировать
                Background = Brushes.White,               // чтобы было видно, что редактируемо
                BorderBrush = Brushes.Gray
            };
            _worldAnchorXBox.TextChanged += WorldAnchorX_TextChanged;   // ← новый обработчик

            _worldAnchorYBox = new TextBox
            {
                Width = 50,
                Margin = new Thickness(0, 0, 0, 0),
                Text = world.Y.ToString("0"),
                IsReadOnly = false,
                Background = Brushes.White,
                BorderBrush = Brushes.Gray
            };
            _worldAnchorYBox.TextChanged += WorldAnchorY_TextChanged;   // ← новый обработчик


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
                    RefreshParamsPanelValues();   // ← ВАЖНО
            };

            _angleTextBox = new TextBox
            {
                Width = 30,
                Margin = new Thickness(6, 0, 0, 0),          // ← здесь главное уменьшение: было 12 → стало 8
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
                        RefreshParamsPanelValues();  // ← ВАЖНО
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
                return $"#{scb.Color.A:X2}{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B}";
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

        // ────────────────────────────────────────────────
        // Перетаскивание фигур и якоря (без изменений)
        // ────────────────────────────────────────────────

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

            _currentShapeVisual = CreateShapeVisual(_currentShape, worldAnchor.X, worldAnchor.Y);
            DrawCanvas.Children.Add(_currentShapeVisual);

            // Обновляем глобальный список визуалов, если фигура там есть
            int idx = _allShapes.IndexOf(_currentShape);
            if (idx >= 0)
                _allShapeVisuals[idx] = _currentShapeVisual;

            if (wasSelected)
            {
                _selectedShapeVisual = _currentShapeVisual;
                ShowBoundingBox(_currentShapeVisual);
            }

            // Обновление панели параметров при перетаскивании якоря
            if (_paramsPanelIsOpen) RefreshParamsPanelValues();

            // Восстанавливаем захват мыши для точки якоря
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
            anchorDragCanvas = null;
            e.Handled = true;
        }

        private void ShapeCanvas_Down(object sender, MouseButtonEventArgs e)
        {
            if (draggingAnchor) return;

            if (e.ClickCount == 2 && sender is Canvas canvas && canvas.Tag is ShapeBase shape)
            {
                _currentShape = shape;
                _currentShapeVisual = canvas;
                e.Handled = true;
                return;
            }

            if (sender is Canvas clickedCanvas)
                SelectShape(clickedCanvas);

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
            double deltaX = pos.X - startMouse.X;
            double deltaY = pos.Y - startMouse.Y;

            if (_editingParentCompound != null && _currentShape != _editingParentCompound)
            {
                // МЫ РЕДАКТИРУЕМ РЕБЕНКА ВНУТРИ ГРУППЫ
                // Двигаем его якорь относительно группы
                double localDeltaX = deltaX / _editingParentCompound.Scale;
                double localDeltaY = deltaY / _editingParentCompound.Scale;

                // Здесь нужна логика обратного поворота, если группа повернута
                // Но для простоты пока сдвинем якорь:
                var p = _currentShape.AnchorPoint;
                p.X = Math.Round(originalAnchorPos.X + localDeltaX);
                p.Y = Math.Round(originalAnchorPos.Y + localDeltaY);
                _currentShape.AnchorPoint = p;

                RedrawPreservingAnchor();
            }
            else
            {
                // Двигаем обычную фигуру или всю группу целиком
                double newLeft = startLeft + deltaX;
                double newTop = startTop + deltaY;

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

            int shapeIndex = _allShapes.IndexOf(_creatingCustomShape);
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
        }

        private void OnCancelCreatingShape(object? sender, RoutedEventArgs e)
        {
            if (!_isCreatingCustomShape || _creatingCustomShape == null) return;

            // Удаляем визуал и модель, откатываем
            var idx = _allShapes.IndexOf(_creatingCustomShape);
            if (idx >= 0)
            {
                var visual = _allShapeVisuals[idx];
                if (DrawCanvas.Children.Contains(visual)) DrawCanvas.Children.Remove(visual);
                _allShapes.RemoveAt(idx);
                _allShapeVisuals.RemoveAt(idx);
            }

            _isCreatingCustomShape = false;
            _creatingCustomShape = null;
            ClearCustomCreationState();
            ClearSelection();
        }

        // Помощники — подсветка сегмента и очистка состояния создания
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
            // Если ничего не нашли — ничего не делаем (можно добавить лог/отладку)
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
            _editingParentCompound = parent;

            // Временно делаем "ребёнка" текущей фигурой для всех механизмов редактирования
            _currentShape = child;

            // Нам нужно найти визуальный объект (Canvas) именно этого ребёнка.
            // В CompoundShape.Build() мы помечали детей через Tag.
            foreach (var element in _currentShapeVisual.Children)
            {
                if (element is Canvas childCanvas && ReferenceEquals(childCanvas.Tag, child))
                {
                    _selectedShapeVisual = childCanvas;
                    break;
                }
            }

            RebuildParamsPanel();
            ShowBoundingBox(_selectedShapeVisual);
        }

        private void StopEditingChild()
        {
            if (_editingParentCompound == null) return;

            var parent = _editingParentCompound;
            _editingParentCompound = null;
            _currentShape = parent;

            // Ищем визуал родителя в общем списке
            int idx = _allShapes.IndexOf(parent);
            if (idx >= 0)
            {
                _currentShapeVisual = _allShapeVisuals[idx];
                _selectedShapeVisual = _currentShapeVisual;
            }

            ClearBoundingBox();
            RedrawPreservingAnchor(); // Это перестроит все дерево
            RebuildParamsPanel();
        }

        private void HighlightChildInCompound(CompoundShape parent, int childIndex)
        {
            if (childIndex < 0 || childIndex >= parent.ChildShapes.Count) return;
            var child = parent.ChildShapes[childIndex];

            // Ищем Canvas ребенка внутри визуального дерева родителя
            foreach (var element in _currentShapeVisual.Children)
            {
                if (element is Canvas childCanvas && ReferenceEquals(childCanvas.Tag, child))
                {
                    ShowBoundingBox(childCanvas); // Показываем рамку только для этой части
                    return;
                }
            }
        }

    }
}