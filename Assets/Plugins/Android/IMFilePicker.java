package com.intersectionmaster;

import android.app.Activity;
import android.app.Fragment;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.OpenableColumns;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;

/**
 * Selector de archivos del sistema (Descargas, Drive, WhatsApp…) para importar mapas. Copia los archivos elegidos a la
 * caché de la app y avisa al juego con UnitySendMessage(objeto, método, rutas separadas por saltos de línea).
 * Código propio; no usa bibliotecas externas.
 */
public class IMFilePicker extends Fragment {
    static final int REQUEST = 4711;
    static String target = "", method = "";

    public static void pick(final Activity activity, String gameObject, String callback) {
        target = gameObject;
        method = callback;
        activity.runOnUiThread(new Runnable() {
            public void run() {
                IMFilePicker f = new IMFilePicker();
                activity.getFragmentManager().beginTransaction().add(f, "imFilePicker").commitAllowingStateLoss();
            }
        });
    }

    @Override
    public void onCreate(Bundle state) {
        super.onCreate(state);
        try {
            Intent i = new Intent(Intent.ACTION_OPEN_DOCUMENT);
            i.addCategory(Intent.CATEGORY_OPENABLE);
            i.setType("*/*");
            i.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
            startActivityForResult(i, REQUEST);
        } catch (Exception e) {
            send("ERROR:" + e.getMessage());
            close();
        }
    }

    @Override
    public void onActivityResult(int requestCode, int resultCode, Intent data) {
        if (requestCode != REQUEST) return;
        StringBuilder paths = new StringBuilder();
        if (resultCode == Activity.RESULT_OK && data != null) {
            try {
                if (data.getClipData() != null) {
                    for (int k = 0; k < data.getClipData().getItemCount(); k++)
                        append(paths, copy(data.getClipData().getItemAt(k).getUri(), k));
                } else if (data.getData() != null) {
                    append(paths, copy(data.getData(), 0));
                }
            } catch (Exception e) {
                send("ERROR:" + e.getMessage());
                close();
                return;
            }
        }
        send(paths.toString());
        close();
    }

    static void append(StringBuilder sb, String p) {
        if (p == null) return;
        if (sb.length() > 0) sb.append('\n');
        sb.append(p);
    }

    String copy(Uri uri, int index) throws Exception {
        Activity a = getActivity();
        String name = "archivo" + index;
        Cursor c = a.getContentResolver().query(uri, null, null, null, null);
        if (c != null) {
            try {
                int col = c.getColumnIndex(OpenableColumns.DISPLAY_NAME);
                if (col >= 0 && c.moveToFirst()) name = c.getString(col);
            } finally { c.close(); }
        }
        name = name.replaceAll("[^A-Za-z0-9._ -]", "_");
        File dir = new File(a.getCacheDir(), "imports");
        dir.mkdirs();
        File out = new File(dir, index + "_" + name);
        InputStream in = a.getContentResolver().openInputStream(uri);
        if (in == null) return null;
        OutputStream os = new FileOutputStream(out);
        try {
            byte[] buf = new byte[16384];
            int n;
            long total = 0;
            while ((n = in.read(buf)) > 0) {
                os.write(buf, 0, n);
                total += n;
                if (total > 40L * 1024 * 1024) throw new Exception("archivo demasiado grande");
            }
        } finally { in.close(); os.close(); }
        return out.getAbsolutePath();
    }

    static void send(String msg) {
        try {
            Class<?> up = Class.forName("com.unity3d.player.UnityPlayer");
            up.getMethod("UnitySendMessage", String.class, String.class, String.class).invoke(null, target, method, msg);
        } catch (Exception ignored) { }
    }

    void close() {
        try { getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss(); } catch (Exception ignored) { }
    }
}
