#!/usr/bin/env python3
"""Assemble ARCHIVISION en un seul fichier HTML à partir des sources de src/ (même principe qu'ARCHIPLAN et ARCHITRAFIC).

  src/parts/head.html            titre, polices
  src/css/*.css                  styles (ordre : CSS_ORDER) : charte Studio Prism d'ARCHIPLAN + couche ARCHIVISION
  src/parts/body.html            structure de la page (intro, compte, visite, réalité augmentée, accueil)
  src/js/intro.js                animation d'ouverture (insérée juste après l'écran #splash, pour démarrer avant le premier affichage)
  src/js/auth.js + vision-home.js + vision-sync.js + auth-end.js   compte Studio Prism, accueil « Mes maquettes », galerie (script classique, une seule IIFE)
  src/parts/importmap.html       Three.js r160 (jsDelivr)
  src/engine/*.js                moteur 3D + AR (module ES, fichiers concaténés dans l'ordre alphabétique)

Sorties :
  index.html                     site en ligne (GitHub Pages) : Firebase branché
  dist/archivision-demo.html     démo autonome (comptes dans le navigateur), utilisée pour les tests
Usage : python3 tools/build.py
"""
import pathlib, sys, json
ROOT = pathlib.Path(__file__).resolve().parent.parent
S = ROOT / 'src'
CSS_ORDER = ['app.css', 'splash.css', 'auth.css', 'home.css', 'brand.css', 'vision.css', 'arlive.css', 'ctx.css']
FB = '10.12.2'
CONFIG = '''const FIREBASE_CONFIG = {
  // Projet Firebase de Studio Prism : le même que le site, ARCHIPLAN et ARCHITRAFIC. Configuration publique par conception (la sécurité repose sur les règles Firestore).
  apiKey: "AIzaSyAAD5rrHuVh6_PC2_45L3y9F7zWe5ACW6E",
  authDomain: "archivision-studioprism.firebaseapp.com",
  projectId: "archivision-studioprism",
  storageBucket: "archivision-studioprism.firebasestorage.app",
  messagingSenderId: "226072216517",
  appId: "1:226072216517:web:0aeef645a4f1481fe801ac"
};'''
def rd(p): return (S / p).read_text(encoding='utf-8')
def assemble():
    engine = ''.join(f'/* ==== {f.name} ==== */\n' + f.read_text(encoding='utf-8') + '\n' for f in sorted((S / 'engine').glob('*.js')))
    return (rd('parts/head.html') + '<style>\n' + ''.join(rd('css/' + c) for c in CSS_ORDER) + '</style>\n' + rd('parts/body.html').replace('<!--SP-INTRO-->', '<script>\n' + rd('js/intro.js') + '</script>')
            + '<script>\n' + rd('js/auth.js') + rd('js/vision-home.js') + rd('js/vision-sync.js') + rd('js/auth-end.js') + '</script>\n'
            + rd('parts/importmap.html') + '<script type="module">\n' + engine + '</script>\n')
HEAD = ('<!doctype html><html lang="fr"><head><meta charset="utf-8">'
        '<meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,viewport-fit=cover">'
        '<meta name="description" content="ARCHIVISION · Studio Prism : maquettes BIM (IFC, GLB) en visite 3D et en réalité augmentée, sur téléphone, tablette et ordinateur.">'
        '<meta name="theme-color" content="#0B1F45"><link rel="icon" href="assets/emblem-64.webp"><link rel="apple-touch-icon" href="assets/icon-192.png">'
        '<link rel="manifest" href="manifest.webmanifest"><meta name="apple-mobile-web-app-capable" content="yes"><meta name="mobile-web-app-capable" content="yes">'
        '<link rel="preconnect" href="https://www.gstatic.com" crossorigin>'
        '<link rel="preconnect" href="https://firestore.googleapis.com">'
        '<link rel="preconnect" href="https://identitytoolkit.googleapis.com">'
        '<link rel="preconnect" href="https://securetoken.googleapis.com">'
        '<link rel="preconnect" href="https://cdn.jsdelivr.net" crossorigin>'
        + ''.join(f'<link rel="preload" as="script" href="https://www.gstatic.com/firebasejs/{FB}/firebase-{n}-compat.js">' for n in ('app', 'auth', 'firestore')) +
        '<style>[hidden]{display:none!important}html{overflow:hidden;overscroll-behavior:none}body{overflow:hidden;overscroll-behavior:none}*{overscroll-behavior-x:contain}:root{padding-top:env(safe-area-inset-top,0px);padding-bottom:env(safe-area-inset-bottom,0px);touch-action:manipulation;-webkit-text-size-adjust:100%;text-size-adjust:100%}body{margin:0}</style></head><body>\n')
MANIFEST = {
    'name': 'ARCHIVISION · Studio Prism', 'short_name': 'ARCHIVISION', 'lang': 'fr', 'start_url': './', 'scope': './', 'display': 'standalone',
    'background_color': '#F3F6F9', 'theme_color': '#0B1F45', 'description': 'Maquettes BIM en visite 3D et en réalité augmentée.',
    'icons': [{'src': 'assets/icon-192.png', 'sizes': '192x192', 'type': 'image/png'}, {'src': 'assets/icon-512.png', 'sizes': '512x512', 'type': 'image/png'}],
}
# ---- référencement et informations légales (ajouté le 19/11/2026, voir README) ----
SEO_URL = 'https://archivision.studio-prism.com/'
SEO_TITLE = 'ARCHIVISION — Maquettes BIM en 3D et réalité augmentée · Studio Prism'
SEO_IMG = 'assets/icon-512.png'
SEO_CAT = 'DesignApplication'
SP_LEGAL = 'https://studio-prism.com/'
def _spseo(live):
    import json as _j, re as _re, html as _h
    m = _re.search(r'<meta name="description" content="([^"]*)"', live); desc = _h.unescape(m.group(1)) if m else ''
    e = lambda s: _h.escape(s, quote=True)
    ld = {'@context': 'https://schema.org', '@type': 'SoftwareApplication', 'name': SEO_TITLE.split(' — ')[0], 'url': SEO_URL, 'description': desc,
          'applicationCategory': SEO_CAT, 'operatingSystem': 'Web (navigateur) : Windows, macOS, Linux, Android, iOS', 'inLanguage': 'fr-FR',
          'image': SEO_URL + SEO_IMG, 'offers': {'@type': 'Offer', 'price': '0', 'priceCurrency': 'EUR'},
          'publisher': {'@type': 'Organization', 'name': 'Studio Prism', 'url': SP_LEGAL}}
    tags = (f'<link rel="canonical" href="{SEO_URL}"><meta property="og:type" content="website"><meta property="og:site_name" content="Studio Prism">'
            f'<meta property="og:title" content="{e(SEO_TITLE)}"><meta property="og:description" content="{e(desc)}"><meta property="og:url" content="{SEO_URL}">'
            f'<meta property="og:image" content="{SEO_URL}{SEO_IMG}"><meta property="og:locale" content="fr_FR"><meta name="twitter:card" content="summary">'
            '<script type="application/ld+json">' + _j.dumps(ld, ensure_ascii=False).replace('</', '<\\/') + '</script>')
    live = _re.sub(r'<title>[^<]*</title>', '<title>' + e(SEO_TITLE) + '</title>', live, count=1)
    live = live.replace('</head>', tags + '</head>', 1)
    nav = ('<nav class="sp-legal" aria-label="Informations légales"><a href="' + SP_LEGAL + 'mentions-legales" target="_blank" rel="noopener">Mentions légales</a>'
           '<a href="' + SP_LEGAL + 'confidentialite" target="_blank" rel="noopener">Confidentialité</a><a href="' + SP_LEGAL + 'conditions-utilisation" target="_blank" rel="noopener">CGU</a>'
           '<a href="' + SP_LEGAL + 'cgv" target="_blank" rel="noopener">CGV</a><a href="' + SP_LEGAL + 'cookies" target="_blank" rel="noopener">Cookies</a></nav>'
           '<style>.sp-legal{position:fixed;left:50%;transform:translateX(-50%);bottom:calc(8px + env(safe-area-inset-bottom,0px));z-index:40;display:flex;flex-wrap:wrap;justify-content:center;gap:2px 14px;'
           'width:max-content;max-width:calc(100% - 16px);box-sizing:border-box;padding:4px 14px;border-radius:999px;background:rgba(255,255,255,.9);color:#3a4256;box-shadow:0 1px 4px rgba(0,0,0,.12);font:12px/1.5 Inter,system-ui,sans-serif}.sp-legal a{color:inherit;opacity:.85;text-decoration:none}.sp-legal a:hover,.sp-legal a:focus-visible{opacity:1;text-decoration:underline}</style>'
           '<script>(function(){var a=document.getElementById("auth"),n=document.querySelector(".sp-legal");if(a&&n)a.appendChild(n);else if(n)n.remove()})()</script>')
    i = live.rfind('</body>')
    return live[:i] + nav + live[i:] if i > 0 else live + nav
def _spdemo(s):
    return s.replace('<meta charset="utf-8">', '<meta charset="utf-8"><meta name="robots" content="noindex,follow">', 1) if '<meta charset="utf-8">' in s[:300] else '<meta name="robots" content="noindex,follow">' + s

def main(argv):
    demo = assemble()
    assert demo.count('const FIREBASE_CONFIG = null;') == 1 and f"FIREBASE_VERSION = '{FB}'" in demo
    (ROOT / 'dist').mkdir(exist_ok=True)
    lnk = ROOT / 'dist' / 'assets'
    if not lnk.exists(): lnk.symlink_to('../assets')
    (ROOT / 'dist' / 'archivision-demo.html').write_text(_spdemo(HEAD + demo + '</body></html>\n'), encoding='utf-8')
    live = HEAD + demo.replace('const FIREBASE_CONFIG = null;', CONFIG).replace('<title>ARCHIVISION</title>', '<title>ARCHIVISION · Studio Prism</title>', 1) + '</body></html>\n'
    (ROOT / 'index.html').write_text(_spseo(live), encoding='utf-8')
    (ROOT / 'manifest.webmanifest').write_text(json.dumps(MANIFEST, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    # convertisseur IFC → GLB de l'application Android (WebView invisible) : même code IFC que le site
    load = (S / 'engine' / '30-load.js').read_text(encoding='utf-8')
    a, b = load.index('/* export GLB'), load.index('/* ---------- ZIP')
    imports = (S / 'engine' / '00-imports.js').read_text(encoding='utf-8')
    conv = ('<!doctype html><html lang="fr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">'
            '<meta name="robots" content="noindex"><link rel="icon" href="assets/emblem-64.webp"><link rel="apple-touch-icon" href="assets/icon-192.png"><title>ARCHIVISION · convertisseur IFC</title></head>'
            '<body style="font:14px system-ui,sans-serif;margin:24px;color:#0B1F45"><p id="st">Convertisseur IFC → GLB…</p>\n'
            + rd('parts/importmap.html') + '<script type="module">\n' + imports
            + 'const frame = () => new Promise(r => requestAnimationFrame(() => r()));\n'
            + load[a:b] + rd('ifc/driver.js') + '</script></body></html>\n')
    (ROOT / 'ifc-convert.html').write_text(conv, encoding='utf-8')
    print('index.html', len(live), 'octets · dist/archivision-demo.html', len(demo), 'octets')
if __name__ == '__main__': main(sys.argv[1:])
