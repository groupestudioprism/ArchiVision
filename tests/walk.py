#!/usr/bin/env python3
"""Collisions du mode Marche sur la maison d'exemple (murs, glissement, mobilier, marches, porte, plafond). Usage : python3 -m http.server 8810 puis python3 tests/walk.py"""
import asyncio, sys
sys.path.insert(0, __file__.rsplit('/', 1)[0])
from e2e import signup, URL, check, fails
from playwright.async_api import async_playwright
async def pos(pg): return await pg.evaluate('(() => { const c = AVDebug.camera.position; return [c.x + 5, c.y - 0.35, c.z + 3.5]; })()')   # repère de la maquette d\'exemple (recentrée à l\'ouverture)
async def put(pg, x, z, yaw=0):
    await pg.evaluate(f"""(() => {{ AVDebug.camera.position.set({x} - 5, 0.35 + 1.6, {z} - 3.5); AVDebug.look.yaw = {yaw}; AVDebug.look.pitch = 0; AVDebug.applyLook(); }})()""")
async def hold(pg, keys, ms):
    keys = keys + ['Shift']
    for k in keys: await pg.keyboard.down(k)
    await pg.wait_for_timeout(ms)
    for k in keys: await pg.keyboard.up(k)
    await pg.wait_for_timeout(300)
async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch(args=['--use-gl=swiftshader', '--enable-webgl', '--ignore-gpu-blocklist'])
        pg = await (await b.new_context(viewport={'width':1200, 'height':760})).new_page(); errs = []
        pg.on('pageerror', lambda e: errs.append(str(e)))
        import os
        THREE = os.environ.get('THREE_DIR', '/tmp/thr/package')   # Three.js 0.160 en local si le CDN est inaccessible
        async def cdn(route):
            f = os.path.join(THREE, route.request.url.split('three@0.160.0/')[1])
            await (route.fulfill(path=f, content_type='text/javascript') if os.path.exists(f) else route.abort())
        await pg.route('https://cdn.jsdelivr.net/npm/three@0.160.0/**', cdn)
        await pg.goto(URL); await pg.wait_for_selector('#ap-email', timeout=25000); await pg.wait_for_function('window.__apReady === true', timeout=25000)
        await signup(pg); await pg.wait_for_selector('#home:not([hidden])'); await pg.click('#homeSample'); await pg.wait_for_selector('#home', state='hidden', timeout=15000)
        await pg.wait_for_timeout(1000); await pg.evaluate("AVDebug.setTool('walk')"); await pg.wait_for_timeout(500)
        # 1. mur nord (z = 0,3) : on s'arrête au rayon du corps
        await put(pg, 5, 3.5, 0); await pg.evaluate("document.activeElement.blur()"); await hold(pg, ['ArrowUp'], 5000); x, y, z = await pos(pg)
        check(0.45 < z < 0.75, f'bloqué par le mur nord z={z:.2f}')
        # 2. glissement le long du mur (avancer + pas de côté)
        x0 = x; await hold(pg, ['ArrowUp', 'ArrowRight'], 2500); x, y, z = await pos(pg)
        check(x > x0 + 0.5 and z > 0.45, f'glisse le long du mur x {x0:.2f}→{x:.2f}, z={z:.2f}')
        # 3. table (z 2,0 → 2,9, plateau à 0,76 m)
        await put(pg, 3, 3.8, 0); await hold(pg, ['ArrowUp'], 4000); x, y, z = await pos(pg)
        check(z > 3.05, f'bloqué par la table z={z:.2f}')
        # 4. cloison (x = 6,2) depuis l'ouest, au-dessus de l'ouverture : mur plein à z = 1.5
        await put(pg, 5, 1.5, -1.5708); await hold(pg, ['ArrowUp'], 4000); x, y, z = await pos(pg)
        check(x < 5.95, f'bloqué par la cloison x={x:.2f}')
        # 5. porte d'entrée traversable (sud, x 1,6 → 2,5) puis chute vers le terrain
        await put(pg, 2.0, 5.0, 3.14159); await hold(pg, ['ArrowUp'], 5000); x, y, z = await pos(pg)
        check(z > 7.2, f'traverse la porte z={z:.2f}')
        # 6. marches (0,15 m) puis obstacle de 0,9 m, ajoutés à la maquette
        await pg.evaluate("""(() => { const V = AVDebug.V, T = window.THREE_FOR_TEST; const mk = (x0, y1, z0, x1, z1) => { const g = new (AVDebug.V.meshes[0].geometry.constructor)(x1 - x0, y1, z1 - z0); g.translate((x0 + x1) / 2, y1 / 2, (z0 + z1) / 2); const m = new (AVDebug.V.meshes[0].constructor)(g, AVDebug.V.meshes[0].material); V.root.add(m); m.updateMatrixWorld(true); return m; };
          const a = [mk(8, 0.15, 3.3, 8.4, 3.7), mk(8.4, 0.3, 3.3, 8.8, 3.7), mk(8.8, 0.45, 3.3, 9.2, 3.7), mk(5, 0.9, 5.5, 5.6, 6.1)]; V.meshes = [...V.meshes, ...a]; })()""")
        await put(pg, 7.0, 3.5, -1.5708); await pg.evaluate("window.__ymax = 0; setInterval(() => { window.__ymax = Math.max(window.__ymax, AVDebug.camera.position.y - 0.35); }, 30)")
        await hold(pg, ['ArrowUp'], 4000); x, y, z = await pos(pg); ym = await pg.evaluate('window.__ymax')
        check(x > 8.5 and ym > 1.6 + 0.4, f'monte les marches x={x:.2f}, yeux max {ym:.2f} (marche 0,45)')
        await put(pg, 5.3, 4.8, 3.14159); await hold(pg, ['ArrowUp'], 3000); x, y, z = await pos(pg)
        check(z > 6.1 + 0.2 and abs(y - 1.6) < 0.05 or z < 6.1 + 0.4, f'bloqué par l\'obstacle haut z={z:.2f}')
        check(not errs, f'aucune erreur {errs}')
        await b.close()
    print('ÉCHECS' if fails else 'TOUT OK'); sys.exit(1 if fails else 0)
asyncio.run(main())
