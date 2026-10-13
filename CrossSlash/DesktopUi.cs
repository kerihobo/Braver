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

        private static readonly Brush PageBackground = Brush("#F3F6FA");
        private static readonly Brush HeaderBackground = Brush("#172B4D");
        private static readonly Brush Accent = Brush("#2563EB");
        private static readonly Brush AccentHover = Brush("#1D4ED8");
        private static readonly Brush TextPrimary = Brush("#172B4D");
        private static readonly Brush TextSecondary = Brush("#64748B");
        private static readonly Brush ControlBorder = Brush("#DCE3ED");

        private static Brush Brush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);

        public static void ConfigureWindow(
            Window window,
            string title,
            string subtitle,
            UIElement content,
            UIElement? headerAction = null,
            double width = 1040,
            double height = 760,
            bool scrollContent = true) {
            window.Title = title;
            window.Width = width;
            window.Height = height;
            window.MinWidth = Math.Min(width, 760);
            window.MinHeight = Math.Min(height, 560);
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.Background = PageBackground;
            window.FontFamily = new FontFamily("Segoe UI");
            window.FontSize = 14;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Border {
                Background = HeaderBackground,
                Padding = new Thickness(24, 20, 24, 20),
            };
            var headerLayout = new DockPanel { LastChildFill = true };
            if (headerAction != null) {
                DockPanel.SetDock(headerAction, Dock.Right);
                headerLayout.Children.Add(headerAction);
            }

            var heading = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            heading.Children.Add(new TextBlock {
                Text = title,
                FontSize = 26,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
            });
            heading.Children.Add(new TextBlock {
                Text = subtitle,
                FontSize = 13,
                Foreground = Brush("#C5D1E2"),
                Margin = new Thickness(0, 4, 0, 0),
            });
            headerLayout.Children.Add(heading);
            header.Child = headerLayout;
            root.Children.Add(header);

            UIElement body = scrollContent
                ? new ScrollViewer {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Padding = new Thickness(24),
                    Content = CreateCard(content),
                }
                : CreateCard(content);
            Grid.SetRow(body, 1);
            root.Children.Add(body);
            window.Content = root;
        }

        public static Border CreateCard(UIElement content) => new() {
            Background = Brushes.White,
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
                Background = primary ? Accent : Brushes.White,
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
            hover.Setters.Add(new Setter(Border.BackgroundProperty, primary ? AccentHover : Brush("#F3F6FA"), "buttonBorder"));
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
            Background = readOnly ? Brush("#F8FAFC") : Brushes.White,
            Foreground = TextPrimary,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 14,
        };

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
