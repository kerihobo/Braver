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
        private string? _pendingAnimationMessage;
        private string? _pendingLightingMessage;
        private string? _pendingFieldLightingMessage;
        private string? _pendingFieldLocationWireframeMessage;
        private string? _lastViewerMessage;
        private string? _lastAnimationMessage;
        private string? _lastLightingMessage;
        private string? _lastFieldLightingMessage;
        private bool? _fieldLocationWireframeVisible;
        private bool _fieldLocationWireframeToggleVisible;
        private bool _terrainViewModeControlVisible;
        private string _terrainViewMode = "textured";
        private int _terrainMapId = -1;
        private Action? _editLightsAction;
        private bool _initializationStarted;
        private bool _pageReady;
        private bool _viewerPreparationPending;
        private long _modelRequest;

        public event Action<string>? ViewerMessageProduced;
        public event Action<string, string?>? ViewerActionRequested;

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

        public void ShowModel(Func<ModelRoot> buildModel, string name, object? lighting = null, float cameraZoom = 1.2f, string cameraMode = "orbit", bool hideControls = false, Func<object?>? getPreviewData = null, float flyMovementSpeedScale = 1.0f, bool preserveCamera = false) {
            long request = Interlocked.Increment(ref _modelRequest);
            Interlocked.Increment(ref _animationRequest);
            _lastAnimationMessage = null;
            _pendingAnimationMessage = null;
            _pendingMessage = null;
            _status.Text = $"Loading {name} preview…";
            _status.Visibility = Visibility.Visible;
            RequestViewerPreparation();
            _ = PrepareModelPreviewAsync(buildModel, name, lighting, cameraZoom, cameraMode, hideControls, getPreviewData, flyMovementSpeedScale, preserveCamera, request);
        }

        private long _animationRequest;

        public void ShowAnimation(Func<ModelRoot> buildModel, string name) {
            long modelRequest = Interlocked.Read(ref _modelRequest);
            long animationRequest = Interlocked.Increment(ref _animationRequest);
            _ = PrepareAnimationPreviewAsync(buildModel, name, modelRequest, animationRequest);
        }

        public void PrepareViewer() => RequestViewerPreparation();

        public void SetPopOutAction(Action? action) {
            _popOutAction = action;
            SendPopOutVisibility();
        }

        private Action? _popOutAction;

        public void CopyViewerStateTo(ModelPreviewControl target) {
            target.SetEditLightsAction(_editLightsAction);
            if (_lastViewerMessage != null)
                target.ReceiveViewerMessage(_lastViewerMessage);
            if (_lastFieldLightingMessage != null)
                target.ReceiveViewerMessage(_lastFieldLightingMessage);
            if (_lastLightingMessage != null)
                target.ReceiveViewerMessage(_lastLightingMessage);
            if (_lastAnimationMessage != null)
                target.ReceiveViewerMessage(_lastAnimationMessage);
            if (_fieldLocationWireframeVisible.HasValue)
                target.SetFieldLocationWireframe(_fieldLocationWireframeVisible.Value);
            target.SetFieldLocationWireframeToggleVisible(_fieldLocationWireframeToggleVisible);
            target.SetTerrainViewModeControl(_terrainViewModeControlVisible, _terrainViewMode, _terrainMapId);
        }

        public void SetEditLightsAction(Action? action) {
            _editLightsAction = action;
            if (_pageReady && _webView.CoreWebView2 != null)
                _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new {
                    type = "edit-lights-visibility",
                    visible = action != null,
                }));
        }

        public void SetFieldLightingOptions(object options) {
            string message = JsonSerializer.Serialize(new { type = "field-lighting-options", options });
            _lastFieldLightingMessage = message;
            _pendingFieldLightingMessage = message;
            ViewerMessageProduced?.Invoke(message);
            SendPendingFieldLightingMessage();
        }

        public void SetFieldLocationWireframe(bool visible) {
            _fieldLocationWireframeVisible = visible;
            string message = JsonSerializer.Serialize(new {
                type = "field-location-wireframe",
                visible,
            });
            _pendingFieldLocationWireframeMessage = message;
            ViewerMessageProduced?.Invoke(message);
            SendPendingFieldLocationWireframeMessage();
        }

        public void SetFieldLocationWireframeToggleVisible(bool visible) {
            _fieldLocationWireframeToggleVisible = visible;
            SendFieldLocationWireframeToggleVisibility();
        }

        public void SetTerrainViewModeControl(bool visible, string mode, int mapId = -1) {
            _terrainViewModeControlVisible = visible;
            _terrainViewMode = mode;
            _terrainMapId = mapId;
            SendTerrainViewModeControl();
        }

        public void ReceiveViewerAction(string action, string? value) => ViewerActionRequested?.Invoke(action, value);

        public void ReceiveViewerMessage(string message) {
            using JsonDocument document = JsonDocument.Parse(message);
            string? type = document.RootElement.TryGetProperty("type", out JsonElement typeElement)
                ? typeElement.GetString()
                : null;
            if (type == "update-lighting") {
                _lastLightingMessage = message;
                _pendingLightingMessage = message;
                SendPendingLightingMessage();
            } else if (type == "field-lighting-options") {
                _lastFieldLightingMessage = message;
                _pendingFieldLightingMessage = message;
                SendPendingFieldLightingMessage();
            } else if (type == "field-location-wireframe") {
                bool visible = document.RootElement.TryGetProperty("visible", out JsonElement visibleElement) && visibleElement.GetBoolean();
                SetFieldLocationWireframe(visible);
            } else if (type == "load-animation") {
                _lastAnimationMessage = message;
                _pendingAnimationMessage = message;
                SendPendingAnimationMessage();
            } else {
                _lastViewerMessage = message;
                _pendingMessage = message;
                SendPendingMessage();
            }
        }

        public void UpdateLighting(object? lighting) {
            string message = JsonSerializer.Serialize(new {
                type = "update-lighting",
                lighting,
            });
            _lastLightingMessage = message;
            _pendingLightingMessage = message;
            ViewerMessageProduced?.Invoke(message);
            SendPendingLightingMessage();
        }

        private async Task PrepareModelPreviewAsync(Func<ModelRoot> buildModel, string name, object? lighting, float cameraZoom, string cameraMode, bool hideControls, Func<object?>? getPreviewData, float flyMovementSpeedScale, bool preserveCamera, long request) {
            await _buildGate.WaitAsync();
            try {
                if (request != Interlocked.Read(ref _modelRequest))
                    return;

                var preview = await Task.Run(() => {
                    ModelRoot model = buildModel();
                    string glbBase64 = Convert.ToBase64String(model.WriteGLB().ToArray());
                    return (glbBase64, previewData: getPreviewData?.Invoke());
                });
                if (request != Interlocked.Read(ref _modelRequest))
                    return;

                string message = JsonSerializer.Serialize(new {
                    type = "load-model",
                    name,
                    glbBase64 = preview.glbBase64,
                    lighting,
                    cameraZoom,
                    cameraMode,
                    hideControls,
                    flyMovementSpeedScale,
                    preserveCamera,
                    previewData = preview.previewData,
                });
                QueueViewerMessage(message);
            } catch (Exception ex) {
                if (request == Interlocked.Read(ref _modelRequest))
                    ShowError($"Could not prepare the preview: {ex.Message}");
            } finally {
                _buildGate.Release();
            }
        }

        private async Task PrepareAnimationPreviewAsync(Func<ModelRoot> buildModel, string name, long modelRequest, long animationRequest) {
            await _buildGate.WaitAsync();
            try {
                if (modelRequest != Interlocked.Read(ref _modelRequest) || animationRequest != Interlocked.Read(ref _animationRequest))
                    return;

                string glbBase64 = await Task.Run(() => {
                    ModelRoot model = buildModel();
                    return Convert.ToBase64String(model.WriteGLB().ToArray());
                });
                if (modelRequest != Interlocked.Read(ref _modelRequest) || animationRequest != Interlocked.Read(ref _animationRequest))
                    return;

                string message = JsonSerializer.Serialize(new {
                    type = "load-animation",
                    name,
                    glbBase64,
                });
                _lastAnimationMessage = message;
                _pendingAnimationMessage = message;
                ViewerMessageProduced?.Invoke(message);
                SendPendingAnimationMessage();
            } catch (Exception ex) {
                if (modelRequest == Interlocked.Read(ref _modelRequest) && animationRequest == Interlocked.Read(ref _animationRequest))
                    ShowError($"Could not prepare the animation preview: {ex.Message}");
            } finally {
                _buildGate.Release();
            }
        }

        public void ShowMessage(string message) {
            Interlocked.Increment(ref _modelRequest);
            Interlocked.Increment(ref _animationRequest);
            _lastAnimationMessage = null;
            _pendingAnimationMessage = null;
            QueueViewerMessage(JsonSerializer.Serialize(new { type = "clear", message }));
            _status.Text = message;
            _status.Visibility = Visibility.Visible;
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
                    SendEditLightsVisibility();
                    SendPopOutVisibility();
                    SendFieldLocationWireframeToggleVisibility();
                    SendTerrainViewModeControl();
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
                } else if (type == "edit-lights") {
                    _editLightsAction?.Invoke();
                } else if (type == "pop-out") {
                    Dispatcher.BeginInvoke(new Action(() => _popOutAction?.Invoke()));
                } else if (type == "field-location-wireframe") {
                    bool visible = root.TryGetProperty("visible", out JsonElement visibleElement) && visibleElement.GetBoolean();
                    SetFieldLocationWireframe(visible);
                } else if (type == "field-scene-selected") {
                    ViewerActionRequested?.Invoke("field-scene", root.TryGetProperty("fieldId", out JsonElement fieldIdElement) ? fieldIdElement.GetString() : null);
                } else if (type == "lighting-model-selected") {
                    ViewerActionRequested?.Invoke("lighting-model", root.TryGetProperty("index", out JsonElement indexElement) ? indexElement.GetString() : null);
                } else if (type == "terrain-view-mode-selected") {
                    ViewerActionRequested?.Invoke("terrain-view-mode", root.TryGetProperty("mode", out JsonElement modeElement) ? modeElement.GetString() : null);
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
                SendPendingFieldLightingMessage();
                SendPendingAnimationMessage();
                SendPendingFieldLocationWireframeMessage();
                return;
            }
            _webView.CoreWebView2.PostWebMessageAsJson(_pendingMessage);
            _pendingMessage = null;
            SendPendingLightingMessage();
            SendPendingFieldLightingMessage();
            SendPendingAnimationMessage();
            SendPendingFieldLocationWireframeMessage();
        }

        private void SendPendingAnimationMessage() {
            if (!_pageReady || _pendingAnimationMessage == null || _webView.CoreWebView2 == null)
                return;
            _webView.CoreWebView2.PostWebMessageAsJson(_pendingAnimationMessage);
            _pendingAnimationMessage = null;
        }

        private void SendPendingLightingMessage() {
            if (!_pageReady || _pendingLightingMessage == null || _webView.CoreWebView2 == null)
                return;
            _webView.CoreWebView2.PostWebMessageAsJson(_pendingLightingMessage);
            _pendingLightingMessage = null;
        }

        private void SendPendingFieldLightingMessage() {
            if (!_pageReady || _pendingFieldLightingMessage == null || _webView.CoreWebView2 == null)
                return;
            _webView.CoreWebView2.PostWebMessageAsJson(_pendingFieldLightingMessage);
            _pendingFieldLightingMessage = null;
        }

        private void SendPendingFieldLocationWireframeMessage() {
            if (!_pageReady || _pendingFieldLocationWireframeMessage == null || _webView.CoreWebView2 == null)
                return;
            _webView.CoreWebView2.PostWebMessageAsJson(_pendingFieldLocationWireframeMessage);
            _pendingFieldLocationWireframeMessage = null;
        }

        private void SendEditLightsVisibility() {
            if (!_pageReady || _webView.CoreWebView2 == null)
                return;
            _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new {
                type = "edit-lights-visibility",
                visible = _editLightsAction != null,
            }));
        }

        private void SendPopOutVisibility() {
            if (!_pageReady || _webView.CoreWebView2 == null)
                return;
            _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new {
                type = "pop-out-visibility",
                visible = _popOutAction != null,
            }));
        }

        private void SendFieldLocationWireframeToggleVisibility() {
            if (!_pageReady || _webView.CoreWebView2 == null)
                return;
            _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new {
                type = "field-location-wireframe-visibility",
                visible = _fieldLocationWireframeToggleVisible,
            }));
        }

        private void SendTerrainViewModeControl() {
            if (!_pageReady || _webView.CoreWebView2 == null)
                return;
            _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new {
                type = "terrain-view-mode-control",
                visible = _terrainViewModeControlVisible,
                mode = _terrainViewMode,
                mapId = _terrainMapId,
            }));
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
            Interlocked.Increment(ref _animationRequest);
            _lastAnimationMessage = null;
            _pendingAnimationMessage = null;
            QueueViewerMessage(JsonSerializer.Serialize(new { type = "clear", message }));
            _status.Text = message;
            _status.Visibility = Visibility.Visible;
        }

        private void QueueViewerMessage(string message) {
            _lastViewerMessage = message;
            _pendingMessage = message;
            ViewerMessageProduced?.Invoke(message);
            SendPendingMessage();
        }
    }
}
