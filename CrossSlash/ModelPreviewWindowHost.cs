// This program and the accompanying materials are made available under the terms of the
// Eclipse Public License v2.0 which accompanies this distribution, and is available at
// https://www.eclipse.org/legal/epl-v20.html
//
// SPDX-License-Identifier: EPL-2.0

using System.Windows;
using System.Windows.Controls;

namespace CrossSlash {
    /// <summary>Maintains a synchronized, resizable copy of a preview in a separate window.</summary>
    internal sealed class ModelPreviewWindowHost {
        private readonly Window _owner;
        private readonly ModelPreviewControl _preview;
        private Grid? _inlineHost;
        private Window? _floatingWindow;
        private ModelPreviewControl? _floatingPreview;
        private Button? _dockPlaceholder;
        private double _inlineHeight;

        public ModelPreviewWindowHost(Window owner, ModelPreviewControl preview) {
            _owner = owner;
            _preview = preview;
            _inlineHeight = preview.Height;
            _preview.SetPopOutAction(PopOut);
        }

        public void AttachTo(Grid host) {
            if (!ReferenceEquals(_inlineHost, host))
                RemoveDockPlaceholder();
            _inlineHost = host;
            if (_floatingWindow != null) {
                ShowDockPlaceholder(host);
                return;
            }

            RemoveDockPlaceholder();
            if (_preview.Parent is Panel oldParent)
                oldParent.Children.Remove(_preview);
            _preview.Height = _inlineHeight;
            _preview.Visibility = Visibility.Visible;
            host.Children.Add(_preview);
        }

        public void PopOut() {
            if (_floatingWindow != null) {
                _floatingWindow.Activate();
                return;
            }
            if (_inlineHost == null)
                return;

            try {
                ShowDockPlaceholder(_inlineHost);
                _preview.Visibility = Visibility.Collapsed;

                var floatingPreview = new ModelPreviewControl { Height = double.NaN };
                _floatingPreview = floatingPreview;
                _preview.ViewerMessageProduced += ForwardViewerMessage;
                floatingPreview.ViewerActionRequested += ForwardViewerAction;
                _preview.CopyViewerStateTo(floatingPreview);

                var floatingHost = new Grid();
                floatingHost.Children.Add(floatingPreview);
                Window? owner = _owner.IsVisible
                    ? _owner
                    : DesktopUi.MainWindow is { IsVisible: true } mainWindow
                        ? mainWindow
                        : null;
                var window = new Window {
                    Title = "CrossSlash Preview",
                    Width = 1120,
                    Height = 850,
                    MinWidth = 700,
                    MinHeight = 500,
                    ResizeMode = ResizeMode.CanResize,
                    WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                };
                if (owner != null)
                    window.Owner = owner;
                DesktopUi.ConfigureWindow(window, floatingHost, 1120, 850, scrollContent: false);
                window.MinWidth = 700;
                window.MinHeight = 500;
                window.ResizeMode = ResizeMode.CanResize;
                _floatingWindow = window;
                window.Closed += (_, _) => FloatingWindowClosed(window);
                window.Show();
            } catch (Exception ex) {
                if (_floatingWindow is { } window && window.IsVisible)
                    window.Close();
                _floatingWindow = null;
                DisconnectFloatingPreview();
                RemoveDockPlaceholder();
                _preview.Visibility = Visibility.Visible;
                DesktopUi.ShowError(_owner, "Could not open preview window", ex.Message);
            }
        }

        public void Close() {
            if (_floatingWindow is { } window) {
                _floatingWindow = null;
                DisconnectFloatingPreview();
                RemoveDockPlaceholder();
                window.Close();
            }
        }

        private void ForwardViewerMessage(string message) => _floatingPreview?.ReceiveViewerMessage(message);
        private void ForwardViewerAction(string action, string? value) => _preview.ReceiveViewerAction(action, value);

        private void ShowDockPlaceholder(Grid host) {
            if (_dockPlaceholder == null) {
                _dockPlaceholder = DesktopUi.CreateButton("Return preview");
                _dockPlaceholder.HorizontalAlignment = HorizontalAlignment.Center;
                _dockPlaceholder.VerticalAlignment = VerticalAlignment.Center;
                _dockPlaceholder.Click += (_, _) => Dock();
            } else if (_dockPlaceholder.Parent is Panel oldParent && !ReferenceEquals(oldParent, host)) {
                oldParent.Children.Remove(_dockPlaceholder);
            }
            if (!host.Children.Contains(_dockPlaceholder))
                host.Children.Add(_dockPlaceholder);
        }

        private void RemoveDockPlaceholder() {
            if (_dockPlaceholder?.Parent is Panel parent)
                parent.Children.Remove(_dockPlaceholder);
        }

        private void Dock() {
            Window? window = _floatingWindow;
            if (window == null)
                return;
            _floatingWindow = null;
            DisconnectFloatingPreview();
            RemoveDockPlaceholder();
            _preview.Visibility = Visibility.Visible;
            if (window.IsVisible)
                window.Close();
        }

        private void FloatingWindowClosed(Window window) {
            if (!ReferenceEquals(_floatingWindow, window))
                return;
            _floatingWindow = null;
            DisconnectFloatingPreview();
            RemoveDockPlaceholder();
            _preview.Visibility = Visibility.Visible;
        }

        private void DisconnectFloatingPreview() {
            _preview.ViewerMessageProduced -= ForwardViewerMessage;
            if (_floatingPreview != null)
                _floatingPreview.ViewerActionRequested -= ForwardViewerAction;
            _floatingPreview = null;
        }
    }
}
