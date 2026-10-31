using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Data;
using IntersectionMaster.MapEditing;
using IntersectionMaster.Systems;
using IntersectionMaster.Traffic;
using IntersectionMaster.UI;

namespace IntersectionMaster.Core
{
    public enum AppScreen { Main, Modes, Campaign, Challenges, Maps, Options, Rewards, Playing, Editor }

    /// <summary>
    /// Director de la aplicación: conecta simulación, renderizado, editor, niveles, puntuación y guardado.
    /// La interfaz (MobileUIController) sólo lee su estado y llama a sus métodos públicos.
    /// </summary>
    public partial class GameController : MonoBehaviour
    {
        public CameraController cam;
        public MapRenderer mapRenderer;
        public TrafficSimulationManager sim;
        public MapEditorController editor;
        public MobileUIController ui;

        public readonly LevelManager levels = new LevelManager();

        public AppScreen Screen { get; private set; }
        public bool Ended { get; private set; }
        public bool PlayingFromEditor { get; private set; }
        public IntersectionController Selected { get; private set; }
        public int PhaseChanges { get; private set; }
        public int LastStars { get; private set; }
        public bool LastWasRecord { get; private set; }
        /// <summary>Recompensas que se han desbloqueado al terminar la última partida.</summary>
        public System.Collections.Generic.List<string> LastUnlocked { get; private set; }
        public MapData PlayMap { get; private set; }

        // Tutorial interactivo
        public bool TutorialActive { get; private set; }
        public int TutorialStep { get; private set; }

        public static readonly string[] TutorialTexts =
        {
            "1/4  Toca un semáforo (la franja de la línea de parada) para cambiarlo. Solo avanzan los coches que lo tienen en verde.",
            "2/4  Cambia varios semáforos: da paso a la dirección donde se forme más cola.",
            "3/4  Deja pasar 3 vehículos hasta su salida. No cambies la luz con el cruce lleno, o habrá choques.",
            "4/4  Usa los botones de arriba (II pausa, > normal, >> y >>> rápido). Cuando estés listo, pulsa «Terminar»."
        };

        bool tapCandidate;
        Vector2 tapDownPos;
        float tapDownTime;
        string lastMessage = "";
        float messageTime = -100f;

        public string Message { get { return Time.unscaledTime - messageTime < 3f ? lastMessage : ""; } }

        void Start()
        {
            ApplyOptions();
            sim.OnMessage = null;   // como el original: sin avisos de texto al jugar
            cam.IsOverUi = ui.IsOverUi;
            editor.IsOverUi = ui.IsOverUi;
            GoTo(AppScreen.Main);
        }

        public void ShowMessage(string text)
        {
            lastMessage = text;
            messageTime = Time.unscaledTime;
        }

        // ---------------- Opciones ----------------

        public void ApplyOptions()
        {
            var p = SaveSystem.Progress;
            AudioManager.Instance.Muted = !p.soundOn;
            AudioManager.Instance.AmbientEnabled = p.ambientOn;
            AudioManager.Instance.MusicEnabled = p.musicOn;
            if (ui != null) ui.ApplyUiScale(p.uiScale);
            Application.targetFrameRate = p.batterySaver ? 30 : 60;
            QualitySettings.vSyncCount = 0;
        }

        // ---------------- Navegación ----------------

        public void GoTo(AppScreen s)
        {
            if (Screen == AppScreen.Playing && s != AppScreen.Playing) StopPlay();
            if (Screen == AppScreen.Editor && s != AppScreen.Editor && s != AppScreen.Playing)
            {
                editor.End();
                mapRenderer.Clear();
            }
            Screen = s;
            if (s != AppScreen.Playing && s != AppScreen.Editor)
            {
                cam.InputEnabled = false;
                // Como el original: detrás del menú principal se ve un mapa con tráfico (de la campaña de simulación).
                if (s == AppScreen.Main && SaveSystem.Progress.menuBackground) StartBackground();
                else { StopBackground(); if (mapRenderer != null) mapRenderer.Clear(); }
            }
            else BackgroundRunning = false;
            ui.OnScreenChanged();
        }

        /// <summary>Hay un mapa de fondo moviéndose detrás del menú.</summary>
        public bool BackgroundRunning { get; private set; }

        void StartBackground()
        {
            if (BackgroundRunning && sim.Running) return;
            try
            {
                int world = UnityEngine.Random.value < 0.5f ? 0 : 4;
                var map = CampaignMaps.Build(world, UnityEngine.Random.Range(0, CampaignMaps.LevelsPerWorld));
                if (map == null) return;
                map.Normalize();
                map.events = 0;
                map.traffic.pedestriansPerMinute = Mathf.Max(map.traffic.pedestriansPerMinute, 4f);
                // Uno de cada doce, de noche (como uno de los mapas de fondo del original).
                map.startHour = UnityEngine.Random.value < 1f / 12f ? 20.75f : 8f;
                mapRenderer.Build(map, false);
                sim.Begin(map, false);
                mapRenderer.BuildSignals(sim.Intersections);
                foreach (var ic in sim.Intersections) ic.light.autoCycle = true;
                cam.FitRect(map.View);
                BackgroundRunning = true;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("Fondo del menú: " + ex.Message);
                BackgroundRunning = false;
            }
        }

        void StopBackground()
        {
            if (!BackgroundRunning) return;
            BackgroundRunning = false;
            sim.Stop();
        }

        // ---------------- Partidas ----------------

        public void StartLevel(LevelDefinition level)
        {
            var map = level.BuildMap();
            if (map == null) { ShowMessage("Mapa del nivel no disponible."); return; }
            StartPlay(map, level.mode, level, level.objective, false);
        }

        /// <summary>Juega el mapa de un nivel en otro modo (sin fin o puntuación más alta), sin objetivo.</summary>
        public void StartLevelMode(LevelDefinition level, GameMode mode)
        {
            var map = level.BuildMap();
            if (map == null) { ShowMessage("Mapa del nivel no disponible."); return; }
            map.objective = new ObjectiveData();
            if (mode == GameMode.Free)
            {
                // Sin límite: sin tiempo ni final por choques y con conductores más temerarios (como el original).
                map.traffic.simMode = 1;
                map.traffic.chaosLevel = 0;
                if (map.playMode == 1) map.playMode = 0;
            }
            StartPlay(map, mode, level, new ObjectiveData(), false);
        }

        public void StartMapPlay(MapData map, GameMode mode)
        {
            var errors = MapValidator.Validate(map);
            if (MapValidator.HasErrors(errors))
            {
                var first = MapValidator.FirstError(errors);
                ShowMessage("No se puede simular todavía: " + (first != null ? first.message : "el mapa tiene errores.") + " Ábrelo en el editor para corregirlo; el mapa no se ha perdido.");
                return;
            }
            ObjectiveData obj = map.objective;
            mode = ModeFor(map, mode, ref obj);
            StartPlay(map, mode, null, obj, false);
        }

        /// <summary>Modo de juego del mapa (Ajustes de mapa → Modo de juego): el Controlador de tráfico tiene objetivo y tiempo.</summary>
        static GameMode ModeFor(MapData map, GameMode mode, ref ObjectiveData obj)
        {
            if (map == null || !map.IsLaneMap || map.playMode != 2) return mode;
            obj = new ObjectiveData { targetDelivered = map.goalVehicles, timeLimitSeconds = map.goalMinutes * 60f, maxAccidents = 3 };
            return GameMode.Challenge;
        }

        public void StartTutorial()
        {
            var map = SampleMaps.Find(SampleMaps.CrossX);
            map.traffic.spawnPerMinute = 14f;
            map.traffic.emergencyChance = 0f;
            StartPlay(map, GameMode.Free, null, new ObjectiveData(), false);
            TutorialActive = true;
            TutorialStep = 0;
        }

        /// <summary>Prueba el mapa del editor. Devuelve false (y deja el motivo en el editor) si no es válido.</summary>
        public bool TestEditorMap(int simMode = -1, int chaosLevel = 1)
        {
            if (editor.Map == null) return false;
            if (!editor.Validate())
            {
                // El mapa sigue abierto y editable: solo se explica qué impide simular.
                ShowMessage("No se puede simular todavía: " + editor.FirstBlockingMessage());
                return false;
            }
            var copy = editor.Map.Clone();
            if (simMode >= 0) { copy.traffic.simMode = simMode; copy.traffic.chaosLevel = chaosLevel; }
            editor.End();
            var obj = new ObjectiveData();
            var mode = ModeFor(copy, GameMode.Test, ref obj);
            StartPlay(copy, mode, null, obj, true);
            return true;
        }

        void StartPlay(MapData map, GameMode mode, LevelDefinition level, ObjectiveData objective, bool fromEditor)
        {
            BackgroundRunning = false;
            StopPlay();
            PlayMap = map;
            PlayingFromEditor = fromEditor;
            Ended = false;
            TutorialActive = false;
            PhaseChanges = 0;
            Selected = null;
            LastStars = 0;
            LastWasRecord = false;
            LastUnlocked = new System.Collections.Generic.List<string>();
            lastMessage = "";

            mapRenderer.Build(map, false);
            sim.Begin(map, mode == GameMode.Infinite);
            mapRenderer.BuildSignals(sim.Intersections);
            if (map.IsLaneMap && map.playMode == 1)
                foreach (var ic in sim.Intersections) ic.light.autoCycle = true;   // Simulación: los semáforos van solos
            sim.SetRain(map.IsLaneMap && UnityEngine.Random.value < map.rainChance);
            cam.FitRect(map.View);
            cam.AllowSingleFingerPan = true;
            cam.InputEnabled = true;
            levels.Begin(mode, level, objective);
            AudioManager.Instance.StartAmbient();

            if (sim.Intersections.Count == 1) Select(sim.Intersections[0]);
            Screen = AppScreen.Playing;
            ui.OnScreenChanged();
        }

        /// <summary>Vuelve a encuadrar la cámara (al girar la pantalla).</summary>
        public void RefitCamera()
        {
            if (Screen == AppScreen.Playing && PlayMap != null) cam.FitRect(PlayMap.View);
            else if (Screen == AppScreen.Editor && editor.Map != null) editor.FitCamera();
        }

        public void Restart()
        {
            if (PlayMap == null) return;
            var mode = levels.Mode;
            var level = levels.Level;
            var objective = levels.Objective;
            bool tutorial = TutorialActive;
            MapData fresh;
            if (level != null) fresh = level.BuildMap();
            else fresh = PlayMap.Clone();
            if (fresh == null) return;
            StartPlay(fresh, mode, level, objective, PlayingFromEditor);
            if (tutorial) { TutorialActive = true; TutorialStep = 0; }
        }

        void StopPlay()
        {
            ResetPlayTools();
            sim.Stop();
            AudioManager.Instance.StopAmbient();
            Selected = null;
            TutorialActive = false;
        }

        /// <summary>Sale de la partida: vuelve al editor si era una prueba, o al menú de modos.</summary>
        public void ExitPlay()
        {
            bool fromEditor = PlayingFromEditor;
            StopPlay();
            cam.AllowSingleFingerPan = true;
            if (fromEditor)
            {
                PlayingFromEditor = false;
                Screen = AppScreen.Editor;
                try { editor.Resume(); }
                catch (System.Exception ex) { Debug.LogWarning("Volver al editor: " + ex); ShowMessage("Hubo un problema al volver al editor: " + ex.Message); }
                finally { ui.OnScreenChanged(); }   // aunque algo falle, la pantalla pasa al editor
            }
            else
            {
                PlayingFromEditor = false;
                GoTo(AppScreen.Modes);
            }
        }

        /// <summary>Como el original: al jugar uno de tus mapas (no un nivel), «Construir» abre ese mapa en el editor.</summary>
        public bool CanBuildPlayMap { get { return PlayingFromEditor || (PlayMap != null && levels.Level == null && !TutorialActive && !string.IsNullOrEmpty(PlayMap.id)); } }

        public void BuildPlayMap()
        {
            if (PlayingFromEditor) { ExitPlay(); return; }
            MapData saved = null;
            try { saved = SaveSystem.Load(PlayMap.id); } catch (System.Exception ex) { Debug.LogWarning(ex); }
            if (saved == null) { ShowMessage("Este mapa no está en Mis mapas: guárdalo primero para poder editarlo."); return; }
            OpenEditor(saved, false);
        }

        public void FinishTutorial()
        {
            SaveSystem.Progress.tutorialSeen = true;
            SaveSystem.SaveProgress();
            ExitPlay();
        }

        // ---------------- Control de cruces ----------------

        public void Select(IntersectionController ic)
        {
            Selected = ic;
            mapRenderer.Select(ic);
        }

        /// <summary>Cambia un semáforo concreto (el que se ha tocado).</summary>
        public void ToggleSignal(IntersectionController ic, Approach app)
        {
            if (ic == null || app == null || Ended) return;
            ic.light.Toggle(app);
            PhaseChanges++;
            AudioManager.Instance.PlayClick();
        }

        /// <summary>Botón de grupo: si algún semáforo vinculado está en rojo los pone todos en verde; si no, todos en rojo.</summary>
        public void PressButton(GroupButton b)
        {
            if (b == null || Ended || sim == null || sim.Network == null) return;
            var targets = new List<KeyValuePair<IntersectionController, Approach>>();
            bool anyOff = false;
            foreach (var l in b.links)
            {
                NetNode nn;
                if (!sim.Network.nodeById.TryGetValue(l.node, out nn) || nn.controller == null || !nn.controller.IsLit) continue;
                foreach (var ap in nn.approaches)
                {
                    bool linked = ap.road != null && ap.road.data.id == l.road;
                    if (!linked && sim.Network.LaneMode) foreach (var il in ap.inLanes) if (il.roadId == l.road) { linked = true; break; }
                    if (!linked || !ap.hasLight || !nn.controller.light.Has(ap)) continue;
                    targets.Add(new KeyValuePair<IntersectionController, Approach>(nn.controller, ap));
                    if (!nn.controller.light.IsOn(ap)) anyOff = true;
                }
            }
            if (targets.Count == 0) { ShowMessage("Este botón no tiene semáforos vinculados."); return; }
            foreach (var t in targets)
            {
                if (anyOff) t.Key.light.RequestGreen(t.Value);
                else t.Key.light.RequestRed(t.Value);
            }
            PhaseChanges++;
            AudioManager.Instance.PlayClick();
        }

        public void SetAutoCycle(bool on)
        {
            for (int i = 0; i < sim.Intersections.Count; i++) sim.Intersections[i].light.autoCycle = on;
        }

        public bool AnyAutoCycle
        {
            get
            {
                for (int i = 0; i < sim.Intersections.Count; i++) if (sim.Intersections[i].light.autoCycle) return true;
                return false;
            }
        }

        // ---------------- Bucle ----------------

        void Update()
        {
            if (Screen != AppScreen.Playing || !sim.Running) return;

            HandleTap();

            if (TutorialActive) UpdateTutorial();

            if (!Ended)
            {
                levels.Evaluate(sim.Scoring);
                if (levels.State != GameState.Running) FinishRound();
            }
        }

        // Como el original: deslizar el dedo sobre varios semáforos los cambia todos (cada uno una vez por pasada).
        bool swiping;
        readonly HashSet<Approach> swiped = new HashSet<Approach>();

        void HandleTap()
        {
            if (Ended) { EndSwipe(); return; }
            if (PointerInput.MultiTouch) { tapCandidate = false; EndSwipe(); return; }
            if (swiping)
            {
                if (PointerInput.Up || !PointerInput.Held) { EndSwipe(); tapCandidate = false; return; }
                Vector2 wp = cam.ScreenToWorld(PointerInput.Position);
                float sl = 10f * 2f * cam.cam.orthographicSize / Mathf.Max(1, UnityEngine.Screen.height);
                IntersectionController wic; Approach wapp;
                if (mapRenderer.FindSignalAt(wp, sl, out wic, out wapp) && swiped.Add(wapp)) ToggleSignal(wic, wapp);
                return;
            }
            if (PointerInput.Down && Tool == PlayTool.None && !ui.IsOverUi(PointerInput.Position))
            {
                Vector2 wp = cam.ScreenToWorld(PointerInput.Position);
                float sl = 14f * 2f * cam.cam.orthographicSize / Mathf.Max(1, UnityEngine.Screen.height);
                IntersectionController wic; Approach wapp;
                if (mapRenderer.FindSignalAt(wp, sl, out wic, out wapp))
                {
                    // Empieza sobre un semáforo: lo cambia ya y, si el dedo sigue a otros, también los cambia (el mapa no se mueve).
                    swiping = true;
                    swiped.Clear();
                    swiped.Add(wapp);
                    cam.AllowSingleFingerPan = false;
                    Select(wic);
                    ToggleSignal(wic, wapp);
                    tapCandidate = false;
                    return;
                }
            }
            if (PointerInput.Down)
            {
                tapCandidate = !ui.IsOverUi(PointerInput.Position);
                tapDownPos = PointerInput.Position;
                tapDownTime = Time.unscaledTime;
            }
            if (PointerInput.Up && tapCandidate)
            {
                tapCandidate = false;
                bool quick = Time.unscaledTime - tapDownTime < 0.45f;
                bool still = Vector2.Distance(PointerInput.Position, tapDownPos) < 28f;
                if (!quick || !still) return;

                Vector2 world = cam.ScreenToWorld(PointerInput.Position);
                float slack = 14f * 2f * cam.cam.orthographicSize / Mathf.Max(1, UnityEngine.Screen.height);
                if (Tool != PlayTool.None && HandleToolTap(world, slack)) return;
                var rb = RuntimeButtonAt(world, 0.4f + slack);
                if (rb != null) { PressRuntimeButton(rb); return; }
                var gb = mapRenderer.FindButtonAt(world, 0.45f);
                if (gb != null) { PressButton(gb); return; }
                IntersectionController sic;
                Approach sapp;
                if (mapRenderer.FindSignalAt(world, slack, out sic, out sapp))
                {
                    Select(sic);
                    ToggleSignal(sic, sapp);
                }
                else
                {
                    // Tocar un cruce solo lo selecciona (sus datos salen en el panel); los semáforos se tocan uno a uno.
                    Select(sim.FindNearestIntersection(world, 1.7f));
                }
            }
        }

        void EndSwipe()
        {
            if (!swiping) return;
            swiping = false;
            swiped.Clear();
            cam.AllowSingleFingerPan = true;
        }

        void UpdateTutorial()
        {
            var sc = sim.Scoring;
            if (TutorialStep == 0 && PhaseChanges >= 1) TutorialStep = 1;
            else if (TutorialStep == 1 && PhaseChanges >= 4) TutorialStep = 2;
            else if (TutorialStep == 2 && sc.delivered >= 3) TutorialStep = 3;
        }

        void FinishRound()
        {
            Ended = true;
            sim.Paused = true;
            var sc = sim.Scoring;
            bool won = levels.State == GameState.Won;
            LastStars = sc.Stars(won);
            LastWasRecord = false;

            if (levels.Level != null && (levels.Mode == GameMode.Campaign || levels.Mode == GameMode.Challenge))
            {
                int before = SaveSystem.Stars(levels.Level.id);
                int totalBefore = TotalStars();
                SaveSystem.RecordLevel(levels.Level.id, LastStars, sc.Score);
                LastWasRecord = LastStars > before;
                LastUnlocked = RewardSystem.NewlyUnlocked(totalBefore, TotalStars());
            }
            else if (levels.Mode == GameMode.ScoreCounter)
            {
                var top = SaveSystem.TopScores(ScoreKey());
                LastWasRecord = top.Count == 0 || sc.Score > top[0].score;
                SaveSystem.AddScore(ScoreKey(), sc.Score, sc.elapsed);
            }
            else if (levels.Mode == GameMode.Infinite)
            {
                var p = SaveSystem.Progress;
                if (sc.Score > p.bestInfiniteScore)
                {
                    p.bestInfiniteScore = sc.Score;
                    LastWasRecord = true;
                }
                if (sc.elapsed > p.bestInfiniteSeconds) p.bestInfiniteSeconds = sc.elapsed;
                SaveSystem.SaveProgress();
                SaveSystem.AddScore(levels.Level != null ? levels.Level.id : (PlayMap != null ? PlayMap.id : "mapa"), sc.Score, sc.elapsed);
            }

            if (won) { AudioManager.Instance.PlayWin(); if (LastStars > 0) AudioManager.Instance.PlayStars(LastStars); }
            else AudioManager.Instance.PlayLose();
            ui.OnRoundEnded();
        }

        /// <summary>Clave de las puntuaciones del contador de puntuación (separadas de las de «Puntuación más alta»).</summary>
        string ScoreKey() { return "score_" + (levels.Level != null ? levels.Level.id : (PlayMap != null ? PlayMap.id : "mapa")); }

        // ---------------- Editor ----------------

        public void OpenEditor(MapData map, bool isNew)
        {
            StopPlay();
            Screen = AppScreen.Editor;
            try { editor.Begin(map, isNew); }
            catch (System.Exception ex) { Debug.LogWarning("Editor: " + ex); ShowMessage("Hubo un problema al abrir el editor: " + ex.Message); }
            finally { ui.OnScreenChanged(); }
        }

        // ---------------- Progreso ----------------

        public bool IsLevelUnlocked(System.Collections.Generic.List<LevelDefinition> list, int index)
        {
            if (list == LevelManager.CampaignLevels) return LevelManager.CampaignUnlocked(index);
            return index <= 0 || SaveSystem.Stars(list[index - 1].id) >= 1;
        }

        /// <summary>Rango según las estrellas totales (recompensa visible en el menú principal).</summary>
        public string Rank()
        {
            int s = TotalStars();
            if (s >= 40) return "Leyenda del tráfico";
            if (s >= 26) return "Maestro del tráfico";
            if (s >= 14) return "Experto";
            if (s >= 5) return "Controlador";
            return "Novato";
        }

        /// <summary>Estrellas de campaña y desafíos fijos (el reto del día no cuenta, para que no se pierdan al cambiar de día).</summary>
        public int TotalStars()
        {
            return RewardSystem.TotalStars();
        }
    }
}
