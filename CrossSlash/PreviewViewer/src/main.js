import './style.css';

const emptyState = document.querySelector('#empty-state');
const controlPanel = document.querySelector('#controls');
const editLightsButton = document.querySelector('#edit-lights-button');
const popOutButton = document.querySelector('#pop-out-button');
const fieldLocationWireframeToggle = document.querySelector('#field-location-wireframe-toggle');
const fieldLocationWireframeInput = document.querySelector('#field-location-wireframe-input');
const terrainViewMode = document.querySelector('#terrain-view-mode');
const terrainHelpButton = document.querySelector('#terrain-help-button');
const terrainHelpOverlay = document.querySelector('#terrain-help-overlay');
const terrainHelpDialog = document.querySelector('#terrain-help-dialog');
const terrainHelpMapNote = document.querySelector('#terrain-help-map-note');
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
let terrainMapId = -1;
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

const terrainTypes = [
  ['Grass', 'Ordinary land; most things can travel here.'],
  ['Forest', 'Vehicles cannot land here; other movement is allowed.'],
  ['Mountain', 'Only chocobos and flying craft can travel here.'],
  ['Sea', 'Deep water for the gold chocobo or submarine.'],
  ['River crossing', 'Buggy, Tiny Bronco, and water-capable chocobos.'],
  ['River', 'Tiny Bronco and chocobos can travel here.'],
  ['Water', 'Shallow water with river-like access.'],
  ['Swamp', 'The Midgar Zolom can move through this terrain.'],
  ['Desert', 'Vehicles cannot land here.'],
  ['Wasteland', 'Barren terrain; vehicles cannot land here.'],
  ['Snow', 'Leaves footprints; vehicles cannot land here.'],
  ['Riverside', 'Shoreline where river meets land.'],
  ['Cliff', 'Sharp elevation edge, often separating walkable levels.'],
  ['Corel Bridge', 'Bridge between Costa del Sol and Corel.'],
  ['Wutai Bridge', 'Rope bridges south of Wutai.'],
  ['Underwater tunnel', 'Prevents the submarine from surfacing.'],
  ['Hillside', 'Small walkable area at the foot of a mountain.'],
  ['Beach', 'Shoreline where land meets shallow water.'],
  ['Sub pen', 'Submarine entry and exit point.'],
  ['Canyon', 'Cosmo Canyon ground; walkability is similar to wasteland.'],
  ['Mountain pass', 'Path through the mountains between Costa del Sol and Corel.'],
  ['Unknown (21)', 'Found near bridges and the Northern Crater; purpose is unclear.'],
  ['Waterfall', 'River terrain that the Tiny Bronco cannot enter.'],
  ['Unused (23)', 'Not seen in the original map data.'],
  ['Gold Saucer desert', 'Special desert terrain around the Gold Saucer.'],
  ['Jungle', 'Used in the south; movement is similar to forest.'],
  ['Sea (2)', 'Rare deep-water type near HP-MP Cave; purpose is unclear.'],
  ['Northern Cave', 'Inside the crater, where the Highwind can land.'],
  ['Gold Saucer desert border', 'Narrow border around the desert, likely tied to quicksand behavior.'],
  ['Bridgehead', 'Small area at bridge ends; special behavior is unclear.'],
  ['Back entrance', 'Can be made unwalkable by a world map script.'],
  ['Unused (31)', 'Not seen in the original map data.'],
];

const regionNames = [
  'Midgar Area', 'Grasslands Area', 'Junon Area', 'Corel Area', 'Gold Saucer Area',
  'Gongaga Area', 'Cosmo Area', 'Nibel Area', 'Rocket Launch Pad Area', 'Wutai Area',
  'Woodlands Area', 'Icicle Area', 'Mideel Area', 'North Corel Area', 'Cactus Island',
  'Goblin Island', 'Round Island', 'Sea', 'Bottom of the Sea', 'Glacier',
];

const terrainViewColors = {
  terrain: {
    0: { 0: '#4c3', 1: '#3a3', 2: '#977', 3: '#00a', 4: '#2bad9f', 5: '#00d', 6: '#14f', 7: '#898', 8: '#ee7', 9: '#aa5', 10: '#eee', 11: '#ff0', 12: '#aa0', 13: '#0d0', 14: '#0e0', 15: '#0f0', 16: '#a98', 17: '#ffa', 18: '#668', 19: '#aa7', 20: '#bba', 21: '#a88', 22: '#0af', 23: '#000', 24: '#ee7', 25: '#282', 26: '#008', 27: '#fbb', 28: '#ffc', 29: '#b99', 30: '#c99' },
    2: { 0: '#258', 3: '#69a', 15: '#479' },
    3: { 1: '#aaa', 2: '#cce', 9: '#eee', 10: '#fff' },
  },
  region: {
    0: { 0: '#605a5a', 1: '#5c3', 2: '#69c', 3: '#988', 4: '#d0a040', 5: '#492', 6: '#aa6039', 7: '#809080', 8: '#6a7', 9: '#e65', 10: '#5e5', 11: '#fff', 12: '#a86', 13: '#c97', 14: '#0d0', 15: '#8f0', 16: '#f9a', 17: '#00a' },
    2: { 0: '#258', 18: '#69a' },
    3: { 11: '#fff' },
  },
  scripts: { 0: '#6a6a6a', 1: '#555555', 2: '#000000', 3: '#ff4a00', 4: '#ff0000', 5: '#ff5500', 6: '#ff8800', 7: '#ffaa00' },
};

function addTerrainGuideItems(container, items, colorMap) {
  container.replaceChildren(...items.map(([id, title, description]) => {
    const item = document.createElement('div');
    item.className = 'terrain-help-item';
    const swatch = document.createElement('span');
    swatch.className = 'terrain-help-swatch';
    swatch.style.backgroundColor = colorMap[id] || '#444444';
    swatch.setAttribute('aria-hidden', 'true');
    const copy = document.createElement('span');
    copy.className = 'terrain-help-copy';
    const heading = document.createElement('span');
    heading.className = 'terrain-help-item-heading';
    heading.textContent = `${id} · ${title}`;
    copy.append(heading);
    if (description) {
      const detail = document.createElement('span');
      detail.className = 'terrain-help-item-description';
      detail.textContent = description;
      copy.append(detail);
    }
    item.append(swatch, copy);
    return item;
  }));
}

function renderTerrainHelp() {
  const terrainColors = terrainViewColors.terrain[terrainMapId] || {};
  const regionColors = terrainViewColors.region[terrainMapId] || {};
  const mapLabel = terrainMapId === 0 ? 'Overworld' : terrainMapId === 2 ? 'Underwater' : terrainMapId === 3 ? 'Great Glacier' : null;
  terrainHelpMapNote.textContent = mapLabel
    ? `Color samples match the ${mapLabel} map. Gray samples mean CrossSlash has no color entry for that value.`
    : 'Select a terrain map to see its color samples. Gray samples mark values without a color entry.';
  addTerrainGuideItems(document.querySelector('#terrain-type-guide'), terrainTypes.map(([title, description], id) => [id, title, description]), terrainColors);
  addTerrainGuideItems(document.querySelector('#terrain-region-guide'), regionNames.map((name, id) => [id, name, '']), regionColors);
  const scripts = [
    ['No script', 'No mesh script is assigned.'],
    ['No battles', 'Suppresses random battles on this triangle.'],
    ['Unused / unknown', 'Value 2 is not listed as an active script type.'],
    ['Mesh function 0', 'Runs mesh script function 0.'],
    ['Mesh function 1', 'Runs mesh script function 1.'],
    ['Mesh function 2', 'Runs mesh script function 2.'],
    ['Mesh function 3', 'Runs mesh script function 3.'],
    ['Mesh function 4', 'Runs mesh script function 4.'],
  ];
  addTerrainGuideItems(document.querySelector('#terrain-script-guide'), scripts.map(([title, description], id) => [id, title, description]), terrainViewColors.scripts);
}

function closeTerrainHelp() {
  terrainHelpOverlay.hidden = true;
  terrainHelpButton.setAttribute('aria-expanded', 'false');
}

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

  if (message.type === 'terrain-view-mode-control') {
    terrainViewMode.hidden = message.visible !== true;
    terrainHelpButton.hidden = message.visible !== true;
    if ([...terrainViewMode.options].some((option) => option.value === message.mode))
      terrainViewMode.value = message.mode;
    terrainMapId = Number.isInteger(message.mapId) ? message.mapId : -1;
    if (!terrainHelpOverlay.hidden)
      renderTerrainHelp();
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
        module.loadModel(message.glbBase64, message.name || 'Model preview', message.lighting || null, message.cameraZoom, message.cameraMode || 'orbit', message.previewData || null, message.flyMovementSpeedScale, message.preserveCamera === true);
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
terrainViewMode.addEventListener('change', () => postToHost({ type: 'terrain-view-mode-selected', mode: terrainViewMode.value }));
terrainHelpButton.addEventListener('click', () => {
  renderTerrainHelp();
  terrainHelpOverlay.hidden = false;
  terrainHelpButton.setAttribute('aria-expanded', 'true');
  terrainHelpDialog.focus();
});
terrainHelpOverlay.addEventListener('click', closeTerrainHelp);
terrainHelpDialog.addEventListener('click', (event) => event.stopPropagation());
document.addEventListener('keydown', (event) => {
  if (event.key === 'Escape' && !terrainHelpOverlay.hidden) {
    closeTerrainHelp();
    terrainHelpButton.focus();
  }
});
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
