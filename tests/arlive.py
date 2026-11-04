#!/usr/bin/env python3
"""Test « Voir en AR sur le PC » (démo autonome, deux onglets) + sens de la caméra en vue Marche.
Usage : python3 -m http.server 8810 (racine du dépôt), puis python3 tests/arlive.py"""
import asyncio, sys, os
from playwright.async_api import async_playwright
URL = 'http://localhost:8810/dist/archivision-demo.html'
fails = []; OUT = '/tmp/t'
async def signup(pg, email='test@studioprism.fr'):
    await pg.wait_for_selector('#splash', state='detached', timeout=20000)   # fin de l'intro animée
    await pg.click('text=Créer un compte')
    await pg.fill('#ap-first', 'Kéliann'); await pg.fill('#ap-last', 'Test')
    await pg.fill('#ap-email', email); await pg.fill('#ap-pw', 'Prism-Test-2026x'); await pg.fill('#ap-pw2', 'Prism-Test-2026x')
    await pg.check('#ap-cgu'); await pg.click('#apForm button[type=submit]')
    try: await pg.wait_for_selector('#ap-phone', timeout=20000)
    except Exception:
        await pg.screenshot(path=f'{OUT}/echec_inscription.png'); print('CARTE :', (await pg.inner_text('#apCard'))[:400]); raise
    await pg.fill('#ap-phone', '0600000000'); await pg.click('#apForm button[type=submit]')
    await pg.wait_for_selector('#apSkip', timeout=8000); await pg.click('#apSkip')
def check(c, m):
    print(('OK  ' if c else 'ÉCHEC ') + m)
    if not c: fails.append(m)
async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch(args=['--use-gl=swiftshader', '--enable-webgl', '--ignore-gpu-blocklist'])
        ctx = await b.new_context(viewport={'width':1280, 'height':800}); errs = []
        THREE = os.environ.get('AV_THREE', '/tmp/thr/package')   # copie locale de three@0.160.0 si le CDN est inaccessible
        async def cdn(route):
            f = THREE + route.request.url.split('three@0.160.0')[1]
            await route.fulfill(path=f, content_type='text/javascript') if os.path.exists(f) else await route.abort()
        if os.path.isdir(THREE): await ctx.route('**/three@0.160.0/**', cdn)
        A = await ctx.new_page(); A.on('pageerror', lambda e: errs.append(str(e)))
        await A.goto(URL); await A.wait_for_selector('#ap-email', timeout=25000)
        await signup(A); await A.wait_for_selector('#home:not([hidden])'); await A.wait_for_timeout(1500); await A.click('#homeSample')
        await A.wait_for_selector('#home', state='hidden', timeout=15000); await A.wait_for_timeout(1500)
        # sens de la caméra en vue Marche : glisser vers la droite => regarder à droite (yaw diminue), vers le haut => regarder en haut
        await A.click('.tools [data-tool="walk"]'); await A.wait_for_timeout(400)
        r0 = await A.evaluate('[AVDebug.camera.rotation.y, AVDebug.camera.rotation.x]')
        bx = await A.locator('#c').bounding_box(); x, y = bx['x'] + 600, bx['y'] + 400
        await A.mouse.move(x, y); await A.mouse.down(); await A.mouse.move(x + 80, y - 60, steps=5); await A.mouse.up()
        r1 = await A.evaluate('[AVDebug.camera.rotation.y, AVDebug.camera.rotation.x]')
        check(r1[0] < r0[0] and r1[1] > r0[1], f'Marche : glisser à droite/haut = regarder à droite/haut {r0} -> {r1}')
        await A.click('.tools [data-tool="orbit"]')
        await A.evaluate('AVLive.hostStart()'); await A.wait_for_timeout(500)
        st = await A.evaluate('AVLive.state()'); print(st); code = st['host']
        check(code and len(code) == 6 and st['objects'] > 3, 'hôte : code à 6 caractères et éléments publiés')
        B = await ctx.new_page(); B.on('pageerror', lambda e: errs.append(str(e)))
        await B.goto(URL); await B.wait_for_selector('#splash', state='detached', timeout=25000)
        await B.wait_for_selector('#home:not([hidden])', timeout=15000)
        await B.click('#homeARpc'); await B.fill('#pcAsk', 'zzzzzz'); await B.click('#pcGo'); await B.wait_for_timeout(300)
        check('inconnu' in await B.inner_text('#pcErr'), 'code inconnu refusé')
        await B.fill('#pcAsk', code[:3].lower() + ' ' + code[3:]); await B.click('#pcGo')
        await B.wait_for_selector('#arPc:not([hidden])', timeout=8000); await B.wait_for_timeout(800)
        n = await B.locator('#pcUl button').count(); check(n > 3, f'liste des éléments ({n})')
        await B.locator('#pcUl button').first.click(); await B.wait_for_timeout(300)
        await B.screenshot(path='/tmp/t/pc.png')
        i = int(await B.locator('#pcUl button').first.get_attribute('data-i'))
        p0 = await A.evaluate(f'AVLive.pos({i})')
        x0 = float(await B.input_value('#pcX'))
        await B.fill('#pcX', str(x0 + 1.5)); await B.dispatch_event('#pcX', 'change'); await B.wait_for_timeout(700)
        p1 = await A.evaluate(f'AVLive.pos({i})'); print(p0, p1)
        d = sum((u - v) ** 2 for u, v in zip(p0, p1)) ** .5
        check(abs(d - 1.5) < 0.2, f'l\'élément bouge en direct sur la maquette hôte ({d:.2f} m)')
        await B.screenshot(path='/tmp/t/pc2.png')
        await A.screenshot(path='/tmp/t/host.png')
        print('\n'.join(set(errs)) or 'aucune erreur JS')
        check(not errs, 'aucune exception JS')
        await b.close()
    sys.exit(1 if fails else 0)
asyncio.run(main())
