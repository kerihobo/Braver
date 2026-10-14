// This program and the accompanying materials are made available under the terms of the
// Eclipse Public License v2.0 which accompanies this distribution, and is available at
// https://www.eclipse.org/legal/epl-v20.html
//
// SPDX-License-Identifier: EPL-2.0

using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SharpGLTF.Schema2;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace CrossSlash {
    internal sealed class ModelPreviewControl : UserControl {
        private readonly WebView2 _webView;
        private readonly TextBlock _status;
        private string? _pendingMessage;
        private bool _initializationStarted;
        private bool _pageReady;

        public ModelPreviewControl() {
            _webView = new WebView2 {
                Visibility = Visibility.Collapsed,
            };
            _webView.CoreWebView2InitializationCompleted += WebViewInitializationCompleted;

            _status = new TextBlock {
                Text = "Choose a model to preview",
                Foreground = DesktopUi.TextSecondaryBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(18),
            };

            var content = new Grid {
                Background = DesktopUi.InputBackgroundBrush,
                ClipToBounds = true,
            };
            content.Children.Add(_webView);
            content.Children.Add(_status);

            Content = new Border {
                Background = DesktopUi.InputBackgroundBrush,
                BorderBrush = DesktopUi.ControlBorderBrush,
                BorderThickness = new Thickness(1),
                Child = content,
            };
            MinHeight = 280;
            IsVisibleChanged += PreviewVisibilityChanged;
            Loaded += InitializeWebView;
        }

        public void ShowModel(ModelRoot model, string name) {
            try {
                byte[] glb = model.WriteGLB().ToArray();
                _pendingMessage = JsonSerializer.Serialize(new {
                    type = "load-model",
                    name,
                    glbBase64 = Convert.ToBase64String(glb),
                });
                _status.Text = "Preparing model preview…";
                _status.Visibility = Visibility.Visible;
                SendPendingMessage();
            } catch (Exception ex) {
                ShowError($"Could not prepare the preview: {ex.Message}");
            }
        }

        public void ShowMessage(string message) {
            _pendingMessage = JsonSerializer.Serialize(new { type = "clear", message });
            _status.Text = message;
            _status.Visibility = Visibility.Visible;
            SendPendingMessage();
        }

        private async void InitializeWebView(object sender, RoutedEventArgs e) {
            if (_initializationStarted)
                return;
            _initializationStarted = true;
            Loaded -= InitializeWebView;
            _status.Text = "Starting 3D preview…";

            try {
                await _webView.EnsureCoreWebView2Async();
                string viewerDirectory = Path.Combine(AppContext.BaseDirectory, "PreviewViewer");
                string entryPoint = Path.Combine(viewerDirectory, "index.html");
                if (!File.Exists(entryPoint))
                    throw new FileNotFoundException("The bundled preview files are missing.", entryPoint);

                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "crossslash.local",
                    viewerDirectory,
                    CoreWebView2HostResourceAccessKind.DenyCors
                );
                _webView.CoreWebView2.WebMessageReceived += WebMessageReceived;
                _webView.CoreWebView2.Navigate("https://crossslash.local/index.html");
                _webView.Visibility = Visibility.Visible;
            } catch (Exception ex) {
                ShowError($"The 3D preview could not start. Check that the Microsoft Edge WebView2 Runtime is installed.\n{ex.Message}");
            }
        }

        private void WebViewInitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e) {
            if (!e.IsSuccess)
                ShowError($"The 3D preview could not start. Check that the Microsoft Edge WebView2 Runtime is installed.\n{e.InitializationException?.Message}");
        }

        private void WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e) {
            try {
                using JsonDocument document = JsonDocument.Parse(e.WebMessageAsJson);
                JsonElement root = document.RootElement;
                string? type = root.TryGetProperty("type", out JsonElement typeElement) ? typeElement.GetString() : null;
                if (type == "viewer-ready") {
                    _pageReady = true;
                    _status.Visibility = Visibility.Collapsed;
                    SendPendingMessage();
                    _webView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new {
                        type = "visibility",
                        visible = IsVisible,
                    }));
                } else if (type == "preview-loaded") {
                    _status.Visibility = Visibility.Collapsed;
                } else if (type == "preview-error") {
                    string detail = root.TryGetProperty("message", out JsonElement messageElement)
                        ? messageElement.GetString() ?? "Unknown viewer error."
                        : "Unknown viewer error.";
                    ShowError($"Preview failed: {detail}");
                }
            } catch (JsonException) {
                // Ignore malformed messages from the preview page.
            }
        }

        private void PreviewVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) {
            if (_pageReady && _webView.CoreWebView2 != null)
                _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new {
                    type = "visibility",
                    visible = IsVisible,
                }));
        }

        private void SendPendingMessage() {
            if (!_pageReady || _pendingMessage == null || _webView.CoreWebView2 == null)
                return;
            _webView.CoreWebView2.PostWebMessageAsJson(_pendingMessage);
            _pendingMessage = null;
        }

        private void ShowError(string message) {
            _pendingMessage = JsonSerializer.Serialize(new { type = "clear", message });
            _status.Text = message;
            _status.Visibility = Visibility.Visible;
            SendPendingMessage();
        }
    }
}
