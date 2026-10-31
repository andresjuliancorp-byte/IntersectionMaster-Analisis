using System;
using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Core;
using IntersectionMaster.Data;
using IntersectionMaster.Systems;
using IntersectionMaster.Traffic;

namespace IntersectionMaster.MapEditing
{
    public enum EditorTool
    {
        Pan, Edit, Road, OneWay, Lanes, LaneMask, Roundabout, Crosswalk, Erase, Entry, Exit,
        SignStop, SignYield, SignPriority, SignNoLeft, SignNoRight, SignNoStraight, SignSpeed, SignLight,
        Building, Tree, Park, Lamp, ParkedCar, Fence, Hedge, Smoke,
        Select, Paste, View,
        Place, Restrictions, Controller, BusStop, Toll, Detector, GroupButton,
        LaneStart, LaneEnd
    }

    /// <summary>
    /// Editor táctil de mapas por nodos y calles libres: dibuja calles arrastrando entre nodos (los cruces se crean solos
    /// al cruzar o tocar otra calle), mueve nodos, curva calles con el tirador central, ajusta carriles, sentidos únicos y
    /// giros por carril, rotondas, pasos de peatones, señales, entradas/salidas, decoración, zonas (copiar / cortar /
    /// pegar / mover), deshacer/rehacer, validar y guardar. Deshacer/rehacer usa instantáneas JSON del mapa.
    /// </summary>
    public partial class MapEditorController : MonoBehaviour
    {
        const int MaxUndo = 60;
        // ---- Cuadrícula (preferencias del editor, guardadas en el dispositivo) ----
        public static readonly float[] GridSteps = { 0.125f, 0.25f, 0.5f, 1f };
        public int GridIndex = 1;
        public bool SnapOn = true;
        public bool ShowGrid;
        public bool GridDiagonals;
        /// <summary>Sin categoría elegida (como el original): con Seleccionar solo se eligen nodos y carriles, no objetos.</summary>
        public bool SelectRoadsOnly;
        float Grid { get { return SnapOn && ShowGrid ? GridSteps[Mathf.Clamp(GridIndex, 0, GridSteps.Length - 1)] : 0.01f; } }   // sin cuadrícula visible, libre
        public float GridStep { get { return GridSteps[Mathf.Clamp(GridIndex, 0, GridSteps.Length - 1)]; } }

        void LoadGridPrefs()
        {
            GridIndex = Mathf.Clamp(PlayerPrefs.GetInt("ed_grid3", 1), 0, GridSteps.Length - 1);
            SnapOn = PlayerPrefs.GetInt("ed_snap", 1) == 1;
            ShowGrid = PlayerPrefs.GetInt("ed_showgrid", 0) == 1;
            GridDiagonals = PlayerPrefs.GetInt("ed_griddiag", 0) == 1;
            MapRenderer.ShowGridLines = ShowGrid;
            MapRenderer.GridDiagonals = GridDiagonals;
            MapRenderer.GridStepLines = GridStep;
        }

        void SaveGridPrefs()
        {
            PlayerPrefs.SetInt("ed_grid3", GridIndex);
            PlayerPrefs.SetInt("ed_snap", SnapOn ? 1 : 0);
            PlayerPrefs.SetInt("ed_showgrid", ShowGrid ? 1 : 0);
            PlayerPrefs.SetInt("ed_griddiag", GridDiagonals ? 1 : 0);
            PlayerPrefs.Save();
            MapRenderer.ShowGridLines = ShowGrid;
            MapRenderer.GridDiagonals = GridDiagonals;
            MapRenderer.GridStepLines = GridStep;
        }

        /// <summary>Cambia el tamaño de celda. No mueve los objetos ya colocados: solo afecta a lo que se coloque o mueva después.</summary>
        public void AdjustGrid(int delta)
        {
            GridIndex = Mathf.Clamp(GridIndex + delta, 0, GridSteps.Length - 1);
            SaveGridPrefs();
            Status = "Cuadrícula: " + (GridStep * MetersPerUnit).ToString("0.#") + " m por celda.";
            Rebuild(true);
        }

        public void ToggleSnap()
        {
            SnapOn = !SnapOn;
            SaveGridPrefs();
            Status = SnapOn ? "Ajuste a la cuadrícula activado." : "Ajuste a la cuadrícula desactivado (colocación libre).";
        }

        /// <summary>Estilo de la tabla como el original: 0 = sin rejilla, 1 = rejilla, 2 = rejilla con diagonales.</summary>
        public int GridStyle { get { return !ShowGrid ? 0 : (GridDiagonals ? 2 : 1); } }

        public void SetGridStyle(int style)
        {
            ShowGrid = style > 0;
            GridDiagonals = style == 2;
            SaveGridPrefs();
            Rebuild(true);
        }

        /// <summary>Tamaño de la tabla: pasa al siguiente (vuelve al más pequeño tras el mayor).</summary>
        public void CycleGridSize()
        {
            GridIndex = (GridIndex + 1) % GridSteps.Length;
            SaveGridPrefs();
            Rebuild(true);
        }

        public void ToggleShowGrid()
        {
            ShowGrid = !ShowGrid;
            SaveGridPrefs();
            Rebuild(true);
        }

        /// <summary>Punto central del elemento seleccionado (para centrar la cámara), o null.</summary>
        public Vector2? SelectionCenter
        {
            get
            {
                if (Map == null) return null;
                var r = SelectedRoad; if (r != null) return Map.PointOnRoad(r, 0.5f);
                var n = Map.Node(SelectedNodeId); if (n != null) return n.Pos;
                var d = SelectedDecorItem; if (d != null) return d.IsLine() ? (d.Pos + d.End) * 0.5f : d.Pos;
                var pt = SelectedPoint; if (pt != null) { var pr = Map.Road(pt.road); if (pr != null) return Map.PointOnRoad(pr, pt.t); }
                var b = SelectedButton; if (b != null) return b.Pos;
                var ar = SelectedArrow; if (ar != null) { Vector2 ap, ad; if (ArrowPose(ar, out ap, out ad)) return ap; }
                var sl = SelectedLight; if (sl != null) { var slr = Map.Road(sl.road); if (slr != null) return Map.PointOnRoad(slr, sl.node == slr.a ? 0.12f : 0.88f); }
                return null;
            }
        }

        public void CenterOnSelection()
        {
            var c = SelectionCenter;
            if (c.HasValue) cam.CenterOn(c.Value);
        }

        enum Gesture { None, Draw, MoveNode, Bend, Rect, Stroke, Tap, Paste, ViewEdge, PendingBend, PendingMove, MoveObj, Shape, RotateObj, PlaceTap, MoveVertex }

        public CameraController cam;
        public MapRenderer mapRenderer;
        public Func<Vector2, bool> IsOverUi;

        // Copia del mapa para deshacer: en los mapas grandes importados (miles de objetos) copiarlo a texto en cada toque
        // hacía lento el editor; se guarda una y solo se rehace cuando el mapa cambia.
        string snapCache;
        MapData map;
        bool dirty;
        public MapData Map { get { return map; } private set { map = value; snapCache = null; } }
        public bool Active { get; private set; }
        public bool Dirty { get { return dirty; } private set { dirty = value; if (value) snapCache = null; } }
        public EditorTool Tool { get; private set; }
        string status = "";
        /// <summary>Último mensaje del editor. Arriba solo se muestran los que avisan de un problema (como el original).</summary>
        public string Status { get { return status; } set { status = value ?? ""; StatusSerial++; } }
        public int StatusSerial { get; private set; }

        static readonly string[] ProblemMarks =
        {
            "No ", "no se pudo", "No se", "No cabe", "No hay", "Límite", "ya existe", "demasiado corto", "Demasiado cerca",
            "necesita", "bloqueado:", "Primero selecciona", "Hacen falta", "no llega ningún", "no puede", "Solo queda",
            "Solo hay", "máximo", "Hay un carril bloqueado", "mismo tipo", "Aviso"
        };

        /// <summary>¿El mensaje avisa de un problema o pide algo al jugador? (los demás no se muestran)</summary>
        public static bool IsProblemStatus(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var m in ProblemMarks) if (s.StartsWith(m) || s.Contains(m)) return true;
            return false;
        }
        public List<MapIssue> Issues = new List<MapIssue>();
        public bool ShowIssues;

        // Ajustes para las calles nuevas.
        public int NewRoadLanes = 1;
        /// <summary>Capa de las calles nuevas (0 = suelo, 1 y 2 = puentes).</summary>
        public int CurrentLayer;
        /// <summary>Herramienta «Curva»: la calle nueva empieza curvada (en «Recta» empieza recta).</summary>
        public bool NewRoadCurved;
        public bool NewRoadOneWay;
        public bool NewRoadPedestrian;

        // ---- Modelo por carriles ----
        /// <summary>Tipo del carril que se dibuja (Herramienta → Tipo).</summary>
        public int NewLaneType;
        /// <summary>Propiedades del carril que se dibuja (Herramienta → Propiedades).</summary>
        public readonly RoadData NewLaneProps = new RoadData { lanesAB = 1, lanesBA = 0, speedKmh = 80 };   // al construir, 80 km/h por defecto
        public const string TypeMismatch = "Los segmentos conectados deben ser del mismo tipo";

        // Elemento seleccionado con la herramienta Editar (-1 = ninguno).
        public int SelectedNodeId = -1;
        public int SelectedRoadId = -1;

        // Zona seleccionada y portapapeles (estático: se puede copiar de un mapa y pegar en otro).
        public bool HasSelection { get; private set; }
        public Rect Selection { get; private set; }
        public static MapRegion Clipboard { get; private set; }
        public bool HasClipboard { get { return Clipboard != null && !Clipboard.IsEmpty; } }

        readonly List<string> undo = new List<string>();
        readonly List<string> redo = new List<string>();

        Gesture gesture;
        string gestureSnapshot;
        bool gestureChanged;
        Vector2 downScreen;
        float downTime;
        bool moveArmed;                 // nodo listo para moverse (tras mantenerlo pulsado)
        public const float HoldToMove = 3f;
        // Carril con dos toques: el primero fija el inicio y el segundo el final.
        bool tapStartActive;
        int tsNode = -1, tsRoad = -1;
        Vector2 tsPos;
        Vector2 lastStrokePos;
        int drawStartNode, drawStartRoad, drawEndNode, drawEndRoad;
        Vector2 drawStart, drawEnd;
        int dragNode, bendRoad;
        Vector2 downWorld, dragNodeStart, bendStart;   // al empezar a arrastrar: así tocar sin mover no deforma nada
        Vector2 rectStart;
        Vector2 pasteOrigin;
        bool pasteVisible;
        bool needRebuild, needFull;

        Transform selRoot, selFill, selTop, selBottom, selLeft, selRight;
        Transform overlayRoot;
        readonly List<SpriteRenderer> pool = new List<SpriteRenderer>();
        int poolUsed;

        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }

        public static MapData CreateBlankMap()
        {
            var m = new MapData { id = MapData.NewId(), name = "Mapa nuevo", author = "Jugador", width = 24f, height = 14f, featureLevel = MapData.CurrentFeatureLevel, laneFormat = MapData.LaneFormatCurrent,
                viewX = 6f, viewY = 3.5f, viewW = 12f, viewH = 7f,   // zona de juego a la mitad: el carril se ve el doble de ancho, como el original
                startHour = 9f, goalMinutes = 6f };                  // como los niveles del original: 6 minutos desde las 9:00
            m.Touch();
            return m;
        }

        void Awake()
        {
            Tool = EditorTool.Road;
            selRoot = new GameObject("EditorSelection").transform;
            selRoot.SetParent(transform, false);
            selFill = SpriteFactory.Make("Fill", selRoot, SpriteFactory.Square, new Color(0.25f, 0.6f, 1f, 0.25f), Vector2.zero, Vector2.one, 30).transform;
            var border = new Color(0.45f, 0.75f, 1f, 0.95f);
            selTop = SpriteFactory.Make("Top", selRoot, SpriteFactory.Square, border, Vector2.zero, Vector2.one, 31).transform;
            selBottom = SpriteFactory.Make("Bottom", selRoot, SpriteFactory.Square, border, Vector2.zero, Vector2.one, 31).transform;
            selLeft = SpriteFactory.Make("Left", selRoot, SpriteFactory.Square, border, Vector2.zero, Vector2.one, 31).transform;
            selRight = SpriteFactory.Make("Right", selRoot, SpriteFactory.Square, border, Vector2.zero, Vector2.one, 31).transform;
            selRoot.gameObject.SetActive(false);

            overlayRoot = new GameObject("EditorOverlay").transform;
            overlayRoot.SetParent(transform, false);
        }

        public void Begin(MapData map, bool isNew)
        {
            LoadGridPrefs();
            if (isNew && ShowGrid) { ShowGrid = false; SaveGridPrefs(); }   // como el original: el mapa nuevo empieza sin cuadrícula
            map.EnsureLanes();
            bool grew = map.ExpandToBig();   // como el original: mapa grande; se juega dentro de la línea roja
            Map = map;
            MapRenderer.EditLayer = CurrentLayer;
            Active = true;
            Dirty = isNew || grew;
            undo.Clear();
            redo.Clear();
            Issues.Clear();
            ShowIssues = false;
            HasSelection = false;
            SelectedNodeId = SelectedRoadId = -1;
            gesture = Gesture.None;
            SetTool(EditorTool.Road);
            Status = isNew ? "Arrastra para dibujar un carril (va en el sentido del dedo). No hace falta poner entradas ni salidas." : "Mapa cargado.";
            mapRenderer.Build(Map, true);
            FitCamera();
            cam.InputEnabled = true;
            overlayRoot.gameObject.SetActive(true);
        }

        /// <summary>Encuadra la línea roja; la cámara puede moverse por todo el mapa.</summary>
        public void FitCamera()
        {
            if (Map == null) return;
            if (Map.IsLaneMap) cam.FitView(Map.View, new Rect(0f, 0f, Map.width, Map.height));
            else cam.Fit(Map.width, Map.height);
        }

        public void End()
        {
            Active = false;
            MapRenderer.EditLayer = 0;
            gesture = Gesture.None;
            if (selRoot != null) selRoot.gameObject.SetActive(false);
            if (overlayRoot != null) overlayRoot.gameObject.SetActive(false);
        }

        /// <summary>Vuelve a dibujar (por ejemplo al regresar de una prueba de simulación).</summary>
        public void Refresh()
        {
            if (Map == null) return;
            mapRenderer.Build(Map, true);
            FitCamera();
        }

        /// <summary>Reactiva el editor tras una prueba de simulación, conservando mapa, deshacer/rehacer y cambios sin guardar.</summary>
        public void Resume()
        {
            if (Map == null) return;
            Active = true;
            gesture = Gesture.None;
            Refresh();
            cam.InputEnabled = true;
            overlayRoot.gameObject.SetActive(true);
            UpdateSelectionVisual();
            Status = "Has vuelto al editor. Tus cambios se conservan.";
        }

        public void MarkDirty() { Dirty = true; }

        // ---------------- Herramientas ----------------

        public static string ToolHint(EditorTool t)
        {
            switch (t)
            {
                case EditorTool.Pan: return "Arrastra para mover el mapa; pellizca para hacer zoom.";
                case EditorTool.Edit: return "Toca un nodo o un carril para seleccionarlo. Arrastra un nodo para moverlo y el rombo central de un carril para curvarlo.";
                case EditorTool.Road: return "Arrastra para dibujar un carril (va en el sentido del dedo). Puede cruzar otros carriles sin unirse.";
                case EditorTool.OneWay: return "Toca un carril para alternar: doble sentido, sentido único A→B o B→A.";
                case EditorTool.Lanes: return "Toca un lado del carril para cambiar sus carriles (1, 2 o 3).";
                case EditorTool.LaneStart: return "Inicio de carril: toca un carril en el punto donde debe empezar un carril más (incorporación). El carril se divide ahí y el tramo siguiente gana un carril en ese sentido.";
                case EditorTool.LaneEnd: return "Fin de carril: toca un carril donde debe terminar un carril (se incorpora al vecino). El carril se divide ahí y el tramo siguiente pierde un carril.";
                case EditorTool.LaneMask: return "Flecha funcional: toca un carril para colocar una flecha (o seleccionar la que haya). Sus giros limitan las maniobras reales de los vehículos.";
                case EditorTool.Roundabout: return "Toca un nodo (o un carril) para convertirlo en rotonda. Vuelve a tocarlo para quitarla.";
                case EditorTool.Crosswalk: return "Toca un carril para poner o quitar un paso de peatones.";
                case EditorTool.Erase: return "Toca un nodo (borra sus carriles), un carril o un objeto para borrarlo.";
                case EditorTool.Entry: return "Toca un nodo en el extremo de un carril para marcar la entrada de vehículos.";
                case EditorTool.Exit: return "Toca un nodo en el extremo de un carril para marcar la salida de vehículos.";
                case EditorTool.SignStop: return "Stop: toca el carril cerca del cruce al que se acerca el tráfico.";
                case EditorTool.SignYield: return "Ceda el paso: toca el carril cerca del cruce.";
                case EditorTool.SignPriority: return "Prioridad: toca el carril cerca del cruce.";
                case EditorTool.SignNoLeft: return "Prohibido girar a la izquierda: toca el carril cerca del cruce.";
                case EditorTool.SignNoRight: return "Prohibido girar a la derecha: toca el carril cerca del cruce.";
                case EditorTool.SignNoStraight: return "Prohibido seguir recto: toca el carril cerca del cruce.";
                case EditorTool.SignSpeed: return "Límite de velocidad: toca cualquier carril.";
                case EditorTool.SignLight: return "Semáforo: toca el carril cerca del nodo donde termina. En juego se cambia tocando cada semáforo.";
                case EditorTool.View: return "Área visible: arrastra los bordes de la línea roja. Las entradas y salidas deben quedar fuera para que los coches aparezcan sin verse.";
                case EditorTool.Building: return "Coloca edificios (arrastra para varios).";
                case EditorTool.Tree: return "Coloca árboles (arrastra para varios).";
                case EditorTool.Park: return "Coloca parques.";
                case EditorTool.Lamp: return "Coloca farolas junto a los carriles.";
                case EditorTool.ParkedCar: return "Coloca coches aparcados junto a el carril (se orientan solos).";
                case EditorTool.Fence: return "Coloca vallas (se orientan según el carril más cercana).";
                case EditorTool.Hedge: return "Coloca setos (se orientan según el carril más cercana).";
                case EditorTool.Smoke: return "Coloca columnas de humo (decoración animada).";
                case EditorTool.Select: return "Arrastra para seleccionar una zona. Luego copia, corta, mueve o borra.";
                case EditorTool.Paste: return "Arrastra para colocar la zona y suelta para pegarla.";
            }
            return ToolHintExt(t);
        }

        public void SetTool(EditorTool t)
        {
            if (gesture != Gesture.None) CancelGesture();
            Pending = false;
            tapStartActive = false;
            Tool = t;
            gesture = Gesture.None;
            pasteVisible = false;
            LinkingButtonId = -1;
            if (t != EditorTool.Edit && t != EditorTool.Controller && t != EditorTool.GroupButton) { SelectedNodeId = SelectedRoadId = -1; ClearObjSel(); }
            else if (t == EditorTool.Controller) { SelectedRoadId = -1; ClearObjSel(); }
            UpdateSelectionVisual();
            Status = ToolHint(t);
        }

        static bool IsSignTool(EditorTool t)
        {
            return t >= EditorTool.SignStop && t <= EditorTool.SignLight;
        }

        static bool IsTapTool(EditorTool t)
        {
            switch (t)
            {
                case EditorTool.OneWay:
                case EditorTool.Lanes:
                case EditorTool.LaneMask:
                case EditorTool.LaneStart:
                case EditorTool.LaneEnd:
                case EditorTool.Roundabout:
                case EditorTool.Crosswalk:
                case EditorTool.Entry:
                case EditorTool.Exit:
                case EditorTool.Restrictions:
                case EditorTool.Controller:
                case EditorTool.BusStop:
                case EditorTool.Toll:
                case EditorTool.Detector:
                case EditorTool.GroupButton:
                    return true;
            }
            return IsSignTool(t);
        }

        // ---------------- Utilidades de puntero ----------------

        float Wpp { get { return 2f * cam.cam.orthographicSize / Mathf.Max(1, Screen.height); } }

        float Tol(float pixels) { return pixels * Wpp; }

        Vector2 World(Vector2 screen) { return cam.ScreenToWorld(screen); }

        /// <summary>Distancia para engancharse a un nodo al dibujar (en mapas por carriles, menos que el ancho de un carril).</summary>
        float SnapTol() { return Map != null && Map.IsLaneMap ? Mathf.Min(Tol(30f), 0.22f) : Tol(30f); }

        Vector2 Snap(Vector2 p)
        {
            float g = Grid;
            if (ShowGrid && GridDiagonals && g > 0.02f)
            {
                // Cuadrícula en rombos (girada 45°): se ajusta a las esquinas de los rombos.
                const float r2 = 0.70710678f;
                float u = Geo.Snap((p.x + p.y) * r2, g), v = Geo.Snap((p.x - p.y) * r2, g);
                return Map.Clamp(new Vector2((u + v) * r2, (u - v) * r2));
            }
            return Map.Clamp(new Vector2(Geo.Snap(p.x, g), Geo.Snap(p.y, g)));
        }

        RoadData FindRoad(Vector2 p, float extra, out float t, out float dist)
        {
            RoadData best = null;
            float bestScore = float.MaxValue;
            t = 0f; dist = 0f;
            for (int i = 0; i < Map.roads.Count; i++)
            {
                var r = Map.roads[i];
                float d;
                float tt = Map.NearestParam(r, p, out d);
                if (Tool == EditorTool.Road && r.layer != CurrentLayer) continue;
                float hw = Map.IsLaneMap ? r.Width * 0.5f : (r.lanesAB + r.lanesBA) * RoadNetworkManager.LaneWidth * 0.5f;
                float score = d - hw;
                if (score < extra && score < bestScore) { bestScore = score; best = r; t = tt; dist = d; }
            }
            return best;
        }

        // ---------------- Bucle de entrada ----------------

        float autosaveTimer;

        /// <summary>Autoguardar (como el original): cada N minutos si hay cambios y no se está dibujando nada.</summary>
        void UpdateAutosave()
        {
            int mins = SaveSystem.Progress.autosaveMinutes;
            if (mins <= 0 || !Dirty) { autosaveTimer = 0f; return; }
            autosaveTimer += Time.unscaledDeltaTime;
            if (autosaveTimer < mins * 60f || gesture != Gesture.None || Pending) return;
            autosaveTimer = 0f;
            if (Save()) Status = "Autoguardando...";
        }

        void Update()
        {
            if (!Active || Map == null) return;
            UpdateAutosave();
            // Como el original: arrastrar con un dedo donde no hay nada que mover (ni se está dibujando) mueve el mapa; no
            // hace falta pellizcar con dos dedos.
            cam.AllowSingleFingerPan = Tool == EditorTool.Pan || gesture == Gesture.None || gesture == Gesture.Tap || gesture == Gesture.PlaceTap;
            if (Tool == EditorTool.Pan) { gesture = Gesture.None; return; }

            if (PointerInput.MultiTouch)
            {
                if (gesture != Gesture.None) CancelGesture();
                return;
            }

            if (PointerInput.Down)
            {
                bool overUi = IsOverUi != null && IsOverUi(PointerInput.Position);
                if (!overUi) BeginGesture(PointerInput.Position);
            }
            else if (gesture != Gesture.None && PointerInput.Held)
            {
                UpdateGesture(PointerInput.Position);
            }

            if (gesture != Gesture.None && PointerInput.Up) EndGesture(PointerInput.Position);
        }

        void LateUpdate()
        {
            if (!Active || Map == null) return;
            if (needRebuild)
            {
                if (needFull) mapRenderer.Build(Map, true);
                else mapRenderer.BuildRoadsOnly(Map, true);
                needRebuild = false;
                needFull = false;
            }
            NoteSelection();
            DrawOverlay();
        }

        public void RebuildFull() { Rebuild(true); }

        void Rebuild(bool full)
        {
            needRebuild = true;
            if (full) needFull = true;
        }

        string Snapshot() { return JsonUtility.ToJson(Map); }
        /// <summary>Copia del mapa tal como está ahora (la misma mientras no cambie).</summary>
        string CachedSnapshot() { if (snapCache == null) snapCache = Snapshot(); return snapCache; }

        void Commit(string snapshot)
        {
            PushUndo(snapshot);
            redo.Clear();
            Dirty = true;
        }

        void CancelGesture()
        {
            if (redrawOnRelease) { redrawOnRelease = false; Rebuild(true); }
            if (gestureChanged && gestureSnapshot != null)
            {
                Map = JsonUtility.FromJson<MapData>(gestureSnapshot);
                Rebuild(true);
            }
            gesture = Gesture.None;
            gestureChanged = false;
        }

        // ---------------- Gestos ----------------

        void BeginGesture(Vector2 screen)
        {
            Vector2 p = World(screen);
            downScreen = screen;
            downWorld = p;
            downTime = Time.unscaledTime;
            moveArmed = false;
            gestureSnapshot = CachedSnapshot();
            gestureChanged = false;

            switch (Tool)
            {
                case EditorTool.View: BeginView(p); break;
                case EditorTool.Edit: BeginEdit(p); break;
                case EditorTool.Road:
                    if (Pending) BeginPendingBend(p); else BeginDraw(p);
                    break;
                case EditorTool.Erase:
                    gesture = Gesture.Stroke;
                    lastStrokePos = p;
                    ApplyStroke(p, true);
                    break;
                case EditorTool.Building:
                case EditorTool.Tree:
                case EditorTool.Park:
                case EditorTool.Lamp:
                case EditorTool.ParkedCar:
                case EditorTool.Fence:
                case EditorTool.Hedge:
                case EditorTool.Smoke:
                    gesture = Gesture.Stroke;
                    lastStrokePos = new Vector2(-999f, -999f);
                    ApplyStroke(p, true);
                    break;
                case EditorTool.Place: BeginPlace(p); break;
                case EditorTool.Select:
                    gesture = Gesture.Rect;
                    rectStart = Snap(p);
                    Selection = new Rect(rectStart.x, rectStart.y, 0.5f, 0.5f);
                    HasSelection = true;
                    UpdateSelectionVisual();
                    Status = "Suelta para fijar la selección.";
                    break;
                case EditorTool.Paste:
                    if (HasClipboard) { gesture = Gesture.Paste; UpdatePaste(p); }
                    break;
                default:
                    if (IsTapTool(Tool)) gesture = Gesture.Tap;
                    break;
            }
        }

        void UpdateGesture(Vector2 screen)
        {
            Vector2 p = World(screen);
            // Mapas por carriles: un nodo solo se mueve tras mantenerlo pulsado (sin mover el dedo) unos segundos.
            if (gesture == Gesture.MoveNode && Map.IsLaneMap)
            {
                if (!moveArmed)
                {
                    // Tolerancia de unos 5 mm: el dedo siempre se mueve un poco al mantenerlo.
                    float dpi = Screen.dpi > 50f ? Screen.dpi : 400f;
                    if (Vector2.Distance(screen, downScreen) > dpi * 0.2f) { gesture = Gesture.None; return; }   // arrastró antes de tiempo: no se mueve
                    if (Time.unscaledTime - downTime < HoldToMove) return;
                    moveArmed = true;
                    downWorld = p;
                    var an = Map.Node(dragNode);
                    if (an != null) dragNodeStart = an.Pos;
#if UNITY_ANDROID || UNITY_IOS
                    Handheld.Vibrate();
#endif
                    Status = "";
                    return;
                }
                UpdateMove(p);   // ya está listo: sigue al dedo desde el primer movimiento
                return;
            }
            // Mover o curvar solo empieza tras un arrastre claro: un toque para seleccionar no lo deforma.
            if ((gesture == Gesture.MoveNode || gesture == Gesture.Bend || gesture == Gesture.MoveObj) && !gestureChanged && Vector2.Distance(screen, downScreen) < 22f) return;
            switch (gesture)
            {
                case Gesture.Draw: UpdateDraw(p); break;
                case Gesture.MoveNode: UpdateMove(p); break;
                case Gesture.Bend: UpdateBend(p); break;
                case Gesture.Stroke: ApplyStroke(p, false); break;
                case Gesture.Rect: UpdateRect(p); break;
                case Gesture.Paste: UpdatePaste(p); break;
                case Gesture.ViewEdge: UpdateView(p); break;
                case Gesture.PendingBend: UpdatePendingBend(p); break;
                case Gesture.PendingMove: UpdatePendingMove(p); break;
                case Gesture.MoveObj: UpdateMoveObj(p); break;
                case Gesture.RotateObj: UpdateSignRotate(p); break;
                case Gesture.MoveVertex: UpdateVertexDrag(p); break;
                case Gesture.Shape: UpdateShape(p); break;
            }
        }

        void EndGesture(Vector2 screen)
        {
            Vector2 p = World(screen);
            var g = gesture;
            gesture = Gesture.None;
            switch (g)
            {
                case Gesture.Draw: EndDraw(p); break;
                case Gesture.MoveNode: EndMove(); break;
                case Gesture.Bend: EndBend(); break;
                case Gesture.Stroke:
                    if (gestureChanged) { Commit(gestureSnapshot); }
                    break;
                case Gesture.Rect:
                    Status = "Zona de " + Selection.width.ToString("0.#") + " x " + Selection.height.ToString("0.#") + ". Copia, corta, mueve o borra.";
                    break;
                case Gesture.Paste:
                    pasteVisible = false;
                    DoPaste(pasteOrigin);
                    break;
                case Gesture.ViewEdge:
                    if (gestureChanged) { Commit(gestureSnapshot); Rebuild(true); }
                    break;
                case Gesture.PendingBend:
                case Gesture.PendingMove:
                    break;
                case Gesture.MoveObj:
                case Gesture.RotateObj:
                case Gesture.MoveVertex:
                    if (redrawOnRelease) { redrawOnRelease = false; Rebuild(true); }
                    if (gestureChanged) Commit(gestureSnapshot);
                    break;
                case Gesture.Shape: EndShape(p); break;
                case Gesture.Tap:
                    if (Vector2.Distance(screen, downScreen) < 18f) DoTap(World(downScreen));
                    break;
                case Gesture.PlaceTap:
                    // Un toque pone el objeto; si el dedo se arrastró, era para mover el mapa.
                    if (Vector2.Distance(screen, downScreen) < 18f)
                    {
                        lastStrokePos = new Vector2(-999f, -999f);
                        ApplyStroke(World(downScreen), true);
                        if (gestureChanged) Commit(gestureSnapshot);
                    }
                    break;
            }
            gestureChanged = false;
        }

        // ---------------- Editar: seleccionar, mover nodos, curvar ----------------

        void BeginEdit(Vector2 p)
        {
            if (BeginSignRotate(p)) return;   // círculo de giro de la señal seleccionada (como el original)
            if (SelectRoadsOnly && Map.IsLaneMap && BeginViewCorner(p)) return;   // esquinas de la línea roja
            ClearObjSel();
            // Tirador de curvatura de la calle seleccionada o de cualquier calle.
            NodeData n = Map.NodeNear(p, Tol(28f));
            if (n != null)
            {
                SelectedNodeId = n.id;
                SelectedRoadId = -1;
                if (NodeTouchesLocked(n.id)) { Status = "Un carril de este nodo está bloqueado: no se puede mover."; gesture = Gesture.None; return; }
                dragNode = n.id;
                dragNodeStart = n.Pos;
                gesture = Gesture.MoveNode;
                Status = "Nodo seleccionado. Arrástralo para moverlo.";
                return;
            }
            for (int i = 0; i < Map.roads.Count && !Map.IsLaneMap; i++)   // en mapas por carriles no hay rombo para curvar
            {
                var r = Map.roads[i];
                Vector2 h = Map.PointOnRoad(r, 0.5f);
                if (Vector2.Distance(h, p) < Tol(26f))
                {
                    SelectedRoadId = r.id;
                    SelectedNodeId = -1;
                    if (r.locked) { Status = "Carril bloqueado: desbloquéalo para curvarlo."; gesture = Gesture.None; return; }
                    bendRoad = r.id;
                    bendStart = new Vector2(r.bendX, r.bendY);
                    gesture = Gesture.Bend;
                    Status = "Arrastra el rombo para curvar el carril.";
                    return;
                }
            }
            // Como el original: aunque no haya ninguna opción elegida, se puede tocar una señal para moverla o girarla.
            if (Map.IsLaneMap)
            {
                int si = Map.DecorNear(p, Tol(24f));
                if (si >= 0)
                {
                    bool sign = Map.decor[si].type == DecorType.TrafficSign;
                    SelectedRoadId = SelectedNodeId = -1;
                    SelectedDecor = si;
                    if (Map.decor[si].locked) { Status = (sign ? "Señal" : "Objeto") + " bloqueado: desbloquéalo para moverlo."; gesture = Gesture.None; return; }
                    grabOffset = Map.decor[si].Pos - p;
                    gesture = Gesture.MoveObj;
                    Status = sign ? "Señal seleccionada. Arrástrala para moverla o gira el aro para cambiar su sentido." : "Objeto seleccionado. Arrástralo para moverlo.";
                    return;
                }
            }
            float t, dist;
            var road = FindRoad(p, Tol(14f), out t, out dist);
            if (road != null)
            {
                SelectedRoadId = road.id;
                SelectedNodeId = -1;
                Status = "Carril seleccionado: cambia sus propiedades en el panel Segmento.";
            }
            else
            {
                SelectedRoadId = SelectedNodeId = -1;
                if (SelectRoadsOnly || !BeginObjectEdit(p)) Status = ToolHint(EditorTool.Edit);
            }
            gesture = Gesture.None;
        }

        void UpdateMove(Vector2 p)
        {
            var n = Map.Node(dragNode);
            if (n == null) return;
            // Se mueve lo mismo que el dedo (sin saltar al punto de rejilla más cercano al dedo).
            Vector2 delta = p - downWorld;
            Vector2 target = Map.IsLaneMap ? Map.Clamp(dragNodeStart + new Vector2(Geo.Snap(delta.x, Grid), Geo.Snap(delta.y, Grid))) : Snap(p);
            if (Mathf.Abs(n.x - target.x) < 0.001f && Mathf.Abs(n.y - target.y) < 0.001f) return;
            Map.MoveNode(dragNode, target);
            gestureChanged = true;
            Rebuild(false);
        }

        void EndMove()
        {
            if (!gestureChanged) return;
            var n = Map.Node(dragNode);
            if (n != null)
            {
                // Soltar encima de otro nodo = unirlos.
                // La tolerancia es la misma que al dibujar una calle, para que no queden huecos entre extremos.
                NodeData other = null;
                float bd = Map.IsLaneMap ? Mathf.Clamp(Tol(20f), 0.12f, 0.3f) : Mathf.Clamp(Tol(30f), 0.6f, 1.0f);
                foreach (var o in Map.nodes)
                {
                    if (o.id == n.id) continue;
                    float d = Vector2.Distance(o.Pos, n.Pos);
                    if (d < bd) { bd = d; other = o; }
                }
                if (other != null)
                {
                    Map.MergeNodes(other.id, n.id);
                    SelectedNodeId = other.id;
                    Status = "Nodos unidos.";
                }
                else if (Map.Degree(n.id) > 0)
                {
                    // Soltar un extremo sobre el lateral de otra calle = unirlo a ella (se crea el cruce).
                    RoadData host = null;
                    float hostT = 0f, hostD = Map.IsLaneMap ? Mathf.Clamp(Tol(12f), 0.08f, 0.18f) : Mathf.Clamp(Tol(20f), 0.3f, 0.8f);
                    foreach (var r in Map.roads)
                    {
                        if (r.a == n.id || r.b == n.id) continue;
                        bool sameLayer = true;
                        foreach (var own in Map.RoadsAt(n.id)) if (own.layer != r.layer) sameLayer = false;
                        if (!sameLayer) continue;
                        float dist;
                        float tt = Map.NearestParam(r, n.Pos, out dist);
                        if (dist < hostD && tt > 0.03f && tt < 0.97f) { hostD = dist; host = r; hostT = tt; }
                    }
                    if (host != null)
                    {
                        Map.MoveNode(n.id, Map.PointOnRoad(host, hostT));
                        if (Map.SplitAtNode(host.id, n.id) != null) Status = "Carril unido.";
                    }
                }
                var touched = new List<int>();
                int nodeId = other != null ? other.id : n.id;
                foreach (var r in Map.RoadsAt(nodeId)) touched.Add(r.id);
                ResolveCrossings(touched);
            }
            Map.Normalize();
            Commit(gestureSnapshot);
            Rebuild(false);
        }

        void UpdateBend(Vector2 p)
        {
            var r = Map.Road(bendRoad);
            if (r == null) return;
            Vector2 mid = (Map.PosOf(r.a) + Map.PosOf(r.b)) * 0.5f;
            Vector2 bend = Map.IsLaneMap ? bendStart + (p - downWorld) * 2f : (p - mid) * 2f;
            if (bend.magnitude < 0.3f) bend = Vector2.zero;
            bend = Vector2.ClampMagnitude(bend, 14f);
            if (Mathf.Abs(r.bendX - bend.x) < 0.001f && Mathf.Abs(r.bendY - bend.y) < 0.001f) return;
            r.bendX = bend.x;
            r.bendY = bend.y;
            gestureChanged = true;
            Rebuild(false);
        }

        void EndBend()
        {
            if (!gestureChanged) return;
            ResolveCrossings(new List<int> { bendRoad });
            Map.Normalize();
            Commit(gestureSnapshot);
            Rebuild(false);
        }

        // ---------------- Dibujar calles ----------------

        void BeginDraw(Vector2 p)
        {
            drawStartNode = drawStartRoad = drawEndNode = drawEndRoad = -1;
            if (Map.IsLaneMap)
            {
                if (tapStartActive)
                {
                    // Segundo toque: el inicio ya está fijado; el dedo marca el final.
                    drawStartNode = tsNode; drawStartRoad = tsRoad; drawStart = tsPos;
                    gesture = Gesture.Draw;
                    UpdateDraw(p);
                    Status = "";
                    return;
                }
                PickLaneEnd(p, true, -1, -1, out drawStartNode, out drawStartRoad, out drawStart);
                drawEnd = drawStart;
                gesture = Gesture.Draw;
                Status = "";
                return;
            }
            NodeData n = Map.NodeNear(p, SnapTol());
            if (n != null)
            {
                drawStartNode = n.id;
                drawStart = n.Pos;
            }
            else
            {
                float t, dist;
                var r = FindRoad(p, Tol(14f), out t, out dist);
                if (r != null)
                {
                    int sn, sr; Vector2 sp;
                    ResolveRoadSnap(r, t, out sn, out sr, out sp);
                    drawStartNode = sn; drawStartRoad = sr; drawStart = sp;
                }
                else drawStart = Snap(p);
            }
            drawEnd = drawStart;
            gesture = Gesture.Draw;
            Status = "Suelta donde termina el carril.";
        }

        void UpdateDraw(Vector2 p)
        {
            drawEndNode = drawEndRoad = -1;
            if (Map.IsLaneMap)
            {
                PickLaneEnd(p, false, drawStartNode, drawStartRoad, out drawEndNode, out drawEndRoad, out drawEnd);
                if (drawStartNode < 0 && drawStartRoad < 0 && Vector2.Distance(drawEnd, drawStart) < 0.05f) { drawEndNode = drawEndRoad = -1; drawEnd = Snap(p); }
                return;
            }
            NodeData n = Map.NodeNear(p, SnapTol());
            if (n != null && n.id != drawStartNode)
            {
                drawEndNode = n.id;
                drawEnd = n.Pos;
                return;
            }
            float t, dist;
            var r = FindRoad(p, Tol(14f), out t, out dist);
            if (r != null && r.id != drawStartRoad && !(drawStartNode >= 0 && (r.a == drawStartNode || r.b == drawStartNode)))
            {
                int en, er; Vector2 ep;
                ResolveRoadSnap(r, t, out en, out er, out ep);
                if (en < 0 || en != drawStartNode)
                {
                    drawEndNode = en; drawEndRoad = er; drawEnd = ep;
                    return;
                }
            }
            drawEnd = Snap(p);
        }

        /// <summary>
        /// Enganche a una calle: si el punto queda muy cerca de uno de sus extremos se engancha al nodo (así no se crean
        /// tramos diminutos al partir la calle); si no, se parte la calle en ese punto.
        /// </summary>
        void ResolveRoadSnap(RoadData r, float t, out int nodeId, out int roadId, out Vector2 pos)
        {
            Vector2 q = Map.PointOnRoad(r, t);
            var na = Map.Node(r.a);
            var nb = Map.Node(r.b);
            float da = na != null ? Vector2.Distance(q, na.Pos) : float.MaxValue;
            float db = nb != null ? Vector2.Distance(q, nb.Pos) : float.MaxValue;
            float minSplit = Map.IsLaneMap ? 0.3f : 0.6f;
            if (da < minSplit && da <= db) { nodeId = r.a; roadId = -1; pos = na.Pos; return; }
            if (db < minSplit) { nodeId = r.b; roadId = -1; pos = nb.Pos; return; }
            nodeId = -1; roadId = r.id; pos = q;
        }

        // La calle dibujada queda pendiente: se puede curvar con el punto central y se confirma o cancela con los botones.
        public bool Pending { get; private set; }
        int pSN, pSR, pEN, pER;
        Vector2 pS, pE, pBend;
        string pSnap;
        /// <summary>Metros por unidad del mapa, como el original: un carril (0,5) mide 3 m (en su cuadrícula, 6 m = dos carriles de largo).</summary>
        public const float MetersPerUnit = 6f;
        /// <summary>Para mostrar la velocidad de los vehículos en km/h (la de siempre: a 50 km/h un coche va a 50).</summary>
        public const float SpeedMetersPerUnit = 9f;

        void EndDraw(Vector2 p)
        {
            UpdateDraw(p);
            float minLen = Map.IsLaneMap ? 0.4f : 0.7f;
            if (Map.IsLaneMap && Vector2.Distance(drawStart, drawEnd) < minLen)
            {
                // Toque sin arrastrar: fija el inicio (o lo quita si se vuelve a tocar el mismo sitio); el siguiente toque pone el final.
                if (tapStartActive) { tapStartActive = false; Status = ""; return; }
                tapStartActive = true;
                tsNode = drawStartNode; tsRoad = drawStartRoad; tsPos = drawStart;
                Status = "";
                return;
            }
            tapStartActive = false;
            if (Vector2.Distance(drawStart, drawEnd) < minLen)
            {
                Status = Map.IsLaneMap ? "Arrastra más lejos para dibujar un carril." : "Arrastra más lejos para dibujar un carril (mínimo 0,7).";
                return;
            }
            pSN = drawStartNode; pSR = drawStartRoad; pEN = drawEndNode; pER = drawEndRoad;
            pS = drawStart; pE = drawEnd; pBend = Vector2.zero;
            if (NewRoadCurved)
            {
                Vector2 d = pE - pS;
                pBend = Map.IsLaneMap ? CurveBend(pS, pE, pSN, pEN) : new Vector2(-d.y, d.x) * 0.3f;   // «Curva»: arranca ya curvada; el tirador la ajusta
            }
            if (Map.IsLaneMap) { Vector2 pb; if (ParallelBend(ref pS, pSN < 0 && pSR < 0, ref pE, pEN < 0, out pb)) pBend = pb; }   // pegado a otro carril: paralelo a él
            pSnap = gestureSnapshot;
            Pending = true;
            PendingRamp = 0;
            if (!NewRoadCurved && !Map.IsLaneMap)
            {
                pBend = SmoothBend(pS, pE, pSN, pEN);
                UpdatePendingStatus();
                if (pBend != Vector2.zero) Status += " Se enlazó con suavidad a el carril anterior (arrastra el punto central al centro para dejarla recta).";
                return;
            }
            UpdatePendingStatus();
        }

        /// <summary>
        /// Curva de un carril nuevo («Curva»): sale con la misma dirección del carril que llega a su inicio y entra con la
        /// del carril que sale de su final, así la unión queda lisa (sin pico). Sin carriles vecinos, una curva suave.
        /// </summary>
        Vector2 CurveBend(Vector2 a, Vector2 b, int nodeA, int nodeB)
        {
            Vector2 chord = b - a;
            float len = chord.magnitude;
            Vector2 mid = (a + b) * 0.5f;
            Vector2 fallback = new Vector2(-chord.y, chord.x) * 0.3f;
            if (len < 0.3f) return fallback;
            Vector2? dA = null, dB = null;
            foreach (var r in Map.roads)
            {
                if (nodeA >= 0 && r.b == nodeA && dA == null) { var pl = Map.CenterOf(r); dA = pl.TangentAt(pl.Length).normalized; }
                if (nodeB >= 0 && r.a == nodeB && dB == null) { var pl = Map.CenterOf(r); dB = pl.TangentAt(0f).normalized; }
            }
            if (dA != null && dB != null)
            {
                // Punto de control: cruce de la recta de salida (desde a) y la de llegada (hasta b).
                Vector2 u = dA.Value, v = dB.Value;
                float den = u.x * v.y - u.y * v.x;
                if (Mathf.Abs(den) > 0.05f)
                {
                    float sA = (chord.x * v.y - chord.y * v.x) / den;
                    float sB = (chord.y * u.x - chord.x * u.y) / den;
                    if (sA > 0.05f && sB > 0.05f && sA < len * 3f && sB < len * 3f)
                        return Vector2.ClampMagnitude(a + u * sA - mid, 14f);
                }
            }
            if (dA != null)
            {
                float ang = Vector2.Angle(dA.Value, chord);
                if (ang < 2f) return Vector2.zero;
                if (ang < 100f) return Vector2.ClampMagnitude(a + dA.Value * (len * 0.5f / Mathf.Max(Mathf.Cos(ang * Mathf.Deg2Rad), 0.35f)) - mid, 14f);
            }
            if (dB != null)
            {
                float ang = Vector2.Angle(dB.Value, chord);
                if (ang < 2f) return Vector2.zero;
                if (ang < 100f) return Vector2.ClampMagnitude(b - dB.Value * (len * 0.5f / Mathf.Max(Mathf.Cos(ang * Mathf.Deg2Rad), 0.35f)) - mid, 14f);
            }
            return fallback;
        }

        /// <summary>
        /// Si la calle nueva continúa un extremo libre (un nodo con una sola calle) con un giro suave, devuelve la curvatura que
        /// hace que ambas calles se unan con la misma dirección, sin quiebre. Si no procede (cruces, giros bruscos, tramos cortos), cero.
        /// </summary>
        Vector2 SmoothBend(Vector2 a, Vector2 b, int nodeA, int nodeB)
        {
            Vector2 chord = b - a;
            float len = chord.magnitude;
            if (len < 1.5f) return Vector2.zero;
            bool atStart = nodeA >= 0 && Map.Degree(nodeA) == 1;
            bool atEnd = !atStart && nodeB >= 0 && Map.Degree(nodeB) == 1;
            if (!atStart && !atEnd) return Vector2.zero;
            int node = atStart ? nodeA : nodeB;
            Vector2 anchor = atStart ? a : b;
            var roads = Map.RoadsAt(node);
            var nd = Map.Node(node);
            if (roads.Count != 1 || nd == null) return Vector2.zero;
            var r = roads[0];
            Vector2 away = Map.PointOnRoad(r, r.a == node ? 0.1f : 0.9f) - nd.Pos;   // hacia dentro de la calle existente
            if (away.sqrMagnitude < 1e-6f) return Vector2.zero;
            Vector2 dir = -away.normalized;                                            // continuación natural
            Vector2 toOther = atStart ? chord : -chord;
            float ang = Vector2.Angle(dir, toOther);
            if (ang < 4f || ang > 75f) return Vector2.zero;
            float k = len * 0.5f / Mathf.Max(Mathf.Cos(ang * Mathf.Deg2Rad), 0.35f);
            Vector2 c = anchor + dir * k;
            return Vector2.ClampMagnitude(c - (a + b) * 0.5f, 14f);
        }

        float PendingLength()
        {
            Vector2 c = (pS + pE) * 0.5f + pBend;
            float len = 0f;
            Vector2 prev = pS;
            for (int i = 1; i <= 16; i++)
            {
                Vector2 q = Geo.Quad(pS, c, pE, i / 16f);
                len += Vector2.Distance(prev, q);
                prev = q;
            }
            return len;
        }

 /// <summary>Motivo por el que el carril no puede unirse en ese extremo (otro tipo de carril), o null.</summary>
        /// <summary>
        /// Problemas de un carril recién puesto (como los avisos del original): ángulo demasiado cerrado con el carril
        /// que llega o sale, curva con el punto de control demasiado cerca, o encima de otro carril del mismo tipo y altura.
        /// </summary>
        string BuildProblem(RoadData lane)
        {
            var pl = Map.CenterOf(lane);
            if (pl.Length < 0.25f) return "Segmento demasiado corto.";
            if (lane.Curved)
            {
                Vector2 c = Map.ControlPoint(lane);
                if (Vector2.Distance(c, Map.PosOf(lane.a)) < 0.12f || Vector2.Distance(c, Map.PosOf(lane.b)) < 0.12f) return "Distancia al punto de control demasiado corta.";
            }
            Vector2 tA = pl.TangentAt(0f).normalized, tB = pl.TangentAt(pl.Length).normalized;
            foreach (var o in Map.roads)
            {
                if (o == lane || o.laneType != lane.laneType) continue;
                var po = Map.CenterOf(o);
                // Giro casi en U en la unión con el carril anterior o el siguiente.
                if (o.b == lane.a && Vector2.Angle(po.TangentAt(po.Length), tA) > 120f) return "Ángulo demasiado corto.";
                if (o.a == lane.b && Vector2.Angle(tB, po.TangentAt(0f)) > 120f) return "Ángulo demasiado corto.";
            }
            // Encima de otro carril (casi paralelo y pegado) en la misma altura, sin compartir nodo.
            if (lane.ramp == 0)
                foreach (var o in Map.roads)
                {
                    if (o == lane || o.ramp != 0 || o.layer != lane.layer) continue;
                    if (o.a == lane.a || o.a == lane.b || o.b == lane.a || o.b == lane.b) continue;
                    if (o.laneType == RoadData.LaneSidewalk || lane.laneType == RoadData.LaneSidewalk) continue;   // las aceras cruzan
                    var po = Map.CenterOf(o);
                    float thr = (lane.Width + o.Width) * 0.5f * 0.6f;
                    int run = 0;
                    for (float sx = 0.05f; sx < pl.Length - 0.05f; sx += 0.08f)
                    {
                        float d;
                        float so = po.Project(pl.PointAt(sx), out d);
                        bool near = d < thr && Mathf.Abs(Vector2.Dot(pl.TangentAt(sx).normalized, po.TangentAt(so).normalized)) > 0.9f;
                        run = near ? run + 1 : 0;
                        if (run >= 5) return "Colisión ilegal con otro segmento.";
                    }
                }
            return null;
        }

        string LaneTypeProblem(int nodeId, int roadId)
        {
            if (Map == null || !Map.IsLaneMap) return null;
            if (nodeId >= 0)
                foreach (var r in Map.RoadsAt(nodeId)) if (r.laneType != NewLaneType) return TypeMismatch;
            if (roadId >= 0)
            {
                var r = Map.Road(roadId);
                if (r != null && r.laneType != NewLaneType) return TypeMismatch;
            }
            return null;
        }

        void UpdatePendingStatus()
        {
            if (Map != null && Map.IsLaneMap)
            {
                string prob = LaneTypeProblem(pSN, pSR) ?? LaneTypeProblem(pEN, pER);
                Status = prob ?? (RoadData.LaneTypeNames[Mathf.Clamp(NewLaneType, 0, RoadData.LaneTypeNames.Length - 1)] + " de " + Mathf.RoundToInt(PendingLength() * MetersPerUnit) + " m. Arrastra el punto central para curvarlo y los extremos para moverlos.");
                return;
            }
            Status = "Calle de " + Mathf.RoundToInt(PendingLength() * MetersPerUnit) + " m. Arrastra el punto central para curvarla y los extremos para alargarla o cambiar su dirección. Botón verde = confirmar, rojo = cancelar, azul = invertir sentido.";
        }

        void BeginPendingBend(Vector2 p)
        {
            if (Map.IsLaneMap)
            {
                if (HitLaneBox(p)) return;
                float er2 = Tol(38f);
                float ds2 = Vector2.Distance(pS, p), de2 = Vector2.Distance(pE, p);
                if (Mathf.Min(ds2, de2) < er2) { pendingEnd = de2 < ds2 ? 1 : 0; gesture = Gesture.PendingMove; return; }
                Vector2 h2 = Geo.Quad(pS, (pS + pE) * 0.5f + pBend, pE, 0.5f);
                if (Vector2.Distance(h2, p) < Tol(40f)) { gesture = Gesture.PendingBend; return; }
                // El carril solo se crea con ✓ (como el original): tocar en otro sitio no hace nada.
                return;
            }
            // Botones ✓ / ✗ dibujados junto a la calle.
            Vector2 okPos, noPos;
            PendingButtonPositions(out okPos, out noPos);
            float br = Tol(32f);
            if (Vector2.Distance(okPos, p) < br) { ConfirmPending(); return; }
            if (Vector2.Distance(noPos, p) < br) { CancelPending(); return; }
            Vector2 flipPos = PendingFlipPosition();
            if (Vector2.Distance(flipPos, p) < br) { FlipPending(); return; }
            // Extremos: arrastrarlos alarga, acorta o cambia la dirección de la calle (se reenganchan a nodos y calles).
            float er = Tol(38f);
            float ds = Vector2.Distance(pS, p), de = Vector2.Distance(pE, p);
            if (Mathf.Min(ds, de) < er) { pendingEnd = de < ds ? 1 : 0; gesture = Gesture.PendingMove; return; }
            Vector2 h = (pS + pE) * 0.5f + pBend * 0.5f;
            if (Vector2.Distance(h, p) < Tol(40f)) { gesture = Gesture.PendingBend; return; }
            Status = "Confirma (✓) o cancela (✗) el carril.";
        }

        int pendingEnd;   // extremo que se arrastra: 0 = inicio, 1 = final

        /// <summary>Mueve un extremo de la calle pendiente. Se engancha a nodos y calles como al dibujar; la curva se conserva.</summary>
        void UpdatePendingMove(Vector2 p)
        {
            bool first = pendingEnd == 0;
            int otherNode = first ? pEN : pSN, otherRoad = first ? pER : pSR;
            int node = -1, road = -1;
            Vector2 pos;
            if (Map.IsLaneMap)
            {
                PickLaneEnd(p, first, otherNode, otherRoad, out node, out road, out pos);
                if (first) { pS = pos; pSN = node; pSR = road; }
                else { pE = pos; pEN = node; pER = road; }
                Vector2 pb2;
                if (ParallelBend(ref pS, first ? false : (pSN < 0 && pSR < 0), ref pE, !first && pEN < 0, out pb2)) pBend = pb2;
                UpdatePendingStatus();
                return;
            }
            NodeData n = Map.NodeNear(p, SnapTol());
            if (n != null && n.id != otherNode) { node = n.id; pos = n.Pos; }
            else
            {
                float t, dist;
                var r = FindRoad(p, Tol(14f), out t, out dist);
                bool snapped = false;
                pos = Snap(p);
                if (r != null && r.id != otherRoad && !(otherNode >= 0 && (r.a == otherNode || r.b == otherNode)))
                {
                    int rn, rr; Vector2 rp;
                    ResolveRoadSnap(r, t, out rn, out rr, out rp);
                    if (rn < 0 || rn != otherNode) { node = rn; road = rr; pos = rp; snapped = true; }
                }
                if (!snapped) pos = Snap(p);
            }
            if (first) { pS = pos; pSN = node; pSR = road; }
            else { pE = pos; pEN = node; pER = road; }
            UpdatePendingStatus();
        }

        /// <summary>Invierte el sentido de la calle pendiente (inicio ↔ final).</summary>
        public void FlipPending()
        {
            if (!Pending) return;
            Vector2 tp = pS; pS = pE; pE = tp;
            int ti = pSN; pSN = pEN; pEN = ti;
            ti = pSR; pSR = pER; pER = ti;
            Status = "Sentido invertido.";
        }

        void UpdatePendingBend(Vector2 p)
        {
            Vector2 mid = (pS + pE) * 0.5f;
            Vector2 bend = (p - mid) * 2f;
            if (bend.magnitude < 0.3f) bend = Vector2.zero;
            pBend = Vector2.ClampMagnitude(bend, 14f);
            UpdatePendingStatus();
        }

        public void CancelPending()
        {
            if (!Pending) return;
            Pending = false;
            PendingRamp = 0;
            Status = Map != null && Map.IsLaneMap ? "Carril cancelado." : "Calle cancelada.";
        }

        public void ConfirmPending()
        {
            if (!Pending) return;
            Pending = false;
            Map = JsonUtility.FromJson<MapData>(pSnap);   // parte del mapa tal como estaba al empezar a dibujar

            if (Map.IsLaneMap)
            {
                string prob = LaneTypeProblem(pSN, pSR) ?? LaneTypeProblem(pEN, pER);
                if (prob != null) { Map = JsonUtility.FromJson<MapData>(pSnap); Pending = true; Status = prob; return; }
            }
            NodeData a = ResolveEndpoint(pSN, pSR, pS);
            if (a == null) { RevertTo(pSnap); Status = "No se pudo crear el carril (límite de nodos o carriles)."; return; }
            NodeData b = ResolveEndpoint(pEN, pER, pE);
            if (b == null) { RevertTo(pSnap); Status = "No se pudo crear el carril (límite de nodos o carriles)."; return; }

            if (Map.IsLaneMap)
            {
                if (Map.FindRoad(a.id, b.id) != null) { RevertTo(pSnap); Status = "Segmento ya construido."; return; }
                if (Vector2.Distance(a.Pos, b.Pos) < 0.25f) { RevertTo(pSnap); Status = "Segmento demasiado corto."; return; }
                var lane = Map.AddRoad(a.id, b.id, 1, 0);
                if (lane == null) { RevertTo(pSnap); Status = "No se pudo crear el carril (límite de nodos o carriles)."; return; }
                ApplyNewLaneProps(lane);
                lane.layer = CurrentLayer;
                lane.ramp = PendingRamp;
                lane.bendX = pBend.x; lane.bendY = pBend.y;
                // Avisos como el original: no deja construir algo que no funcionaría.
                string bp = BuildProblem(lane);
                if (bp != null) { Map = JsonUtility.FromJson<MapData>(pSnap); Pending = true; Rebuild(true); Status = bp; return; }
                lastLaneId = lane.id;
                if (PendingRamp != 0) CurrentLayer = Mathf.Clamp(CurrentLayer + PendingRamp, MapData.MinLayer, MapData.MaxLayer);
                MapRenderer.EditLayer = CurrentLayer;
                PendingRamp = 0;
                lane.bendX = pBend.x;
                lane.bendY = pBend.y;
                Map.Normalize();
                Commit(pSnap);
                Rebuild(true);
                AudioManager.Instance.PlayPlop();
                Status = RoadData.LaneTypeNames[lane.laneType] + " creado. Sigue desde su extremo, o desde un cuadradito de al lado para el carril vecino.";
                return;
            }
            int la = Mathf.Clamp(NewRoadLanes, 1, 3);
            var road = Map.AddRoad(a.id, b.id, NewRoadPedestrian ? 1 : la, NewRoadPedestrian ? 1 : (NewRoadOneWay ? 0 : la));
            if (road != null && NewRoadPedestrian) { road.pedestrian = true; road.noMarks = true; }
            if (road == null)
            {
                RevertTo(pSnap);
                Status = "Ese carril ya existe o es demasiado corto.";
                return;
            }
            road.layer = CurrentLayer;
            road.bendX = pBend.x;
            road.bendY = pBend.y;

            AutoPortal(a);
            AutoPortal(b);

            ResolveCrossings(new List<int> { road.id });
            Map.Normalize();
            Commit(pSnap);
            Rebuild(true);
            Status = "Carril creado. Conecta más carriles o prueba el mapa.";
        }

        // ---------------- Área visible (línea roja) ----------------

        int viewMask;
        Rect viewStart;

        void BeginView(Vector2 p)
        {
            Rect v = Map.View;
            float tol = Tol(36f);
            int mask = 0;
            bool inY = p.y > v.yMin - tol && p.y < v.yMax + tol;
            bool inX = p.x > v.xMin - tol && p.x < v.xMax + tol;
            if (inY && Mathf.Abs(p.x - v.xMin) < tol) mask |= 1;
            if (inY && Mathf.Abs(p.x - v.xMax) < tol) mask |= 2;
            if (inX && Mathf.Abs(p.y - v.yMin) < tol) mask |= 4;
            if (inX && Mathf.Abs(p.y - v.yMax) < tol) mask |= 8;
            if (mask == 0) return;
            viewMask = mask;
            viewStart = v;
            gesture = Gesture.ViewEdge;
        }

        /// <summary>Como el original: la línea roja se cambia arrastrando una de sus cuatro esquinas.</summary>
        bool BeginViewCorner(Vector2 p)
        {
            Rect v = Map.View;
            float tol = Mathf.Max(Tol(40f), 0.3f);
            Vector2[] c = { new Vector2(v.xMin, v.yMin), new Vector2(v.xMax, v.yMin), new Vector2(v.xMax, v.yMax), new Vector2(v.xMin, v.yMax) };
            int[] masks = { 1 | 4, 2 | 4, 2 | 8, 1 | 8 };
            int best = -1; float bd = tol;
            for (int i = 0; i < 4; i++) { float d = Vector2.Distance(c[i], p); if (d < bd) { bd = d; best = i; } }
            if (best < 0) return false;
            viewMask = masks[best];
            viewStart = v;
            gesture = Gesture.ViewEdge;
            return true;
        }

        void DrawViewCorners(float wpp)
        {
            if (!SelectRoadsOnly || Tool != EditorTool.Edit || !Map.IsLaneMap) return;
            Rect v = Map.View;
            Vector2[] c = { new Vector2(v.xMin, v.yMin), new Vector2(v.xMax, v.yMin), new Vector2(v.xMax, v.yMax), new Vector2(v.xMin, v.yMax) };
            foreach (var q in c)
            {
                Spr(SpriteFactory.Square, new Color(0.15f, 0.15f, 0.17f, 0.95f), q, Vector2.one * 22f * wpp, 51);
                Spr(SpriteFactory.Square, new Color(0.85f, 0.2f, 0.18f, 1f), q, Vector2.one * 14f * wpp, 52);
            }
        }

        void UpdateView(Vector2 p)
        {
            float x0 = viewStart.xMin, x1 = viewStart.xMax, y0 = viewStart.yMin, y1 = viewStart.yMax;
            float min = MapData.MinViewSize;
            if ((viewMask & 1) != 0) x0 = Mathf.Clamp(Geo.Snap(p.x, Grid), 0f, x1 - min);
            if ((viewMask & 2) != 0) x1 = Mathf.Clamp(Geo.Snap(p.x, Grid), x0 + min, Map.width);
            if ((viewMask & 4) != 0) y0 = Mathf.Clamp(Geo.Snap(p.y, Grid), 0f, y1 - min);
            if ((viewMask & 8) != 0) y1 = Mathf.Clamp(Geo.Snap(p.y, Grid), y0 + min, Map.height);
            Map.SetView(Rect.MinMaxRect(x0, y0, x1, y1));
            gestureChanged = true;
            Status = "Área visible: " + (x1 - x0).ToString("0.#") + " x " + (y1 - y0).ToString("0.#") + ". Las entradas y salidas deben quedar fuera.";
        }

        NodeData ResolveEndpoint(int nodeId, int roadId, Vector2 pos)
        {
            if (nodeId >= 0) return Map.Node(nodeId);
            if (roadId >= 0) return Map.SplitRoad(roadId, pos);
            if (Map.IsLaneMap) { var at = NodeAt(pos, -1); if (at != null) return at; }
            return Map.AddNode(pos);
        }

        void AutoPortal(NodeData n)
        {
            if (n == null || n.entry || n.exit) return;
            const float e = 0.05f;
            bool border = n.x < e || n.y < e || n.x > Map.width - e || n.y > Map.height - e;
            if (border && Map.Degree(n.id) == 1) { n.entry = true; n.exit = true; }
        }

        void RevertTo(string snapshot)
        {
            Map = JsonUtility.FromJson<MapData>(snapshot);
            Rebuild(true);
        }

        /// <summary>Crea cruces donde las calles indicadas atraviesan otras calles por su interior.</summary>
        void ResolveCrossings(List<int> startPieces)
        {
            var pieces = new HashSet<int>(startPieces);
            for (int guard = 0; guard < 40; guard++)
            {
                bool split = false;
                var ids = new List<int>(pieces);
                for (int i = 0; i < ids.Count && !split; i++)
                {
                    var pr = Map.Road(ids[i]);
                    if (pr == null) continue;
                    Polyline pp = Map.CenterOf(pr);
                    for (int j = 0; j < Map.roads.Count && !split; j++)
                    {
                        var o = Map.roads[j];
                        if (pieces.Contains(o.id)) continue;
                        if (o.a == pr.a || o.a == pr.b || o.b == pr.a || o.b == pr.b) continue;
                        if (o.layer != pr.layer) continue;   // capas distintas: puente, sin cruce
                        Polyline op = Map.CenterOf(o);
                        float s1, s2;
                        if (!Geo.PolylineCross(pp, op, out s1, out s2)) continue;
                        if (s1 < 0.35f || s1 > pp.Length - 0.35f || s2 < 0.35f || s2 > op.Length - 0.35f) continue;
                        Vector2 at = pp.PointAt(s1);
                        var x = Map.SplitRoad(pr.id, at);
                        if (x == null) { guard = 99; break; }
                        pieces.Add(Map.roads[Map.roads.Count - 1].id);
                        Map.SplitAtNode(o.id, x.id);
                        split = true;
                    }
                }
                if (!split) break;
            }
        }

        // ---------------- Trazos continuos: borrar y decoración ----------------

        void ApplyStroke(Vector2 p, bool first)
        {
            float step = Tool == EditorTool.Place ? BrushStep() : Tool == EditorTool.Erase ? Tol(10f) : (Tool == EditorTool.Park ? 1.6f : (Tool == EditorTool.Lamp || Tool == EditorTool.ParkedCar ? 0.6f : 0.9f));
            if (!first && Vector2.Distance(p, lastStrokePos) < step) return;
            // Como el original: en mapas por carriles un toque pone un objeto (arrastrar no pinta más).
            if (!first && Tool != EditorTool.Erase && Map.IsLaneMap) return;
            lastStrokePos = p;

            bool changed = false;
            switch (Tool)
            {
                case EditorTool.Erase: changed = EraseAt(p); break;
                case EditorTool.Place: changed = PlaceBrushAt(p); break;
                case EditorTool.Building: changed = PlaceDecorAt(p, DecorType.Building); break;
                case EditorTool.Tree: changed = PlaceDecorAt(p, DecorType.Tree); break;
                case EditorTool.Park: changed = PlaceDecorAt(p, DecorType.Park); break;
                case EditorTool.Lamp: changed = PlaceDecorAt(p, DecorType.Lamp); break;
                case EditorTool.ParkedCar: changed = PlaceDecorAt(p, DecorType.ParkedCar); break;
                case EditorTool.Fence: changed = PlaceDecorAt(p, DecorType.Fence); break;
                case EditorTool.Hedge: changed = PlaceDecorAt(p, DecorType.Hedge); break;
                case EditorTool.Smoke: changed = PlaceDecorAt(p, DecorType.Smoke); break;
            }
            if (changed)
            {
                gestureChanged = true;
                Dirty = true;
                Rebuild(true);
                if (Tool == EditorTool.Erase) AudioManager.Instance.PlayUnplop(); else AudioManager.Instance.PlayPlop();
            }
        }

        bool PlaceDecorAt(Vector2 p, DecorType type)
        {
            Vector2 s = Snap(p);
            float min = type == DecorType.Building ? 1.3f : (type == DecorType.Park ? 1.0f : 0.7f);
            if (type == DecorType.Lamp) min = 0.6f;
            else if (type == DecorType.ParkedCar) min = 0.55f;
            else if (type == DecorType.Fence || type == DecorType.Hedge) min = 0.8f;
            else if (type == DecorType.Smoke) min = 0.5f;
            if (Map.IsLaneMap ? (min >= 0.8f && Map.DistanceToVehicleLanes(s) < 0.05f) : Map.DistanceToRoads(s) < min)
            {
                Status = "Demasiado cerca de un carril.";
                return false;
            }
            return Map.PlaceDecor(s, type);
        }

        bool EraseAt(Vector2 p)
        {
            NodeData n = Map.NodeNear(p, Tol(22f));
            if (n != null)
            {
                if (NodeTouchesLocked(n.id)) { Status = "Hay un carril bloqueado en ese nodo."; return false; }
                Map.RemoveNode(n.id);
                SelectedNodeId = SelectedRoadId = -1;
                Status = "Nodo borrado.";
                return true;
            }
            float t, dist;
            var r = FindRoad(p, Tol(12f), out t, out dist);
            if (r != null)
            {
                if (r.locked) { Status = "Calle bloqueada."; return false; }
                Map.RemoveRoad(r.id);
                SelectedNodeId = SelectedRoadId = -1;
                Status = "Calle borrada.";
                return true;
            }
            if (EraseObjectsAt(p)) return true;
            return Map.EraseDecorNear(p, 0.7f);
        }

        // ---------------- Toques ----------------

        void DoTap(Vector2 p)
        {
            string snap = gestureSnapshot;
            bool changed = false;
            switch (Tool)
            {
                case EditorTool.Entry: changed = TapPortal(p, true); break;
                case EditorTool.Exit: changed = TapPortal(p, false); break;
                case EditorTool.Roundabout: changed = TapRoundabout(p); break;
                case EditorTool.Crosswalk: changed = TapCrosswalk(p); break;
                case EditorTool.Lanes: changed = TapLanes(p); break;
                case EditorTool.OneWay: changed = TapOneWay(p); break;
                case EditorTool.LaneMask: changed = TapArrow(p); break;
                case EditorTool.LaneStart: changed = TapLaneChangeHere(p, 1); break;
                case EditorTool.LaneEnd: changed = TapLaneChangeHere(p, -1); break;
                case EditorTool.SignStop: changed = TapSign(p, SignType.Stop); break;
                case EditorTool.SignYield: changed = TapSign(p, SignType.Yield); break;
                case EditorTool.SignPriority: changed = TapSign(p, SignType.Priority); break;
                case EditorTool.SignNoLeft: changed = TapSign(p, SignType.NoLeft); break;
                case EditorTool.SignNoRight: changed = TapSign(p, SignType.NoRight); break;
                case EditorTool.SignNoStraight: changed = TapSign(p, SignType.NoStraight); break;
                case EditorTool.SignSpeed: changed = TapSign(p, SignType.SpeedLimit); break;
                case EditorTool.SignLight: changed = TapSign(p, SignType.Light); break;
                case EditorTool.Restrictions: changed = TapRestrictions(p); break;
                case EditorTool.Controller: TapController(p); break;
                case EditorTool.BusStop: changed = TapPoint(p, PointKind.BusStop); break;
                case EditorTool.Toll: changed = TapPoint(p, PointKind.Toll); break;
                case EditorTool.Detector: changed = TapPoint(p, PointKind.Detector); break;
                case EditorTool.GroupButton: changed = TapGroupButton(p); break;
            }
            if (changed)
            {
                Map.Normalize();
                Commit(snap);
                Rebuild(false);
            }
        }

        bool TapPortal(Vector2 p, bool entry)
        {
            NodeData n = Map.NodeNear(p, Tol(32f));
            if (n == null) { Status = "Toca un nodo en el extremo de un carril."; return false; }
            if (Map.Degree(n.id) != 1)
                Status = "Mejor en el extremo de un carril (este nodo tiene " + Map.Degree(n.id) + " carriles).";
            if (entry) n.entry = !n.entry; else n.exit = !n.exit;
            Status = (entry ? "Entrada " : "Salida ") + ((entry ? n.entry : n.exit) ? "marcada." : "quitada.");
            return true;
        }

        bool TapRoundabout(Vector2 p)
        {
            NodeData n = Map.NodeNear(p, Tol(32f));
            if (n == null)
            {
                float t, dist;
                var r = FindRoad(p, Tol(14f), out t, out dist);
                if (r == null) { Status = "Toca un nodo o un carril para crear la rotonda."; return false; }
                n = Map.SplitRoad(r.id, Map.PointOnRoad(r, t));
                if (n == null) return false;
            }
            n.roundabout = !n.roundabout;
            if (n.roundabout && n.radius < 0.8f) n.radius = 1.1f;
            Status = n.roundabout ? "Rotonda creada. Ajusta su tamaño con la herramienta Editar." : "Rotonda quitada.";
            return true;
        }

        bool TapCrosswalk(Vector2 p)
        {
            float t, dist;
            var r = FindRoad(p, Tol(14f), out t, out dist);
            if (r == null) { Status = "Toca un carril."; return false; }
            Status = "Paso de peatones cambiado.";
            return Map.ToggleCrosswalk(r.id, t);
        }

        bool TapLanes(Vector2 p)
        {
            float t, dist;
            var r = FindRoad(p, Tol(14f), out t, out dist);
            if (r == null) { Status = "Toca un carril."; return false; }
            Polyline pl = Map.CenterOf(r);
            float s = pl.Project(p, out dist);
            bool forward = Geo.Cross(pl.TangentAt(s), p - pl.PointAt(s)) <= 0f;
            int count = forward ? r.lanesAB : r.lanesBA;
            int next = count == 0 ? 1 : (count % 3) + 1;
            if (forward) r.lanesAB = next; else r.lanesBA = next;
            r.maskAB = new int[0];
            r.maskBA = new int[0];
            r.ResetLaneAllow();
            Status = "Carriles en ese sentido: " + next + ".";
            return true;
        }

        bool TapOneWay(Vector2 p)
        {
            float t, dist;
            var r = FindRoad(p, Tol(14f), out t, out dist);
            if (r == null) { Status = "Toca un carril."; return false; }
            CycleOneWay(r);
            return true;
        }

        void CycleOneWay(RoadData r)
        {
            r.maskAB = new int[0];
            r.maskBA = new int[0];
            r.ResetLaneAllow();
            if (r.lanesAB > 0 && r.lanesBA > 0)
            {
                r.lanesAB = Mathf.Max(r.lanesAB, r.lanesBA);
                r.lanesBA = 0;
                Status = "Sentido único (A→B).";
            }
            else if (r.lanesAB > 0)
            {
                r.lanesBA = r.lanesAB;
                r.lanesAB = 0;
                Status = "Sentido único (B→A).";
            }
            else
            {
                r.lanesAB = r.lanesBA;
                Status = "Doble sentido.";
            }
        }


        public static string MaskName(int mask)
        {
            if (mask == 0) return "automático";
            string s = "";
            if ((mask & 1) != 0) s += "izquierda ";
            if ((mask & 2) != 0) s += "recto ";
            if ((mask & 4) != 0) s += "derecha ";
            return s.Trim().Replace(" ", " + ");
        }

        bool TapSign(Vector2 p, SignType type)
        {
            float t, dist;
            var r = FindRoad(p, Tol(16f), out t, out dist);
            if (r == null) { Status = "Toca un carril."; return false; }
            if (type == SignType.SpeedLimit)
            {
                Map.ToggleSign(r.id, -1, type);
                Status = r.limited ? "Límite de velocidad puesto." : "Límite de velocidad quitado.";
                return true;
            }
            int node = Map.IsLaneMap ? r.b : (t < 0.5f ? r.a : r.b);
            if (Map.IsLaneMap)
            {
                if (type == SignType.Light && Map.FindSign(r.id, node, SignType.Light) != null)
                {
                    SelectedSignRoad = r.id; SelectedSignNode = node;
                    Status = "Semáforo seleccionado: cambia su modelo, tamaño, lado o fase en el panel.";
                    return false;
                }
                bool added2 = ToggleNodeSign(node, type);
                if (type == SignType.Light && Map.FindSign(r.id, node, SignType.Light) != null) { SelectedSignRoad = r.id; SelectedSignNode = node; }
                return added2;
            }
            if (type == SignType.Light)
            {
                var ex = Map.FindSign(r.id, node, SignType.Light);
                if (ex != null)
                {
                    SelectedSignRoad = r.id; SelectedSignNode = node;
                    Status = "Semáforo seleccionado: cambia su modelo, tamaño, lado o fase en el panel.";
                    return false;
                }
                if (Map.Degree(node) < 3) Status = "Aviso: ese extremo no es un cruce (menos de 3 carriles); el semáforo no tendrá efecto.";
                else Status = "Semáforo colocado. Tócalo otra vez para editarlo.";
                bool added = Map.ToggleSign(r.id, node, type);
                if (added) { SelectedSignRoad = r.id; SelectedSignNode = node; }
                return added;
            }
            if (Map.Degree(node) < 3)
                Status = "Aviso: ese extremo no es un cruce (menos de 3 carriles); la señal no tendrá efecto.";
            else Status = "Señal cambiada en el cruce.";
            return Map.ToggleSign(r.id, node, type);
        }

        // ---------------- Panel de selección (Editar) ----------------

        public NodeData SelectedNode { get { return Map == null ? null : Map.Node(SelectedNodeId); } }
        public RoadData SelectedRoad { get { return Map == null ? null : Map.Road(SelectedRoadId); } }

        /// <summary>true si el objeto seleccionado (calle o decoración) está bloqueado.</summary>
        public bool SelectionLocked
        {
            get
            {
                var r = SelectedRoad; if (r != null && r.locked) return true;
                var d = SelectedDecorItem; return d != null && d.locked;
            }
        }

        public void ToggleLockSelected()
        {
            string snap = Snapshot();
            var r = SelectedRoad; var d = SelectedDecorItem;
            if (r != null) { r.locked = !r.locked; Status = r.locked ? "Calle bloqueada." : "Calle desbloqueada."; }
            else if (d != null) { d.locked = !d.locked; Status = d.locked ? "Objeto bloqueado." : "Objeto desbloqueado."; }
            else return;
            Commit(snap);
        }

        bool NodeTouchesLocked(int nodeId)
        {
            foreach (var rr in Map.RoadsAt(nodeId)) if (rr.locked) return true;
            return false;
        }

        void Edit(Func<bool> action, bool full = false)
        {
            if (SelectionLocked) { Status = "Objeto bloqueado: desbloquéalo para editarlo."; return; }
            string snap = CachedSnapshot();
            if (!action()) return;
            Map.Normalize();
            Commit(snap);
            Rebuild(full);
        }

        public void AdjustLanes(bool forward, int delta)
        {
            var r = SelectedRoad;
            if (r == null) return;
            Edit(() =>
            {
                int cur = forward ? r.lanesAB : r.lanesBA;
                int other = forward ? r.lanesBA : r.lanesAB;
                int next = Mathf.Clamp(cur + delta, 0, 3);
                if (next == cur) return false;
                if (next == 0 && other == 0) return false;
                if (forward) r.lanesAB = next; else r.lanesBA = next;
                r.maskAB = new int[0];
                r.maskBA = new int[0];
                r.ResetLaneAllow();
                return true;
            });
        }

        public void ToggleSelectedOneWay()
        {
            var r = SelectedRoad;
            if (r == null) return;
            Edit(() => { CycleOneWay(r); return true; });
        }

        public void FlipSelectedRoad()
        {
            var r = SelectedRoad;
            if (r == null) return;
            Edit(() => { Map.FlipRoad(r); Status = "Calle invertida."; return true; });
        }

        public void StraightenSelectedRoad()
        {
            var r = SelectedRoad;
            if (r == null) return;
            Edit(() => { r.bendX = 0f; r.bendY = 0f; Status = "Calle enderezada."; return true; });
        }

        public void SplitSelectedRoad()
        {
            var r = SelectedRoad;
            if (r == null) return;
            Edit(() =>
            {
                var n = Map.SplitRoad(r.id, Map.PointOnRoad(r, 0.5f));
                if (n == null) return false;
                SelectedRoadId = -1;
                SelectedNodeId = n.id;
                Status = "Nodo añadido en medio del carril.";
                return true;
            });
        }

        public void CycleSelectedColor()
        {
            Edit(() => { var r = SelectedRoad; if (r == null) return false; r.color = (r.color + 1) % MapRenderer.RoadColorNames.Length; return true; }, true);
        }

        public void ToggleSelectedCables()
        {
            Edit(() => { var r = SelectedRoad; if (r == null) return false; r.cables = !r.cables; return true; }, true);
        }

        public void ToggleSelectedPedestrian()
        {
            Edit(() =>
            {
                var r = SelectedRoad; if (r == null) return false;
                r.pedestrian = !r.pedestrian;
                if (r.pedestrian) { r.noMarks = true; }
                Status = r.pedestrian ? "Calle peatonal: ningún vehículo puede entrar." : "Calle para vehículos otra vez.";
                return true;
            }, true);
        }

        public void ToggleSelectedRails()
        {
            Edit(() => { var r = SelectedRoad; if (r == null) return false; r.rails = !r.rails; return true; }, true);
        }

        // ---- Tráfico permitido por carril ----
        public bool SelLaneForward = true;
        public int SelLaneIndex;

        public void SelectLane(bool forward, int index)
        {
            SelLaneForward = forward; SelLaneIndex = index;
        }

        public int SelLaneMask
        {
            get { var r = SelectedRoad; return r == null ? RoadData.AllVehicles : r.GetLaneAllowRaw(SelLaneForward, SelLaneIndex); }
        }

        public void ToggleLaneVehicle(VehicleType t)
        {
            Edit(() =>
            {
                var r = SelectedRoad; if (r == null) return false;
                int m = r.GetLaneAllowRaw(SelLaneForward, SelLaneIndex) ^ (1 << (int)t);
                if ((m & RoadData.AllVehicles) == 0) { Status = "Un carril necesita al menos un tipo de vehículo."; return false; }
                r.SetLaneAllow(SelLaneForward, SelLaneIndex, m & RoadData.AllVehicles);
                return true;
            }, false);
        }

        public void SetLaneAllowAll()
        {
            Edit(() => { var r = SelectedRoad; if (r == null) return false; r.SetLaneAllow(SelLaneForward, SelLaneIndex, RoadData.AllVehicles); return true; }, false);
        }

        public void SetLaneAllowOnly(VehicleType t)
        {
            Edit(() => { var r = SelectedRoad; if (r == null) return false; r.SetLaneAllow(SelLaneForward, SelLaneIndex, 1 << (int)t); return true; }, false);
        }

        /// <summary>Copia la configuración del carril seleccionado a los demás carriles de la calle (mismo sentido y contrario).</summary>
        public void CopyLaneAllowToAll()
        {
            Edit(() =>
            {
                var r = SelectedRoad; if (r == null) return false;
                int m = r.GetLaneAllowRaw(SelLaneForward, SelLaneIndex);
                for (int i = 0; i < r.lanesAB; i++) r.SetLaneAllow(true, i, m);
                for (int i = 0; i < r.lanesBA; i++) r.SetLaneAllow(false, i, m);
                Status = "Configuración copiada a todos los carriles del carril.";
                return true;
            }, false);
        }

        /// <summary>Copia al carril vecino (siguiente del mismo sentido).</summary>
        public void CopyLaneAllowToNext()
        {
            Edit(() =>
            {
                var r = SelectedRoad; if (r == null) return false;
                int count = SelLaneForward ? r.lanesAB : r.lanesBA;
                if (count < 2) { Status = "Solo hay un carril en ese sentido."; return false; }
                int m = r.GetLaneAllowRaw(SelLaneForward, SelLaneIndex);
                r.SetLaneAllow(SelLaneForward, (SelLaneIndex + 1) % count, m);
                Status = "Copiado al carril vecino.";
                return true;
            }, false);
        }

        // ---- Carriles que empiezan o terminan a mitad de calle ----

        /// <summary>Divide la calle en el punto tocado y suma (+1) o resta (-1) un carril al tramo siguiente en el sentido tocado.</summary>
        bool TapLaneChangeHere(Vector2 p, int delta)
        {
            float t, dist;
            var r = FindRoad(p, Tol(14f), out t, out dist);
            if (r == null) { Status = "Toca un carril."; return false; }
            if (r.locked) { Status = "Calle bloqueada."; return false; }
            Polyline pl = Map.CenterOf(r);
            float sp = pl.Project(p, out dist);
            bool forward = Geo.Cross(pl.TangentAt(sp), p - pl.PointAt(sp)) <= 0f;
            int cur = forward ? r.lanesAB : r.lanesBA;
            if (cur == 0) { Status = "No hay carriles en ese sentido."; return false; }
            if (delta > 0 && cur >= 3) { Status = "Ese sentido ya tiene el máximo de carriles (3)."; return false; }
            if (delta < 0 && cur <= 1) { Status = "Solo queda un carril: no puede terminar."; return false; }
            int oldB = r.b;
            var node = Map.SplitRoad(r.id, p);
            if (node == null) { Status = "No se pudo dividir el carril (límite de nodos o carriles)."; return false; }
            // Tramo "siguiente" en el sentido de circulación: a→b continúa por la parte nodo→b; b→a continúa por a→nodo.
            RoadData down = null;
            foreach (var q in Map.RoadsAt(node.id))
            {
                if (forward && q.a == node.id && q.b == oldB) down = q;
                if (!forward && q.b == node.id && q.id == r.id) down = q;
            }
            if (down == null) { Status = "No se encontró el tramo siguiente."; return false; }
            if (forward) down.lanesAB += delta; else down.lanesBA += delta;
            down.maskAB = new int[0]; down.maskBA = new int[0];
            down.ResetLaneAllow();
            Status = delta > 0 ? "Carril añadido desde este punto (el carril exterior se abre en dos)." : "Carril terminado en este punto (se incorpora al vecino).";
            return true;
        }

        // ---- Acera, mediana y copia de propiedades de calle ----

        static readonly float[] SidewalkSteps = { -1f, 0f, 0.15f, 0.45f };
        public static readonly string[] SidewalkNames = { "Normal", "Sin acera", "Estrecha", "Ancha" };
        static readonly float[] MedianSteps = { 0f, 0.3f, 0.6f, 1.0f };

        public static int SidewalkIndex(RoadData r) { for (int i = 0; i < SidewalkSteps.Length; i++) if (Mathf.Abs(SidewalkSteps[i] - r.sidewalk) < 0.01f) return i; return 0; }

        public void CycleSelectedSidewalk()
        {
            Edit(() => { var r = SelectedRoad; if (r == null) return false; r.sidewalk = SidewalkSteps[(SidewalkIndex(r) + 1) % SidewalkSteps.Length]; return true; }, true);
        }

        public void CycleSelectedMedian()
        {
            Edit(() =>
            {
                var r = SelectedRoad; if (r == null) return false;
                if (r.lanesAB == 0 || r.lanesBA == 0) { Status = "La mediana separa los dos sentidos: el carril necesita carriles en ambos."; return false; }
                int i = 0; for (int k = 0; k < MedianSteps.Length; k++) if (Mathf.Abs(MedianSteps[k] - r.median) < 0.01f) i = k;
                r.median = MedianSteps[(i + 1) % MedianSteps.Length];
                Status = r.median > 0f ? "Mediana de " + (r.median * MetersPerUnit).ToString("0.#") + " m." : "Sin mediana.";
                return true;
            }, true);
        }

        public void CycleSelectedMedianType()
        {
            Edit(() => { var r = SelectedRoad; if (r == null) return false; r.medianType = (r.medianType + 1) % RoadData.MedianNames.Length; return true; }, true);
        }

        RoadData roadClipboard;
        public bool HasRoadClipboard { get { return roadClipboard != null; } }

        /// <summary>Copia las propiedades de la calle seleccionada (carriles, tráfico permitido, flechas no, aspecto, acera, mediana).</summary>
        public void CopyRoadProps()
        {
            var r = SelectedRoad; if (r == null) return;
            roadClipboard = JsonUtility.FromJson<RoadData>(JsonUtility.ToJson(r));
            Status = "Propiedades del carril copiadas. Selecciona otro carril y pulsa «Pegar propiedades».";
        }

        public void PasteRoadProps()
        {
            var src = roadClipboard; if (src == null) return;
            Edit(() =>
            {
                var r = SelectedRoad; if (r == null || r.id == src.id) return false;
                r.lanesAB = src.lanesAB; r.lanesBA = src.lanesBA;
                r.limited = src.limited; r.noLaneChange = src.noLaneChange; r.noMarks = src.noMarks; r.color = src.color; r.colorHex = src.colorHex;
                r.lineL = src.lineL; r.lineR = src.lineR; r.lineSame = src.lineSame; r.lineOpp = src.lineOpp; r.lineEdge = src.lineEdge;
                r.cables = src.cables; r.rails = src.rails; r.pedestrian = src.pedestrian; r.allow = src.allow;
                r.allowAB = src.allowAB == null ? new int[0] : (int[])src.allowAB.Clone();
                r.allowBA = src.allowBA == null ? new int[0] : (int[])src.allowBA.Clone();
                r.sidewalk = src.sidewalk; r.median = src.median; r.medianType = src.medianType;
                r.maskAB = new int[0]; r.maskBA = new int[0];
                Status = "Propiedades pegadas.";
                return true;
            }, true);
        }

        // ---- Grupos de objetos decorativos ----

        public void GroupSelection()
        {
            if (!HasSelection) { Status = "Primero selecciona una zona con «Seleccionar zona»."; return; }
            var list = new List<DecorItem>();
            foreach (var d in Map.decor) if (Selection.Contains(d.Pos)) list.Add(d);
            if (list.Count < 2) { Status = "Hacen falta al menos dos objetos decorativos en la zona."; return; }
            string snap = Snapshot();
            int g = 1; foreach (var d in Map.decor) if (d.group >= g) g = d.group + 1;
            foreach (var d in list) d.group = g;
            Commit(snap);
            Status = "Grupo creado con " + list.Count + " objetos: se mueven, duplican y borran juntos.";
        }

        public void UngroupSelection()
        {
            if (!HasSelection) { Status = "Primero selecciona una zona con «Seleccionar zona»."; return; }
            string snap = Snapshot();
            int c = 0;
            foreach (var d in Map.decor) if (Selection.Contains(d.Pos) && d.group != 0) { d.group = 0; c++; }
            if (c == 0) { Status = "No hay objetos agrupados en la zona."; return; }
            Commit(snap);
            Status = c + " objetos desagrupados.";
        }

        public void CycleSelectedAllow()
        {
            Edit(() => { var r = SelectedRoad; if (r == null) return false; r.NextAllowPreset(); return true; }, false);
        }

        public void ToggleSelectedHidden()
        {
            Edit(() => { var r = SelectedRoad; if (r == null) return false; r.hidden = !r.hidden; return true; }, true);
        }

        public void CycleCurrentLayer()
        {
            CurrentLayer = (CurrentLayer + 1) % 3;
            MapRenderer.EditLayer = CurrentLayer; Rebuild(true);
            Status = CurrentLayer == 0 ? "Capa 0: suelo. Las carriles nuevas se cruzan entre sí." : "Capa " + CurrentLayer + ": las carriles nuevas pasan por encima (puente) sin cruzarse con otras capas.";
        }

        public void CycleSelectedLayer()
        {
            Edit(() =>
            {
                var r = SelectedRoad; if (r == null) return false;
                r.layer = (r.layer + 1) % 3;
                ResolveCrossings(new List<int> { r.id });
                return true;
            }, true);
        }

        public void ToggleSelectedLaneChange()
        {
            Edit(() => { var r = SelectedRoad; if (r == null) return false; r.noLaneChange = !r.noLaneChange; return true; }, false);
        }

        /// <summary>Tabla de tráfico: peso (0 a 5) de un nodo de entrada/salida.</summary>
        public void AdjustNodeWeight(int nodeId, float delta)
        {
            Edit(() =>
            {
                var n = Map.Node(nodeId); if (n == null) return false;
                float w = Mathf.Clamp(Mathf.Round((n.weight + delta) * 2f) / 2f, 0f, 5f);
                if (Mathf.Approximately(w, n.weight)) return false;
                n.weight = w;
                return true;
            }, false);
        }

        public void ToggleSelectedMarks()
        {
            Edit(() => { var r = SelectedRoad; if (r == null) return false; r.noMarks = !r.noMarks; return true; }, true);
        }

        /// <summary>Pone (o quita) un semáforo en cada acceso del nodo seleccionado.</summary>
        public void ToggleSelectedNodeLights()
        {
            Edit(() =>
            {
                var n = SelectedNode;
                if (n == null) return false;
                var accesses = new List<RoadData>();
                foreach (var r in Map.RoadsAt(n.id))
                {
                    int incoming = r.a == n.id ? r.lanesBA : r.lanesAB;
                    if (incoming > 0) accesses.Add(r);
                }
                if (accesses.Count == 0) return false;
                bool all = true;
                foreach (var r in accesses) if (Map.FindSign(r.id, n.id, SignType.Light) == null) all = false;
                foreach (var r in accesses)
                {
                    bool has = Map.FindSign(r.id, n.id, SignType.Light) != null;
                    if (all == has) Map.ToggleSign(r.id, n.id, SignType.Light);
                }
                Status = all ? "Semáforos quitados del nodo." : "Semáforos puestos en todos los accesos del nodo.";
                return true;
            }, true);
        }

        public bool NodeHasLights(NodeData n)
        {
            if (n == null) return false;
            foreach (var r in Map.RoadsAt(n.id)) if (Map.FindSign(r.id, n.id, SignType.Light) != null) return true;
            return false;
        }

        public void ToggleSelectedSpeedLimit()
        {
            var r = SelectedRoad;
            if (r == null) return;
            Edit(() => Map.ToggleSign(r.id, -1, SignType.SpeedLimit));
        }

        public void ToggleSelectedCrosswalk()
        {
            var r = SelectedRoad;
            if (r == null) return;
            Edit(() => Map.ToggleCrosswalk(r.id, 0.5f));
        }

        public void ToggleSelectedNodeFlag(int which)
        {
            var n = SelectedNode;
            if (n == null) return;
            Edit(() =>
            {
                if (which == 0) n.entry = !n.entry;
                else if (which == 1) n.exit = !n.exit;
                else
                {
                    n.roundabout = !n.roundabout;
                    if (n.roundabout && n.radius < 0.8f) n.radius = 1.1f;
                }
                return true;
            });
        }

        public void AdjustRoundaboutRadius(float delta)
        {
            var n = SelectedNode;
            if (n == null || !n.roundabout) return;
            Edit(() =>
            {
                float next = Mathf.Clamp(n.radius + delta, 0.7f, 2f);
                if (Mathf.Abs(next - n.radius) < 0.001f) return false;
                n.radius = next;
                return true;
            });
        }

        public void DeleteSelected()
        {
            if (DeleteSelectedObject()) return;
            if (SelectedNodeId >= 0)
            {
                int id = SelectedNodeId;
                Edit(() => { Map.RemoveNode(id); SelectedNodeId = -1; Status = "Nodo borrado."; return true; });
            }
            else if (SelectedRoadId >= 0)
            {
                int id = SelectedRoadId;
                Edit(() => { Map.RemoveRoad(id); SelectedRoadId = -1; Status = "Calle borrada."; return true; });
            }
        }

        // ---------------- Selección de zona, copiar, cortar, pegar, mover ----------------

        void UpdateRect(Vector2 p)
        {
            Vector2 cur = Snap(p);
            float x0 = Mathf.Min(rectStart.x, cur.x), y0 = Mathf.Min(rectStart.y, cur.y);
            float x1 = Mathf.Max(rectStart.x, cur.x), y1 = Mathf.Max(rectStart.y, cur.y);
            Selection = new Rect(x0, y0, Mathf.Max(0.5f, x1 - x0), Mathf.Max(0.5f, y1 - y0));
            UpdateSelectionVisual();
        }

        void UpdatePaste(Vector2 p)
        {
            if (Clipboard == null) return;
            Vector2 o = Snap(p - new Vector2(Clipboard.width, Clipboard.height) * 0.5f);
            o.x = Mathf.Clamp(o.x, Mathf.Min(0f, Map.width - Clipboard.width), Mathf.Max(0f, Map.width - Clipboard.width));
            o.y = Mathf.Clamp(o.y, Mathf.Min(0f, Map.height - Clipboard.height), Mathf.Max(0f, Map.height - Clipboard.height));
            pasteOrigin = o;
            pasteVisible = true;
        }

        void DoPaste(Vector2 origin)
        {
            string snapshot = Snapshot();
            int before = Map.roads.Count;
            if (Map.PasteRegion(Clipboard, origin))
            {
                var pieces = new List<int>();
                for (int i = before; i < Map.roads.Count; i++) pieces.Add(Map.roads[i].id);
                ResolveCrossings(pieces);
                Map.Normalize();
                Commit(snapshot);
                Rebuild(true);
                Status = "Zona pegada. Arrastra de nuevo para pegarla otra vez o elige otra herramienta.";
            }
        }

        public void CopySelection()
        {
            if (!HasSelection) { Status = "Primero selecciona una zona con «Seleccionar»."; return; }
            var reg = Map.ExtractRegion(Selection);
            if (reg.IsEmpty) { Status = "La zona seleccionada está vacía."; return; }
            Clipboard = reg;
            Status = "Zona copiada (" + reg.roads.Count + " carriles). Usa «Pegar».";
        }

        public void CutSelection()
        {
            if (!HasSelection) { Status = "Primero selecciona una zona con «Seleccionar»."; return; }
            var reg = Map.ExtractRegion(Selection);
            if (reg.IsEmpty) { Status = "La zona seleccionada está vacía."; return; }
            Clipboard = reg;
            Commit(Snapshot());
            Map.ClearRegion(Selection);
            Map.Normalize();
            Rebuild(true);
            Status = "Zona cortada. Usa «Pegar» para colocarla.";
        }

        public void DeleteSelection()
        {
            if (!HasSelection) { Status = "Primero selecciona una zona con «Seleccionar»."; return; }
            Commit(Snapshot());
            Map.ClearRegion(Selection);
            Map.Normalize();
            Rebuild(true);
            Status = "Zona borrada.";
        }

        /// <summary>Mover = cortar y pegar en otro sitio.</summary>
        public void MoveSelection()
        {
            if (!HasSelection) { Status = "Primero selecciona una zona con «Seleccionar»."; return; }
            CutSelection();
            if (HasClipboard) BeginPaste();
        }

        public void BeginPaste()
        {
            if (!HasClipboard) { Status = "No hay nada copiado todavía."; return; }
            SetTool(EditorTool.Paste);
        }

        public void ClearSelection()
        {
            HasSelection = false;
            UpdateSelectionVisual();
        }

        void UpdateSelectionVisual()
        {
            if (selRoot == null) return;
            bool show = Active && Tool == EditorTool.Select && HasSelection;
            selRoot.gameObject.SetActive(show);
            if (!show) return;
            float x = Selection.xMin, y = Selection.yMin, w = Selection.width, h = Selection.height;
            const float t = 0.07f;
            Place(selFill, x + w / 2f, y + h / 2f, w, h);
            Place(selTop, x + w / 2f, y + h, w + t, t);
            Place(selBottom, x + w / 2f, y, w + t, t);
            Place(selLeft, x, y + h / 2f, t, h + t);
            Place(selRight, x + w, y + h / 2f, t, h + t);
        }

        static void Place(Transform tr, float x, float y, float w, float h)
        {
            tr.localPosition = new Vector3(x, y, 0f);
            tr.localScale = new Vector3(w, h, 1f);
        }

        // ---------------- Superposición del editor (nodos, tiradores, vista previa) ----------------

        SpriteRenderer Spr(Sprite sprite, Color color, Vector2 pos, Vector2 size, int order, float rot = 0f)
        {
            SpriteRenderer sr;
            if (poolUsed < pool.Count) sr = pool[poolUsed];
            else
            {
                var go = new GameObject("Ov");
                go.transform.SetParent(overlayRoot, false);
                sr = go.AddComponent<SpriteRenderer>();
                pool.Add(sr);
            }
            poolUsed++;
            sr.enabled = true;
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            var t = sr.transform;
            t.localPosition = new Vector3(pos.x, pos.y, 0f);
            t.localRotation = Quaternion.Euler(0f, 0f, rot);
            t.localScale = new Vector3(size.x, size.y, 1f);
            return sr;
        }

        void Segment(Vector2 a, Vector2 b, float width, Color col, int order)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.001f) return;
            Spr(SpriteFactory.Square, col, (a + b) * 0.5f, new Vector2(len, width), order, Geo.Angle(d));
        }

        void DrawOverlay()
        {
            poolUsed = 0;
            BeginPreviewLabels();
            float wpp = Wpp;
            bool nodesBig = Tool == EditorTool.Edit || Tool == EditorTool.Road || Tool == EditorTool.Entry
                || Tool == EditorTool.Exit || Tool == EditorTool.Roundabout || Tool == EditorTool.Erase;
            float nodePx = nodesBig ? 24f : 14f;

            // Calle seleccionada.
            var sel = SelectedRoad;
            if (sel != null)
            {
                Polyline pl = Map.CenterOf(sel);
                if (Map.IsLaneMap)
                {
                    // Como el original: franja azul del ancho del carril.
                    var blue = new Color(0.16f, 0.5f, 0.95f, 0.85f);
                    float bw = sel.Width * 0.7f;
                    for (int i = 0; i < pl.pts.Length - 1; i++) Segment(pl.pts[i], pl.pts[i + 1], bw, blue, 49);
                    for (int i = 1; i < pl.pts.Length - 1; i++) Spr(SpriteFactory.Circle, blue, pl.pts[i], Vector2.one * bw, 49);
                }
                else
                    for (int i = 0; i < pl.pts.Length - 1; i++) Segment(pl.pts[i], pl.pts[i + 1], 12f * wpp, new Color(1f, 0.9f, 0.3f, 0.55f), 49);
            }

            // Tiradores de curvatura (no en mapas por carriles: como el original, sin rombo).
            if (Tool == EditorTool.Edit && !Map.IsLaneMap)
            {
                for (int i = 0; i < Map.roads.Count; i++)
                {
                    var r = Map.roads[i];
                    Vector2 h = Map.PointOnRoad(r, 0.5f);
                    bool isSel = r.id == SelectedRoadId;
                    Spr(SpriteFactory.Square, new Color(0.1f, 0.12f, 0.16f, 0.9f), h, Vector2.one * 20f * wpp, 52, 45f);
                    Spr(SpriteFactory.Square, isSel ? new Color(1f, 0.9f, 0.3f, 1f) : new Color(0.95f, 0.95f, 0.98f, 0.95f), h, Vector2.one * 14f * wpp, 53, 45f);
                }
            }

            // Nodos.
            for (int i = 0; i < Map.nodes.Count; i++)
            {
                var n = Map.nodes[i];
                Color c;
                if (n.roundabout) c = SpriteFactory.Hex("#F0932B");
                else if (n.entry && n.exit) c = SpriteFactory.Hex("#4C8DF6");
                else if (n.entry) c = SpriteFactory.Hex("#3FB950");
                else if (n.exit) c = SpriteFactory.Hex("#D62828");
                else c = new Color(0.95f, 0.95f, 0.98f, 1f);
                float size = nodePx * wpp;
                if (Map.IsLaneMap)
                {
                    // Como en la referencia: nodos como cuadraditos grises; el seleccionado, azul.
                    bool selN = n.id == SelectedNodeId;
                    float q = (nodesBig ? 13f : 9f) * wpp;
                    Spr(SpriteFactory.Square, new Color(0.25f, 0.25f, 0.27f, 0.95f), n.Pos, Vector2.one * q * 1.35f, 54);
                    Spr(SpriteFactory.Square, selN ? new Color(0.15f, 0.45f, 1f, 1f) : new Color(0.55f, 0.55f, 0.57f, 1f), n.Pos, Vector2.one * q, 55);
                    continue;
                }
                Spr(SpriteFactory.Circle, new Color(0.08f, 0.1f, 0.14f, 0.95f), n.Pos, Vector2.one * size * 1.3f, 54);
                Spr(SpriteFactory.Circle, c, n.Pos, Vector2.one * size, 55);
                if (n.id == SelectedNodeId)
                    Spr(SpriteFactory.Ring, new Color(1f, 0.9f, 0.3f, 1f), n.Pos, Vector2.one * size * 2.2f, 56);
                if (n.roundabout && n.id == SelectedNodeId)
                    Spr(SpriteFactory.Ring, new Color(1f, 0.9f, 0.3f, 0.5f), n.Pos, Vector2.one * (n.radius * 2f + 0.5f), 48);
            }

            DrawObjectOverlay(wpp);
            DrawViewCorners(wpp);
            if (Tool == EditorTool.Road && Map.IsLaneMap) DrawSideSquares(wpp);

            // Área visible: línea roja, zona exterior atenuada y tiradores (con la herramienta Área visible).
            {
                Rect v = Map.View;
                var red = new Color(0.95f, 0.18f, 0.18f, 0.95f);
                var dim = new Color(0f, 0f, 0f, Map.IsLaneMap ? 0f : 0.28f);
                float big = 200f;
                Spr(SpriteFactory.Square, dim, new Vector2(v.xMin - big * 0.5f, v.center.y), new Vector2(big, big * 2f), 46);
                Spr(SpriteFactory.Square, dim, new Vector2(v.xMax + big * 0.5f, v.center.y), new Vector2(big, big * 2f), 46);
                Spr(SpriteFactory.Square, dim, new Vector2(v.center.x, v.yMin - big * 0.5f), new Vector2(v.width, big), 46);
                Spr(SpriteFactory.Square, dim, new Vector2(v.center.x, v.yMax + big * 0.5f), new Vector2(v.width, big), 46);
                float lw = (Tool == EditorTool.View ? 6f : 3f) * wpp;
                Segment(new Vector2(v.xMin, v.yMin), new Vector2(v.xMax, v.yMin), lw, red, 50);
                Segment(new Vector2(v.xMax, v.yMin), new Vector2(v.xMax, v.yMax), lw, red, 50);
                Segment(new Vector2(v.xMax, v.yMax), new Vector2(v.xMin, v.yMax), lw, red, 50);
                Segment(new Vector2(v.xMin, v.yMax), new Vector2(v.xMin, v.yMin), lw, red, 50);
                if (Tool == EditorTool.View)
                {
                    Vector2[] hs =
                    {
                        new Vector2(v.xMin, v.yMin), new Vector2(v.xMax, v.yMin), new Vector2(v.xMax, v.yMax), new Vector2(v.xMin, v.yMax),
                        new Vector2(v.center.x, v.yMin), new Vector2(v.center.x, v.yMax), new Vector2(v.xMin, v.center.y), new Vector2(v.xMax, v.center.y)
                    };
                    for (int i = 0; i < hs.Length; i++)
                    {
                        Spr(SpriteFactory.Square, Color.white, hs[i], Vector2.one * 26f * wpp, 51);
                        Spr(SpriteFactory.Square, red, hs[i], Vector2.one * 18f * wpp, 52);
                    }
                }
            }

            // Calle pendiente de confirmar: se ve completa (calzada, carriles, medidas) y con botones ✓ / ✗.
            if (Pending)
            {
                DrawRoadPreview(pS, pE, pBend, pSN, pSR, pEN, pER, true);
            }

            // Vista previa de la calle que se está dibujando, igual que quedará al confirmar.
            if (gesture == Gesture.Draw)
            {
                Vector2 d0 = drawEnd - drawStart;
                Vector2 bend0 = !NewRoadCurved || d0.magnitude < 0.4f ? Vector2.zero
                    : (Map.IsLaneMap ? CurveBend(drawStart, drawEnd, drawStartNode, drawEndNode) : new Vector2(-d0.y, d0.x) * 0.3f);
                Vector2 pbd;
                if (Map.IsLaneMap && ParallelBend(ref drawStart, drawStartNode < 0 && drawStartRoad < 0, ref drawEnd, drawEndNode < 0, out pbd)) bend0 = pbd;
                if (d0.magnitude < 0.05f)
                {
                    Spr(SpriteFactory.Circle, new Color(0.45f, 0.8f, 1f, 0.9f), drawStart, Vector2.one * 20f * wpp, 58);
                    if (drawStartNode >= 0 || drawStartRoad >= 0) Spr(SpriteFactory.Ring, PrevGreen, drawStart, Vector2.one * 46f * wpp, 58);
                }
                else DrawRoadPreview(drawStart, drawEnd, bend0, drawStartNode, drawStartRoad, drawEndNode, drawEndRoad, false);
            }

            // Carril con dos toques: marca del inicio fijado con el primer toque.
            if (tapStartActive && Tool == EditorTool.Road && !Pending && gesture != Gesture.Draw)
            {
                float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 6f);
                Spr(SpriteFactory.Circle, PrevGreen, tsPos, Vector2.one * 26f * wpp * pulse, 58);
                Spr(SpriteFactory.Ring, Color.white, tsPos, Vector2.one * 44f * wpp * pulse, 58);
            }

            // Mantener pulsado un nodo para moverlo: anillo que se llena (y azul cuando ya se puede mover).
            if (gesture == Gesture.MoveNode && Map.IsLaneMap)
            {
                var hn = Map.Node(dragNode);
                if (hn != null)
                {
                    float k = moveArmed ? 1f : Mathf.Clamp01((Time.unscaledTime - downTime) / HoldToMove);
                    var col = moveArmed ? new Color(0.15f, 0.45f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.35f + 0.6f * k);
                    if (moveArmed || k > 0.1f) Spr(SpriteFactory.Ring, col, hn.Pos, Vector2.one * (20f + 40f * k) * wpp, 59);
                }
            }

            // Vista previa del pegado.
            if (Tool == EditorTool.Paste && pasteVisible && Clipboard != null)
            {
                Spr(SpriteFactory.Square, new Color(0.3f, 0.7f, 1f, 0.14f), pasteOrigin + new Vector2(Clipboard.width, Clipboard.height) * 0.5f,
                    new Vector2(Mathf.Max(0.5f, Clipboard.width), Mathf.Max(0.5f, Clipboard.height)), 47);
                var byId = new Dictionary<int, NodeData>();
                foreach (var nd in Clipboard.nodes) byId[nd.id] = nd;
                var ghost = new Color(0.35f, 0.8f, 1f, 0.8f);
                foreach (var rd in Clipboard.roads)
                {
                    NodeData na, nb;
                    if (!byId.TryGetValue(rd.a, out na) || !byId.TryGetValue(rd.b, out nb)) continue;
                    Vector2 a = na.Pos + pasteOrigin, b = nb.Pos + pasteOrigin;
                    Vector2 ctrl = (a + b) * 0.5f + new Vector2(rd.bendX, rd.bendY);
                    int steps = rd.Curved ? 12 : 1;
                    Vector2 prev = a;
                    for (int s = 1; s <= steps; s++)
                    {
                        Vector2 q = rd.Curved ? Geo.Quad(a, ctrl, b, s / (float)steps) : b;
                        Segment(prev, q, 0.5f, ghost, 59);
                        prev = q;
                    }
                }
                foreach (var nd in Clipboard.nodes)
                    Spr(SpriteFactory.Circle, ghost, nd.Pos + pasteOrigin, Vector2.one * 0.3f, 60);
            }

            for (int i = poolUsed; i < pool.Count; i++) pool[i].enabled = false;
            EndPreviewLabels();
        }

        // ---------------- Deshacer / rehacer ----------------

        void PushUndo(string snapshot)
        {
            undo.Add(snapshot);
            if (undo.Count > MaxUndo) undo.RemoveAt(0);
        }

        public void Undo()
        {
            if (undo.Count == 0) return;
            redo.Add(JsonUtility.ToJson(Map));
            Map = JsonUtility.FromJson<MapData>(undo[undo.Count - 1]);
            undo.RemoveAt(undo.Count - 1);
            ClearSelectionIds();
            Dirty = true;
            Rebuild(true);
        }

        public void Redo()
        {
            if (redo.Count == 0) return;
            undo.Add(JsonUtility.ToJson(Map));
            Map = JsonUtility.FromJson<MapData>(redo[redo.Count - 1]);
            redo.RemoveAt(redo.Count - 1);
            ClearSelectionIds();
            Dirty = true;
            Rebuild(true);
        }

        void ClearSelectionIds()
        {
            if (Map.Node(SelectedNodeId) == null) SelectedNodeId = -1;
            if (Map.Road(SelectedRoadId) == null) SelectedRoadId = -1;
            ClampObjSel();
        }

        // ---------------- Tamaño, validación y guardado ----------------

        public void ResizeBy(float dw, float dh)
        {
            float mx = Map.MaxSizeAllowed;
            float w = Mathf.Clamp(Map.width + dw, MapData.MinSize, mx);
            float h = Mathf.Clamp(Map.height + dh, MapData.MinSize, mx);
            if (Mathf.Abs(w - Map.width) < 0.001f && Mathf.Abs(h - Map.height) < 0.001f) return;
            Commit(Snapshot());
            Map.Resize(w, h);
            HasSelection = false;
            ClearSelectionIds();
            UpdateSelectionVisual();
            Dirty = true;
            mapRenderer.Build(Map, true);
            FitCamera();
        }

        /// <summary>Estado de la última validación (en edición / con advertencias / listo).</summary>
        public MapReadiness Readiness = MapReadiness.Editing;
        public int IssueSel;   // problema elegido en la lista (muestra su «Cómo corregirlo»)

        /// <summary>
        /// Valida el mapa sin bloquear nunca la edición. Devuelve true si se puede simular (sin errores).
        /// Los errores solo impiden iniciar la simulación; el mapa sigue siendo editable.
        /// </summary>
        public bool Validate()
        {
            Issues = MapValidator.Validate(Map);
            MapValidator.SortForDisplay(Issues);
            IssueSel = 0;
            IssuesVersion++;
            ShowIssues = Issues.Count > 0;
            Readiness = MapValidator.Readiness(Issues);
            Status = MapValidator.ReadinessText(Issues);
            bool errors = Readiness == MapReadiness.Editing;
            if (errors)
            {
                var first = MapValidator.FirstError(Issues);
                if (first != null && first.hasPos) cam.CenterOn(first.pos);
            }
            return !errors;
        }

        /// <summary>Se incrementa en cada validación: la interfaz redibuja la lista aunque el número de problemas no cambie.</summary>
        public int IssuesVersion;

        /// <summary>
        /// Repara conexiones rotas que se pueden resolver sin ambigüedad: calles casi nulas y nodos casi juntos se funden
        /// en uno, y los extremos pegados al lateral de otra calle se unen a ella creando el cruce. Es una sola acción
        /// de Deshacer. Devuelve cuántas reparaciones hizo.
        /// </summary>
        public int AutoFix()
        {
            if (Map.IsLaneMap) return AutoFixLanes();
            string snap = Snapshot();
            int fixes = 0;
            for (int guard = 0; guard < 60; guard++)
            {
                bool changed = false;
                for (int i = 0; i < Map.roads.Count && !changed; i++)
                {
                    var r = Map.roads[i];
                    var na = Map.Node(r.a); var nb = Map.Node(r.b);
                    if (na == null || nb == null) continue;
                    if (Vector2.Distance(na.Pos, nb.Pos) < 0.6f) { Map.MergeNodes(na.id, nb.id); changed = true; }
                }
                for (int i = 0; i < Map.nodes.Count && !changed; i++)
                {
                    var a = Map.nodes[i];
                    if (a.roundabout) continue;
                    for (int j = i + 1; j < Map.nodes.Count && !changed; j++)
                    {
                        var b = Map.nodes[j];
                        if (b.roundabout) continue;
                        if (Vector2.Distance(a.Pos, b.Pos) < MapValidator.GapTolerance && Map.FindRoad(a.id, b.id) == null)
                        {
                            Map.MergeNodes(a.id, b.id);
                            changed = true;
                        }
                    }
                }
                for (int i = 0; i < Map.nodes.Count && !changed; i++)
                {
                    var n = Map.nodes[i];
                    if (n.roundabout || Map.Degree(n.id) == 0) continue;
                    foreach (var r in Map.roads)
                    {
                        if (r.a == n.id || r.b == n.id) continue;
                        bool sameLayer = true;
                        foreach (var own in Map.RoadsAt(n.id)) if (own.layer != r.layer) sameLayer = false;
                        if (!sameLayer) continue;
                        float dist;
                        float t = Map.NearestParam(r, n.Pos, out dist);
                        if (dist >= MapValidator.TouchTolerance || t <= 0.03f || t >= 0.97f) continue;
                        Map.MoveNode(n.id, Map.PointOnRoad(r, t));
                        if (Map.SplitAtNode(r.id, n.id) != null) changed = true;
                        break;
                    }
                }
                if (!changed) break;
                fixes++;
            }
            if (fixes > 0)
            {
                var all = new List<int>();
                foreach (var r in Map.roads) all.Add(r.id);
                ResolveCrossings(all);
                Map.Normalize();
                Commit(snap);
                Dirty = true;
                ClearSelectionIds();
                Rebuild(true);
            }
            return fixes;
        }

        /// <summary>
        /// Reparación del modelo por carriles: une el final de un carril con el inicio de otro del mismo tipo cuando casi se
        /// tocan, y quita los carriles diminutos fundiendo sus dos nodos. Nunca une carriles paralelos ni crea cruces.
        /// </summary>
        int AutoFixLanes()
        {
            string snap = Snapshot();
            int fixes = 0;
            for (int guard = 0; guard < 80; guard++)
            {
                bool changed = false;
                foreach (var r in Map.roads)
                {
                    if (Map.CenterOf(r).Length >= 0.2f) continue;
                    Map.MergeNodes(r.a, r.b);
                    changed = true;
                    break;
                }
                if (!changed)
                {
                    foreach (var end in Map.roads)
                    {
                        var ne = Map.Node(end.b);
                        if (ne == null || Map.RoadsAt(ne.id).Count != 1) continue;   // solo extremos libres
                        foreach (var start in Map.roads)
                        {
                            if (start == end || start.laneType != end.laneType || start.layer != end.layer) continue;
                            var ns = Map.Node(start.a);
                            if (ns == null || ns.id == ne.id || Map.RoadsAt(ns.id).Count != 1) continue;
                            if (Vector2.Distance(ne.Pos, ns.Pos) > 0.15f) continue;
                            Map.MergeNodes(ne.id, ns.id);
                            changed = true;
                            break;
                        }
                        if (changed) break;
                    }
                }
                if (!changed) break;
                fixes++;
            }
            if (fixes > 0)
            {
                Map.Normalize();
                Commit(snap);
                Dirty = true;
                ClearSelectionIds();
                Rebuild(true);
            }
            return fixes;
        }

        /// <summary>Mensaje concreto del primer problema que impide simular (para avisar al jugador).</summary>
        public string FirstBlockingMessage()
        {
            var first = MapValidator.FirstError(Issues);
            return first == null ? "" : first.message;
        }

        public bool Save()
        {
            string err;
            if (SaveSystem.Save(Map, out err))
            {
                Dirty = false;
                Status = "Mapa guardado: " + Map.name;
                return true;
            }
            Status = err;
            return false;
        }
    }
}
