using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace ShadePilotWpf
{
    /// <summary>
    /// Small, dependency-free color picker used by both basic and advanced
    /// crosshair editors. HSV is the primary interaction; RGB/HEX are kept as
    /// secondary precise inputs.
    /// </summary>
    internal sealed class CrosshairColorPickerDialog : Window
    {
        readonly Window owner;
        readonly Canvas svCanvas = new() { Width = 300, Height = 180, ClipToBounds = true };
        readonly Rectangle svWhite = new(), svBlack = new();
        readonly Ellipse svMarker = new() { Width = 14, Height = 14, Stroke = Brushes.White, StrokeThickness = 2 };
        readonly Slider hueSlider = new() { Minimum = 0, Maximum = 360, Height = 24 };
        readonly Border preview = new() { Width = 42, Height = 28, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1) };
        readonly TextBox hexBox = new(), redBox = new(), greenBox = new(), blueBox = new();
        readonly List<Button> swatchButtons = new();
        bool updating, draggingSv;
        double hue, saturation, value;
        Color selectedColor;
        public Color SelectedColor => selectedColor;
        public event Action<Color> ColorChanged;

        public CrosshairColorPickerDialog(Window owner, Color initial)
        {
            this.owner = owner;
            Owner = owner; Title = "选择准星颜色"; Width = 390; Height = 660; ResizeMode = ResizeMode.NoResize; WindowStyle = WindowStyle.None; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), CornerRadius = new CornerRadius(10), GlassFrameThickness = new Thickness(0) });
            Background = Brush("Canvas", new SolidColorBrush(Color.FromRgb(250, 246, 239)));
            BuildUi(); SetColor(initial);
        }

        Brush Brush(string key, Brush fallback) => owner?.TryFindResource(key) as Brush ?? fallback;
        Style Style(string key) => owner?.TryFindResource(key) as Style;

        void BuildUi()
        {
            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(new TextBlock { Text = "选择准星颜色", Foreground = Brush("Text", Brushes.Black), FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
            var svWrap = new Border { Background = Brushes.Transparent, BorderBrush = Brush("Border", Brushes.Gray), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left };
            svCanvas.Children.Add(svWhite); svCanvas.Children.Add(svBlack); svCanvas.Children.Add(svMarker); svWrap.Child = svCanvas; root.Children.Add(svWrap);
            svWhite.Width = svBlack.Width = svCanvas.Width; svWhite.Height = svBlack.Height = svCanvas.Height;
            svWhite.Fill = new LinearGradientBrush(Colors.White, Colors.Transparent, new Point(0, 0), new Point(1, 0));
            svBlack.Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, new Point(0, 0), new Point(0, 1));
            svCanvas.MouseLeftButtonDown += (_, e) => { draggingSv = true; svCanvas.CaptureMouse(); SetSv(e.GetPosition(svCanvas)); };
            svCanvas.MouseMove += (_, e) => { if (draggingSv) SetSv(e.GetPosition(svCanvas)); };
            svCanvas.MouseLeftButtonUp += (_,__) => { draggingSv = false; svCanvas.ReleaseMouseCapture(); };
            root.Children.Add(new TextBlock { Text = "色相", Foreground = Brush("Muted", Brushes.Gray), Margin = new Thickness(0, 12, 0, 2) });
            hueSlider.ValueChanged += (_,__) => { if (!updating) { hue = hueSlider.Value; UpdateFromHsv(); } };
            if (Style("LPSlider") is { } sliderStyle) hueSlider.Style = sliderStyle;
            root.Children.Add(hueSlider);
            root.Children.Add(new TextBlock { Text = "常用颜色", Foreground = Brush("Muted", Brushes.Gray), Margin = new Thickness(0, 10, 0, 5) });
            var swatches = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var c in new[] { "#FFFFFFFF", "#FFFFD166", "#FFFF6B6B", "#FF63E6BE", "#FF4D96FF", "#FFB983FF", "#FF22252D", "#FF000000", "#FF52D273", "#FFFF9F43" })
            {
                var color = (Color)ColorConverter.ConvertFromString(c);
                var b = new Button { Width = 30, Height = 30, Margin = new Thickness(0, 0, 7, 0), Padding = new Thickness(0), Background = new SolidColorBrush(color), BorderBrush = Brush("Border", Brushes.Gray), BorderThickness = new Thickness(1), Tag = color, ToolTip = $"#{color.R:X2}{color.G:X2}{color.B:X2}" };
                if (Style("LPSecondary") is { } buttonStyle) b.Style = buttonStyle;
                b.Click += (_,__) => SetColor((Color)b.Tag); swatchButtons.Add(b); swatches.Children.Add(b);
            }
            root.Children.Add(swatches);
            var current = new Grid { Margin = new Thickness(0, 2, 0, 8) };
            current.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); current.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); current.ColumnDefinitions.Add(new ColumnDefinition());
            current.Children.Add(new TextBlock { Text = "当前颜色", Foreground = Brush("Muted", Brushes.Gray), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(preview, 1); preview.Margin = new Thickness(12, 0, 10, 0); preview.BorderBrush = Brush("Border", Brushes.Gray); current.Children.Add(preview);
            Grid.SetColumn(hexBox, 2); hexBox.VerticalContentAlignment = VerticalAlignment.Center; hexBox.ToolTip = "输入 #RRGGBB 后按 Enter 或离开输入框应用"; ApplyTextBoxStyle(hexBox); current.Children.Add(hexBox); root.Children.Add(current);
            var precise = new Expander { Header = "精确编辑（HEX / RGB）", Foreground = Brush("Muted", Brushes.Gray), Margin = new Thickness(0, 4, 0, 0) };
            var fields = new Grid { Margin = new Thickness(0, 9, 0, 0) };
            fields.ColumnDefinitions.Add(new ColumnDefinition()); fields.ColumnDefinitions.Add(new ColumnDefinition()); fields.ColumnDefinitions.Add(new ColumnDefinition());
            AddField(fields, "R", redBox, 0); AddField(fields, "G", greenBox, 1); AddField(fields, "B", blueBox, 2); precise.Content = fields; root.Children.Add(precise);
            hexBox.LostFocus += (_,__) => ApplyHex(); redBox.LostFocus += (_,__) => ApplyRgb(); greenBox.LostFocus += (_,__) => ApplyRgb(); blueBox.LostFocus += (_,__) => ApplyRgb();
            var footer = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "取消", MinWidth = 76, Margin = new Thickness(8, 0, 0, 0), Style = Style("LPSecondary") }; cancel.Click += (_,__) => { DialogResult = false; };
            var ok = new Button { Content = "确定", MinWidth = 76, Margin = new Thickness(8, 0, 0, 0), IsDefault = true, Style = Style("LPPrimary") }; ok.Click += (_,__) => { DialogResult = true; };
            actions.Children.Add(cancel); actions.Children.Add(ok); DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions); root.Children.Add(footer);
            var shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
            shell.RowDefinitions.Add(new RowDefinition());
            var header = new Border { Background = Brush("Tint", Brushes.White), BorderBrush = Brush("Border", Brushes.Gray), BorderThickness = new Thickness(0, 0, 0, 1) };
            var headerGrid = new Grid { Margin = new Thickness(18, 0, 14, 0) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.Children.Add(new TextBlock { Text = "选择颜色", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Brush("Text", Brushes.Black), VerticalAlignment = VerticalAlignment.Center });
            var close = new Button { Content = "×", Style = Style("LPWindowControl"), VerticalAlignment = VerticalAlignment.Center };
            close.Click += (_,__) => Close();
            Grid.SetColumn(close, 1); headerGrid.Children.Add(close); header.Child = headerGrid;
            header.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
            shell.Children.Add(header);
            Grid.SetRow(root, 1); shell.Children.Add(root);
            Content = shell;
        }

        static void AddField(Grid grid, string label, TextBox box, int column)
        {
            var p = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 6, 0, 6, 0) }; p.Children.Add(new TextBlock { Text = label, FontSize = 11 }); box.Margin = new Thickness(0, 3, 0, 0); p.Children.Add(box); Grid.SetColumn(p, column); grid.Children.Add(p);
        }

        void ApplyTextBoxStyle(TextBox box) { if (Style("LPTextBox") is { } style) box.Style = style; }

        void SetSv(Point point)
        {
            saturation = Math.Clamp(point.X / svCanvas.Width, 0, 1); value = 1 - Math.Clamp(point.Y / svCanvas.Height, 0, 1); UpdateFromHsv();
        }

        void SetColor(Color color)
        {
            selectedColor = Color.FromRgb(color.R, color.G, color.B); RgbToHsv(selectedColor, out hue, out saturation, out value); updating = true; hueSlider.Value = hue; updating = false; UpdateSvVisual(); UpdateFields(); ColorChanged?.Invoke(selectedColor);
        }

        void UpdateFromHsv() { selectedColor = HsvToRgb(hue, saturation, value); UpdateSvVisual(); UpdateFields(); ColorChanged?.Invoke(selectedColor); }

        void UpdateSvVisual()
        {
            svCanvas.Background = new SolidColorBrush(HsvToRgb(hue, 1, 1));
            Canvas.SetLeft(svMarker, Math.Clamp(saturation * svCanvas.Width - svMarker.Width / 2, 0, svCanvas.Width - svMarker.Width));
            Canvas.SetTop(svMarker, Math.Clamp((1 - value) * svCanvas.Height - svMarker.Height / 2, 0, svCanvas.Height - svMarker.Height));
            preview.Background = new SolidColorBrush(selectedColor); UpdateSwatchSelection();
        }

        void UpdateSwatchSelection()
        {
            foreach (var b in swatchButtons)
            {
                var c = b.Tag is Color color ? color : Colors.Transparent;
                b.BorderBrush = c.R == selectedColor.R && c.G == selectedColor.G && c.B == selectedColor.B ? Brush("Accent", Brushes.Gray) : Brush("Border", Brushes.Gray);
                b.BorderThickness = c.R == selectedColor.R && c.G == selectedColor.G && c.B == selectedColor.B ? new Thickness(2) : new Thickness(1);
            }
        }

        void UpdateFields()
        {
            updating = true; hexBox.Text = $"#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}"; redBox.Text = selectedColor.R.ToString(CultureInfo.InvariantCulture); greenBox.Text = selectedColor.G.ToString(CultureInfo.InvariantCulture); blueBox.Text = selectedColor.B.ToString(CultureInfo.InvariantCulture); updating = false;
        }

        void ApplyHex()
        {
            if (updating) return; try { SetColor((Color)ColorConverter.ConvertFromString(hexBox.Text)); } catch { UpdateFields(); }
        }

        void ApplyRgb()
        {
            if (updating) return; if (byte.TryParse(redBox.Text, out var r) && byte.TryParse(greenBox.Text, out var g) && byte.TryParse(blueBox.Text, out var b)) SetColor(Color.FromArgb(selectedColor.A, r, g, b)); else UpdateFields();
        }

        static void RgbToHsv(Color color, out double h, out double s, out double v)
        {
            var r = color.R / 255d; var g = color.G / 255d; var b = color.B / 255d; var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var delta = max - min; v = max; s = max <= 0 ? 0 : delta / max; if (delta == 0) { h = 0; return; } if (max == r) h = 60 * (((g - b) / delta) % 6); else if (max == g) h = 60 * ((b - r) / delta + 2); else h = 60 * ((r - g) / delta + 4); if (h < 0) h += 360;
        }

        static Color HsvToRgb(double h, double s, double v)
        {
            h = (h % 360 + 360) % 360; var c = v * s; var x = c * (1 - Math.Abs((h / 60 % 2) - 1)); var m = v - c; double r, g, b; if (h < 60) (r, g, b) = (c, x, 0); else if (h < 120) (r, g, b) = (x, c, 0); else if (h < 180) (r, g, b) = (0, c, x); else if (h < 240) (r, g, b) = (0, x, c); else if (h < 300) (r, g, b) = (x, 0, c); else (r, g, b) = (c, 0, x); return Color.FromArgb(255, (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }
    }
}
