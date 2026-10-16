import './style.css';

const emptyState = document.querySelector('#empty-state');
const controlPanel = document.querySelector('#controls');
const editLightsButton = document.querySelector('#edit-lights-button');
const popOutButton = document.querySelector('#pop-out-button');
const fieldLocationWireframeToggle = document.querySelector('#field-location-wireframe-toggle');
const fieldLocationWireframeInput = document.querySelector('#field-location-wireframe-input');
const fieldLightingRow = document.querySelector('#field-lighting-row');
const fieldSearch = document.querySelector('#field-search');
const fieldOptions = document.querySelector('#field-options');
const fieldToggle = document.querySelector('#field-toggle');
const lightingModelSearch = document.querySelector('#lighting-model-search');
const lightingModelOptions = document.querySelector('#lighting-model-options');
const lightingModelToggle = document.querySelector('#lighting-model-toggle');
let availableFields = [];
let availableLightingModels = [];
let selectedFieldId = '';
let selectedLightingModelIndex = -1;
let viewerModule = null;
let viewerModulePromise = null;
let visible = true;
let fieldLocationWireframe = true;
let modelRequest = 0;
let animationRequest = 0;
let viewerControlsSuppressed = false;

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

function setupSearchableDropdown(input, toggle, list, getOptions, getSelectedLabel, choose) {
  const hide = () => {
    list.hidden = true;
    input.setAttribute('aria-expanded', 'false');
    toggle.setAttribute('aria-expanded', 'false');
  };
  const render = () => {
    const query = input.value.trim().toLowerCase() === getSelectedLabel().toLowerCase()
      ? ''
      : input.value.trim().toLowerCase();
    const matches = getOptions().filter((option) => option.label.toLowerCase().includes(query));
    list.replaceChildren(...matches.map((option) => {
      const button = document.createElement('button');
      button.type = 'button';
      button.setAttribute('role', 'option');
      button.setAttribute('aria-selected', option.label === getSelectedLabel() ? 'true' : 'false');
      button.textContent = option.label;
      button.title = option.label;
      button.addEventListener('mousedown', (event) => event.preventDefault());
      button.addEventListener('click', () => {
        input.value = option.label;
        hide();
        choose(option);
      });
      return button;
    }));
  };
  const open = () => {
    render();
    list.hidden = false;
    input.setAttribute('aria-expanded', 'true');
    toggle.setAttribute('aria-expanded', 'true');
  };
  input.addEventListener('focus', () => open());
  input.addEventListener('input', () => open());
  input.addEventListener('keydown', (event) => {
    if (event.key === 'Escape') {
      input.value = getSelectedLabel();
      hide();
    } else if (event.key === 'Enter' && !list.hidden) {
      const first = list.querySelector('button');
      if (first) {
        event.preventDefault();
        first.click();
      }
    } else if (event.key === 'ArrowDown' && list.hidden) {
      open();
    }
  });
  toggle.addEventListener('click', () => list.hidden ? open() : hide());
  document.addEventListener('mousedown', (event) => {
    if (!event.target.closest(`#${input.parentElement.id}`))
      hide();
  });
  return render;
}

let renderFields = () => {};
let renderLightingModels = () => {};

window.chrome?.webview?.addEventListener('message', (event) => {
  const message = event.data;
  if (!message || typeof message !== 'object') return;

  if (message.type === 'visibility') {
    visible = message.visible === true;
    viewerModule?.setVisibility(visible);
    return;
  }

  if (message.type === 'edit-lights-visibility') {
    editLightsButton.style.display = message.visible ? 'inline-block' : 'none';
    return;
  }

  if (message.type === 'pop-out-visibility') {
    popOutButton.style.display = message.visible ? 'block' : 'none';
    return;
  }

  if (message.type === 'field-location-wireframe-visibility') {
    fieldLocationWireframeToggle.hidden = message.visible !== true;
    return;
  }

  if (message.type === 'field-location-wireframe') {
    fieldLocationWireframe = message.visible === true;
    fieldLocationWireframeInput.checked = fieldLocationWireframe;
    viewerModule?.setFieldLocationWireframe(fieldLocationWireframe);
    return;
  }

  if (message.type === 'field-lighting-options') {
    const options = message.options || {};
    availableFields = options.fields || [];
    availableLightingModels = options.models || [];
    selectedFieldId = options.selectedField || '';
    selectedLightingModelIndex = options.selectedModelIndex == null ? -1 : Number(options.selectedModelIndex);
    fieldSearch.value = getSelectedFieldLabel();
    lightingModelSearch.value = getSelectedLightingModelLabel();
    renderFields();
    renderLightingModels();
    // Field lighting options can arrive after a Battlefield model load due to
    // queued WebView messages. Never let them reveal the viewer controls in fly mode.
    fieldLightingRow.hidden = true;
    controlPanel.hidden = viewerControlsSuppressed;
    return;
  }

  if (message.type === 'prepare-viewer') {
    prepareViewer().catch(showLoadError);
    return;
  }

  if (message.type === 'clear') {
    modelRequest++;
    animationRequest++;
    setEmptyState(message.message || 'Choose a model to preview');
    viewerModule?.clearModel(message.message);
    return;
  }

  if (message.type === 'load-model') {
    const request = ++modelRequest;
    animationRequest++;
    const flyMode = message.cameraMode === 'fly';
    const fieldMode = message.cameraMode === 'field';
    viewerControlsSuppressed = flyMode || message.hideControls === true;
    controlPanel.hidden = viewerControlsSuppressed;
    fieldLightingRow.hidden = viewerControlsSuppressed;
    document.querySelector('#model-canvas').title = flyMode
      ? 'Fly controls: drag to look; W/A/S/D to move; Q/E to descend and rise; Shift to move faster.'
      : fieldMode
        ? 'Field controls: left-drag to zoom; middle-drag to pan; right-click to fly; left-click or middle-click to return to 2D; double-click to reset.'
        : '';
    setEmptyState('Loading preview…');
    prepareViewer().then((module) => {
      if (request === modelRequest)
      {
        module.loadModel(message.glbBase64, message.name || 'Model preview', message.lighting || null, message.cameraZoom, message.cameraMode || 'orbit', message.previewData || null, message.flyMovementSpeedScale);
        module.setFieldLocationWireframe(fieldLocationWireframe);
      }
    }).catch(showLoadError);
    return;
  }

  if (message.type === 'load-animation') {
    const request = ++animationRequest;
    const modelAtRequest = modelRequest;
    prepareViewer().then((module) => {
      if (request === animationRequest && modelAtRequest === modelRequest)
        module.loadAnimation(message.glbBase64, message.name || 'Animation');
    }).catch(showLoadError);
    return;
  }

  if (message.type === 'update-lighting') {
    prepareViewer().then((module) => {
      module.setLighting(message.lighting || null);
    }).catch(showLoadError);
  }
});

editLightsButton.addEventListener('click', () => postToHost({ type: 'edit-lights' }));
popOutButton.addEventListener('click', () => postToHost({ type: 'pop-out' }));
fieldLocationWireframeInput.addEventListener('change', () => {
  fieldLocationWireframe = fieldLocationWireframeInput.checked;
  viewerModule?.setFieldLocationWireframe(fieldLocationWireframe);
  postToHost({ type: 'field-location-wireframe', visible: fieldLocationWireframe });
});
function getSelectedFieldLabel() {
  return availableFields.find((field) => field.id === selectedFieldId)?.label || selectedFieldId;
}
function getSelectedLightingModelLabel() {
  return availableLightingModels.find((model) => model.index === selectedLightingModelIndex)?.label || '';
}

renderFields = setupSearchableDropdown(
  fieldSearch,
  fieldToggle,
  fieldOptions,
  () => availableFields,
  getSelectedFieldLabel,
  (field) => postToHost({ type: 'field-scene-selected', fieldId: field.id }),
);
renderLightingModels = setupSearchableDropdown(
  lightingModelSearch,
  lightingModelToggle,
  lightingModelOptions,
  () => availableLightingModels,
  getSelectedLightingModelLabel,
  (model) => postToHost({ type: 'lighting-model-selected', index: String(model.index) }),
);

postToHost({ type: 'viewer-ready' });
