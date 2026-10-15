// ARCHIVISION · conversion IFC → GLB sur le téléphone : une WebView invisible charge le convertisseur web d'ARCHIVISION
// (web-ifc + Three.js, le même code que le site), reçoit l'IFC par morceaux et renvoie le GLB (avec les données BIM dans « extras »).
package com.studioprism.archivision;

import android.app.Activity;
import android.util.Base64;
import android.view.ViewGroup;
import android.webkit.JavascriptInterface;
import android.webkit.WebChromeClient;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import java.io.File;
import java.io.FileOutputStream;
import java.io.RandomAccessFile;

public class AVIfc {
    static WebView web;
    static final int CHUNK = 192 * 1024;

    static void send(String m, String s) { com.unity3d.player.UnityPlayer.UnitySendMessage("AVNative", m, s == null ? "" : s); }

    public static class Bridge {
        final String inPath, outPath; final long size; FileOutputStream out;
        Bridge(String in, String outp) { inPath = in; outPath = outp; size = new File(in).length(); }
        @JavascriptInterface public long size() { return size; }
        @JavascriptInterface public String read(int index) {
            try (RandomAccessFile f = new RandomAccessFile(inPath, "r")) {
                long off = (long) index * CHUNK; if (off >= size) return "";
                int n = (int) Math.min(CHUNK, size - off); byte[] b = new byte[n]; f.seek(off); f.readFully(b);
                return Base64.encodeToString(b, Base64.NO_WRAP);
            } catch (Exception e) { return ""; }
        }
        @JavascriptInterface public void write(String b64) {
            try { if (out == null) out = new FileOutputStream(outPath); out.write(Base64.decode(b64, Base64.NO_WRAP)); } catch (Exception e) { fail("écriture : " + e.getMessage()); }
        }
        @JavascriptInterface public void progress(String msg) { send("OnIfcProgress", msg); }
        @JavascriptInterface public void done(String meta) { try { if (out != null) out.close(); } catch (Exception ignored) { } send("OnIfcDone", outPath + "|" + meta); close(); }
        @JavascriptInterface public void fail(String msg) { try { if (out != null) out.close(); } catch (Exception ignored) { } send("OnIfcDone", "!" + msg); close(); }
    }

    static void close() {
        final Activity a = com.unity3d.player.UnityPlayer.currentActivity;
        a.runOnUiThread(() -> { if (web != null) { try { ((ViewGroup) web.getParent()).removeView(web); web.destroy(); } catch (Exception ignored) { } web = null; } });
    }

    public static void convert(final String inPath, final String outPath, final String pageUrl) {
        final Activity a = com.unity3d.player.UnityPlayer.currentActivity;
        a.runOnUiThread(() -> {
            try {
                if (web != null) { try { ((ViewGroup) web.getParent()).removeView(web); web.destroy(); } catch (Exception ignored) { } }
                web = new WebView(a);
                WebSettings s = web.getSettings(); s.setJavaScriptEnabled(true); s.setDomStorageEnabled(true); s.setCacheMode(WebSettings.LOAD_DEFAULT);
                web.addJavascriptInterface(new Bridge(inPath, outPath), "AVIfcBridge");
                web.setWebChromeClient(new WebChromeClient());
                web.setWebViewClient(new WebViewClient() {
                    @Override public void onReceivedError(WebView v, int code, String desc, String url) { send("OnIfcDone", "!Convertisseur IFC injoignable (" + desc + "). Vérifiez la connexion."); close(); }
                });
                web.setAlpha(0f);
                a.addContentView(web, new ViewGroup.LayoutParams(4, 4));
                web.loadUrl(pageUrl);
            } catch (Exception e) { send("OnIfcDone", "!" + e.getMessage()); }
        });
    }
}
