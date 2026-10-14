import './style.css';

const emptyState = document.querySelector('#empty-state');
let viewerModule = null;
let viewerModulePromise = null;
let visible = true;
let modelRequest = 0;

function postToHost(payload) {
  window.chrome?.webview?.postMessage(payload);
}

function setEmptyState(message, show = true) {
  emptyState.textContent = message;
  emptyState.classList.toggle('visible', show);
}

function prepareViewer() {
  if (!viewerModulePromise) {
    viewerModulePromise = import('./viewer-runtime.js').then((module) => {
      viewerModule = module;
      module.setVisibility(visible);
      return module;
    });
  }
  return viewerModulePromise;
}

function showLoadError(error) {
  const detail = error?.message || 'The 3D preview could not start.';
  setEmptyState(`Preview failed: ${detail}`);
  postToHost({ type: 'preview-error', message: detail });
}

window.chrome?.webview?.addEventListener('message', (event) => {
  const message = event.data;
  if (!message || typeof message !== 'object') return;

  if (message.type === 'visibility') {
    visible = message.visible === true;
    viewerModule?.setVisibility(visible);
    return;
  }

  if (message.type === 'prepare-viewer') {
    prepareViewer().catch(showLoadError);
    return;
  }

  if (message.type === 'clear') {
    modelRequest++;
    setEmptyState(message.message || 'Choose a model to preview');
    viewerModule?.clearModel(message.message);
    return;
  }

  if (message.type === 'load-model') {
    const request = ++modelRequest;
    setEmptyState('Loading preview…');
    prepareViewer().then((module) => {
      if (request === modelRequest)
        module.loadModel(message.glbBase64, message.name || 'Model preview', message.lighting || null, message.cameraZoom);
    }).catch(showLoadError);
    return;
  }

  if (message.type === 'update-lighting') {
    prepareViewer().then((module) => {
      module.setLighting(message.lighting || null);
    }).catch(showLoadError);
  }
});

postToHost({ type: 'viewer-ready' });
