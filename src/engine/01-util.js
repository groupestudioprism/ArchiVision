/* ============ Utilitaires ============ */
const $ = s => document.querySelector(s), $$ = s => [...document.querySelectorAll(s)];
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const nf = (x, d = 2) => Number(x).toLocaleString('fr-FR', {minimumFractionDigits:d, maximumFractionDigits:d});
const fmtM = x => (Math.abs(x) < 1 ? nf(x * 100, 1) + ' cm' : nf(x, 2) + ' m');
const clamp = (x, a, b) => Math.min(b, Math.max(a, x));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const frame = () => new Promise(r => requestAnimationFrame(() => r()));
let toastT = 0;
function toast(msg){ if (window.AVUI) return window.AVUI.toast(msg); const t = $('#toast'); t.textContent = msg; t.hidden = false; clearTimeout(toastT); toastT = setTimeout(() => t.hidden = true, 2800); }
const isNarrow = matchMedia('(max-width: 860px)');
const isIOS = /iP(hone|ad|od)/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
const canFull = () => !!window.AVAccess?.();
function needLicence(){ if (canFull()) return false; window.AVUI?.licence('export'); return true; }
function prompt(html){ const p = $('#prompt'); if (p) p.innerHTML = html; }
function loading(on, title, step, pct){
  const b = $('#loadBox'); b.hidden = !on; if (!on) return;
  if (title != null) $('#loadTitle').textContent = title; if (step != null) $('#loadStep').textContent = step;
  $('#loadBar').style.width = (pct == null ? 0 : clamp(pct, 0, 1) * 100) + '%';
}
const blobToDataURL = b => new Promise((res, rej) => { const r = new FileReader(); r.onload = () => res(r.result); r.onerror = rej; r.readAsDataURL(b); });
