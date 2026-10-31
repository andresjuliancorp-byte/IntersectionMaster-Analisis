using System;
using System.IO;
using UnityEngine;
using IntersectionMaster.Systems;

namespace IntersectionMaster.UI
{
    /// <summary>
    /// Abre el selector de archivos de Android para importar mapas (.trzmap de Intersection Controller o .imap.json
    /// propios) desde Descargas, Drive, WhatsApp… sin tener que entrar en la carpeta privada del juego.
    /// </summary>
    public class FilePicker : MonoBehaviour
    {
        static FilePicker instance;
        Action<string> done;

        public static bool Available { get { return Application.platform == RuntimePlatform.Android; } }

        /// <summary>Pide archivos; al volver llama a onDone con el mensaje para el jugador.</summary>
        public static void PickAndImport(Action<string> onDone)
        {
            if (instance == null)
            {
                var go = new GameObject("IMFilePicker");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<FilePicker>();
            }
            instance.done = onDone;
            try
            {
                using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var picker = new AndroidJavaClass("com.intersectionmaster.IMFilePicker"))
                    picker.CallStatic("pick", act, "IMFilePicker", "OnPicked");
            }
            catch (Exception ex)
            {
                onDone("No se pudo abrir el selector de archivos: " + ex.Message);
            }
        }

        /// <summary>Lo llama Android (UnitySendMessage) con las rutas de las copias, una por línea.</summary>
        public void OnPicked(string msg)
        {
            var cb = done;
            done = null;
            if (cb == null) return;
            if (string.IsNullOrEmpty(msg)) { cb("No se eligió ningún archivo."); return; }
            if (msg.StartsWith("ERROR:")) { cb("No se pudo leer el archivo: " + msg.Substring(6)); return; }
            int copied = 0;
            foreach (var path in msg.Split('\n'))
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    byte[] data = File.ReadAllBytes(path);
                    string baseName = Path.GetFileName(path);
                    // Por el contenido: comprimido con gzip → mapa de Intersection Controller; si no, mapa propio.
                    string ext = TrzImporter.LooksLikeTrz(data) ? ".trzmap" : ".imap.json";
                    string name = Path.GetFileNameWithoutExtension(baseName.Replace(".imap.json", "").Replace(".trzmap", ""));
                    // La copia de Android lleva delante «<número>_» (para no pisar dos archivos con el mismo nombre).
                    int us = name.IndexOf('_');
                    if (us > 0 && us < name.Length - 1 && System.Text.RegularExpressions.Regex.IsMatch(name.Substring(0, us), "^[0-9]+$")) name = name.Substring(us + 1);
                    string dest = Path.Combine(SaveSystem.ImportsDir, name + ext);
                    for (int k = 2; File.Exists(dest); k++) dest = Path.Combine(SaveSystem.ImportsDir, name + " " + k + ext);
                    File.WriteAllBytes(dest, data);
                    File.Delete(path);
                    copied++;
                }
                catch (Exception ex) { Debug.LogWarning("Importar " + path + ": " + ex.Message); }
            }
            cb(copied == 0 ? "No se pudo copiar ningún archivo." : SaveSystem.ImportAll());
        }
    }
}
