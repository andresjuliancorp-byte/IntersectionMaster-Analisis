using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using IntersectionMaster.Data;

namespace IntersectionMaster.Systems
{
    public class MapInfo
    {
        public string id, name, author, description, thumbnail, modifiedUtc;
        public float width, height;
        public int difficulty;
        public bool isSample;
        public string error;   // no se pudo leer: se muestra en la lista con el motivo (no desaparece)
    }

    [Serializable]
    public class LevelResult
    {
        public string id;
        public int stars;
        public int bestScore;
    }

    /// <summary>Una puntuación del ranking local. «key» identifica el nivel o mapa.</summary>
    [Serializable]
    public class ScoreEntry
    {
        public string key;
        public int score;
        public float seconds;
    }

    [Serializable]
    public class ProgressData
    {
        public List<ScoreEntry> scores = new List<ScoreEntry>();
        public List<LevelResult> levels = new List<LevelResult>();
        public int bestInfiniteScore;
        public float bestInfiniteSeconds;
        public bool soundOn = true;
        public bool batterySaver;
        public bool tutorialSeen;
        public bool hintsEnabled = true;                       // tutoriales en contexto (Ajustes)
        public List<string> hintsSeen = new List<string>();   // los que ya se mostraron
        public bool mph;                                       // unidades: millas por hora en vez de km/h
        public int autosaveMinutes = 5;   // editor: guardar solo cada N minutos (0 = no)
        public bool showLightZone;   // al jugar: ver la zona de toque de cada semáforo
        public bool redOnCrash;    // como el original: todas las luces a rojo cuando hay un accidente
        public bool roadGrain;          // grano (textura) en el asfalto (por defecto no: asfalto normal, liso)
        public int asphaltDefaults;     // 1 = ya se pasó al asfalto normal por defecto (sin grano)
        public int asphaltColor;        // color del asfalto al jugar (MapRenderer.AsphaltNames; 0 = gris claro)
        public float grainAmount = 1f;  // fuerza del grano (0,3 a 2)
        public bool menuBackground = true;   // mapa con tráfico detrás del menú principal
        public bool colorAssist;   // accesibilidad: semáforos con forma distinta además del color
        public bool ambientOn = true;      // sonido ambiente de ciudad
        public bool musicOn;               // música relajante (generada por código)
        public float uiScale = 1f;         // Tamaño del menú (0,5 a 2,5)
        public string theme = "dia";       // tema visual elegido (ver RewardSystem)
        public string livery = "clasico";  // colores de coches elegidos (ver RewardSystem)
    }

    /// <summary>Guardado local en Application.persistentDataPath: mapas del jugador y progreso.</summary>
    public static class SaveSystem
    {
        const string Ext = ".imap.json";
        static ProgressData progress;

        public static string MapsDir { get { return Dir("maps"); } }
        public static string ExportsDir { get { return Dir("exports"); } }
        public static string ImportsDir { get { return Dir("imports"); } }

        static string Dir(string sub)
        {
            string p = Path.Combine(Application.persistentDataPath, sub);
            Directory.CreateDirectory(p);
            return p;
        }

        static string PathFor(string id)
        {
            return Path.Combine(MapsDir, id + Ext);
        }

        static string SafeFileName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }

        // ---------------- Mapas ----------------

        public static List<MapInfo> ListSamples()
        {
            var list = new List<MapInfo>();
            foreach (var m in SampleMaps.All()) list.Add(Info(m, true));
            return list;
        }

        public static List<MapInfo> ListSavedMaps()
        {
            var list = new List<MapInfo>();
            string[] files;
            try { files = Directory.GetFiles(MapsDir, "*" + Ext); }
            catch (Exception ex) { Debug.LogWarning("No se pudo listar los mapas: " + ex.Message); return list; }
            // Cada mapa por separado: si uno falla no esconde a los demás, y aparece en la lista con el motivo.
            foreach (var f in files)
            {
                string fname = Path.GetFileName(f);
                string id = fname.Substring(0, fname.Length - Ext.Length);
                string err;
                try
                {
                    MapData m;
                    if (MapSerializationSystem.TryFromJson(File.ReadAllText(f), out m, out err)) { list.Add(Info(m, false)); continue; }
                }
                catch (Exception ex) { err = ex.GetType().Name + ": " + ex.Message; }
                Debug.LogWarning("No se pudo leer el mapa " + fname + ": " + err);
                string when = "";
                try { when = File.GetLastWriteTimeUtc(f).ToString("o"); } catch (Exception) { }
                list.Add(new MapInfo { id = id, name = "Mapa que no se pudo leer", author = "", description = "", modifiedUtc = when, error = string.IsNullOrEmpty(err) ? "error desconocido" : err });
            }
            list.Sort((a, b) => string.CompareOrdinal(b.modifiedUtc ?? "", a.modifiedUtc ?? ""));
            return list;
        }

        static MapInfo Info(MapData m, bool sample)
        {
            return new MapInfo
            {
                id = m.id, name = m.name, author = m.author, description = m.description, thumbnail = m.thumbnailBase64,
                modifiedUtc = m.modifiedUtc ?? "", width = m.width, height = m.height, difficulty = m.difficulty, isSample = sample
            };
        }

        public static MapData Load(string id)
        {
            var sample = SampleMaps.Find(id);
            if (sample != null) return sample;
            string path = PathFor(id);
            if (!File.Exists(path)) return null;
            MapData m; string err;
            if (MapSerializationSystem.TryFromJson(File.ReadAllText(path), out m, out err)) return m;
            Debug.LogWarning("Mapa dañado " + id + ": " + err);
            return null;
        }

        public static bool Save(MapData m, out string error)
        {
            error = "";
            try
            {
                if (string.IsNullOrEmpty(m.id) || SampleMaps.IsSample(m.id)) m.id = MapData.NewId();
                m.thumbnailBase64 = MapSerializationSystem.MakeThumbnail(m);
                m.Touch();
                string path = PathFor(m.id);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, MapSerializationSystem.ToJson(m, true));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                return true;
            }
            catch (Exception ex)
            {
                error = "No se pudo guardar: " + ex.Message;
                return false;
            }
        }

        public static bool Delete(string id)
        {
            try
            {
                string path = PathFor(id);
                if (File.Exists(path)) { File.Delete(path); return true; }
            }
            catch (Exception ex) { Debug.LogWarning(ex.Message); }
            return false;
        }

        public static MapData Duplicate(string id)
        {
            var m = Load(id);
            if (m == null) return null;
            m = m.Clone();
            m.id = MapData.NewId();
            m.name = (m.name.Length > 32 ? m.name.Substring(0, 32) : m.name) + " (copia)";
            m.author = string.IsNullOrEmpty(m.author) ? "Jugador" : m.author;
            string err;
            return Save(m, out err) ? m : null;
        }

        /// <summary>Escribe el mapa en la carpeta de exportación y devuelve la ruta (o null).</summary>
        public static string Export(string id, out string error)
        {
            error = "";
            var m = Load(id);
            if (m == null) { error = "No se encontró el mapa."; return null; }
            try
            {
                string path = Path.Combine(ExportsDir, SafeFileName(m.name) + Ext);
                File.WriteAllText(path, MapSerializationSystem.ToJson(m, true));
                return path;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        /// <summary>Importa los archivos .imap.json y .trzmap de la carpeta de importación (validándolos antes).
        /// Cada importación crea un mapa nuevo (con fecha y hora en el nombre), aunque el archivo ya se haya importado.</summary>
        public static string ImportAll()
        {
            int ok = 0, bad = 0;
            string lastError = "";
            var names = new List<string>();
            string stamp = DateTime.Now.ToString("dd/MM HH:mm");
            string[] files;
            try
            {
                var all = new List<string>(Directory.GetFiles(ImportsDir, "*" + Ext));
                all.AddRange(Directory.GetFiles(ImportsDir, "*.trzmap"));
                files = all.ToArray();
            }
            catch (Exception ex) { return "Error al importar: " + ex.Message; }

            foreach (var f in files)
            {
                string fname = Path.GetFileName(f);
                try
                {
                    MapData m; string err;
                    bool trz = f.EndsWith(".trzmap", StringComparison.OrdinalIgnoreCase);
                    string baseName = trz ? Path.GetFileNameWithoutExtension(f) : fname.Substring(0, fname.Length - Ext.Length);
                    bool converted = trz ? TrzImporter.TryConvert(File.ReadAllBytes(f), baseName, out m, out err)
                                         : MapSerializationSystem.TryFromJson(File.ReadAllText(f), out m, out err);
                    if (!converted) { bad++; lastError = fname + ": " + err; continue; }
                    m.id = MapData.NewId();
                    if (trz || string.IsNullOrEmpty(m.name)) m.name = (string.IsNullOrEmpty(baseName) ? "Mapa importado" : baseName) + " (" + stamp + ")";
                    if (!Save(m, out err)) { bad++; lastError = fname + ": " + err; continue; }
                    ok++;
                    names.Add("«" + m.name + "»");
                }
                catch (Exception ex) { bad++; lastError = fname + ": " + ex.Message; }
                // El archivo ya se usó: se guarda como .importado (reemplazando el de una importación anterior).
                try
                {
                    string done = f + ".importado";
                    if (File.Exists(done)) File.Delete(done);
                    File.Move(f, done);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("No se pudo apartar " + fname + ": " + ex.Message);
                    try { File.Delete(f); } catch (Exception) { }
                }
            }

            if (ok == 0 && bad == 0) return "No hay archivos. Copia archivos " + Ext + " o .trzmap (de Intersection Controller) a: " + ImportsDir;
            string msg = ok == 0 ? "No se importó ningún mapa." : (ok == 1 ? "Mapa importado: " : ok + " mapas importados: ") + string.Join(", ", names.ToArray()) + ".";
            if (bad > 0) msg += "\n" + bad + " con error: " + lastError;
            return msg;
        }

        // ---------------- Progreso y opciones ----------------

        public static ProgressData Progress
        {
            get
            {
                if (progress == null)
                {
                    progress = new ProgressData();
                    try
                    {
                        string path = Path.Combine(Application.persistentDataPath, "progress.json");
                        if (File.Exists(path)) progress = JsonUtility.FromJson<ProgressData>(File.ReadAllText(path)) ?? new ProgressData();
                    }
                    catch (Exception ex) { Debug.LogWarning("Progreso no legible: " + ex.Message); }
                    if (progress.levels == null) progress.levels = new List<LevelResult>();
                    if (string.IsNullOrEmpty(progress.theme)) progress.theme = "dia";
                    if (string.IsNullOrEmpty(progress.livery)) progress.livery = "clasico";
                    // Una sola vez: los carriles vuelven al asfalto normal (gris claro, sin grano); se cambia en Opciones.
                    if (progress.asphaltDefaults < 1)
                    {
                        progress.asphaltDefaults = 1;
                        progress.roadGrain = false;
                        progress.asphaltColor = 0;
                        progress.grainAmount = 1f;
                    }
                }
                return progress;
            }
        }

        public static void SaveProgress()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "progress.json");
                File.WriteAllText(path, JsonUtility.ToJson(Progress, true));
            }
            catch (Exception ex) { Debug.LogWarning("No se pudo guardar el progreso: " + ex.Message); }
        }

        public static void AddScore(string key, int score, float seconds)
        {
            if (string.IsNullOrEmpty(key) || score <= 0) return;
            Progress.scores.Add(new ScoreEntry { key = key, score = score, seconds = seconds });
            // Se conservan las 10 mejores de cada clave.
            var mine = Progress.scores.FindAll(e => e.key == key);
            mine.Sort((a, b) => b.score.CompareTo(a.score));
            for (int i = 10; i < mine.Count; i++) Progress.scores.Remove(mine[i]);
            SaveProgress();
        }

        public static List<ScoreEntry> TopScores(string key)
        {
            var list = Progress.scores.FindAll(e => e.key == key);
            list.Sort((a, b) => b.score.CompareTo(a.score));
            return list;
        }

        public static int Stars(string levelId)
        {
            foreach (var r in Progress.levels) if (r.id == levelId) return r.stars;
            return 0;
        }

        public static void RecordLevel(string levelId, int stars, int score)
        {
            // Del reto del día sólo se guarda el de hoy.
            if (levelId.StartsWith("daily_")) Progress.levels.RemoveAll(r => r.id.StartsWith("daily_") && r.id != levelId);
            LevelResult res = null;
            foreach (var r in Progress.levels) if (r.id == levelId) { res = r; break; }
            if (res == null) { res = new LevelResult { id = levelId }; Progress.levels.Add(res); }
            res.stars = Mathf.Max(res.stars, stars);
            res.bestScore = Mathf.Max(res.bestScore, score);
            SaveProgress();
        }
    }
}
