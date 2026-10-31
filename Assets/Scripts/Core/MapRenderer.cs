using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Data;
using IntersectionMaster.Systems;
using IntersectionMaster.Traffic;

namespace IntersectionMaster.Core
{
    /// <summary>
    /// Dibuja un mapa de nodos y calles: aceras con bordillos redondeados, calzadas de varios carriles con curvas,
    /// líneas centrales y de carril, líneas de parada, flechas de giro, pasos de peatones, rotondas, señales y
    /// decoración. La geometría vial se genera como mallas (un objeto por capa) y el resto con sprites.
    /// Los semáforos son sprites animados. El aspecto cambia según el tema desbloqueado (ver RewardSystem).
    /// </summary>
    public partial class MapRenderer : MonoBehaviour
    {
        class SignalView
        {
            public IntersectionController ic;
            public Approach app;
            public SpriteRenderer lamp;
            public SpriteRenderer bar, glow;
            public SpriteRenderer red, yellow, green, ped;   // modelo con tres luces (y peatonal opcional)
            public Vector2 mid;
            public float radius;
            public Vector2 head;        // cabeza del semáforo: tocarla también lo cambia
            public bool hasHead;
            public List<SpriteRenderer> extra;   // manchas y franjas de los demás carriles del acceso
            public List<SpriteRenderer[]> heads;  // cabezas del semáforo (rojo, ámbar, verde) sobre cada carril
            public bool pedLight;                 // semáforo peatonal: en ámbar el verde parpadea
        }

        const float CurbWidth = 0.28f;

        // Paleta de color del asfalto de una calle (0 = el del tema).
        public static readonly string[] RoadColors = { "", "#6B6F7A", "#2B2D33", "#8A6D4B", "#A04848", "#3E6FB5" };
        public static readonly string[] RoadColorNames = { "Tema", "Gris", "Negro", "Tierra", "Rojo", "Azul" };

        static readonly Color Paint = new Color32(232, 197, 71, 255);
        static readonly Color White = new Color32(240, 240, 235, 255);
        static readonly Color EdgeLine = new Color(0.94f, 0.94f, 0.92f, 0.6f);
        static readonly Color ArrowCol = new Color(0.9f, 0.9f, 0.88f, 1f);   // opaca: con transparencia se veían las piezas encimadas
        static readonly string[] BuildingColors = { "#C9A27E", "#B9B2C9", "#D8C3A5", "#A9C0B5" };

        Transform root, staticRoot, roadRoot, signalRoot;
        ThemeDef theme;
        Color asphalt, junction, sidewalk;
        readonly List<SignalView> signals = new List<SignalView>();
        SpriteRenderer selection;
        IntersectionController selected;

        /// <summary>Red construida para el último mapa dibujado (la usa el editor para consultas).</summary>
        public RoadNetworkManager Net { get; private set; }

        void Awake()
        {
            root = new GameObject("MapVisuals").transform;
            root.SetParent(transform, false);
        }

        public void Clear()
        {
            signals.Clear();
            selection = null;
            selected = null;
            if (root != null) Destroy(root.gameObject);
            root = new GameObject("MapVisuals").transform;
            root.SetParent(transform, false);
            staticRoot = roadRoot = signalRoot = null;
            ClearDayNight();
        }

        void LoadTheme()
        {
            theme = RewardSystem.Theme();
            asphalt = SpriteFactory.Hex(theme.asphalt);
            junction = SpriteFactory.Hex(theme.junction);
            sidewalk = SpriteFactory.Hex(theme.sidewalk);
        }

        /// <summary>Colores del terreno (0 = el normal del tema).</summary>
        public static readonly string[] TerrainColors = { "", "#CBE5A4", "#9CC46B", "#6E9A55", "#D9CD8E", "#E8D9B0", "#B9B9B0", "#F2F4F6", "#A9C7DA" };
        static readonly string[] TerrainOutside = { "", "#8FA070", "#6E8A4B", "#4E6E3C", "#A69A60", "#B5A784", "#8A8A82", "#C9CDD2", "#7C98AA" };
        public static readonly string[] TerrainNames = { "Normal", "Césped claro", "Césped", "Bosque", "Seco", "Arena", "Gris", "Nieve", "Agua" };

        public void Build(MapData map, bool editorMode)
        {
            map.EnsureLanes();
            Clear();
            LoadTheme();
            string groundHex = theme.ground, outsideHex = theme.outside;
            if (editorMode)
            {
                // Paleta del editor: fondo verde claro, calzada gris y aceras claras (como el editor de referencia).
                asphalt = junction = SpriteFactory.Hex("#A1A1A1");
                sidewalk = SpriteFactory.Hex("#F2EEE8");
                groundHex = "#CBE5A4";
                outsideHex = "#8FA070";
            }
            if (map.IsLaneMap && map.terrain > 0 && map.terrain < TerrainColors.Length)
            {
                // Terreno elegido en Ajustes de mapa → General (como el original): cambia el color del suelo.
                groundHex = TerrainColors[map.terrain];
                outsideHex = TerrainOutside[map.terrain];
            }
            staticRoot = NewChild("Static");
            roadRoot = NewChild("Roads");
            signalRoot = NewChild("Signals");

            float w = map.width, h = map.height;
            // En mapas por carriles el suelo va más abajo para que se vean los túneles (capas negativas).
            int gOrd = map.IsLaneMap ? -120 : -20;
            SpriteFactory.Make("Outside", staticRoot, SpriteFactory.Square, SpriteFactory.Hex(outsideHex), new Vector2(w / 2f, h / 2f), new Vector2(w + 80f, h + 80f), gOrd);
            SpriteFactory.Make("Ground", staticRoot, SpriteFactory.Square, SpriteFactory.Hex(groundHex), new Vector2(w / 2f, h / 2f), new Vector2(w, h), gOrd + 1);
            if (editorMode && map.IsLaneMap)
            {
                // Como en el editor de referencia: el mapa entero más oscuro y el área de juego clara con borde rojo.
                Rect v = map.View;
                SpriteFactory.Make("MapDark", staticRoot, SpriteFactory.Square, SpriteFactory.Hex("#8E9E72"), new Vector2(w / 2f, h / 2f), new Vector2(w, h), gOrd + 1);
                SpriteFactory.Make("PlayArea", staticRoot, SpriteFactory.Square, SpriteFactory.Hex(groundHex), v.center, v.size, gOrd + 2);
                var red = SpriteFactory.Hex("#C8281E");
                const float B = 0.05f;
                SpriteFactory.Make("AreaL", staticRoot, SpriteFactory.Square, red, new Vector2(v.xMin, v.center.y), new Vector2(B, v.height + B), 50);
                SpriteFactory.Make("AreaR", staticRoot, SpriteFactory.Square, red, new Vector2(v.xMax, v.center.y), new Vector2(B, v.height + B), 50);
                SpriteFactory.Make("AreaB", staticRoot, SpriteFactory.Square, red, new Vector2(v.center.x, v.yMin), new Vector2(v.width + B, B), 50);
                SpriteFactory.Make("AreaT", staticRoot, SpriteFactory.Square, red, new Vector2(v.center.x, v.yMax), new Vector2(v.width + B, B), 50);
            }

            curEditor = editorMode;
            LeftHand = map.leftHand;
            railSignLamps.Clear();
            detectorWarns.Clear();
            decorParts.Clear();
            foreach (var d in map.decor)
            {
                int before = staticRoot.childCount;
                try
                {
                    DrawDecor(d, map);
                    if (IsTall(d.type)) GroupTall(before, d);
                }
                catch (System.Exception ex) { Debug.LogWarning("No se pudo dibujar " + d.type + ": " + ex.Message); }   // un objeto raro no deja el mapa a medias
                if (editorMode)
                {
                    var parts = new List<Transform>();
                    for (int i = before; i < staticRoot.childCount; i++) parts.Add(staticRoot.GetChild(i));
                    decorParts[d] = parts;
                }
            }
            PoleCables(map);

            BuildRoads(map, editorMode);

            if (!editorMode)
            {
                // Fuera del área visible todo queda tapado: ahí aparecen y desaparecen coches y peatones.
                Rect v = map.View;
                Color mask = new Color(0.05f, 0.06f, 0.08f, 1f);
                const float Big = 400f;
                SpriteFactory.Make("MaskL", staticRoot, SpriteFactory.Square, mask, new Vector2(v.xMin - Big / 2f, v.center.y), new Vector2(Big, Big * 2f), 60);
                SpriteFactory.Make("MaskR", staticRoot, SpriteFactory.Square, mask, new Vector2(v.xMax + Big / 2f, v.center.y), new Vector2(Big, Big * 2f), 60);
                SpriteFactory.Make("MaskB", staticRoot, SpriteFactory.Square, mask, new Vector2(v.center.x, v.yMin - Big / 2f), new Vector2(v.width, Big), 60);
                SpriteFactory.Make("MaskT", staticRoot, SpriteFactory.Square, mask, new Vector2(v.center.x, v.yMax + Big / 2f), new Vector2(v.width, Big), 60);

                var wgo = new GameObject("Weather");
                wgo.transform.SetParent(root, false);
                var fx = wgo.AddComponent<WeatherFx>();
                int kind = theme.id == "nieve" ? 2 : (Mathf.Abs((map.id ?? "").GetHashCode()) % 2 == 0 ? 1 : 0);
                fx.Setup(kind);
            }

            if (editorMode) { if (ShowGridLines) DrawGrid(map); }
            else if (theme.overlay.a > 0.01f)
                SpriteFactory.Make("ThemeOverlay", staticRoot, SpriteFactory.Square, theme.overlay, new Vector2(w / 2f, h / 2f), new Vector2(w + 80f, h + 80f), 40);
        }

        /// <summary>Redibuja sólo la red vial (rápido; para arrastrar nodos en el editor). Requiere un Build previo.</summary>
        public void BuildRoadsOnly(MapData map, bool editorMode)
        {
            if (root == null || staticRoot == null) { Build(map, editorMode); return; }
            if (theme == null) LoadTheme();
            if (roadRoot != null) Destroy(roadRoot.gameObject);
            roadRoot = NewChild("Roads");
            BuildRoads(map, editorMode);
        }

        /// <summary>
        /// Objetos con altura (edificios, farolas, señales, árboles…): van por encima de los vehículos del suelo y por
        /// debajo de los puentes; entre ellos, delante el que está más abajo en la pantalla (como en 3D). Así una farola
        /// puede quedar delante o detrás de un edificio según dónde esté.
        /// </summary>
        public const int TallOrder = 25;

        // Editor: lo que se dibujó de cada objeto, para moverlo mientras se arrastra sin volver a dibujar todo el mapa.
        readonly Dictionary<DecorItem, List<Transform>> decorParts = new Dictionary<DecorItem, List<Transform>>();

        /// <summary>Desplaza lo dibujado de un objeto (al soltarlo se vuelve a dibujar bien). False si no se puede.</summary>
        public bool MoveDecorParts(DecorItem d, Vector2 delta)
        {
            List<Transform> parts;
            if (d == null || !decorParts.TryGetValue(d, out parts) || parts.Count == 0) return false;
            foreach (var t in parts)
            {
                if (t == null) return false;
                t.localPosition += new Vector3(delta.x, delta.y, 0f);
            }
            return true;
        }

        static bool IsTall(DecorType t)
        {
            switch (t)
            {
                case DecorType.Building: case DecorType.Lamp: case DecorType.FloodLight: case DecorType.TrafficSign: case DecorType.RoadSign:
                case DecorType.Tree: case DecorType.BusShelter: case DecorType.PhonePole: case DecorType.RailSign: case DecorType.Fence:
                case DecorType.WallSticker:   // pegada a la pared: delante del edificio (o detrás, si va en la pared de atrás)
                    return true;
            }
            return false;
        }

        /// <summary>Mete en un grupo ordenado lo que dibujó el objeto (menos sombras y luces de noche, que van aparte).</summary>
        void GroupTall(int before, DecorItem d)
        {
            int after = staticRoot.childCount;
            if (after <= before) return;
            var g = new GameObject("Tall").transform;
            g.SetParent(staticRoot, false);
            g.localPosition = new Vector3(0f, 0f, d.y * 0.001f + d.x * 0.000001f);   // más arriba en pantalla = más lejos = detrás
            var sg = g.gameObject.AddComponent<UnityEngine.Rendering.SortingGroup>();
            sg.sortingOrder = TallOrder;
            var move = new List<Transform>();
            for (int i = before; i < after; i++)
            {
                var c = staticRoot.GetChild(i);
                var rend = c.GetComponent<Renderer>();
                if (rend == null) continue;
                if (rend.sortingOrder <= 3 || rend.sortingOrder >= NightOverlayOrder) continue;   // sombras y brillos de noche
                move.Add(c);
            }
            foreach (var c in move) c.SetParent(g, true);
            if (g.childCount == 0) Destroy(g.gameObject);
        }

        Transform NewChild(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            return t;
        }

        // ---------------- Decoración ----------------

        void DrawDecor(DecorItem d, MapData map)
        {
            Vector2 p = d.Pos;
            Transform parent = staticRoot;
            var tree1 = SpriteFactory.Hex(theme.tree1);
            var tree2 = SpriteFactory.Hex(theme.tree2);
            if (DrawDecorPoly(d)) return;                       // zonas, caminos y pegatinas de pared
            if (map.IsLaneMap && DrawDecorIC2(d, map)) return;   // con las opciones del original
            if (DrawDecorExt(d, map)) return;
            float k = (1f + 0.08f * (d.variant % 4)) * d.size;
            switch (d.type)
            {
                case DecorType.Building:
                    {
                        float size = 1.25f * k;
                        Color wall = SpriteFactory.Hex(BuildingColors[Mathf.Abs(d.variant) % BuildingColors.Length]);
                        RegShadow(SpriteFactory.Make("Shadow", parent, SpriteFactory.Square, new Color(0, 0, 0, 0.32f), p + new Vector2(0.16f, -0.16f), Vector2.one * size, 3), p);
                        SpriteFactory.Make("Building", parent, SpriteFactory.Square, Color.Lerp(wall, Color.black, 0.25f), p, Vector2.one * size, 4);
                        // Tejado a dos aguas: dos mitades con distinta luz y una cumbrera oscura.
                        SpriteFactory.Make("RoofA", parent, SpriteFactory.Square, Color.Lerp(wall, Color.white, 0.12f), p + new Vector2(0f, size * 0.17f), new Vector2(size * 0.92f, size * 0.44f), 5);
                        SpriteFactory.Make("RoofB", parent, SpriteFactory.Square, Color.Lerp(wall, Color.black, 0.12f), p + new Vector2(0f, -size * 0.17f), new Vector2(size * 0.92f, size * 0.44f), 5);
                        SpriteFactory.Make("Ridge", parent, SpriteFactory.Square, new Color(0.15f, 0.15f, 0.2f, 0.55f), p, new Vector2(size * 0.92f, 0.03f), 6);
                        break;
                    }
                case DecorType.Tree:
                    RegShadow(SpriteFactory.Make("Shadow", parent, SpriteFactory.Circle, new Color(0, 0, 0, 0.3f), p + new Vector2(0.14f, -0.14f), Vector2.one * 0.9f * k, 3), p);
                    SpriteFactory.Make("Tree", parent, SpriteFactory.Circle, Color.Lerp(tree1, Color.black, 0.15f), p, Vector2.one * 0.86f * k, 4);
                    SpriteFactory.Make("Leaves", parent, SpriteFactory.Circle, tree1, p + new Vector2(-0.05f, 0.05f), Vector2.one * 0.6f * k, 5);
                    SpriteFactory.Make("Top", parent, SpriteFactory.Circle, tree2, p + new Vector2(-0.1f, 0.1f), Vector2.one * 0.32f * k, 6);
                    break;
                case DecorType.Lamp:
                    DrawStreetLamp(parent, d, map, p);
                    break;
                case DecorType.ParkedCar:
                    {
                        float ang = d.rot != 0f ? d.rot : map.RoadAngleNear(p);
                        string[] cols = { "#C9524B", "#4F7FCB", "#E8E8E8", "#3A3D46", "#D8A93B", "#4E9A66" };
                        Color body = SpriteFactory.Hex(cols[Mathf.Abs(d.variant) % cols.Length]);
                        RegShadow(SpriteFactory.Make("CarShadow", parent, SpriteFactory.Square, new Color(0, 0, 0, 0.28f), p + new Vector2(0.03f, -0.04f), new Vector2(0.5f, 0.27f), 3, ang), p);
                        SpriteFactory.Make("Car", parent, SpriteFactory.Square, body, p, new Vector2(0.48f, 0.26f), 4, ang);
                        SpriteFactory.Make("CarRoof", parent, SpriteFactory.Square, Color.Lerp(body, Color.white, 0.18f), p, new Vector2(0.18f, 0.22f), 5, ang);
                        SpriteFactory.Make("CarGlass", parent, SpriteFactory.Square, new Color(0.1f, 0.14f, 0.2f, 0.85f), p + FromAngle(ang) * 0.12f, new Vector2(0.09f, 0.2f), 5, ang);
                        break;
                    }
                case DecorType.Fence:
                    {
                        float ang = d.rot != 0f ? d.rot : map.RoadAngleNear(p);
                        RegShadow(SpriteFactory.Make("FenceShadow", parent, SpriteFactory.Square, new Color(0, 0, 0, 0.22f), p + new Vector2(0.03f, -0.03f), new Vector2(0.9f, 0.05f), 3, ang), p);
                        SpriteFactory.Make("Fence", parent, SpriteFactory.Square, new Color(0.45f, 0.33f, 0.22f), p, new Vector2(0.9f, 0.05f), 4, ang);
                        for (int i = -2; i <= 2; i++)
                            SpriteFactory.Make("FencePost", parent, SpriteFactory.Square, new Color(0.38f, 0.28f, 0.19f), p + FromAngle(ang) * (i * 0.2f), new Vector2(0.04f, 0.1f), 5, ang);
                        break;
                    }
                case DecorType.Hedge:
                    {
                        float ang = d.rot != 0f ? d.rot : map.RoadAngleNear(p);
                        // Largo y ancho propios (setos importados: semilargo x2, semiancho y2) o los de siempre.
                        float hl = d.x2 > 0.05f ? d.x2 * 2f : 0.95f * d.size, hw = d.y2 > 0.05f ? d.y2 * 2f : 0.22f;
                        RegShadow(SpriteFactory.Make("HedgeShadow", parent, SpriteFactory.Square, new Color(0, 0, 0, 0.25f), p + new Vector2(0.05f, -0.05f), new Vector2(hl, hw), 3, ang), p);
                        SpriteFactory.Make("Hedge", parent, SpriteFactory.Square, Color.Lerp(tree1, Color.black, 0.2f), p, new Vector2(hl, hw), 4, ang);
                        SpriteFactory.Make("HedgeTop", parent, SpriteFactory.Square, tree1, p, new Vector2(hl * 0.95f, hw * 0.64f), 5, ang);
                        break;
                    }
                case DecorType.Smoke:
                    {
                        SpriteFactory.Make("SmokeBase", parent, SpriteFactory.Circle, new Color(0.15f, 0.15f, 0.17f, 0.35f), p, Vector2.one * 0.22f, 5);
                        var sgo = new GameObject("Smoke");
                        sgo.transform.SetParent(parent, false);
                        sgo.transform.position = new Vector3(p.x, p.y, 0f);
                        sgo.AddComponent<SmokeFx>();
                        break;
                    }
                default:
                    SpriteFactory.Make("Park", parent, SpriteFactory.Square, SpriteFactory.Hex(theme.park), p, Vector2.one * 1.5f * d.size, 3);
                    SpriteFactory.Make("ParkTree", parent, SpriteFactory.Circle, tree1, p + new Vector2(-0.3f, 0.24f), Vector2.one * 0.45f, 4);
                    SpriteFactory.Make("ParkTree", parent, SpriteFactory.Circle, tree1, p + new Vector2(0.3f, -0.27f), Vector2.one * 0.45f, 4);
                    break;
            }
        }

        // ---------------- Red vial ----------------

        bool curAutoArrows, curAutoStopLines, curEditor;
        MapData curMap;

        void BuildRoads(MapData map, bool editorMode)
        {
            laneDraw = false;
            curAutoArrows = map.autoArrows; curAutoStopLines = map.autoStopLines; curEditor = editorMode; curMap = map;
            var net = new RoadNetworkManager();
            net.Build(map);
            Net = net;
            if (net.LaneMode)
            {
                BuildLaneRoads(map, net, editorMode);
                builtMap = map;
                DrawPointsAndButtons(map, net, editorMode);
                DrawLaneSigns(map, net, editorMode);
                return;
            }

            const int L = 3;
            var curbB = new MeshBatch[L]; var roadB = new MeshBatch[L]; var islandB = new MeshBatch[L];
            var marksB = new MeshBatch[L]; var shadowB = new MeshBatch[L];
            for (int i = 0; i < L; i++)
            {
                curbB[i] = new MeshBatch(); roadB[i] = new MeshBatch(); islandB[i] = new MeshBatch();
                marksB[i] = new MeshBatch(); shadowB[i] = new MeshBatch();
            }
            Color shadowCol = new Color(0f, 0f, 0f, 0.32f);

            // 1) Aceras y asfalto (por capas: las capas altas se dibujan encima, con sombra).
            foreach (var r in net.roads)
            {
                if (r.data.hidden && !editorMode) continue;
                int ly = Mathf.Clamp(r.data.layer, 0, L - 1);
                Color sw = sidewalk, asp = asphalt;
                if (r.data.color > 0 && r.data.color < RoadColors.Length) asp = SpriteFactory.Hex(RoadColors[r.data.color]);
                if (r.data.pedestrian) asp = Color.Lerp(sidewalk, Color.white, 0.18f);
                if (r.data.hidden) { sw.a = 0.3f; asp.a = 0.3f; }
                float curbW = r.data.sidewalk < 0f ? CurbWidth : r.data.sidewalk;
                if (curbW > 0.005f) curbB[ly].Strip(r.center.OffsetPts(-(r.halfWidth + curbW)), r.center.OffsetPts(r.halfWidth + curbW), sw);
                roadB[ly].Strip(r.center.OffsetPts(-r.halfWidth), r.center.OffsetPts(r.halfWidth), asp);
                float med = r.data.MedianWidth;
                if (med > 0.01f && r.length - r.trimA - r.trimB > 0.3f)
                {
                    // Mediana elevada entre los dos sentidos (con bordillo).
                    var msub = r.center.Sub(r.trimA + 0.1f, r.length - r.trimB - 0.1f);
                    float mu = (r.data.lanesBA - r.data.lanesAB) * 0.5f * RoadNetworkManager.LaneWidth;
                    Color mc = r.data.medianType == 0 ? SpriteFactory.Hex(theme.park) : (r.data.medianType == 1 ? sidewalk : SpriteFactory.Hex("#F4C20D"));
                    islandB[ly].Strip(msub.OffsetPts(mu - med * 0.5f), msub.OffsetPts(mu + med * 0.5f), Color.Lerp(mc, Color.black, 0.3f));
                    islandB[ly].Strip(msub.OffsetPts(mu - med * 0.5f + 0.03f), msub.OffsetPts(mu + med * 0.5f - 0.03f), mc);
                    if (r.data.medianType == 2)
                    {
                        var mline = new Polyline(msub.OffsetPts(mu));
                        for (float ms = 0.1f; ms < mline.Length - 0.1f; ms += 0.36f)
                            islandB[ly].Rect(mline.PointAt(ms), mline.TangentAt(ms), 0.16f, Mathf.Max(0.04f, med - 0.06f), SpriteFactory.Hex("#2B2B2E"));
                    }
                }
                if (ly > 0)
                {
                    var off = new Vector2(0.1f, -0.14f) * ly;
                    var la = r.center.OffsetPts(-(r.halfWidth + CurbWidth));
                    var lb = r.center.OffsetPts(r.halfWidth + CurbWidth);
                    for (int i = 0; i < la.Length; i++) { la[i] += off; lb[i] += off; }
                    shadowB[ly].Strip(la, lb, shadowCol);
                }
            }
            foreach (var n in net.nodes)
            {
                int ly = Mathf.Clamp(NodeLayer(n), 0, L - 1);
                if (n.roundabout)
                {
                    curbB[ly].Disc(n.pos, n.radius + RoadNetworkManager.RingHalfWidth + CurbWidth, sidewalk, 40);
                    roadB[ly].Annulus(n.pos, n.radius - RoadNetworkManager.RingHalfWidth, n.radius + RoadNetworkManager.RingHalfWidth, asphalt, 48);
                    islandB[ly].Disc(n.pos, n.radius - RoadNetworkManager.RingHalfWidth + 0.02f, sidewalk, 40);
                    islandB[ly].Disc(n.pos, Mathf.Max(0.1f, n.radius - RoadNetworkManager.RingHalfWidth - 0.1f), SpriteFactory.Hex(theme.park), 36);
                    continue;
                }
                if (n.approaches.Count < 2) continue;
                Fan(curbB[ly], n, CurbWidth, sidewalk);
                Fan(roadB[ly], n, 0f, asphalt);
            }

            // 2) Marcas viales.
            foreach (var r in net.roads) if (!r.data.hidden && !r.data.noMarks) DrawRoadMarkings(marksB[Mathf.Clamp(r.data.layer, 0, L - 1)], r);
            foreach (var n in net.nodes) DrawNodeMarkings(marksB[Mathf.Clamp(NodeLayer(n), 0, L - 1)], n);
            foreach (var cw in net.crosswalks)
            {
                int cl = 0;
                foreach (var r in net.roads) if (r.data.id == cw.roadId) { cl = Mathf.Clamp(r.data.layer, 0, L - 1); break; }
                DrawZebra(marksB[cl], cw);
            }

            // Flechas funcionales colocadas a mano.
            foreach (var ar in map.arrows)
            {
                foreach (var r in net.roads)
                {
                    if (r.data.id != ar.road) continue;
                    if (r.data.hidden && !editorMode) break;
                    var lanes = ar.forward ? r.lanesAB : r.lanesBA;
                    if (ar.lane < 0 || ar.lane >= lanes.Length) break;
                    var ln = lanes[ar.lane];
                    float sa = Mathf.Clamp01(ar.t) * ln.Length;
                    LaneArrow(marksB[Mathf.Clamp(r.data.layer, 0, L - 1)], ln.path.PointAt(sa), ln.path.TangentAt(sa), ar.moves, ArrowCol, ar.size, ar.style);
                    break;
                }
            }

            var over = new MeshBatch();
            foreach (var r in net.roads) if (!(r.data.hidden && !editorMode)) DrawRoadProps(marksB[Mathf.Clamp(r.data.layer, 0, L - 1)], over, r);

            int[] oCurb = { -10, 24, 34 }, oRoad = { 1, 25, 35 }, oIsl = { 2, 26, 36 }, oMarks = { 3, 27, 37 }, oShadow = { 0, 23, 33 };
            for (int i = 0; i < L; i++)
            {
                if (i > 0) shadowB[i].Build(roadRoot, "Shadow" + i, oShadow[i]);
                curbB[i].Build(roadRoot, "Curbs" + i, oCurb[i]);
                roadB[i].Build(roadRoot, "Asphalt" + i, oRoad[i]);
                islandB[i].Build(roadRoot, "Islands" + i, oIsl[i]);
                marksB[i].Build(roadRoot, "Markings" + i, oMarks[i]);
            }
            over.Build(roadRoot, "Cables", 35);

            builtMap = map;
            DrawPointsAndButtons(map, net, editorMode);

            // 3) Señales y portales.
            DrawSigns(map, net, editorMode);
            DrawPortals(net);
        }

        static int NodeLayer(NetNode n)
        {
            int ly = 0;
            foreach (var ap in n.approaches) if (ap.road != null && ap.road.data.layer > ly) ly = ap.road.data.layer;
            return ly;
        }

        // Polígono del cuadro del cruce con las esquinas redondeadas (curva de Bézier entre los bordes de calles contiguas).
        void Fan(MeshBatch m, NetNode n, float margin, Color col)
        {
            var apps = n.approaches;
            int c = apps.Count;
            var poly = new List<Vector2>();
            for (int k = 0; k < c; k++)
            {
                var a = apps[k];
                var b = apps[(k + 1) % c];
                Vector2 pa = (a.edgeLeft + a.edgeRight) * 0.5f;
                Vector2 pb = (b.edgeLeft + b.edgeRight) * 0.5f;
                float hwA = a.halfWidth + margin, hwB = b.halfWidth + margin;
                Vector2 aRight = pa + Geo.Right(a.edgeDir) * hwA;
                Vector2 aLeft = pa + Geo.Left(a.edgeDir) * hwA;
                Vector2 bRight = pb + Geo.Right(b.edgeDir) * hwB;
                if (k == 0) poly.Add(aRight);
                poly.Add(aLeft);

                // Esquina entre el borde izquierdo de a y el derecho de b.
                Vector2 ctrl = (aLeft + bRight) * 0.5f;
                float cr = Geo.Cross(a.edgeDir, b.edgeDir);
                if (Mathf.Abs(cr) > 0.08f)
                {
                    float t = -Geo.Cross(bRight - aLeft, b.edgeDir) / cr;
                    if (t > 0f)
                    {
                        Vector2 q = aLeft - a.edgeDir * t;
                        if ((q - ctrl).magnitude < 3f) ctrl = q;
                    }
                }
                float len = Vector2.Distance(aLeft, bRight);
                int steps = Mathf.Clamp(Mathf.CeilToInt(len / 0.12f), 3, 16);
                for (int i = 1; i < steps; i++) poly.Add(Geo.Quad(aLeft, ctrl, bRight, i / (float)steps));
                poly.Add(bRight);
            }
            // El primer punto de cada tramo se repite como "bRight" del anterior; se eliminan duplicados consecutivos.
            var clean = new List<Vector2>();
            for (int i = 0; i < poly.Count; i++)
            {
                if (clean.Count > 0 && (clean[clean.Count - 1] - poly[i]).sqrMagnitude < 1e-6f) continue;
                clean.Add(poly[i]);
            }
            if (clean.Count > 1 && (clean[0] - clean[clean.Count - 1]).sqrMagnitude < 1e-6f) clean.RemoveAt(clean.Count - 1);
            if (clean.Count >= 3) m.Fan(n.pos, clean, col);
        }

        // ---------------- Marcas viales ----------------

        // Barandillado y postes con cables eléctricos.
        static readonly string[] RailColors = { "#B8C2D1", "#F2C230", "#F4F4F4", "#3A3D44" };
        bool laneDraw;

        void DrawRoadProps(MeshBatch marks, MeshBatch over, NetRoad r)
        {
            if (!r.data.rails && !r.data.cables) return;
            float s0 = r.trimA, s1 = r.length - r.trimB;
            if (s1 - s0 < 0.4f) return;
            Polyline sub = r.center.Sub(s0, s1);
            if (laneDraw)
            {
                // Modelo por carriles: barandilla en los dos bordes del carril y postes con cables a un lado.
                float hw = r.halfWidth;
                if (r.data.rails)
                {
                    var rc = SpriteFactory.Hex(RailColors[Mathf.Clamp(r.data.railColor, 0, RailColors.Length - 1)]);
                    var dk = Color.Lerp(rc, Color.black, 0.55f);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float o = side * (hw - 0.025f);
                        marks.Line(sub, o, 0.045f, rc);
                        for (float s = 0.15f; s < sub.Length; s += 0.6f)
                            marks.Disc(sub.PointAt(s) + Geo.Right(sub.TangentAt(s)) * o, 0.025f, dk, 8);
                    }
                }
                if (r.data.cables)
                {
                    float po = (r.data.cableSide == 1 ? -1f : 1f) * (hw + 0.14f);
                    var wire = new Color(0.1f, 0.1f, 0.12f, 0.9f);
                    over.Line(sub, po - 0.04f, 0.012f, wire);
                    over.Line(sub, po + 0.04f, 0.012f, wire);
                    for (float s = 0.4f; s < sub.Length; s += 2.4f)
                    {
                        Vector2 t = sub.TangentAt(s);
                        Vector2 p = sub.PointAt(s) + Geo.Right(t) * po;
                        over.Disc(p, 0.06f, new Color(0.36f, 0.26f, 0.18f, 1f), 10);
                        over.Rect(p, Geo.Right(t), 0.2f, 0.025f, new Color(0.3f, 0.22f, 0.15f, 1f));
                    }
                }
                return;
            }

            if (r.data.rails)
            {
                var steel = new Color(0.72f, 0.76f, 0.82f, 1f);
                var dark = new Color(0.2f, 0.22f, 0.26f, 1f);
                for (int side = -1; side <= 1; side += 2)
                {
                    float o = side * (r.halfWidth + CurbWidth - 0.04f);
                    marks.Line(sub, o, 0.05f, steel);
                    marks.Line(sub, o, 0.016f, dark);
                    for (float s = 0.2f; s < sub.Length; s += 0.8f)
                        marks.Disc(sub.PointAt(s) + Geo.Right(sub.TangentAt(s)) * o, 0.028f, dark, 8);
                }
            }

            if (r.data.cables)
            {
                float po = r.halfWidth + CurbWidth + 0.12f;
                var wire = new Color(0.1f, 0.1f, 0.12f, 0.9f);
                over.Line(sub, po - 0.05f, 0.012f, wire);
                over.Line(sub, po + 0.05f, 0.012f, wire);
                for (float s = 0.4f; s < sub.Length; s += 2.8f)
                {
                    Vector2 t = sub.TangentAt(s);
                    Vector2 p = sub.PointAt(s) + Geo.Right(t) * po;
                    over.Disc(p, 0.065f, new Color(0.36f, 0.26f, 0.18f, 1f), 10);
                    over.Rect(p, Geo.Right(t), 0.22f, 0.025f, new Color(0.3f, 0.22f, 0.15f, 1f));
                }
            }
        }

        void DrawRoadMarkings(MeshBatch m, NetRoad r)
        {
            float s0 = r.trimA, s1 = r.length - r.trimB;
            if (s1 - s0 < 0.15f) return;
            Polyline sub = r.center.Sub(s0, s1);
            float len = sub.Length;
            int n1 = r.data.lanesAB, n2 = r.data.lanesBA;
            float u0 = (n2 - n1) * 0.5f * RoadNetworkManager.LaneWidth;
            float lw = RoadNetworkManager.LaneWidth;
            float hw = r.halfWidth;

            // Bordes.
            m.Line(sub, -(hw - 0.05f), 0.03f, EdgeLine);
            m.Line(sub, hw - 0.05f, 0.03f, EdgeLine);

            float medW = r.data.MedianWidth;
            // Línea central (no hay si la calle tiene mediana).
            if (n1 > 0 && n2 > 0 && medW <= 0.01f)
            {
                if (n1 == 1 && n2 == 1) m.Dashes(sub, 0f, len, u0, 0.035f, 0.32f, 0.26f, Paint);
                else
                {
                    m.Line(sub, 0f, len, u0 - 0.025f, 0.028f, Paint);
                    m.Line(sub, 0f, len, u0 + 0.025f, 0.028f, Paint);
                }
            }
            // Líneas entre carriles del mismo sentido.
            for (int i = 1; i < n1; i++) m.Dashes(sub, 0f, len, u0 + medW * 0.5f + i * lw, 0.028f, 0.26f, 0.24f, new Color(White.r, White.g, White.b, 0.85f));
            for (int j = 1; j < n2; j++) m.Dashes(sub, 0f, len, u0 - medW * 0.5f - j * lw, 0.028f, 0.26f, 0.24f, new Color(White.r, White.g, White.b, 0.85f));

            // Flechas de sentido único repartidas por la calle (en el editor siempre, para orientarse; al jugar solo con flechas automáticas).
            if ((n1 == 0) != (n2 == 0) && (curAutoArrows || curEditor))
            {
                var lanes = n1 > 0 ? r.lanesAB : r.lanesBA;
                foreach (var lane in lanes)
                {
                    float ll = lane.Length;
                    for (float s = 0.9f; s < ll - 0.7f; s += 2.6f)
                    {
                        Vector2 t = lane.path.TangentAt(s);
                        ArrowMesh(m, lane.path.PointAt(s) - t * 0.14f, t, 0.3f, new Color(1f, 0.93f, 0.55f, 0.95f), true);
                    }
                }
            }
        }

        void DrawNodeMarkings(MeshBatch m, NetNode n)
        {
            // Líneas de parada, "ceda" y flechas de giro al llegar a un cruce.
            if (n.IsJunction)
            {
                foreach (var a in n.approaches)
                {
                    for (int i = 0; i < a.inLanes.Count; i++)
                    {
                        var lane = a.inLanes[i];
                        float sp = Mathf.Clamp(lane.stopS, 0f, lane.Length);
                        Vector2 p = lane.path.PointAt(sp);
                        Vector2 t = lane.path.TangentAt(sp);
                        bool stopSign = a.hasRule && a.rule == SignType.Stop;
                        bool giveWay = (a.hasRule && a.rule == SignType.Yield) || n.roundabout;
                        float lw = RoadNetworkManager.LaneWidth;
                        if (!curAutoStopLines) { }
                        else if (giveWay)
                        {
                            Vector2 rt = Geo.Right(t);
                            for (int k = -1; k <= 1; k += 2)
                            {
                                Vector2 c = p + rt * (k * lw * 0.22f);
                                m.Tri(c + rt * 0.065f, c - rt * 0.065f, c - t * 0.13f, White);
                            }
                        }
                        else
                        {
                            m.Rect(p, t, stopSign ? 0.1f : 0.06f, lw - 0.03f, stopSign ? new Color(1f, 0.88f, 0.88f, 1f) : White);
                        }

                        if (curAutoArrows && !n.roundabout && lane.moves != 0 && lane.Length > 1.4f && n.approaches.Count >= 3
                            && (curMap == null || curMap.LaneArrowMoves(a.road.data.id, a.atB, i) == 0))
                        {
                            float sa = Mathf.Max(0.3f, sp - (a.hasCrosswalk ? 0.2f : 0.42f));
                            LaneArrow(m, lane.path.PointAt(sa), lane.path.TangentAt(sa), lane.moves, ArrowCol);
                        }
                    }
                }
            }

            // Sentido de la rotonda.
            foreach (var seg in n.ringSegs)
            {
                if (seg.Length < 0.9f) continue;
                float s = seg.Length * 0.5f;
                Vector2 t = seg.path.TangentAt(s);
                ArrowMesh(m, seg.path.PointAt(s) - t * 0.09f, t, 0.18f, new Color(1f, 1f, 1f, 0.7f), true);
            }
        }

        static void ArrowMesh(MeshBatch m, Vector2 tail, Vector2 dir, float len, Color col, bool head)
        {
            dir.Normalize();
            Vector2 right = Geo.Right(dir);
            float shaft = head ? len * 0.6f : len;
            float headLen = len * 0.4f;
            float w = Mathf.Max(0.03f, len * 0.13f);
            m.Rect(tail + dir * (shaft * 0.5f), dir, shaft, w, col);
            if (head)
                m.Tri(tail + dir * shaft + right * (headLen * 0.6f), tail + dir * shaft - right * (headLen * 0.6f), tail + dir * (shaft + headLen), col);
        }

        /// <summary>Punta de flecha de pintura vial: triángulo con el fondo hundido (dos «alas»), como las del asfalto.</summary>
        static void ArrowHead(MeshBatch m, Vector2 tip, Vector2 dir, float len, float width, float notch, Color col)
        {
            dir.Normalize();
            Vector2 r = Geo.Right(dir);
            Vector2 bc = tip - dir * len;
            Vector2 bl = bc - r * (width * 0.5f), br = bc + r * (width * 0.5f), nt = bc + dir * notch;
            m.Tri(tip, bl, nt, col);
            m.Tri(tip, nt, br, col);
        }

        static void ArrowStem(MeshBatch m, Vector2 a, Vector2 b, float w, Color col)
        {
            float len = (b - a).magnitude;
            if (len > 1e-4f) m.Rect((a + b) * 0.5f, b - a, len + 0.004f, w, col);
        }

        /// <summary>
        /// Flecha pintada como el original (medida en sus capturas, carril = 0,5): mide 0,8 de largo y el palo 0,045 de
        /// ancho. Recto: punta de lanza estrecha (0,28 × 0,18) con las alas hacia atrás. Un solo giro: el palo se desvía en
        /// diagonal suave y acaba en una punta estrecha. Giros a los lados: «corbatín» con una punta lateral de alas largas
        /// por cada lado (al final si no hay recto; cerca de la cola si también hay recto). Cabe en el carril (±0,15).
        /// </summary>
        static void LaneArrow(MeshBatch m, Vector2 pos, Vector2 t, int moves, Color col, float k = 1f, int style = 0)
        {
            bool l = (moves & 1) != 0, s = (moves & 2) != 0, r = (moves & 4) != 0;
            if (!l && !s && !r) return;
            t.Normalize();
            Vector2 rt = Geo.Right(t);
            System.Func<float, float, Vector2> Q = (lat, fwd) => pos + (rt * lat + t * fwd) * k;
            const float sw = 0.045f;
            if (s)
            {
                ArrowStem(m, Q(0f, -0.4f), Q(0f, 0.4f - 0.28f + 0.085f), sw * k, col);
                ArrowHead(m, Q(0f, 0.4f), t, 0.28f * k, 0.18f * k, 0.08f * k, col);
                // Giros junto al recto: una sola punta lateral por lado, unida al palo (como una rama) y algo más
                // pequeña, a media altura: se lee como UNA flecha con ramas, no como dos flechas encimadas.
                System.Func<float, float, Vector2> Qb = (lat, fwd) => Q(lat * 0.78f, -0.06f + fwd * 0.78f);
                if (l) SideHead(m, Qb, -1f, 0f, col);
                if (r) SideHead(m, Qb, 1f, 0f, col);
                return;
            }
            if (l && r)
            {
                ArrowStem(m, Q(0f, -0.4f), Q(0f, 0.2f - 0.165f), sw * k, col);
                SideHead(m, Q, -1f, 0.2f, col);
                SideHead(m, Q, 1f, 0.2f, col);
                return;
            }
            float sg = r ? 1f : -1f;
            if (style != 1)
            {
                // Giro (izquierda o derecha) con el mismo estilo que «izquierda y derecha»: palo recto y, al final, la
                // punta lateral de alas largas hacia ese lado. Corrida hacia el otro lado para quedar centrada en el carril.
                float x0 = -0.07f * sg;
                System.Func<float, float, Vector2> Qs = (lat, fwd) => Q(lat + x0, fwd);
                ArrowStem(m, Qs(0f, -0.4f), Qs(0f, 0.06f), sw * k, col);
                SideHead(m, Qs, sg, 0.2f, col, true);
                return;
            }
            // Curva suave: recto hasta poco antes de la mitad, desvío de 20° y punta estrecha a 12° hacia el lado.
            Vector2 b0 = Q(0f, -0.08f);
            ArrowStem(m, Q(0f, -0.4f), b0, sw * k, col);
            Vector2 d1 = (t + rt * (sg * Mathf.Tan(20f * Mathf.Deg2Rad))).normalized;
            Vector2 b1 = b0 + d1 * (0.14f * k);
            ArrowStem(m, b0 - d1 * (0.01f * k), b1, sw * k, col);
            Vector2 d2 = (t + rt * (sg * Mathf.Tan(12f * Mathf.Deg2Rad))).normalized;
            ArrowStem(m, b1 - d2 * (0.01f * k), b1 + d2 * (0.06f * k), sw * k, col);
            ArrowHead(m, b1 + d2 * (0.3f * k), d2, 0.3f * k, 0.12f * k, 0.06f * k, col);
        }

        /// <summary>
        /// Media «corbatín» del original: brazo en «^» desde el palo y punta lateral con un ala hacia atrás y otra hacia
        /// delante (como un arpón). side = -1 izquierda, +1 derecha; c = posición a lo largo de la flecha.
        /// </summary>
        static void SideHead(MeshBatch m, System.Func<float, float, Vector2> Q, float side, float c, Color col, bool alone = false)
        {
            System.Func<float, float, Vector2> P = (lat, fwd) => Q(lat * side, c + fwd);
            Vector2 A = P(0f, -0.175f), B = P(0.085f, -0.098f), C = P(0.098f, -0.196f), P1 = P(0.135f, -0.1f);
            Vector2 D = P(0.144f, 0f), P2 = P(0.135f, 0.1f), E = P(0.098f, 0.196f), F = P(0.09f, 0.072f), G = P(0f, -0.021f);
            if (alone)
            {
                // Punta sola (giro a un lado): el brazo se alarga hasta el borde de fuera del palo, así el palo sigue recto
                // y se dobla hacia la punta sin escalón.
                A = P(-0.0225f, -0.195f);
                G = P(-0.0225f, -0.044f);
            }
            m.Quad(A, B, F, G, col);
            m.Tri(B, C, P1, col);
            m.Tri(B, P1, D, col);
            m.Tri(B, D, F, col);
            m.Tri(F, D, P2, col);
            m.Tri(F, P2, E, col);
        }

        static void DrawZebra(MeshBatch m, Crosswalk cw)
        {
            float span = cw.halfWidth * 2f - 0.04f;
            int count = Mathf.Max(3, Mathf.FloorToInt(span / 0.2f));
            float step = span / count;
            for (int i = 0; i < count; i++)
            {
                float off = -span * 0.5f + step * (i + 0.5f);
                m.Rect(cw.center + cw.across * off, cw.axis, Crosswalk.ZoneHalf * 2f, step * 0.52f, new Color(White.r, White.g, White.b, 0.9f));
            }
        }

        // ---------------- Portales de entrada y salida ----------------

        void DrawPortals(RoadNetworkManager net)
        {
            foreach (var n in net.nodes)
            {
                if (!n.entry && !n.exit) continue;
                Color col = n.entry && n.exit ? SpriteFactory.Hex("#4C8DF6") : n.entry ? SpriteFactory.Hex("#3FB950") : SpriteFactory.Hex("#D62828");
                if (n.approaches.Count == 1)
                {
                    var a = n.approaches[0];
                    Vector2 dir = a.Outward;
                    float width = a.halfWidth * 2f + CurbWidth * 2f;
                    SpriteFactory.Make("Portal", roadRoot, SpriteFactory.Square, col, n.pos - dir * 0.04f,
                        new Vector2(0.1f, width), 6, Geo.Angle(dir));
                }
                else
                {
                    SpriteFactory.Make("PortalDot", roadRoot, SpriteFactory.Circle, col, n.pos, Vector2.one * 0.3f, 6);
                }
            }
        }

        // ---------------- Señales de tráfico ----------------

        void DrawSigns(MapData map, RoadNetworkManager net, bool editorMode)
        {
            var stack = new Dictionary<int, int>();
            foreach (var s in map.signs)
            {
                NetRoad nr;
                if (!net.roadById.TryGetValue(s.road, out nr)) continue;
                if (s.type == SignType.Light && !editorMode) continue;   // en juego se dibuja la franja luminosa
                // Como el original: al jugar, el ceda y el stop se ven solo por las marcas del suelo (sin el icono grande).
                if (!editorMode && map.IsLaneMap && (s.type == SignType.Yield || s.type == SignType.Stop)) continue;
                if (s.type == SignType.SpeedLimit)
                {
                    float sp = nr.length * 0.35f;
                    Vector2 t0 = nr.center.TangentAt(sp);
                    DrawSign(s.type, nr.center.PointAt(sp) + Geo.Right(t0) * (nr.halfWidth + 0.38f), Geo.Angle(t0));
                    continue;
                }
                Approach a = s.node == nr.data.a ? nr.appA : (s.node == nr.data.b ? nr.appB : null);
                if (a == null || a.inLanes.Count == 0) continue;
                var lane = a.inLanes[a.inLanes.Count - 1];
                int key = nr.data.id * 2 + (a.atB ? 1 : 0);
                int k;
                stack.TryGetValue(key, out k);
                stack[key] = k + 1;
                float pos = Mathf.Max(0.05f, lane.stopS - 0.12f - 0.36f * k);
                Vector2 t = lane.path.TangentAt(pos);
                Vector2 p = lane.path.PointAt(pos) + Geo.Right(t) * (RoadNetworkManager.LaneWidth * 0.5f + 0.4f);
                DrawSign(s.type, p, Geo.Angle(t));
            }
        }

        void DrawSign(SignType type, Vector2 pos, float travelAngle)
        {
            const float K = 1.5f;
            Color red = SpriteFactory.Hex("#D62828");
            Transform rt = roadRoot;
            switch (type)
            {
                case SignType.Stop:
                    SpriteFactory.Make("SignStop", rt, SpriteFactory.Octagon, red, pos, Vector2.one * 0.19f * K, 15);
                    SpriteFactory.Make("SignStopBar", rt, SpriteFactory.Square, Color.white, pos, new Vector2(0.12f, 0.035f) * K, 16, travelAngle + 90f);
                    break;
                case SignType.Yield:
                    SpriteFactory.Make("SignYield", rt, SpriteFactory.Triangle, red, pos, Vector2.one * 0.2f * K, 15, travelAngle + 180f);
                    SpriteFactory.Make("SignYieldIn", rt, SpriteFactory.Triangle, Color.white,
                        pos - new Vector2(Mathf.Cos((travelAngle + 180f) * Mathf.Deg2Rad), Mathf.Sin((travelAngle + 180f) * Mathf.Deg2Rad)) * 0.006f,
                        Vector2.one * 0.11f * K, 16, travelAngle + 180f);
                    break;
                case SignType.Priority:
                    SpriteFactory.Make("SignPrioBack", rt, SpriteFactory.Square, Color.white, pos, Vector2.one * 0.16f * K, 15, 45f);
                    SpriteFactory.Make("SignPrio", rt, SpriteFactory.Square, SpriteFactory.Hex("#F6C945"), pos, Vector2.one * 0.11f * K, 16, 45f);
                    break;
                case SignType.NoLeft:
                case SignType.NoRight:
                case SignType.NoStraight:
                    {
                        SpriteFactory.Make("SignBan", rt, SpriteFactory.Circle, Color.white, pos, Vector2.one * 0.19f * K, 15);
                        SpriteFactory.Make("SignBanRing", rt, SpriteFactory.Ring, red, pos, Vector2.one * 0.19f * K, 16);
                        float a = type == SignType.NoLeft ? travelAngle + 90f : (type == SignType.NoRight ? travelAngle - 90f : travelAngle);
                        DrawSignArrow(pos, a, 0.1f * K);
                        SpriteFactory.Make("SignBanSlash", rt, SpriteFactory.Square, red, pos, new Vector2(0.17f, 0.024f) * K, 18, 45f);
                        break;
                    }
                case SignType.Light:
                    SpriteFactory.Make("SignalBox", rt, SpriteFactory.Square, new Color(0.08f, 0.08f, 0.1f), pos, new Vector2(0.42f, 0.17f) * 1.2f, 15, travelAngle);
                    SpriteFactory.Make("SignalRed", rt, SpriteFactory.Circle, SpriteFactory.Hex("#E5534B"), pos + FromAngle(travelAngle) * 0.13f, Vector2.one * 0.1f * 1.2f, 16);
                    SpriteFactory.Make("SignalYellow", rt, SpriteFactory.Circle, SpriteFactory.Hex("#F6C945"), pos, Vector2.one * 0.1f * 1.2f, 16);
                    SpriteFactory.Make("SignalGreen", rt, SpriteFactory.Circle, SpriteFactory.Hex("#3FD16B"), pos - FromAngle(travelAngle) * 0.13f, Vector2.one * 0.1f * 1.2f, 16);
                    break;
                case SignType.SpeedLimit:
                    SpriteFactory.Make("SignSpeed", rt, SpriteFactory.Circle, Color.white, pos, Vector2.one * 0.19f * K, 15);
                    SpriteFactory.Make("SignSpeedRing", rt, SpriteFactory.Ring, red, pos, Vector2.one * 0.19f * K, 16);
                    SpriteFactory.Make("SignSpeedBar", rt, SpriteFactory.Square, new Color(0.1f, 0.1f, 0.12f), pos + new Vector2(-0.025f, 0f) * K, new Vector2(0.025f, 0.08f) * K, 17);
                    SpriteFactory.Make("SignSpeedBar", rt, SpriteFactory.Square, new Color(0.1f, 0.1f, 0.12f), pos + new Vector2(0.025f, 0f) * K, new Vector2(0.025f, 0.08f) * K, 17);
                    break;
            }
        }

        static Vector2 FromAngle(float deg)
        {
            return new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));
        }

        void DrawSignArrow(Vector2 pos, float angleDeg, float len)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            var dark = new Color(0.1f, 0.1f, 0.12f);
            SpriteFactory.Make("SignArrow", roadRoot, SpriteFactory.Square, dark, pos - dir * (len * 0.12f), new Vector2(len * 0.6f, 0.032f), 17, angleDeg);
            SpriteFactory.Make("SignArrowHead", roadRoot, SpriteFactory.Triangle, dark, pos + dir * (len * 0.3f), Vector2.one * (len * 0.5f), 17, angleDeg);
        }

        // ---------------- Cuadrícula del editor ----------------

        /// <summary>Cuadrícula opcional del editor (se activa en el botón Rejilla).</summary>
        public static bool ShowGridLines;
        public static float GridStepLines = 0.5f;
        public static bool GridDiagonals;

        void DrawGrid(MapData map)
        {
            var col = new Color(1f, 1f, 1f, 0.12f);
            var strong = new Color(1f, 1f, 1f, 0.22f);
            var batch = new MeshBatch();
            float step = Mathf.Max(0.05f, GridStepLines);
            // Nunca más de ~900 líneas por eje para no cargar el móvil: si la celda es muy pequeña se agrupan.
            while (Mathf.Max(map.width, map.height) / step > 900f) step *= 2f;
            if (GridDiagonals)
            {
                // Como el original: cuadros girados 45° (rombos). Líneas x+y = c y x−y = c.
                float d = step * 1.41421356f;
                Vector2 dirA = new Vector2(0.70710678f, -0.70710678f), dirB = new Vector2(0.70710678f, 0.70710678f);
                float W = map.width, H = map.height;
                for (float c = 0f; c <= W + H + 0.001f; c += d)
                {
                    float x0 = Mathf.Max(0f, c - H), x1 = Mathf.Min(W, c);
                    if (x1 - x0 < 0.001f) continue;
                    Vector2 p0 = new Vector2(x0, c - x0), p1 = new Vector2(x1, c - x1);
                    batch.Rect((p0 + p1) * 0.5f, dirA, (p1 - p0).magnitude, 0.012f, col);
                }
                for (float c = -Mathf.Floor(H / d) * d; c <= W + 0.001f; c += d)
                {
                    float x0 = Mathf.Max(0f, c), x1 = Mathf.Min(W, c + H);
                    if (x1 - x0 < 0.001f) continue;
                    Vector2 p0 = new Vector2(x0, x0 - c), p1 = new Vector2(x1, x1 - c);
                    batch.Rect((p0 + p1) * 0.5f, dirB, (p1 - p0).magnitude, 0.012f, col);
                }
            }
            else
            {
            int nx = Mathf.FloorToInt(map.width / step + 0.001f), ny = Mathf.FloorToInt(map.height / step + 0.001f);
            for (int i = 0; i <= nx; i++)
            {
                float x = i * step;
                bool major = Mathf.Abs(x - Mathf.Round(x)) < 0.001f && Mathf.RoundToInt(x) % 5 == 0;
                batch.Rect(new Vector2(x, map.height / 2f), Vector2.up, map.height, major ? 0.03f : 0.012f, major ? strong : col);
            }
            for (int i = 0; i <= ny; i++)
            {
                float y = i * step;
                bool major = Mathf.Abs(y - Mathf.Round(y)) < 0.001f && Mathf.RoundToInt(y) % 5 == 0;
                batch.Rect(new Vector2(map.width / 2f, y), Vector2.right, map.width, major ? 0.03f : 0.012f, major ? strong : col);
            }
            }
            batch.Rect(new Vector2(map.width / 2f, 0f), Vector2.right, map.width, 0.06f, strong);
            batch.Rect(new Vector2(map.width / 2f, map.height), Vector2.right, map.width, 0.06f, strong);
            batch.Rect(new Vector2(0f, map.height / 2f), Vector2.up, map.height, 0.06f, strong);
            batch.Rect(new Vector2(map.width, map.height / 2f), Vector2.up, map.height, 0.06f, strong);
            batch.Build(staticRoot, "Grid", map.IsLaneMap ? -110 : -18);
        }

        // ---------------- Semáforos ----------------

        public void BuildSignals(List<IntersectionController> intersections)
        {
            signals.Clear();
            if (signalRoot == null) signalRoot = NewChild("Signals");
            // Semáforos de carriles contiguos: un solo poste por grupo (como en la vida real).
            laneUnits.Clear();
            if (laneDraw)
            {
                var all = new List<LightUnit>();
                foreach (var ic in intersections)
                    foreach (var a in ic.node.approaches)
                    {
                        if (a.inLanes.Count == 0 || !ic.light.Has(a) || IsPedLight(a.inLanes)) continue;   // los peatonales van aparte
                        var u = new LightUnit { lanes = new List<RoadSeg>(a.inLanes), side = a.lightSide, k = Mathf.Clamp(a.lightSize, 0.6f, 1.8f), hidden = a.lightHidden };
                        all.Add(u); laneUnits[a] = u;
                    }
                if (Net != null) GroupLights(all, Net);
                else foreach (var u in all) { u.group = MakeLightGeom(u.lanes, u.side); u.pole = !u.hidden; u.groupK = u.k; }
            }
            foreach (var ic in intersections)
            {
                foreach (var a in ic.node.approaches)
                {
                    if (a.inLanes.Count == 0 || !ic.light.Has(a)) continue;
                    if (laneDraw) { BuildLaneSignal(ic, a); continue; }
                    int firstChild = signalRoot.childCount;
                    var lane = a.inLanes[a.inLanes.Count - 1];
                    float sp = Mathf.Clamp(lane.stopS - 0.05f, 0.05f, lane.Length);
                    Vector2 t = lane.path.TangentAt(sp);
                    float k = Mathf.Clamp(a.lightSize, 0.6f, 1.8f);
                    var sideLane = a.lightSide == 1 ? a.inLanes[0] : lane;
                    float spSide = Mathf.Clamp(sideLane.stopS - 0.05f, 0.05f, sideLane.Length);
                    Vector2 sideDir = a.lightSide == 1 ? Geo.Left(t) : Geo.Right(t);
                    Vector2 pos = sideLane.path.PointAt(spSide) + sideDir * (RoadNetworkManager.LaneWidth * 0.5f + 0.34f);
                    float tAng = Geo.Angle(t);
                    var housing = new Color(0.08f, 0.08f, 0.1f, 1f);
                    var pole = new Color(0.45f, 0.47f, 0.5f, 1f);
                    SpriteRenderer lr, ly, lg, lp = null;
                    Vector2 headPos = pos;
                    Vector2 along;   // dirección en la que se ordenan las luces (rojo → verde)
                    if (a.lightModel == 1)
                    {
                        // Horizontal en báculo: brazo desde el poste hasta encima de los carriles.
                        Vector2 over = Vector2.zero;
                        for (int li = 0; li < a.inLanes.Count; li++) over += a.inLanes[li].path.PointAt(Mathf.Clamp(a.inLanes[li].stopS - 0.05f, 0.05f, a.inLanes[li].Length));
                        over /= a.inLanes.Count;
                        SpriteFactory.Make("SignalPole", signalRoot, SpriteFactory.Circle, pole, pos, Vector2.one * 0.13f * k, 40);
                        Vector2 arm = over - pos;
                        SpriteFactory.Make("SignalArm", signalRoot, SpriteFactory.Square, pole, (pos + over) * 0.5f, new Vector2(arm.magnitude, 0.05f * k), 40, Geo.Angle(arm));
                        headPos = over;
                        along = sideDir;
                        SpriteFactory.Make("SignalHousing", signalRoot, SpriteFactory.Square, housing, headPos, new Vector2(0.17f, 0.46f) * k, 41, tAng);
                    }
                    else
                    {
                        // Vertical en poste lateral (también el de flechas y el que lleva peatonal).
                        SpriteFactory.Make("SignalPole", signalRoot, SpriteFactory.Circle, pole, pos - t * 0.18f * k, Vector2.one * 0.1f * k, 40);
                        along = -t;
                        SpriteFactory.Make("SignalHousing", signalRoot, SpriteFactory.Square, housing, headPos, new Vector2(0.46f, 0.17f) * k, 41, tAng);
                    }
                    lr = SpriteFactory.Make("SignalRed", signalRoot, SpriteFactory.Circle, Color.red, headPos - along * 0.14f * k, Vector2.one * 0.12f * k, 42);
                    ly = SpriteFactory.Make("SignalYellow", signalRoot, SpriteFactory.Circle, Color.yellow, headPos, Vector2.one * 0.12f * k, 42);
                    lg = SpriteFactory.Make("SignalGreen", signalRoot, SpriteFactory.Circle, Color.green, headPos + along * 0.14f * k, Vector2.one * 0.12f * k, 42);
                    if (a.lightModel == 2)
                    {
                        // Flechas en las luces según los giros del carril más a la derecha.
                        int mv = lane.moves;
                        float arrowAng = tAng + ((mv & 2) != 0 ? 0f : ((mv & 1) != 0 ? 90f : -90f));
                        foreach (var lamp0 in new[] { lr, ly, lg })
                            SpriteFactory.Make("SignalArrow", signalRoot, SpriteFactory.Triangle, new Color(0f, 0f, 0f, 0.75f), lamp0.transform.localPosition, Vector2.one * 0.07f * k, 43, arrowAng - 90f);
                    }
                    if (a.lightModel == 3)
                    {
                        Vector2 pp = pos + sideDir * 0.24f * k;
                        SpriteFactory.Make("PedHousing", signalRoot, SpriteFactory.Square, housing, pp, new Vector2(0.17f, 0.17f) * k, 41, tAng);
                        lp = SpriteFactory.Make("PedLamp", signalRoot, SpriteFactory.Square, Color.red, pp, new Vector2(0.06f, 0.11f) * k, 42, tAng + 90f);
                    }
                    var lamp = lg;
                    if (a.lightHidden)
                        for (int ci = firstChild; ci < signalRoot.childCount; ci++)
                        {
                            var csr = signalRoot.GetChild(ci).GetComponent<SpriteRenderer>();
                            if (csr != null) csr.enabled = false;
                        }
                    // Franja luminosa sobre la línea de parada, a lo ancho de todos los carriles de entrada.
                    Vector2 mid = Vector2.zero;
                    for (int li = 0; li < a.inLanes.Count; li++) mid += a.inLanes[li].path.PointAt(Mathf.Clamp(a.inLanes[li].stopS - 0.05f, 0.05f, a.inLanes[li].Length));
                    mid /= a.inLanes.Count;
                    float stripW = a.inLanes.Count * RoadNetworkManager.LaneWidth;
                    float ang = Geo.Angle(t);
                    var glow = SpriteFactory.Make("SignalGlow", signalRoot, SpriteFactory.Square, new Color(1f, 0.2f, 0.2f, 0.18f), mid, new Vector2(0.55f, stripW + 0.25f), 38, ang);
                    var bar = SpriteFactory.Make("SignalBar", signalRoot, SpriteFactory.Square, new Color(1f, 0.2f, 0.2f, 0.6f), mid, new Vector2(0.12f, stripW), 39, ang);
                    signals.Add(new SignalView { ic = ic, app = a, lamp = lamp, red = lr, yellow = ly, green = lg, ped = lp, bar = bar, glow = glow, mid = mid, radius = Mathf.Max(0.8f, stripW * 0.5f + 0.5f) });
                }
            }
            // Opción «Ver zona de toque de los semáforos» (como el original): dónde hay que tocar para cambiar cada uno.
            if (!curEditor && SaveSystem.Progress.showLightZone)
                foreach (var sv in signals)
                {
                    SpriteFactory.Make("TouchZone", signalRoot, SpriteFactory.Circle, new Color(1f, 1f, 1f, 0.1f), sv.mid, Vector2.one * sv.radius * 2f, 44);
                    SpriteFactory.Make("TouchZoneRing", signalRoot, SpriteFactory.Ring, new Color(1f, 1f, 1f, 0.45f), sv.mid, Vector2.one * sv.radius * 2f, 44);
                }
            selection = SpriteFactory.Make("Selection", signalRoot, SpriteFactory.Ring, new Color(1f, 1f, 1f, 0.9f), Vector2.zero, Vector2.one * 2.4f, 43);
            selection.enabled = false;
        }

        /// <summary>
        /// Semáforo al jugar en mapas por carriles, como el original: solo una mancha luminosa del color del estado sobre
        /// el final de cada carril que llega (sin poste ni cabeza). Se cambia tocando la mancha.
        /// </summary>
        readonly Dictionary<Approach, LightUnit> laneUnits = new Dictionary<Approach, LightUnit>();

        void BuildLaneSignal(IntersectionController ic, Approach a)
        {
            var lanes = a.inLanes;
            var g = MakeLightGeom(lanes, a.lightSide);
            // La franja y el brillo están pintados en el suelo: por debajo de los vehículos (antes pasaban «por debajo»).
            int ground = RoadOrder(lanes[0].LayerAt(lanes[0].Length)) + 4;
            bool ped = IsPedLight(lanes);   // peatonal: sin franja ni mancha en la acera (solo el poste con su caja)
            float spanW = 0f;
            SpriteRenderer firstGlow = null, firstBar = null;
            var extra = new List<SpriteRenderer>();
            Vector2 mid = Vector2.zero;
            for (int i = 0; i < lanes.Count; i++)
            {
                float ang = Geo.Angle(g.t);
                var gl = SpriteFactory.Make("SignalGlow", signalRoot, SpriteFactory.Soft, new Color(1f, 0.2f, 0.2f, 0.4f), g.stops[i] - g.t * 0.1f, ped ? Vector2.zero : Vector2.one * (g.widths[i] * 1.8f), ground);
                var b = SpriteFactory.Make("SignalBar", signalRoot, SpriteFactory.Square, new Color(1f, 0.2f, 0.2f, 0.75f), g.stops[i], ped ? Vector2.zero : new Vector2(0.11f, g.widths[i] - 0.02f), ground + 1, ang);
                if (firstGlow == null) { firstGlow = gl; firstBar = b; } else { extra.Add(gl); extra.Add(b); }
                mid += g.stops[i]; spanW += g.widths[i];
            }
            mid /= lanes.Count;
            var view = new SignalView { ic = ic, app = a, bar = firstBar, glow = firstGlow, mid = mid, radius = Mathf.Max(0.45f, spanW * 0.5f + 0.25f), extra = extra };
            view.lamp = firstBar;
            if (!a.lightHidden && IsPedLight(lanes))
            {
                view.heads = DrawPedLight(signalRoot, lanes, a.outLanes, Mathf.Clamp(a.lightSize, 0.6f, 1.8f), 40, ground + 1);
                view.pedLight = true;
                var hb = view.heads[0][0].transform.localPosition;
                view.head = new Vector2(hb.x, hb.y); view.hasHead = true;
            }
            else if (!a.lightHidden)
            {
                LightUnit u;
                if (laneUnits.TryGetValue(a, out u) && u.group.heads != null)
                {
                    var ug = UnitGeom(u.group, lanes);
                    view.heads = DrawLightHardware(signalRoot, ug, u.groupK, 40, u.pole, ground + 1);
                    Vector2 hm = Vector2.zero;
                    foreach (var h in ug.heads) hm += h;
                    view.head = hm / Mathf.Max(1, ug.heads.Length) + new Vector2(0f, 0.2f * u.groupK); view.hasHead = true;   // la cabeza está en alto
                }
                else
                {
                    view.heads = DrawLightHardware(signalRoot, g, Mathf.Clamp(a.lightSize, 0.6f, 1.8f), 40, true, ground + 1);
                    view.head = (g.pole + g.armEnd) * 0.5f + new Vector2(0f, 0.2f * Mathf.Clamp(a.lightSize, 0.6f, 1.8f)); view.hasHead = true;
                }
            }
            signals.Add(view);
        }

        /// <summary>Centro de la franja de un semáforo (para dibujar marcas y líneas sobre él).</summary>
        public Vector2 SignalMid(Approach a)
        {
            foreach (var s in signals) if (s.app == a) return s.mid;
            return a != null && a.node != null ? a.node.pos : Vector2.zero;
        }

        /// <summary>Semáforo más cercano al punto tocado (dentro de su radio de toque). Devuelve false si no hay ninguno.</summary>
        public bool FindSignalAt(Vector2 world, float extra, out IntersectionController ic, out Approach app)
        {
            ic = null; app = null;
            float best = float.MaxValue;
            for (int i = 0; i < signals.Count; i++)
            {
                var s = signals[i];
                float d = Vector2.Distance(world, s.mid);
                if (s.hasHead) d = Mathf.Min(d, Vector2.Distance(world, s.head) * 0.8f);
                if (d > s.radius + extra || d >= best) continue;
                best = d; ic = s.ic; app = s.app;
            }
            return app != null;
        }

        public void Select(IntersectionController ic)
        {
            selected = ic;
            if (selection == null) return;
            selection.enabled = false;   // como el original: sin círculo blanco sobre el cruce elegido
            if (ic != null) selection.transform.localPosition = new Vector3(ic.node.pos.x, ic.node.pos.y, 0f);
        }

        static void Glow(SignalView s, Color c)
        {
            var band = Color.Lerp(c, Color.white, 0.55f);   // la franja es clara y apenas teñida, como el original
            if (s.bar != null) s.bar.color = new Color(band.r, band.g, band.b, 0.6f);
            if (s.glow != null) s.glow.color = new Color(c.r, c.g, c.b, 0.4f);
            if (s.extra != null)
                for (int i = 0; i < s.extra.Count; i++)
                    s.extra[i].color = i % 2 == 0 ? new Color(c.r, c.g, c.b, 0.4f) : new Color(band.r, band.g, band.b, 0.6f);
        }

        void Update()
        {
            UpdateDayNight();
            UpdateBlinkers();
            if (detectorLoops.Count > 0)
                foreach (var kv in detectorLoops)
                    if (kv.Value != null) kv.Value.color = IntersectionMaster.Traffic.TrafficSimulationManager.OccupiedDetectors.Contains(kv.Key) ? DetectorOn : DetectorIdle;
            UpdateWarnings();
            bool assist = signals.Count > 0 && SaveSystem.Progress.colorAssist;
            for (int i = 0; i < signals.Count; i++)
            {
                var s = signals[i];
                if (s.lamp == null) continue;
                Color dim = new Color(0.2f, 0.2f, 0.22f, 1f);
                if (s.ic.broken)
                {
                    // Semáforo averiado: solo el ámbar, parpadeando.
                    bool on = Mathf.Repeat(Time.time * 1.6f, 1f) < 0.5f;
                    Color amb = on ? SpriteFactory.Hex("#F6C945") : dim;
                    if (s.red != null) { s.red.color = dim; s.yellow.color = amb; s.green.color = dim; if (s.ped != null) s.ped.color = dim; }
                    if (s.heads != null)
                        foreach (var h in s.heads)
                        {
                            h[0].color = new Color(0.22f, 0.07f, 0.07f);
                            if (!s.pedLight) h[1].color = on ? SpriteFactory.Hex("#F6C945") : new Color(0.22f, 0.2f, 0.07f);
                            h[2].color = new Color(0.07f, 0.2f, 0.1f);
                            if (h.Length > 3) { h[3].transform.localPosition = h[1].transform.localPosition; h[3].color = on && !s.pedLight ? new Color(1f, 0.85f, 0.3f, 0.6f) : new Color(0f, 0f, 0f, 0f); }
                        }
                    Glow(s, on ? new Color(0.97f, 0.8f, 0.25f) : new Color(0.3f, 0.3f, 0.3f));
                    continue;
                }
                var st = s.ic.GetState(s.app);
                if (s.red != null)
                {
                    s.red.color = st == SignalState.Red ? SpriteFactory.Hex("#E5534B") : dim;
                    s.yellow.color = st == SignalState.Yellow ? SpriteFactory.Hex("#F6C945") : dim;
                    s.green.color = st == SignalState.Green ? SpriteFactory.Hex("#3FD16B") : dim;
                    if (assist)
                    {
                        s.red.sprite = st == SignalState.Red ? SpriteFactory.Square : SpriteFactory.Circle;
                        s.yellow.sprite = st == SignalState.Yellow ? SpriteFactory.Ring : SpriteFactory.Circle;
                    }
                    // Peatonal: verde solo cuando los coches de este acceso están en rojo.
                    if (s.ped != null) s.ped.color = st == SignalState.Red ? SpriteFactory.Hex("#3FD16B") : SpriteFactory.Hex("#E5534B");
                }
                if (s.heads != null && s.pedLight)
                {
                    // Peatonal como en la vida real: rojo o verde; antes de cambiar a rojo el verde parpadea.
                    bool blinkOn = Mathf.Repeat(Time.time * 2.5f, 1f) < 0.55f;
                    bool green = st == SignalState.Green || (st == SignalState.Yellow && blinkOn);
                    bool red = st == SignalState.Red;
                    foreach (var h in s.heads)
                    {
                        h[0].color = red ? SpriteFactory.Hex("#FF3B30") : new Color(0.22f, 0.07f, 0.07f);
                        h[2].color = green ? SpriteFactory.Hex("#3FD16B") : new Color(0.07f, 0.2f, 0.1f);
                        if (h.Length > 3)
                        {
                            h[3].transform.localPosition = (red ? h[0] : h[2]).transform.localPosition;
                            h[3].color = red ? new Color(1f, 0.25f, 0.2f, 0.6f) : (green ? new Color(0.3f, 1f, 0.45f, 0.6f) : new Color(0f, 0f, 0f, 0f));
                        }
                    }
                }
                else if (s.heads != null)
                    foreach (var h in s.heads)
                    {
                        h[0].color = st == SignalState.Red ? SpriteFactory.Hex("#FF3B30") : new Color(0.22f, 0.07f, 0.07f);
                        h[1].color = st == SignalState.Yellow ? SpriteFactory.Hex("#F6C945") : new Color(0.22f, 0.2f, 0.07f);
                        h[2].color = st == SignalState.Green ? SpriteFactory.Hex("#3FD16B") : new Color(0.07f, 0.2f, 0.1f);
                        if (h.Length > 3)
                        {
                            // Halo sobre la luz encendida.
                            var on = st == SignalState.Red ? h[0] : (st == SignalState.Yellow ? h[1] : h[2]);
                            h[3].transform.localPosition = on.transform.localPosition;
                            Color hc = st == SignalState.Red ? new Color(1f, 0.25f, 0.2f, 0.6f) : (st == SignalState.Yellow ? new Color(1f, 0.85f, 0.3f, 0.6f) : new Color(0.3f, 1f, 0.45f, 0.6f));
                            h[3].color = hc;
                        }
                    }
                switch (st)
                {
                    case SignalState.Green: Glow(s, new Color(0.25f, 0.9f, 0.4f)); break;
                    case SignalState.Yellow: Glow(s, new Color(0.97f, 0.8f, 0.25f)); break;
                    default: Glow(s, new Color(0.95f, 0.25f, 0.22f)); break;
                }
            }
            if (selection != null && selection.enabled && selected != null)
            {
                float baseSize = Mathf.Max(2.0f, selected.node.trim * 2f + 0.8f);
                float k = baseSize + Mathf.Sin(Time.unscaledTime * 5f) * 0.1f;
                selection.transform.localScale = new Vector3(k, k, 1f);
            }
        }
    }
}
