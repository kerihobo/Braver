// This program and the accompanying materials are made available under the terms of the
// Eclipse Public License v2.0 which accompanies this distribution, and is available at
// https://www.eclipse.org/legal/epl-v20.html
//
// SPDX-License-Identifier: EPL-2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CrossSlash {
    internal sealed class FieldLightingEditorWindow : Window {
        private readonly FieldPreviewLightingSettings _initialSettings;
        private readonly Action<FieldPreviewLightingSettings> _onUpdate;
        private readonly TextBox _ambientColor;
        private readonly CheckBox _dynamicLighting;
        private readonly List<(TextBox Color, TextBox X, TextBox Y, TextBox Z)> _pointLightInputs = new();
        private readonly Dictionary<TextBox, int> _lastValidColors = new();

        public FieldLightingEditorWindow(FieldPreviewLightingSettings settings, Action<FieldPreviewLightingSettings> onApply) {
            _initialSettings = settings.Clone();
            _onUpdate = onApply;
            var content = new StackPanel { Margin = new Thickness(18) };

            content.Children.Add(DesktopUi.CreateSectionTitle("Global tint"));
            var globalRow = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            globalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
            globalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            globalRow.Children.Add(DesktopUi.CreateLabel("Color"));
            _ambientColor = CreateColorValue(settings.AmbientColor);
            var ambientEditor = CreateColorEditor(_ambientColor);
            Grid.SetColumn(ambientEditor, 1);
            globalRow.Children.Add(ambientEditor);
            content.Children.Add(globalRow);

            var refreshRow = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            refreshRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _dynamicLighting = new CheckBox {
                Content = "Refresh every frame",
                IsChecked = settings.DynamicLighting,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "On: refresh field lighting every frame (PC behavior). Off: refresh once when the scene loads (original PSX behavior).",
            };
            _dynamicLighting.Checked += (_, _) => UpdatePreview();
            _dynamicLighting.Unchecked += (_, _) => UpdatePreview();
            refreshRow.Children.Add(_dynamicLighting);
            content.Children.Add(refreshRow);

            content.Children.Add(DesktopUi.CreateSectionTitle("Point lights"));
            var header = CreateLightGrid();
            header.Margin = new Thickness(0, 0, 0, 4);
            string[] headers = { "Light", "X", "Y", "Z", "Color" };
            for (int i = 0; i < headers.Length; i++) {
                var label = new TextBlock {
                    Text = headers[i],
                    Foreground = DesktopUi.TextSecondaryBrush,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(4, 0, 4, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(label, i);
                header.Children.Add(label);
            }
            content.Children.Add(header);

            for (int i = 0; i < settings.PointLights.Count; i++) {
                FieldPreviewPointLight light = settings.PointLights[i];
                var row = CreateLightGrid();
                var name = new TextBlock {
                    Text = $"Light {i + 1}",
                    Foreground = DesktopUi.TextPrimaryBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 4, 0),
                };
                Grid.SetColumn(name, 0);
                row.Children.Add(name);
                var x = CreateCoordinateBox(light.X);
                var y = CreateCoordinateBox(light.Y);
                var z = CreateCoordinateBox(light.Z);
                var color = CreateColorValue(light.Color);
                Grid.SetColumn(x, 1);
                Grid.SetColumn(y, 2);
                Grid.SetColumn(z, 3);
                row.Children.Add(x);
                row.Children.Add(y);
                row.Children.Add(z);
                var colorEditor = CreateColorEditor(color);
                Grid.SetColumn(colorEditor, 4);
                row.Children.Add(colorEditor);
                row.Margin = new Thickness(0, 0, 0, 6);
                content.Children.Add(row);
                _pointLightInputs.Add((color, x, y, z));
            }

            var reset = DesktopUi.CreateButton("Reset");
            reset.Click += (_, _) => LoadValues(_initialSettings);
            reset.Width = 84;
            reset.Height = 40;
            reset.Margin = new Thickness(0, 0, 8, 0);
            var close = DesktopUi.CreateButton("Close");
            close.Click += (_, _) => Close();
            close.Width = 84;
            close.Height = 40;
            close.Margin = new Thickness(0);
            content.Children.Add(new StackPanel {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0),
                Children = { reset, close },
            });

            DesktopUi.ConfigureWindow(this, content, width: 480, height: 420);
            Title = "Field preview light editor";
            ShowInTaskbar = false;
        }

        private static Grid CreateLightGrid() {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        private static TextBox CreateValueBox(string value) {
            var box = DesktopUi.CreateTextBox(value);
            box.Margin = new Thickness(4, 0, 4, 0);
            box.Height = 34;
            box.Padding = new Thickness(8, 2, 8, 2);
            box.VerticalContentAlignment = VerticalAlignment.Center;
            box.TextAlignment = TextAlignment.Center;
            return box;
        }

        private static TextBox CreateColorValue(int value) => new() { Text = ColorText(value) };

        private TextBox CreateCoordinateBox(int value) {
            var box = CreateValueBox(value.ToString());
            box.Cursor = Cursors.SizeNS;
            box.ToolTip = "Type a value, or drag up/down to adjust. Shift: ×10. Ctrl: ×100.";
            box.TextChanged += (_, _) => UpdatePreview();
            double? startY = null;
            int startValue = value;
            bool scrubbing = false;

            box.PreviewMouseLeftButtonDown += (_, e) => {
                if (!TryParseCoordinate(box.Text, out startValue)) {
                    startY = null;
                    return;
                }
                startY = e.GetPosition(box).Y;
                scrubbing = false;
            };
            box.PreviewMouseMove += (_, e) => {
                if (startY is null || e.LeftButton != MouseButtonState.Pressed)
                    return;
                double delta = startY.Value - e.GetPosition(box).Y;
                if (!scrubbing && Math.Abs(delta) < 3)
                    return;
                if (!scrubbing) {
                    scrubbing = true;
                    box.CaptureMouse();
                }
                int multiplier = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 100
                    : Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
                int adjusted = (int)Math.Clamp(startValue + Math.Round(delta * multiplier), short.MinValue, short.MaxValue);
                box.Text = adjusted.ToString();
                box.CaretIndex = box.Text.Length;
                e.Handled = true;
            };
            box.PreviewMouseLeftButtonUp += (_, e) => {
                if (scrubbing) {
                    scrubbing = false;
                    startY = null;
                    box.ReleaseMouseCapture();
                    e.Handled = true;
                } else {
                    startY = null;
                }
            };
            box.LostMouseCapture += (_, _) => {
                if (!scrubbing)
                    startY = null;
                scrubbing = false;
            };
            return box;
        }

        private FrameworkElement CreateColorEditor(TextBox colorBox) {
            var grid = new Grid { Margin = new Thickness(0, 0, 4, 0) };

            var swatch = new Border {
                BorderBrush = DesktopUi.ControlBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Width = 58,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Hand,
                Child = new Border { Margin = new Thickness(3), CornerRadius = new CornerRadius(2) },
                ToolTip = "Choose color",
            };
            var colorSurface = (Border)swatch.Child;
            void RefreshSwatch() {
                colorSurface.Background = TryParseColor(colorBox.Text, out int parsed)
                    ? new SolidColorBrush(ColorFromRgb(parsed))
                    : DesktopUi.InputBackgroundBrush;
            }
            RefreshSwatch();
            if (TryParseColor(colorBox.Text, out int initialColor))
                _lastValidColors[colorBox] = initialColor;
            colorBox.TextChanged += (_, _) => {
                RefreshSwatch();
                if (TryParseColor(colorBox.Text, out int parsed)) {
                    _lastValidColors[colorBox] = parsed;
                    UpdatePreview();
                }
            };
            swatch.MouseLeftButtonUp += (_, e) => {
                int originalColor = _lastValidColors.TryGetValue(colorBox, out int parsed) ? parsed : 0xFFFFFF;
                var picker = new FieldColorPickerWindow(originalColor, color => {
                    colorBox.Text = ColorText(color);
                    UpdatePreview();
                }) {
                    Owner = this,
                };
                picker.ShowDialog();
                if (!picker.Accepted) {
                    _lastValidColors[colorBox] = originalColor;
                    colorBox.Text = ColorText(originalColor);
                    UpdatePreview();
                }
                e.Handled = true;
            };
            grid.Children.Add(swatch);
            return grid;
        }

        private void LoadValues(FieldPreviewLightingSettings settings) {
            _ambientColor.Text = ColorText(settings.AmbientColor);
            _dynamicLighting.IsChecked = settings.DynamicLighting;
            for (int i = 0; i < _pointLightInputs.Count; i++) {
                FieldPreviewPointLight light = settings.PointLights[i];
                var inputs = _pointLightInputs[i];
                inputs.Color.Text = ColorText(light.Color);
                inputs.X.Text = light.X.ToString();
                inputs.Y.Text = light.Y.ToString();
                inputs.Z.Text = light.Z.ToString();
            }
            UpdatePreview();
        }

        private void UpdatePreview() {
            if (TryReadSettings(out FieldPreviewLightingSettings? settings, out _))
                _onUpdate(settings!);
        }

        private bool TryReadSettings(out FieldPreviewLightingSettings? settings, out string error) {
            settings = null;
            error = string.Empty;
            if (!TryParseColor(_ambientColor.Text, out int ambientColor))
                return Invalid("Global tint color must be six-digit RGB hex, such as 575455.", out error);

            var updated = _initialSettings.Clone();
            updated.AmbientColor = ambientColor;
            updated.DynamicLighting = _dynamicLighting.IsChecked == true;
            updated.PointLights.Clear();
            for (int i = 0; i < _pointLightInputs.Count; i++) {
                var inputs = _pointLightInputs[i];
                if (!TryParseColor(inputs.Color.Text, out int color))
                    return Invalid($"Light {i + 1} color must be six-digit RGB hex, such as 939091.", out error);
                if (!TryParseCoordinate(inputs.X.Text, out int x) ||
                    !TryParseCoordinate(inputs.Y.Text, out int y) ||
                    !TryParseCoordinate(inputs.Z.Text, out int z))
                    return Invalid($"Light {i + 1} XYZ positions must be whole numbers between -32768 and 32767.", out error);
                updated.PointLights.Add(new FieldPreviewPointLight { Color = color, X = x, Y = y, Z = z });
            }

            settings = updated;
            return true;
        }

        private static bool TryParseColor(string text, out int color) {
            string value = text.Trim().TrimStart('#');
            color = 0;
            return value.Length == 6 && int.TryParse(value, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out color);
        }

        private static Color ColorFromRgb(int color) => Color.FromRgb(
            (byte)((color >> 16) & 0xFF), (byte)((color >> 8) & 0xFF), (byte)(color & 0xFF));

        private static bool TryParseCoordinate(string text, out int value) =>
            int.TryParse(text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out value) &&
            value >= short.MinValue && value <= short.MaxValue;

        private static bool Invalid(string message, out string error) {
            error = message;
            return false;
        }

        private static string ColorText(int color) => $"{color & 0xFFFFFF:X6}";
    }

    internal sealed class FieldColorPickerWindow : Window {
        private const double SaturationValueWidth = 252;
        private const double SaturationValueHeight = 190;
        private readonly Grid _saturationValue;
        private readonly Canvas _svMarkerLayer;
        private readonly Ellipse _svMarker;
        private readonly Grid _hueGrid;
        private readonly Canvas _hueMarkerLayer;
        private readonly Rectangle _hueMarker;
        private readonly Border _preview;
        private readonly TextBox _hex;
        private double _hue;
        private double _saturation;
        private double _value;
        private bool _syncing;
        private bool _pickerReady;
        private readonly Action<int> _onColorChanged;

        public int SelectedRgb { get; private set; }
        public bool Accepted { get; private set; }

        public FieldColorPickerWindow(int initialRgb, Action<int> onColorChanged) {
            SelectedRgb = initialRgb & 0xFFFFFF;
            _onColorChanged = onColorChanged;
            (_hue, _saturation, _value) = ToHsv(ColorFromRgb(SelectedRgb));

            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(new TextBlock {
                Text = "Choose a color",
                FontSize = 19,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesktopUi.TextPrimaryBrush,
                Margin = new Thickness(0, 0, 0, 14),
            });

            var pickerRow = new StackPanel { Orientation = Orientation.Horizontal };
            _saturationValue = CreateSaturationValueField();
            _svMarkerLayer = (Canvas)_saturationValue.Children[3];
            _svMarker = (Ellipse)_svMarkerLayer.Children[0];
            pickerRow.Children.Add(_saturationValue);

            _hueGrid = CreateHueField();
            _hueMarkerLayer = (Canvas)_hueGrid.Children[1];
            _hueMarker = (Rectangle)_hueMarkerLayer.Children[0];
            _hueGrid.Margin = new Thickness(12, 0, 0, 0);
            pickerRow.Children.Add(_hueGrid);
            root.Children.Add(pickerRow);

            var tools = new Grid { Margin = new Thickness(0, 16, 0, 8) };
            tools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            tools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            _preview = new Border {
                BorderBrush = DesktopUi.ControlBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Height = 38,
                Margin = new Thickness(0, 0, 10, 0),
            };
            Grid.SetColumn(_preview, 0);
            tools.Children.Add(_preview);
            var hexLabel = DesktopUi.CreateLabel("Hex RGB");
            hexLabel.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(hexLabel, 1);
            tools.Children.Add(hexLabel);
            _hex = DesktopUi.CreateTextBox(ColorText(SelectedRgb));
            _hex.Height = 38;
            _hex.Padding = new Thickness(8, 2, 8, 2);
            _hex.TextAlignment = TextAlignment.Center;
            Grid.SetColumn(_hex, 2);
            tools.Children.Add(_hex);
            root.Children.Add(tools);

            _hex.TextChanged += (_, _) => {
                if (_syncing || !TryParseColor(_hex.Text, out int rgb))
                    return;
                (_hue, _saturation, _value) = ToHsv(ColorFromRgb(rgb));
                SelectedRgb = rgb;
                UpdatePickerVisuals(syncHex: false, exactRgb: rgb);
            };

            var cancel = DesktopUi.CreateButton("Cancel");
            cancel.Click += (_, _) => Close();
            var ok = DesktopUi.CreateButton("OK", primary: true);
            ok.Click += (_, _) => AcceptColor();
            root.Children.Add(DesktopUi.CreateActionRow(cancel, ok));

            DesktopUi.ConfigureWindow(this, root, width: 390, height: 360, scrollContent: false);
            MinWidth = Width;
            MinHeight = Height;
            MaxWidth = Width;
            MaxHeight = Height;
            Title = "Choose color";
            ShowInTaskbar = false;
            UpdatePickerVisuals();
            _pickerReady = true;
        }

        private Grid CreateSaturationValueField() {
            var grid = new Grid {
                Width = SaturationValueWidth,
                Height = SaturationValueHeight,
                Cursor = Cursors.Cross,
                ClipToBounds = true,
            };
            grid.Children.Add(new Border { Background = HueBrush(_hue) });
            grid.Children.Add(new Border {
                Background = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(1, 0)),
            });
            grid.Children.Add(new Border {
                Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, new Point(0, 0), new Point(0, 1)),
            });
            var markerLayer = new Canvas { IsHitTestVisible = false };
            markerLayer.Children.Add(new Ellipse {
                Width = 14,
                Height = 14,
                Stroke = Brushes.White,
                StrokeThickness = 2,
                Fill = Brushes.Transparent,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 2, ShadowDepth = 0 },
            });
            grid.Children.Add(markerLayer);
            grid.PreviewMouseLeftButtonDown += (_, e) => {
                grid.CaptureMouse();
                SetSaturationValue(e.GetPosition(grid));
                e.Handled = true;
            };
            grid.PreviewMouseMove += (_, e) => {
                if (e.LeftButton == MouseButtonState.Pressed && grid.IsMouseCaptured) {
                    SetSaturationValue(e.GetPosition(grid));
                    e.Handled = true;
                }
            };
            grid.PreviewMouseLeftButtonUp += (_, e) => {
                if (grid.IsMouseCaptured)
                    grid.ReleaseMouseCapture();
                e.Handled = true;
            };
            return grid;
        }

        private Grid CreateHueField() {
            var grid = new Grid {
                Width = 24,
                Height = SaturationValueHeight,
                Cursor = Cursors.SizeNS,
                ClipToBounds = false,
            };
            var hue = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            hue.GradientStops.Add(new GradientStop(ColorFromHsv(0, 1, 1), 0));
            hue.GradientStops.Add(new GradientStop(ColorFromHsv(60, 1, 1), 1.0 / 6));
            hue.GradientStops.Add(new GradientStop(ColorFromHsv(120, 1, 1), 2.0 / 6));
            hue.GradientStops.Add(new GradientStop(ColorFromHsv(180, 1, 1), 3.0 / 6));
            hue.GradientStops.Add(new GradientStop(ColorFromHsv(240, 1, 1), 4.0 / 6));
            hue.GradientStops.Add(new GradientStop(ColorFromHsv(300, 1, 1), 5.0 / 6));
            hue.GradientStops.Add(new GradientStop(ColorFromHsv(360, 1, 1), 1));
            grid.Children.Add(new Border { Background = hue, BorderBrush = DesktopUi.ControlBorderBrush, BorderThickness = new Thickness(1) });
            var markerLayer = new Canvas { IsHitTestVisible = false };
            markerLayer.Children.Add(new Rectangle {
                Width = 30,
                Height = 4,
                Fill = Brushes.White,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
            });
            grid.Children.Add(markerLayer);
            grid.PreviewMouseLeftButtonDown += (_, e) => {
                grid.CaptureMouse();
                SetHue(e.GetPosition(grid));
                e.Handled = true;
            };
            grid.PreviewMouseMove += (_, e) => {
                if (e.LeftButton == MouseButtonState.Pressed && grid.IsMouseCaptured) {
                    SetHue(e.GetPosition(grid));
                    e.Handled = true;
                }
            };
            grid.PreviewMouseLeftButtonUp += (_, e) => {
                if (grid.IsMouseCaptured)
                    grid.ReleaseMouseCapture();
                e.Handled = true;
            };
            return grid;
        }

        private void SetSaturationValue(Point point) {
            _saturation = Math.Clamp(point.X / SaturationValueWidth, 0, 1);
            _value = 1 - Math.Clamp(point.Y / SaturationValueHeight, 0, 1);
            UpdatePickerVisuals();
        }

        private void SetHue(Point point) {
            _hue = Math.Clamp(point.Y / SaturationValueHeight, 0, 1) * 360;
            UpdatePickerVisuals();
        }

        private void UpdatePickerVisuals(bool syncHex = true, int? exactRgb = null) {
            Color color = ColorFromHsv(_hue, _saturation, _value);
            int rgb = exactRgb ?? ((color.R << 16) | (color.G << 8) | color.B);
            SelectedRgb = rgb;
            _preview.Background = new SolidColorBrush(ColorFromRgb(rgb));
            ((Border)_saturationValue.Children[0]).Background = HueBrush(_hue);
            Canvas.SetLeft(_svMarker, Math.Clamp(_saturation * SaturationValueWidth - _svMarker.Width / 2, -_svMarker.Width / 2, SaturationValueWidth - _svMarker.Width / 2));
            Canvas.SetTop(_svMarker, Math.Clamp((1 - _value) * SaturationValueHeight - _svMarker.Height / 2, -_svMarker.Height / 2, SaturationValueHeight - _svMarker.Height / 2));
            Canvas.SetTop(_hueMarker, Math.Clamp(_hue / 360 * SaturationValueHeight - _hueMarker.Height / 2, -_hueMarker.Height / 2, SaturationValueHeight - _hueMarker.Height / 2));
            if (syncHex) {
                _syncing = true;
                _hex.Text = ColorText(rgb);
                _syncing = false;
            }
            if (_pickerReady)
                _onColorChanged(rgb);
        }
        private void AcceptColor() {
            if (!TryParseColor(_hex.Text, out int rgb)) {
                DesktopUi.ShowError(this, "Invalid color", "Enter a six-digit RGB hex color, such as 575455.");
                return;
            }
            SelectedRgb = rgb;
            Accepted = true;
            Close();
        }

        private static SolidColorBrush HueBrush(double hue) => new(ColorFromHsv(hue, 1, 1));

        private static (double Hue, double Saturation, double Value) ToHsv(Color color) {
            double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            double hue = 0;
            if (delta > 0) {
                if (max == r) hue = 60 * (((g - b) / delta) % 6);
                else if (max == g) hue = 60 * (((b - r) / delta) + 2);
                else hue = 60 * (((r - g) / delta) + 4);
            }
            if (hue < 0) hue += 360;
            return (hue, max == 0 ? 0 : delta / max, max);
        }

        private static Color ColorFromHsv(double hue, double saturation, double value) {
            hue = ((hue % 360) + 360) % 360;
            double chroma = value * saturation;
            double x = chroma * (1 - Math.Abs((hue / 60 % 2) - 1));
            double m = value - chroma;
            (double r, double g, double b) = hue switch {
                < 60 => (chroma, x, 0.0),
                < 120 => (x, chroma, 0.0),
                < 180 => (0.0, chroma, x),
                < 240 => (0.0, x, chroma),
                < 300 => (x, 0.0, chroma),
                _ => (chroma, 0.0, x),
            };
            return Color.FromRgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
        }

        private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);

        private static Color ColorFromRgb(int rgb) => Color.FromRgb(
            (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

        private static bool TryParseColor(string text, out int color) {
            string value = text.Trim().TrimStart('#');
            color = 0;
            return value.Length == 6 && int.TryParse(value, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out color);
        }

        private static string ColorText(int color) => $"{color & 0xFFFFFF:X6}";
    }
}
