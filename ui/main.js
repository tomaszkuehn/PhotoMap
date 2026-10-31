const { invoke } = window.__TAURI__.core;
const { listen } = window.__TAURI__.event;

const MAX_ZOOM = 19;

const map = L.map('map', { minZoom: 2, maxZoom: MAX_ZOOM, worldCopyJump: true, zoomSnap: 1 });
L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
  maxZoom: MAX_ZOOM,
  attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
}).addTo(map);
map.setView([24, 8], 2);

const clusterLayer = L.layerGroup().addTo(map);
let photos = [];

const PROTO = 'http://photo.localhost';
function b64url(s) {
  return btoa(unescape(encodeURIComponent(s)))
    .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}
const thumbUrl = p => `${PROTO}/t/${b64url(p.path)}`;
const fullUrl = p => `${PROTO}/f/${b64url(p.path)}`;

/* Web-mercator world-pixel projection at integer zoom z */
function project(lat, lon, z) {
  const s = 256 * Math.pow(2, z);
  const x = ((lon + 180) / 360) * s;
  const sin = Math.max(-1, Math.min(1, Math.sin((lat * Math.PI) / 180)));
  const y = (0.5 - Math.log((1 + sin) / (1 - sin)) / (4 * Math.PI)) * s;
  return { x, y };
}

/* Cluster cell size (world px) per zoom: zooming in halves/quarters the
   location range until single photos appear at max zoom. */
function cellSize(z) {
  if (z <= 11) return 256;
  if (z <= 15) return 128;
  if (z <= 17) return 64;
  return 40;
}

function formatCount(n) {
  return n > 999 ? (n / 1000).toFixed(1) + 'k' : String(n);
}

function render() {
  clusterLayer.clearLayers();
  if (!photos.length) return;
  const z = Math.max(0, Math.min(MAX_ZOOM, Math.round(map.getZoom())));
  const cell = cellSize(z);

  const groups = new Map();
  for (const p of photos) {
    const { x, y } = project(p.lat, p.lon, z);
    const key = Math.floor(x / cell) + ':' + Math.floor(y / cell);
    let arr = groups.get(key);
    if (!arr) { arr = []; groups.set(key, arr); }
    arr.push(p);
  }

  const bounds = map.getBounds().pad(0.3);
  for (const g of groups.values()) {
    let lat = 0, lon = 0;
    for (const p of g) { lat += p.lat; lon += p.lon; }
    lat /= g.length; lon /= g.length;
    if (!bounds.contains([lat, lon])) continue;

    if (g.length === 1) addSingle(g[0]);
    else if (z >= 18 && g.length <= 16) addSpider(g);
    else addFrame(g.length, lat, lon);
  }
}

function addFrame(n, lat, lon) {
  const size = Math.min(138, 42 + Math.round(Math.sqrt(n) * 8));
  const icon = L.divIcon({
    className: '', iconSize: [size, size], iconAnchor: [size / 2, size / 2],
    html: `<div class="frame" style="width:${size}px;height:${size}px" title="${n} photos"><span>${formatCount(n)}</span></div>`
  });
  L.marker([lat, lon], { icon, riseOnHover: true }).on('click', () => {
    map.setView([lat, lon], Math.min(MAX_ZOOM, map.getZoom() + 1));
  }).addTo(clusterLayer);
}

function addSingle(p, dx = 0, dy = 0) {
  const icon = L.divIcon({
    className: '', iconSize: [48, 48], iconAnchor: [24 - dx, 24 - dy],
    html: `<div class="thumb"><img src="${thumbUrl(p)}" loading="lazy" draggable="false"></div>`
  });
  L.marker([p.lat, p.lon], { icon, riseOnHover: true })
    .on('click', () => openOverlay(p))
    .addTo(clusterLayer);
}

/* Near-coincident photos at max zoom: fan out in a golden-angle spiral */
function addSpider(g) {
  g.forEach((p, i) => {
    if (i === 0) { addSingle(p); return; }
    const a = i * 2.39996;
    const r = 12 + 15 * Math.sqrt(i);
    addSingle(p, r * Math.cos(a), r * Math.sin(a));
  });
}

/* ---- photo overlay ---- */
const overlay = document.getElementById('overlay');
const ovImg = document.getElementById('ov-img');
const ovName = document.getElementById('ov-name');
const ovMeta = document.getElementById('ov-meta');

function openOverlay(p) {
  ovImg.classList.remove('ready');
  ovImg.src = fullUrl(p);
  ovName.textContent = p.name;
  ovMeta.textContent = (p.date ? p.date + ' · ' : '') + p.lat.toFixed(5) + ', ' + p.lon.toFixed(5);
  overlay.hidden = false;
}
function closeOverlay() {
  overlay.hidden = true;
  ovImg.src = '';
}
ovImg.addEventListener('load', () => ovImg.classList.add('ready'));
document.querySelector('.ov-back').addEventListener('click', closeOverlay);
document.getElementById('ov-close').addEventListener('click', closeOverlay);
window.addEventListener('keydown', e => {
  if (e.key === 'Escape' && !overlay.hidden) closeOverlay();
});

/* ---- folder loading ---- */
const openBtn = document.getElementById('openBtn');
const statusEl = document.getElementById('status');
const status = t => { statusEl.textContent = t; };

openBtn.addEventListener('click', async () => {
  openBtn.disabled = true;
  try {
    const folder = await invoke('pick_folder');
    if (!folder) return;
    status('Scanning ' + folder + ' …');
    const res = await invoke('scan_folder', { path: folder });
    photos = res.photos || [];
    render();
    if (photos.length) {
      map.fitBounds(L.latLngBounds(photos.map(p => [p.lat, p.lon])), { padding: [70, 70], maxZoom: 14 });
      status(`${res.mapped.toLocaleString()} of ${res.scanned.toLocaleString()} photos have GPS`);
    } else {
      status(`No geotagged photos found (${res.scanned.toLocaleString()} scanned, photos without GPS are ignored)`);
    }
  } catch (err) {
    status('Error: ' + (err && err.message ? err.message : err));
  } finally {
    openBtn.disabled = false;
  }
});

listen('scan-progress', e => {
  const p = e.payload;
  status(`Scanning… ${p.scanned.toLocaleString()} files checked, ${p.mapped.toLocaleString()} geotagged`);
});

map.on('zoomend', render);
map.on('moveend', render);
render();