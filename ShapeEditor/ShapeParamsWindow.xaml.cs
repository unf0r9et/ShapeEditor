using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace ShapeEditor
{
    public partial class ShapeParamsWindow : Window
    {
        public List<Brush> Colors { get; private set; } = new();
        public List<double> Thicknesses { get; private set; } = new();
        public Brush Fill { get; private set; } = Brushes.Transparent;
        public double Scale { get; private set; } = 1.0;
        public double Angle { get; private set; } = 0;
        public Point[] Vertices { get; private set; }
        public Point WorldAnchor { get; private set; } // абсолютные координаты точки привязки
        public Point AnchorPoint { get; private set; } // локальные (относительные) координаты точки привязки

        private List<TextBox> colorBoxes = new();
        private List<Border> colorSwatches = new();
        private List<TextBox> thickBoxes = new();
        private TextBox fillTextBox;
        private Border fillSwatch;
        private TextBox scaleTextBox;
        private TextBox angleTextBox;
        private List<TextBox> vertexXBoxes = new();
        private List<TextBox> vertexYBoxes = new();
        private TextBox worldAnchorXBox, worldAnchorYBox; // поля для ввода абсолютных координат
        private TextBox localAnchorXBox, localAnchorYBox; // поля для ввода локальных координат

        private bool _isUniform;
        private int _selectedIndex;

        public ShapeParamsWindow(int sides, string[] sideNames,
            List<Brush> initialColors,
            List<double> initialThicknesses,
            double initialScale,
            Brush initialFill,
            Point[] initialVertices,
            Point initialAnchor, // локальный anchor
            double initialAngle,
            bool uniform = false,
            int selectedIndex = -2,
            Point worldAnchor = default)
        {
            InitializeComponent();
            _isUniform = uniform;
            _selectedIndex = selectedIndex;
            Vertices = initialVertices?.Clone() as Point[] ?? new Point[sides];
            BuildUI(sides, sideNames, uniform, initialColors, initialThicknesses, initialScale, initialFill, initialAnchor, initialAngle, worldAnchor);
        }

        // Публичные методы для обновления координат из MainWindow
        public void UpdateWorldAnchor(Point world)
        {
            if (worldAnchorXBox != null && worldAnchorYBox != null)
            {
                worldAnchorXBox.Text = world.X.ToString("0");
                worldAnchorYBox.Text = world.Y.ToString("0");
            }
        }

        public void UpdateLocalAnchor(Point local)
        {
            if (localAnchorXBox != null && localAnchorYBox != null)
            {
                localAnchorXBox.Text = local.X.ToString("0");
                localAnchorYBox.Text = local.Y.ToString("0");
            }
        }

        private string GetColorHex(Brush brush)
        {
            if (brush is SolidColorBrush scb)
            {
                Color c = scb.Color;
                return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            return "#00FFFFFF";
        }

        private void BuildUI(int sides, string[] sideNames, bool uniform,
            List<Brush> initialColors,
            List<double> initialThicknesses,
            double initialScale,
            Brush initialFill,
            Point initialAnchor,
            double initialAngle,
            Point worldAnchor)
        {
            int fieldsCount = uniform ? 1 : sides;

            // Стороны (цвет и толщина)
            for (int i = 0; i < fieldsCount; i++)
            {
                string sideLabel = uniform ? "Окружность" : $"Сторона {i + 1}";

                var sidePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

                var nameBlock = new TextBlock
                {
                    Text = sideLabel + ":",
                    Width = 90,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.SemiBold
                };
                sidePanel.Children.Add(nameBlock);

                var colorLabel = new TextBlock
                {
                    Text = "Цвет:",
                    Width = 50,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 0, 0)
                };
                sidePanel.Children.Add(colorLabel);

                var colorTextBox = new TextBox
                {
                    IsReadOnly = true,
                    Width = 90,
                    Margin = new Thickness(2, 0, 0, 0),
                    Text = (initialColors != null && i < initialColors.Count) ? GetColorHex(initialColors[i]) : "#FF000000",
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1)
                };
                sidePanel.Children.Add(colorTextBox);

                var colorSwatch = new Border
                {
                    Width = 22,
                    Height = 22,
                    Margin = new Thickness(2, 0, 10, 0),
                    Background = (initialColors != null && i < initialColors.Count) ? initialColors[i] : Brushes.Black,
                    BorderBrush = Brushes.DarkGray,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = i
                };
                colorSwatch.MouseLeftButtonDown += ColorSwatch_MouseDown;
                sidePanel.Children.Add(colorSwatch);

                var thickLabel = new TextBlock
                {
                    Text = "Толщина:",
                    Width = 70,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0)
                };
                sidePanel.Children.Add(thickLabel);

                var thickTextBox = new TextBox
                {
                    Width = 50,
                    Text = (initialThicknesses != null && i < initialThicknesses.Count) ? initialThicknesses[i].ToString() : "3",
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(2, 0, 0, 0)
                };
                sidePanel.Children.Add(thickTextBox);

                Panel.Children.Add(sidePanel);

                colorBoxes.Add(colorTextBox);
                colorSwatches.Add(colorSwatch);
                thickBoxes.Add(thickTextBox);
            }

            // Заливка
            var fillPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 8) };
            fillPanel.Children.Add(new TextBlock
            {
                Text = "Заливка:",
                Width = 90,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
            });

            var fillColorLabel = new TextBlock
            {
                Text = "Цвет:",
                Width = 50,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0)
            };
            fillPanel.Children.Add(fillColorLabel);

            fillTextBox = new TextBox
            {
                IsReadOnly = true,
                Width = 90,
                Margin = new Thickness(2, 0, 0, 0),
                Text = (initialFill != null) ? GetColorHex(initialFill) : "#00FFFFFF",
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1)
            };
            fillPanel.Children.Add(fillTextBox);

            fillSwatch = new Border
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(2, 0, 0, 0),
                Background = initialFill ?? Brushes.Transparent,
                BorderBrush = Brushes.DarkGray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            fillSwatch.MouseLeftButtonDown += FillSwatch_MouseDown;
            fillPanel.Children.Add(fillSwatch);

            Panel.Children.Add(fillPanel);

            // Вершины (если не круг)
            if (!uniform && sides > 0)
            {
                Panel.Children.Add(new TextBlock
                {
                    Text = "Вершины (X, Y):",
                    Margin = new Thickness(0, 20, 0, 10),
                    FontWeight = FontWeights.Bold
                });

                for (int i = 0; i < sides; i++)
                {
                    var vertexPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 0, 6) };
                    vertexPanel.Children.Add(new TextBlock
                    {
                        Text = $"Вершина {i + 1}:",
                        Width = 70,
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    vertexPanel.Children.Add(new TextBlock
                    {
                        Text = "X:",
                        Width = 20,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(5, 0, 0, 0)
                    });
                    var xBox = new TextBox
                    {
                        Width = 55,
                        Margin = new Thickness(2, 0, 5, 0),
                        Text = ((int)Vertices[i].X).ToString(),
                        BorderBrush = Brushes.Gray,
                        BorderThickness = new Thickness(1)
                    };
                    vertexPanel.Children.Add(xBox);

                    vertexPanel.Children.Add(new TextBlock
                    {
                        Text = "Y:",
                        Width = 20,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(5, 0, 0, 0)
                    });
                    var yBox = new TextBox
                    {
                        Width = 55,
                        Text = ((int)Vertices[i].Y).ToString(),
                        BorderBrush = Brushes.Gray,
                        BorderThickness = new Thickness(1)
                    };
                    vertexPanel.Children.Add(yBox);

                    Panel.Children.Add(vertexPanel);

                    vertexXBoxes.Add(xBox);
                    vertexYBoxes.Add(yBox);
                }
            }

            // ---- Абсолютные координаты точки привязки (мировые) ----
            var worldAnchorPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 10) };
            worldAnchorPanel.Children.Add(new TextBlock
            {
                Text = "Абсолютная точка привязки:",
                Width = 140,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
            });
            worldAnchorPanel.Children.Add(new TextBlock { Text = "X:", Width = 20, VerticalAlignment = VerticalAlignment.Center });
            worldAnchorXBox = new TextBox
            {
                Width = 55,
                Margin = new Thickness(2, 0, 5, 0),
                Text = worldAnchor.X.ToString("0"),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1)
            };
            worldAnchorPanel.Children.Add(worldAnchorXBox);
            worldAnchorPanel.Children.Add(new TextBlock { Text = "Y:", Width = 20, VerticalAlignment = VerticalAlignment.Center });
            worldAnchorYBox = new TextBox
            {
                Width = 55,
                Text = worldAnchor.Y.ToString("0"),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1)
            };
            worldAnchorPanel.Children.Add(worldAnchorYBox);
            Panel.Children.Add(worldAnchorPanel);

            // ---- Относительные координаты точки привязки (локальные) ----
            var localAnchorPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 10) };
            localAnchorPanel.Children.Add(new TextBlock
            {
                Text = "Локальная точка привязки:",
                Width = 140,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
            });
            localAnchorPanel.Children.Add(new TextBlock { Text = "X:", Width = 20, VerticalAlignment = VerticalAlignment.Center });
            localAnchorXBox = new TextBox
            {
                Width = 55,
                Margin = new Thickness(2, 0, 5, 0),
                Text = initialAnchor.X.ToString("0"),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1)
            };
            localAnchorPanel.Children.Add(localAnchorXBox);
            localAnchorPanel.Children.Add(new TextBlock { Text = "Y:", Width = 20, VerticalAlignment = VerticalAlignment.Center });
            localAnchorYBox = new TextBox
            {
                Width = 55,
                Text = initialAnchor.Y.ToString("0"),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1)
            };
            localAnchorPanel.Children.Add(localAnchorYBox);
            Panel.Children.Add(localAnchorPanel);

            // Масштаб
            var scalePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 20) };
            scalePanel.Children.Add(new TextBlock
            {
                Text = "Масштаб:",
                Width = 90,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
            });
            scaleTextBox = new TextBox
            {
                Width = 100,
                Text = initialScale.ToString("0.##"),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(5, 0, 0, 0)
            };
            scalePanel.Children.Add(scaleTextBox);
            Panel.Children.Add(scalePanel);

            // Угол поворота
            var anglePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 20) };
            anglePanel.Children.Add(new TextBlock
            {
                Text = "Угол поворота (°):",
                Width = 90,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
            });
            angleTextBox = new TextBox
            {
                Width = 100,
                Text = initialAngle.ToString("0"),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(5, 0, 0, 0)
            };
            anglePanel.Children.Add(angleTextBox);
            Panel.Children.Add(anglePanel);

            // Кнопка OK
            var btn = new Button
            {
                Content = "OK",
                Width = 80,
                Height = 30,
                Margin = new Thickness(0, 15, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = Brushes.LightGray,
                BorderBrush = Brushes.DarkGray,
                Foreground = Brushes.Black,
                BorderThickness = new Thickness(1)
            };
            btn.Click += Ok_Click;
            Panel.Children.Add(btn);

            // Подсветка выбранного элемента
            HighlightSelected();
        }

        private void HighlightSelected()
        {
            if (_selectedIndex == -2) return;

            if (_selectedIndex == -1) // заливка
            {
                if (fillTextBox != null)
                    fillTextBox.BorderBrush = Brushes.Gold;
                if (fillSwatch != null)
                    fillSwatch.BorderBrush = Brushes.Gold;
            }
            else if (_selectedIndex >= 0 && _selectedIndex < colorBoxes.Count) // сторона
            {
                if (_selectedIndex < colorBoxes.Count)
                    colorBoxes[_selectedIndex].BorderBrush = Brushes.Gold;
                if (_selectedIndex < colorSwatches.Count)
                    colorSwatches[_selectedIndex].BorderBrush = Brushes.Gold;
                if (_selectedIndex < thickBoxes.Count)
                    thickBoxes[_selectedIndex].BorderBrush = Brushes.Gold;
            }
        }

        private void ColorSwatch_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var swatch = sender as Border;
            int index = (int)swatch.Tag;
            using (var cd = new Forms.ColorDialog())
            {
                try
                {
                    if (new BrushConverter().ConvertFromString(colorBoxes[index].Text) is SolidColorBrush b)
                        cd.Color = Drawing.Color.FromArgb(b.Color.A, b.Color.R, b.Color.G, b.Color.B);
                }
                catch { }
                if (cd.ShowDialog() == Forms.DialogResult.OK)
                {
                    var sc = cd.Color;
                    var mediaColor = System.Windows.Media.Color.FromArgb(sc.A, sc.R, sc.G, sc.B);
                    var newBrush = new SolidColorBrush(mediaColor);
                    colorBoxes[index].Text = GetColorHex(newBrush);
                    swatch.Background = newBrush;
                }
            }
        }

        private void FillSwatch_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            using (var cd = new Forms.ColorDialog())
            {
                try
                {
                    if (new BrushConverter().ConvertFromString(fillTextBox.Text) is SolidColorBrush b)
                        cd.Color = Drawing.Color.FromArgb(b.Color.A, b.Color.R, b.Color.G, b.Color.B);
                }
                catch { }
                if (cd.ShowDialog() == Forms.DialogResult.OK)
                {
                    var sc = cd.Color;
                    var mediaColor = System.Windows.Media.Color.FromArgb(sc.A, sc.R, sc.G, sc.B);
                    var newBrush = new SolidColorBrush(mediaColor);
                    fillTextBox.Text = GetColorHex(newBrush);
                    fillSwatch.Background = newBrush;
                }
            }
        }

        private void Ok_Click(object s, RoutedEventArgs e)
        {
            // Цвета сторон
            for (int i = 0; i < colorBoxes.Count; i++)
            {
                try
                {
                    Colors.Add((Brush)new BrushConverter().ConvertFromString(colorBoxes[i].Text));
                }
                catch { Colors.Add(Brushes.Black); }
            }

            // Толщины сторон
            for (int i = 0; i < thickBoxes.Count; i++)
            {
                if (double.TryParse(thickBoxes[i].Text, out var t))
                    Thicknesses.Add(t);
                else
                    Thicknesses.Add(3);
            }

            // Заливка
            try
            {
                Fill = (Brush)new BrushConverter().ConvertFromString(fillTextBox.Text);
            }
            catch { Fill = Brushes.Transparent; }

            // Масштаб
            if (double.TryParse(scaleTextBox.Text, out var scale))
                Scale = Math.Max(0.1, Math.Min(30, scale));
            else
                Scale = 1.0;

            // Угол
            if (double.TryParse(angleTextBox.Text, out var angle))
                Angle = angle;
            else
                Angle = 0;

            // Вершины
            if (vertexXBoxes.Count > 0)
            {
                int n = vertexXBoxes.Count;
                Vertices = new Point[n];
                for (int i = 0; i < n; i++)
                {
                    int x = 0, y = 0;
                    int.TryParse(vertexXBoxes[i].Text, out x);
                    int.TryParse(vertexYBoxes[i].Text, out y);
                    Vertices[i] = new Point(x, y);
                }
            }

            // Абсолютные координаты точки привязки
            double wx = 0, wy = 0;
            double.TryParse(worldAnchorXBox.Text, out wx);
            double.TryParse(worldAnchorYBox.Text, out wy);
            WorldAnchor = new Point(wx, wy);

            // Локальные координаты точки привязки
            double lx = 0, ly = 0;
            double.TryParse(localAnchorXBox.Text, out lx);
            double.TryParse(localAnchorYBox.Text, out ly);
            AnchorPoint = new Point(lx, ly);

            DialogResult = true;
        }
    }
}