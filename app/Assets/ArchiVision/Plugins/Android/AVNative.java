// ARCHIVISION · fonctions Android natives appelées depuis Unity (C# : StudioPrism.ArchiVision.Native.AndroidNative).
// Choix de fichiers (Storage Access Framework), enregistrement dans la galerie (MediaStore), partage, empreinte / visage (BiometricPrompt).
package com.studioprism.archivision;

import android.app.Activity;
import android.app.Fragment;
import android.app.FragmentManager;
import android.content.ContentResolver;
import android.content.ContentValues;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.database.Cursor;
import android.hardware.biometrics.BiometricManager;
import android.hardware.biometrics.BiometricPrompt;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.CancellationSignal;
import android.os.Environment;
import android.provider.MediaStore;
import android.provider.OpenableColumns;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.ArrayList;
import java.util.concurrent.Executor;

public class AVNative {
    static final String GO = "AVNative";

    static Activity act() { return com.unity3d.player.UnityPlayer.currentActivity; }
    static void send(String method, String msg) { com.unity3d.player.UnityPlayer.UnitySendMessage(GO, method, msg == null ? "" : msg); }

    static String esc(String s) { return s.replace("\\", "\\\\").replace("\"", "\\\""); }

    // ------------------------------------------------------------ Choix de fichiers
    public static class PickFragment extends Fragment {
        @Override public void onCreate(Bundle b) {
            super.onCreate(b);
            Intent i = new Intent(Intent.ACTION_OPEN_DOCUMENT);
            i.addCategory(Intent.CATEGORY_OPENABLE);
            i.setType("*/*");
            i.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
            try { startActivityForResult(i, 4711); } catch (Exception e) { send("OnFilesPicked", "!" + e.getMessage()); remove(); }
        }
        @Override public void onActivityResult(int req, int res, Intent data) {
            if (req != 4711) return;
            final ArrayList<Uri> uris = new ArrayList<>();
            if (res == Activity.RESULT_OK && data != null) {
                if (data.getClipData() != null) for (int k = 0; k < data.getClipData().getItemCount(); k++) uris.add(data.getClipData().getItemAt(k).getUri());
                else if (data.getData() != null) uris.add(data.getData());
            }
            final Activity a = getActivity();
            remove();
            if (uris.isEmpty()) { send("OnFilesPicked", ""); return; }
            new Thread(() -> {
                StringBuilder sb = new StringBuilder("[");
                try {
                    File dir = new File(a.getCacheDir(), "import"); dir.mkdirs();
                    for (File f : dir.listFiles()) f.delete();
                    ContentResolver cr = a.getContentResolver();
                    for (Uri u : uris) {
                        String name = "fichier";
                        try (Cursor c = cr.query(u, null, null, null, null)) { if (c != null && c.moveToFirst()) { int ix = c.getColumnIndex(OpenableColumns.DISPLAY_NAME); if (ix >= 0) name = c.getString(ix); } }
                        name = name.replaceAll("[\\\\/:*?\"<>|]", "_");
                        File out = new File(dir, name);
                        try (InputStream in = cr.openInputStream(u); OutputStream os = new FileOutputStream(out)) { byte[] buf = new byte[1 << 16]; int n; while ((n = in.read(buf)) > 0) os.write(buf, 0, n); }
                        if (sb.length() > 1) sb.append(',');
                        sb.append('"').append(esc(out.getAbsolutePath())).append('"');
                    }
                    sb.append(']'); send("OnFilesPicked", sb.toString());
                } catch (Exception e) { send("OnFilesPicked", "!" + e.getMessage()); }
            }).start();
        }
        void remove() { try { getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss(); } catch (Exception ignored) { } }
    }

    public static void pickFiles() {
        final Activity a = act();
        a.runOnUiThread(() -> { FragmentManager fm = a.getFragmentManager(); fm.beginTransaction().add(new PickFragment(), "av-pick").commitAllowingStateLoss(); });
    }

    // ------------------------------------------------------------ Galerie du téléphone (Images/ArchiVision) ; renvoie l'URI de contenu
    public static String saveImage(String path, String displayName) {
        try {
            Activity a = act(); ContentResolver cr = a.getContentResolver();
            ContentValues v = new ContentValues();
            v.put(MediaStore.Images.Media.DISPLAY_NAME, displayName);
            v.put(MediaStore.Images.Media.MIME_TYPE, "image/jpeg");
            if (Build.VERSION.SDK_INT >= 29) { v.put(MediaStore.Images.Media.RELATIVE_PATH, Environment.DIRECTORY_PICTURES + "/ArchiVision"); v.put(MediaStore.Images.Media.IS_PENDING, 1); }
            Uri uri = cr.insert(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, v);
            if (uri == null) return "!insert";
            try (InputStream in = new FileInputStream(path); OutputStream os = cr.openOutputStream(uri)) { byte[] buf = new byte[1 << 16]; int n; while ((n = in.read(buf)) > 0) os.write(buf, 0, n); }
            if (Build.VERSION.SDK_INT >= 29) { ContentValues d = new ContentValues(); d.put(MediaStore.Images.Media.IS_PENDING, 0); cr.update(uri, d, null, null); }
            return uri.toString();
        } catch (Exception e) { return "!" + e.getMessage(); }
    }

    public static void share(String contentUri, String mime, String text, String title) {
        final Activity a = act();
        a.runOnUiThread(() -> {
            try {
                Intent i = new Intent(Intent.ACTION_SEND);
                i.setType(mime);
                if (contentUri != null && contentUri.length() > 0) { i.putExtra(Intent.EXTRA_STREAM, Uri.parse(contentUri)); i.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION); }
                if (text != null && text.length() > 0) i.putExtra(Intent.EXTRA_TEXT, text);
                a.startActivity(Intent.createChooser(i, title));
            } catch (Exception e) { send("OnNativeError", "share:" + e.getMessage()); }
        });
    }

    // ------------------------------------------------------------ Empreinte / visage (facultatif)
    public static boolean canBiometric() {
        try {
            if (Build.VERSION.SDK_INT >= 29) { BiometricManager m = act().getSystemService(BiometricManager.class); return m != null && m.canAuthenticate() == BiometricManager.BIOMETRIC_SUCCESS; }
            if (Build.VERSION.SDK_INT >= 28) return act().getPackageManager().hasSystemFeature(PackageManager.FEATURE_FINGERPRINT);
        } catch (Exception ignored) { }
        return false;
    }

    public static void biometric(final String title, final String subtitle) {
        final Activity a = act();
        if (Build.VERSION.SDK_INT < 28) { send("OnBiometric", "unavailable"); return; }
        a.runOnUiThread(() -> {
            try {
                Executor ex = a.getMainExecutor();
                BiometricPrompt p = new BiometricPrompt.Builder(a).setTitle(title).setSubtitle(subtitle)
                    .setNegativeButton("Utiliser le code PIN", ex, (d, w) -> send("OnBiometric", "cancel")).build();
                p.authenticate(new CancellationSignal(), ex, new BiometricPrompt.AuthenticationCallback() {
                    @Override public void onAuthenticationSucceeded(BiometricPrompt.AuthenticationResult r) { send("OnBiometric", "ok"); }
                    @Override public void onAuthenticationError(int code, CharSequence s) { send("OnBiometric", code == BiometricPrompt.BIOMETRIC_ERROR_USER_CANCELED || code == BiometricPrompt.BIOMETRIC_ERROR_CANCELED ? "cancel" : "error:" + s); }
                });
            } catch (Exception e) { send("OnBiometric", "error:" + e.getMessage()); }
        });
    }
}
