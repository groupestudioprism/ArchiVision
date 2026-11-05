#!/usr/bin/env python3
"""Clic droit partout et Spectre (Ctrl K). Usage : python3 -m http.server 8810 puis python3 tests/ctx.py"""
import asyncio, sys, os
sys.path.insert(0, __file__.rsplit('/', 1)[0])
from e2e import signup, URL, check, fails
from playwright.async_api import async_playwright
async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch(args=['--use-gl=swiftshader', '--enable-webgl', '--ignore-gpu-blocklist'])
        pg = await (await b.new_context(viewport={'width':1300, 'height':800})).new_page(); errs = []
        pg.on('pageerror', lambda e: errs.append(str(e)))
        THREE = os.environ.get('THREE_DIR', '/tmp/thr/package')
        async def cdn(route):
            f = os.path.join(THREE, route.request.url.split('three@0.160.0/')[1])
            await (route.fulfill(path=f, content_type='text/javascript') if os.path.exists(f) else route.abort())
        await pg.route('https://cdn.jsdelivr.net/npm/three@0.160.0/**', cdn)
        await pg.goto(URL); await pg.wait_for_selector('#ap-email', timeout=25000); await pg.wait_for_function('window.__apReady === true', timeout=25000)
        await pg.keyboard.press('Control+k'); await pg.wait_for_timeout(200)
        check(await pg.evaluate("document.querySelector('#spectre').hidden"), 'Ctrl K inactif sur l\'écran de compte')
        await signup(pg); await pg.wait_for_selector('#home:not([hidden])'); await pg.wait_for_timeout(800)
        # accueil : clic droit
        await pg.click('#homeBody', button='right', position={'x':200, 'y':100}); await pg.wait_for_selector('.ctx')
        check('Importer une maquette' in await pg.inner_text('.ctx'), 'clic droit sur l\'accueil')
        await pg.keyboard.press('Escape'); await pg.wait_for_selector('.ctx', state='detached')
        # Spectre depuis l'accueil → maquette d'exemple
        await pg.keyboard.press('Control+k'); await pg.wait_for_selector('#spectre:not([hidden])')
        await pg.fill('#spQ', 'exemple'); await pg.keyboard.press('Enter')
        await pg.wait_for_selector('#home', state='hidden', timeout=15000); await pg.wait_for_timeout(1200)
        check(await pg.evaluate("AVApp.state().name") == 'Maison d\'exemple', 'Spectre : ouvrir l\'exemple')
        # Spectre : style filaire, outil marche
        await pg.keyboard.press('Control+k'); await pg.fill('#spQ', 'filaire'); await pg.keyboard.press('Enter'); await pg.wait_for_timeout(400)
        check(await pg.evaluate("AVApp.state().style") == 'wire', 'Spectre : style filaire')
        await pg.keyboard.press('Control+k'); await pg.fill('#spQ', 'marche'); await pg.keyboard.press('Enter'); await pg.wait_for_timeout(400)
        check(await pg.evaluate("AVApp.state().tool") == 'walk', 'Spectre : outil Marche')
        await pg.keyboard.press('Control+k'); await pg.fill('#spQ', 'orbite'); await pg.keyboard.press('Enter'); await pg.wait_for_timeout(400)
        # clic droit sur un élément de la vue 3D
        box = await pg.locator('#c').bounding_box()
        await pg.mouse.click(box['x'] + box['width'] * .5, box['y'] + box['height'] * .55, button='right'); await pg.wait_for_selector('.ctx')
        txt = await pg.inner_text('.ctx'); check('Isoler' in txt and 'Centrer la vue' in txt, 'clic droit sur un élément 3D')
        await pg.screenshot(path='/tmp/ctx3d.png')
        await pg.keyboard.press('Escape')
        for sel, word in [('.tools', 'Marche'), ('.vtools', 'Filaire'), ('.top', 'Fichier'), ('.tabs', 'Galerie')]:
            await pg.locator(sel).first.click(button='right'); await pg.wait_for_selector('.ctx')
            check(word in await pg.inner_text('.ctx'), f'clic droit {sel}'); await pg.keyboard.press('Escape'); await pg.wait_for_timeout(100)
        await pg.locator('.tr[data-tk]').nth(2).click(button='right'); await pg.wait_for_selector('.ctx')
        check('Isoler' in await pg.inner_text('.ctx'), 'clic droit sur la hiérarchie'); await pg.keyboard.press('Escape')
        # champ de saisie : menu natif (pas de .ctx)
        await pg.locator('#treeQ').click(button='right'); await pg.wait_for_timeout(200)
        check(await pg.locator('.ctx').count() == 0, 'champ de saisie : menu du navigateur conservé')
        check(not errs, f'aucune erreur {errs}')
        await b.close()
    print('ÉCHECS' if fails else 'TOUT OK'); sys.exit(1 if fails else 0)
asyncio.run(main())
