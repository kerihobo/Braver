// This program and the accompanying materials are made available under the terms of the
// Eclipse Public License v2.0 which accompanies this distribution, and is available at
// https://www.eclipse.org/legal/epl-v20.html
//
// SPDX-License-Identifier: EPL-2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace CrossSlash {
    internal static class DesktopUi {
        public static Window? MainWindow { get; set; }

        private static readonly Brush PageBackground = Brush("#202A35");
        private static readonly Brush Surface = Brush("#293643");
        private static readonly Brush SurfaceRaised = Brush("#344352");
        private static readonly Brush InputBackground = Brush("#222D38");
        private static readonly Brush Accent = Brush("#3478C4");
        private static readonly Brush AccentHover = Brush("#2D6DAF");
        private static readonly Brush TextPrimary = Brush("#E7EDF3");
        private static readonly Brush TextSecondary = Brush("#B3C0CC");
        private static readonly Brush ControlBorder = Brush("#506171");

        public static Brush SurfaceBrush => Surface;
        public static Brush InputBackgroundBrush => InputBackground;
        public static Brush ControlBorderBrush => ControlBorder;
        public static Brush TextPrimaryBrush => TextPrimary;
        public static Brush TextSecondaryBrush => TextSecondary;

        private static Brush Brush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);

        public static void ConfigureWindow(
            Window window,
            UIElement content,
            double width = 1040,
            double height = 760,
            bool scrollContent = true) {
            window.Title = string.Empty;
            window.Width = width;
            window.Height = height;
            window.MinWidth = Math.Min(width, 760);
            window.MinHeight = Math.Min(height, 560);
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.Background = PageBackground;
            window.Foreground = TextPrimary;
            window.FontFamily = new FontFamily("Segoe UI");
            window.FontSize = 14;
            ApplyThemeResources(window);

            UIElement body = scrollContent
                ? new ScrollViewer {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Padding = new Thickness(24),
                    Content = CreateCard(content),
                }
                : content;
            window.Content = body;
        }

        private static void ApplyThemeResources(Window window) {
            window.Resources[SystemColors.WindowBrushKey] = Surface;
            window.Resources[SystemColors.WindowTextBrushKey] = TextPrimary;
            window.Resources[SystemColors.ControlBrushKey] = SurfaceRaised;
            window.Resources[SystemColors.ControlLightBrushKey] = SurfaceRaised;
            window.Resources[SystemColors.ControlTextBrushKey] = TextPrimary;
            window.Resources[SystemColors.HighlightBrushKey] = Accent;
            window.Resources[SystemColors.HighlightTextBrushKey] = Brushes.White;
            window.Resources[SystemColors.GrayTextBrushKey] = TextSecondary;
            window.Resources[SystemColors.MenuBrushKey] = Surface;
            window.Resources[SystemColors.MenuTextBrushKey] = TextPrimary;
            window.Resources[typeof(CheckBox)] = CreateCheckBoxStyle();
            window.Resources[typeof(ListBox)] = CreateListBoxStyle();
            window.Resources[typeof(ListBoxItem)] = CreateListBoxItemStyle();
            window.Resources[typeof(ComboBox)] = CreateComboBoxStyle();
            window.Resources[typeof(ComboBoxItem)] = CreateComboBoxItemStyle();
            window.Resources[typeof(TabControl)] = CreateTabControlStyle();
            window.Resources[typeof(TabItem)] = CreateTabItemStyle();
        }

        private static Style CreateCheckBoxStyle() {
            var checkBorder = new FrameworkElementFactory(typeof(Border), "checkBorder");
            checkBorder.SetValue(FrameworkElement.WidthProperty, 16d);
            checkBorder.SetValue(FrameworkElement.HeightProperty, 16d);
            checkBorder.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            checkBorder.SetValue(Border.BackgroundProperty, InputBackground);
            checkBorder.SetValue(Border.BorderBrushProperty, ControlBorder);
            checkBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            checkBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));

            var mark = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path), "checkMark");
            mark.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 2,8 L 6,12 L 14,3"));
            mark.SetValue(System.Windows.Shapes.Path.StrokeProperty, Brushes.White);
            mark.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 2d);
            mark.SetValue(System.Windows.Shapes.Path.StrokeStartLineCapProperty, PenLineCap.Round);
            mark.SetValue(System.Windows.Shapes.Path.StrokeEndLineCapProperty, PenLineCap.Round);
            mark.SetValue(FrameworkElement.WidthProperty, 16d);
            mark.SetValue(FrameworkElement.HeightProperty, 16d);
            mark.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            checkBorder.AppendChild(mark);

            var root = new FrameworkElementFactory(typeof(StackPanel));
            root.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            root.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            root.AppendChild(checkBorder);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            content.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(ContentControl.Content)) { RelativeSource = RelativeSource.TemplatedParent });
            content.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding(nameof(ContentControl.ContentTemplate)) { RelativeSource = RelativeSource.TemplatedParent });
            root.AppendChild(content);

            var template = new ControlTemplate(typeof(CheckBox)) { VisualTree = root };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "checkBorder"));
            template.Triggers.Add(hover);
            var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Accent, "checkBorder"));
            checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "checkBorder"));
            checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "checkMark"));
            template.Triggers.Add(checkedTrigger);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.55));
            template.Triggers.Add(disabled);

            var style = new Style(typeof(CheckBox));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            return style;
        }

        private static Style CreateListBoxStyle() {
            var style = new Style(typeof(ListBox));
            style.Setters.Add(new Setter(Control.BackgroundProperty, InputBackground));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, ControlBorder));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            return style;
        }

        private static Style CreateListBoxItemStyle() {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 3, 6, 3)));
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceRaised));
            style.Triggers.Add(hover);
            var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, Accent));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(selected);
            return style;
        }

        private static Style CreateComboBoxStyle() {
            var style = new Style(typeof(ComboBox));
            style.Setters.Add(new Setter(Control.BackgroundProperty, InputBackground));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, ControlBorder));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            return style;
        }

        private static Style CreateComboBoxItemStyle() {
            var style = new Style(typeof(ComboBoxItem));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Surface));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
            var highlighted = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            highlighted.Setters.Add(new Setter(Control.BackgroundProperty, Accent));
            highlighted.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(highlighted);
            return style;
        }

        private static Style CreateTabControlStyle() {
            var style = new Style(typeof(TabControl));
            style.Setters.Add(new Setter(Control.BackgroundProperty, PageBackground));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, ControlBorder));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            return style;
        }

        private static Style CreateTabItemStyle() {
            var border = new FrameworkElementFactory(typeof(Border), "tabBorder");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7, 7, 0, 0));
            border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background)) { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new Binding(nameof(Control.BorderBrush)) { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new Binding(nameof(Control.BorderThickness)) { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.PaddingProperty, new Binding(nameof(Control.Padding)) { RelativeSource = RelativeSource.TemplatedParent });
            var header = new FrameworkElementFactory(typeof(ContentPresenter));
            header.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            header.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            header.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(HeaderedContentControl.Header)) { RelativeSource = RelativeSource.TemplatedParent });
            header.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding(nameof(HeaderedContentControl.HeaderTemplate)) { RelativeSource = RelativeSource.TemplatedParent });
            border.AppendChild(header);

            var template = new ControlTemplate(typeof(TabItem)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, SurfaceRaised, "tabBorder"));
            template.Triggers.Add(hover);
            var selected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, Surface));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            selected.Setters.Add(new Setter(Control.BorderBrushProperty, Accent));
            template.Triggers.Add(selected);

            var style = new Style(typeof(TabItem));
            style.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceRaised));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextSecondary));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, ControlBorder));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 8, 14, 8)));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 5, 0)));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            var selectedStyle = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
            selectedStyle.Setters.Add(new Setter(Control.BackgroundProperty, Surface));
            selectedStyle.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            selectedStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Accent));
            style.Triggers.Add(selectedStyle);
            return style;
        }

        public static Border CreateCard(UIElement content) => new() {
            Background = Surface,
            BorderBrush = ControlBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(22),
            Child = content,
        };

        public static Button CreateButton(string text, bool primary = false) {
            var button = new Button {
                Content = text,
                Padding = new Thickness(16, 9, 16, 9),
                Margin = new Thickness(0, 0, 10, 0),
                FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = primary ? Accent : SurfaceRaised,
                Foreground = primary ? Brushes.White : TextPrimary,
                BorderBrush = primary ? Accent : ControlBorder,
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            var border = new FrameworkElementFactory(typeof(Border), "buttonBorder");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background)) { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new Binding(nameof(Control.BorderBrush)) { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new Binding(nameof(Control.BorderThickness)) { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.PaddingProperty, new Binding(nameof(Control.Padding)) { RelativeSource = RelativeSource.TemplatedParent });
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(ContentControl.Content)) { RelativeSource = RelativeSource.TemplatedParent });
            border.AppendChild(presenter);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, primary ? AccentHover : Brush("#415263"), "buttonBorder"));
            template.Triggers.Add(hover);
            button.Template = template;
            return button;
        }

        public static TextBox CreateTextBox(string? text = null, bool readOnly = false) => new() {
            Text = text ?? string.Empty,
            IsReadOnly = readOnly,
            Padding = new Thickness(10, 8, 10, 8),
            BorderBrush = ControlBorder,
            BorderThickness = new Thickness(1),
            Background = readOnly ? Surface : InputBackground,
            Foreground = TextPrimary,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 14,
        };

        public static TextBox CreateSearchTextBox(string toolTip) => new() {
            ToolTip = toolTip,
            Height = 30,
            Padding = new Thickness(8, 3, 8, 3),
            BorderBrush = ControlBorder,
            BorderThickness = new Thickness(1),
            Background = InputBackground,
            Foreground = TextPrimary,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 13,
        };

        public static UIElement CreateSearchField(string placeholderText, TextBox textBox) {
            var placeholder = new TextBlock {
                Text = placeholderText,
                Foreground = TextSecondary,
                Margin = new Thickness(textBox.Padding.Left + textBox.BorderThickness.Left, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            var placeholderStyle = new Style(typeof(TextBlock));
            placeholderStyle.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
            var emptyText = new DataTrigger {
                Binding = new Binding(nameof(TextBox.Text)) { Source = textBox },
                Value = string.Empty,
            };
            emptyText.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));
            placeholderStyle.Triggers.Add(emptyText);
            placeholder.Style = placeholderStyle;

            var field = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            field.Children.Add(textBox);
            field.Children.Add(placeholder);
            return field;
        }

        public static TextBlock CreateSectionTitle(string text) => new() {
            Text = text,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
            Margin = new Thickness(0, 0, 0, 12),
        };

        public static TextBlock CreateLabel(string text) => new() {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0),
        };

        public static Grid CreateFormRow(string label, UIElement control, double labelWidth = 170) {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(CreateLabel(label));
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            return grid;
        }

        public static StackPanel CreateActionRow(params Button[] buttons) {
            var grid = new UniformGrid { Columns = buttons.Length, Margin = new Thickness(0, 8, 0, 12) };
            foreach (var button in buttons) {
                button.Margin = new Thickness(0, 0, 10, 0);
                grid.Children.Add(button);
            }
            return new StackPanel { Children = { grid } };
        }

        public static TextBlock CreateHint(string text) => new() {
            Text = text,
            Foreground = TextSecondary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 12),
        };

        public static Window ResolveOwner(Window requestedOwner) => MainWindow ?? requestedOwner;

        public static void ShowInfo(Window owner, string title, string message) =>
            MessageBox.Show(ResolveOwner(owner), message, title, MessageBoxButton.OK, MessageBoxImage.Information);

        public static void ShowError(Window owner, string title, string message) =>
            MessageBox.Show(ResolveOwner(owner), message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
