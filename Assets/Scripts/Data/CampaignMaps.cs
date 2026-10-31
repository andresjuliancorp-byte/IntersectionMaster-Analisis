using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Core;

namespace IntersectionMaster.Data
{
    /// <summary>
    /// Mapas de la campaña: 5 mundos de 12 niveles, diseño propio generado por código (siempre igual para cada nivel),
    /// organizados como el original: Periferia, Ciudad baja y Polígono industrial son mundos con objetivo
    /// (6 minutos, empiezan a las 9:00); Común y Autopista son mundos de simulación (2,5 minutos, 8:00, objetivo 30,
    /// sin semáforos). Como en el original, todo está dibujado en diagonal (calles a 45°).
    /// </summary>
    public static class CampaignMaps
    {
        public const int Worlds = 5, LevelsPerWorld = 12;
        public static readonly string[] WorldNames = { "Común", "Periferia", "Ciudad baja", "Polígono industrial", "Autopista" };
        public static readonly string[] WorldHelp =
        {
            "Simulación: plazas y rotondas sin semáforos. El tráfico se regula solo; mira cómo funciona.",
            "Casas con jardín y cruces con semáforo.",
            "Tejados rojos, canales con barcos y lluvia en muchas partidas.",
            "Naves, camiones y trenes. Casi sin semáforos: tú decides cuándo pasan los stop.",
            "Simulación: avenidas anchas y enlaces sin semáforos."
        };

        /// <summary>Mundos de simulación (sin semáforos, 2,5 minutos, los vehículos no chocan).</summary>
        public static bool IsSimWorld(int world) { return world == 0 || world == 4; }

        // Objetivos de los mundos con objetivo, nivel a nivel (como los del original).
        static readonly int[][] Targets =
        {
            null,
            new[] { 22, 35, 60, 50, 85, 180, 65, 110, 90, 65, 110, 80 },
            new[] { 100, 105, 90, 80, 190, 85, 85, 100, 200, 85, 100, 90 },
            new[] { 70, 65, 60, 50, 105, 75, 65, 55, 70, 75, 80, 70 },
            null
        };

        // Cuadrícula de cruces de cada nivel (columnas × filas).
        static readonly int[] Cols = { 1, 2, 1, 2, 3, 2, 3, 2, 3, 3, 4, 3 };
        static readonly int[] Rows = { 1, 1, 1, 1, 1, 2, 1, 2, 2, 2, 2, 3 };

        public static string Id(int world, int level) { return "camp_" + world + "_" + level; }

        public static bool IsCampaign(string id) { return id != null && id.StartsWith("camp_"); }

        public static int Target(int world, int level)
        {
            var t = Targets[world];
            return t == null ? 30 : t[Mathf.Clamp(level, 0, t.Length - 1)];
        }

        public static float Seconds(int world) { return IsSimWorld(world) ? 150f : 360f; }

        public static MapData Find(string id)
        {
            if (!IsCampaign(id)) return null;
            var p = id.Split('_');
            int w, l;
            if (p.Length != 3 || !int.TryParse(p[1], out w) || !int.TryParse(p[2], out l)) return null;
            if (w < 0 || w >= Worlds || l < 0 || l >= LevelsPerWorld) return null;
            return Build(w, l);
        }

        /// <summary>Paso de coordenadas de la cuadrícula (calles horizontales y verticales) al mapa (girado 45°).</summary>
        class Frame
        {
            public Vector2 offset;
            const float C = 0.70710678f;
            public Vector2 Rot(Vector2 v) { return new Vector2((v.x - v.y) * C, (v.x + v.y) * C); }
            public Vector2 P(Vector2 local) { return Rot(local) + offset; }
            public Vector2 P(float x, float y) { return P(new Vector2(x, y)); }
        }

        public static MapData Build(int world, int level)
        {
            var rnd = new System.Random(1000 + world * 97 + level * 13);
            bool sim = IsSimWorld(world);
            int cols = Cols[level], rows = Rows[level];
            float sx = world == 4 ? 13f : (world == 3 ? 11f : 10f), sy = world == 4 ? 11f : 9f;
            float halfW = (cols - 1) * sx * 0.5f, halfH = (rows - 1) * sy * 0.5f;
            // Brazos largos: los extremos de las calles quedan fuera de la zona visible (los vehículos aparecen fuera).
            float arm = Mathf.Max(halfW, halfH) + 8f;
            int target = Target(world, level);

            var m = new MapData
            {
                id = Id(world, level), name = WorldNames[world] + " " + (level + 1), description = WorldHelp[world],
                author = "Intersection Master", difficulty = 1 + level / 3
            };
            m.featureLevel = MapData.CurrentFeatureLevel;
            m.autoArrows = m.autoCrosswalks = m.autoStopLines = true;
            m.startHour = sim ? 8f : 9f;
            m.goalMinutes = Seconds(world) / 60f;
            m.goalVehicles = target;
            m.playMode = sim ? 1 : 2;
            float[] simRain = { 0f, 0.05f, 0.2f };
            m.rainChance = world == 2 ? (level % 3 == 0 ? 0.2f : 0.5f) : (sim ? simRain[level % 3] : 0f);
            m.terrain = world == 0 ? 5 : world == 1 ? 2 : world == 3 ? 6 : world == 4 ? 2 : 0;
            m.traffic.spawnPerMinute = sim ? 18f + level : Mathf.Clamp(target / 6f * 1.45f, 14f, 60f);
            m.traffic.pedestriansPerMinute = world == 4 ? 0f : (world == 3 ? 3f : 6f);
            m.traffic.safeSignals = level < 4;
            if (world == 3) { m.traffic.truckChance = 0.32f; m.traffic.busChance = 0.02f; m.traffic.vanChance = 0.14f; }
            if (world == 4) { m.traffic.speedFactor = 1.15f; m.traffic.truckChance = 0.1f; }

            // Tamaño del mapa: el cuadrado girado que contiene todo, con margen.
            float reach = (Mathf.Max(halfW, halfH) + arm) * 1.42f + 4f;
            float size = Mathf.Ceil(reach * 2f);
            m.width = m.height = size;
            var fr = new Frame { offset = new Vector2(size * 0.5f, size * 0.5f) };

            // Cruces.
            var grid = new NodeData[cols, rows];
            for (int i = 0; i < cols; i++)
                for (int j = 0; j < rows; j++)
                    grid[i, j] = m.AddNode(fr.P(-halfW + i * sx, -halfH + j * sy));

            int laneH = world == 4 ? (level >= 8 ? 3 : 2) : (level >= 6 && world != 1 ? 2 : 1);
            int laneV = world == 4 && level >= 4 ? 2 : (world == 3 && level >= 5 ? 2 : 1);
            bool oneWay = world == 2 && level >= 3;   // Ciudad baja: calles verticales de un sentido, alternando

            for (int j = 0; j < rows; j++)
            {
                float y = -halfH + j * sy;
                bool dropW = level == 2 || (level >= 3 && rows > 1 && rnd.NextDouble() < 0.2);
                bool dropE = !dropW && level >= 3 && rows > 1 && rnd.NextDouble() < 0.2;
                if (!dropW) Road(m, Edge(m, fr.P(-halfW - arm, y)), grid[0, j], laneH, laneH);
                for (int i = 0; i + 1 < cols; i++) Road(m, grid[i, j], grid[i + 1, j], laneH, laneH);
                if (!dropE) Road(m, grid[cols - 1, j], Edge(m, fr.P(halfW + arm, y)), laneH, laneH);
            }
            for (int i = 0; i < cols; i++)
            {
                float x = -halfW + i * sx;
                bool down = oneWay && i % 2 == 0, up = oneWay && i % 2 == 1;
                bool dropS = !oneWay && level >= 3 && cols > 1 && rnd.NextDouble() < 0.2;
                Vector2 lo = fr.P(x, -halfH - arm), hi = fr.P(x, halfH + arm);
                if (up)
                {
                    var a = Edge(m, lo); a.exit = false;
                    Road(m, a, grid[i, 0], laneV, 0);
                    for (int j = 0; j + 1 < rows; j++) Road(m, grid[i, j], grid[i, j + 1], laneV, 0);
                    var b = Edge(m, hi); b.entry = false;
                    Road(m, grid[i, rows - 1], b, laneV, 0);
                }
                else if (down)
                {
                    var a = Edge(m, hi); a.exit = false;
                    Road(m, a, grid[i, rows - 1], laneV, 0);
                    for (int j = rows - 1; j > 0; j--) Road(m, grid[i, j], grid[i, j - 1], laneV, 0);
                    var b = Edge(m, lo); b.entry = false;
                    Road(m, grid[i, 0], b, laneV, 0);
                }
                else
                {
                    if (!dropS) Road(m, Edge(m, lo), grid[i, 0], laneV, laneV);
                    for (int j = 0; j + 1 < rows; j++) Road(m, grid[i, j], grid[i, j + 1], laneV, laneV);
                    Road(m, grid[i, rows - 1], Edge(m, hi), laneV, laneV);
                }
            }

            // Regulación de los cruces.
            if (sim)
            {
                // Simulación (sin semáforos): rotondas en los cruces grandes y ceda el paso en los demás.
                for (int i = 0; i < cols; i++)
                    for (int j = 0; j < rows; j++)
                    {
                        var n = grid[i, j];
                        if (m.Degree(n.id) >= 4 && (world == 4 || (i + j + level) % 2 == 0)) { n.roundabout = true; n.radius = world == 4 ? 2.4f : 1.8f; continue; }
                        SignsAt(m, n, fr, SignType.Yield, 0);
                    }
            }
            else if (world == 3 && level < 8)
            {
                // Polígono: stop controlados por el jugador (se tocan para dejar pasar, como un semáforo).
                for (int i = 0; i < cols; i++)
                    for (int j = 0; j < rows; j++) SignsAt(m, grid[i, j], fr, SignType.Stop, 2);
            }
            else AutoLights(m);

            // Zona visible: el cuadro que enseña todos los cruces (los extremos de los brazos quedan fuera).
            float view = (halfW + halfH) * 0.7072f + 6f;
            var center = new Vector2(size * 0.5f, size * 0.5f);
            var viewRect = new Rect(center.x - view, center.y - view, view * 2f, view * 2f);

            // Vías de tren (Polígono) y canal (Ciudad baja): una franja entre dos filas de calles o junto a la primera.
            float bandY = rows >= 2 ? -halfH + sy * 0.5f : -sy * 0.5f;
            bool rails = world == 3 && level % 4 != 2;
            bool canal = world == 2 && level % 3 != 1;
            var avoid = new List<KeyValuePair<Vector2, Vector2>>();
            float bandLen = halfW + arm;
            if (rails || canal) avoid.Add(new KeyValuePair<Vector2, Vector2>(fr.P(-bandLen, bandY), fr.P(bandLen, bandY)));

            if (canal)
            {
                var z = new DecorItem { type = DecorType.Zone, variant = 1 };
                Vector2 c0 = fr.P(0f, bandY);
                z.x = c0.x; z.y = c0.y;
                float hw = 0.8f;
                foreach (var q in new[] { new Vector2(-bandLen, -hw), new Vector2(bandLen, -hw), new Vector2(bandLen, hw), new Vector2(-bandLen, hw) })
                    z.pts.Add(fr.Rot(q + new Vector2(0f, bandY)) + fr.offset - c0);
                m.PlaceDecorItem(z);
                // Barcos amarrados en el canal, entre las calles.
                for (int i = 0; i <= cols; i++)
                {
                    float x0 = i == 0 ? -halfW - sx * 0.5f : (i == cols ? halfW + sx * 0.5f : -halfW + (i - 0.5f) * sx);
                    if (rnd.NextDouble() < 0.3) continue;
                    Vector2 bp = fr.P(x0 + (float)(rnd.NextDouble() - 0.5) * 2f, bandY + (rnd.Next(2) == 0 ? -0.35f : 0.35f));
                    if (viewRect.Contains(bp)) m.PlaceDecorItem(new DecorItem { x = bp.x, y = bp.y, type = DecorType.Boat, variant = rnd.Next(4), rot = 45f, size = 0.8f });
                }
            }

            Decorate(m, world, rnd, viewRect, avoid, level);

            // Al modelo por carriles (como los mapas del editor) y, si toca, las vías del tren por encima.
            m.EnsureLanes();
            m.SetView(viewRect);
            if (rails)
            {
                for (int k = 0; k < 2; k++)
                {
                    float off = k == 0 ? -0.3f : 0.3f;
                    Vector2 a = fr.P(-bandLen, bandY + off), b = fr.P(bandLen, bandY + off);
                    var na = m.AddNode(k == 0 ? a : b);
                    var nb = m.AddNode(k == 0 ? b : a);
                    if (na == null || nb == null) continue;
                    var r = m.AddRoad(na.id, nb.id, 1, 0);
                    if (r != null) r.laneType = RoadData.LaneRail;
                }
                // Señales del paso a nivel junto a cada calle que cruza la vía.
                for (int i = 0; i < cols; i++)
                {
                    float x = -halfW + i * sx;
                    foreach (var q in new[] { new Vector2(x + 1.1f, bandY - 1.0f), new Vector2(x - 1.1f, bandY + 1.0f) })
                    {
                        Vector2 sp = fr.P(q);
                        if (viewRect.Contains(sp)) m.PlaceDecorItem(new DecorItem { x = sp.x, y = sp.y, type = DecorType.RailSign, rot = 45f });
                    }
                }
            }
            m.Touch();
            return m;
        }

        static NodeData Edge(MapData m, Vector2 p)
        {
            var n = m.AddNode(p);
            n.entry = n.exit = true;
            return n;
        }

        static RoadData Road(MapData m, NodeData a, NodeData b, int ab, int ba) { return m.AddRoad(a.id, b.id, ab, ba); }

        /// <summary>Señal en cada acceso del cruce: las calles «horizontales» de la cuadrícula tienen prioridad con ceda.</summary>
        static void SignsAt(MapData m, NodeData n, Frame fr, SignType type, int style)
        {
            foreach (var r in m.RoadsAt(n.id))
            {
                int incoming = r.a == n.id ? r.lanesBA : r.lanesAB;
                if (incoming <= 0) continue;
                var other = m.Node(r.a == n.id ? r.b : r.a);
                Vector2 d = other.Pos - n.Pos;
                // Dirección en la cuadrícula (deshace el giro): horizontal si x' ≈ y' … calles a +45°.
                bool horizontal = d.x * d.y > 0f;
                if (type == SignType.Yield && horizontal) continue;   // la calle principal no cede
                m.ToggleSign(r.id, n.id, type);
                var sg = m.FindSign(r.id, n.id, type);
                if (sg != null) sg.style = style;
            }
        }

        static void AutoLights(MapData m)
        {
            foreach (var n in m.nodes)
            {
                if (n.roundabout || m.Degree(n.id) < 3) continue;
                foreach (var r in m.RoadsAt(n.id))
                {
                    int incoming = r.a == n.id ? r.lanesBA : r.lanesAB;
                    if (incoming > 0 && !m.signs.Exists(sg => sg.road == r.id && sg.node == n.id))
                        m.signs.Add(new SignItem { road = r.id, node = n.id, type = SignType.Light });
                }
            }
        }

        // ---------------- Decoración de cada mundo ----------------

        static bool NearBand(Vector2 p, List<KeyValuePair<Vector2, Vector2>> avoid, float dist)
        {
            foreach (var kv in avoid)
            {
                float t;
                if (Geo.DistToSegment(p, kv.Key, kv.Value, out t) < dist) return true;
            }
            return false;
        }

        static void Decorate(MapData m, int world, System.Random rnd, Rect view, List<KeyValuePair<Vector2, Vector2>> avoid, int level)
        {
            Rect area = new Rect(view.xMin - 3f, view.yMin - 3f, view.width + 6f, view.height + 6f);
            float step = world == 3 ? 2.4f : (world == 1 ? 1.5f : 1.8f);
            for (float x = area.xMin; x < area.xMax; x += step)
                for (float y = area.yMin; y < area.yMax; y += step)
                {
                    var p = new Vector2(x + (float)(rnd.NextDouble() - 0.5) * 0.3f, y + (float)(rnd.NextDouble() - 0.5) * 0.3f);
                    if (!m.InBounds(p) || NearBand(p, avoid, 1.4f)) continue;
                    // Distancias pensadas para el carril antiguo (0,36): se miden en esa escala para que nada quede encima.
                    float dist = m.DistanceToRoads(p) / IntersectionMaster.Traffic.RoadNetworkManager.WidthScale;
                    double r = rnd.NextDouble();
                    float rot = 45f + rnd.Next(4) * 90f;   // los edificios siguen la diagonal de las calles
                    switch (world)
                    {
                        case 1:   // Periferia: casas bajas con tejado y mucho verde
                            if (r < 0.45 && dist > 1.5f) House(m, p, rot, 0.75f, 1, rnd.Next(2) == 0 ? 0 : 2, rnd.Next(3) == 0 ? 6 : 2, rnd.Next(4) == 0 ? 4 : 3, 0);
                            else if (r < 0.85 && dist > 0.9f) Item(m, p, DecorType.Tree, rnd.Next(3) == 0 ? 3 : 0, 0.8f + (float)rnd.NextDouble() * 0.5f);
                            else if (dist > 1.0f) Item(m, p, DecorType.Hedge, 0, 1f);
                            break;
                        case 2:   // Ciudad baja: bloques de 2 a 4 pisos con tejas rojas o verdes
                            if (r < 0.65 && dist > 1.6f) House(m, p, rot, 1.0f, 2 + rnd.Next(3), rnd.Next(3) == 0 ? 2 : 0, rnd.Next(4) == 0 ? 4 : 1, rnd.Next(3), 1);
                            else if (r < 0.85 && dist > 1.0f) Item(m, p, DecorType.Tree, 0, 0.9f);
                            break;
                        case 3:   // Polígono: naves grandes de techo plano, contenedores y árboles de otoño
                            if (r < 0.45 && dist > 2.1f) House(m, p, rot, 1.6f + (float)rnd.NextDouble() * 0.5f, 2, 1, 0, 2, 0);
                            else if (r < 0.62 && dist > 1.2f) Item(m, p, DecorType.Containers, rnd.Next(4), 1f);
                            else if (r < 0.74 && dist > 1.0f) Item(m, p, DecorType.Trailer, rnd.Next(4), 1f);
                            else if (r < 0.8 && dist > 1.6f) Item(m, p, DecorType.RoundStructure, rnd.Next(4), 1f);
                            else if (r < 0.9 && dist > 1.0f) Item(m, p, DecorType.Tree, 1, 0.9f);
                            break;
                        case 4:   // Autopista: campo con muchos árboles
                            if (r < 0.6 && dist > 1.3f) Item(m, p, DecorType.Tree, rnd.Next(4) == 0 ? 3 : 0, 1f + (float)rnd.NextDouble() * 0.4f);
                            break;
                        default:  // Común: ciudad beige con plazas
                            if (r < 0.55 && dist > 1.9f) House(m, p, rot, 1.0f, 1 + rnd.Next(3), 1, 7, 0, 0);
                            else if (r < 0.72 && dist > 1.0f) Item(m, p, DecorType.Tree, 0, 1f);
                            else if (r > 0.95 && dist > 1.4f) Item(m, p, DecorType.Park, 0, 1f);
                            break;
                    }
                }

            // Farolas (o luces de autopista) junto a las calles.
            foreach (var r in m.roads)
            {
                float len = m.CenterOf(r).Length;
                for (float d = 2.5f; d < len - 2f; d += world == 4 ? 6f : 4.5f)
                {
                    Vector2 c = m.PointOnRoad(r, d / len);
                    if (!area.Contains(c)) continue;
                    float ang = m.RoadAngleNear(c) * Mathf.Deg2Rad;
                    Vector2 nrm = new Vector2(-Mathf.Sin(ang), Mathf.Cos(ang));
                    Vector2 p = c + nrm * ((r.lanesAB + r.lanesBA) * IntersectionMaster.Traffic.RoadNetworkManager.LaneWidth * 0.5f + 0.45f);
                    if (m.DistanceToRoads(p) >= 0.55f * IntersectionMaster.Traffic.RoadNetworkManager.WidthScale && !NearBand(p, avoid, 1.0f)) m.PlaceDecor(p, world == 4 ? DecorType.FloodLight : DecorType.Lamp);
                }
            }
        }

        static void House(MapData m, Vector2 p, float rot, float size, int floors, int roof, int roofColor, int wallColor, int extra)
        {
            for (int i = 0; i < m.decor.Count; i++) if (Vector2.Distance(m.decor[i].Pos, p) < size * 1.1f) return;
            m.PlaceDecorItem(new DecorItem
            {
                x = p.x, y = p.y, type = DecorType.Building, size = size, height = floors, roof = roof, roofColor = roofColor,
                color = wallColor, wall = floors >= 3 || extra == 1 ? 1 : 0, rot = rot, variant = Mathf.Abs(Mathf.RoundToInt(p.x * 7f + p.y)) % 4
            });
        }

        static void Item(MapData m, Vector2 p, DecorType t, int variant, float size)
        {
            for (int i = 0; i < m.decor.Count; i++) if (Vector2.Distance(m.decor[i].Pos, p) < 0.5f) return;
            m.PlaceDecorItem(new DecorItem { x = p.x, y = p.y, type = t, variant = variant, size = size });
        }
    }
}
