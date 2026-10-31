using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using IntersectionMaster.Core;
using IntersectionMaster.Data;
using IntersectionMaster.MapEditing;
using IntersectionMaster.Systems;
using IntersectionMaster.Traffic;

namespace IntersectionMaster.UI
{
    /// <summary>
    /// Toda la interfaz táctil en español, construida por código con UIKit: menús, selección de niveles,
    /// mapas guardados, HUD de juego, pausa, resultados, opciones, tutorial y herramientas del editor.
    /// No contiene lógica de juego: lee el estado de GameController y le envía órdenes.
    /// </summary>
    public partial class MobileUIController : MonoBehaviour
    {
        enum MapsPurpose { Manage, PickInfinite, PickFree }

        public GameController game;

        Canvas canvas;
        RectTransform safe;
        Rect lastSafeArea;
        Vector2Int lastScreenSize;

        RectTransform menuRoot, hudRoot, editorRoot;
        RectTransform pauseRoot, resultsRoot, dialogRoot, propsRoot, propsBody, issuesRoot, tableRoot, tableBody;
        Text toast;
        readonly List<Texture2D> thumbTextures = new List<Texture2D>();
        MapsPurpose mapsPurpose = MapsPurpose.Manage;

        // HUD
        Text hudDelivered, hudTime, hudScore, hudAccidents, hudQueue, hudFluidity, hudTutorial;
        Button hudTutorialDone, hudAuto;
        Button[] hudSpeedBtns;
        Image[] hudSpeedCircles;
        Text hudRates;
        RectTransform statsStrip;
        bool signalVisible = true;
        RectTransform signalPanel;
        GameObject tutorialBox;
        Text signalInfo;
        IntersectionController panelFor;
        readonly List<Button> phaseButtons = new List<Button>();
        float hudTimer;
        static readonly float[] Speeds = { 1f, 2f, 4f };

        // Editor
        Text editorStatus, editorTitle;
        int statusSerialSeen = -1;
        float statusShownAt;
        int shownIssuesCount = -1;
        int shownIssueSel = -1;
        int shownIssuesVersion = -1;
        bool shownIssuesFlag;
        RectTransform selPanel;
        string selPanelKey = "";

        // ------------------------------------------------------------------ Ciclo de vida

        void Awake()
        {
            var go = new GameObject("Canvas");
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            uiScaler = scaler;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(720f, 1280f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            safe = UIKit.Rect("SafeArea", go.transform);
            ApplySafeArea();

            toast = UIKit.Label(safe, "", 30, TextAnchor.MiddleCenter, Color.white);
            Place(toast.rectTransform, 0.2f, 1f, 0.8f, 1f, 0, -150, 0, 90);
            var shadow = toast.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.8f);
            toast.transform.SetAsLastSibling();
        }

        void ApplySafeArea()
        {
            Rect sa = Screen.safeArea;
            lastSafeArea = sa;
            lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            if (Screen.width <= 0 || Screen.height <= 0) return;
            safe.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
            safe.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
            safe.offsetMin = Vector2.zero;
            safe.offsetMax = Vector2.zero;
        }

        void Update()
        {
            if (game == null) return;

            if (Screen.safeArea != lastSafeArea || Screen.width != lastScreenSize.x || Screen.height != lastScreenSize.y)
            {
                ApplySafeArea();
                game.RefitCamera();
            }

            toast.text = game.Message;
            toast.transform.SetAsLastSibling();

            if (Input.GetKeyDown(KeyCode.Escape)) BackPressed();

            if (game.Screen == AppScreen.Playing && hudRoot != null) UpdateHud();
            else if (game.Screen == AppScreen.Editor && editorRoot != null)
            {
                UpdateEditor();
                if (game.editor.Tool == EditorTool.Place) ShowHint("decor");
            }
            hintCheck -= Time.unscaledDeltaTime;
            if (hintCheck <= 0f) { hintCheck = 0.5f; UpdateHints(); }
        }

        float hintCheck;

        public bool IsOverUi(Vector2 screenPos)
        {
            return UIKit.PointerOverUi(screenPos);
        }

        // ------------------------------------------------------------------ Cambios de pantalla

        public void OnScreenChanged()
        {
            DestroyOverlays();
            DestroyRoot(ref menuRoot);
            DestroyRoot(ref hudRoot);
            DestroyRoot(ref editorRoot); DestroyRoot(ref edMenuRoot); DestroyRoot(ref profRoot); profBody = null;
            ClearThumbs();
            panelFor = null;

            switch (game.Screen)
            {
                case AppScreen.Main: BuildMain(); break;
                case AppScreen.Modes: BuildModes(); break;
                case AppScreen.Campaign: BuildLevelList("Campaña", LevelManager.CampaignLevels); break;
                case AppScreen.Challenges: BuildLevelList("Desafíos", LevelManager.ChallengeLevels); break;
                case AppScreen.Maps: BuildMaps(); break;
                case AppScreen.Options: BuildOptions(); break;
                case AppScreen.Rewards: BuildRewards(); break;
                case AppScreen.Playing: BuildHud(); HintsOnPlayStart(); break;
                case AppScreen.Editor: BuildEditorUi(); ShowHint("editor"); break;
            }
            toast.transform.SetAsLastSibling();
        }

        void BackPressed()
        {
            if (dialogRoot != null) { CloseDialog(); return; }
            switch (game.Screen)
            {
                case AppScreen.Main: break;
                case AppScreen.Modes: game.GoTo(AppScreen.Main); break;
                case AppScreen.Campaign:
                case AppScreen.Challenges: game.GoTo(AppScreen.Modes); break;
                case AppScreen.Maps: game.GoTo(mapsPurpose == MapsPurpose.Manage ? AppScreen.Main : AppScreen.Modes); break;
                case AppScreen.Options: game.GoTo(AppScreen.Main); break;
                case AppScreen.Rewards: game.GoTo(AppScreen.Main); break;
                case AppScreen.Playing:
                    if (resultsRoot != null) return;
                    if (pauseRoot != null) ResumeFromPause(); else ShowPause();
                    break;
                case AppScreen.Editor:
                    if (menuRoot != null) DestroyRoot(ref menuRoot);
                    else if (tableRoot != null) { UnityEngine.Object.Destroy(tableRoot.gameObject); tableRoot = null; tableBody = null; }
                    else if (propsRoot != null) { UnityEngine.Object.Destroy(propsRoot.gameObject); propsRoot = null; }
                    else EditorBack();
                    break;
            }
        }

        // ------------------------------------------------------------------ Utilidades de diseño

        static RectTransform Place(RectTransform rt, float minX, float minY, float maxX, float maxY,
            float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = new Vector2(minX, minY);
            rt.anchorMax = new Vector2(maxX, maxY);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Altura de un cuadro de diálogo limitada a la pantalla (por si está en horizontal).</summary>
        float BoxHeight(float wanted)
        {
            float h = safe != null ? safe.rect.height : 0f;
            return h > 100f ? Mathf.Min(wanted, h - 30f) : wanted;
        }

        static RectTransform Rt(Component c) { return c.GetComponent<RectTransform>(); }

        void DestroyRoot(ref RectTransform r)
        {
            if (r != null) UnityEngine.Object.Destroy(r.gameObject);
            r = null;
        }

        void DestroyOverlays()
        {
            DestroyRoot(ref pauseRoot);
            DestroyRoot(ref resultsRoot);
            DestroyRoot(ref dialogRoot);
            DestroyRoot(ref propsRoot);
            DestroyRoot(ref issuesRoot);
            DestroyRoot(ref diagRoot);
            DestroyRoot(ref allowRoot);
            DestroyRoot(ref layerRoot);
            DestroyRoot(ref optRoot);
            DestroyRoot(ref tableRoot);
            DestroyRoot(ref menuRoot);
            propsBody = null;
            tableBody = null;
        }

        void ClearThumbs()
        {
            for (int i = 0; i < thumbTextures.Count; i++)
                if (thumbTextures[i] != null) UnityEngine.Object.Destroy(thumbTextures[i]);
            thumbTextures.Clear();
        }

        RectTransform NewRoot(string name, Color color)
        {
            var img = UIKit.Panel(safe, name, color);
            UIKit.Stretch(img.rectTransform);
            return img.rectTransform;
        }

        /// <summary>Pantalla de menú con título, cuerpo central y botón Atrás.</summary>
        RectTransform MenuScreen(string title, Action back, out RectTransform body)
        {
            menuRoot = NewRoot("Menu", UIKit.Bg);
            var t = UIKit.Label(menuRoot, title, 56, TextAnchor.MiddleCenter);
            Place(Rt(t), 0, 1, 1, 1, 130, -100, 130, 10);
            if (back != null)
            {
                var b = UIKit.Button(menuRoot, "Atrás", UIKit.Neutral, () => { AudioManager.Instance.PlayClick(); back(); }, 30);
                Place(Rt(b), 0, 1, 0, 1, 16, -90, 0, 14);
                Rt(b).sizeDelta = new Vector2(110, 70);
                Rt(b).anchorMax = new Vector2(0, 1);
                Rt(b).offsetMin = new Vector2(16, -86);
                Rt(b).offsetMax = new Vector2(126, -16);
            }
            body = UIKit.Rect("Body", menuRoot);
            Place(body, 0, 0, 1, 1, 24, 16, 24, 110);
            return menuRoot;
        }

        Button Big(Transform parent, string text, Color color, Action act, float height = 84)
        {
            var b = UIKit.Button(parent, text, color, () => { AudioManager.Instance.PlayClick(); act(); }, 34);
            UIKit.Size(b, -1, height);
            return b;
        }

        RectTransform Column(RectTransform body, float width)
        {
            var col = UIKit.Rect("Column", body);
            col.anchorMin = new Vector2(0.5f, 0f);
            col.anchorMax = new Vector2(0.5f, 1f);
            col.sizeDelta = new Vector2(width, 0f);
            col.anchoredPosition = Vector2.zero;
            var v = UIKit.VLayout(col, 14, 0);
            v.childAlignment = TextAnchor.MiddleCenter;
            return col;
        }

        // ------------------------------------------------------------------ Menú principal

        void BuildMain()
        {
            RectTransform body;
            MenuScreen("", null, out body);
            // Fondo semitransparente: se ve el mapa con tráfico que se mueve detrás (como el original).
            if (game.BackgroundRunning)
            {
                var bgImg = menuRoot.GetComponent<Image>();
                if (bgImg != null) { var c = UIKit.Bg; c.a = 0.35f; bgImg.color = c; }
            }
            var title = UIKit.Label(menuRoot, "INTERSECTION MASTER", 72, TextAnchor.MiddleCenter, Color.white);
            title.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            Place(Rt(title), 0, 0.72f, 1, 1, 20, 0, 20, 30);
            var sub = UIKit.Label(menuRoot, "Controla el tráfico de la ciudad", 32, TextAnchor.MiddleCenter, new Color(0.7f, 0.78f, 0.9f));
            Place(Rt(sub), 0, 0.64f, 1, 0.74f);

            body.offsetMax = new Vector2(-24, -250);
            var col = Column(body, 520);
            Big(col, "Jugar", UIKit.Good, () => game.GoTo(AppScreen.Modes), 84);
            Big(col, "Editor de mapas", UIKit.Accent, () => { mapsPurpose = MapsPurpose.Manage; game.GoTo(AppScreen.Maps); }, 72);
            Big(col, "Recompensas", UIKit.Accent, () => game.GoTo(AppScreen.Rewards), 72);
            Big(col, "Tutorial", UIKit.Neutral, () => game.StartTutorial(), 72);
            Big(col, "Opciones", UIKit.Neutral, () => game.GoTo(AppScreen.Options), 72);

            var stars = UIKit.Label(menuRoot, "Rango: " + game.Rank() + "  ·  Estrellas: " + game.TotalStars(), 26, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.35f));
            Place(Rt(stars), 0, 0, 1, 0, 0, 10, 0, -50);
            Rt(stars).anchorMax = new Vector2(1, 0);
            Rt(stars).offsetMin = new Vector2(0, 8);
            Rt(stars).offsetMax = new Vector2(0, 54);

            if (!SaveSystem.Progress.tutorialSeen)
            {
                var hint = UIKit.Label(menuRoot, "Primera vez: prueba el Tutorial", 26, TextAnchor.MiddleCenter, new Color(0.6f, 0.9f, 0.7f));
                Place(Rt(hint), 0, 0, 1, 0);
                Rt(hint).offsetMin = new Vector2(0, 54);
                Rt(hint).offsetMax = new Vector2(0, 94);
            }
        }

        // ------------------------------------------------------------------ Modos

        void BuildModes()
        {
            RectTransform body;
            MenuScreen("Modos de juego", () => game.GoTo(AppScreen.Main), out body);
            var col = Column(body, 640);
            Big(col, "Campaña", UIKit.Good, () => game.GoTo(AppScreen.Campaign));
            Big(col, "Desafíos", UIKit.Accent, () => game.GoTo(AppScreen.Challenges));
            Big(col, "Modo infinito", UIKit.Neutral, () => { mapsPurpose = MapsPurpose.PickInfinite; game.GoTo(AppScreen.Maps); });
            Big(col, "Modo libre", UIKit.Neutral, () => { mapsPurpose = MapsPurpose.PickFree; game.GoTo(AppScreen.Maps); });
        }

        // ------------------------------------------------------------------ Niveles

        void BuildLevelList(string title, List<LevelDefinition> list)
        {
            RectTransform body;
            MenuScreen(title, () => game.GoTo(AppScreen.Modes), out body);
            RectTransform scrollRoot;
            var content = UIKit.ScrollList(body, out scrollRoot);
            UIKit.Stretch(scrollRoot);

            // En los desafíos, arriba del todo el reto del día (siempre disponible).
            if (list == LevelManager.ChallengeLevels) AddLevelRow(content, LevelManager.Daily(), true);

            if (list == LevelManager.CampaignLevels)
            {
                // Mundos de la campaña (como el original): una fila de botones y debajo los 12 niveles del elegido.
                var tabs = UIKit.Rect("Worlds", content);
                UIKit.Size(tabs, -1, 104);
                UIKit.HLayout(tabs, 8, 0);
                for (int w = 0; w < LevelManager.WorldTabs; w++)
                {
                    int wi = w;
                    int first = 0;
                    for (int i = 0; i < list.Count; i++) if (LevelManager.WorldOf(i) == w) { first = i; break; }
                    bool open = game.IsLevelUnlocked(list, first);
                    string label = LevelManager.WorldName(w) + "\n<size=18>" + (open ? LevelManager.WinsInWorld(w) + "/" + LevelManager.LevelsInWorld(w) : "Bloqueado") + "</size>";
                    var b = UIKit.Button(tabs, label, w == campaignWorld ? UIKit.Orange : (open ? UIKit.Neutral : UIKit.Dim), () =>
                    {
                        AudioManager.Instance.PlayClick();
                        campaignWorld = wi;
                        game.GoTo(AppScreen.Campaign);
                    }, 20);
                    var t = b.GetComponentInChildren<Text>();
                    if (t != null) t.supportRichText = true;
                    UIKit.Size(b, -1, -1, 1f);
                }
                var help = UIKit.Label(content, LevelManager.WorldHelp(campaignWorld) + (campaignWorld > 0 && campaignWorld < CampaignMaps.Worlds ? "  (Se abre al superar " + LevelManager.WorldUnlockWins + " niveles del mundo anterior.)" : ""),
                    22, TextAnchor.MiddleLeft, new Color(0.8f, 0.85f, 0.92f));
                UIKit.Size(help, -1, 60);
                for (int i = 0; i < list.Count; i++)
                    if (LevelManager.WorldOf(i) == campaignWorld) AddLevelRow(content, list[i], game.IsLevelUnlocked(list, i));
                return;
            }
            for (int i = 0; i < list.Count; i++)
                AddLevelRow(content, list[i], game.IsLevelUnlocked(list, i));
        }

        int campaignWorld;

        void AddLevelRow(RectTransform content, LevelDefinition lv, bool unlocked)
        {
            int stars = SaveSystem.Stars(lv.id);

            var row = UIKit.Panel(content, "Level_" + lv.id, UIKit.PanelCol);
            UIKit.Size(row, -1, 120);
            UIKit.HLayout(row.rectTransform, 14, 12);

            var texts = UIKit.Rect("Texts", row.transform);
            UIKit.Size(texts, -1, -1, 1f);
            UIKit.VLayout(texts, 2, 0);
            var name = UIKit.Label(texts, lv.title, 34, TextAnchor.MiddleLeft);
            UIKit.Size(name, -1, 44);
            var desc = UIKit.Label(texts, unlocked ? lv.description : "Bloqueado: supera el nivel anterior.", 24, TextAnchor.UpperLeft,
                unlocked ? new Color(0.8f, 0.85f, 0.92f) : new Color(0.6f, 0.6f, 0.6f));
            UIKit.Size(desc, -1, 60);

            var starText = UIKit.Label(row.transform, stars > 0 ? "Estrellas: " + stars + "/3" : "Sin estrellas", 26, TextAnchor.MiddleCenter,
                stars > 0 ? new Color(1f, 0.85f, 0.35f) : new Color(0.7f, 0.7f, 0.7f));
            UIKit.Size(starText, 150, -1);

            var play = UIKit.Button(row.transform, unlocked ? "Jugar" : "Bloqueado", unlocked ? UIKit.Good : UIKit.Dim,
                () => { if (unlocked) { AudioManager.Instance.PlayClick(); ShowLevelModes(lv); } }, 32);
            play.interactable = unlocked;
            UIKit.Size(play, 150, -1);
        }

        /// <summary>Ventana del nivel: objetivo, sin fin y puntuación más alta (ranking local).</summary>
        void ShowLevelModes(LevelDefinition lv)
        {
            CloseDialog();
            dialogRoot = NewRoot("Dialog", new Color(0, 0, 0, 0.7f));
            var box = UIKit.Panel(dialogRoot, "Box", UIKit.PanelCol);
            box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            box.rectTransform.sizeDelta = new Vector2(640, BoxHeight(760f));

            var hdr = UIKit.Panel(box.transform, "Header", UIKit.Orange);
            hdr.rectTransform.anchorMin = new Vector2(0, 1); hdr.rectTransform.anchorMax = new Vector2(1, 1);
            hdr.rectTransform.offsetMin = new Vector2(0, -56); hdr.rectTransform.offsetMax = Vector2.zero;
            var ht = UIKit.Label(hdr.transform, lv.title, 30, TextAnchor.MiddleLeft);
            UIKit.Stretch(ht.rectTransform, 16, 0, 60, 0);
            var close = UIKit.Button(hdr.transform, "X", UIKit.Neutral, () => CloseDialog(), 26);
            close.GetComponent<RectTransform>().anchorMin = new Vector2(1, 0);
            close.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);
            close.GetComponent<RectTransform>().offsetMin = new Vector2(-54, 4);
            close.GetComponent<RectTransform>().offsetMax = new Vector2(-4, -4);

            var col = UIKit.Rect("Rows", box.transform);
            col.anchorMin = new Vector2(0, 0); col.anchorMax = new Vector2(1, 1);
            col.offsetMin = new Vector2(16, 16); col.offsetMax = new Vector2(-16, -66);
            var v = UIKit.VLayout(col, 8, 0);
            v.childAlignment = TextAnchor.UpperCenter;

            string objective = lv.objective.targetDelivered > 0
                ? "Alcanzar objetivo: " + lv.objective.targetDelivered
                : "Sobrevivir " + Mathf.RoundToInt(lv.objective.timeLimitSeconds) + " s";
            ModeRow(col, objective, () => { CloseDialog(); game.StartLevel(lv); });
            ModeRow(col, "Sin límite", () => { CloseDialog(); game.StartLevelMode(lv, GameMode.Free); });
            ModeRow(col, "Puntuación más alta", () => { CloseDialog(); game.StartLevelMode(lv, GameMode.Infinite); });
            ModeRow(col, "Contador de puntuación (6 min)", () => { CloseDialog(); game.StartLevelMode(lv, GameMode.ScoreCounter); });

            var top = SaveSystem.TopScores(lv.id);
            var sb = new StringBuilder();
            for (int i = 0; i < top.Count && i < 10; i++)
                sb.Append(i + 1).Append(":   ").Append(top[i].score).Append("    (").Append(FormatTime(top[i].seconds)).Append(")\n");
            if (top.Count == 0) sb.Append("Aún no hay puntuaciones.\n");
            sb.Append("Puntuación más alta propia: ").Append(top.Count > 0 ? top[0].score : 0);
            var list = UIKit.Label(col, sb.ToString(), 22, TextAnchor.UpperLeft, new Color(0.9f, 0.92f, 1f));
            UIKit.Size(list, -1, 300);
        }

        void ModeRow(RectTransform parent, string text, Action act)
        {
            var b = UIKit.Button(parent, "Jugar:  " + text, UIKit.Dim, () => { AudioManager.Instance.PlayClick(); act(); }, 28);
            UIKit.Size(b, -1, 84);
        }

        // ------------------------------------------------------------------ Recompensas

        void BuildRewards()
        {
            RectTransform body;
            MenuScreen("Recompensas", () => game.GoTo(AppScreen.Main), out body);
            body.offsetMax = new Vector2(-24, -150);

            var head = UIKit.Label(menuRoot, "Estrellas: " + game.TotalStars() + "   ·   Rango: " + game.Rank(), 30, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.35f));
            Place(Rt(head), 0, 1, 1, 1, 24, -150, 24, 100);

            RectTransform scrollRoot;
            var content = UIKit.ScrollList(body, out scrollRoot);
            UIKit.Stretch(scrollRoot);

            foreach (var reward in RewardSystem.All())
            {
                var rw = reward;
                var row = UIKit.Panel(content, "Reward_" + rw.id, UIKit.PanelCol);
                UIKit.Size(row, -1, 104);
                UIKit.HLayout(row.rectTransform, 14, 12);

                var texts = UIKit.Rect("Texts", row.transform);
                UIKit.Size(texts, -1, -1, 1f);
                UIKit.VLayout(texts, 2, 0);
                var name = UIKit.Label(texts, rw.title, 32, TextAnchor.MiddleLeft, rw.unlocked ? Color.white : new Color(0.65f, 0.65f, 0.65f));
                UIKit.Size(name, -1, 44);
                var desc = UIKit.Label(texts, rw.description, 24, TextAnchor.UpperLeft, new Color(0.78f, 0.83f, 0.92f));
                UIKit.Size(desc, -1, 36);

                string label = !rw.unlocked ? "Bloqueado" : (rw.selected ? "En uso" : "Usar");
                var btn = UIKit.Button(row.transform, label, rw.selected ? UIKit.Accent : (rw.unlocked ? UIKit.Good : UIKit.Dim), () =>
                {
                    if (RewardSystem.Select(rw)) { AudioManager.Instance.PlayClick(); game.GoTo(AppScreen.Rewards); }
                }, 30);
                btn.interactable = rw.unlocked && !rw.selected;
                UIKit.Size(btn, 200, -1);
            }
        }

        // ------------------------------------------------------------------ Mapas

        void BuildMaps()
        {
            string title = mapsPurpose == MapsPurpose.Manage ? "Mis mapas" :
                (mapsPurpose == MapsPurpose.PickInfinite ? "Modo infinito: elige un mapa" : "Modo libre: elige un mapa");
            RectTransform body;
            MenuScreen(title, () => game.GoTo(mapsPurpose == MapsPurpose.Manage ? AppScreen.Main : AppScreen.Modes), out body);

            RectTransform listArea = body;
            if (mapsPurpose == MapsPurpose.Manage)
            {
                var bar = UIKit.Rect("Actions", menuRoot);
                Place(bar, 0, 1, 1, 1, 140, -190, 24, 100);
                bar.anchoredPosition = new Vector2(0, -150);
                bar.offsetMin = new Vector2(24, -190);
                bar.offsetMax = new Vector2(-24, -110);
                UIKit.HLayout(bar, 14, 0);
                var nb = UIKit.Button(bar, "+ Nuevo mapa", UIKit.Good, () => { AudioManager.Instance.PlayClick(); game.OpenEditor(MapEditorController.CreateBlankMap(), true); }, 30);
                var ib = UIKit.Button(bar, "Importar archivos", UIKit.Accent, () =>
                {
                    AudioManager.Instance.PlayClick();
                    // En Android: selector de archivos del sistema (Descargas, Drive, WhatsApp…), también para los
                    // .trzmap de Intersection Controller. En el PC: la carpeta de importación.
                    if (FilePicker.Available)
                        FilePicker.PickAndImport(msg => Info("Importar mapas", msg, () => game.GoTo(AppScreen.Maps)));
                    else
                        Info("Importar mapas", SaveSystem.ImportAll(), () => game.GoTo(AppScreen.Maps));
                }, 30);
                var pb = UIKit.Button(bar, "Pegar código", UIKit.Accent, () =>
                {
                    AudioManager.Instance.PlayClick();
                    Info("Importar código", MapShare.ImportFromClipboard(), () => game.GoTo(AppScreen.Maps));
                }, 30);
                body.offsetMax = new Vector2(-24, -200);
            }

            RectTransform scrollRoot;
            var content = UIKit.ScrollList(listArea, out scrollRoot);
            UIKit.Stretch(scrollRoot);

            var all = new List<MapInfo>();
            if (mapsPurpose == MapsPurpose.Manage) all.AddRange(SaveSystem.ListSavedMaps());
            all.AddRange(SaveSystem.ListSamples());

            if (all.Count == 0)
            {
                var empty = UIKit.Label(content, "No hay mapas.", 30);
                UIKit.Size(empty, -1, 80);
            }

            for (int i = 0; i < all.Count; i++) AddMapRow(content, all[i]);
        }

        void AddMapRow(RectTransform content, MapInfo info)
        {
            if (!string.IsNullOrEmpty(info.error)) { AddBrokenMapRow(content, info); return; }
            var row = UIKit.Panel(content, "Map_" + info.id, UIKit.PanelCol);
            UIKit.Size(row, -1, 120);
            UIKit.HLayout(row.rectTransform, 12, 10);

            var thumbHolder = UIKit.Rect("Thumb", row.transform);
            UIKit.Size(thumbHolder, 150, -1);
            Texture2D tex = MapSerializationSystem.ThumbnailTexture(info.thumbnail);
            if (tex == null)
            {
                var m = SampleMaps.Find(info.id);
                if (m != null) tex = MapSerializationSystem.ThumbnailTexture(MapSerializationSystem.MakeThumbnail(m));
            }
            if (tex != null)
            {
                thumbTextures.Add(tex);
                var raw = thumbHolder.gameObject.AddComponent<RawImage>();
                raw.texture = tex;
                raw.raycastTarget = false;
            }

            var texts = UIKit.Rect("Texts", row.transform);
            UIKit.Size(texts, -1, -1, 1f);
            UIKit.VLayout(texts, 2, 0);
            var n = UIKit.Label(texts, info.name + (info.isSample ? "  (ejemplo)" : ""), 32, TextAnchor.MiddleLeft);
            UIKit.Size(n, -1, 42);
            string detail = info.width.ToString("0") + "x" + info.height.ToString("0") + "  ·  dificultad " + info.difficulty + "  ·  " + info.author;
            var d = UIKit.Label(texts, detail, 22, TextAnchor.UpperLeft, new Color(0.75f, 0.8f, 0.9f));
            UIKit.Size(d, -1, 30);

            string id = info.id;
            if (mapsPurpose == MapsPurpose.Manage)
            {
                var play = UIKit.Button(row.transform, "Jugar", UIKit.Good, () => PlayMapById(id, GameMode.Free), 26);
                UIKit.Size(play, 110, -1);
                var edit = UIKit.Button(row.transform, "Editar", UIKit.Accent, () =>
                {
                    var map = SaveSystem.Load(id);
                    if (map == null) { game.ShowMessage("No se pudo abrir el mapa."); return; }
                    game.OpenEditor(map, false);
                    if (info.isSample) game.editor.Status = "Mapa de ejemplo: al guardar se crea tu propia copia.";
                }, 26);
                UIKit.Size(edit, 110, -1);
                var dup = UIKit.Button(row.transform, "Copiar", UIKit.Neutral, () =>
                {
                    var copy = SaveSystem.Duplicate(id);
                    game.ShowMessage(copy != null ? "Mapa duplicado." : "No se pudo duplicar.");
                    game.GoTo(AppScreen.Maps);
                }, 26);
                UIKit.Size(dup, 110, -1);
                var share = UIKit.Button(row.transform, "Código", UIKit.Neutral, () =>
                {
                    var toShare = SaveSystem.Load(id);
                    if (toShare == null) { game.ShowMessage("No se pudo abrir el mapa."); return; }
                    GUIUtility.systemCopyBuffer = MapShare.Encode(toShare);
                    Info("Compartir mapa", "Código copiado al portapapeles. Pégalo en un mensaje: quien lo reciba usa «Pegar código» en Mis mapas.", null);
                }, 24);
                UIKit.Size(share, 110, -1);
                if (!info.isSample)
                {
                    var exp = UIKit.Button(row.transform, "Exportar", UIKit.Neutral, () =>
                    {
                        string err;
                        string path = SaveSystem.Export(id, out err);
                        Info("Exportar mapa", path != null ? "Archivo creado en:\n" + path : "Error: " + err, null);
                    }, 24);
                    UIKit.Size(exp, 120, -1);
                    string mapName = info.name;
                    var del = UIKit.Button(row.transform, "Borrar", UIKit.Bad, () =>
                    {
                        Confirm("¿Borrar «" + mapName + "»? Esta acción no se puede deshacer.", "Borrar", () =>
                        {
                            SaveSystem.Delete(id);
                            game.GoTo(AppScreen.Maps);
                        }, null, null);
                    }, 26);
                    UIKit.Size(del, 110, -1);
                }
            }
            else
            {
                GameMode mode = mapsPurpose == MapsPurpose.PickInfinite ? GameMode.Infinite : GameMode.Free;
                var pick = UIKit.Button(row.transform, "Elegir", UIKit.Good, () => PlayMapById(id, mode), 30);
                UIKit.Size(pick, 160, -1);
            }
        }

        /// <summary>Mapa guardado que no se pudo leer: se muestra con el motivo (para no perderlo de vista) y se puede borrar.</summary>
        void AddBrokenMapRow(RectTransform content, MapInfo info)
        {
            if (mapsPurpose != MapsPurpose.Manage) return;
            var row = UIKit.Panel(content, "Map_" + info.id, new Color(0.35f, 0.16f, 0.16f));
            UIKit.Size(row, -1, 120);
            UIKit.HLayout(row.rectTransform, 12, 10);
            var texts = UIKit.Rect("Texts", row.transform);
            UIKit.Size(texts, -1, -1, 1f);
            UIKit.VLayout(texts, 2, 0);
            var n = UIKit.Label(texts, info.name, 30, TextAnchor.MiddleLeft);
            UIKit.Size(n, -1, 40);
            string why = info.error.Length > 140 ? info.error.Substring(0, 140) + "…" : info.error;
            var d = UIKit.Label(texts, why, 20, TextAnchor.UpperLeft, new Color(1f, 0.85f, 0.8f));
            UIKit.Size(d, -1, 60);
            string id = info.id, full = info.error;
            var more = UIKit.Button(row.transform, "Detalle", UIKit.Neutral, () => Info("Mapa que no se pudo leer", "Archivo: " + id + "\n\n" + full, null), 24);
            UIKit.Size(more, 120, -1);
            var del = UIKit.Button(row.transform, "Borrar", UIKit.Bad, () =>
            {
                Confirm("¿Borrar este mapa que no se puede leer? Esta acción no se puede deshacer.", "Borrar", () =>
                {
                    SaveSystem.Delete(id);
                    game.GoTo(AppScreen.Maps);
                }, null, null);
            }, 26);
            UIKit.Size(del, 110, -1);
        }

        void PlayMapById(string id, GameMode mode)
        {
            AudioManager.Instance.PlayClick();
            var map = SaveSystem.Load(id);
            if (map == null) { game.ShowMessage("No se pudo abrir el mapa."); return; }
            ShowSimModeDialog(map.traffic.simMode, map.traffic.chaosLevel, false, (m, level, remember) =>
            {
                map.traffic.simMode = m; map.traffic.chaosLevel = level;
                game.StartMapPlay(map, mode);
            });
        }

        /// <summary>Selección previa a la simulación: modo realista o caótico (con intensidad). El mismo mapa sirve para ambos.</summary>
        void ShowSimModeDialog(int mode, int level, bool canRemember, Action<int, int, bool> go)
        {
            // Sin ventana de modo realista/caótico (se quitó el modo caótico): se juega directamente en realista.
            go(0, 1, false);
        }

        void ShowSimModeDialogOld(int mode, int level, bool canRemember, Action<int, int, bool> go)
        {
            CloseDialog();
            dialogRoot = NewRoot("Dialog", new Color(0, 0, 0, 0.7f));
            var box = UIKit.Panel(dialogRoot, "Box", UIKit.PanelCol);
            box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            box.rectTransform.sizeDelta = new Vector2(760, BoxHeight(720f));
            var title = UIKit.Label(box.transform, "Modo de simulación", 38, TextAnchor.MiddleCenter, Color.white);
            Place(Rt(title), 0, 0.88f, 1, 1, 20, 0, 20, 6);

            var body = UIKit.Rect("Body", box.transform);
            Place(body, 0, 0.16f, 1, 0.88f, 24, 0, 24, 0);
            UIKit.VLayout(body, 10, 0);

            bool remember = canRemember;
            Action rebuild = null;
            rebuild = () =>
            {
                UIKit.ClearChildren(body);
                var b1 = UIKit.Button(body, "1. Modo realista", mode == 0 ? UIKit.Good : UIKit.Dim, () => { mode = 0; rebuild(); }, 30);
                UIKit.Size(b1, -1, 70);
                var d1 = UIKit.Label(body, "Los vehículos respetan semáforos, stops, cesiones, direcciones y restricciones de carril; mantienen distancias y evitan chocar.", 22, TextAnchor.MiddleCenter, new Color(0.85f, 0.9f, 1f));
                UIKit.Size(d1, -1, 80);
                var b2 = UIKit.Button(body, "2. Modo caótico", mode == 1 ? UIKit.Bad : UIKit.Dim, () => { mode = 1; rebuild(); }, 30);
                UIKit.Size(b2, -1, 70);
                var d2 = UIKit.Label(body, "Una parte de los conductores ignora semáforos, señales, prioridades y carriles exclusivos, corre más y se pega al de delante: pueden producirse choques y obstrucciones.", 22, TextAnchor.MiddleCenter, new Color(0.85f, 0.9f, 1f));
                UIKit.Size(d2, -1, 100);
                if (mode == 1)
                {
                    var row = UIKit.Rect("Levels", body);
                    UIKit.Size(row, -1, 64);
                    UIKit.HLayout(row, 6, 0);
                    for (int i = 0; i < 4; i++)
                    {
                        int lv = i;
                        UIKit.Button(row, ChaosProfile.Names[i].Replace("Caos ", ""), level == lv ? UIKit.Orange : UIKit.Neutral, () => { level = lv; rebuild(); }, 24);
                    }
                    var inf = UIKit.Label(body, "Infracción: " + Mathf.RoundToInt(ChaosProfile.Violation(level) * 100f) + "% de los vehículos · velocidad ×" + ChaosProfile.Speed(level).ToString("0.00"), 22, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.5f));
                    UIKit.Size(inf, -1, 40);
                }
                if (canRemember)
                {
                    var rb = UIKit.Button(body, remember ? "Guardar esta elección en el mapa: sí" : "Guardar esta elección en el mapa: no", remember ? UIKit.Accent : UIKit.Neutral, () => { remember = !remember; rebuild(); }, 22);
                    UIKit.Size(rb, -1, 54);
                }
            };
            rebuild();

            var bar = UIKit.Rect("Buttons", box.transform);
            Place(bar, 0, 0, 1, 0.15f, 20, 12, 20, 0);
            UIKit.HLayout(bar, 14, 0);
            UIKit.Button(bar, "Cancelar", UIKit.Neutral, () => CloseDialog(), 30);
            UIKit.Button(bar, "Iniciar", UIKit.Good, () => { CloseDialog(); go(mode, level, remember); }, 32);
        }

        // ------------------------------------------------------------------ Opciones

        void BuildOptions()
        {
            RectTransform body;
            MenuScreen("Opciones", () => game.GoTo(AppScreen.Main), out body);
            var col = Column(body, 640);
            var p = SaveSystem.Progress;

            var snd = Big(col, "Sonido: " + (p.soundOn ? "activado" : "desactivado"), UIKit.Neutral, () => { }, 62);
            snd.onClick.AddListener(() =>
            {
                p.soundOn = !p.soundOn;
                SaveSystem.SaveProgress();
                game.ApplyOptions();
                UIKit.SetText(snd, "Sonido: " + (p.soundOn ? "activado" : "desactivado"));
            });

            var bat = Big(col, "Ahorro de batería (30 FPS): " + (p.batterySaver ? "sí" : "no"), UIKit.Neutral, () => { }, 62);
            bat.onClick.AddListener(() =>
            {
                p.batterySaver = !p.batterySaver;
                SaveSystem.SaveProgress();
                game.ApplyOptions();
                UIKit.SetText(bat, "Ahorro de batería (30 FPS): " + (p.batterySaver ? "sí" : "no"));
            });

            var ambient = Big(col, "Sonido ambiente: " + (p.ambientOn ? "sí" : "no"), UIKit.Neutral, () => { }, 62);
            ambient.onClick.AddListener(() =>
            {
                p.ambientOn = !p.ambientOn;
                SaveSystem.SaveProgress();
                game.ApplyOptions();
                UIKit.SetText(ambient, "Sonido ambiente: " + (p.ambientOn ? "sí" : "no"));
            });

            var assist = Big(col, "Semáforos accesibles (formas): " + (p.colorAssist ? "sí" : "no"), UIKit.Neutral, () => { }, 62);
            assist.onClick.AddListener(() =>
            {
                p.colorAssist = !p.colorAssist;
                SaveSystem.SaveProgress();
                UIKit.SetText(assist, "Semáforos accesibles (formas): " + (p.colorAssist ? "sí" : "no"));
            });

            int[] autos = { 0, 2, 5, 10 };
            Func<string> autoTxt = () => "Autoguardar en el editor: " + (p.autosaveMinutes <= 0 ? "no" : "cada " + p.autosaveMinutes + " min");
            var aut = Big(col, autoTxt(), UIKit.Neutral, () => { }, 62);
            aut.onClick.AddListener(() =>
            {
                int i = Array.IndexOf(autos, p.autosaveMinutes);
                p.autosaveMinutes = autos[(i + 1) % autos.Length];
                SaveSystem.SaveProgress();
                UIKit.SetText(aut, autoTxt());
            });
            var lz = Big(col, "Ver zona de toque de los semáforos: " + (p.showLightZone ? "sí" : "no"), UIKit.Neutral, () => { }, 62);
            lz.onClick.AddListener(() =>
            {
                p.showLightZone = !p.showLightZone;
                SaveSystem.SaveProgress();
                UIKit.SetText(lz, "Ver zona de toque de los semáforos: " + (p.showLightZone ? "sí" : "no"));
            });

            var roc = Big(col, "Luces a rojo en un accidente: " + (p.redOnCrash ? "sí" : "no"), UIKit.Neutral, () => { }, 62);
            roc.onClick.AddListener(() =>
            {
                p.redOnCrash = !p.redOnCrash;
                SaveSystem.SaveProgress();
                UIKit.SetText(roc, "Luces a rojo en un accidente: " + (p.redOnCrash ? "sí" : "no"));
            });

            var ht = Big(col, "Tutoriales en contexto: " + (p.hintsEnabled ? "sí" : "no"), UIKit.Neutral, () => { }, 62);
            ht.onClick.AddListener(() =>
            {
                p.hintsEnabled = !p.hintsEnabled;
                SaveSystem.SaveProgress();
                UIKit.SetText(ht, "Tutoriales en contexto: " + (p.hintsEnabled ? "sí" : "no"));
            });
            Big(col, "Volver a ver los tutoriales", UIKit.Neutral, () =>
            {
                p.hintsSeen.Clear(); p.hintsEnabled = true; SaveSystem.SaveProgress();
                game.ShowMessage("Los tutoriales en contexto saldrán otra vez.");
                game.GoTo(AppScreen.Options);
            }, 62);
            var un = Big(col, "Unidades de velocidad: " + (p.mph ? "mph" : "km/h"), UIKit.Neutral, () => { }, 62);
            un.onClick.AddListener(() =>
            {
                p.mph = !p.mph;
                SaveSystem.SaveProgress();
                UIKit.SetText(un, "Unidades de velocidad: " + (p.mph ? "mph" : "km/h"));
            });
            var mb = Big(col, "Mapa de fondo en el menú: " + (p.menuBackground ? "sí" : "no"), UIKit.Neutral, () => { }, 62);
            mb.onClick.AddListener(() =>
            {
                p.menuBackground = !p.menuBackground;
                SaveSystem.SaveProgress();
                UIKit.SetText(mb, "Mapa de fondo en el menú: " + (p.menuBackground ? "sí" : "no"));
            });
            var gr = Big(col, "Grano en el asfalto: " + (p.roadGrain ? "sí" : "no"), UIKit.Neutral, () => { }, 62);
            gr.onClick.AddListener(() =>
            {
                p.roadGrain = !p.roadGrain;
                SaveSystem.SaveProgress();
                UIKit.SetText(gr, "Grano en el asfalto: " + (p.roadGrain ? "sí" : "no"));
            });
            System.Func<string> grainTxt = () => "Fuerza del grano: " + Mathf.RoundToInt(p.grainAmount * 100f) + "%";
            var ga = Big(col, grainTxt(), UIKit.Neutral, () => { }, 62);
            ga.onClick.AddListener(() =>
            {
                float[] steps = { 0.5f, 1f, 1.5f, 2f };
                int i = 0; for (int k = 0; k < steps.Length; k++) if (Mathf.Abs(steps[k] - p.grainAmount) < 0.01f) i = k;
                p.grainAmount = steps[(i + 1) % steps.Length];
                SaveSystem.SaveProgress();
                UIKit.SetText(ga, grainTxt());
            });
            System.Func<string> asphTxt = () => "Color del asfalto: " + MapRenderer.AsphaltNames[Mathf.Clamp(p.asphaltColor, 0, MapRenderer.AsphaltNames.Length - 1)];
            var ac = Big(col, asphTxt(), UIKit.Neutral, () => { }, 62);
            ac.onClick.AddListener(() =>
            {
                p.asphaltColor = (p.asphaltColor + 1) % MapRenderer.AsphaltNames.Length;
                SaveSystem.SaveProgress();
                UIKit.SetText(ac, asphTxt());
            });

            Big(col, "Ver tutorial", UIKit.Accent, () => game.StartTutorial(), 62);

            Big(col, "Borrar progreso", UIKit.Bad, () =>
            {
                Confirm("¿Borrar estrellas y récords? Tus mapas no se borran.", "Borrar progreso", () =>
                {
                    var pr = SaveSystem.Progress;
                    pr.levels.Clear();
                    pr.bestInfiniteScore = 0;
                    pr.bestInfiniteSeconds = 0f;
                    SaveSystem.SaveProgress();
                    game.ShowMessage("Progreso borrado.");
                    game.GoTo(AppScreen.Options);
                }, null, null);
            }, 62);

            var info = UIKit.Label(col, "Récord infinito: " + p.bestInfiniteScore + " puntos  ·  " + FormatTime(p.bestInfiniteSeconds), 26, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.35f));
            UIKit.Size(info, -1, 50);
        }

        // ------------------------------------------------------------------ Diálogos

        void CloseDialog()
        {
            DestroyRoot(ref dialogRoot);
        }

        void Confirm(string message, string yesText, Action yes, string altText, Action alt)
        {
            CloseDialog();
            dialogRoot = NewRoot("Dialog", new Color(0, 0, 0, 0.7f));
            var box = UIKit.Panel(dialogRoot, "Box", UIKit.PanelCol);
            box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            box.rectTransform.sizeDelta = new Vector2(660, BoxHeight(420f));
            var text = UIKit.Label(box.transform, message, 34, TextAnchor.MiddleCenter);
            Place(Rt(text), 0, 0.35f, 1, 1, 30, 0, 30, 20);

            var bar = UIKit.Rect("Buttons", box.transform);
            Place(bar, 0, 0, 1, 0.35f, 20, 20, 20, 0);
            UIKit.HLayout(bar, 14, 0);
            UIKit.Button(bar, "Cancelar", UIKit.Neutral, () => CloseDialog(), 30);
            if (alt != null && !string.IsNullOrEmpty(altText))
                UIKit.Button(bar, altText, UIKit.Accent, () => { CloseDialog(); alt(); }, 28);
            UIKit.Button(bar, yesText, UIKit.Bad, () => { CloseDialog(); if (yes != null) yes(); }, 30);
        }

        void Info(string title, string message, Action onClose)
        {
            CloseDialog();
            dialogRoot = NewRoot("Dialog", new Color(0, 0, 0, 0.7f));
            var box = UIKit.Panel(dialogRoot, "Box", UIKit.PanelCol);
            box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            box.rectTransform.sizeDelta = new Vector2(680, BoxHeight(480f));
            var t = UIKit.Label(box.transform, title, 40, TextAnchor.MiddleCenter, Color.white);
            Place(Rt(t), 0, 0.8f, 1, 1, 20, 0, 20, 10);
            var text = UIKit.Label(box.transform, message, 26, TextAnchor.MiddleCenter);
            Place(Rt(text), 0, 0.25f, 1, 0.8f, 30, 0, 30, 0);
            var ok = UIKit.Button(box.transform, "Aceptar", UIKit.Accent, () => { CloseDialog(); if (onClose != null) onClose(); }, 32);
            Place(Rt(ok), 0.3f, 0, 0.7f, 0.22f, 0, 20, 0, 0);
        }

        // ------------------------------------------------------------------ HUD de juego

        static void Corner(RectTransform rt, float ax, float ay, float w, float h, float x, float y)
        {
            rt.anchorMin = new Vector2(ax, ay);
            rt.anchorMax = new Vector2(ax, ay);
            rt.pivot = new Vector2(ax, ay);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
        }

        Button SideButton(Transform parent, string text, Color color, Action act, float height)
        {
            var b = UIKit.Button(parent, text, color, () => { AudioManager.Instance.PlayClick(); act(); }, 22);
            UIKit.Size(b, -1, height);
            return b;
        }

        RectTransform hudHourHand, hudMinuteHand;

        static RectTransform ClockHand(Transform parent, float len, float w)
        {
            var img = UIKit.Panel(parent, "Hand", Color.white, false);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(w, len);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        void SetClockHands(float hour)
        {
            if (hudHourHand == null || hudMinuteHand == null) return;
            hudHourHand.localRotation = Quaternion.Euler(0f, 0f, -(hour % 12f) / 12f * 360f);
            hudMinuteHand.localRotation = Quaternion.Euler(0f, 0f, -(hour - Mathf.Floor(hour)) * 360f);
        }

        void SetSpeed(int idx)
        {
            if (game.sim == null) return;
            if (idx == 0) game.sim.Paused = !game.sim.Paused;
            else
            {
                game.sim.Paused = false;
                game.sim.TimeScale = Speeds[Mathf.Clamp(idx - 1, 0, Speeds.Length - 1)];
            }
        }

        void BuildHud()
        {
            hudRoot = UIKit.Rect("Hud", safe);
            UIKit.Stretch(hudRoot);
            signalVisible = true;

            // Barra superior: velocidad del tiempo y ritmo de vehículos / peatones
            // Barra superior como el original: reloj, pausa y velocidades en círculos; debajo, vehículos y peatones que
            // salen por minuto y el botón de reiniciar.
            var bar = UIKit.Panel(hudRoot, "SpeedBar", PanelDark);
            Corner(bar.rectTransform, 0, 1, 330, 116, 0, 0);
            var btnRow = UIKit.Rect("SpeedButtons", bar.transform);
            Place(btnRow, 0, 1, 1, 1, 4, -60, 4, 4);
            UIKit.HLayout(btnRow, 4, 0);
            var clock = UIKit.Panel(btnRow, "Clock", Color.white, false);
            clock.sprite = IconKit.Get("clockface");
            clock.preserveAspect = true;
            clock.raycastTarget = true;
            UIKit.Size(clock, 52, 52);
            // Tocar el reloj deja cambiar la hora del juego (como el original).
            var clockBtn = clock.gameObject.AddComponent<Button>();
            clockBtn.targetGraphic = clock;
            clockBtn.onClick.AddListener(() =>
            {
                AudioManager.Instance.PlayClick();
                if (!DayNight.Active) return;
                var opts = new List<Opt>();
                for (int h = 0; h < 24; h++) opts.Add(O("none", h.ToString("00") + ":00", null, h >= 7 && h < 18 ? new Color(0.85f, 0.6f, 0.2f) : new Color(0.2f, 0.25f, 0.45f), h.ToString()));
                ShowOptions("Hora", opts, Mathf.FloorToInt(DayNight.Hour) % 24, i => DayNight.SetHour(i));
            });
            // Agujas como el original: marcan la hora del juego y se mueven con el día.
            hudHourHand = ClockHand(clock.transform, 11f, 4f);
            hudMinuteHand = ClockHand(clock.transform, 17f, 3f);
            SetClockHands(DayNight.Hour);
            string[] speedIcons = { "pause", "play", "ff2", "ff3" };
            hudSpeedBtns = new Button[4];
            hudSpeedCircles = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var cell = UIKit.Panel(btnRow, "Speed", new Color(0, 0, 0, 0));
                var b = cell.gameObject.AddComponent<Button>();
                b.targetGraphic = cell;
                b.onClick.AddListener(() => { AudioManager.Instance.PlayClick(); SetSpeed(idx); });
                UIKit.Size(cell, 60, 52);
                var circ = UIKit.Panel(cell.transform, "Circle", CircleGray, false);
                circ.sprite = IconKit.Circle;
                circ.rectTransform.anchorMin = circ.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                circ.rectTransform.sizeDelta = new Vector2(48, 48);
                var g = UIKit.Panel(circ.transform, "Glyph", Color.white, false);
                g.sprite = IconKit.Get(speedIcons[i]);
                g.rectTransform.anchorMin = g.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                g.rectTransform.sizeDelta = new Vector2(30, 30);
                hudSpeedBtns[i] = b;
                hudSpeedCircles[i] = circ;
            }
            hudRates = UIKit.Label(bar.transform, "", 19, TextAnchor.MiddleLeft);
            Place(Rt(hudRates), 0, 0, 1, 0, 10, 4, 64, -54);
            var restart = UIKit.Panel(bar.transform, "Restart", new Color(0, 0, 0, 0));
            var rb = restart.gameObject.AddComponent<Button>();
            rb.targetGraphic = restart;
            rb.onClick.AddListener(() => { AudioManager.Instance.PlayClick(); game.Restart(); });
            Corner(restart.rectTransform, 1, 0, 56, 52, -4, 2);
            var rg = UIKit.Panel(restart.transform, "Glyph", Color.white, false);
            rg.sprite = IconKit.Get("restart");
            rg.rectTransform.anchorMin = rg.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rg.rectTransform.sizeDelta = new Vector2(36, 36);

            // Puntos y choques en pequeño debajo (como pediste; el original no los tiene).
            var strip = UIKit.Panel(hudRoot, "StatsStrip", new Color(0.06f, 0.06f, 0.07f, 0.75f));
            statsStrip = strip.rectTransform;
            Corner(statsStrip, 0, 1, 330, 34, 0, -116);
            UIKit.HLayout(statsStrip, 2, 2);
            hudDelivered = StatCell(statsStrip);
            hudTime = StatCell(statsStrip);
            hudScore = StatCell(statsStrip);
            hudAccidents = StatCell(statsStrip);
            hudQueue = StatCell(statsStrip);
            hudFluidity = StatCell(statsStrip);
            hudQueue.gameObject.SetActive(false);
            hudFluidity.gameObject.SetActive(false);
            // Como el original: puntos y choques solo con objetivo (Controlador de tráfico, campaña, desafíos).
            var pm = game.PlayMap;
            bool showScore = pm == null || !pm.IsLaneMap || pm.playMode == 2 || game.levels.Mode == GameMode.Campaign || game.levels.Mode == GameMode.Challenge || game.levels.Mode == GameMode.Infinite || game.levels.Mode == GameMode.ScoreCounter;
            statsStrip.gameObject.SetActive(showScore);

            // Barra de la derecha como el original: Tráfico, Semáforo, Menú y Construir; Tráfico y Semáforo abren su propia barra.
            hudBars.Clear(); hudToolCircles.Clear();
            var main = new List<HudItem>
            {
                new HudItem("traffic", "Tráfico", () => ShowHudBar(1)),
                new HudItem("signal", "Semáforo", () => ShowHudBar(2)),
                new HudItem("menu", "Menú", () => ShowPause()),
            };
            if (game.CanBuildPlayMap) main.Add(new HudItem("build", "Construir", () => game.BuildPlayMap()));
            hudBars.Add(BuildHudBar(main));
            hudBars.Add(BuildHudBar(new List<HudItem>
            {
                new HudItem("amount", "Cantidad\nde tráfico", () => ShowTrafficAmount()),
                new HudItem("info", "Información", () => { game.SetPlayTool(PlayTool.Info); ShowHint("info"); }, PlayTool.Info),
                new HudItem("stop", "Stop", () => { game.SetPlayTool(PlayTool.StopCar); ShowHint("stop"); }, PlayTool.StopCar),
                new HudItem("patience", "Paciencia", () => { VehicleController.ShowPatience = !VehicleController.ShowPatience; }, PlayTool.None, true),
                new HudItem("back", "Volver", () => ShowHudBar(0)),
            }));
            hudBars.Add(BuildHudBar(new List<HudItem>
            {
                new HudItem("controller", "Controlador\nde semáforos", () => { game.SetPlayTool(PlayTool.Controller); ShowHint("ctrl"); }, PlayTool.Controller),
                new HudItem("gbutton", "Botón para\nvarios semáforos", () => { game.SetPlayTool(PlayTool.MultiButton); ShowHint("button"); }, PlayTool.MultiButton),
                new HudItem("allred", "Todo rojo", () => game.AllRed()),
                new HudItem("back", "Volver", () => ShowHudBar(0)),
            }));
            ShowHudBar(0);

            // Mensajes de tutorial
            var tut = UIKit.Panel(hudRoot, "Tutorial", new Color(0.1f, 0.3f, 0.45f, 0.92f));
            Place(tut.rectTransform, 0.04f, 1, 0.96f, 1, 0, -310, 0, 160);
            hudTutorial = UIKit.Label(tut.transform, "", 24, TextAnchor.MiddleLeft);
            Place(Rt(hudTutorial), 0, 0, 0.7f, 1, 16, 6, 6, 6);
            hudTutorialDone = UIKit.Button(tut.transform, "Terminar", UIKit.Good, () => game.FinishTutorial(), 26);
            Place(Rt(hudTutorialDone), 0.72f, 0.1f, 1, 0.9f, 0, 0, 14, 0);
            tut.gameObject.name = "TutorialBox";
            tutorialBox = tut.gameObject;
            tutorialBox.SetActive(game.TutorialActive);

            // Panel del cruce seleccionado (abajo, a la izquierda de la barra de herramientas)
            signalPanel = UIKit.Panel(hudRoot, "SignalPanel", UIKit.Dark).rectTransform;
            signalPanel.anchorMin = new Vector2(0, 0);
            signalPanel.anchorMax = new Vector2(1, 0);
            RebuildSignalPanel();
        }

        class HudItem
        {
            public string icon, label; public Action act; public PlayTool tool; public bool patience;
            public HudItem(string icon, string label, Action act, PlayTool tool = PlayTool.None, bool patience = false)
            { this.icon = icon; this.label = label; this.act = act; this.tool = tool; this.patience = patience; }
        }

        readonly List<RectTransform> hudBars = new List<RectTransform>();
        readonly List<KeyValuePair<HudItem, Image>> hudToolCircles = new List<KeyValuePair<HudItem, Image>>();
        int hudBar;

        RectTransform BuildHudBar(List<HudItem> items)
        {
            var tb = UIKit.Panel(hudRoot, "Toolbar", PanelDark);
            const float cellH = 90f;
            Corner(tb.rectTransform, 1, 0, 110, items.Count * cellH, 0, 8);
            UIKit.VLayout(tb.rectTransform, 0, 0);
            foreach (var it in items)
            {
                var item = it;
                Image circ;
                LayoutCell(IconCell(tb.transform, it.icon, it.label, () => { item.act(); RefreshHudTools(); }, out circ, 15), cellH);
                if (it.tool != PlayTool.None || it.patience) hudToolCircles.Add(new KeyValuePair<HudItem, Image>(it, circ));
            }
            return tb.rectTransform;
        }

        void ShowHudBar(int i)
        {
            hudBar = i;
            for (int k = 0; k < hudBars.Count; k++) hudBars[k].gameObject.SetActive(k == i);
            if (i == 0 && game.Tool != PlayTool.None) game.SetPlayTool(game.Tool);   // al volver se deja la herramienta
            DestroyRoot(ref amountRoot);
            RefreshHudTools();
        }

        void RefreshHudTools()
        {
            foreach (var kv in hudToolCircles)
            {
                bool on = kv.Key.patience ? VehicleController.ShowPatience : game.Tool == kv.Key.tool;
                kv.Value.color = on ? UIKit.Orange : CircleGray;
            }
            signalKey = "~";
        }

        RectTransform amountRoot;

        /// <summary>Cantidad de tráfico: barra de 0 a 100 %, como el original.</summary>
        void ShowTrafficAmount()
        {
            if (amountRoot != null) { DestroyRoot(ref amountRoot); return; }
            amountRoot = UIKit.Panel(hudRoot, "Amount", PanelDark).rectTransform;
            Corner(amountRoot, 1, 0, 300, 110, -118, 8 + 4 * 90f);
            var hd = UIKit.Panel(amountRoot, "Head", UIKit.Orange);
            Place(hd.rectTransform, 0, 1, 1, 1, 0, -40, 0, 0);
            var ht = UIKit.Label(hd.transform, "Cantidad de tráfico", 20, TextAnchor.MiddleLeft, Color.white);
            UIKit.Stretch(ht.rectTransform, 10, 0, 6, 0);
            var val = UIKit.Label(amountRoot, Mathf.RoundToInt(game.sim.TrafficAmount * 100f) + "%", 24, TextAnchor.MiddleCenter, Color.white);
            Place(Rt(val), 0, 0, 0.3f, 1, 6, 6, 0, 44);
            ColorSlider(amountRoot, -48f, null, UIKit.Orange, game.sim.TrafficAmount, v =>
            {
                game.sim.TrafficAmount = v;
                val.text = Mathf.RoundToInt(v * 100f) + "%";
            }, () => { });
            var sl = amountRoot.Find("Slider") as RectTransform;
            if (sl != null) { sl.offsetMin = new Vector2(96, sl.offsetMin.y); }
        }

        string signalKey = "";

        Text StatCell(Transform parent)
        {
            var t = UIKit.Label(parent, "", 15, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return t;
        }

        string SignalPanelKey()
        {
            var c = game.CtrlEdit;
            return game.Tool + ":" + (game.Selected != null ? game.Selected.GetHashCode() : 0) + ":" + (game.InfoVehicle != null ? game.InfoVehicle.id : -1) + game.FollowInfo
                + ":" + (c != null ? c.GetHashCode() + "/" + game.CtrlStage + "/" + c.light.autoCycle : "") + ":" + game.RuntimeButtons.Count;
        }

        Text toolInfo;

        void ToolRow(RectTransform parent, params KeyValuePair<string, Action>[] items)
        {
            var row = UIKit.Rect("Row", parent);
            Place(row, 0, 0, 1, 0.55f, 8, 8, 8, 0);
            UIKit.HLayout(row, 6, 0);
            foreach (var kv in items)
            {
                var a = kv.Value;
                if (a == null) UIKit.Label(row, kv.Key, 20, TextAnchor.MiddleCenter, Color.white);
                else UIKit.Button(row, kv.Key, UIKit.Neutral, () => { AudioManager.Instance.PlayClick(); a(); signalKey = "~"; }, 22);
            }
        }

        static KeyValuePair<string, Action> KV(string t, Action a) { return new KeyValuePair<string, Action>(t, a); }

        void RebuildSignalPanel()
        {
            UIKit.ClearChildren(signalPanel);
            phaseButtons.Clear();
            panelFor = game.Selected;
            hudAuto = null;
            signalInfo = null;
            toolInfo = null;
            signalPanel.gameObject.SetActive(true);
            if (game.Tool != PlayTool.None)
            {
                // Panel de la herramienta activa.
                signalPanel.offsetMin = new Vector2(8, 16);
                signalPanel.offsetMax = new Vector2(-122, 150);
                toolInfo = UIKit.Label(signalPanel, "", 21, TextAnchor.MiddleLeft, Color.white);
                Place(Rt(toolInfo), 0, 0.5f, 1, 1, 14, 0, 14, 4);
                switch (game.Tool)
                {
                    case PlayTool.Info:
                        if (game.InfoVehicle == null) { toolInfo.text = "Información: toca un vehículo para ver su velocidad y su ruta."; Place(Rt(toolInfo), 0, 0, 1, 1, 14, 4, 14, 4); }
                        else ToolRow(signalPanel, KV(game.FollowInfo ? "Seguir: sí" : "Seguir: no", () => game.FollowInfo = !game.FollowInfo), KV("Cerrar", () => game.SetPlayTool(PlayTool.None)));
                        break;
                    case PlayTool.StopCar:
                        toolInfo.text = "Stop: toca un vehículo para detenerlo; tócalo otra vez para que siga.";
                        Place(Rt(toolInfo), 0, 0, 1, 1, 14, 4, 14, 4);
                        break;
                    case PlayTool.Controller:
                        if (game.CtrlEdit == null) { toolInfo.text = "Controlador: toca un semáforo del cruce que quieres programar."; Place(Rt(toolInfo), 0, 0, 1, 1, 14, 4, 14, 4); }
                        else
                        {
                            ToolRow(signalPanel,
                                KV("◀", () => game.CtrlStage = (game.CtrlStage + GameController.MaxStages - 1) % GameController.MaxStages),
                                KV("Etapa " + (game.CtrlStage + 1), null),
                                KV("▶", () => game.CtrlStage = (game.CtrlStage + 1) % GameController.MaxStages),
                                KV("Verde −", () => game.AdjustStageGreen(-1f)), KV("Verde +", () => game.AdjustStageGreen(1f)),
                                KV("Espera −", () => game.AdjustChangeDelay(-0.5f)), KV("Espera +", () => game.AdjustChangeDelay(0.5f)),
                                KV(game.CtrlEdit.light.autoCycle ? "Auto: sí" : "Auto: no", () => game.SetCtrlAuto(!game.CtrlEdit.light.autoCycle)));
                        }
                        break;
                    case PlayTool.MultiButton:
                        toolInfo.text = "Toca el mapa para poner un botón y luego los semáforos que cambia. Fuera de esta herramienta, tócalo para usarlo.";
                        Place(Rt(toolInfo), 0, 0, 1, 1, 14, 4, 14, 4);
                        break;
                }
                return;
            }
            if (game.PlayMap != null && game.PlayMap.IsLaneMap && !game.TutorialActive)
            {
                // Como el original: sin panel abajo (el ciclo automático está en Semáforo → Controlador).
                signalPanel.gameObject.SetActive(false);
                return;
            }

            if (panelFor == null)
            {
                string hint = game.sim.Intersections.Count > 0 ? "Toca un semáforo para cambiarlo: verde ↔ rojo."
                    : "Este mapa no tiene semáforos: se regula con señales o rotondas. Observa y acelera el tiempo.";
                var l = UIKit.Label(signalPanel, hint, 24, TextAnchor.MiddleCenter, new Color(0.85f, 0.9f, 1f));
                UIKit.Stretch(l.rectTransform, 10, 4, 10, 4);
                signalPanel.offsetMin = new Vector2(8, 16);
                signalPanel.offsetMax = new Vector2(-128, 96);
                return;
            }

            signalPanel.offsetMin = new Vector2(8, 16);
            signalPanel.offsetMax = new Vector2(-128, 150);
            signalInfo = UIKit.Label(signalPanel, "", 22, TextAnchor.MiddleLeft);
            Place(Rt(signalInfo), 0, 0.5f, 1, 1, 16, 0, 16, 4);

            var row = UIKit.Rect("Row", signalPanel);
            Place(row, 0, 0, 1, 0.5f, 10, 8, 10, 0);
            UIKit.HLayout(row, 8, 0);
            hudAuto = UIKit.Button(row, "Auto: " + (game.AnyAutoCycle ? "sí" : "no"), UIKit.Neutral, () =>
            {
                game.SetAutoCycle(!game.AnyAutoCycle);
                AudioManager.Instance.PlayClick();
                UIKit.SetText(hudAuto, "Auto: " + (game.AnyAutoCycle ? "sí" : "no"));
            }, 24);
        }

        void UpdateHud()
        {
            string key = SignalPanelKey();
            if (key != signalKey) { signalKey = key; RebuildSignalPanel(); }
            if (tutorialBox != null && tutorialBox.activeSelf != game.TutorialActive) tutorialBox.SetActive(game.TutorialActive);

            hudTimer -= Time.unscaledDeltaTime;
            if (hudTimer > 0f) return;
            hudTimer = 0.15f;

            var sc = game.sim.Scoring;
            if (hudSpeedBtns != null)
            {
                bool paused = game.sim.Paused;
                hudSpeedCircles[0].color = paused ? UIKit.Orange : CircleGray;
                for (int i = 1; i < hudSpeedBtns.Length; i++)
                    hudSpeedCircles[i].color = !paused && Mathf.Abs(game.sim.TimeScale - Speeds[i - 1]) < 0.01f ? UIKit.Orange : CircleGray;
            }
            if (hudRates != null)
            {
                // Como el original: los que salen por minuto.
                float mins = sc.elapsed / 60f;
                float vpm = sc.elapsed >= 5f ? game.sim.SpawnedVehicles / mins : 0f;
                float ppm = sc.elapsed >= 5f ? game.sim.SpawnedWalkers / mins : 0f;
                SetClockHands(DayNight.Hour);
                hudRates.text = vpm.ToString("0.0") + " vehículos por min\n" + ppm.ToString("0.0") + " Peatones por min";
            }
            var obj = game.levels.Objective;
            var mode = game.levels.Mode;

            hudDelivered.text = "Llegados " + sc.delivered + (obj != null && obj.targetDelivered > 0 ? "/" + obj.targetDelivered : "");
            if (mode == GameMode.ScoreCounter)
                hudTime.text = FormatTime(Mathf.Max(0f, LevelManager.ScoreCounterSeconds - sc.elapsed));
            else if (obj != null && obj.timeLimitSeconds > 0f)
                hudTime.text = FormatTime(Mathf.Max(0f, obj.timeLimitSeconds - sc.elapsed));
            else
                hudTime.text = FormatTime(sc.elapsed);
            hudScore.text = "Puntos " + sc.Score;

            string acc = "" + sc.accidents;
            if (mode == GameMode.Infinite) acc += "/1";
            else if (mode == GameMode.Campaign || mode == GameMode.Challenge) acc += "/" + (obj != null ? obj.maxAccidents : 0);
            hudAccidents.text = "Choques " + acc;
            hudQueue.text = "Cola\n" + sc.currentQueue;
            hudFluidity.text = "Fluidez\n" + Mathf.RoundToInt(sc.Fluidity) + "%";
            hudAccidents.color = sc.accidents > 0 ? new Color(1f, 0.5f, 0.45f) : Color.white;

            if (game.TutorialActive)
            {
                hudTutorial.text = GameController.TutorialTexts[Mathf.Clamp(game.TutorialStep, 0, GameController.TutorialTexts.Length - 1)];
                hudTutorialDone.gameObject.SetActive(game.TutorialStep >= 3);
            }

            if (toolInfo != null)
            {
                if (game.Tool == PlayTool.Info && game.InfoVehicle != null)
                {
                    var v = game.InfoVehicle;
                    toolInfo.text = v.spec.label + "   " + Mathf.RoundToInt(v.speed * MapEditorController.SpeedMetersPerUnit * 3.6f) + " KM/H" + (v.waitTime > 1f ? "   esperando " + Mathf.RoundToInt(v.waitTime) + " s" : "");
                }
                else if (game.Tool == PlayTool.Controller && game.CtrlEdit != null)
                {
                    int n = game.CtrlStage < game.CtrlEdit.light.phases.Count ? game.CtrlEdit.light.phases[game.CtrlStage].approaches.Count : 0;
                    toolInfo.text = "Etapa " + (game.CtrlStage + 1) + ": " + n + " semáforos (toca para poner o quitar)   Verde " + game.StageGreen(game.CtrlStage).ToString("0") + " s   Espera " + game.ChangeDelay.ToString("0.0") + " s";
                }
                else if (game.Tool == PlayTool.MultiButton && game.EditButton != null)
                    toolInfo.text = "Botón: " + game.EditButton.links.Count + " semáforos conectados. Toca semáforos para conectarlos o quitarlos.";
            }
            var ic = game.Selected;
            if (ic != null && signalInfo != null)
            {
                int queue = 0, on = 0;
                foreach (var app in ic.light.Approaches)
                {
                    queue += game.sim.QueueLength(app);
                    if (ic.light.IsOn(app)) on++;
                }
                signalInfo.text = "Toca cada semáforo para cambiarlo.\nEn verde: " + on + "/" + ic.light.Count + "   Cola: " + queue;
            }
        }

        // ------------------------------------------------------------------ Pausa y resultados

        void ShowPause()
        {
            if (pauseRoot != null || game.Ended) return;
            game.sim.Paused = true;
            pauseRoot = NewRoot("Pause", new Color(0, 0, 0, 0.7f));
            // Como el original: Opciones con Ajustes, Construir, Continuar y Reiniciar (y Salir).
            var box = UIKit.Panel(pauseRoot, "Box", PanelDark);
            Corner(box.rectTransform, 0.5f, 0.5f, 3 * 120 + 16, 50 + 2 * 104 + 16, 0, 0);
            var head = UIKit.Panel(box.transform, "Head", UIKit.Orange);
            Place(head.rectTransform, 0, 1, 1, 1, 0, -50, 0, 0);
            var ht = UIKit.Label(head.transform, "Opciones", 26, TextAnchor.MiddleLeft);
            UIKit.Stretch(ht.rectTransform, 12, 0, 8, 0);
            var grid = UIKit.Rect("Grid", box.transform);
            Place(grid, 0, 0, 1, 1, 8, 8, 8, 56);
            UIKit.Grid(grid, new Vector2(120, 104), 0, 0, 3);
            Image c;
            IconCell(grid, "settings", "Ajustes", () => ShowGameSettings(), out c, 16);
            if (game.CanBuildPlayMap) IconCell(grid, "build", "Construir", () => { DestroyRoot(ref pauseRoot); game.BuildPlayMap(); }, out c, 16);
            IconCell(grid, "play", "Continuar", ResumeFromPause, out c, 16);
            IconCell(grid, "restart", "Reiniciar", () => { DestroyRoot(ref pauseRoot); game.Restart(); }, out c, 16);
            if (!game.PlayingFromEditor) IconCell(grid, "exit", game.TutorialActive ? "Salir del\ntutorial" : "Salir", () => { DestroyRoot(ref pauseRoot); game.ExitPlay(); }, out c, 16);
        }

        void ResumeFromPause()
        {
            DestroyRoot(ref pauseRoot);
            game.sim.Paused = false;
        }

        public void OnRoundEnded()
        {
            DestroyRoot(ref pauseRoot);
            DestroyRoot(ref resultsRoot);
            var sc = game.sim.Scoring;
            bool won = game.levels.State == GameState.Won;
            var mode = game.levels.Mode;

            resultsRoot = NewRoot("Results", new Color(0, 0, 0, 0.78f));
            var box = UIKit.Panel(resultsRoot, "Box", UIKit.PanelCol);
            box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            box.rectTransform.sizeDelta = new Vector2(680, BoxHeight(760f));

            string title = mode == GameMode.ScoreCounter ? "Fin del tiempo" : (won ? "¡Nivel superado!" : (mode == GameMode.Infinite ? "Fin de la partida" : "Nivel fallido"));
            var t = UIKit.Label(box.transform, title, 54, TextAnchor.MiddleCenter, won ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.6f, 0.55f));
            Place(Rt(t), 0, 0.84f, 1, 1, 10, 0, 10, 10);
            var reason = UIKit.Label(box.transform, game.levels.EndReason, 30, TextAnchor.MiddleCenter, new Color(0.85f, 0.9f, 1f));
            Place(Rt(reason), 0, 0.76f, 1, 0.84f);

            var sb = new StringBuilder();
            sb.Append("Puntuación: ").Append(sc.Score).Append('\n');
            sb.Append("Vehículos entregados: ").Append(sc.delivered).Append('\n');
            sb.Append("Tiempo: ").Append(FormatTime(sc.elapsed)).Append('\n');
            sb.Append("Choques: ").Append(sc.accidents).Append("   Bloqueos: ").Append(sc.blockages).Append('\n');
            sb.Append("Espera media: ").Append(sc.AverageWait.ToString("0.0")).Append(" s").Append('\n');
            sb.Append("Cola máxima: ").Append(sc.peakQueue).Append("   Fluidez: ").Append(Mathf.RoundToInt(sc.Fluidity)).Append("%");
            if (sc.emergencyDelivered > 0)
                sb.Append('\n').Append("Emergencias atendidas: ").Append(sc.emergencyDelivered)
                  .Append(" (espera media ").Append((sc.emergencyWaitTotal / sc.emergencyDelivered).ToString("0.0")).Append(" s)");
            if (sc.pedestriansCrossed > 0)
                sb.Append('\n').Append("Peatones que han cruzado: ").Append(sc.pedestriansCrossed);
            var stats = UIKit.Label(box.transform, sb.ToString(), 30, TextAnchor.MiddleCenter);
            Place(Rt(stats), 0, 0.30f, 1, 0.76f, 20, 0, 20, 0);

            if (mode == GameMode.Campaign || mode == GameMode.Challenge)
            {
                string starsLine = "Estrellas: " + game.LastStars + " de 3" + (game.LastWasRecord ? "  ·  ¡nuevo récord!" : "");
                if (game.LastUnlocked != null && game.LastUnlocked.Count > 0)
                    starsLine += "\nDesbloqueado: " + string.Join(", ", game.LastUnlocked.ToArray());
                var st = UIKit.Label(box.transform, starsLine, 28, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.35f));
                Place(Rt(st), 0, 0.2f, 1, 0.31f);
            }
            else if ((mode == GameMode.Infinite || mode == GameMode.ScoreCounter) && game.LastWasRecord)
            {
                var st = UIKit.Label(box.transform, "¡Nuevo récord!", 34, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.35f));
                Place(Rt(st), 0, 0.22f, 1, 0.31f);
            }

            var bar = UIKit.Rect("Buttons", box.transform);
            Place(bar, 0, 0, 1, 0.2f, 20, 16, 20, 0);
            UIKit.HLayout(bar, 14, 0);
            UIKit.Button(bar, "Reintentar", UIKit.Accent, () => { AudioManager.Instance.PlayClick(); DestroyRoot(ref resultsRoot); game.Restart(); }, 30);

            LevelDefinition next = null;
            if (won && mode == GameMode.Campaign && game.levels.Level != null)
            {
                int idx = LevelManager.CampaignLevels.FindIndex(l => l.id == game.levels.Level.id);
                if (idx >= 0 && idx + 1 < LevelManager.CampaignLevels.Count) next = LevelManager.CampaignLevels[idx + 1];
            }
            if (next != null)
            {
                var nl = next;
                UIKit.Button(bar, "Siguiente", UIKit.Good, () => { AudioManager.Instance.PlayClick(); DestroyRoot(ref resultsRoot); game.StartLevel(nl); }, 30);
            }
            UIKit.Button(bar, game.PlayingFromEditor ? "Al editor" : "Menú", UIKit.Neutral, () => { AudioManager.Instance.PlayClick(); DestroyRoot(ref resultsRoot); game.ExitPlay(); }, 30);
        }

        // ------------------------------------------------------------------ Interfaz del editor

        class ToolDef
        {
            public readonly string label, icon;
            public readonly EditorTool tool;
            public readonly int curved;            // -1 = no aplica; 0 = Recta; 1 = Curva
            public readonly Action act;            // acción directa (zona) en lugar de herramienta
            public readonly int need;              // 1 = requiere selección; 2 = requiere portapapeles
            public int laneType = -1;              // Tipo de carril (Herramienta → Tipo); -1 = no es un tipo
            public int construct = -1;             // «Construir»: categoría cuyo último Tipo se vuelve a usar
            public ToolDef(string label, string icon, EditorTool tool, int curved = -1) { this.label = label; this.icon = icon; this.tool = tool; this.curved = curved; }
            public ToolDef(string label, string icon, DecorType brush) { this.label = label; this.icon = icon; this.tool = EditorTool.Place; this.curved = (int)brush; }
            public ToolDef(string label, string icon, Action act, int need = 0) { this.label = label; this.icon = icon; this.act = act; this.need = need; this.curved = -1; }
            public bool IsTool { get { return act == null; } }
        }

        class FlyCell { public ToolDef def; public Image circle; public Button button; }

        // Distribución del editor de referencia: categorías abajo a la derecha (Carril / Lógica / Decoración),
        // cabecera naranja arriba a la derecha con pestañas «Acción» y «Tipo», y una rejilla de herramientas.
        // «Zona» y varias herramientas de «Tipo» de Carril y Lógica son añadidos propios (no se vieron en la referencia).
        static readonly string[] CategoryNames = { "Carril", "Lógica", "Decoración" };
        static readonly string[] CategoryHeaders = { "Herramienta", "Lógica", "Decoración", "Zona" };
        static readonly string[] CategoryIcons = { "cat_lane", "cat_logic", "cat_decor" };
        static readonly string[] TypeTabIcons = { "vehicle", "signal", "tree", "vehicle" };
        static readonly string[] LaneTypeIcons = { "vehicle", "walk", "train", "bike", "boat" };

        ToolDef[][][] BuildToolTabs()
        {
            var ed = game.editor;
            var erase = new ToolDef("Borrar", "erase", EditorTool.Erase);
            var pan = new ToolDef("Mover mapa", "pan", EditorTool.Pan);
            return new[]
            {
                // Carril (como la referencia: Acción y Tipo de carril; las entradas y salidas son automáticas)
                new[]
                {
                    new[]
                    {
                        new ToolDef("Seleccionar\nsegmento", "select", EditorTool.Edit), new ToolDef("Seleccionar\nnodo", "node", EditorTool.Edit),
                        new ToolDef("Ver\nrestricciones", "restrict", EditorTool.Restrictions), new ToolDef("Recta", "straight", EditorTool.Road, 0),
                        new ToolDef("Curva", "curve", EditorTool.Road, 1), erase, pan
                    },
                    new[]
                    {
                        LaneTypeDef("Carril del\nvehículo", "vehicle", RoadData.LaneVehicle), LaneTypeDef("Acera", "walk", RoadData.LaneSidewalk),
                        LaneTypeDef("Ferrocarril", "train", RoadData.LaneRail), LaneTypeDef("Carril bici", "bike", RoadData.LaneBike),
                        LaneTypeDef("Ruta de\nbarco", "boat", RoadData.LaneBoat), LaneTypeDef("Berma", "vehicle", RoadData.LaneShoulder)
                    }
                },
                // Lógica
                new[]
                {
                    // Como el original: Acción = Construir (lo elegido en Tipo) o Seleccionar.
                    new[] { new ToolDef("Construir", "build", () => BuildLast(1)) { construct = 1 }, new ToolDef("Seleccionar", "select", EditorTool.Edit) },
                    new[]
                    {
                        // Como la referencia (el stop, el ceda y la velocidad están en los paneles Nodo y Segmento).
                        new ToolDef("Controlador de\nsemáforos", "controller", EditorTool.Controller), new ToolDef("Parada", "busstop", EditorTool.BusStop),
                        new ToolDef("Peaje", "toll", EditorTool.Toll), new ToolDef("Advertencia\nde vehículo", "detector", EditorTool.Detector),
                        new ToolDef("Botón para\nmúltiples\nsemáforos", "gbutton", EditorTool.GroupButton), new ToolDef("Semáforo", "signal", EditorTool.SignLight)
                    }
                },
                // Decoración
                new[]
                {
                    new[] { new ToolDef("Construir", "build", () => BuildLast(2)) { construct = 2 }, new ToolDef("Seleccionar", "select", EditorTool.Edit), erase },
                    new[]
                    {
                        // Mismo orden que la referencia.
                        new ToolDef("Árbol", "tree", DecorType.Tree), new ToolDef("Edificio", "building", DecorType.Building),
                        new ToolDef("Material", "material", DecorType.Material), new ToolDef("Aparcamiento", "parking", DecorType.Parking),
                        new ToolDef("Coche\naparcado", "parked", DecorType.ParkedCar), new ToolDef("Remolque", "trailer", DecorType.Trailer),
                        new ToolDef("Barco", "boat", DecorType.Boat), new ToolDef("Valla", "fence", DecorType.Fence),
                        new ToolDef("Farola", "lamp", DecorType.Lamp), new ToolDef("Luz de inundación\nde autopista", "flood", DecorType.FloodLight),
                        new ToolDef("Humo", "smoke", DecorType.Smoke), new ToolDef("Luz", "glow", DecorType.GlowLight),
                        new ToolDef("Señal de\nferrocarril", "railsign", DecorType.RailSign), new ToolDef("Poste de\nteléfono", "phone", DecorType.PhonePole),
                        new ToolDef("Señal de\ntráfico", "roadsign", DecorType.TrafficSign), new ToolDef("Estructura\nredonda", "tank", DecorType.RoundStructure),
                        new ToolDef("Parada de\nautobús", "shelter", DecorType.BusShelter), new ToolDef("Contenedores", "containers", DecorType.Containers),
                        new ToolDef("Tendido\neléctrico", "powerline", DecorType.PowerLine), new ToolDef("Grúa de\ncontenedores", "crane", DecorType.Crane),
                        new ToolDef("Pegatina", "sticker", DecorType.Sticker), new ToolDef("Texto", "text", DecorType.TextLabel),
                        new ToolDef("Líneas", "lines", DecorType.Lines), new ToolDef("Misceláneo", "misc", DecorType.Misc),
                        new ToolDef("Zona", "zone", DecorType.Zone), new ToolDef("Camino", "trail", DecorType.Trail),
                        new ToolDef("Pegatina\nde pared", "wallsticker", DecorType.WallSticker)
                    }
                },
                // Zona
                new[]
                {
                    new[]
                    {
                        new ToolDef("Área\nvisible", "area", EditorTool.View), new ToolDef("Seleccionar\nzona", "selzone", EditorTool.Select),
                        new ToolDef("Copiar", "copy", () => ed.CopySelection(), 1), new ToolDef("Cortar", "cut", () => ed.CutSelection(), 1),
                        new ToolDef("Mover\nzona", "pan", () => ed.MoveSelection(), 1), new ToolDef("Pegar", "paste", () => ed.BeginPaste(), 2),
                        new ToolDef("Borrar\nzona", "erase", () => ed.DeleteSelection(), 1),
                        new ToolDef("Agrupar", "group", () => ed.GroupSelection(), 1), new ToolDef("Desagrupar", "ungroup", () => ed.UngroupSelection(), 1)
                    },
                    new ToolDef[0]
                }
            };
        }

        ToolDef LaneTypeDef(string label, string icon, int type)
        {
            var ed = game.editor;
            var d = new ToolDef(label, icon, () => { ed.SetNewLaneType(type); if (ed.Tool != EditorTool.Road) { ed.NewRoadCurved = false; ed.SetTool(EditorTool.Road); } });
            d.laneType = type;
            return d;
        }

        ToolDef[][][] toolTabs;
        int editorCategory, editorTab;
        bool flyoutOpen, propsCollapsed;
        RectTransform flyout, selBody, rightBar;
        Text selHeader, headerText;
        Image selArrow;
        Button selTrash;
        int selCount;
        int selCols = 2;
        float selRowH = 58f;
        float selShownH = -1f;
        RectTransform selViewport;
        readonly List<Image> categoryCircles = new List<Image>();
        readonly List<Image> tabCircles = new List<Image>();
        readonly List<Image> tabGlyphs = new List<Image>();
        readonly List<FlyCell> flyCells = new List<FlyCell>();
        Image layerCircle;
        Text layerNumber;
        RectTransform edMenuRoot;

        static readonly Color CircleGray = new Color32(78, 78, 78, 255);
        static readonly Color PanelDark = new Color32(22, 24, 22, 245);

        /// <summary>Botón redondo con pictograma y rótulo debajo (como los de la referencia).</summary>
        Button IconCell(Transform parent, string icon, string label, Action act, out Image circle, int labelSize = 19)
        {
            var cell = UIKit.Panel(parent, "Cell", new Color(0, 0, 0, 0));
            var btn = cell.gameObject.AddComponent<Button>();
            btn.targetGraphic = cell;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => { AudioManager.Instance.PlayClick(); if (act != null) act(); });

            circle = UIKit.Panel(cell.transform, "Circle", CircleGray, false);
            circle.sprite = IconKit.Circle;
            var cr = circle.rectTransform;
            cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 1f);
            cr.pivot = new Vector2(0.5f, 1f);
            cr.sizeDelta = new Vector2(48, 48);
            cr.anchoredPosition = new Vector2(0, -6);
            if (!string.IsNullOrEmpty(icon))
            {
                var g = UIKit.Panel(circle.transform, "Glyph", Color.white, false);
                g.sprite = IconKit.Get(icon);
                var gr = g.rectTransform;
                gr.anchorMin = gr.anchorMax = new Vector2(0.5f, 0.5f);
                gr.sizeDelta = new Vector2(32, 32);
                gr.anchoredPosition = Vector2.zero;
            }
            var t = UIKit.Label(cell.transform, label, labelSize, TextAnchor.UpperCenter, Color.white);
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var tr = t.rectTransform;
            tr.anchorMin = new Vector2(0, 0); tr.anchorMax = new Vector2(1, 1);
            tr.offsetMin = new Vector2(0, 0); tr.offsetMax = new Vector2(0, -56);
            return btn;
        }

        bool categoryOff;
        RectTransform headRoot, tabsRoot;

        /// <summary>Cierra las ventanitas abiertas (opciones, capa, tabla) para que nunca se monten unas sobre otras.</summary>
        void CloseMenus(bool flyoutToo)
        {
            DestroyRoot(ref optRoot);
            DestroyRoot(ref layerRoot);
            DestroyRoot(ref gridRoot);
            DestroyRoot(ref destRoot);
            if (flyoutToo && flyoutOpen) { flyoutOpen = false; RebuildFlyout(); }
        }

        void SelectCategory(int index)
        {
            CloseMenus(false);
            var ed = game.editor;
            if (index == editorCategory && !categoryOff)
            {
                // Volver a pulsar la categoría elegida la cierra: queda solo seleccionar nodos y carriles (como el original).
                categoryOff = true;
                flyoutOpen = false;
                if (ed.Pending) ed.CancelPending();
                ed.SetTool(EditorTool.Edit);
                ed.SelectRoadsOnly = true;
            }
            else
            {
                categoryOff = false;
                ed.SelectRoadsOnly = false;
                editorCategory = index;
                if (editorTab >= 2 || toolTabs[editorCategory][editorTab].Length == 0) editorTab = 0;
                flyoutOpen = true;
            }
            selShownH = -1f;
            RebuildFlyout();
        }

        void SelectTab(int tab)
        {
            CloseMenus(false);
            if (toolTabs[editorCategory][tab].Length == 0) return;
            if (tab == editorTab && flyoutOpen) flyoutOpen = false;
            else { editorTab = tab; flyoutOpen = true; }
            RebuildFlyout();
        }

        void RebuildFlyout()
        {
            if (flyout == null) return;
            UIKit.ClearChildren(flyout);
            flyCells.Clear();
            flyout.gameObject.SetActive(flyoutOpen);
            if (headerText != null) headerText.text = CategoryHeaders[editorCategory];
            if (!flyoutOpen) return;

            var defs = toolTabs[editorCategory][editorTab];
            int cols = defs.Length > 9 ? 4 : 3;
            float cw = 128f, ch = 108f;
            int rows = (defs.Length + cols - 1) / cols;
            float avail = Mathf.Max(200f, safe.rect.height - 154f - 8f);
            float h = Mathf.Min(rows * ch + 16f, avail);
            flyout.sizeDelta = new Vector2(cols * cw + 16f, h);

            var viewport = UIKit.Rect("Viewport", flyout);
            UIKit.Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = UIKit.Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero; content.offsetMax = Vector2.zero;
            UIKit.Grid(content, new Vector2(cw, ch), 0, 8, cols);
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = flyout.gameObject.GetComponent<ScrollRect>() ?? flyout.gameObject.AddComponent<ScrollRect>();
            sr.viewport = viewport; sr.content = content; sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 30f;

            for (int i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                Image circ;
                var btn = IconCell(content, def.icon, def.label, () => OnToolPicked(def), out circ, 20);
                flyCells.Add(new FlyCell { def = def, circle = circ, button = btn });
            }
        }

        readonly ToolDef[] lastTypeDef = new ToolDef[3];

        /// <summary>«Construir»: vuelve a la última herramienta elegida en Tipo de esa categoría (o la primera).</summary>
        void BuildLast(int cat)
        {
            var d = lastTypeDef[cat] ?? (toolTabs[cat][1].Length > 0 ? toolTabs[cat][1][0] : null);
            if (d == null) return;
            var ed = game.editor;
            if (d.tool == EditorTool.Place) ed.Brush = (DecorType)d.curved;
            ed.SetTool(d.tool);
            if (d.tool == EditorTool.Place && (DecorType)d.curved == DecorType.TrafficSign) ShowSignCatalog(ed.NewSignFace, i => ed.NewSignFace = i);
        }

        void OnToolPicked(ToolDef def)
        {
            var ed = game.editor;
            if (editorTab == 1 && def.IsTool && def.laneType < 0 && editorCategory > 0) lastTypeDef[editorCategory] = def;
            if (def.IsTool)
            {
                if (def.tool == EditorTool.Place) ed.Brush = (DecorType)def.curved;
                else if (def.curved >= 0) ed.NewRoadCurved = def.curved == 1;
                ed.SetTool(def.tool);
                // Como el original: al elegir «Señal de tráfico» se abre el catálogo para escoger cuál poner.
                if (def.tool == EditorTool.Place && (DecorType)def.curved == DecorType.TrafficSign)
                {
                    flyoutOpen = false;
                    RebuildFlyout();
                    ShowSignCatalog(ed.NewSignFace, i => ed.NewSignFace = i);
                    return;
                }
            }
            else def.act();
            flyoutOpen = false;
            RebuildFlyout();
        }

        bool IsActiveDef(ToolDef d, MapEditorController ed)
        {
            if (d.construct > 0)
            {
                foreach (var t in toolTabs[d.construct][1]) if (t.IsTool && t.tool == ed.Tool && (t.curved < 0 || t.curved == ed.ToolArg)) return true;
                return false;
            }
            if (d.laneType >= 0) return ed.NewLaneType == d.laneType;
            return d.IsTool && d.tool == ed.Tool && (d.curved < 0 || d.curved == ed.ToolArg)
                ;
        }

        void RefreshEditorChrome()
        {
            var ed = game.editor;
            if (headerText != null) headerText.text = CategoryHeaders[editorCategory];
            if (headRoot != null && headRoot.gameObject.activeSelf == categoryOff) headRoot.gameObject.SetActive(!categoryOff);
            if (tabsRoot != null && tabsRoot.gameObject.activeSelf == categoryOff) tabsRoot.gameObject.SetActive(!categoryOff);
            for (int i = 0; i < categoryCircles.Count; i++)
                categoryCircles[i].color = i == editorCategory && !categoryOff ? UIKit.Orange : CircleGray;

            for (int t = 0; t < 2; t++)
            {
                var defs = toolTabs[editorCategory][t];
                ToolDef active = null;
                foreach (var d in defs) if (IsActiveDef(d, ed)) { active = d; break; }
                string icon = active != null ? active.icon : (t == 0 ? "dig" : TypeTabIcons[editorCategory]);
                if (editorCategory == 0 && t == 1) icon = LaneTypeIcons[Mathf.Clamp(ed.NewLaneType, 0, LaneTypeIcons.Length - 1)];
                tabGlyphs[t].sprite = IconKit.Get(icon);
                bool enabled = defs.Length > 0;
                bool hot = (active != null) || (flyoutOpen && editorTab == t);
                tabCircles[t].color = !enabled ? new Color(0.28f, 0.28f, 0.28f, 1f) : (hot ? UIKit.Orange : CircleGray);
                tabGlyphs[t].color = enabled ? Color.white : new Color(1, 1, 1, 0.35f);
            }

            for (int i = 0; i < flyCells.Count; i++)
            {
                var fc = flyCells[i];
                bool on = IsActiveDef(fc.def, ed);
                bool usable = fc.def.need == 0 || (fc.def.need == 1 && ed.HasSelection) || (fc.def.need == 2 && ed.HasClipboard);
                fc.button.interactable = usable;
                fc.circle.color = !usable ? new Color(0.28f, 0.28f, 0.28f, 1f) : (on ? UIKit.Orange : CircleGray);
            }

            if (layerCircle != null) layerCircle.color = ed.CurrentLayer != 0 ? UIKit.Orange : CircleGray;
            if (layerNumber != null) layerNumber.text = ed.CurrentLayer.ToString();
        }

        CanvasScaler uiScaler;

        /// <summary>Tamaño del menú (Ajustes): escala toda la interfaz.</summary>
        public void ApplyUiScale(float k)
        {
            if (uiScaler == null) return;
            k = Mathf.Clamp(k <= 0.01f ? 1f : k, 0.5f, 2.5f);
            uiScaler.referenceResolution = new Vector2(720f, 1280f) / k;
        }

        /// <summary>Menú del editor como el original: rejilla de iconos (y Validar, Deshacer y Rehacer, que son útiles).</summary>
        void ShowEditorMenu()
        {
            if (edMenuRoot != null) return;
            var ed = game.editor;
            CloseMenus(true);
            edMenuRoot = NewRoot("EditorMenu", new Color(0, 0, 0, 0.35f));
            var bg = edMenuRoot.gameObject.AddComponent<Button>();
            bg.onClick.AddListener(() => DestroyRoot(ref edMenuRoot));
            var box = UIKit.Panel(edMenuRoot, "Box", PanelDark);
            Corner(box.rectTransform, 0.5f, 0.5f, 3 * 120 + 16, 50 + 3 * 104 + 16, 0, 0);
            var head = UIKit.Panel(box.transform, "Head", UIKit.Orange);
            Place(head.rectTransform, 0, 1, 1, 1, 0, -50, 0, 0);
            var ht = UIKit.Label(head.transform, "Opciones", 26, TextAnchor.MiddleLeft);
            UIKit.Stretch(ht.rectTransform, 12, 0, 8, 0);
            var grid = UIKit.Rect("Grid", box.transform);
            Place(grid, 0, 0, 1, 1, 8, 8, 8, 56);
            UIKit.Grid(grid, new Vector2(120, 104), 0, 0, 3);
            Action<string, string, Action> item = (icon, text, act) =>
            {
                Image c;
                IconCell(grid, icon, text, () => { DestroyRoot(ref edMenuRoot); act(); }, out c, 16);
            };
            item("settings", "Ajustes", () => ShowGameSettings());
            item("mapset", "Ajustes\nde mapa", () => { if (ed.Map != null && ed.Map.IsLaneMap) ShowMapSettings(); else ShowProps(); });
            item("save", "Guardar", () => { ed.Save(); game.ShowMessage(ed.Status); });
            item("copy", "Copiar mapa", () =>
            {
                GUIUtility.systemCopyBuffer = MapShare.Encode(ed.Map);
                Info("Compartir mapa", "Código copiado al portapapeles. Pégalo en un mensaje: quien lo reciba usa «Pegar código» en Mis mapas.", null);
            });
            item("check", "Validar", () => ed.Validate());
            item("undo", "Deshacer", () => ed.Undo());
            item("redo", "Rehacer", () => ed.Redo());
            item("exit", "Salir", () => EditorBack());
            item("play", "Continuar", () => { });
        }

        RectTransform gameSetRoot;

        /// <summary>Ajustes del juego (como el original): sonido, música y tamaño del menú.</summary>
        void ShowGameSettings()
        {
            DestroyRoot(ref gameSetRoot);
            var p = SaveSystem.Progress;
            gameSetRoot = NewRoot("GameSettings", new Color(0, 0, 0, 0.35f));
            var bg = gameSetRoot.gameObject.AddComponent<Button>();
            bg.onClick.AddListener(() => DestroyRoot(ref gameSetRoot));
            var box = UIKit.Panel(gameSetRoot, "Box", PanelDark);
            string credits = AudioManager.MusicCredits;
            Corner(box.rectTransform, 0.5f, 0.5f, 340, string.IsNullOrEmpty(credits) ? 300 : 370, 0, 0);
            var head = UIKit.Panel(box.transform, "Head", UIKit.Orange);
            Place(head.rectTransform, 0, 1, 1, 1, 0, -50, 0, 0);
            if (!string.IsNullOrEmpty(credits))
            {
                var cr = UIKit.Label(box.transform, credits, 13, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f));
                Place(cr.rectTransform, 0, 0, 1, 0, 8, 104, 8, -150);
            }
            var ht = UIKit.Label(head.transform, "Ajustes", 26, TextAnchor.MiddleLeft);
            UIKit.Stretch(ht.rectTransform, 12, 0, 8, 0);
            var row = UIKit.Rect("Row", box.transform);
            Place(row, 0, 1, 1, 1, 30, -160, 30, 56);
            UIKit.Grid(row, new Vector2(140, 104), 0, 0, 2);
            Image c1, c2;
            IconCell(row, "sound", "Sonido", () => { p.soundOn = !p.soundOn; SaveSystem.SaveProgress(); game.ApplyOptions(); ShowGameSettings(); }, out c1, 18);
            c1.color = p.soundOn ? UIKit.Orange : CircleGray;
            IconCell(row, "music", "Música", () => { p.musicOn = !p.musicOn; SaveSystem.SaveProgress(); game.ApplyOptions(); ShowGameSettings(); }, out c2, 18);
            c2.color = p.musicOn ? UIKit.Orange : CircleGray;
            var size = UIKit.Rect("Size", box.transform);
            Place(size, 0, 0, 1, 0, 12, 14, 12, -92);
            UIKit.HLayout(size, 6, 0);
            Action<float> setScale = d =>
            {
                p.uiScale = Mathf.Clamp(Mathf.Round(((p.uiScale <= 0.01f ? 1f : p.uiScale) + d) * 10f) / 10f, 0.5f, 2.5f);
                SaveSystem.SaveProgress();
                ApplyUiScale(p.uiScale);
                ShowGameSettings();
            };
            UIKit.Size(UIKit.Button(size, "−", UIKit.Neutral, () => setScale(-0.1f), 30), 64, -1);
            var lab = UIKit.Label(size, Mathf.RoundToInt((p.uiScale <= 0.01f ? 1f : p.uiScale) * 100f) + "%\n<size=16>Tamaño del menú</size>", 24, TextAnchor.MiddleCenter, Color.white);
            lab.supportRichText = true;
            UIKit.Size(lab, -1, -1, 1f);
            UIKit.Size(UIKit.Button(size, "+", UIKit.Neutral, () => setScale(0.1f), 30), 64, -1);
        }

        void BuildEditorUi()
        {
            editorRoot = UIKit.Rect("EditorUi", safe);
            UIKit.Stretch(editorRoot);
            toolTabs = BuildToolTabs();
            shownIssuesCount = -1;
            editorCategory = 0; editorTab = 0;
            categoryOff = true;   // como el original: al entrar no hay categoría elegida (solo seleccionar nodos y carriles)
            game.editor.SelectRoadsOnly = true;
            if (game.editor.Tool != EditorTool.Edit) game.editor.SetTool(EditorTool.Edit);
            flyoutOpen = false;
            propsCollapsed = false;
            selShownH = -1f;
            categoryCircles.Clear(); tabCircles.Clear(); tabGlyphs.Clear(); flyCells.Clear();

            // Estado
            editorStatus = UIKit.Label(editorRoot, "", 22, TextAnchor.UpperLeft, Color.white);
            Place(Rt(editorStatus), 0.17f, 1, 0.52f, 1, 0, -90, 0, 0);
            var sh = editorStatus.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.9f);

            const float cellH = 90f;
            // Panel izquierdo A: Menú, Jugar, Centrar, Ajustes de mapa
            var leftA = UIKit.Panel(editorRoot, "LeftA", PanelDark);
            Corner(leftA.rectTransform, 0, 1, 96, 4 * cellH, 0, 0);
            UIKit.VLayout(leftA.rectTransform, 0, 0);
            Image c0;
            LayoutCell(IconCell(leftA.transform, "menu", "Menú", () => ShowEditorMenu(), out c0), cellH);
            LayoutCell(IconCell(leftA.transform, "play", "Jugar", () =>
            {
                ShowSimModeDialog(game.editor.Map.traffic.simMode, game.editor.Map.traffic.chaosLevel, true, (mode, level, remember) =>
                {
                    if (remember) { game.editor.Map.traffic.simMode = mode; game.editor.Map.traffic.chaosLevel = level; game.editor.MarkDirty(); }
                    game.TestEditorMap(mode, level);   // si no es válido, el propio controlador muestra qué falla y el mapa sigue editable
                });
            }, out c0), cellH);
            LayoutCell(IconCell(leftA.transform, "center", "Centrar", () => game.editor.FitCamera(), out c0), cellH);
            LayoutCell(IconCell(leftA.transform, "mapset", "Ajustes\nde mapa", () => { if (game.editor.Map != null && game.editor.Map.IsLaneMap) ShowMapSettings(); else ShowProps(); }, out c0, 15), cellH);

            // Panel izquierdo B: Capa (número dentro del círculo) y Tabla
            var leftB = UIKit.Panel(editorRoot, "LeftB", PanelDark);
            Corner(leftB.rectTransform, 0, 1, 96, 2 * cellH, 0, -(4 * cellH + 8));
            UIKit.VLayout(leftB.rectTransform, 0, 0);
            var layerBtn2 = IconCell(leftB.transform, null, "Capa", () => ShowLayerPicker(-999), out layerCircle);
            LayoutCell(layerBtn2, cellH);
            layerNumber = UIKit.Label(layerCircle.transform, "0", 34, TextAnchor.MiddleCenter, Color.white);
            UIKit.Stretch(layerNumber.rectTransform);
            LayoutCell(IconCell(leftB.transform, "grid", "Tabla", () => ShowGridPicker(), out c0), cellH);

            editorTitle = UIKit.Label(editorRoot, "", 16, TextAnchor.LowerLeft, new Color(0.85f, 0.9f, 1f));
            Corner(Rt(editorTitle), 0, 0, 300, 36, 8, 6);

            // Cabecera naranja + pestañas Acción / Tipo
            var head = UIKit.Panel(editorRoot, "Header", UIKit.Orange);
            headRoot = head.rectTransform;
            Corner(head.rectTransform, 1, 1, 280, 56, 0, 0);
            headerText = UIKit.Label(head.transform, "Herramienta", 32, TextAnchor.MiddleLeft, Color.white);
            UIKit.Stretch(headerText.rectTransform, 10, 0, 6, 0);

            var tabs = UIKit.Panel(editorRoot, "Tabs", PanelDark);
            tabsRoot = tabs.rectTransform;
            Corner(tabs.rectTransform, 1, 1, 280, 98, 0, -56);
            UIKit.HLayout(tabs.rectTransform, 0, 0);
            string[] tabNames = { "Acción", "Tipo" };
            for (int t = 0; t < 2; t++)
            {
                int tt = t;
                Image circ;
                var tb = IconCell(tabs.transform, t == 0 ? "dig" : TypeTabIcons[0], tabNames[t], () => SelectTab(tt), out circ);
                tabCircles.Add(circ);
                tabGlyphs.Add(circ.transform.Find("Glyph").GetComponent<Image>());
            }

            // Propiedades (contextual) bajo las pestañas
            var sp = UIKit.Panel(editorRoot, "Props", PanelDark);
            selPanel = sp.rectTransform;
            bool land = Screen.width > Screen.height;
            Corner(selPanel, 1, 1, 300, 60, land ? -100 : 0, -154);
            var hdr = UIKit.Button(selPanel, "", new Color32(150, 78, 20, 255), () => { propsCollapsed = !propsCollapsed; selShownH = -1f; }, 24);
            hdr.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            var hr = Rt(hdr);
            hr.anchorMin = new Vector2(0, 1); hr.anchorMax = new Vector2(1, 1);
            hr.offsetMin = new Vector2(0, -44); hr.offsetMax = Vector2.zero;
            selHeader = hdr.GetComponentInChildren<Text>();
            selHeader.fontSize = 26; selHeader.resizeTextForBestFit = false;
            selHeader.rectTransform.offsetMin = new Vector2(14, 2);
            selArrow = UIKit.Panel(hdr.transform, "Arrow", Color.white, false);
            selArrow.sprite = IconKit.Get("yield");
            var ar = selArrow.rectTransform;
            ar.anchorMin = ar.anchorMax = new Vector2(1, 0.5f);
            ar.sizeDelta = new Vector2(22, 22); ar.anchoredPosition = new Vector2(-20, 0);
            // Papelera en el título (como el original): borra lo seleccionado.
            selTrash = UIKit.Button(hdr.transform, "", new Color(0, 0, 0, 0), () => game.editor.DeleteSelected(), 18);
            var trr = Rt(selTrash);
            trr.anchorMin = trr.anchorMax = new Vector2(1, 0.5f);
            trr.sizeDelta = new Vector2(44, 40); trr.anchoredPosition = new Vector2(-62, 0);
            var trIcon = UIKit.Panel(selTrash.transform, "Icon", Color.white, false);
            trIcon.sprite = IconKit.Get("trash");
            trIcon.raycastTarget = false;
            var tri = trIcon.rectTransform;
            tri.anchorMin = tri.anchorMax = new Vector2(0.5f, 0.5f);
            tri.sizeDelta = new Vector2(28, 28); tri.anchoredPosition = Vector2.zero;
            selTrash.gameObject.SetActive(false);

            selViewport = UIKit.Rect("Viewport", selPanel);
            selViewport.anchorMin = Vector2.zero; selViewport.anchorMax = Vector2.one;
            selViewport.offsetMin = Vector2.zero; selViewport.offsetMax = new Vector2(0, -44);
            selViewport.gameObject.AddComponent<RectMask2D>();
            selBody = UIKit.Rect("Body", selViewport);
            selBody.anchorMin = new Vector2(0, 1); selBody.anchorMax = new Vector2(1, 1);
            selBody.pivot = new Vector2(0.5f, 1f);
            selBody.offsetMin = Vector2.zero; selBody.offsetMax = Vector2.zero;
            UIKit.Grid(selBody, new Vector2(138, 52), 6, 6, 2);
            var sfit = selBody.gameObject.AddComponent<ContentSizeFitter>();
            sfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var ssr = selPanel.gameObject.AddComponent<ScrollRect>();
            ssr.viewport = selViewport; ssr.content = selBody; ssr.horizontal = false; ssr.vertical = true;
            ssr.movementType = ScrollRect.MovementType.Clamped; ssr.scrollSensitivity = 30f;
            selPanel.gameObject.SetActive(false);
            selPanelKey = "";

            // Barra de categorías abajo a la derecha
            rightBar = UIKit.Panel(editorRoot, "Categories", PanelDark).rectTransform;
            Corner(rightBar, 1, 0, 96, CategoryNames.Length * cellH, 0, 8);
            UIKit.VLayout(rightBar, 0, 0);
            for (int i = 0; i < CategoryNames.Length; i++)
            {
                int idx = i;
                Image circ;
                LayoutCell(IconCell(rightBar, CategoryIcons[i], CategoryNames[i], () => SelectCategory(idx), out circ, 18), cellH);
                categoryCircles.Add(circ);
            }

            // Rejilla de herramientas (se abre al tocar una pestaña o categoría)
            var fly = UIKit.Panel(editorRoot, "Flyout", PanelDark);
            flyout = fly.rectTransform;
            Corner(flyout, 1, 1, 400, 200, 0, -154);
            flyout.gameObject.SetActive(false);

            issuesRoot = UIKit.Panel(editorRoot, "Issues", UIKit.Dark).rectTransform;
            Place(issuesRoot, 0.16f, 0.5f, 0.84f, 0.5f, 0, -200, 0, -200);
            issuesRoot.gameObject.SetActive(false);
            RefreshEditorChrome();
        }

        static void LayoutCell(Button b, float h)
        {
            UIKit.Size(b, -1, h);
        }

        void LayoutSelPanel()
        {
            if (selPanel == null || !selPanel.gameObject.activeSelf) return;
            float rows = Mathf.Ceil(selCount / (float)selCols);
            float full = rows * selRowH + 6f;
            bool land = Screen.width > Screen.height;
            float maxH = Mathf.Max(140f, safe.rect.height - 154f - (land ? 12f : 4f * 98f + 16f));
            float bodyH = propsCollapsed ? 0f : Mathf.Min(full, maxH);
            float h = 44f + bodyH;
            float want = categoryOff ? 0f : -154f;
            if (!Mathf.Approximately(selPanel.anchoredPosition.y, want)) selPanel.anchoredPosition = new Vector2(selPanel.anchoredPosition.x, want);
            if (Mathf.Approximately(h, selShownH)) return;
            selShownH = h;
            selPanel.sizeDelta = new Vector2(300, h);
            selViewport.gameObject.SetActive(!propsCollapsed);
            if (selArrow != null) selArrow.rectTransform.localRotation = Quaternion.Euler(0, 0, propsCollapsed ? 0f : 180f);
        }

        void UpdateEditor()
        {
            var ed = game.editor;
            if (ed.Map == null) return;
            // Como el original: arriba no hay textos de ayuda; solo se avisa unos segundos cuando algo no se puede hacer.
            if (ed.StatusSerial != statusSerialSeen) { statusSerialSeen = ed.StatusSerial; statusShownAt = Time.unscaledTime; }
            bool showStatus = MapEditorController.IsProblemStatus(ed.Status) && Time.unscaledTime - statusShownAt < 4f;
            editorStatus.text = showStatus ? ed.Status : "";
            editorTitle.text = ed.Map.name + (ed.Dirty ? " *" : "") + "   " + ed.Map.width.ToString("0") + "x" + ed.Map.height.ToString("0");

            RefreshEditorChrome();
            if (ed.Pending && propsCollapsed) { propsCollapsed = false; selShownH = -1f; }
            UpdateSelectionPanel();
            LayoutSelPanel();

            if (issuesRoot != null && (shownIssuesCount != ed.Issues.Count || shownIssuesFlag != ed.ShowIssues || shownIssueSel != ed.IssueSel || shownIssuesVersion != ed.IssuesVersion))
            {
                shownIssuesVersion = ed.IssuesVersion;
                shownIssuesCount = ed.Issues.Count;
                shownIssueSel = ed.IssueSel;
                shownIssuesFlag = ed.ShowIssues;
                RebuildIssues();
            }
        }

        // Panel contextual: ajustes de la calle nueva, o del nodo / calle seleccionados con la herramienta Editar.
        void UpdateSelectionPanel()
        {
            if (selPanel == null) return;
            var ed = game.editor;
            string key;
            bool editLike = ed.Tool == EditorTool.Edit;
            var road = editLike ? ed.SelectedRoad : null;
            var node = (editLike || ed.Tool == EditorTool.Controller) ? ed.SelectedNode : null;
            var decorSel = (editLike || (ed.Tool == EditorTool.Place && ed.Map != null && ed.Map.IsLaneMap)) ? ed.SelectedDecorItem : null;
            var pointSel = editLike ? ed.SelectedPoint : null;
            var buttonSel = (editLike || ed.Tool == EditorTool.GroupButton) ? ed.SelectedButton : null;
            var arrowSel = (editLike || ed.Tool == EditorTool.LaneMask) ? ed.SelectedArrow : null;
            var lightSel = ed.Tool == EditorTool.SignLight ? ed.SelectedLight : null;
            var np = ed.NewLaneProps;
            if (ed.Tool == EditorTool.Road) key = "new:" + ed.NewLaneType + ":" + ed.Pending + ":" + np.hidden + np.noLaneChange + np.allow + ":" + np.speedKmh + ":" + np.lineL + np.lineR + np.lineSame + np.lineOpp + np.lineEdge + np.color + np.cables + np.rails + np.railColor + np.cableSide;
            else if (lightSel != null) key = "light:" + lightSel.road + ":" + lightSel.node + ":" + lightSel.model + ":" + lightSel.size.ToString("0.0") + ":" + lightSel.side + ":" + lightSel.phase;
            else if (arrowSel != null) key = "arrow:" + arrowSel.id + ":" + arrowSel.moves + ":" + arrowSel.t.ToString("0.00") + ":" + arrowSel.size.ToString("0.0");
            else if (road != null) key = "road:" + road.id + ":" + road.lanesAB + ":" + road.lanesBA + ":" + road.limited + ":" + road.hidden + ":" + road.noMarks + ":" + road.color + ":" + road.cables + ":" + road.rails + ":" + road.allow + ":" + road.layer + ":" + road.noLaneChange + ":" + ed.SelLaneForward + ed.SelLaneIndex + ":" + ed.SelLaneMask + ":" + road.locked + ":" + road.pedestrian + ":" + road.ramp + ":" + road.railColor + road.cableSide + ":" + (ed.SelectedLaneArrow != null ? ed.SelectedLaneArrow.t.ToString("0.00") + ed.SelectedLaneArrow.color : "") + ":" + road.speedKmh + ":" + road.lineL + road.lineR + road.lineSame + road.lineOpp + road.lineEdge + ":" + ed.SelectedArrowIndex() + ":" + road.sidewalk + ":" + road.median + ":" + road.medianType + ":" + ed.HasRoadClipboard + ":" + road.crossStyle + ":" + road.avoid + ":" + road.roadText + ":" + road.markKind + road.markT.ToString("0.00") + road.markColor;
            else if (ed.Tool == EditorTool.Controller) key = node != null ? "ctrl:" + node.id + ":" + node.ctrl + ":" + node.green.ToString("0.0") + ":" + node.yellow.ToString("0.0") + ":" + ed.NodeHasLights(node) + ":" + node.allRed.ToString("0.0") + node.sensor : "";
            else if (node != null) key = "node:" + node.id + ":" + node.entry + ":" + node.exit + ":" + node.roundabout + ":" + node.radius.ToString("0.0") + ":" + ed.NodeHasLights(node)
                + ":" + ed.NodeHasSign(node.id, SignType.Light) + ed.NodeHasSign(node.id, SignType.Yield) + ed.NodeHasSign(node.id, SignType.Stop) + ed.NodeHasLamp(node.id) + ed.NodeLightPhase(node.id) + ed.NodeSignStyle(node.id, SignType.Stop) + ed.NodeSignStyle(node.id, SignType.Yield)
                + ":" + LightKey(ed.NodeLight(node.id)) + ":" + StartKey(node);
            else if (decorSel != null) key = "decor:" + ed.SelectedDecor + ":" + ed.PolyAdding + ":" + JsonUtility.ToJson(decorSel);
            else if (pointSel != null) key = "point:" + pointSel.id + ":" + pointSel.seconds.ToString("0.0") + ":" + pointSel.dir;
            else if (buttonSel != null) key = "button:" + buttonSel.id + ":" + buttonSel.links.Count + ":" + (ed.LinkingButtonId == buttonSel.id);
            else key = "";
            if (ed.Tool == EditorTool.Road) key += ":L" + ed.CurrentLayer;
            if (key == selPanelKey) return;
            selPanelKey = key;

            UIKit.ClearChildren(selBody);
            selCount = 0;
            selPanel.gameObject.SetActive(key.Length > 0);
            if (key.Length == 0) return;
            bool laneMap = ed.Map != null && ed.Map.IsLaneMap;
            if (selTrash != null)
                selTrash.gameObject.SetActive(laneMap && ed.Tool != EditorTool.Road && lightSel == null && arrowSel == null
                    && (road != null || (node != null && ed.Tool != EditorTool.Controller) || decorSel != null));
            SetSelGrid(ed.Tool == EditorTool.Road || (road != null && lightSel == null && arrowSel == null) || (laneMap && decorSel != null && road == null && node == null)
                || (laneMap && node != null && lightSel == null && arrowSel == null && decorSel == null && pointSel == null && buttonSel == null && ed.Tool != EditorTool.Controller));

            if (ed.Tool == EditorTool.Road && laneMap)
            {
                selHeader.text = "Propiedades";
                PanelIcon("eye", "Visible", !np.hidden, () => ed.ToggleNewLaneHidden());
                PanelIcon("lanechange", "Cambio\nde carril", !np.noLaneChange, () => ed.ToggleNewLaneChange());
                PanelIcon("vehicle", "Tráfico\npermitido", np.allow != 0 || np.tram, () => ShowAllowGroups());
                LanePropCells(np);
            }
            else if (ed.Tool == EditorTool.Road)
            {
                selHeader.text = ed.Pending ? "Calle nueva" : "Calles nuevas";
                if (ed.Pending)
                {
                    PanelIcon("check", "Confirmar", true, () => ed.ConfirmPending(), UIKit.Good);
                    PanelIcon("erase", "Cancelar", true, () => ed.CancelPending(), UIKit.Bad);
                    PanelIcon("flip", "Invertir", false, () => ed.FlipPending());
                }
                PanelIcon("layer", "Capa " + ed.CurrentLayer, ed.CurrentLayer > 0, () => ed.CycleCurrentLayer());
                PanelIcon("lanes", "Carriles " + ed.NewRoadLanes, false, () => { ed.NewRoadLanes = ed.NewRoadLanes % 3 + 1; });
                PanelIcon("oneway", ed.NewRoadOneWay ? "Un sentido" : "Doble", ed.NewRoadOneWay, () => { ed.NewRoadOneWay = !ed.NewRoadOneWay; });
                PanelIcon("walk", "Peatonal", ed.NewRoadPedestrian, () => { ed.NewRoadPedestrian = !ed.NewRoadPedestrian; });
            }
            else if (lightSel != null)
            {
                var lt = lightSel;
                selHeader.text = "Semáforo";
                SessionButtons(ed);
                PanelButton("Modelo: " + SignItem.ModelNames[Mathf.Clamp(lt.model, 0, SignItem.ModelNames.Length - 1)], UIKit.Neutral, () => ed.CycleLightModel());
                PanelButton(lt.side == 0 ? "Lado: derecha" : "Lado: izquierda", UIKit.Neutral, () => ed.ToggleLightSide());
                PanelButton("Tamaño −", UIKit.Neutral, () => ed.AdjustLightSize(-0.1f));
                PanelButton("Tamaño +", UIKit.Neutral, () => ed.AdjustLightSize(0.1f));
                PanelLabel("Tamaño " + Mathf.RoundToInt(lt.size * 100f) + "%");
                PanelButton(lt.phase == 0 ? "Fase: independiente" : "Fase: " + lt.phase, lt.phase > 0 ? UIKit.Orange : UIKit.Neutral, () => ed.CycleLightPhase());
                PanelButton("Copiar al cruce", UIKit.Accent, () => ed.CopyLightToNode());
                PanelButton("Centrar", UIKit.Neutral, () => ed.CenterOnSelection());
                PanelButton("Quitar", UIKit.Bad, () => ed.DeleteSelectedLight());
            }
            else if (arrowSel != null)
            {
                var ar = arrowSel;
                selHeader.text = "Flecha funcional";
                SessionButtons(ed);
                PanelButton("Izquierda", (ar.moves & 1) != 0 ? UIKit.Good : UIKit.Dim, () => ed.ToggleArrowMove(1));
                PanelButton("Recto", (ar.moves & 2) != 0 ? UIKit.Good : UIKit.Dim, () => ed.ToggleArrowMove(2));
                PanelButton("Derecha", (ar.moves & 4) != 0 ? UIKit.Good : UIKit.Dim, () => ed.ToggleArrowMove(4));
                PanelLabel(MapEditorController.MaskName(ar.moves));
                PanelButton("Posición −5%", UIKit.Neutral, () => ed.MoveArrow(-0.05f));
                PanelButton("Posición +5%", UIKit.Neutral, () => ed.MoveArrow(0.05f));
                PanelLabel("Posición " + Mathf.RoundToInt(ar.t * 100f) + "%");
                PanelLabel("Tamaño " + Mathf.RoundToInt(ar.size * 100f) + "%");
                PanelButton("Tamaño −", UIKit.Neutral, () => ed.SizeArrow(-0.1f));
                PanelButton("Tamaño +", UIKit.Neutral, () => ed.SizeArrow(0.1f));
                PanelButton("Duplicar", UIKit.Neutral, () => ed.DuplicateArrow());
                PanelButton("Copiar a carriles", UIKit.Accent, () => ed.CopyArrowToSiblings());
                PanelButton("Centrar", UIKit.Neutral, () => ed.CenterOnSelection());
                PanelButton("Borrar", UIKit.Bad, () => ed.DeleteArrow());
            }
            else if (road != null && laneMap)
            {
                // Mismo orden que el original: Visible, Flechas, Cambio de carril, Tráfico permitido, KM/H, Marcas, colores…
                selHeader.text = "Segmento";
                PanelIcon("eye", "Visible", !road.hidden, () => ed.ToggleSelectedHidden());
                PanelIcon("turns", "Flechas", ed.SelectedArrowIndex() != 0, () => ShowArrowOptions());
                PanelIcon("lanechange", "Cambio\nde carril", !road.noLaneChange, () => ed.ToggleSelectedLaneChange());
                if (road.laneType == RoadData.LaneVehicle)
                    PanelIcon("vehicle", "Tráfico\npermitido", road.allow != 0 || road.tram, () => ShowAllowGroups());
                if (road.laneType == RoadData.LaneSidewalk)
                {
                    PanelIcon("walk", "Semáforo\npeatonal", ed.SelectedHasPedLight(), () => ed.TogglePedestrianLights());
                    // Como el original: estilo del paso peatonal donde la acera cruza la calzada.
                    if (ed.SidewalkCrossesRoad(road))
                        PanelIcon(CrossIcons[Mathf.Clamp(road.crossStyle, 0, CrossIcons.Length - 1)], "Paso\npeatonal", road.crossStyle != 0, () => ShowCrossStyles(road));
                }
                LanePropCells(road, () => PanelIcon("marks", "Marcas", !road.noMarks, () => ed.ToggleSelectedMarks()));
                PanelLabel(RoadData.LaneTypeNames[Mathf.Clamp(road.laneType, 0, RoadData.LaneTypeNames.Length - 1)] + "\n" + (ed.Map.CenterOf(road).Length * MapEditorController.MetersPerUnit).ToString("0") + " m", 16);
                PanelNumber(road.layer.ToString(), "Capa", road.layer != 0, () => ShowLayerPicker(road.layer));
                PanelIcon(road.ramp > 0 ? "rampup" : (road.ramp < 0 ? "rampdown" : "rampflat"), road.ramp > 0 ? "Rampa sube" : (road.ramp < 0 ? "Rampa baja" : "Sin rampa"), road.ramp != 0,
                    () => ed.SetSelectedRamp(road.ramp == 0 ? 1 : (road.ramp > 0 ? -1 : 0)));
                PanelIcon("straight", "Recta", false, () => ed.StraightenSelectedRoad());
                PanelIcon("split", "Dividir", false, () => ed.SplitSelectedRoad());
                PanelIcon("copy", "Copiar\nprop.", false, () => ed.CopyRoadProps());
                if (ed.HasRoadClipboard) PanelIcon("paste", "Pegar\nprop.", false, () => ed.PasteRoadProps());
                PanelIcon("lock", road.locked ? "Bloqueado" : "Bloquear", road.locked, () => ed.ToggleLockSelected());
                PanelIcon("center", "Centrar", false, () => ed.CenterOnSelection());
            }
            else if (road != null)
            {
                selHeader.text = "Segmento";
                PanelIcon("check", "Confirmar", true, () => ed.ConfirmSelection(), UIKit.Good);
                PanelIcon("erase", "Cancelar", true, () => ed.CancelSelection(), UIKit.Bad);
                PanelIcon("center", "Centrar", false, () => ed.CenterOnSelection());
                float lenM = ed.Map.CenterOf(road).Length * MapEditorController.MetersPerUnit;
                float widthM = (road.lanesAB + road.lanesBA) * RoadNetworkManager.LaneWidth * MapEditorController.MetersPerUnit;
                PanelLabel("Largo\n" + lenM.ToString("0") + " m", 18);
                PanelLabel("Ancho\n" + widthM.ToString("0.0") + " m", 18);
                PanelIcon("layer", "Capa " + road.layer, road.layer > 0, () => ed.CycleSelectedLayer());
                PanelIcon("minus", "A→B", false, () => ed.AdjustLanes(true, -1));
                PanelLabel("A→B\n" + road.lanesAB + " carr.", 18);
                PanelIcon("plus", "A→B", false, () => ed.AdjustLanes(true, 1));
                PanelIcon("minus", "B→A", false, () => ed.AdjustLanes(false, -1));
                PanelLabel("B→A\n" + road.lanesBA + " carr.", 18);
                PanelIcon("plus", "B→A", false, () => ed.AdjustLanes(false, 1));
                PanelIcon("eye", road.hidden ? "Oculta" : "Visible", !road.hidden, () => ed.ToggleSelectedHidden());
                PanelIcon("lanechange", "Cambio\nde carril", !road.noLaneChange, () => ed.ToggleSelectedLaneChange());
                PanelIcon("vehicle", "Tráfico\npermitido", road.allow != 0 || ed.SelLaneMask != RoadData.AllVehicles, () => ShowAllowDialog());
                PanelIcon("speed", "Límite", road.limited, () => ed.ToggleSelectedSpeedLimit());
                PanelIcon("marks", road.noMarks ? "Sin marcas" : "Marcas", !road.noMarks, () => ed.ToggleSelectedMarks());
                PanelIcon("color", MapRenderer.RoadColorNames[Mathf.Clamp(road.color, 0, MapRenderer.RoadColorNames.Length - 1)], road.color != 0, () => ed.CycleSelectedColor());
                PanelIcon("turns", "Flechas", false, () => ed.SetTool(EditorTool.LaneMask));
                PanelIcon("crosswalk", "Paso", false, () => ed.ToggleSelectedCrosswalk());
                PanelIcon("sidewalk", "Acera", road.sidewalk >= 0f, () => ed.CycleSelectedSidewalk());
                PanelIcon("median", road.MedianWidth > 0f ? (road.median * MapEditorController.MetersPerUnit).ToString("0.#") + " m" : "Mediana", road.MedianWidth > 0f, () => ed.CycleSelectedMedian());
                if (road.MedianWidth > 0f) PanelIcon("median", RoadData.MedianNames[road.medianType], false, () => ed.CycleSelectedMedianType());
                PanelIcon("walk", "Peatonal", road.pedestrian, () => ed.ToggleSelectedPedestrian());
                PanelIcon("cables", "Cables", road.cables, () => ed.ToggleSelectedCables());
                PanelIcon("rails", "Barandilla", road.rails, () => ed.ToggleSelectedRails());
                PanelIcon("oneway", "Sentido", false, () => ed.ToggleSelectedOneWay());
                PanelIcon("flip", "Invertir", false, () => ed.FlipSelectedRoad());
                PanelIcon("straight", "Recta", false, () => ed.StraightenSelectedRoad());
                PanelIcon("split", "Dividir", false, () => ed.SplitSelectedRoad());
                PanelIcon("copy", "Copiar\nprop.", false, () => ed.CopyRoadProps());
                if (ed.HasRoadClipboard) PanelIcon("paste", "Pegar\nprop.", false, () => ed.PasteRoadProps());
                PanelIcon("lock", road.locked ? "Bloqueada" : "Bloquear", road.locked, () => ed.ToggleLockSelected());
                PanelIcon("erase", "Borrar", true, () => ed.DeleteSelected(), UIKit.Bad);
            }
            else if (ed.Tool == EditorTool.Controller && node != null)
            {
                selHeader.text = "Controlador de semáforos";
                SessionButtons(ed);
                PanelButton("Modo: " + MapEditorController.CtrlNames[Mathf.Clamp(node.ctrl, 0, 2)], node.ctrl != 0 ? UIKit.Orange : UIKit.Neutral, () => ed.CycleNodeCtrl());
                PanelButton(ed.NodeHasLights(node) ? "Con semáforos" : "Sin semáforos", ed.NodeHasLights(node) ? UIKit.Good : UIKit.Dim, () => ed.ToggleSelectedNodeLights());
                float g = node.green <= 0.1f ? ed.Map.traffic.greenSeconds : node.green;
                float yv = node.yellow <= 0.1f ? 1.4f : node.yellow;
                PanelButton("Verde −", UIKit.Neutral, () => ed.AdjustNodeGreen(-1f));
                PanelLabel("Verde: " + g.ToString("0") + " s");
                PanelButton("Verde +", UIKit.Neutral, () => ed.AdjustNodeGreen(1f));
                PanelButton("Ámbar −", UIKit.Neutral, () => ed.AdjustNodeYellow(-0.2f));
                PanelLabel("Ámbar: " + yv.ToString("0.0") + " s");
                PanelButton("Ámbar +", UIKit.Neutral, () => ed.AdjustNodeYellow(0.2f));
                float arv = node.allRed <= 0.05f ? 0.8f : node.allRed;
                PanelButton("Espera −", UIKit.Neutral, () => ed.AdjustNodeAllRed(-0.2f));
                PanelLabel("Todo rojo: " + arv.ToString("0.0") + " s");
                PanelButton("Espera +", UIKit.Neutral, () => ed.AdjustNodeAllRed(0.2f));
                PanelButton(node.sensor ? "Sensor activado" : "Sensor: no", node.sensor ? UIKit.Good : UIKit.Dim, () => ed.ToggleNodeSensor());
                PanelButton("Restablecer", UIKit.Neutral, () => ed.ResetNodeController());
            }
            else if (decorSel != null && laneMap)
            {
                var dd = decorSel;
                selHeader.text = MapEditorController.DecorName(dd.type);
                PanelIcon("minus", "Tamaño", false, () => ed.AdjustDecorSize(-0.1f));
                PanelIcon("plus", Mathf.RoundToInt(dd.size * 100f) + "%\nTamaño", false, () => ed.AdjustDecorSize(0.1f));
                bool rotatable = !DecorItem.IsLine(dd.type) && !DecorItem.IsArea(dd.type) && !DecorItem.IsPoly(dd.type) && dd.type != DecorType.Tree && dd.type != DecorType.Park
                    && dd.type != DecorType.Smoke;
                if (rotatable)
                {
                    if (dd.type != DecorType.TrafficSign)   // la señal se gira con su rueda, como el original
                    {
                        PanelIcon("undo", "Girar", false, () => ed.RotateDecor(-15f));
                        PanelIcon("redo", dd.rot == 0f ? "Girar\n(auto)" : "Girar\n" + Mathf.RoundToInt(dd.rot) + "°", dd.rot != 0f, () => ed.RotateDecor(15f));
                    }
                    PanelIcon("flip", "Voltear", false, () => ed.RotateDecor(180f));
                }
                if (dd.type == DecorType.TrafficSign)
                {
                    PanelIcon(SignCatalog.SpriteName(dd.variant), "Tipo", false, () => ShowSignCatalog(dd.variant, i => { ed.SetDecorVariant(i); ed.NewSignFace = i; }));
                    PanelIcon("pole", "Poste", dd.color == 0, () => ed.ToggleSignPole());
                }
                else if (dd.type == DecorType.Lamp)
                {
                    // Farola como el original: doble sentido y color de la luz (solo alumbra de noche).
                    PanelIcon("lamp", "Doble\nsentido", dd.variant % 2 == 1, () => ed.CycleDecorVariant());
                    PanelIcon("color", "Luz:\n" + MapRenderer.LampColorNames[Mathf.Abs(dd.color) % MapRenderer.LampColorNames.Length], dd.color != 0, () => ed.CycleDecorColor());
                    PanelIcon("minus", "Alcance", false, () => ed.AdjustLampLight(-0.1f));
                    PanelIcon("plus", Mathf.RoundToInt(dd.light * 100f) + "%\nAlcance", dd.light != 1f, () => ed.AdjustLampLight(0.1f));
                }
                else if (dd.type == DecorType.FloodLight)
                {
                    // Como el original: «Altura» en niveles (1 a 10) y color de la luz.
                    int lv = MapRenderer.FloodLevels(dd);
                    PanelIcon("minus", "Altura", false, () => ed.ApplyDecor(x => x.height = Mathf.Max(2, MapRenderer.FloodLevels(x) - 1)));
                    PanelIcon("plus", lv + "\nAltura", false, () => ed.ApplyDecor(x => x.height = Mathf.Min(10, MapRenderer.FloodLevels(x) + 1)));
                    PanelIcon("color", "Luz:\n" + MapRenderer.LampColorNames[Mathf.Abs(dd.color) % MapRenderer.LampColorNames.Length], dd.color != 0, () => ed.CycleDecorColor());
                }
                else if (DecorPropsIC2(dd)) { }
                else
                    PanelIcon(DecorIconOf(dd.type), "Tipo", dd.variant != 0, () => ed.CycleDecorVariant());
                if (dd.type == DecorType.Trailer || dd.type == DecorType.GlowLight || dd.type == DecorType.Crane
                    || dd.type == DecorType.Lines || dd.type == DecorType.Boat || dd.type == DecorType.StopLine)
                    PanelIcon("color", "Color", dd.color != 0, () => ed.CycleDecorColor());
                if (dd.type == DecorType.TextLabel)
                {
                    // Como el original: alto aparte del ancho (Y %) y color con el selector.
                    float ys = dd.y2 > 0.05f ? dd.y2 : 1f;
                    PanelIcon("minus", "Alto", false, () => ed.ApplyDecor(x => x.y2 = Mathf.Max(0.3f, Mathf.Round(((x.y2 > 0.05f ? x.y2 : 1f) - 0.1f) * 10f) / 10f)));
                    PanelIcon("plus", Mathf.RoundToInt(ys * 100f) + "%\nAlto", Mathf.Abs(ys - 1f) > 0.01f, () => ed.ApplyDecor(x => x.y2 = Mathf.Min(4f, Mathf.Round(((x.y2 > 0.05f ? x.y2 : 1f) + 0.1f) * 10f) / 10f)));
                    PanelIcon("color", "Color", dd.color != 0 || RoadData.ValidHex(dd.hexA), () => PickPalette("Color del texto", MapRenderer.ObjColorNames, MapRenderer.ObjColors, dd.color, i => ed.ApplyDecor(x => { x.color = i; x.hexA = ""; }), false, hx => ed.ApplyDecor(x => x.hexA = hx), dd.hexA));
                    UIKit.Input(selBody, string.IsNullOrEmpty(dd.text) ? "Texto" : dd.text, 40, str => ed.SetDecorText(str));
                    selCount++;
                }
                PanelIcon("copy", "Duplicar", false, () => ed.DuplicateDecor());
                PanelIcon("lock", dd.locked ? "Bloqueado" : "Bloquear", dd.locked, () => ed.ToggleLockSelected());
                PanelIcon("center", "Centrar", false, () => ed.CenterOnSelection());
            }
            else if (decorSel != null)
            {
                var dd = decorSel;
                selHeader.text = MapEditorController.DecorName(dd.type);
                SessionButtons(ed);
                if (dd.IsLine()) PanelLabel("Largo " + (Vector2.Distance(dd.Pos, dd.End) * MapEditorController.MetersPerUnit).ToString("0.#") + " m");
                PanelButton("Centrar", UIKit.Neutral, () => ed.CenterOnSelection());
                PanelButton("Tamaño −", UIKit.Neutral, () => ed.AdjustDecorSize(-0.1f));
                PanelLabel("Tamaño: " + Mathf.RoundToInt(dd.size * 100f) + "%");
                PanelButton("Tamaño +", UIKit.Neutral, () => ed.AdjustDecorSize(0.1f));
                if (!DecorItem.IsLine(dd.type) && !DecorItem.IsArea(dd.type) && dd.type != DecorType.Tree && dd.type != DecorType.Park && dd.type != DecorType.Building
                    && dd.type != DecorType.Smoke)
                {
                    PanelButton("Girar −", UIKit.Neutral, () => ed.RotateDecor(-15f));
                    PanelLabel(dd.rot == 0f ? "Giro: auto" : "Giro: " + Mathf.RoundToInt(dd.rot) + "°");
                    PanelButton("Girar +", UIKit.Neutral, () => ed.RotateDecor(15f));
                    PanelButton("Giro auto", UIKit.Neutral, () => ed.ResetDecorRotation());
                }
                if (dd.type == DecorType.Material) PanelButton("Material: " + MapRenderer.MaterialNames[dd.variant % MapRenderer.MaterialNames.Length], UIKit.Neutral, () => ed.CycleDecorVariant());
                else if (dd.type == DecorType.Sticker) PanelButton("Dibujo: " + MapRenderer.StickerNames[dd.variant % MapRenderer.StickerNames.Length], UIKit.Neutral, () => ed.CycleDecorVariant());
                else if (dd.type == DecorType.Misc) PanelButton("Objeto: " + MapRenderer.MiscNames[dd.variant % MapRenderer.MiscNames.Length], UIKit.Neutral, () => ed.CycleDecorVariant());
                else if (dd.type == DecorType.Lines) PanelButton("Estilo: " + MapRenderer.LineStyles[dd.variant % MapRenderer.LineStyles.Length], UIKit.Neutral, () => ed.CycleDecorVariant());
                else if (dd.type == DecorType.RoadSign) PanelButton("Cartel: " + MapRenderer.SignFaces[dd.variant % MapRenderer.SignFaces.Length], UIKit.Neutral, () => ed.CycleDecorVariant());
                else PanelButton("Variante", UIKit.Neutral, () => ed.CycleDecorVariant());
                if (dd.type == DecorType.Trailer || dd.type == DecorType.GlowLight || dd.type == DecorType.Crane || dd.type == DecorType.Sticker
                    || dd.type == DecorType.TextLabel || dd.type == DecorType.Lines || dd.type == DecorType.Boat || dd.type == DecorType.StopLine)
                    PanelButton("Color: " + MapRenderer.ObjColorNames[dd.color % MapRenderer.ObjColorNames.Length], UIKit.Neutral, () => ed.CycleDecorColor());
                if (dd.type == DecorType.TextLabel)
                {
                    UIKit.Input(selBody, string.IsNullOrEmpty(dd.text) ? "Texto" : dd.text, 40, str => ed.SetDecorText(str));
                    selCount++;
                }
                PanelButton(dd.locked ? "Desbloquear" : "Bloquear", dd.locked ? UIKit.Orange : UIKit.Neutral, () => ed.ToggleLockSelected());
                PanelButton("Duplicar", UIKit.Neutral, () => ed.DuplicateDecor());
                PanelButton("Borrar", UIKit.Bad, () => ed.DeleteSelected());
            }
            else if (pointSel != null)
            {
                var pp = pointSel;
                selHeader.text = pp.kind == PointKind.BusStop ? "Parada de autobús" : (pp.kind == PointKind.Toll ? "Peaje" : "Advertencia de vehículo");
                SessionButtons(ed);
                if (pp.kind != PointKind.Detector)
                {
                    PanelButton("Espera −", UIKit.Neutral, () => ed.AdjustPointSeconds(-0.5f));
                    PanelLabel("Espera: " + pp.seconds.ToString("0.0") + " s");
                    PanelButton("Espera +", UIKit.Neutral, () => ed.AdjustPointSeconds(0.5f));
                }
                if (pp.kind != PointKind.Toll) PanelButton(MapEditorController.PointDirNames[Mathf.Clamp(pp.dir, 0, 2)], UIKit.Neutral, () => ed.CyclePointDir());
                PanelButton("Borrar", UIKit.Bad, () => ed.DeleteSelected());
            }
            else if (buttonSel != null)
            {
                var bb = buttonSel;
                selHeader.text = "Botón de semáforos";
                SessionButtons(ed);
                bool linking = ed.LinkingButtonId == bb.id;
                PanelButton(linking ? "Vinculando…" : "Vincular", linking ? UIKit.Orange : UIKit.Neutral, () => ed.ToggleLinking());
                PanelLabel("Vínculos: " + bb.links.Count);
                PanelButton("Quitar vínculos", UIKit.Neutral, () => ed.ClearButtonLinks());
                PanelButton("Borrar", UIKit.Bad, () => ed.DeleteSelected());
            }
            else if (node != null && laneMap && ed.IsStartNode(node.id))
            {
                // Como el original: de qué vehículos y cuántos salen de aquí, y a qué destinos van.
                selHeader.text = "Nodo de inicio";
                for (int i = 0; i < NodeData.RateCount; i++)
                {
                    int ti = i;
                    float rv = node.GetRate(i);
                    string val = !node.CustomRates ? "A" : (rv < 0f ? "0" : rv.ToString("0.#"));
                    PanelNumber(val, NodeData.RateNames[i] + "\npor minuto", node.CustomRates, () =>
                    {
                        float[] steps = { -1f, 0f, 0.5f, 1f, 2f, 3f, 5f, 8f, 10f, 15f, 20f, 30f };
                        var opts = new List<Opt>();
                        int cur = 0;
                        for (int k = 0; k < steps.Length; k++)
                        {
                            opts.Add(O(null, k == 0 ? "Automático" : "por minuto", null, null, k == 0 ? "A" : steps[k].ToString("0.#")));
                            if (node.CustomRates ? Mathf.Approximately(steps[k], Mathf.Max(0f, node.GetRate(ti))) : k == 0) cur = k;
                        }
                        ShowOptions(NodeData.RateNames[ti] + " por minuto", opts, cur, k => ed.SetSelectedNodeRate(ti, steps[k]));
                    });
                }
                PanelIcon("exit", "Probabilidad\nde destino", node.dest != null && node.dest.Count > 0, () => ShowDestDialog(node));
                PanelIcon("center", "Centrar", false, () => ed.CenterOnSelection());
            }
            else if (node != null && laneMap)
            {
                selHeader.text = "Nodo";
                int ph = ed.NodeLightPhase(node.id);
                PanelIcon("signal", "Semáforo", ed.NodeHasSign(node.id, SignType.Light), () => ed.ToggleSelectedNodeSign(SignType.Light));
                // Como el original: tocar Stop o Ceda muestra sus opciones (quitar, estilos y, en el stop, controlado por el usuario).
                bool hasYield = ed.NodeHasSign(node.id, SignType.Yield), hasStop = ed.NodeHasSign(node.id, SignType.Stop);
                PanelIcon(hasYield ? YieldStyleIcons[ed.NodeSignStyle(node.id, SignType.Yield)] : "yield", "Ceda el paso", hasYield, () =>
                    ShowOptions("Ceda el paso", new List<Opt> { O("erase", "Sin ceda"), O("yl_teeth", "Triángulos"), O("yl_dash", "Discontinua"), O("yl_dots", "Puntos") },
                        hasYield ? ed.NodeSignStyle(node.id, SignType.Yield) + 1 : 0, i => ed.SetSelectedNodeSign(SignType.Yield, i - 1)));
                PanelIcon(hasStop ? StopStyleIcons[ed.NodeSignStyle(node.id, SignType.Stop)] : "stop", hasStop && ed.NodeSignStyle(node.id, SignType.Stop) == 2 ? "Controlado\npor el usuario" : "Stop", hasStop, () =>
                    ShowOptions("Stop", new List<Opt> { O("erase", "Sin stop"), O("stl_line", "Línea"), O("stl_squares", "Cuadros"), O("stl_user", "Controlado\npor el usuario") },
                        hasStop ? ed.NodeSignStyle(node.id, SignType.Stop) + 1 : 0, i => ed.SetSelectedNodeSign(SignType.Stop, i - 1)));
                PanelIcon("lamp", "Farola", ed.NodeHasLamp(node.id), () => ed.ToggleSelectedNodeLamp());
                var nl = ed.NodeLight(node.id);
                if (nl != null)
                {
                    PanelIcon(LightModelIcons[Mathf.Clamp(nl.model, 0, LightModelIcons.Length - 1)], "Modelo", nl.model != 0, () =>
                    {
                        var opts = new List<Opt>();
                        for (int i = 0; i < SignItem.ModelNames.Length; i++) opts.Add(O(LightModelIcons[i], SignItem.ModelNames[i]));
                        ShowOptions("Modelo de semáforo", opts, Mathf.Clamp(nl.model, 0, opts.Count - 1), i => ed.SetSelectedNodeLightModel(i));
                    });
                    PanelIcon(nl.side == 0 ? "edgeR" : "edgeL", nl.side == 0 ? "Lado\nderecho" : "Lado\nizquierdo", nl.side != 0, () => ed.SetSelectedNodeLightSide(1 - nl.side));
                    PanelIcon("minus", "Tamaño", false, () => ed.AdjustSelectedNodeLightSize(-0.1f));
                    PanelIcon("plus", Mathf.RoundToInt((nl.size <= 0.01f ? 1f : nl.size) * 100f) + "%\nTamaño", false, () => ed.AdjustSelectedNodeLightSize(0.1f));
                    PanelIcon(nl.hidden ? "eyeoff" : "eye", "Visible", !nl.hidden, () => ed.ToggleSelectedNodeLightVisible());
                }
                PanelIcon("phase", "Cambiar fase\n−", false, () => ed.ShiftSelectedNodePhase(-1));
                PanelIcon("phase", ph == 0 ? "Cambiar fase\n+ (auto)" : "Cambiar fase\n+ (" + ph + ")", ph > 0, () => ed.ShiftSelectedNodePhase(1));
                PanelIcon("center", "Centrar", false, () => ed.CenterOnSelection());
            }
            else if (node != null)
            {
                selHeader.text = "Nodo";
                SessionButtons(ed);
                PanelButton("Centrar", UIKit.Neutral, () => ed.CenterOnSelection());
                PanelButton(node.entry ? "Entrada (sí)" : "Entrada", node.entry ? UIKit.Good : UIKit.Neutral, () => ed.ToggleSelectedNodeFlag(0));
                PanelButton(node.exit ? "Salida (sí)" : "Salida", node.exit ? UIKit.Bad : UIKit.Neutral, () => ed.ToggleSelectedNodeFlag(1));
                PanelButton(ed.NodeHasLights(node) ? "Semáforos (sí)" : "Semáforos", ed.NodeHasLights(node) ? UIKit.Orange : UIKit.Neutral, () => ed.ToggleSelectedNodeLights());
                PanelButton(node.roundabout ? "Rotonda (sí)" : "Rotonda", node.roundabout ? UIKit.Orange : UIKit.Neutral, () => ed.ToggleSelectedNodeFlag(2));
                if (node.roundabout)
                {
                    PanelButton("Radio −", UIKit.Neutral, () => ed.AdjustRoundaboutRadius(-0.15f));
                    PanelButton("Radio +", UIKit.Neutral, () => ed.AdjustRoundaboutRadius(0.15f));
                }
                PanelButton("Borrar", UIKit.Bad, () => ed.DeleteSelected());
            }

        }

        static readonly string[] StopStyleIcons = { "stl_line", "stl_squares", "stl_user" };
        static readonly string[] YieldStyleIcons = { "yl_teeth", "yl_dash", "yl_dots" };
        static readonly string[] LightModelIcons = { "lt_vertical", "lt_horizontal", "lt_arrows", "lt_ped" };

        static string StartKey(NodeData n)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < NodeData.RateCount; i++) sb.Append(n.GetRate(i).ToString("0.#")).Append(',');
            if (n.dest != null) foreach (var d in n.dest) sb.Append(d.node).Append('=').Append(d.weight.ToString("0")).Append(';');
            return sb.ToString();
        }

        RectTransform destRoot;

        /// <summary>Probabilidad de destino: cada final de carril (numerado también sobre el mapa) con su porcentaje.</summary>
        void ShowDestDialog(NodeData start)
        {
            var ed = game.editor;
            CloseMenus(true);
            DestroyRoot(ref destRoot);
            destRoot = NewRoot("Dest", new Color(0, 0, 0, 0.01f));
            var bg = destRoot.gameObject.AddComponent<Button>();
            bg.onClick.AddListener(() => { DestroyRoot(ref destRoot); selPanelKey = "~"; });
            var ends = ed.EndNodes();
            const float rowH = 58f, head = 48f;
            float h = head + Mathf.Max(1, ends.Count) * rowH + 70f;
            var box = UIKit.Panel(destRoot, "Box", PanelDark);
            PlacePopup(box.rectTransform, 340f, h, FitColsBeside(340f));
            var hd = UIKit.Panel(box.transform, "Head", UIKit.Orange);
            Place(hd.rectTransform, 0, 1, 1, 1, 0, -head, 0, 0);
            var ht = UIKit.Label(hd.transform, "Probabilidad de destino", 22, TextAnchor.MiddleLeft, Color.white);
            UIKit.Stretch(ht.rectTransform, 12, 0, 8, 0);
            var body = UIKit.Rect("Body", box.transform);
            Place(body, 0, 0, 1, 1, 10, 64, 10, head + 6f);
            UIKit.VLayout(body, 4, 0);
            float total = 0f;
            foreach (var e in ends) total += ed.DestWeightOf(start, e.id);
            if (ends.Count == 0)
                UIKit.Size(UIKit.Label(body, "No hay finales de carril.", 20, TextAnchor.MiddleCenter, Color.white), -1, rowH);
            for (int i = 0; i < ends.Count; i++)
            {
                int endId = ends[i].id;
                float w = ed.DestWeightOf(start, endId);
                var row = UIKit.Rect("Row", body);
                UIKit.Size(row, -1, rowH - 4f);
                UIKit.HLayout(row, 6, 0);
                UIKit.Size(UIKit.Label(row, (i + 1).ToString(), 26, TextAnchor.MiddleCenter, UIKit.Orange), 36, -1);
                UIKit.Size(UIKit.Button(row, "−", UIKit.Neutral, () => { ed.AdjustSelectedDest(endId, -10f); ShowDestDialog(start); }, 30), 60, -1);
                UIKit.Size(UIKit.Label(row, total > 0.001f ? Mathf.RoundToInt(w / total * 100f) + " %" : "0 %", 24, TextAnchor.MiddleCenter, Color.white), 90, -1);
                UIKit.Size(UIKit.Button(row, "+", UIKit.Neutral, () => { ed.AdjustSelectedDest(endId, 10f); ShowDestDialog(start); }, 30), 60, -1);
            }
            var reset = UIKit.Button(box.transform, "Todos por igual", UIKit.Neutral, () => { ed.ResetSelectedDest(); ShowDestDialog(start); }, 22);
            Place(Rt(reset), 0, 0, 1, 0, 10, 10, 10, -54);
        }

        bool FitColsBeside(float w)
        {
            float sw = safe != null ? safe.rect.width : 720f;
            return sw - PropsPanelWidth - 24f >= w;
        }

        static string LightKey(SignItem s)
        {
            return s == null ? "-" : s.model + "," + s.side + "," + s.size.ToString("0.0") + "," + s.hidden;
        }

        /// <summary>Confirmar / Cancelar los cambios hechos desde que se seleccionó el elemento.</summary>
        void SessionButtons(MapEditorController ed)
        {
            PanelButton("Confirmar", UIKit.Good, () => ed.ConfirmSelection());
            PanelButton("Cancelar", UIKit.Bad, () => ed.CancelSelection());
        }

        static readonly string[] VehicleNames = { "Auto", "Taxi", "Bus", "Camión", "Moto", "Ambulancia", "Bomberos", "Policía", "Furgoneta" };

        void PanelButton(string text, Color color, Action act)
        {
            UIKit.Button(selBody, text, color, () => { AudioManager.Instance.PlayClick(); act(); selPanelKey = "~"; }, 20);
            selCount++;
        }

        void PanelLabel(string text, int size = 20)
        {
            UIKit.Label(selBody, text, size, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.5f));
            selCount++;
        }

        /// <summary>Cambia la rejilla del panel de propiedades: iconos (3 columnas) o botones de texto (2 columnas).</summary>
        void SetSelGrid(bool icons)
        {
            var grid = selBody.GetComponent<GridLayoutGroup>();
            if (grid == null) return;
            grid.cellSize = icons ? new Vector2(138, 86) : new Vector2(138, 52);
            grid.constraintCount = 2;
            selCols = 2;
            selRowH = icons ? 92f : 58f;
            selShownH = -1f;
        }

        /// <summary>Casilla de propiedad con icono y rótulo (como en la referencia): naranja si está activa.</summary>
        void PanelIcon(string icon, string label, bool active, Action act, Color? activeColor = null)
        {
            Image circ;
            IconCell(selBody, icon, label, () => { act(); selPanelKey = "~"; }, out circ, 15);
            circ.color = active ? (activeColor.HasValue ? activeColor.Value : UIKit.Orange) : CircleGray;
            selCount++;
        }

        /// <summary>Casilla con un número dentro del círculo (por ejemplo «50» KM/H), como en la referencia.</summary>
        void PanelNumber(string number, string label, bool active, Action act)
        {
            Image circ;
            IconCell(selBody, null, label, () => { act(); selPanelKey = "~"; }, out circ, 15);
            circ.color = active ? UIKit.Orange : CircleGray;
            var t = UIKit.Label(circ.transform, number, 22, TextAnchor.MiddleCenter, Color.white);
            UIKit.Stretch(t.rectTransform);
            selCount++;
        }

        // ---------------- Ventanitas de opciones (como en la referencia) ----------------

        class Opt
        {
            public string icon, label, number;
            public Color tint = Color.white;
            public Color? circle;
        }

        RectTransform optRoot;

        static Opt O(string icon, string label, Color? tint = null, Color? circle = null, string number = null)
        {
            return new Opt { icon = icon, label = label, tint = tint ?? Color.white, circle = circle, number = number };
        }

        /// <summary>
        /// Ventanita junto al panel de propiedades con una rejilla de opciones (la actual en naranja). extra = fila inferior
        /// opcional (por ejemplo Posición y color de las flechas). keepOpen = no se cierra al elegir.
        /// </summary>
        void ShowOptions(string title, List<Opt> opts, int current, Action<int> pick, Action<RectTransform> extra = null, bool keepOpen = false)
        {
            DestroyRoot(ref optRoot);
            CloseMenus(true);
            optRoot = NewRoot("Options", new Color(0, 0, 0, 0.01f));
            var bgb = optRoot.gameObject.AddComponent<Button>();
            bgb.onClick.AddListener(() => DestroyRoot(ref optRoot));
            const float cw = 96f, ch = 96f, head = 48f;
            bool beside;
            int cols = FitCols(opts.Count > 6 ? 4 : 3, cw, out beside);
            int rows = (opts.Count + cols - 1) / cols;
            float extraH = extra != null ? 80f : 0f;
            float w = cols * cw + 16f, h = head + rows * ch + 12f + extraH;
            var box = UIKit.Panel(optRoot, "Box", PanelDark);
            PlacePopup(box.rectTransform, w, h, beside);
            var hd = UIKit.Panel(box.transform, "Head", UIKit.Orange);
            Place(hd.rectTransform, 0, 1, 1, 1, 0, -head, 0, 0);
            var ht = UIKit.Label(hd.transform, title, 24, TextAnchor.MiddleLeft, Color.white);
            UIKit.Stretch(ht.rectTransform, 12, 0, 8, 0);
            var grid = UIKit.Rect("Grid", box.transform);
            Place(grid, 0, 1, 1, 1, 8, -(head + 6f + rows * ch), 8, head + 6f);
            UIKit.Grid(grid, new Vector2(cw, ch), 0, 0, cols);
            for (int i = 0; i < opts.Count; i++)
            {
                int idx = i;
                var op = opts[i];
                Image circ;
                IconCell(grid, op.icon, op.label, () =>
                {
                    pick(idx);
                    selPanelKey = "~";
                    if (!keepOpen) DestroyRoot(ref optRoot);
                }, out circ, 14);
                circ.color = idx == current ? UIKit.Orange : (op.circle ?? CircleGray);
                var g = circ.transform.Find("Glyph");
                if (g != null) g.GetComponent<Image>().color = op.tint;
                if (!string.IsNullOrEmpty(op.number))
                {
                    var t = UIKit.Label(circ.transform, op.number, 20, TextAnchor.MiddleCenter, Color.white);
                    UIKit.Stretch(t.rectTransform);
                }
            }
            if (extra != null)
            {
                var ex = UIKit.Rect("Extra", box.transform);
                Place(ex, 0, 0, 1, 0, 8, 8, 8, -(extraH - 8f));
                extra(ex);
            }
        }

        /// <summary>Catálogo de señales de tráfico: rejilla con todas las señales (la elegida en naranja).</summary>
        void ShowSignCatalog(int current, Action<int> pick)
        {
            DestroyRoot(ref optRoot);
            CloseMenus(true);
            optRoot = NewRoot("Signs", new Color(0, 0, 0, 0.01f));
            var bgb = optRoot.gameObject.AddComponent<Button>();
            bgb.onClick.AddListener(() => DestroyRoot(ref optRoot));
            int n = SignCatalog.Count;
            const float cs = 76f, head = 48f;
            bool beside;
            int cols = FitCols(6, cs, out beside);
            int rows = (n + cols - 1) / cols;
            float w = cols * cs + 16f, h = head + rows * cs + 14f;
            var box = UIKit.Panel(optRoot, "Box", PanelDark);
            PlacePopup(box.rectTransform, w, h, beside);
            var hd = UIKit.Panel(box.transform, "Head", UIKit.Orange);
            Place(hd.rectTransform, 0, 1, 1, 1, 0, -head, 0, 0);
            var ht = UIKit.Label(hd.transform, "Señal de tráfico", 24, TextAnchor.MiddleLeft, Color.white);
            UIKit.Stretch(ht.rectTransform, 12, 0, 8, 0);
            var view = UIKit.Rect("View", box.transform);
            Place(view, 0, 0, 1, 1, 8, 6, 8, head + 4f);
            view.gameObject.AddComponent<RectMask2D>();
            var grid = UIKit.Rect("Grid", view);
            grid.anchorMin = new Vector2(0, 1); grid.anchorMax = new Vector2(1, 1); grid.pivot = new Vector2(0.5f, 1f);
            grid.offsetMin = new Vector2(0, -rows * cs); grid.offsetMax = Vector2.zero;
            UIKit.Grid(grid, new Vector2(cs, cs), 0, 0, cols);
            var sr = box.gameObject.AddComponent<ScrollRect>();
            sr.viewport = view; sr.content = grid; sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 30f;
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                var cell = UIKit.Panel(grid, "Sign", idx == current ? UIKit.Orange : new Color(0, 0, 0, 0));
                var b = cell.gameObject.AddComponent<Button>();
                b.targetGraphic = cell;
                b.onClick.AddListener(() => { AudioManager.Instance.PlayClick(); pick(idx); selPanelKey = "~"; DestroyRoot(ref optRoot); });
                var img = UIKit.Panel(cell.transform, "Img", Color.white, false);
                img.sprite = IconKit.Get(SignCatalog.SpriteName(idx));
                img.preserveAspect = true;
                var ir = img.rectTransform;
                ir.anchorMin = ir.anchorMax = new Vector2(0.5f, 0.5f);
                ir.sizeDelta = new Vector2(cs - 14f, cs - 14f);
                ir.anchoredPosition = Vector2.zero;
            }
        }

        float PanelTop { get { return categoryOff ? 0f : 154f; } }

        float PropsPanelWidth { get { return 300f + (Screen.width > Screen.height ? 100f : 0f); } }

        /// <summary>
        /// Columnas que caben en una ventanita. Si caben al menos 3 a la izquierda del panel de propiedades, va al lado
        /// (beside); si no, va debajo del panel, a lo ancho de la pantalla, para no salirse ni taparlo.
        /// </summary>
        int FitCols(int want, float cw, out bool beside)
        {
            float sw = safe != null ? safe.rect.width : 720f;
            int side = Mathf.FloorToInt((sw - PropsPanelWidth - 32f) / cw);
            if (side >= Mathf.Min(want, 3)) { beside = true; return Mathf.Clamp(side, 1, want); }
            beside = false;
            return Mathf.Clamp(Mathf.FloorToInt((sw - 32f) / cw), 1, Mathf.Max(want, 6));
        }

        /// <summary>Coloca una ventanita sin salirse de la pantalla: al lado del panel de propiedades o debajo de él.</summary>
        void PlacePopup(RectTransform box, float w, float h, bool beside)
        {
            float sw = safe != null ? safe.rect.width : 720f, sh = safe != null ? safe.rect.height : 1280f;
            w = Mathf.Min(w, sw - 16f);
            if (beside)
            {
                h = Mathf.Min(h, sh - PanelTop - 16f);
                Corner(box, 1, 1, w, h, -(PropsPanelWidth + 8f), -PanelTop);
                return;
            }
            float top = PanelTop + (selPanel != null && selPanel.gameObject.activeSelf ? Mathf.Max(0f, selShownH) : 0f) + 8f;
            if (top + h > sh - 8f) top = Mathf.Max(60f, sh - 8f - h);
            h = Mathf.Min(h, sh - top - 8f);
            Corner(box, 0.5f, 1, w, h, 0f, -top);
        }

        // ---------------- Paleta de color del carril (como el original) ----------------

        static Sprite hueSprite;

        static Sprite HueSprite()
        {
            if (hueSprite != null) return hueSprite;
            var tex = new Texture2D(128, 1, TextureFormat.RGBA32, false);
            for (int x = 0; x < 128; x++) tex.SetPixel(x, 0, Color.HSVToRGB(x / 127f, 1f, 1f));
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();
            hueSprite = Sprite.Create(tex, new Rect(0, 0, 128, 1), new Vector2(0.5f, 0.5f), 100f);
            return hueSprite;
        }

        static List<string> SavedLaneColors()
        {
            var list = new List<string>();
            foreach (var h in PlayerPrefs.GetString("lane_colors", "").Split(';')) if (RoadData.ValidHex(h)) list.Add(h);
            return list;
        }

        static void SaveLaneColor(string hex)
        {
            var list = SavedLaneColors();
            list.Remove(hex);
            list.Insert(0, hex);
            if (list.Count > 12) list.RemoveRange(12, list.Count - 12);
            PlayerPrefs.SetString("lane_colors", string.Join(";", list.ToArray()));
            PlayerPrefs.Save();
        }

        /// <summary>Barra deslizante 0..1 hecha por código. onUp se llama al soltar (ahí se aplica el cambio, un solo «deshacer»).</summary>
        Slider ColorSlider(Transform parent, float y, Sprite bg, Color bgCol, float value, Action<float> onChange, Action onUp)
        {
            var root = UIKit.Rect("Slider", parent);
            root.anchorMin = new Vector2(0, 1); root.anchorMax = new Vector2(1, 1); root.pivot = new Vector2(0.5f, 1f);
            root.offsetMin = new Vector2(16, y - 34); root.offsetMax = new Vector2(-16, y);
            var back = UIKit.Panel(root, "Back", bgCol);
            if (bg != null) back.sprite = bg;
            UIKit.Stretch(back.rectTransform, 0, 10, 0, 10);
            var area = UIKit.Rect("HandleArea", root);
            UIKit.Stretch(area, 12, 0, 12, 0);
            var handle = UIKit.Panel(area, "Handle", Color.white);
            handle.sprite = IconKit.Circle;
            handle.rectTransform.sizeDelta = new Vector2(30, 0);
            var sl = root.gameObject.AddComponent<Slider>();
            sl.targetGraphic = handle;
            sl.handleRect = handle.rectTransform;
            sl.direction = Slider.Direction.LeftToRight;
            sl.minValue = 0f; sl.maxValue = 1f;
            sl.value = value;
            sl.onValueChanged.AddListener(v => onChange(v));
            var trig = root.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var e = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerUp };
            e.callback.AddListener(_ => onUp());
            trig.triggers.Add(e);
            return sl;
        }

        /// <summary>
        /// Color del carril: «A» (el normal), los colores del tema, los guardados y un selector libre de tono, saturación y
        /// brillo con su código #RRGGBB, como la paleta del original.
        /// </summary>
        /// <summary>
        /// Selector de cualquier color (como la rueda del original): tono, saturación y brillo con muestra en vivo; al soltar
        /// una barra se aplica. apply recibe el código #RRGGBB.
        /// </summary>
        void ShowHexPicker(string title, Color start, Action<string> apply)
        {
            DestroyRoot(ref optRoot);
            CloseMenus(true);
            optRoot = NewRoot("HexColor", new Color(0, 0, 0, 0.01f));
            var bgb = optRoot.gameObject.AddComponent<Button>();
            bgb.onClick.AddListener(() => { DestroyRoot(ref optRoot); selPanelKey = "~"; });
            const float head = 48f;
            bool beside;
            FitCols(5, 60f, out beside);
            float w = 360f, h = head + 70f + 3 * 44f + 40f;
            var box = UIKit.Panel(optRoot, "Box", PanelDark);
            PlacePopup(box.rectTransform, w, h, beside);
            var hd = UIKit.Panel(box.transform, "Head", UIKit.Orange);
            Place(hd.rectTransform, 0, 1, 1, 1, 0, -head, 0, 0);
            var ht = UIKit.Label(hd.transform, title, 24, TextAnchor.MiddleLeft, Color.white);
            UIKit.Stretch(ht.rectTransform, 12, 0, 8, 0);
            float hh, ss, vv;
            Color.RGBToHSV(start, out hh, out ss, out vv);
            float[] hsv = { hh, ss, vv };
            var prev = UIKit.Panel(box.transform, "Preview", start);
            Place(prev.rectTransform, 0, 1, 0, 1, 16, -(head + 60f), -76, head + 10f);
            var hex = UIKit.Label(box.transform, "#" + ColorUtility.ToHtmlStringRGB(start), 24, TextAnchor.MiddleLeft, Color.white);
            Place(hex.rectTransform, 0, 1, 1, 1, 90, -(head + 60f), 16, head + 10f);
            Action<bool> refresh = doApply =>
            {
                Color c = Color.HSVToRGB(hsv[0], hsv[1], hsv[2]);
                prev.color = c;
                string code = "#" + ColorUtility.ToHtmlStringRGB(c);
                hex.text = code;
                if (doApply) apply(code);
            };
            float y = -(head + 70f);
            ColorSlider(box.transform, y, HueSprite(), Color.white, hsv[0], v => { hsv[0] = v; refresh(false); }, () => refresh(true));
            y -= 44f;
            ColorSlider(box.transform, y, null, new Color(0.55f, 0.55f, 0.6f), hsv[1], v => { hsv[1] = v; refresh(false); }, () => refresh(true));
            y -= 44f;
            ColorSlider(box.transform, y, null, new Color(0.3f, 0.3f, 0.33f), hsv[2], v => { hsv[2] = v; refresh(false); }, () => refresh(true));
            y -= 50f;
            var lab = UIKit.Label(box.transform, "Tono · Saturación · Brillo", 16, TextAnchor.MiddleCenter, new Color(0.8f, 0.85f, 0.9f));
            Place(lab.rectTransform, 0, 1, 1, 1, 16, y, 16, -(y + 22f));
        }

        void ShowLaneColorPicker(RoadData r)
        {
            var ed = game.editor;
            DestroyRoot(ref optRoot);
            CloseMenus(true);
            optRoot = NewRoot("LaneColor", new Color(0, 0, 0, 0.01f));
            var bgb = optRoot.gameObject.AddComponent<Button>();
            bgb.onClick.AddListener(() => { DestroyRoot(ref optRoot); selPanelKey = "~"; });
            const float cs = 56f, head = 48f;
            bool beside;
            int cols = Mathf.Clamp(FitCols(6, cs, out beside), 4, 6);
            var saved = SavedLaneColors();
            int swatches = 1 + (MapRenderer.RoadColors.Length - 1) + 12;
            int rows = (swatches + cols - 1) / cols;
            float w = cols * cs + 32f;
            float h = head + 70f + 3 * 44f + 54f + rows * cs + 16f;
            var box = UIKit.Panel(optRoot, "Box", PanelDark);
            PlacePopup(box.rectTransform, w, h, beside);
            var hd = UIKit.Panel(box.transform, "Head", UIKit.Orange);
            Place(hd.rectTransform, 0, 1, 1, 1, 0, -head, 0, 0);
            var ht = UIKit.Label(hd.transform, "Color", 24, TextAnchor.MiddleLeft, Color.white);
            UIKit.Stretch(ht.rectTransform, 12, 0, 8, 0);

            Color start = RoadData.ValidHex(r.colorHex) ? SpriteFactory.Hex(r.colorHex)
                : (r.color > 0 && r.color < MapRenderer.RoadColors.Length ? SpriteFactory.Hex(MapRenderer.RoadColors[r.color]) : new Color(0.62f, 0.62f, 0.62f));
            float hh, ss, vv;
            Color.RGBToHSV(start, out hh, out ss, out vv);
            float[] hsv = { hh, ss, vv };

            // Muestra y código.
            var prev = UIKit.Panel(box.transform, "Preview", start);
            Place(prev.rectTransform, 0, 1, 0, 1, 16, -(head + 60f), -76, head + 10f);
            var hex = UIKit.Label(box.transform, "#" + ColorUtility.ToHtmlStringRGB(start), 24, TextAnchor.MiddleLeft, Color.white);
            Place(hex.rectTransform, 0, 1, 1, 1, 90, -(head + 60f), 16, head + 10f);

            Action<bool> refresh = apply =>
            {
                Color c = Color.HSVToRGB(hsv[0], hsv[1], hsv[2]);
                prev.color = c;
                string code = "#" + ColorUtility.ToHtmlStringRGB(c);
                hex.text = code;
                if (apply) { ed.ApplyLaneProp(x => { x.colorHex = code; }); SaveLaneColor(code); }
            };
            float y = -(head + 70f);
            Sprite sq = null;
            Slider sH = null, sS = null, sV = null;
            sH = ColorSlider(box.transform, y, HueSprite(), Color.white, hsv[0], v => { hsv[0] = v; refresh(false); }, () => refresh(true));
            y -= 44f;
            sS = ColorSlider(box.transform, y, sq, new Color(0.55f, 0.55f, 0.6f), hsv[1], v => { hsv[1] = v; refresh(false); }, () => refresh(true));
            y -= 44f;
            sV = ColorSlider(box.transform, y, sq, new Color(0.3f, 0.3f, 0.33f), hsv[2], v => { hsv[2] = v; refresh(false); }, () => refresh(true));
            y -= 54f;
            var lab = UIKit.Label(box.transform, "Tono · Saturación · Brillo", 16, TextAnchor.MiddleCenter, new Color(0.8f, 0.85f, 0.9f));
            Place(lab.rectTransform, 0, 1, 1, 1, 16, y, 16, -(y + 22f));

            // Rejilla: A, colores del tema y colores guardados.
            var grid = UIKit.Rect("Swatches", box.transform);
            Place(grid, 0, 1, 1, 1, 16, y - rows * cs - 4f, 16, -(y - 4f));
            UIKit.Grid(grid, new Vector2(cs, cs), 0, 0, cols);
            Action<string, Color, Action, bool> cell = (label, col, act, on) =>
            {
                var c = UIKit.Panel(grid, "Cell", new Color(0, 0, 0, 0));
                var b = c.gameObject.AddComponent<Button>();
                b.targetGraphic = c;
                b.onClick.AddListener(() => { AudioManager.Instance.PlayClick(); act(); selPanelKey = "~"; DestroyRoot(ref optRoot); });
                var ring = UIKit.Panel(c.transform, "Ring", on ? UIKit.Orange : new Color(0.2f, 0.2f, 0.22f), false);
                ring.sprite = IconKit.Circle;
                ring.rectTransform.anchorMin = ring.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                ring.rectTransform.sizeDelta = new Vector2(cs - 8f, cs - 8f);
                var dot = UIKit.Panel(ring.transform, "Dot", col, false);
                dot.sprite = IconKit.Circle;
                dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                dot.rectTransform.sizeDelta = new Vector2(cs - 18f, cs - 18f);
                if (!string.IsNullOrEmpty(label))
                {
                    var t = UIKit.Label(dot.transform, label, 20, TextAnchor.MiddleCenter, new Color(0.15f, 0.15f, 0.15f));
                    UIKit.Stretch(t.rectTransform);
                }
            };
            bool custom = RoadData.ValidHex(r.colorHex);
            cell("A", new Color(0.85f, 0.85f, 0.85f), () => ed.ApplyLaneProp(x => { x.color = 0; x.colorHex = ""; }), !custom && r.color == 0);
            for (int i = 1; i < MapRenderer.RoadColors.Length; i++)
            {
                int idx = i;
                cell("", SpriteFactory.Hex(MapRenderer.RoadColors[i]), () => ed.ApplyLaneProp(x => { x.color = idx; x.colorHex = ""; }), !custom && r.color == i);
            }
            for (int i = 0; i < 12; i++)
            {
                if (i < saved.Count)
                {
                    string hx = saved[i];
                    cell("", SpriteFactory.Hex(hx), () => ed.ApplyLaneProp(x => { x.colorHex = hx; }), custom && r.colorHex.ToUpperInvariant() == hx.ToUpperInvariant());
                }
                else cell("", Color.white, () => { }, false);   // hueco libre: se llena al elegir un color con las barras
            }
        }

        static readonly Color LineWhite = new Color(0.95f, 0.95f, 0.95f, 1f);
        static readonly Color LineYellow = new Color(0.95f, 0.78f, 0.2f, 1f);

        static List<Opt> LineOptions()
        {
            return new List<Opt>
            {
                O("ln_none", "Sin línea"), O("ln_solid", "Blanca", LineWhite), O("ln_solid", "Amarilla", LineYellow),
                O("ln_dash", "Blanca\ndiscontinua", LineWhite), O("ln_dash", "Amarilla\ndiscontinua", LineYellow), O("ln_double", "Doble\namarilla", LineYellow)
            };
        }

        static readonly string[] SameIcons = { "ln_none", "ln_dash", "ln_solid", "ln_dash", "ln_solid" };
        static readonly Color[] SameTints = { Color.white, Color.white, Color.white, new Color(0.95f, 0.78f, 0.2f, 1f), new Color(0.95f, 0.78f, 0.2f, 1f) };
        static readonly string[] OppIcons = { "ln_none", "ln_solid", "ln_double", "ln_solid", "ln_dash", "ln_dash" };
        static readonly Color[] OppTints = { Color.white, new Color(0.95f, 0.78f, 0.2f, 1f), new Color(0.95f, 0.78f, 0.2f, 1f), Color.white, new Color(0.95f, 0.78f, 0.2f, 1f), Color.white };
        static readonly string[] EdgeIcons = { "ln_none", "ln_solid", "ln_solid" };

        static List<Opt> SameOptions()
        {
            return new List<Opt> { O("ln_auto", "Automática"), O("ln_none", "Sin línea"), O("ln_dash", "Discontinua", LineWhite), O("ln_solid", "Continua", LineWhite),
                O("ln_dash", "Amarilla\ndiscontinua", LineYellow), O("ln_solid", "Amarilla\ncontinua", LineYellow) };
        }

        static List<Opt> OppOptions()
        {
            return new List<Opt>
            {
                O("ln_auto", "Automática"), O("ln_none", "Sin línea"), O("ln_solid", "Amarilla", LineYellow), O("ln_double", "Doble\namarilla", LineYellow),
                O("ln_solid", "Blanca", LineWhite), O("ln_dash", "Amarilla\ndiscontinua", LineYellow), O("ln_dash", "Blanca\ndiscontinua", LineWhite)
            };
        }

        static List<Opt> EdgeOptions()
        {
            return new List<Opt> { O("ln_auto", "Automática"), O("ln_none", "Sin línea"), O("ln_solid", "Blanca", LineWhite), O("ln_solid", "Amarilla", LineYellow) };
        }

        static string LineIcon(int style) { return style == 0 ? "edgeL" : (style == 5 ? "ln_double" : (style >= 3 ? "ln_dash" : "ln_solid")); }
        static Color LineTint(int style) { return style == 2 || style == 4 || style == 5 ? LineYellow : Color.white; }

        /// <summary>Casillas comunes de Propiedades y Segmento: KM/H, líneas, color, cables y barandillado (con sus ventanitas).</summary>
        void LanePropCells(RoadData r, Action afterSpeed = null)
        {
            var ed = game.editor;
            PanelNumber(MapEditorController.SpeedLabel(r.speedKmh), MapEditorController.SpeedUnit.ToUpperInvariant(), r.speedKmh > 0, () =>
            {
                var opts = new List<Opt>();
                int cur = 0;
                for (int i = 0; i < RoadData.SpeedSteps.Length; i++)
                {
                    int v = RoadData.SpeedSteps[i];
                    opts.Add(O(null, v == 0 ? "Normal" : "KM/H", null, null, v == 0 ? "50" : v.ToString()));
                    if (v == r.speedKmh) cur = i;
                }
                ShowOptions("Velocidad", opts, cur, i => ed.ApplyLaneProp(x => x.speedKmh = RoadData.SpeedSteps[i]));
            });
            if (afterSpeed != null) afterSpeed();
            // Tres líneas como el original (cada una con «A» = automática); elegir una pasa el carril a líneas automáticas.
            PanelIconTint(r.lineSame < 0 ? "ln_auto" : SameIcons[r.lineSame], "Mismo\nsentido", r.lineSame >= 0, r.lineSame < 0 ? Color.white : SameTints[r.lineSame],
                () => ShowOptions("Entre carriles del mismo sentido", SameOptions(), r.lineSame + 1, i => ed.ApplyLaneProp(x => { x.lineSame = i - 1; x.lineL = x.lineR = 0; x.noMarks = false; })));
            PanelIconTint(r.lineOpp < 0 ? "ln_auto" : OppIcons[r.lineOpp], "Doble\nsentido", r.lineOpp >= 0, r.lineOpp < 0 ? Color.white : OppTints[r.lineOpp],
                () => ShowOptions("Entre carriles de sentido contrario", OppOptions(), r.lineOpp + 1, i => ed.ApplyLaneProp(x => { x.lineOpp = i - 1; x.lineL = x.lineR = 0; x.noMarks = false; })));
            PanelIconTint(r.lineEdge < 0 ? "ln_auto" : EdgeIcons[r.lineEdge], "Bordes", r.lineEdge >= 0, r.lineEdge == 2 ? LineYellow : Color.white,
                () => ShowOptions("Líneas de los bordes", EdgeOptions(), r.lineEdge + 1, i => ed.ApplyLaneProp(x => { x.lineEdge = i - 1; x.lineL = x.lineR = 0; x.noMarks = false; })));
            Color laneSw = RoadData.ValidHex(r.colorHex) ? SpriteFactory.Hex(r.colorHex)
                : (r.color > 0 && r.color < MapRenderer.RoadColors.Length ? SpriteFactory.Hex(MapRenderer.RoadColors[r.color]) : Color.white);
            PanelIconTint("swatch", "Color", r.color != 0 || RoadData.ValidHex(r.colorHex), laneSw, () => ShowLaneColorPicker(r));
            int cabCur = !r.cables ? 0 : (r.cableSide == 1 ? 2 : 1);
            PanelIcon("cables", "Cables sobre\nsegmento", r.cables, () => ShowOptions("Cables", new List<Opt>
            {
                O("ar_none", "Sin cables"), O("cables", "A la\nderecha"), O("cables", "A la\nizquierda")
            }, cabCur, i => ed.ApplyLaneProp(x => { x.cables = i != 0; x.cableSide = i == 2 ? 1 : 0; })));
            string[] railHex = { "#B8C2D1", "#F2C230", "#F4F4F4", "#3A3D44" };
            string[] railNames = { "Acero", "Amarillo", "Blanco", "Oscuro" };
            PanelIcon("rails", "Barandillado", r.rails, () =>
            {
                var opts = new List<Opt> { O("ar_none", "Sin\nbarandilla") };
                for (int i = 0; i < railHex.Length; i++) opts.Add(O("rails", railNames[i], SpriteFactory.Hex(railHex[i])));
                ShowOptions("Barandillado", opts, r.rails ? r.railColor + 1 : 0, i => ed.ApplyLaneProp(x => { x.rails = i != 0; if (i > 0) x.railColor = i - 1; }));
            });
            if (r.laneType == RoadData.LaneShoulder)
            {
                // Berma: ancho ajustable; siempre queda pegada a la carretera y crece hacia afuera.
                PanelIcon("minus", "Ancho", false, () => ed.ApplyLaneProp(x => x.laneWidth = Mathf.Max(0.15f, Mathf.Round((x.Width - 0.05f) * 100f) / 100f)));
                PanelIcon("plus", (r.Width * MapEditorController.MetersPerUnit).ToString("0.0") + " m\nAncho", r.laneWidth > 0.05f, () => ed.ApplyLaneProp(x => x.laneWidth = Mathf.Min(1.5f, Mathf.Round((x.Width + 0.05f) * 100f) / 100f)));
            }
            // Como el original: «Evitar» (los vehículos no lo usan si tienen otro camino).
            PanelIcon("avoid", "Evitar", r.avoid, () => ed.ApplyLaneProp(x => x.avoid = !x.avoid));
            if (r.laneType == RoadData.LaneVehicle || r.laneType == RoadData.LaneBike)
            {
                // Como el original: texto libre pintado en el carril, con su posición y color.
                UIKit.Input(selBody, string.IsNullOrEmpty(r.roadText) ? "Texto en el carril" : r.roadText, 20, str =>
                {
                    string txt = (str ?? "").Trim();
                    if (txt == "Texto en el carril") return;
                    ed.ApplyLaneProp(x => x.roadText = txt);
                });
                selCount++;
                if (!string.IsNullOrEmpty(r.roadText) || r.markKind > 0)
                {
                    float pos = r.markT >= 0f ? r.markT : 0.5f;
                    PanelIcon("minus", "Posición", false, () => ed.ApplyLaneProp(x => x.markT = Mathf.Max(0f, Mathf.Round(((x.markT >= 0f ? x.markT : 0.5f) - 0.05f) * 20f) / 20f)));
                    PanelIcon("plus", Mathf.RoundToInt(pos * 100f) + "%\nPosición", r.markT >= 0f, () => ed.ApplyLaneProp(x => x.markT = Mathf.Min(1f, Mathf.Round(((x.markT >= 0f ? x.markT : 0.5f) + 0.05f) * 20f) / 20f)));
                    PanelIcon("color", r.markColor == 1 ? "Amarilla" : "Blanca", r.markColor == 1, () => ed.ApplyLaneProp(x => x.markColor = x.markColor == 1 ? 0 : 1), new Color(0.95f, 0.78f, 0.2f, 1f));
                }
            }
        }

        static readonly string[] CrossIcons = { "cw_none", "cw_solid", "cw_lines", "cw_zebra", "cw_diag", "cw_ladder" };
        static readonly string[] CrossNames = { "Sin marcas", "Bloque", "Dos líneas", "Cebra", "Diagonal", "Escalera" };

        // ---------------- Decoración con las opciones del original ----------------

        void PickNames(string title, string[] names, int cur, Action<int> pick, Func<int, Color?> circle = null, string icon = "none", Func<int, Color> tint = null)
        {
            var opts = new List<Opt>();
            for (int i = 0; i < names.Length; i++)
                opts.Add(O(icon, names[i], tint != null ? tint(i) : (Color?)null, circle != null ? circle(i) : null, icon == "none" ? (i + 1).ToString() : null));
            ShowOptions(title, opts, Mathf.Clamp(cur, 0, names.Length - 1), pick);
        }

        void PickPalette(string title, string[] names, string[] hex, int cur, Action<int> pick, bool firstIsRandom = false, Action<string> custom = null, string curHex = null)
        {
            var opts = new List<Opt>();
            for (int i = 0; i < names.Length; i++)
            {
                int hi = firstIsRandom ? i - 1 : i;
                Color? col = hi >= 0 ? SpriteFactory.Hex(hex[hi % hex.Length]) : (Color?)null;
                opts.Add(O("none", names[i], null, col, hi < 0 ? "?" : null));
            }
            bool hasCustom = RoadData.ValidHex(curHex);
            if (custom != null) opts.Add(O("color", "Otro color…", null, hasCustom ? SpriteFactory.Hex(curHex) : (Color?)null));
            ShowOptions(title, opts, hasCustom ? names.Length : Mathf.Clamp(cur, 0, names.Length - 1), i =>
            {
                if (custom != null && i == names.Length)
                {
                    Color start = hasCustom ? SpriteFactory.Hex(curHex) : (cur >= 0 && cur < hex.Length ? SpriteFactory.Hex(hex[Mathf.Max(0, firstIsRandom ? cur - 1 : cur) % hex.Length]) : Color.gray);
                    ShowHexPicker(title, start, custom);
                    return;
                }
                pick(i);
            });
        }

        /// <summary>Propiedades del original para edificio, aparcamiento, coche aparcado, valla, material, pegatina y árbol.</summary>
        bool DecorPropsIC2(DecorItem dd)
        {
            var ed = game.editor;
            switch (dd.type)
            {
                case DecorType.Zone:
                case DecorType.Trail:
                    {
                        bool zone = dd.type == DecorType.Zone;
                        string[] names = zone ? MapRenderer.ZoneNames : MapRenderer.TrailNames;
                        string[] hex = zone ? MapRenderer.ZoneHex : MapRenderer.TrailHex;
                        PanelIcon(zone ? "zone" : "trail", "Tipo:\n" + names[Mathf.Abs(dd.variant) % names.Length], false,
                            () => PickPalette("Tipo", names, hex, dd.variant, i => ed.ApplyDecor(x => { x.variant = i; x.hexA = ""; }), false, hx => ed.ApplyDecor(x => x.hexA = hx), dd.hexA));
                        PanelNumber(dd.pts.Count.ToString(), "Puntos", false, () => { });
                        if (ed.PolyAdding) PanelIcon("check", "Listo", true, () => { ed.FinishPoly(); selPanelKey = "~"; });
                        else PanelIcon("plus", "Añadir\npuntos", false, () => { ed.ContinuePoly(); selPanelKey = "~"; });
                        PanelIcon("undo", "Quitar\núltimo", false, () => ed.RemoveLastPolyPoint());
                        PanelIcon("check", "Bajo carril", !dd.overLane, () => ed.ApplyDecor(x => x.overLane = !x.overLane));
                        if (!zone) PanelIcon("check", "Sombras", !dd.noShadow, () => ed.ApplyDecor(x => x.noShadow = !x.noShadow));
                        PanelIcon("minus", "Transparencia", false, () => ed.ApplyDecor(x => x.transparency = Mathf.Max(0f, Mathf.Round(x.transparency * 10f - 1f) / 10f)));
                        PanelIcon("plus", Mathf.RoundToInt(dd.transparency * 100f) + "%\nTransparencia", dd.transparency > 0f, () => ed.ApplyDecor(x => x.transparency = Mathf.Min(0.9f, Mathf.Round(x.transparency * 10f + 1f) / 10f)));
                        return true;
                    }
                case DecorType.PhonePole:
                    PanelIcon("powerline", "Cables al\nsiguiente poste", (dd.carFlags & 1) != 0, () => ed.ApplyDecor(x => x.carFlags ^= 1));
                    return true;
                case DecorType.RailSign:
                    // Paso a nivel: sus luces parpadean cuando un tren está a menos de este alcance.
                    PanelIcon("minus", "Alcance", false, () => ed.ApplyDecor(x => x.light = Mathf.Max(0.3f, Mathf.Round((x.light - 0.25f) * 4f) / 4f)));
                    PanelIcon("plus", Mathf.RoundToInt((4f + 2f * dd.light) * MapEditorController.MetersPerUnit) + " m\nAlcance", false, () => ed.ApplyDecor(x => x.light = Mathf.Min(3f, Mathf.Round((x.light + 0.25f) * 4f) / 4f)));
                    return true;
                case DecorType.WallSticker:
                    PanelIcon("wallsticker", "Tipo:\n" + MapRenderer.WallStickerNames[Mathf.Abs(dd.variant) % MapRenderer.WallStickerNames.Length], false,
                        () => PickNames("Tipo", MapRenderer.WallStickerNames, dd.variant, i => ed.ApplyDecor(x => x.variant = i)));
                    PanelIcon("minus", "Piso", false, () => ed.ApplyDecor(x => x.height = Mathf.Max(1, x.height - 1)));
                    PanelNumber(dd.height.ToString(), "Piso +", dd.height > 1, () => ed.ApplyDecor(x => x.height = Mathf.Min(10, x.height + 1)));
                    PanelIcon("color", "Color", dd.color != 0 || RoadData.ValidHex(dd.hexA), () => PickPalette("Color", MapRenderer.ObjColorNames, MapRenderer.ObjColors, dd.color, i => ed.ApplyDecor(x => { x.color = i; x.hexA = ""; }), false, hx => ed.ApplyDecor(x => x.hexA = hx), dd.hexA));
                    return true;
                case DecorType.Building:
                    PanelIcon("minus", "Altura", false, () => ed.ApplyDecor(x => x.height = Mathf.Max(1, x.height - 1)));
                    PanelNumber(dd.height.ToString(), "Altura +", dd.height > 1, () => ed.ApplyDecor(x => x.height = Mathf.Min(10, x.height + 1)));
                    PanelIcon("check", "Sombras", !dd.noShadow, () => ed.ApplyDecor(x => x.noShadow = !x.noShadow));
                    PanelIcon("check", "Reducir\naltura", dd.lowHeight, () => ed.ApplyDecor(x => x.lowHeight = !x.lowHeight));
                    PanelIcon("building", "Tejado:\n" + MapRenderer.RoofNames[dd.roof % MapRenderer.RoofNames.Length], false,
                        () => PickNames("Tejado", MapRenderer.RoofNames, dd.roof, i => ed.ApplyDecor(x => x.roof = i)));
                    PanelIcon("color", "Color\ntejado", false,
                        () => PickPalette("Color del tejado", MapRenderer.RoofColorNames, RoofHex(), dd.roofColor, i => ed.ApplyDecor(x => { x.roofColor = i; x.hexB = ""; }), false, hx => ed.ApplyDecor(x => x.hexB = hx), dd.hexB));
                    PanelIcon("check", "Opciones\ndel techo", dd.roofItems, () => ed.ApplyDecor(x => x.roofItems = !x.roofItems));
                    PanelIcon("building", "Paredes:\n" + MapRenderer.WallNames[dd.wall % MapRenderer.WallNames.Length], false,
                        () => PickNames("Paredes", MapRenderer.WallNames, dd.wall, i => ed.ApplyDecor(x => x.wall = i)));
                    PanelIcon("color", "Color\nparedes", false,
                        () => PickPalette("Color de las paredes", MapRenderer.WallColorNames, WallHex(), dd.color, i => ed.ApplyDecor(x => { x.color = i; x.hexA = ""; }), false, hx => ed.ApplyDecor(x => x.hexA = hx), dd.hexA));
                    return true;
                case DecorType.Parking:
                    PanelIcon("minus", "Uso", false, () => ed.ApplyDecor(x => x.usage = Mathf.Max(0f, Mathf.Round(x.usage * 10f - 1f) / 10f)));
                    PanelIcon("plus", Mathf.RoundToInt(dd.usage * 100f) + "%\nUso", false, () => ed.ApplyDecor(x => x.usage = Mathf.Min(1f, Mathf.Round(x.usage * 10f + 1f) / 10f)));
                    PanelIcon("minus", "Espacios", false, () => ed.ApplyDecor(x => x.spaces = Mathf.Max(1, x.spaces - 1)));
                    PanelNumber(dd.spaces.ToString(), "Espacios +", false, () => ed.ApplyDecor(x => x.spaces = Mathf.Min(30, x.spaces + 1)));
                    PanelIcon("check", "Doble\nsentido", dd.twoWay, () => ed.ApplyDecor(x => x.twoWay = !x.twoWay));
                    PanelIcon("eye", "Visible", !dd.invisible, () => ed.ApplyDecor(x => x.invisible = !x.invisible));
                    PanelIcon("color", "Color", dd.color != 0 || RoadData.ValidHex(dd.hexA), () => PickPalette("Color de las rayas", MapRenderer.ObjColorNames, MapRenderer.ObjColors, dd.color, i => ed.ApplyDecor(x => { x.color = i; x.hexA = ""; }), false, hx => ed.ApplyDecor(x => x.hexA = hx), dd.hexA));
                    PanelIcon("parking", "Tipo:\n" + MapRenderer.ParkingNames[Mathf.Abs(dd.variant) % MapRenderer.ParkingNames.Length], false,
                        () => PickNames("Tipo", MapRenderer.ParkingNames, dd.variant, i => ed.ApplyDecor(x => x.variant = i)));
                    return true;
                case DecorType.ParkedCar:
                    PanelIcon("minus", "Uso", false, () => ed.ApplyDecor(x => x.presence = Mathf.Max(0f, Mathf.Round(x.presence * 10f - 1f) / 10f)));
                    PanelIcon("plus", Mathf.RoundToInt(dd.presence * 100f) + "%\nUso", false, () => ed.ApplyDecor(x => x.presence = Mathf.Min(1f, Mathf.Round(x.presence * 10f + 1f) / 10f)));
                    PanelIcon("parked", MapRenderer.CarModelNames[Mathf.Abs(dd.variant) % MapRenderer.CarModelNames.Length], dd.variant != 0,
                        () => PickNames("Modelo", MapRenderer.CarModelNames, dd.variant, i => ed.ApplyDecor(x => x.variant = i)));
                    PanelIcon("lamp", "Luces", (dd.carFlags & 1) != 0, () => ed.ApplyDecor(x => x.carFlags ^= 1));
                    PanelIcon("info", "Emergencia", (dd.carFlags & 2) != 0, () => ed.ApplyDecor(x => x.carFlags ^= 2));
                    PanelIcon("undo", "Intermitente\nizq.", (dd.carFlags & 4) != 0, () => ed.ApplyDecor(x => x.carFlags ^= 4));
                    PanelIcon("redo", "Intermitente\nder.", (dd.carFlags & 8) != 0, () => ed.ApplyDecor(x => x.carFlags ^= 8));
                    PanelIcon("color", "Color A", dd.color != 0, () => PickPalette("Color A", MapRenderer.CarColorNames, MapRenderer.CarColorHex, dd.color, i => ed.ApplyDecor(x => { x.color = i; x.hexA = ""; }), true, hx => ed.ApplyDecor(x => x.hexA = hx), dd.hexA));
                    PanelIcon("color", "Color B", dd.color2 != 0, () => PickPalette("Color B", MapRenderer.CarColorNames, MapRenderer.CarColorHex, dd.color2, i => ed.ApplyDecor(x => { x.color2 = i; x.hexB = ""; }), true, hx => ed.ApplyDecor(x => x.hexB = hx), dd.hexB));
                    return true;
                case DecorType.Fence:
                    PanelIconTint("fence", "Tipo:\n" + MapRenderer.FenceNames[Mathf.Abs(dd.variant) % MapRenderer.FenceNames.Length], true, MapRenderer.FenceTints[Mathf.Abs(dd.variant) % MapRenderer.FenceTints.Length],
                        () => PickNames("Tipo", MapRenderer.FenceNames, dd.variant, i => ed.ApplyDecor(x => x.variant = i), null, "fence", i => MapRenderer.FenceTints[i]));
                    return true;
                case DecorType.Material:
                    PanelIcon("material", "Color:\n" + MapRenderer.MaterialNames[Mathf.Abs(dd.variant) % MapRenderer.MaterialNames.Length], false,
                        () => PickPalette("Material", MapRenderer.MaterialNames, MaterialHex(), dd.variant, i => ed.ApplyDecor(x => { x.variant = i; x.hexA = ""; }), false, hx => ed.ApplyDecor(x => x.hexA = hx), dd.hexA));
                    PanelIcon("color", "Marcas", dd.color2 != 0, () => PickPalette("Color de las marcas", MapRenderer.ObjColorNames, MapRenderer.ObjColors, dd.color2, i => ed.ApplyDecor(x => x.color2 = i)));
                    PanelNumber(MapRenderer.BarNames[dd.bars % 4] == "Sin barras" ? "–" : MapRenderer.BarNames[dd.bars % 4], "Barras", dd.bars != 0,
                        () => PickNames("Barras", MapRenderer.BarNames, dd.bars, i => ed.ApplyDecor(x => x.bars = i)));
                    if (dd.bars > 0 && dd.pts.Count < 3)
                        PanelIcon("redo", "Rayas\n" + Mathf.RoundToInt(Mathf.Repeat(dd.rot, 180f)) + "°", false, () => ed.ApplyDecor(x => x.rot = Mathf.Repeat(x.rot + 45f, 180f)));
                    PanelIcon("check", "Bajo carril", !dd.overLane, () => ed.ApplyDecor(x => x.overLane = !x.overLane));
                    PanelIcon("minus", "Transparencia", false, () => ed.ApplyDecor(x => x.transparency = Mathf.Max(0f, Mathf.Round(x.transparency * 10f - 1f) / 10f)));
                    PanelIcon("plus", Mathf.RoundToInt(dd.transparency * 100f) + "%\nTransparencia", dd.transparency > 0f, () => ed.ApplyDecor(x => x.transparency = Mathf.Min(0.9f, Mathf.Round(x.transparency * 10f + 1f) / 10f)));
                    return true;
                case DecorType.Sticker:
                    {
                        var names = new List<string>(MapRenderer.Sticker2Names);
                        names.AddRange(MapRenderer.StickerNames);   // las 6 antiguas al final
                        int cur = dd.variant >= 6 ? dd.variant - 6 : MapRenderer.Sticker2Names.Length + dd.variant;
                        PanelIcon("sticker", "Tipo:\n" + names[Mathf.Clamp(cur, 0, names.Count - 1)], false,
                            () => PickNames("Pegatina", names.ToArray(), cur, i => ed.ApplyDecor(x => x.variant = i < MapRenderer.Sticker2Names.Length ? i + 6 : i - MapRenderer.Sticker2Names.Length)));
                        PanelIcon("color", "Color", dd.color != 0 || RoadData.ValidHex(dd.hexA), () => PickPalette("Color", MapRenderer.ObjColorNames, MapRenderer.ObjColors, dd.color, i => ed.ApplyDecor(x => { x.color = i; x.hexA = ""; }), false, hx => ed.ApplyDecor(x => x.hexA = hx), dd.hexA));
                        return true;
                    }
                case DecorType.GlowLight:
                    PanelIcon("color", "Color", dd.color != 0, () => ed.CycleDecorColor());
                    PanelIcon("glow", "Destello", dd.flashPeriod > 0.05f, () => ed.ApplyDecor(x => x.flashPeriod = x.flashPeriod > 0.05f ? 0f : 1f));
                    if (dd.flashPeriod > 0.05f)
                    {
                        PanelIcon("minus", "Tiempo del\ndestello", false, () => ed.ApplyDecor(x => x.flashPeriod = Mathf.Max(0.2f, Mathf.Round(x.flashPeriod * 10f - 1f) / 10f)));
                        PanelIcon("plus", dd.flashPeriod.ToString("0.0") + " s", false, () => ed.ApplyDecor(x => x.flashPeriod = Mathf.Min(5f, Mathf.Round(x.flashPeriod * 10f + 1f) / 10f)));
                    }
                    return true;
                case DecorType.Tree:
                    PanelIcon("tree", "Tipo:\n" + MapRenderer.TreeNames[Mathf.Abs(dd.variant) % 4], false,
                        () => PickNames("Tipo", MapRenderer.TreeNames, dd.variant % 4, i => ed.ApplyDecor(x => x.variant = i), null, "tree", i => TreeTint(i)));
                    return true;
            }
            return false;
        }

        static readonly string[] RoadTextPresets = { "Ninguno", "BUS", "SOLO BUS", "TAXI", "LENTO", "PARE", "CEDA", "ESCUELA", "DESPACIO", "BICI", "CARGA", "SOLO" };

        static Color TreeTint(int i) { return i == 1 ? new Color(0.95f, 0.75f, 0.2f) : (i == 3 ? new Color(0.2f, 0.45f, 0.3f) : new Color(0.45f, 0.75f, 0.35f)); }
        static string[] RoofHex() { return new[] { "#8E9196", "#B4553C", "#4A4C52", "#E4E4E0", "#5E8A5B", "#4F6E96", "#7A5638", "#CDB98F" }; }
        static string[] WallHex() { return new[] { "#D8C3A5", "#B5654A", "#A9A9A6", "#EDEBE6", "#8FA6C2", "#9DB59A", "#D2A85C", "#5E5F66" }; }
        static string[] MaterialHex() { return new[] { "#BDBDB5", "#E3D3A0", "#5DA9E9", "#9C7B54", "#8F8F8F", "#6E9A55", "#8E8E8E" }; }

        void ShowCrossStyles(RoadData road)
        {
            var ed = game.editor;
            var opts = new List<Opt>();
            for (int i = 0; i < CrossIcons.Length; i++) opts.Add(O(CrossIcons[i], CrossNames[i]));
            ShowOptions("Paso peatonal", opts, Mathf.Clamp(road.crossStyle, 0, CrossIcons.Length - 1), i => ed.ApplyLaneProp(x => x.crossStyle = i));
        }

        static readonly string[] ArrowIcons = { "ar_none", "ar_s", "ar_l", "ar_r", "ar_sl", "ar_sr", "ar_lr", "ar_lsr", "ar_cl", "ar_cr" };

        /// <summary>Flechas del carril seleccionado: dibujo, posición y color (como la ventanita de la referencia).</summary>
        void ShowArrowOptions()
        {
            var ed = game.editor;
            var opts = new List<Opt>();
            for (int i = 0; i < ArrowIcons.Length; i++) opts.Add(O(ArrowIcons[i], MapEditorController.ArrowCycleNames[i].Replace(" y ", "\ny ").Replace(" e ", "\ne ")));
            int[] moves = { 0, 2, 1, 4, 3, 6, 5, 7, 1, 4 };
            var ar = ed.SelectedLaneArrow;
            ShowOptions("Flechas", opts, ed.SelectedArrowIndex(), i => { ed.SetSelectedArrow(moves[i], i >= 8 ? 1 : 0); ShowArrowOptions(); }, ex =>
            {
                UIKit.HLayout(ex, 6, 0);
                var minus = UIKit.Button(ex, "−", UIKit.Neutral, () => { ed.ShiftSelectedArrowPos(-0.05f); ShowArrowOptions(); }, 30);
                UIKit.Size(minus, 56, 64);
                var lbl = UIKit.Label(ex, ar != null ? Mathf.RoundToInt(ar.t * 100f) + "%\nPosición" : "Posición", 18, TextAnchor.MiddleCenter, Color.white);
                UIKit.Size(lbl, 96, 64);
                var plus = UIKit.Button(ex, "+", UIKit.Neutral, () => { ed.ShiftSelectedArrowPos(0.05f); ShowArrowOptions(); }, 30);
                UIKit.Size(plus, 56, 64);
                var wb = UIKit.Button(ex, "", LineWhite, () => { ed.SetSelectedArrowColor(0); ShowArrowOptions(); }, 20);
                UIKit.Size(wb, 56, 56);
                var yb = UIKit.Button(ex, "", LineYellow, () => { ed.SetSelectedArrowColor(1); ShowArrowOptions(); }, 20);
                UIKit.Size(yb, 56, 56);
                if (ar != null) UIKit.SetText(ar.color == 0 ? wb : yb, "✓");
            }, true);
        }

        // Grupos de «Tráfico permitido» como el original: Coche, Camión, Autobús y Tranvía.
        static readonly int[] AllowGroupMasks =
        {
            (1 << (int)VehicleType.Car) | (1 << (int)VehicleType.Taxi) | (1 << (int)VehicleType.Motorbike) | (1 << (int)VehicleType.Van) | (1 << (int)VehicleType.Ambulance) | (1 << (int)VehicleType.Police),
            (1 << (int)VehicleType.Truck) | (1 << (int)VehicleType.FireTruck),
            (1 << (int)VehicleType.Bus)
        };

        static bool GroupOn(RoadData r, int g)
        {
            if (g == 3) return r.tram;
            int m = r.allow == 0 ? RoadData.AllVehicles : r.allow;
            return (m & AllowGroupMasks[g]) != 0;
        }

        void ShowAllowGroups()
        {
            var ed = game.editor;
            var r = ed.PropTarget;
            if (r == null) return;
            var opts = new List<Opt> { O("vehicle", "Coche"), O("truck", "Camión"), O("bus", "Autobús"), O("tram", "Tranvía") };
            ShowOptions("Tráfico permitido", opts, -1, g =>
            {
                ed.ApplyLaneProp(x =>
                {
                    if (g == 3) { x.tram = !x.tram; }
                    else
                    {
                        int m = x.allow == 0 ? RoadData.AllVehicles : (x.allow & RoadData.AllVehicles);
                        m ^= AllowGroupMasks[g];
                        x.allow = m == RoadData.AllVehicles ? 0 : m;
                    }
                    // Sin ningún vehículo de carretera: solo tranvía (o nadie si tampoco hay tranvía; se deja como estaba).
                    if (x.allow == 0 && !GroupOn(x, 0) && !GroupOn(x, 1) && !GroupOn(x, 2)) x.allow = 0;
                    if ((x.allow & RoadData.AllVehicles) == 0 && x.allow != 0) x.allow = 1 << (int)VehicleType.Tram;
                    x.allowAB = new int[0]; x.allowBA = new int[0];
                });
                ShowAllowGroups();
            }, null, true);
            // Marcar los activados.
            if (optRoot != null)
            {
                var grid = optRoot.Find("Box/Grid");
                if (grid != null)
                    for (int g = 0; g < 4 && g < grid.childCount; g++)
                    {
                        var circ = grid.GetChild(g).Find("Circle");
                        if (circ != null) circ.GetComponent<Image>().color = GroupOn(r, g) ? UIKit.Orange : CircleGray;
                    }
            }
        }

        void ShowAllowPresetOptions()
        {
            var ed = game.editor;
            var opts = new List<Opt>();
            for (int i = 0; i < RoadData.AllowLabels.Length; i++) opts.Add(O("vehicle", RoadData.AllowLabels[i].Replace(" ", "\n")));
            ShowOptions("Tráfico permitido", opts, ed.NewLaneProps.AllowPreset, i =>
            {
                var np = ed.NewLaneProps;
                np.allow = 0;
                for (int k = 0; k < i; k++) np.NextAllowPreset();
            });
        }

        /// <summary>Casilla con icono teñido (por ejemplo una línea amarilla).</summary>
        void PanelIconTint(string icon, string label, bool active, Color tint, Action act)
        {
            Image circ;
            IconCell(selBody, icon, label, () => { act(); selPanelKey = "~"; }, out circ, 15);
            circ.color = active ? UIKit.Orange : CircleGray;
            var g = circ.transform.Find("Glyph");
            if (g != null) g.GetComponent<Image>().color = tint;
            selCount++;
        }

        static string DecorIconOf(DecorType t)
        {
            switch (t)
            {
                case DecorType.Tree: return "tree";
                case DecorType.Building: return "building";
                case DecorType.Material: return "material";
                case DecorType.Parking: return "parking";
                case DecorType.ParkedCar: return "parked";
                case DecorType.Trailer: return "trailer";
                case DecorType.Boat: return "boat";
                case DecorType.Fence: return "fence";
                case DecorType.Lamp: return "lamp";
                case DecorType.FloodLight: return "flood";
                case DecorType.Smoke: return "smoke";
                case DecorType.GlowLight: return "glow";
                case DecorType.RailSign: return "railsign";
                case DecorType.PhonePole: return "phone";
                case DecorType.RoadSign: return "roadsign";
                case DecorType.TrafficSign: return "roadsign";
                case DecorType.RoundStructure: return "tank";
                case DecorType.BusShelter: return "shelter";
                case DecorType.Containers: return "containers";
                case DecorType.PowerLine: return "powerline";
                case DecorType.Crane: return "crane";
                case DecorType.Sticker: return "sticker";
                case DecorType.TextLabel: return "text";
                case DecorType.Lines: return "lines";
                case DecorType.Zone: return "zone";
                case DecorType.Trail: return "trail";
                case DecorType.WallSticker: return "wallsticker";
                default: return "misc";
            }
        }

        RectTransform layerRoot;

        /// <summary>Selector de capa como el de la referencia: rejilla 4 3 2 / 1 0 -1 / -2 -3 -4. forRoad = capa del carril seleccionado, o -999 para la capa de dibujo.</summary>
        void ShowLayerPicker(int forRoad)
        {
            var ed = game.editor;
            DestroyRoot(ref layerRoot);
            CloseMenus(true);
            layerRoot = NewRoot("LayerPicker", new Color(0, 0, 0, 0.01f));
            var close = layerRoot.gameObject.AddComponent<Button>();
            close.onClick.AddListener(() => DestroyRoot(ref layerRoot));
            var box = UIKit.Panel(layerRoot, "Box", PanelDark);
            bool editingRoad = forRoad != -999;
            int current = editingRoad ? forRoad : ed.CurrentLayer;
            Corner(box.rectTransform, 0, 1, 3 * 78 + 16, 3 * 78 + 16, 100, -(4 * 90f + 8 + 60));
            UIKit.Grid(box.rectTransform, new Vector2(78, 78), 0, 8, 3);
            int[] order = { 4, 3, 2, 1, 0, -1, -2, -3, -4 };
            foreach (int v in order)
            {
                int val = v;
                Image circ;
                IconCell(box.transform, null, "", () =>
                {
                    if (editingRoad) ed.SetSelectedLayer(val); else ed.SetCurrentLayer(val);
                    selPanelKey = "~";
                    DestroyRoot(ref layerRoot);
                }, out circ, 12);
                circ.color = val == current ? UIKit.Orange : CircleGray;
                var t = UIKit.Label(circ.transform, val.ToString(), 26, TextAnchor.MiddleCenter, Color.white);
                UIKit.Stretch(t.rectTransform);
            }
        }

        RectTransform allowRoot;

        /// <summary>Ventana «Tráfico permitido»: elegir el carril y qué vehículos pueden usarlo (se aplica de verdad al carril).</summary>
        void ShowAllowDialog()
        {
            var ed = game.editor;
            var road = ed.SelectedRoad;
            if (road == null) return;
            DestroyRoot(ref allowRoot);
            allowRoot = NewRoot("Allow", new Color(0, 0, 0, 0.6f));
            var box = UIKit.Panel(allowRoot, "Box", UIKit.PanelCol);
            Place(box.rectTransform, 0.08f, 0.08f, 0.92f, 0.92f);
            var head = UIKit.Panel(box.transform, "Head", UIKit.Orange);
            Place(head.rectTransform, 0, 1, 1, 1, 0, -64, 0, 0);
            var title = UIKit.Label(head.transform, "Tráfico permitido", 32, TextAnchor.MiddleCenter);
            UIKit.Stretch(title.rectTransform);
            var close = UIKit.Button(box.transform, "Cerrar", UIKit.Good, () => { DestroyRoot(ref allowRoot); selPanelKey = "~"; }, 30);
            Place(Rt(close), 0.3f, 0, 0.7f, 0, 0, 10, 0, -76);
            RectTransform scrollRoot;
            var list = UIKit.ScrollList(box.transform, out scrollRoot);
            Place(scrollRoot, 0, 0, 1, 1, 12, 92, 12, 72);

            var info = UIKit.Label(list, "Elige un carril y marca los vehículos que pueden usarlo. Sin ninguno marcado, el carril admite todos.", 20, TextAnchor.MiddleCenter, new Color(0.85f, 0.9f, 1f));
            UIKit.Size(info, -1, 60);
            for (int li = 0; li < road.lanesAB; li++)
            {
                int idx = li;
                bool on = ed.SelLaneForward && ed.SelLaneIndex == idx;
                AllowRow(list, "Carril A→B " + (idx + 1), on ? UIKit.Orange : UIKit.Neutral, () => ed.SelectLane(true, idx));
            }
            for (int li = 0; li < road.lanesBA; li++)
            {
                int idx = li;
                bool on = !ed.SelLaneForward && ed.SelLaneIndex == idx;
                AllowRow(list, "Carril B→A " + (idx + 1), on ? UIKit.Orange : UIKit.Neutral, () => ed.SelectLane(false, idx));
            }
            int laneMask = ed.SelLaneMask;
            for (int vt = 0; vt < VehicleNames.Length; vt++)
            {
                var vType = (VehicleType)vt;
                bool allowed = (laneMask & (1 << vt)) != 0;
                AllowRow(list, VehicleNames[vt] + (allowed ? ": permitido" : ": no"), allowed ? UIKit.Good : UIKit.Dim, () => ed.ToggleLaneVehicle(vType));
            }
            AllowRow(list, "Permitir todos", UIKit.Neutral, () => ed.SetLaneAllowAll());
            AllowRow(list, "Solo buses", UIKit.Neutral, () => ed.SetLaneAllowOnly(VehicleType.Bus));
            AllowRow(list, "Copiar a todos los carriles", UIKit.Accent, () => ed.CopyLaneAllowToAll());
            AllowRow(list, "Copiar al carril vecino", UIKit.Accent, () => ed.CopyLaneAllowToNext());
            AllowRow(list, "Toda la calle: " + RoadData.AllowLabels[road.AllowPreset], road.allow != 0 ? UIKit.Orange : UIKit.Neutral, () => ed.CycleSelectedAllow());
        }

        void AllowRow(RectTransform list, string text, Color col, Action act)
        {
            var b = UIKit.Button(list, text, col, () => { AudioManager.Instance.PlayClick(); act(); ShowAllowDialog(); }, 24);
            UIKit.Size(b, -1, 58);
        }

        void RebuildIssues()
        {
            var ed = game.editor;
            UIKit.ClearChildren(issuesRoot);
            bool show = ed.ShowIssues && ed.Issues.Count > 0;
            issuesRoot.gameObject.SetActive(show);
            if (!show) return;

            int errors = 0, warns = 0;
            foreach (var it in ed.Issues) { if (it.severity == IssueSeverity.Error) errors++; else warns++; }
            string head = ed.Readiness == MapReadiness.Editing ? "En edición · " + errors + " error" + (errors == 1 ? "" : "es") + " · " + warns + " aviso" + (warns == 1 ? "" : "s")
                : "Listo · " + warns + " aviso" + (warns == 1 ? "" : "s");
            var t = UIKit.Label(issuesRoot, head, 24, TextAnchor.MiddleLeft, Color.white);
            Place(Rt(t), 0, 1, 1, 1, 12, -46, 90, 4);
            var close = UIKit.Button(issuesRoot, "X", UIKit.Neutral, () => { ed.ShowIssues = false; }, 26);
            Place(Rt(close), 1, 1, 1, 1, 0, -46, 0, 4);
            Rt(close).anchorMin = new Vector2(1, 1);
            Rt(close).offsetMin = new Vector2(-76, -46);
            Rt(close).offsetMax = new Vector2(-6, -4);

            int sel = Mathf.Clamp(ed.IssueSel, 0, ed.Issues.Count - 1);

            var list = UIKit.Rect("IssueList", issuesRoot);
            Place(list, 0, 0, 1, 1, 12, 96, 12, 54);
            UIKit.VLayout(list, 4, 0);
            int shown = Mathf.Min(ed.Issues.Count, 5);
            // La lista siempre incluye el problema elegido.
            int first = Mathf.Clamp(sel - shown + 1, 0, Mathf.Max(0, ed.Issues.Count - shown));
            if (sel < first) first = sel;
            for (int i = first; i < first + shown && i < ed.Issues.Count; i++)
            {
                var issue = ed.Issues[i];
                int idx = i;
                var msg = (issue.severity == IssueSeverity.Error ? "[Error] " : "[Aviso] ") + issue.what;
                var capture = issue;
                Color col = issue.severity == IssueSeverity.Error ? UIKit.Bad : UIKit.Dim;
                var ib = UIKit.Button(list, (idx == sel ? "> " : "") + msg, col, () =>
                {
                    ed.IssueSel = idx;
                    if (capture.hasPos) game.cam.CenterOn(capture.pos);
                }, 17);
                UIKit.Size(ib, -1, 52);
            }
            if (ed.Issues.Count > shown)
            {
                var more = UIKit.Label(list, "Mostrando " + shown + " de " + ed.Issues.Count + ". Toca un problema para verlo y corregirlo.", 18, TextAnchor.MiddleLeft, new Color(1f, 0.9f, 0.8f));
                UIKit.Size(more, -1, 28);
            }

            var fix = UIKit.Label(issuesRoot, "Cómo corregirlo: " + ed.Issues[sel].fix, 19, TextAnchor.UpperLeft, new Color(0.8f, 1f, 0.85f));
            Place(Rt(fix), 0, 0, 1, 0, 14, 8, 222, -92);
            var repair = UIKit.Button(issuesRoot, "Reparar", UIKit.Good, () =>
            {
                int fixed1 = ed.AutoFix();
                game.ShowMessage(fixed1 > 0 ? "Se repararon " + fixed1 + " conexiones. Deshacer lo revierte." : "No hay nada que se pueda reparar automáticamente.");
                ed.Validate();
            }, 26);
            Place(Rt(repair), 1, 0, 1, 0, -208, 10, 10, -86);
        }

        void EditorBack()
        {
            var ed = game.editor;
            if (!ed.Dirty) { game.GoTo(AppScreen.Maps); return; }
            Confirm("Hay cambios sin guardar. ¿Qué quieres hacer?", "Salir sin guardar", () => game.GoTo(AppScreen.Maps),
                "Guardar y salir", () =>
                {
                    if (ed.Save()) game.GoTo(AppScreen.Maps);
                    else game.ShowMessage(ed.Status);
                });
        }

        // ------------------------------------------------------------------ Ajustes del mapa (editor)

        // ---------------- Ajustes de mapa como el original ----------------

        RectTransform setRoot;

        /// <summary>Ventana centrada con cabecera naranja; tocar fuera la cierra. Devuelve el cuerpo.</summary>
        RectTransform Popup(string title, float w, float h, Action onClose = null)
        {
            CloseMenus(true);
            DestroyRoot(ref setRoot);
            setRoot = NewRoot("Settings", new Color(0, 0, 0, 0.35f));
            var bg = setRoot.gameObject.AddComponent<Button>();
            bg.onClick.AddListener(() => { DestroyRoot(ref setRoot); if (onClose != null) onClose(); });
            float sw = safe != null ? safe.rect.width : 720f, sh = safe != null ? safe.rect.height : 1280f;
            w = Mathf.Min(w, sw - 24f); h = Mathf.Min(h, sh - 40f);
            var box = UIKit.Panel(setRoot, "Box", PanelDark);
            Corner(box.rectTransform, 0.5f, 0.5f, w, h, 0, 0);
            var hd = UIKit.Panel(box.transform, "Head", UIKit.Orange);
            Place(hd.rectTransform, 0, 1, 1, 1, 0, -50, 0, 0);
            var ht = UIKit.Label(hd.transform, title, 26, TextAnchor.MiddleLeft, Color.white);
            UIKit.Stretch(ht.rectTransform, 12, 0, 8, 0);
            var body = UIKit.Rect("Body", box.transform);
            Place(body, 0, 0, 1, 1, 12, 12, 12, 58);
            return body;
        }

        void ShowMapSettings()
        {
            var body = Popup("Ajustes de mapa", 380, 430);
            UIKit.VLayout(body, 10, 4);
            Action<string, Action> item = (t, a) =>
            {
                var b = UIKit.Button(body, t, new Color(0.1f, 0.1f, 0.11f, 1f), () => { AudioManager.Instance.PlayClick(); a(); }, 26);
                var o = b.gameObject.AddComponent<Outline>(); o.effectColor = Color.white; o.effectDistance = new Vector2(1.5f, 1.5f);
                UIKit.Size(b, -1, 56);
            };
            item("Nombre del mapa", () => ShowTextSetting("Nombre del mapa", game.editor.Map.name, 32, v => { var n = (v ?? "").Trim(); if (n.Length == 0) n = "Mapa sin nombre"; game.editor.Map.name = n; game.editor.MarkDirty(); }));
            item("Descripción", () => ShowTextSetting("Descripción", game.editor.Map.description, 200, v => { game.editor.Map.description = v ?? ""; game.editor.MarkDirty(); }));
            item("Vehículos", () => ShowVehicleSettings(0));
            item("General", () => ShowGeneralSettings());
            item("Modo de juego", () => ShowModeSettings());
            item("Más ajustes", () => { DestroyRoot(ref setRoot); ShowProps(); });
        }

        void ShowTextSetting(string title, string value, int max, Action<string> set)
        {
            var body = Popup(title, 520, 230, () => ShowMapSettings());
            var inp = UIKit.Input(body, value, max, v => set(v));
            Place(Rt(inp), 0, 1, 1, 1, 0, -80, 0, 0);
            var ok = UIKit.Button(body, "Listo", UIKit.Good, () => { set(inp.text); DestroyRoot(ref setRoot); ShowMapSettings(); }, 26);
            Place(Rt(ok), 0.25f, 0, 0.75f, 0, 0, 4, 0, -64);
        }

        Text SettingLabel(RectTransform body, string text, float y, int size = 20)
        {
            var l = UIKit.Label(body, text, size, TextAnchor.MiddleCenter, Color.white);
            Place(Rt(l), 0, 1, 1, 1, 0, y - 30, 0, -y);
            return l;
        }

        static readonly string[] VehIcons = { "taxi", "bus", "truck", "moto", "van", "tram", "ambulance" };
        static readonly string[] VehLabels = { "Taxi", "Autobús", "Camión", "Moto", "Furgoneta", "Tranvía", "Vehículo de emergencia" };

        float GetChance(TrafficSettings t, int i)
        {
            switch (i) { case 0: return t.taxiChance; case 1: return t.busChance; case 2: return t.truckChance; case 3: return t.motorbikeChance; case 4: return t.vanChance; case 5: return t.tramChance; default: return t.emergencyChance; }
        }

        void SetChance(TrafficSettings t, int i, float v)
        {
            switch (i) { case 0: t.taxiChance = v; break; case 1: t.busChance = v; break; case 2: t.truckChance = v; break; case 3: t.motorbikeChance = v; break; case 4: t.vanChance = v; break; case 5: t.tramChance = v; break; default: t.emergencyChance = v; break; }
        }

        /// <summary>Vehículos: rejilla de tipos y, abajo, el porcentaje del elegido (como el original).</summary>
        void ShowVehicleSettings(int sel)
        {
            var ed = game.editor;
            var t = ed.Map.traffic;
            var body = Popup("Vehículos", 400, 520, () => ShowMapSettings());
            var grid = UIKit.Rect("Grid", body);
            Place(grid, 0, 1, 1, 1, 0, -250, 0, 0);
            UIKit.Grid(grid, new Vector2(92, 84), 0, 0, 4);
            for (int i = 0; i < VehIcons.Length; i++)
            {
                int idx = i;
                Image circ;
                IconCell(grid, VehIcons[i], "", () => ShowVehicleSettings(idx), out circ, 12);
                circ.color = i == sel ? UIKit.Orange : CircleGray;
            }
            var val = SettingLabel(body, "", -262f, 22);
            Action refresh = () => { val.text = Mathf.RoundToInt(GetChance(t, sel) * 100f) + "%  " + VehLabels[sel]; };
            refresh();
            var sl = ColorSlider(body, -300f, null, UIKit.Orange, GetChance(t, sel) / 0.5f, v => { SetChance(t, sel, v * 0.5f); refresh(); }, () => ed.MarkDirty());
            var per = SettingLabel(body, "", -346f, 20);
            Action refreshRate = () => { per.text = Mathf.RoundToInt(t.spawnPerMinute) + " vehículos por minuto"; };
            refreshRate();
            ColorSlider(body, -380f, null, new Color(0.4f, 0.4f, 0.45f), Mathf.InverseLerp(4f, 90f, t.spawnPerMinute), v => { t.spawnPerMinute = Mathf.Round(Mathf.Lerp(4f, 90f, v)); refreshRate(); }, () => ed.MarkDirty());
            var prof = UIKit.Button(body, "Perfiles de vehículo", UIKit.Accent, () => { DestroyRoot(ref setRoot); ShowVehicleProfiles(); }, 22);
            Place(Rt(prof), 0.1f, 0, 0.9f, 0, 0, 2, 0, -50);
        }

        /// <summary>General: terreno, probabilidad de lluvia y eventos aleatorios.</summary>
        void ShowGeneralSettings()
        {
            var ed = game.editor;
            var m = ed.Map;
            var body = Popup("General", 420, 860, () => ShowMapSettings());
            SettingLabel(body, "Terreno", 0f, 22);
            var tg = UIKit.Rect("Terrain", body);
            Place(tg, 0, 1, 1, 1, 10, -190, 10, 34);
            UIKit.Grid(tg, new Vector2(78, 78), 0, 0, 5);
            for (int i = 0; i < MapRenderer.TerrainColors.Length; i++)
            {
                int idx = i;
                var cell = UIKit.Panel(tg, "T", new Color(0, 0, 0, 0));
                var b = cell.gameObject.AddComponent<Button>();
                b.targetGraphic = cell;
                b.onClick.AddListener(() => { AudioManager.Instance.PlayClick(); m.terrain = idx; ed.MarkDirty(); ed.RebuildFull(); ShowGeneralSettings(); });
                var ring = UIKit.Panel(cell.transform, "Ring", m.terrain == i ? UIKit.Orange : new Color(0.25f, 0.25f, 0.27f), false);
                ring.sprite = IconKit.Circle;
                ring.rectTransform.anchorMin = ring.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                ring.rectTransform.sizeDelta = new Vector2(60, 60);
                var dot = UIKit.Panel(ring.transform, "Dot", i == 0 ? new Color(0.62f, 0.75f, 0.5f) : SpriteFactory.Hex(MapRenderer.TerrainColors[i]), false);
                dot.sprite = IconKit.Circle;
                dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                dot.rectTransform.sizeDelta = new Vector2(46, 46);
            }
            var rain = SettingLabel(body, "", -200f, 22);
            Action rr = () => { rain.text = "Probabilidad de lluvia: " + Mathf.RoundToInt(m.rainChance * 100f) + "%"; };
            rr();
            ColorSlider(body, -236f, null, UIKit.Orange, m.rainChance, v => { m.rainChance = v; rr(); }, () => ed.MarkDirty());
            SettingLabel(body, "Eventos aleatorios", -290f, 22);
            var eg = UIKit.Rect("Events", body);
            Place(eg, 0, 1, 1, 1, 0, -552, 0, 322);
            UIKit.Grid(eg, new Vector2(92, 110), 0, 0, 4);
            string[] icons = { "ambulance", "truck", "taxi", "fire", "cone", "light", "lamp", "hydrant" };
            for (int i = 0; i < icons.Length; i++)
            {
                int bit = 1 << i;
                Image circ;
                IconCell(eg, icons[i], MapData.EventNames[i], () => { m.events ^= bit; ed.MarkDirty(); ShowGeneralSettings(); }, out circ, 13);
                circ.color = (m.events & bit) != 0 ? UIKit.Orange : CircleGray;
            }
            // Dirección del tráfico (como el original): por la derecha o por la izquierda.
            var dir = UIKit.Button(body, "Dirección del tráfico: " + (m.leftHand ? "por la izquierda" : "por la derecha"), UIKit.Neutral, () =>
            {
                AudioManager.Instance.PlayClick(); m.leftHand = !m.leftHand; ed.MarkDirty(); ed.RebuildFull(); ShowGeneralSettings();
            }, 22);
            Place(Rt(dir), 0, 1, 1, 1, 10, -612, 10, 562);
            // Perfil y cantidad de tráfico del mapa (como el original).
            var prof = UIKit.Button(body, "Perfil de tráfico: " + MapData.TrafficProfileNames[m.trafficProfile], UIKit.Neutral, () =>
            {
                AudioManager.Instance.PlayClick(); m.trafficProfile = (m.trafficProfile + 1) % MapData.TrafficProfileNames.Length; ed.MarkDirty(); ShowGeneralSettings();
            }, 22);
            Place(Rt(prof), 0, 1, 1, 1, 10, -672, 10, 622);
            var amt = SettingLabel(body, "", -684f, 22);
            Action ar = () => { amt.text = "Cantidad de tráfico: " + Mathf.RoundToInt(m.trafficAmount * 100f) + "%"; };
            ar();
            ColorSlider(body, -716f, null, UIKit.Orange, Mathf.InverseLerp(0.25f, 3f, m.trafficAmount), v => { m.trafficAmount = Mathf.Round(Mathf.Lerp(0.25f, 3f, v) * 20f) / 20f; ar(); }, () => ed.MarkDirty());
        }

        /// <summary>Modo de juego: Diverso, Simulación o Controlador de tráfico (objetivo y tiempo), y la hora de inicio.</summary>
        void ShowModeSettings()
        {
            var ed = game.editor;
            var m = ed.Map;
            var body = Popup("Modo de juego", 440, m.playMode == 2 ? 560 : 420, () => ShowMapSettings());
            var row = UIKit.Rect("Mode", body);
            Place(row, 0, 1, 1, 1, 0, -70, 0, 0);
            UIKit.HLayout(row, 6, 0);
            UIKit.Size(UIKit.Button(row, "−", UIKit.Neutral, () => { m.playMode = (m.playMode + 2) % 3; ed.MarkDirty(); ShowModeSettings(); }, 30), 64, -1);
            var name = UIKit.Label(row, MapData.PlayModeNames[m.playMode] + "\n<size=16>Modo de juego</size>", 24, TextAnchor.MiddleCenter, Color.white);
            name.supportRichText = true;
            UIKit.Size(name, -1, -1, 1f);
            UIKit.Size(UIKit.Button(row, "+", UIKit.Neutral, () => { m.playMode = (m.playMode + 1) % 3; ed.MarkDirty(); ShowModeSettings(); }, 30), 64, -1);
            var help = UIKit.Label(body, MapData.PlayModeHelp[m.playMode], 18, TextAnchor.UpperLeft, new Color(0.9f, 0.92f, 0.95f));
            Place(Rt(help), 0, 1, 1, 1, 4, -190, 4, 78);
            float y = -200f;
            if (m.playMode == 2)
            {
                var goal = UIKit.Rect("Goal", body);
                Place(goal, 0, 1, 1, 1, 0, y - 64, 0, -y);
                UIKit.HLayout(goal, 6, 0);
                UIKit.Size(UIKit.Button(goal, "−", UIKit.Neutral, () => { m.goalVehicles = Mathf.Clamp(m.goalVehicles - 5, 5, 999); ed.MarkDirty(); ShowModeSettings(); }, 30), 64, -1);
                var gl = UIKit.Label(goal, m.goalVehicles + "\n<size=16>Objetivo (vehículos)</size>", 24, TextAnchor.MiddleCenter, Color.white);
                gl.supportRichText = true;
                UIKit.Size(gl, -1, -1, 1f);
                UIKit.Size(UIKit.Button(goal, "+", UIKit.Neutral, () => { m.goalVehicles = Mathf.Clamp(m.goalVehicles + 5, 5, 999); ed.MarkDirty(); ShowModeSettings(); }, 30), 64, -1);
                y -= 76f;
                var tl = SettingLabel(body, "", y, 22);
                Action rt = () => { tl.text = FormatTime(m.goalMinutes * 60f) + "  Tiempo de juego"; };
                rt();
                ColorSlider(body, y - 34f, null, UIKit.Orange, Mathf.InverseLerp(1f, 30f, m.goalMinutes), v => { m.goalMinutes = Mathf.Round(Mathf.Lerp(1f, 30f, v) * 4f) / 4f; rt(); }, () => ed.MarkDirty());
                y -= 86f;
            }
            var hl = SettingLabel(body, "", y, 22);
            Action rh = () => { int hh = Mathf.FloorToInt(m.startHour), mm = Mathf.RoundToInt((m.startHour - hh) * 60f) % 60; hl.text = hh.ToString("00") + ":" + mm.ToString("00") + "  Hora de inicio (el día avanza al jugar)"; };
            rh();
            ColorSlider(body, y - 34f, null, UIKit.Orange, m.startHour / 24f, v => { m.startHour = Mathf.Round(v * 24f * 4f) / 4f; if (m.startHour >= 24f) m.startHour = 23.75f; rh(); }, () => ed.MarkDirty());
        }

        void ShowProps()
        {
            if (propsRoot != null) return;
            propsRoot = NewRoot("Props", new Color(0, 0, 0, 0.7f));
            var box = UIKit.Panel(propsRoot, "Box", UIKit.PanelCol);
            Place(box.rectTransform, 0.12f, 0.04f, 0.88f, 0.96f);

            var title = UIKit.Label(box.transform, "Ajustes del mapa", 40, TextAnchor.MiddleCenter);
            Place(Rt(title), 0, 1, 1, 1, 10, -64, 10, 6);

            var close = UIKit.Button(box.transform, "Cerrar", UIKit.Good, () =>
            {
                DestroyRoot(ref propsRoot);
                propsBody = null;
            }, 30);
            Place(Rt(close), 0.3f, 0, 0.7f, 0, 0, 10, 0, 0);
            Rt(close).offsetMax = new Vector2(0, 80);
            Rt(close).offsetMin = new Vector2(0, 10);

            RectTransform scrollRoot;
            var content = UIKit.ScrollList(box.transform, out scrollRoot);
            Place(scrollRoot, 0, 0, 1, 1, 16, 96, 16, 74);
            propsBody = content;
            RebuildProps();
        }

        void RebuildProps()
        {
            if (propsBody == null) return;
            var ed = game.editor;
            var m = ed.Map;
            UIKit.ClearChildren(propsBody);

            var nameRow = UIKit.Rect("NameRow", propsBody);
            UIKit.Size(nameRow, -1, 70);
            UIKit.HLayout(nameRow, 10, 0);
            var nl = UIKit.Label(nameRow, "Nombre", 28, TextAnchor.MiddleLeft);
            UIKit.Size(nl, 220, -1);
            var input = UIKit.Input(nameRow, m.name, 32, s =>
            {
                string n = (s ?? "").Trim();
                if (n.Length == 0) n = "Mapa sin nombre";
                if (ed.Map.name != n) { ed.Map.name = n; ed.MarkDirty(); }
            });
            UIKit.Size(input, -1, -1, 1f);

            PropRow("Ancho", m.width.ToString("0"), () => { ed.ResizeBy(-2f, 0f); }, () => { ed.ResizeBy(2f, 0f); });
            PropRow("Alto", m.height.ToString("0"), () => { ed.ResizeBy(0f, -2f); }, () => { ed.ResizeBy(0f, 2f); });
            PropRow("Dificultad", m.difficulty + " de 5",
                () => { ed.Map.difficulty = Mathf.Clamp(ed.Map.difficulty - 1, 1, 5); ed.MarkDirty(); },
                () => { ed.Map.difficulty = Mathf.Clamp(ed.Map.difficulty + 1, 1, 5); ed.MarkDirty(); });
            PropRow("Pasos de peatones automáticos", m.autoCrosswalks ? "sí" : "no",
                () => { ed.Map.autoCrosswalks = !ed.Map.autoCrosswalks; ed.MarkDirty(); ed.RebuildFull(); },
                () => { ed.Map.autoCrosswalks = !ed.Map.autoCrosswalks; ed.MarkDirty(); ed.RebuildFull(); });
            PropRow("Líneas de detención automáticas", m.autoStopLines ? "sí" : "no",
                () => { ed.Map.autoStopLines = !ed.Map.autoStopLines; ed.MarkDirty(); ed.RebuildFull(); },
                () => { ed.Map.autoStopLines = !ed.Map.autoStopLines; ed.MarkDirty(); ed.RebuildFull(); });
            ChanceRow("Taxis", () => ed.Map.traffic.taxiChance, v => ed.Map.traffic.taxiChance = v);
            ChanceRow("Autobuses", () => ed.Map.traffic.busChance, v => ed.Map.traffic.busChance = v);
            ChanceRow("Camiones", () => ed.Map.traffic.truckChance, v => ed.Map.traffic.truckChance = v);
            ChanceRow("Motos", () => ed.Map.traffic.motorbikeChance, v => ed.Map.traffic.motorbikeChance = v);
            ChanceRow("Furgonetas", () => ed.Map.traffic.vanChance, v => ed.Map.traffic.vanChance = v);
            var profBtn = UIKit.Button(propsBody, "Perfiles de vehículo (velocidad, aceleración, separación)", UIKit.Accent, () => ShowVehicleProfiles(), 26);
            UIKit.Size(profBtn, -1, 70);
            PropRow("Flechas automáticas", m.autoArrows ? "sí" : "no",
                () => { ed.Map.autoArrows = !ed.Map.autoArrows; ed.MarkDirty(); ed.RebuildFull(); },
                () => { ed.Map.autoArrows = !ed.Map.autoArrows; ed.MarkDirty(); ed.RebuildFull(); });
            PropRow("Máx. vehículos a la vez", m.traffic.maxVehicles.ToString(),
                () => { ed.Map.traffic.maxVehicles = Mathf.Clamp(ed.Map.traffic.maxVehicles - 10, 10, 200); ed.MarkDirty(); },
                () => { ed.Map.traffic.maxVehicles = Mathf.Clamp(ed.Map.traffic.maxVehicles + 10, 10, 200); ed.MarkDirty(); });
            PropRow("Vehículos por minuto", Mathf.RoundToInt(m.traffic.spawnPerMinute).ToString(),
                () => { ed.Map.traffic.spawnPerMinute = Mathf.Clamp(ed.Map.traffic.spawnPerMinute - 4f, 4f, 90f); ed.MarkDirty(); },
                () => { ed.Map.traffic.spawnPerMinute = Mathf.Clamp(ed.Map.traffic.spawnPerMinute + 4f, 4f, 90f); ed.MarkDirty(); });
            PropRow("Velocidad del tráfico", Mathf.RoundToInt(m.traffic.speedFactor * 100f) + "%",
                () => { ed.Map.traffic.speedFactor = Mathf.Clamp(ed.Map.traffic.speedFactor - 0.1f, 0.5f, 1.5f); ed.MarkDirty(); },
                () => { ed.Map.traffic.speedFactor = Mathf.Clamp(ed.Map.traffic.speedFactor + 0.1f, 0.5f, 1.5f); ed.MarkDirty(); });
            PropRow("Vehículos de emergencia", Mathf.RoundToInt(m.traffic.emergencyChance * 100f) + "%",
                () => { ed.Map.traffic.emergencyChance = Mathf.Clamp(ed.Map.traffic.emergencyChance - 0.02f, 0f, 0.4f); ed.MarkDirty(); },
                () => { ed.Map.traffic.emergencyChance = Mathf.Clamp(ed.Map.traffic.emergencyChance + 0.02f, 0f, 0.4f); ed.MarkDirty(); });
            PropRow("Peatones por minuto", Mathf.RoundToInt(m.traffic.pedestriansPerMinute).ToString(),
                () => { ed.Map.traffic.pedestriansPerMinute = Mathf.Clamp(ed.Map.traffic.pedestriansPerMinute - 2f, 0f, 40f); ed.MarkDirty(); },
                () => { ed.Map.traffic.pedestriansPerMinute = Mathf.Clamp(ed.Map.traffic.pedestriansPerMinute + 2f, 0f, 40f); ed.MarkDirty(); });
            PropRow("Semáforos seguros", m.traffic.safeSignals ? "Sí" : "No",
                () => { ed.Map.traffic.safeSignals = !ed.Map.traffic.safeSignals; ed.MarkDirty(); },
                () => { ed.Map.traffic.safeSignals = !ed.Map.traffic.safeSignals; ed.MarkDirty(); });
            PropRow("Ciclo automático", m.traffic.autoCycle ? "Sí" : "No",
                () => { ed.Map.traffic.autoCycle = !ed.Map.traffic.autoCycle; ed.MarkDirty(); },
                () => { ed.Map.traffic.autoCycle = !ed.Map.traffic.autoCycle; ed.MarkDirty(); });
            PropRow("Segundos en verde (auto)", m.traffic.greenSeconds.ToString("0"),
                () => { ed.Map.traffic.greenSeconds = Mathf.Clamp(ed.Map.traffic.greenSeconds - 1f, 3f, 30f); ed.MarkDirty(); },
                () => { ed.Map.traffic.greenSeconds = Mathf.Clamp(ed.Map.traffic.greenSeconds + 1f, 3f, 30f); ed.MarkDirty(); });

            var note = UIKit.Label(propsBody, "Los cambios de esta ventana no se pueden deshacer con «Deshacer», salvo el tamaño.", 22, TextAnchor.MiddleLeft, new Color(0.7f, 0.75f, 0.85f));
            UIKit.Size(note, -1, 60);
        }

        // ---------------- Tabla de tráfico ----------------

        RectTransform gridRoot;

        /// <summary>
        /// Tabla como el original: tres estilos (sin rejilla, rejilla, rejilla con diagonales) y el tamaño; se abre junto
        /// al botón Tabla. Mantener pulsado el tamaño no hace falta: cada toque pasa al siguiente.
        /// </summary>
        void ShowGridPicker()
        {
            var ed = game.editor;
            DestroyRoot(ref gridRoot);
            CloseMenus(true);
            gridRoot = NewRoot("GridPicker", new Color(0, 0, 0, 0.01f));
            var close = gridRoot.gameObject.AddComponent<Button>();
            close.onClick.AddListener(() => DestroyRoot(ref gridRoot));
            var box = UIKit.Panel(gridRoot, "Box", PanelDark);
            const float c = 78f;
            Corner(box.rectTransform, 0, 1, 3 * c + 16, 2 * c + 16, 0, -(6 * 90f + 8));   // justo debajo del botón Tabla, como el original
            UIKit.Grid(box.rectTransform, new Vector2(c, c), 0, 8, 3);
            string[] icons = { "grid_none", "grid", "grid_diag" };
            for (int i = 0; i < 3; i++)
            {
                int st = i;
                Image circ;
                IconCell(box.transform, icons[i], "", () => { ed.SetGridStyle(st); ShowGridPicker(); }, out circ, 12);
                circ.color = ed.GridStyle == st ? UIKit.Orange : CircleGray;
            }
            Image sc;
            float rel = ed.GridStep / 0.25f;   // 1 = el carril ocupa 2 casillas (como el original)
            IconCell(box.transform, null, "Tamaño", () => { ed.CycleGridSize(); ShowGridPicker(); }, out sc, 14);
            sc.sprite = null;
            sc.color = UIKit.Orange;
            var t = UIKit.Label(sc.transform, rel.ToString("0.##"), 24, TextAnchor.MiddleCenter, Color.white);
            UIKit.Stretch(t.rectTransform);
            Image ac;
            IconCell(box.transform, null, ed.SnapOn ? "Ajuste" : "Libre", () => { ed.ToggleSnap(); ShowGridPicker(); }, out ac, 14);
            ac.color = ed.SnapOn ? UIKit.Orange : CircleGray;
            var at = UIKit.Label(ac.transform, ed.SnapOn ? "#" : "~", 26, TextAnchor.MiddleCenter, Color.white);
            UIKit.Stretch(at.rectTransform);
        }

        /// <summary>Controles de cuadrícula a un toque: mostrar, ajuste y tamaño de celda (en metros reales de la escala del mapa).</summary>
        void ShowGridDialog()
        {
            var ed = game.editor;
            CloseDialog();
            dialogRoot = NewRoot("Dialog", new Color(0, 0, 0, 0.5f));
            var box = UIKit.Panel(dialogRoot, "Box", UIKit.PanelCol);
            box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            box.rectTransform.sizeDelta = new Vector2(620, BoxHeight(520f));
            var title = UIKit.Label(box.transform, "Cuadrícula", 36, TextAnchor.MiddleCenter, Color.white);
            Place(Rt(title), 0, 0.84f, 1, 1, 20, 0, 20, 6);
            var body = UIKit.Rect("Body", box.transform);
            Place(body, 0, 0.18f, 1, 0.84f, 24, 0, 24, 0);
            UIKit.VLayout(body, 10, 0);
            Action rebuild = null;
            rebuild = () =>
            {
                UIKit.ClearChildren(body);
                var b1 = UIKit.Button(body, ed.ShowGrid ? "Mostrar cuadrícula: sí" : "Mostrar cuadrícula: no", ed.ShowGrid ? UIKit.Good : UIKit.Neutral, () => { ed.ToggleShowGrid(); rebuild(); }, 28);
                UIKit.Size(b1, -1, 64);
                var b2 = UIKit.Button(body, ed.SnapOn ? "Ajustar a la cuadrícula: sí" : "Ajustar a la cuadrícula: no", ed.SnapOn ? UIKit.Good : UIKit.Neutral, () => { ed.ToggleSnap(); rebuild(); }, 28);
                UIKit.Size(b2, -1, 64);
                var row = UIKit.Rect("Size", body);
                UIKit.Size(row, -1, 64);
                UIKit.HLayout(row, 8, 0);
                UIKit.Button(row, "−", UIKit.Neutral, () => { ed.AdjustGrid(-1); rebuild(); }, 34);
                UIKit.Label(row, "Celda: " + (ed.GridStep * MapEditorController.MetersPerUnit).ToString("0.##") + " m", 28, TextAnchor.MiddleCenter, Color.white);
                UIKit.Button(row, "+", UIKit.Neutral, () => { ed.AdjustGrid(1); rebuild(); }, 34);
                var info = UIKit.Label(body, "Cambiar la cuadrícula no mueve lo ya colocado. Un carril mide " + (RoadNetworkManager.LaneWidth * MapEditorController.MetersPerUnit).ToString("0.0") + " m.", 20, TextAnchor.MiddleCenter, new Color(0.85f, 0.9f, 1f));
                UIKit.Size(info, -1, 60);
            };
            rebuild();
            var ok = UIKit.Button(box.transform, "Cerrar", UIKit.Accent, () => CloseDialog(), 30);
            Place(Rt(ok), 0.3f, 0, 0.7f, 0.15f, 0, 16, 0, 0);
        }

        void ShowTable()
        {
            if (tableRoot != null) return;
            tableRoot = NewRoot("Table", new Color(0, 0, 0, 0.7f));
            var box = UIKit.Panel(tableRoot, "Box", UIKit.PanelCol);
            Place(box.rectTransform, 0.12f, 0.04f, 0.88f, 0.96f);

            var head = UIKit.Panel(box.transform, "Head", UIKit.Orange);
            Place(head.rectTransform, 0, 1, 1, 1, 0, -70, 0, 0);
            var title = UIKit.Label(head.transform, "Tabla de tráfico", 36, TextAnchor.MiddleCenter);
            UIKit.Stretch(title.rectTransform);

            var close = UIKit.Button(box.transform, "Cerrar", UIKit.Good, () => { DestroyRoot(ref tableRoot); tableBody = null; }, 30);
            Place(Rt(close), 0.3f, 0, 0.7f, 0, 0, 10, 0, 0);
            Rt(close).offsetMax = new Vector2(0, 80);
            Rt(close).offsetMin = new Vector2(0, 10);

            RectTransform scrollRoot;
            var content = UIKit.ScrollList(box.transform, out scrollRoot);
            Place(scrollRoot, 0, 0, 1, 1, 16, 96, 16, 80);
            tableBody = content;
            RebuildTable();
        }

        /// <summary>¿Es una entrada (o salida) de vehículos? En mapas por carriles se deduce sola de los carriles.</summary>
        static bool IsPortal(MapData m, NodeData n, bool entries)
        {
            if (!m.IsLaneMap) return entries ? n.entry : n.exit;
            bool ins = false, outs = false;
            foreach (var r in m.roads)
            {
                if (r.laneType != RoadData.LaneVehicle) continue;
                if (r.b == n.id) ins = true;
                if (r.a == n.id) outs = true;
            }
            return entries ? (outs && !ins) : (ins && !outs);
        }

        void RebuildTable()
        {
            if (tableBody == null) return;
            var ed = game.editor;
            UIKit.ClearChildren(tableBody);
            var m = ed.Map;

            var info = UIKit.Label(tableBody, "Peso 0 = no aparecen vehículos (entradas) o nadie va ahí (salidas). Más peso = más tráfico.", 22, TextAnchor.MiddleLeft, new Color(0.85f, 0.9f, 1f));
            UIKit.Size(info, -1, 60);

            TableSection("Entradas (dónde aparecen)", m, true);
            TableSection("Salidas (destinos)", m, false);
        }

        void TableSection(string title, MapData m, bool entries)
        {
            var ed = game.editor;
            float total = 0f;
            int count = 0;
            foreach (var n in m.nodes) if (IsPortal(m, n, entries)) { total += n.weight; count++; }
            var h = UIKit.Label(tableBody, title, 28, TextAnchor.MiddleLeft, new Color(1f, 0.8f, 0.4f));
            UIKit.Size(h, -1, 48);
            if (count == 0)
            {
                var none = UIKit.Label(tableBody, entries ? "No hay nodos de entrada." : "No hay nodos de salida.", 24, TextAnchor.MiddleLeft, Color.white);
                UIKit.Size(none, -1, 44);
                return;
            }
            int k = 0;
            foreach (var n in m.nodes)
            {
                if (!IsPortal(m, n, entries)) continue;
                k++;
                int id = n.id;
                var row = UIKit.Rect("Row", tableBody);
                UIKit.Size(row, -1, 64);
                UIKit.HLayout(row, 8, 0);
                var l = UIKit.Label(row, (entries ? "Entrada " : "Salida ") + k + "  (" + n.x.ToString("0") + "," + n.y.ToString("0") + ")", 24, TextAnchor.MiddleLeft);
                UIKit.Size(l, -1, -1, 1f);
                UIKit.Button(row, "-", UIKit.Neutral, () => { ed.AdjustNodeWeight(id, -0.5f); RebuildTable(); }, 36).gameObject.AddComponent<LayoutElement>().preferredWidth = 80;
                var v = UIKit.Label(row, n.weight.ToString("0.0"), 28, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.5f));
                UIKit.Size(v, 70, -1);
                UIKit.Button(row, "+", UIKit.Neutral, () => { ed.AdjustNodeWeight(id, 0.5f); RebuildTable(); }, 36).gameObject.AddComponent<LayoutElement>().preferredWidth = 80;
                string pct = total > 0.001f ? Mathf.RoundToInt(n.weight / total * 100f) + "%" : "-";
                var pl = UIKit.Label(row, pct, 26, TextAnchor.MiddleRight);
                UIKit.Size(pl, 80, -1);
            }
        }

        void ChanceRow(string label, Func<float> get, Action<float> set)
        {
            var ed = game.editor;
            PropRow(label, Mathf.RoundToInt(get() * 100f) + "%",
                () => { set(Mathf.Clamp01(Mathf.Round((get() - 0.02f) * 100f) / 100f)); ed.MarkDirty(); },
                () => { set(Mathf.Clamp(Mathf.Round((get() + 0.02f) * 100f) / 100f, 0f, 0.6f)); ed.MarkDirty(); });
        }

        RectTransform profRoot, profBody;

        /// <summary>Perfiles por tipo de vehículo: multiplicadores de velocidad máxima, aceleración y separación (se guardan en el mapa).</summary>
        RectTransform diagRoot;

        /// <summary>Ejecuta las pruebas internas de mapas (conexiones, validación, rutas, guardado) y muestra el resultado real.</summary>
        void ShowSelfTest()
        {
            if (diagRoot != null) return;
            var results = MapSelfTest.RunAll();
            MapSelfTest.SaveReport(MapSelfTest.Report(results));
            int pass = 0;
            foreach (var r in results) if (r.passed) pass++;

            diagRoot = NewRoot("Diag", new Color(0, 0, 0, 0.75f));
            var box = UIKit.Panel(diagRoot, "Box", UIKit.PanelCol);
            Place(box.rectTransform, 0.06f, 0.04f, 0.94f, 0.96f);
            var head = UIKit.Panel(box.transform, "Head", pass == results.Count ? UIKit.Good : UIKit.Orange);
            Place(head.rectTransform, 0, 1, 1, 1, 0, -70, 0, 0);
            var title = UIKit.Label(head.transform, "Diagnóstico: " + pass + " de " + results.Count + " pruebas correctas", 30, TextAnchor.MiddleCenter);
            UIKit.Stretch(title.rectTransform);
            var close = UIKit.Button(box.transform, "Cerrar", UIKit.Good, () => { DestroyRoot(ref diagRoot); }, 30);
            Place(Rt(close), 0.3f, 0, 0.7f, 0, 0, 10, 0, 0);
            Rt(close).offsetMax = new Vector2(0, 80);
            Rt(close).offsetMin = new Vector2(0, 10);
            RectTransform scrollRoot;
            var body = UIKit.ScrollList(box.transform, out scrollRoot);
            Place(scrollRoot, 0, 0, 1, 1, 16, 96, 16, 80);
            var note = UIKit.Label(body, "Solo prueba la lógica de mapas y validación. Los toques en pantalla y el tráfico en marcha hay que probarlos a mano.", 20, TextAnchor.MiddleLeft, new Color(0.85f, 0.9f, 1f));
            UIKit.Size(note, -1, 64);
            foreach (var r in results)
            {
                string text = (r.passed ? "[OK] " : "[FALLA] ") + r.name + (r.passed ? "" : "\n" + r.detail);
                int lines = 1 + (text.Length + 44) / 45;
                var l = UIKit.Label(body, text, 20, TextAnchor.MiddleLeft, r.passed ? new Color(0.6f, 1f, 0.65f) : new Color(1f, 0.6f, 0.55f));
                UIKit.Size(l, -1, Mathf.Max(48, 26 * lines));
            }
        }

        void ShowVehicleProfiles()
        {
            if (profRoot != null) return;
            profRoot = NewRoot("Profiles", new Color(0, 0, 0, 0.7f));
            var box = UIKit.Panel(profRoot, "Box", UIKit.PanelCol);
            Place(box.rectTransform, 0.08f, 0.04f, 0.92f, 0.96f);
            var head = UIKit.Panel(box.transform, "Head", UIKit.Orange);
            Place(head.rectTransform, 0, 1, 1, 1, 0, -70, 0, 0);
            var title = UIKit.Label(head.transform, "Perfiles de vehículo", 36, TextAnchor.MiddleCenter);
            UIKit.Stretch(title.rectTransform);
            var close = UIKit.Button(box.transform, "Cerrar", UIKit.Good, () => { DestroyRoot(ref profRoot); profBody = null; }, 30);
            Place(Rt(close), 0.3f, 0, 0.7f, 0, 0, 10, 0, 0);
            Rt(close).offsetMax = new Vector2(0, 80);
            Rt(close).offsetMin = new Vector2(0, 10);
            RectTransform scrollRoot;
            profBody = UIKit.ScrollList(box.transform, out scrollRoot);
            Place(scrollRoot, 0, 0, 1, 1, 16, 96, 16, 80);
            RebuildProfiles();
        }

        void RebuildProfiles()
        {
            if (profBody == null) return;
            var ed = game.editor;
            var t = ed.Map.traffic;
            UIKit.ClearChildren(profBody);
            var info = UIKit.Label(profBody, "100% = valores del juego. Separación = distancia y tiempo de seguridad respecto al de delante.", 22, TextAnchor.MiddleCenter, new Color(0.85f, 0.9f, 1f));
            UIKit.Size(info, -1, 60);
            for (int vt = 0; vt < VehicleNames.Length; vt++)
            {
                int k = vt;
                var row = UIKit.Rect("Row", profBody);
                UIKit.Size(row, -1, 64);
                UIKit.HLayout(row, 6, 0);
                var l = UIKit.Label(row, VehicleNames[k], 26, TextAnchor.MiddleLeft);
                UIKit.Size(l, 200, -1);
                ProfCell(row, "Vel.", () => TrafficSettings.Mul(ed.Map.traffic.typeSpeed, k), v => ed.Map.traffic.typeSpeed = TrafficSettings.SetMul(ed.Map.traffic.typeSpeed, k, v));
                ProfCell(row, "Acel.", () => TrafficSettings.Mul(ed.Map.traffic.typeAccel, k), v => ed.Map.traffic.typeAccel = TrafficSettings.SetMul(ed.Map.traffic.typeAccel, k, v));
                ProfCell(row, "Sep.", () => TrafficSettings.Mul(ed.Map.traffic.typeGap, k), v => ed.Map.traffic.typeGap = TrafficSettings.SetMul(ed.Map.traffic.typeGap, k, v));
            }
        }

        void ProfCell(RectTransform row, string label, Func<float> get, Action<float> set)
        {
            var ed = game.editor;
            UIKit.Button(row, "−", UIKit.Neutral, () => { set(get() - 0.1f); ed.MarkDirty(); RebuildProfiles(); }, 28).gameObject.AddComponent<LayoutElement>().preferredWidth = 56;
            var v = UIKit.Label(row, label + " " + Mathf.RoundToInt(get() * 100f) + "%", 22, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.5f));
            UIKit.Size(v, 150, -1);
            UIKit.Button(row, "+", UIKit.Neutral, () => { set(get() + 0.1f); ed.MarkDirty(); RebuildProfiles(); }, 28).gameObject.AddComponent<LayoutElement>().preferredWidth = 56;
        }

        void PropRow(string label, string value, Action minus, Action plus)
        {
            var row = UIKit.Rect("Row", propsBody);
            UIKit.Size(row, -1, 70);
            UIKit.HLayout(row, 10, 0);
            var l = UIKit.Label(row, label, 28, TextAnchor.MiddleLeft);
            UIKit.Size(l, -1, -1, 1f);
            UIKit.Button(row, "-", UIKit.Neutral, () => { minus(); RebuildProps(); }, 40).gameObject.AddComponent<LayoutElement>().preferredWidth = 90;
            var v = UIKit.Label(row, value, 30, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.5f));
            UIKit.Size(v, 140, -1);
            UIKit.Button(row, "+", UIKit.Neutral, () => { plus(); RebuildProps(); }, 40).gameObject.AddComponent<LayoutElement>().preferredWidth = 90;
        }

        // ------------------------------------------------------------------ Varios

        static string FormatTime(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }
    }
}
