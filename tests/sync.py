#!/usr/bin/env python3
"""Test de la synchronisation (transport Firestore/Storage simulé en mémoire, deux « appareils » = deux contextes partageant le même faux serveur via le test).
Usage : python3 -m http.server 8810, puis python3 tests/sync.py"""
import asyncio, sys
sys.path.insert(0, __file__.rsplit('/', 1)[0])
from e2e import signup, URL, check, fails
from playwright.async_api import async_playwright
FAKE = """
window.__srv = window.__srv || {docs:{avModels:{}, avPhotos:{}}, blobs:{}, subs:[]};
window.__mk = () => ({
  listen(col, u, cb){ const s = {col, cb}; __srv.subs.push(s); setTimeout(() => cb(Object.entries(__srv.docs[col]).map(([id, d]) => ({id, ...d}))), 0); return () => {}; },
  async setDoc(col, id, d){ __srv.docs[col][id] = d; __srv.subs.filter(s => s.col === col).forEach(s => s.cb(Object.entries(__srv.docs[col]).map(([i, x]) => ({id:i, ...x})))); },
  async delDoc(col, id){ delete __srv.docs[col][id]; },
  async putBlob(p, b){ __srv.blobs[p] = b; }, async getBlob(p){ return __srv.blobs[p]; }, async delBlob(p){ delete __srv.blobs[p]; },
});
"""
async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch(); ctx = await b.new_context(); pg = await ctx.new_page(); errs = []
        pg.on('pageerror', lambda e: errs.append(str(e)))
        await pg.goto(URL); await pg.wait_for_selector('#ap-email', timeout=25000); await signup(pg)
        await pg.wait_for_selector('#home:not([hidden])', timeout=10000)
        await pg.evaluate(FAKE)
        uid = await pg.evaluate('AVUser().uid')
        # appareil A : crée une maquette + une photo, synchronise
        await pg.evaluate("""async uid => { AVSync.start(__mk());
          await AVStore.putModel({id:'m_t1', uid, name:'Test', kind:'glb', size:5, glb:new Blob(['glbdata']), createdAt:Date.now(), openedAt:Date.now(), unitScale:1});
          await AVStore.putPhoto({id:'p_t1', uid, modelId:'m_t1', modelName:'Test', blob:new Blob(['img']), size:3, w:1, h:1, thumb:'', note:'', mode:'3d', createdAt:Date.now()}); }""", uid)
        await pg.wait_for_function("Object.keys(__srv.docs.avModels).length === 1 && Object.keys(__srv.docs.avPhotos).length === 1 && AVSync.S.state === 'ok'", timeout=8000)
        check(await pg.evaluate("Object.keys(__srv.blobs).length") == 2, 'fichiers envoyés avant/avec les fiches')
        # appareil B : local vide → tout revient
        await pg.evaluate("async () => { AVSync.stop(); await AVStore.deleteModel('m_t1', {silent:true}); await AVStore.deletePhoto('p_t1', {silent:true}); }")
        await pg.evaluate("AVSync.start(__mk())")
        await pg.wait_for_function("AVSync.S.state === 'ok'", timeout=8000)
        got = await pg.evaluate("async () => { const m = await AVStore.getModel('m_t1'), p = await AVStore.getPhoto('p_t1'); return [!!m?.glb, m?.name, !!p?.blob, m?.syncedAt === m?.updatedAt]; }")
        check(got == [True, 'Test', True, True], f'récupération sur un autre appareil {got}')
        # renommage local → poussé
        await pg.evaluate("AVStore.patchModel('m_t1', {name:'Renommé'})")
        await pg.wait_for_function("__srv.docs.avModels.m_t1.name === 'Renommé'", timeout=8000)
        check(True, 'renommage synchronisé')
        # suppression ici → disparaît du serveur (fiche + fichiers)
        await pg.evaluate("AVStore.deleteModel('m_t1')")
        await pg.wait_for_function("Object.keys(__srv.docs.avModels).length === 0 && Object.keys(__srv.docs.avPhotos).length === 0 && Object.keys(__srv.blobs).length === 0", timeout=8000)
        check(True, 'suppression synchronisée (fiches et fichiers)')
        # suppression distante → disparaît ici
        await pg.evaluate("""async uid => { await AVStore.putModel({id:'m_t2', uid, name:'Autre', kind:'glb', size:5, glb:new Blob(['x']), createdAt:Date.now(), openedAt:Date.now()}); }""", uid)
        await pg.wait_for_function("Object.keys(__srv.docs.avModels).length === 1", timeout=8000)
        await pg.evaluate("async () => { delete __srv.docs.avModels.m_t2; __srv.subs.at(-2).cb([]); }")
        await pg.wait_for_function("AVStore.getModel('m_t2').then(m => !m)", timeout=8000); await pg.wait_for_timeout(500)
        check(await pg.evaluate("AVStore.getModel('m_t2').then(m => !m)"), 'suppression distante appliquée ici')
        check(not errs, f'aucune erreur page {errs}')
        await b.close()
    print('ÉCHECS' if fails else 'TOUT OK')
    sys.exit(1 if fails else 0)
asyncio.run(main())
