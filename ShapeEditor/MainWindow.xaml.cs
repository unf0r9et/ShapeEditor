using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

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

        private TextBox _localAnchorXBox, _localAnchorYBox;
        private TextBox _worldAnchorXBox, _worldAnchorYBox;

        private Slider _scaleSlider;
        private TextBox _scaleTextBox;
        private Slider _angleSlider;
        private TextBox _angleTextBox;

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

        private void DrawCanvas_BackgroundClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == DrawCanvas)
                ClearSelection();
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

        private void ShowBoundingBox(Canvas shapeVisual)
        {
            var shape = shapeVisual.Tag as ShapeBase;
            if (shape == null) return;

            _boundingBoxVisual = new Rectangle
            {
                Width = shapeVisual.Width,
                Height = shapeVisual.Height,
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(_boundingBoxVisual, Canvas.GetLeft(shapeVisual));
            Canvas.SetTop(_boundingBoxVisual, Canvas.GetTop(shapeVisual));
            DrawCanvas.Children.Add(_boundingBoxVisual);
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
            if (_currentShape == null || _currentShapeVisual == null) return;

            Point worldAnchor = _currentShape.GetAnchorWorldPosition(_currentShapeVisual);
            bool wasSelected = (_selectedShapeVisual == _currentShapeVisual);

            if (DrawCanvas.Children.Contains(_currentShapeVisual))
                DrawCanvas.Children.Remove(_currentShapeVisual);
            if (_boundingBoxVisual != null && DrawCanvas.Children.Contains(_boundingBoxVisual))
                DrawCanvas.Children.Remove(_boundingBoxVisual);

            _currentShapeVisual = CreateShapeVisual(_currentShape, worldAnchor.X, worldAnchor.Y);
            DrawCanvas.Children.Add(_currentShapeVisual);

            if (wasSelected)
            {
                _selectedShapeVisual = _currentShapeVisual;
                ShowBoundingBox(_currentShapeVisual);
            }

            if (_paramsPanelIsOpen)
                RefreshParamsPanelValues();
        }

        private void RefreshParamsPanelValues()
        {
            if (_currentShape == null) return;

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

            bool isCircle = _currentShape is CircleShape;
            int sides = _currentShape.SidesCount;
            string[] names = _currentShape.SideNames ?? Array.Empty<string>();

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
                _thicknessTextBoxes.Add(thickTb);

                row.Children.Add(colorTb);
                row.Children.Add(swatch);
                row.Children.Add(thickLabel);
                row.Children.Add(thickTb);

                _paramsStackPanel.Children.Add(row);
            }

            // Заливка
            var fillRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 12) };
            fillRow.Children.Add(new TextBlock
            {
                Text = "Заливка:",
                Width = 110,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
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

            // Вершины
            if (!isCircle && sides > 0)
            {
                _paramsStackPanel.Children.Add(new TextBlock
                {
                    Text = "Вершины (X, Y):",
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 14, 0, 6)
                });

                for (int i = 0; i < sides; i++)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };

                    row.Children.Add(new TextBlock
                    {
                        Text = $"V{i + 1}:",
                        Width = 35,
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    var xTb = new TextBox
                    {
                        Width = 50,
                        Margin = new Thickness(6, 0, 6, 0),
                        Text = _currentShape.Vertices[i].X.ToString("0"),
                        Tag = i
                    };
                    xTb.TextChanged += VertexX_TextChanged;
                    _vertexXBoxes.Add(xTb);

                    var yTb = new TextBox
                    {
                        Width = 50,
                        Margin = new Thickness(0, 0, 0, 0),
                        Text = _currentShape.Vertices[i].Y.ToString("0"),
                        Tag = i
                    };
                    yTb.TextChanged += VertexY_TextChanged;
                    _vertexYBoxes.Add(yTb);

                    row.Children.Add(xTb);
                    row.Children.Add(yTb);
                    _paramsStackPanel.Children.Add(row);
                }
            }

            // Точка привязки
            _paramsStackPanel.Children.Add(new TextBlock
            {
                Text = "Точка привязки",
                FontWeight = FontWeights.SemiBold,
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
                FontWeight = FontWeights.SemiBold,
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
                if (_angleTextBox != null) _angleTextBox.Text = ((int)ev.NewValue).ToString();
                RedrawPreservingAnchor();
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
                    if (_angleSlider != null) _angleSlider.Value = v;
                    RedrawPreservingAnchor();
                }
            };

            Grid.SetColumn(_angleSlider, 0);
            Grid.SetColumn(_angleTextBox, 1);
            angleGrid.Children.Add(_angleSlider);
            angleGrid.Children.Add(_angleTextBox);
            anglePanel.Children.Add(angleGrid);
            _paramsStackPanel.Children.Add(anglePanel);
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

                _currentShape.SideColors[idx] = brush;
                swatch.Background = brush;
                if (idx < _colorTextBoxes.Count)
                    _colorTextBoxes[idx].Text = GetColorHex(brush);

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
                RedrawPreservingAnchor();
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
                    p.X = v;
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
                    p.Y = v;
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

            if (wasSelected)
            {
                _selectedShapeVisual = _currentShapeVisual;
                ShowBoundingBox(_currentShapeVisual);
            }

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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}