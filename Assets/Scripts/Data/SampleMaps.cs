using System.Collections.Generic;
using UnityEngine;

namespace IntersectionMaster.Data
{
    /// <summary>Mapas de ejemplo generados por código con nodos y calles libres (el jugador puede duplicarlos y editarlos).</summary>
    public static class SampleMaps
    {
        public const string CrossX = "sample_cross_x";
        public const string TJunction = "sample_t";
        public const string DoubleCross = "sample_double";
        public const string Grid = "sample_grid";
        public const string Round = "sample_round";
        public const string Signs = "sample_signs";
        public const string OneWay = "sample_oneway";
        public const string Lanes = "sample_lanes";
        public const string Curves = "sample_curves";

        public static List<MapData> All()
        {
            return new List<MapData>
            {
                MakeCrossX(), MakeT(), MakeDouble(), MakeGrid(),
                MakeRound(), MakeSigns(), MakeOneWay(), MakeLanes(), MakeCurves()
            };
        }

        public static MapData Find(string id)
        {
            foreach (var m in All()) if (m.id == id) return m;
            return null;
        }

        public static bool IsSample(string id)
        {
            return id != null && id.StartsWith("sample_");
        }

        static MapData Base(string id, string name, string desc, float w, float h, int difficulty, float spawn)
        {
            var m = new MapData
            {
                id = id, name = name, description = desc, author = "Intersection Master",
                width = w, height = h, difficulty = difficulty
            };
            m.traffic.spawnPerMinute = spawn;
            m.traffic.pedestriansPerMinute = 6f;
            m.autoArrows = m.autoCrosswalks = m.autoStopLines = true;
            m.featureLevel = MapData.CurrentFeatureLevel;
            m.Touch();
            return m;
        }

        /// <summary>Nodo interior (cruce o punto de paso).</summary>
        static NodeData N(MapData m, float x, float y)
        {
            return m.AddNode(new Vector2(x, y));
        }

        /// <summary>Nodo en el borde: entrada y salida de vehículos.</summary>
        static NodeData E(MapData m, float x, float y)
        {
            var n = m.AddNode(new Vector2(x, y));
            n.entry = true;
            n.exit = true;
            return n;
        }

        static RoadData R(MapData m, NodeData a, NodeData b, int ab = 1, int ba = 1, float bendX = 0f, float bendY = 0f)
        {
            var r = m.AddRoad(a.id, b.id, ab, ba);
            if (r != null) { r.bendX = bendX; r.bendY = bendY; }
            return r;
        }

        /// <summary>Pone un semáforo en cada acceso de los cruces que no tienen señales.</summary>
        static void AutoLights(MapData m)
        {
            foreach (var n in m.nodes)
            {
                if (n.roundabout || m.Degree(n.id) < 3) continue;
                bool hasSign = false;
                foreach (var r in m.RoadsAt(n.id)) if (m.signs.Exists(sg => sg.road == r.id && sg.node == n.id)) hasSign = true;
                if (hasSign) continue;
                foreach (var r in m.RoadsAt(n.id))
                {
                    int incoming = r.a == n.id ? r.lanesBA : r.lanesAB;
                    if (incoming > 0) m.signs.Add(new SignItem { road = r.id, node = n.id, type = SignType.Light });
                }
            }
        }

        /// <summary>Gira el mapa 90° (los de ejemplo son apaisados; en pantalla vertical se ven mejor así).</summary>
        static void RotateToPortrait(MapData m)
        {
            float w = m.width, h = m.height;
            foreach (var n in m.nodes)
            {
                float x = n.x, y = n.y;
                n.x = y;
                n.y = w - x;
            }
            foreach (var r in m.roads)
            {
                float bx = r.bendX, by = r.bendY;
                r.bendX = by;
                r.bendY = -bx;
            }
            m.width = h;
            m.height = w;
        }

        static void Decorate(MapData m)
        {
            if (Screen.height > Screen.width && m.width > m.height * 1.15f) RotateToPortrait(m);
            AutoLights(m);
            for (float x = 1f; x < m.width; x += 1.8f)
            {
                for (float y = 1f; y < m.height; y += 1.8f)
                {
                    var p = new Vector2(x, y);
                    // Distancias de la escala antigua (carril 0,36): con el carril de 0,5 nada queda encima de la calle.
                    float dist = m.DistanceToRoads(p) / IntersectionMaster.Traffic.RoadNetworkManager.WidthScale;
                    int h = Mathf.Abs((Mathf.RoundToInt(x * 10f) * 73856093) ^ (Mathf.RoundToInt(y * 10f) * 19349663)) % 11;
                    if (h < 6 && dist > 1.9f) m.PlaceDecor(p, DecorType.Building);
                    else if (h < 9 && dist > 1.0f) m.PlaceDecor(p, DecorType.Tree);
                    else if (h == 10 && dist > 1.4f) m.PlaceDecor(p, DecorType.Park);
                }
            }

            // Farolas junto a las calles.
            foreach (var r in m.roads)
            {
                float len = m.CenterOf(r).Length;
                for (float d = 2.5f; d < len - 2f; d += 4.5f)
                {
                    Vector2 c = m.PointOnRoad(r, d / len);
                    float ang = m.RoadAngleNear(c) * Mathf.Deg2Rad;
                    Vector2 nrm = new Vector2(-Mathf.Sin(ang), Mathf.Cos(ang));
                    Vector2 p = c + nrm * ((r.lanesAB + r.lanesBA) * IntersectionMaster.Traffic.RoadNetworkManager.LaneWidth * 0.5f + 0.45f);
                    if (m.DistanceToRoads(p) >= 0.55f * IntersectionMaster.Traffic.RoadNetworkManager.WidthScale) m.PlaceDecor(p, DecorType.Lamp);
                }
            }
        }

        static MapData MakeCrossX()
        {
            var m = Base(CrossX, "Cruce en X", "Un cruce clásico de cuatro vías. Ideal para empezar.", 22f, 14f, 1, 24f);
            var c = N(m, 11f, 7f);
            R(m, E(m, 0f, 7f), c);
            R(m, c, E(m, 22f, 7f));
            R(m, E(m, 11f, 0f), c);
            R(m, c, E(m, 11f, 14f));
            Decorate(m);
            return m;
        }

        static MapData MakeT()
        {
            var m = Base(TJunction, "Cruce en T", "Tres vías: la calle secundaria desemboca en la principal.", 22f, 14f, 2, 26f);
            var c = N(m, 11f, 8f);
            R(m, E(m, 0f, 8f), c, 2, 2);
            R(m, c, E(m, 22f, 8f), 2, 2);
            R(m, E(m, 11f, 0f), c);
            Decorate(m);
            return m;
        }

        static MapData MakeDouble()
        {
            var m = Base(DoubleCross, "Doble cruce", "Dos cruces en X en la misma avenida.", 30f, 14f, 3, 36f);
            var c1 = N(m, 9f, 7f);
            var c2 = N(m, 21f, 7f);
            R(m, E(m, 0f, 7f), c1);
            R(m, c1, c2);
            R(m, c2, E(m, 30f, 7f));
            R(m, E(m, 9f, 0f), c1);
            R(m, c1, E(m, 9f, 14f));
            R(m, E(m, 21f, 0f), c2);
            R(m, c2, E(m, 21f, 14f));
            Decorate(m);
            return m;
        }

        static MapData MakeGrid()
        {
            var m = Base(Grid, "Cuadrícula", "Cuatro cruces conectados. Sin ayudas de seguridad.", 28f, 20f, 4, 48f);
            var a = N(m, 9f, 6f); var b = N(m, 19f, 6f);
            var c = N(m, 9f, 14f); var d = N(m, 19f, 14f);
            R(m, a, b); R(m, c, d); R(m, a, c); R(m, b, d);
            R(m, E(m, 0f, 6f), a); R(m, b, E(m, 28f, 6f));
            R(m, E(m, 0f, 14f), c); R(m, d, E(m, 28f, 14f));
            R(m, E(m, 9f, 0f), a); R(m, c, E(m, 9f, 20f));
            R(m, E(m, 19f, 0f), b); R(m, d, E(m, 19f, 20f));
            m.traffic.safeSignals = false;
            Decorate(m);
            return m;
        }

        static MapData MakeRound()
        {
            var m = Base(Round, "Rotonda", "Circulación antihoraria: quien entra cede el paso al anillo. Un cruce con semáforo a la entrada oeste.", 30f, 18f, 3, 34f);
            var ring = N(m, 17f, 9f);
            ring.roundabout = true;
            ring.radius = 1.9f;
            var w = N(m, 8f, 9f);
            R(m, E(m, 0f, 9f), w, 2, 2);
            R(m, w, ring, 2, 2);
            R(m, ring, E(m, 30f, 9f), 2, 2);
            R(m, E(m, 17f, 0f), ring);
            R(m, ring, E(m, 17f, 18f));
            R(m, w, E(m, 8f, 18f));
            Decorate(m);
            return m;
        }

        static MapData MakeSigns()
        {
            var m = Base(Signs, "Señales de tráfico", "Stop y ceda en las calles pequeñas, prioridad en la avenida, un giro prohibido y un paso de peatones.", 34f, 14f, 3, 30f);
            var c1 = N(m, 8f, 7f);
            var c2 = N(m, 19f, 7f);
            var c3 = N(m, 27f, 7f);
            var w = E(m, 0f, 7f);
            var e = E(m, 34f, 7f);
            var r1 = R(m, w, c1);
            var r2 = R(m, c1, c2);
            var r3 = R(m, c2, c3);
            var r4 = R(m, c3, e);
            var s1 = R(m, E(m, 8f, 0f), c1);
            var n1 = R(m, c1, E(m, 8f, 14f));
            var s2 = R(m, E(m, 19f, 0f), c2);
            var n2 = R(m, c2, E(m, 19f, 14f));
            R(m, E(m, 27f, 0f), c3);
            R(m, c3, E(m, 27f, 14f));
            // Primer cruce: stop en las calles pequeñas, prioridad en la avenida, giro a la izquierda prohibido desde el oeste.
            m.ToggleSign(s1.id, c1.id, SignType.Stop);
            m.ToggleSign(n1.id, c1.id, SignType.Stop);
            m.ToggleSign(r1.id, c1.id, SignType.Priority);
            m.ToggleSign(r2.id, c1.id, SignType.Priority);
            m.ToggleSign(r1.id, c1.id, SignType.NoLeft);
            // Segundo cruce: ceda en las calles pequeñas.
            m.ToggleSign(s2.id, c2.id, SignType.Yield);
            m.ToggleSign(n2.id, c2.id, SignType.Yield);
            m.ToggleSign(r2.id, c2.id, SignType.Priority);
            m.ToggleSign(r3.id, c2.id, SignType.Priority);
            m.ToggleSign(r2.id, -1, SignType.SpeedLimit);
            m.ToggleCrosswalk(r2.id, 0.5f);
            Decorate(m);
            return m;
        }

        static MapData MakeOneWay()
        {
            var m = Base(OneWay, "Calles de un sentido", "Dos avenidas de sentido único unidas por calles de doble sentido.", 28f, 18f, 3, 36f);
            var a1 = N(m, 7f, 6f); var a2 = N(m, 21f, 6f);
            var b1 = N(m, 7f, 12f); var b2 = N(m, 21f, 12f);
            var w1 = E(m, 0f, 6f); var e1 = E(m, 28f, 6f);
            var w2 = E(m, 0f, 12f); var e2 = E(m, 28f, 12f);
            w1.exit = false; e1.entry = false;      // la avenida baja va de oeste a este
            e2.exit = false; w2.entry = false;      // la avenida alta va de este a oeste
            R(m, w1, a1, 1, 0); R(m, a1, a2, 1, 0); R(m, a2, e1, 1, 0);
            R(m, e2, b2, 1, 0); R(m, b2, b1, 1, 0); R(m, b1, w2, 1, 0);
            // Las calles "e2->b2" y "b1->w2" se dibujan de a hacia b, por eso los nodos se crean en ese orden.
            R(m, a1, b1); R(m, a2, b2);
            R(m, E(m, 7f, 0f), a1); R(m, b1, E(m, 7f, 18f));
            R(m, E(m, 21f, 0f), a2); R(m, b2, E(m, 21f, 18f));
            Decorate(m);
            return m;
        }

        static MapData MakeLanes()
        {
            var m = Base(Lanes, "Avenida de dos carriles", "La avenida tiene dos carriles por sentido: los coches se colocan según vayan a girar.", 32f, 14f, 4, 42f);
            var c1 = N(m, 10f, 7f);
            var c2 = N(m, 22f, 7f);
            R(m, E(m, 0f, 7f), c1, 2, 2);
            R(m, c1, c2, 2, 2);
            R(m, c2, E(m, 32f, 7f), 2, 2);
            R(m, E(m, 10f, 0f), c1);
            R(m, c1, E(m, 10f, 14f));
            R(m, E(m, 22f, 0f), c2);
            R(m, c2, E(m, 22f, 14f));
            Decorate(m);
            return m;
        }

        static MapData MakeCurves()
        {
            var m = Base(Curves, "Carretera con curvas", "Una carretera sinuosa cruza una avenida recta. Hay dos pasos de peatones.", 30f, 18f, 2, 26f);
            var s = E(m, 0f, 3f);
            var k1 = N(m, 9f, 4f);
            var k2 = N(m, 15f, 9f);       // cruce con la avenida vertical
            var k3 = N(m, 22f, 12f);
            var f = E(m, 30f, 14f);
            var r1 = R(m, s, k1, 1, 1, 0f, 1.2f);
            R(m, k1, k2, 1, 1, 2.4f, -1.4f);
            R(m, k2, k3, 1, 1, -2.2f, 1.2f);
            var r4 = R(m, k3, f, 1, 1, 0f, -1.6f);
            R(m, E(m, 15f, 0f), k2);
            R(m, k2, E(m, 15f, 18f));
            m.ToggleCrosswalk(r1.id, 0.5f);
            m.ToggleCrosswalk(r4.id, 0.5f);
            Decorate(m);
            return m;
        }
    }
}
