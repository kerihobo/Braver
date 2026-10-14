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
        private readonly SemaphoreSlim _buildGate = new(1, 1);
        private string? _pendingMessage;
        private string? _pendingLightingMessage;
        private bool _initializationStarted;
        private bool _pageReady;
        private bool _viewerPreparationPending;
        private long _modelRequest;

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

        public void ShowModel(Func<ModelRoot> buildModel, string name, object? lighting = null, float cameraZoom = 1.2f) {
            long request = Interlocked.Increment(ref _modelRequest);
            _pendingMessage = null;
            _status.Text = $"Loading {name} preview…";
            _status.Visibility = Visibility.Visible;
            RequestViewerPreparation();
            _ = PrepareModelPreviewAsync(buildModel, name, lighting, cameraZoom, request);
        }

        public void PrepareViewer() => RequestViewerPreparation();

        public void UpdateLighting(object? lighting) {
            _pendingLightingMessage = JsonSerializer.Serialize(new {
                type = "update-lighting",
                lighting,
            });
            SendPendingMessage();
        }

        private async Task PrepareModelPreviewAsync(Func<ModelRoot> buildModel, string name, object? lighting, float cameraZoom, long request) {
            await _buildGate.WaitAsync();
            try {
                if (request != Interlocked.Read(ref _modelRequest))
                    return;

                string glbBase64 = await Task.Run(() => {
                    ModelRoot model = buildModel();
                    return Convert.ToBase64String(model.WriteGLB().ToArray());
                });
                if (request != Interlocked.Read(ref _modelRequest))
                    return;

                _pendingMessage = JsonSerializer.Serialize(new {
                    type = "load-model",
                    name,
                    glbBase64,
                    lighting,
                    cameraZoom,
                });
                SendPendingMessage();
            } catch (Exception ex) {
                if (request == Interlocked.Read(ref _modelRequest))
                    ShowError($"Could not prepare the preview: {ex.Message}");
            } finally {
                _buildGate.Release();
            }
        }

        public void ShowMessage(string message) {
            Interlocked.Increment(ref _modelRequest);
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
                    SendViewerPreparation();
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
            {
                SendPendingLightingMessage();
                return;
            }
            _webView.CoreWebView2.PostWebMessageAsJson(_pendingMessage);
            _pendingMessage = null;
            SendPendingLightingMessage();
        }

        private void SendPendingLightingMessage() {
            if (!_pageReady || _pendingLightingMessage == null || _webView.CoreWebView2 == null)
                return;
            _webView.CoreWebView2.PostWebMessageAsJson(_pendingLightingMessage);
            _pendingLightingMessage = null;
        }

        private void RequestViewerPreparation() {
            _viewerPreparationPending = true;
            SendViewerPreparation();
        }

        private void SendViewerPreparation() {
            if (!_viewerPreparationPending || !_pageReady || _webView.CoreWebView2 == null)
                return;
            _viewerPreparationPending = false;
            _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "prepare-viewer" }));
        }

        private void ShowError(string message) {
            Interlocked.Increment(ref _modelRequest);
            _pendingMessage = JsonSerializer.Serialize(new { type = "clear", message });
            _status.Text = message;
            _status.Visibility = Visibility.Visible;
            SendPendingMessage();
        }
    }
}
