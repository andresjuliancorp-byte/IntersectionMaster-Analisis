using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using IntersectionMaster.Data;

namespace IntersectionMaster.Systems
{
    /// <summary>
    /// Importa los mapas propios hechos en Intersection Controller (archivos .trzmap): texto comprimido con una línea por
    /// objeto «type:N;clave:valor;…». Se usan los carriles (cada «segmento» es una lista ordenada de puntos; el primero y el
    /// último son nodos compartidos con otros carriles), su tipo (coches, acera, tren, bici), las curvas, los semáforos,
    /// la zona visible, la hora de inicio, el aspecto de los carriles y casi toda la decoración.
    /// 36 píxeles del original = un carril (0,5 unidades aquí); el eje y del original va hacia abajo.
    /// </summary>
    public static class TrzImporter
    {
        /// <summary>
        /// Más objetos del original (deducidos por dónde aparecen en sus mapas): setos, postes con cables, señales,
        /// coches/remolques aparcados, barcos, objetos sueltos, paradas, luces que destellan, humo, botones de semáforos
        /// y controladores. Devuelve true si el objeto se trató aquí.
        /// </summary>
        static bool ImportMore(MapData m, Dictionary<string, string> o, int st, Vector2 p, Func<float, float, Vector2> P,
            Dictionary<string, Dictionary<string, string>> objs, Dictionary<string, KeyValuePair<int, int>> lightOf, int made, Dictionary<string, string> header)
        {
            switch (st)
            {
                case 8:   // seto: rectángulo con esquinas c1..c4
                    {
                        if (!o.ContainsKey("c1x")) return true;
                        Vector2 c1 = P(F(o, "c1x"), F(o, "c1y")), c2 = P(F(o, "c2x"), F(o, "c2y")), c3 = P(F(o, "c3x"), F(o, "c3y")), c4 = P(F(o, "c4x"), F(o, "c4y"));
                        Vector2 c = (c1 + c2 + c3 + c4) * 0.25f;
                        Vector2 e1 = c2 - c1, e2 = c4 - c1;
                        Vector2 along = e1.magnitude >= e2.magnitude ? e1 : e2, across = e1.magnitude >= e2.magnitude ? e2 : e1;
                        m.PlaceDecorItem(new DecorItem
                        {
                            x = c.x, y = c.y, type = DecorType.Hedge, rot = Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg + 0.001f,
                            x2 = along.magnitude * 0.5f, y2 = Mathf.Max(0.08f, across.magnitude * 0.5f)
                        });
                        return true;
                    }
                case 20:   // poste de teléfono; ci0.. = postes a los que va el cable
                    {
                        m.PlaceDecorItem(new DecorItem { x = p.x, y = p.y, type = DecorType.PhonePole });
                        int cc = I(o, "cc");
                        for (int i = 0; i < cc && i < 4; i++)
                        {
                            string to; Dictionary<string, string> q;
                            if (!o.TryGetValue("ci" + i, out to) || !objs.TryGetValue(to, out q)) continue;
                            Vector2 e = P(F(q, "x"), F(q, "y"));
                            if ((e - p).sqrMagnitude > 0.01f && m.InBounds(e))
                                m.PlaceDecorItem(new DecorItem { x = p.x, y = p.y, x2 = e.x, y2 = e.y, type = DecorType.PowerLine, variant = 1 });
                        }
                        return true;
                    }
                case 21:   // señal (st = posición en el menú de señales del original, 4 por fila)
                    {
                        int sti = I(o, "st", -1);
                        float an = F(o, "an", 90f);   // 90 = de frente (como al ponerla)
                        int si = sti + 10;   // el archivo cuenta desde la 11.ª del menú (Stop = 17, Ceda = 18)
                        int sign = si >= 0 && si < SignMap.Length ? SignMap[si] : -1;
                        if (sign >= 0)
                            m.PlaceDecorItem(new DecorItem { x = p.x, y = p.y, type = DecorType.TrafficSign, variant = sign, rot = an + 0.001f, color = I(o, "p", 1) == 0 ? 1 : 0 });
                        else if (sign <= -10)   // carteles grandes y placas sobre poste: nuestros carteles (RoadSign)
                            m.PlaceDecorItem(new DecorItem { x = p.x, y = p.y, type = DecorType.RoadSign, variant = -10 - sign, rot = an - 90f + 0.001f });
                        else if (sign == -2)    // balizas de borde (postes rayados)
                            m.PlaceDecorItem(new DecorItem { x = p.x, y = p.y, type = DecorType.Misc, variant = 10, size = 0.8f });
                        return true;   // -1: estructuras sin equivalente (pórticos vacíos, placas en blanco, panel apagado)
                    }
                case 24:   // vehículo aparcado
                    m.PlaceDecorItem(new DecorItem { x = p.x, y = p.y, type = DecorType.ParkedCar, rot = -F(o, "an") + 0.001f, color = Mathf.Abs(I(o, "c")) % 11 });
                    return true;
                case 25:   // remolque aparcado
                    m.PlaceDecorItem(new DecorItem { x = p.x, y = p.y, type = DecorType.Trailer, rot = -F(o, "an") + 0.001f });
                    return true;
                case 45:   // barco amarrado
                    m.PlaceDecorItem(new DecorItem { x = p.x, y = p.y, type = DecorType.Boat, rot = -F(o, "a") + 0.001f, size = Mathf.Clamp(F(o, "c", 1f) / 0.7f, 0.5f, 2f), variant = I(o, "b") % 4 });
                    return true;
                case 48:   // pegatina de pared (v = tipo del menú, 21; h = «Altura»; c = color; a = hacia dónde mira, radianes)
                    {
                        int v = I(o, "v");
                        int[] wmap = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 7, 7, 3, 3, 3, 1, 7, 3, 2, 3 };
                        string wc = TrzColor(I(o, "c", 1), header);
                        m.PlaceDecorItem(new DecorItem
                        {
                            x = p.x, y = p.y, type = DecorType.WallSticker, variant = wmap[Mathf.Clamp(v, 0, wmap.Length - 1)],
                            rot = 90f - F(o, "a", Mathf.PI * 0.5f) * Mathf.Rad2Deg + 0.001f, height = Mathf.Clamp(I(o, "h", 1), 1, 10),
                            hexA = wc == "#FFFFFF" ? "" : wc
                        });
                    }
                    return true;
                case 12:   // humo
                    m.PlaceDecorItem(new DecorItem { x = p.x, y = p.y, type = DecorType.Smoke, size = Mathf.Clamp(F(o, "ss", 0.5f) * 2f, 0.4f, 3f) });
                    return true;
                case 10:   // farola suelta junto a la calle (a = giro)
                    m.PlaceDecorItem(new DecorItem { x = p.x, y = p.y, type = DecorType.Lamp, rot = -F(o, "a") + 0.001f });
                    return true;
                case 44:   // luz de inundación de autopista (redonda, con cuatro focos; también la usan en plazas)
                    m.PlaceDecorItem(new DecorItem { x = p.x, y = p.y, type = DecorType.FloodLight, height = Mathf.Clamp(I(o, "h", 10), 1, 10) });   // h = «Altura»
                    return true;
                case 7:    // coche aparcado (c = probabilidad de que esté, sc = color, a = giro en grados)
                    m.PlaceDecorItem(new DecorItem
                    {
                        x = p.x, y = p.y, type = DecorType.ParkedCar, rot = -F(o, "a") + 0.001f,
                        presence = Mathf.Clamp01(F(o, "c", 1f)), color = Mathf.Abs(I(o, "sc")) % 11
                    });
                    return true;
                case 31:   // banca (an = giro en grados)
                    m.PlaceDecorItem(new DecorItem { x = p.x, y = p.y, type = DecorType.Misc, variant = 2, rot = -F(o, "an") + 0.001f });
                    return true;
                case 49:   // bolardo
                    m.PlaceDecorItem(new DecorItem { x = p.x, y = p.y, type = DecorType.Misc, variant = 10, size = 0.8f });
                    return true;
                case 37:   // pegatina en la calzada (v = dibujo, a = hacia dónde apunta, c = color)
                    {
                        int v = I(o, "v");
                        string sc = TrzColor(I(o, "c", 1), header);
                        m.PlaceDecorItem(new DecorItem { x = p.x, y = p.y, type = DecorType.Sticker, variant = StickerVariant(v), rot = -F(o, "a") + 0.001f, color = 0, hexA = sc == "#FFFFFF" ? "" : sc });
                        return true;
                    }
                case 33:   // parada de autobús (marquesina): rectángulo c1..c4
                    {
                        if (!o.ContainsKey("c1x")) return true;
                        Vector2 c1 = P(F(o, "c1x"), F(o, "c1y")), c2 = P(F(o, "c2x"), F(o, "c2y")), c3 = P(F(o, "c3x"), F(o, "c3y")), c4 = P(F(o, "c4x"), F(o, "c4y"));
                        Vector2 c = (c1 + c2 + c3 + c4) * 0.25f, e = c2 - c1;
                        m.PlaceDecorItem(new DecorItem { x = c.x, y = c.y, type = DecorType.BusShelter, rot = Mathf.Atan2(e.y, e.x) * Mathf.Rad2Deg + 0.001f, size = Mathf.Clamp(e.magnitude / 0.86f, 0.5f, 2f) });
                        return true;
                    }
                case 19:   // cono
                    m.PlaceDecorItem(new DecorItem { x = p.x, y = p.y, type = DecorType.Misc, variant = 0, size = 0.8f });
                    return true;
                case 55:   // mesa de parque con bancos de madera
                    m.PlaceDecorItem(new DecorItem { x = p.x, y = p.y, type = DecorType.Misc, variant = 4, rot = -F(o, "a") * Mathf.Rad2Deg + 0.001f });
                    return true;
                case 43:   // texto (te = texto, sx = tamaño, a = giro)
                    {
                        string te;
                        if (!o.TryGetValue("te", out te) || string.IsNullOrEmpty(te)) return true;
                        if (te.Length > 40) te = te.Substring(0, 40);
                        float sx = Mathf.Max(0.1f, F(o, "sx", 1f)), sy = Mathf.Max(0.1f, F(o, "sy", 1f));
                        m.PlaceDecorItem(new DecorItem
                        {
                            x = p.x, y = p.y, type = DecorType.TextLabel, text = te, rot = -F(o, "a") - 90f,   // a = -90: se lee derecho
                            size = Mathf.Clamp(sx * 0.6f, 0.4f, 3f), y2 = sy / sx,
                            hexA = I(o, "ic") == 4 ? "#F4C20D" : "#FFFFFF"   // en el original el texto es blanco salvo que se elija otro color
                        });
                        return true;
                    }
                case 9:    // aparcamiento: (x, y) = esquina de la primera plaza; las plazas (b, de 36 px) siguen el giro a; d = dos filas; c = uso
                    {
                        int n = Mathf.Clamp(I(o, "b", 4), 1, 60);
                        bool two = I(o, "d") == 1;
                        float ang = -F(o, "a");
                        Vector2 u = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad));
                        float sw = 36f * K, depth = 72f * K;   // plaza: un carril de ancho y dos de fondo
                        // (x, y) = esquina donde empieza (el cono del mapa de prueba): las plazas siguen a, y las filas quedan a
                        // la derecha de esa dirección (en el original, hacia abajo con a = 0). Una fila: la línea del fondo pasa por
                        // la esquina; dos filas: la línea compartida va en el medio.
                        Vector2 rn = new Vector2(u.y, -u.x);
                        Vector2 c = p + u * (sw * n * 0.5f) + rn * ((two ? 2f : 1f) * depth * 0.5f);
                        m.PlaceDecorItem(new DecorItem
                        {
                            x = c.x, y = c.y, type = DecorType.Parking, spaces = n, twoWay = two, rot = ang + 0.001f,
                            x2 = n * sw * 0.5f, y2 = (two ? 2f : 1f) * depth * 0.5f, usage = Mathf.Clamp01(F(o, "c", 0.3f)), invisible = I(o, "v", 1) == 0
                        });
                        return true;
                    }
                case 4:    // material (esquinas c1..c4; m = material, b = rayas 1..3, hc = color de las rayas)
                    {
                        if (!o.ContainsKey("c1x")) return true;
                        var cs = new[] { P(F(o, "c1x"), F(o, "c1y")), P(F(o, "c2x"), F(o, "c2y")), P(F(o, "c3x"), F(o, "c3y")), P(F(o, "c4x"), F(o, "c4y")) };
                        Vector2 c = (cs[0] + cs[1] + cs[2] + cs[3]) * 0.25f;
                        int mt = I(o, "m");
                        var z = new DecorItem { x = c.x, y = c.y, type = DecorType.Material };
                        if (mt == 2) z.variant = 0; else if (mt == 6) z.variant = 3; else if (mt == 8) z.hexA = "#8DBA6C";   // 8 = césped claro (franjas verdes de la plaza)
                        else { string ch = mt >= 201 ? TrzColor(mt, header) : ""; if (ch != "") z.hexA = ch; else z.variant = 6; }   // 0 = asfalto, del color de la calzada
                        int b = I(o, "b");
                        if (b >= 1 && b <= 3) { z.bars = b; z.color2 = I(o, "hc", 1) == 2 ? 1 : 0; }
                        z.overLane = I(o, "s", 1) == 0;   // s = «Bajo carril» (0 = por encima: tapa las líneas del carril)
                        float hx = 0f, hy = 0f;
                        foreach (var q in cs) { z.pts.Add(q - c); hx = Mathf.Max(hx, Mathf.Abs(q.x - c.x)); hy = Mathf.Max(hy, Mathf.Abs(q.y - c.y)); }
                        z.x2 = Mathf.Max(0.1f, hx); z.y2 = Mathf.Max(0.1f, hy);
                        m.PlaceDecorItem(z);
                        return true;
                    }
                case 26:   // cabina de peaje: peaje en el carril más cercano
                    {
                        RoadData best = null; float bd = 0.7f, bt = 0.5f;
                        foreach (var r in m.roads)
                        {
                            if (r.laneType != RoadData.LaneVehicle) continue;
                            float dist, t = m.NearestParam(r, p, out dist);
                            if (dist < bd) { bd = dist; best = r; bt = t; }
                        }
                        if (best != null) m.TogglePoint(best.id, bt, PointKind.Toll, 0);
                        return true;
                    }
                case 42:   // botón conectado a semáforos (l0.. = semáforos)
                    {
                        var b = m.AddButton(p);
                        if (b == null) return true;
                        int lc = I(o, "lc");
                        for (int i = 0; i < lc && i < 16; i++)
                        {
                            string lid; KeyValuePair<int, int> lk;
                            if (o.TryGetValue("l" + i, out lid) && lightOf.TryGetValue(lid, out lk)) b.links.Add(new ButtonLink { road = lk.Key, node = lk.Value });
                        }
                        if (b.links.Count == 0) m.buttons.Remove(b);
                        return true;
                    }
                case 1:    // controlador de semáforos con fases (p{i}t = segundos de la fase, p{i}l{j} = semáforos)
                    {
                        int pc = I(o, "pc");
                        for (int i = 0; i < pc && i < 12; i++)
                        {
                            float secs = Mathf.Clamp(F(o, "p" + i + "t", 8f), 2f, 60f);
                            int lc = I(o, "p" + i + "c");
                            for (int j = 0; j < lc && j < 16; j++)
                            {
                                string lid; KeyValuePair<int, int> lk;
                                if (!o.TryGetValue("p" + i + "l" + j, out lid) || !lightOf.TryGetValue(lid, out lk)) continue;
                                var nd = m.Node(lk.Value);
                                if (nd != null) { nd.ctrl = 2; nd.green = secs; }
                            }
                        }
                        return true;
                    }
                case 6:    // luz que destella (ti = segundos); va en su sitio (x, y), no en el punto del carril «p»
                    m.PlaceDecorItem(new DecorItem { x = p.x, y = p.y, type = DecorType.GlowLight, flashPeriod = Mathf.Clamp(F(o, "ti", 1f), 0.2f, 10f), color = 1, size = 0.5f });
                    return true;
                case 5:    // parada de transporte en un punto del carril
                    {
                        Vector2 sp = p;
                        Dictionary<string, string> q;
                        if (o.ContainsKey("p") && objs.TryGetValue(o["p"], out q)) sp = P(F(q, "x"), F(q, "y"));
                        RoadData best = null; float bd = 0.6f, bt = 0.5f;
                        foreach (var r in m.roads)
                        {
                            if (r.laneType != RoadData.LaneVehicle) continue;
                            float dist, t = m.NearestParam(r, sp, out dist);
                            if (dist < bd) { bd = dist; best = r; bt = t; }
                        }
                        if (best != null) m.TogglePoint(best.id, bt, PointKind.BusStop, 0);
                        return true;
                    }
                case 15:   // botón para varios semáforos: tp0.. = semáforos vinculados
                    {
                        var b = m.AddButton(p);
                        if (b == null) return true;
                        int tc = I(o, "tc");
                        for (int i = 0; i < tc && i < 16; i++)
                        {
                            string lid; KeyValuePair<int, int> lk;
                            if (o.TryGetValue("tp" + i, out lid) && lightOf.TryGetValue(lid, out lk)) b.links.Add(new ButtonLink { road = lk.Key, node = lk.Value });
                        }
                        if (b.links.Count == 0) m.buttons.Remove(b);
                        return true;
                    }
                case 41:   // controlador de semáforos: ciclo automático con i segundos por fase en los cruces de sus semáforos
                    {
                        int tc = I(o, "tc");
                        float secs = Mathf.Clamp(F(o, "i", 8f), 2f, 60f);
                        for (int i = 0; i < tc && i < 16; i++)
                        {
                            string lid; KeyValuePair<int, int> lk;
                            if (!o.TryGetValue("tp" + i, out lid) || !lightOf.TryGetValue(lid, out lk)) continue;
                            var nd = m.Node(lk.Value);
                            if (nd != null) { nd.ctrl = 2; nd.green = secs; }
                        }
                        return true;
                    }
            }
            return false;
        }



        /// <summary>
        /// Menú de señales del original (80, 4 por fila) → nuestro catálogo (SignCatalog). -1 = sin equivalente,
        /// -2 = baliza de borde, -10 - k = cartel RoadSign k (0 azul, 1 amarillo, 2 rojo, 3 verde, 4 cartel de autopista).
        /// </summary>
        static readonly int[] SignMap =
        {
            -14, -10, -14, -14,   -10, -14, -13, -10,       // pórticos con carteles
            -13, -10, -12, -10,   -11, -13, -11, -10,       // placas pequeñas y en T
            -1, -1, -1, -1,       -1, -1, -1, -1,           // pórticos y postes vacíos, placa blanca en blanco
            67, 39, 24, 0,        1, 2, 39, 76,             // placa azul, prioridad, peligro, stop | ceda, prohibido, prioridad, fin
            34, 28, 21, 32,       11, 13, 33, 17,           // paso de peatones, peatones, rotonda | giros, aparcar
            56, 58, 29, 29,       30, 24, 74, 74,           // animales, resalto, obras | paso a nivel, peligro, curvas
            31, 36, 51, 50,       18, 53, 25, 26,           // carriles | recto, paso por la derecha, curvas
            27, 27, 52, 59,       38, 73, 38, 73,           // semáforo, doble sentido | autopista, fin de autopista
            -2, -2, 3, 4,         5, 6, 7, 4,               // balizas, velocidades (15, 25, 35, 45, 55, 30)
            6, 40, 41, 42,        -1, 18, 53, 2             // 50, 70, 90, 110 | panel apagado, flechas, aspa roja
        };

        // Colores de la paleta del original (líneas, carriles); 201.. = colores propios del mapa (ccs0.. en la cabecera, RGBA).
        static string TrzColor(int c, Dictionary<string, string> header)
        {
            if (c >= 201 && c < 230)
            {
                string v; long n;
                if (header != null && header.TryGetValue("ccs" + (c - 201), out v) && long.TryParse(v, out n) && n != -1)
                {
                    uint u = (uint)n;
                    return string.Format("#{0:X2}{1:X2}{2:X2}", (u >> 24) & 255, (u >> 16) & 255, (u >> 8) & 255);
                }
                return "";
            }
            switch (c)
            {
                case 1: return "#FFFFFF";
                case 2: return "#F4C20D";
                case 4: return "#3FA34D";
                case 5: return "#D93A2F";
                case 6: return "#3B6FD0";
                case 17: return "#222224";
                default: return "";
            }
        }

        // Colores de edificios vistos en el original (pared 1 = clara, techo 9 = gris); los demás, con nuestra paleta.
        static string TrzWall(int c) { return c == 1 ? "#CFC6BC" : ""; }
        static string TrzRoof(int c) { return c == 9 ? "#8A8D93" : ""; }

        /// <summary>Primera de las 7 flechas de decoración en Sticker2Names (MapRenderer).</summary>
        public const int ArrowSticker0 = 24;

        // Pegatinas: número «v» del archivo → posición en el menú del original (= orden de nuestras pegatinas).
        static readonly int[] StickerV = { 1, 2, 3, 4, 7, 8, 5, 6, 10, 12, 14, 17, 20, 22, 24, 26, 28, 30, 32, 19, 34, 35, 36, 37 };
        static int StickerVariant(int v)
        {
            // Flechas de decoración (vistas en el mapa de prueba): recto, izquierda, derecha, recto e izquierda,
            // recto y derecha, a ambos lados, recto y a ambos lados; 14 = ceda, 17 = bicicleta, 5 = flecha pequeña.
            switch (v)
            {
                case 20: return 6 + ArrowSticker0;
                case 22: return 6 + ArrowSticker0 + 1;
                case 24: return 6 + ArrowSticker0 + 2;
                case 26: return 6 + ArrowSticker0 + 3;
                case 28: return 6 + ArrowSticker0 + 4;
                case 30: return 6 + ArrowSticker0 + 5;
                case 32: return 6 + ArrowSticker0 + 6;
                case 14: return 6 + 10;
                case 17: return 6 + 11;
                case 5: return 6 + 6;
            }
            int best = 0, bd = int.MaxValue;
            for (int i = 0; i < StickerV.Length; i++)
            {
                int d = Mathf.Abs(StickerV[i] - v);
                if (d < bd) { bd = d; best = i; }
            }
            return 6 + best;
        }

        /// <summary>
        /// Flecha (ao, as, arc) y marca (li, is, icc, it) de un carril del original, cada una en su sitio exacto:
        /// as = fracción del largo; is = distancia desde el inicio (píxeles del original). ao = cifras: 1 izquierda,
        /// 10 recto, 100 derecha, 1000 giro leve a la izquierda, 10000 giro leve a la derecha (se suman).
        /// </summary>
        static void LaneMarks(MapData m, Dictionary<string, string> s, List<string> pts, Dictionary<string, Dictionary<string, string>> objs,
            Func<float, float, Vector2> P, List<RoadData> roads, int laneType)
        {
            var line = new List<Vector2>();
            foreach (var id in pts) line.Add(P(F(objs[id], "x"), F(objs[id], "y")));
            float total = 0f;
            for (int i = 1; i < line.Count; i++) total += (line[i] - line[i - 1]).magnitude;
            if (total < 0.01f) return;
            Func<float, Vector2> At = d =>
            {
                d = Mathf.Clamp(d, 0f, total);
                for (int i = 1; i < line.Count; i++)
                {
                    float l = (line[i] - line[i - 1]).magnitude;
                    if (d <= l || i == line.Count - 1) return Vector2.Lerp(line[i - 1], line[i], l > 1e-4f ? Mathf.Clamp01(d / l) : 0f);
                    d -= l;
                }
                return line[line.Count - 1];
            };
            // Carril (de los trozos de este carril) y posición t en él más cercanos a un punto.
            Func<Vector2, KeyValuePair<RoadData, float>> Near = q =>
            {
                RoadData best = roads[0]; float bt = 0.5f, bd = float.MaxValue;
                foreach (var r in roads)
                {
                    float dist, t = m.NearestParam(r, q, out dist);
                    if (dist < bd) { bd = dist; best = r; bt = t; }
                }
                return new KeyValuePair<RoadData, float>(best, bt);
            };
            bool drives = laneType == RoadData.LaneVehicle || laneType == RoadData.LaneBike;
            int ao = I(s, "ao", 0);
            if (drives && ao > 0)
            {
                int moves = 0, style = 0;
                if (ao % 10 != 0) moves |= 1;                 // izquierda
                if ((ao / 10) % 10 != 0) moves |= 2;          // recto
                if ((ao / 100) % 10 != 0) moves |= 4;         // derecha
                if ((ao / 1000) % 10 != 0) { moves |= 1; style = 1; }    // giro leve a la izquierda
                if ((ao / 10000) % 10 != 0) { moves |= 4; style = 1; }   // giro leve a la derecha
                if (moves != 0)
                {
                    var nr = Near(At(Mathf.Clamp01(F(s, "as", 0.5f)) * total));
                    m.arrows.Add(new LaneArrowItem
                    {
                        id = m.NewObjectId(), road = nr.Key.id, forward = true, lane = 0, t = Mathf.Clamp01(nr.Value), moves = moves, style = style,
                        color = I(s, "arc", 1) == 2 ? 1 : 0, deco = true   // como el original: solo pintada, no cambia por dónde van los coches
                    });
                }
            }
            int li = I(s, "li", 0);
            string it;
            bool hasText = s.TryGetValue("it", out it) && !string.IsNullOrEmpty(it);
            if (drives && (li > 0 || hasText))
            {
                var nr = Near(At(F(s, "is", total / K * 0.1f) * K));
                var r = nr.Key;
                r.markKind = li > 0 ? li : RoadData.MarkText;
                r.markT = Mathf.Clamp01(nr.Value);
                r.markColor = I(s, "icc", 1) == 2 ? 1 : 0;
                if (hasText)
                {
                    it = it.Replace("\\n", " ").Trim();
                    while (it.Contains("  ")) it = it.Replace("  ", " ");
                    if (it.Length > 20) it = it.Substring(0, 20);
                    r.roadText = it;
                }
            }
        }

        /// <summary>
        /// Aspecto y reglas de un carril del original: sl/sr = se puede cambiar de carril, omc ausente = línea continua
        /// con el sentido contrario, blc 8 = sin bordes, a0.. = vehículos permitidos (1 coches, 3 camiones, 5 autobuses;
        /// 27 siempre), lc = color del carril, s = velocidad (unos 0,32 km/h por unidad).
        /// </summary>
        /// <summary>
        /// Líneas de un carril (también bici y berma): cada carril pinta la de su lado izquierdo. omc ausente = amarilla
        /// continua; 15 = automática (el original la pinta amarilla discontinua si se puede cambiar de carril). smc 2 =
        /// amarilla en el lado izquierdo. blc 8 = sin bordes, 4/5 blancos, 6/7 amarillos.
        /// </summary>
        static void LaneLines(RoadData r, Dictionary<string, string> s)
        {
            if (I(s, "sl", 1) == 0 && I(s, "sr", 1) == 0) r.noLaneChange = true;
            if (!s.ContainsKey("omc")) r.lineOpp = 1;
            else if (I(s, "omc") == 15 && !r.noLaneChange) r.lineOpp = 4;
            if (I(s, "smc", 0) == 2) r.lineSame = r.noLaneChange ? 4 : 3;
            int blc = I(s, "blc", 4);
            if (blc == 8) r.lineEdge = 0; else if (blc == 6 || blc == 7) r.lineEdge = 2; else r.lineEdge = 1;
        }

        static void LaneLook(RoadData r, Dictionary<string, string> s, Dictionary<string, string> header)
        {
            LaneLines(r, s);
            // a0.. = tráfico permitido (a veces sin «ac»: se cuentan los que hay). Ninguno de coche, camión o bus (solo el 27,
            // o «ac» = 0) = todo desactivado: el carril queda cerrado (no pasa ni sale nada, como el original). Sin a0 ni ac
            // (mapas antiguos) = todo permitido.
            int ac = s.ContainsKey("ac") ? I(s, "ac") : 0, mask = 0;
            if (!s.ContainsKey("ac")) while (ac < 8 && s.ContainsKey("a" + ac)) ac++;
            bool car = false, truck = false, bus = false;
            for (int i = 0; i < ac && i < 8; i++)
            {
                int a = I(s, "a" + i, 27);
                if (a == 1) car = true; else if (a == 3) truck = true; else if (a == 5) bus = true;
            }
            if ((s.ContainsKey("ac") || ac > 0) && !car && !truck && !bus) { r.allow = RoadData.ClosedMask; return; }
            if (ac > 0 && !(car && !truck && !bus && ac == 2))
            {
                if (car) mask |= (1 << (int)VehicleType.Car) | (1 << (int)VehicleType.Taxi) | (1 << (int)VehicleType.Motorbike) | (1 << (int)VehicleType.Van);
                if (truck) mask |= 1 << (int)VehicleType.Truck;
                if (bus) mask |= (1 << (int)VehicleType.Bus) | (1 << (int)VehicleType.Taxi);
                if (mask != 0) r.allow = mask | (1 << (int)VehicleType.Ambulance) | (1 << (int)VehicleType.FireTruck) | (1 << (int)VehicleType.Police);
            }
            if (s.ContainsKey("lc")) r.colorHex = LaneTint(TrzColor(I(s, "lc"), header));
            float sp = F(s, "s", 0f) * 0.322f;   // 174 ≈ 60, 273 ≈ 90, 373 ≈ 120 km/h
            if (sp > 1f)
            {
                int best = 50;
                foreach (int k in RoadData.SpeedSteps) if (k > 0 && Mathf.Abs(k - sp) < Mathf.Abs(best - sp)) best = k;
                if (best != 50) r.speedKmh = best;
            }
        }

        // El color del carril tiñe el asfalto (más oscuro que el color puro).
        static string LaneTint(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length != 7) return "";
            int[] g = { 64, 66, 71 };
            var sb = new StringBuilder("#");
            for (int i = 0; i < 3; i++)
            {
                int v = Convert.ToInt32(hex.Substring(1 + i * 2, 2), 16);
                sb.Append(Mathf.RoundToInt(v * 0.65f + g[i] * 0.35f).ToString("X2"));
            }
            return sb.ToString();
        }


        /// <summary>Carriles bici que son bermas: grupos unidos sin ningún inicio con bicicletas (st0 &gt; 0).</summary>
        static HashSet<Dictionary<string, string>> BermaLanes(List<Dictionary<string, string>> segs, Dictionary<string, Dictionary<string, string>> objs)
        {
            var bikes = new List<Dictionary<string, string>>();
            foreach (var s in segs) if (I(s, "stid", 1) == 7) bikes.Add(s);
            var result = new HashSet<Dictionary<string, string>>();
            if (bikes.Count == 0) return result;
            // Unión de carriles que comparten un extremo.
            var parent = new int[bikes.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            Func<int, int> Root = null;
            Root = i => parent[i] == i ? i : (parent[i] = Root(parent[i]));
            var byEnd = new Dictionary<string, int>();
            var incoming = new HashSet<string>();
            for (int i = 0; i < bikes.Count; i++)
            {
                int nc = I(bikes[i], "nc");
                string a, b;
                if (!bikes[i].TryGetValue("n0", out a) || !bikes[i].TryGetValue("n" + (nc - 1), out b)) continue;
                incoming.Add(b);
                foreach (var e in new[] { a, b })
                {
                    int j;
                    if (byEnd.TryGetValue(e, out j)) parent[Root(i)] = Root(j); else byEnd[e] = i;
                }
            }
            var withBikes = new HashSet<int>();
            for (int i = 0; i < bikes.Count; i++)
            {
                string a; Dictionary<string, string> o;
                if (!bikes[i].TryGetValue("n0", out a) || incoming.Contains(a) || !objs.TryGetValue(a, out o)) continue;
                if (F(o, "st0") > 0.1f) withBikes.Add(Root(i));
            }
            for (int i = 0; i < bikes.Count; i++) if (!withBikes.Contains(Root(i))) result.Add(bikes[i]);
            return result;
        }

        const float K = 0.5f / 36f;

        static Dictionary<string, string> Parse(string line)
        {
            var kv = new Dictionary<string, string>();
            foreach (var part in line.Split(';'))
            {
                int i = part.IndexOf(':');
                if (i > 0) kv[part.Substring(0, i)] = part.Substring(i + 1);
            }
            return kv;
        }

        static float F(Dictionary<string, string> kv, string k, float def = 0f)
        {
            string v; float r;
            return kv.TryGetValue(k, out v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : def;
        }

        static int I(Dictionary<string, string> kv, string k, int def = 0) { return Mathf.RoundToInt(F(kv, k, def)); }

        public static bool LooksLikeTrz(byte[] data) { return data != null && data.Length > 2 && data[0] == 0x1f && data[1] == 0x8b; }

        public static string Decompress(byte[] data)
        {
            using (var input = new MemoryStream(data))
            using (var gz = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buf = new byte[8192]; int n;
                while ((n = gz.Read(buf, 0, buf.Length)) > 0)
                {
                    output.Write(buf, 0, n);
                    if (output.Length > 40 * 1024 * 1024) throw new Exception("archivo demasiado grande");
                }
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }

        public static bool TryConvert(byte[] data, string name, out MapData map, out string error)
        {
            map = null; error = "";
            string text;
            try { text = LooksLikeTrz(data) ? Decompress(data) : Encoding.UTF8.GetString(data); }
            catch (Exception ex) { error = "No se pudo leer el archivo: " + ex.Message; return false; }
            return TryConvertText(text, name, out map, out error);
        }

        public static bool TryConvertText(string text, string name, out MapData map, out string error)
        {
            map = null; error = "";
            var objs = new Dictionary<string, Dictionary<string, string>>();
            Dictionary<string, string> header = null;
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                var kv = Parse(line);
                string type, id;
                if (!kv.TryGetValue("type", out type)) continue;
                if (type == "4") { header = kv; continue; }
                if (kv.TryGetValue("id", out id)) objs[id] = kv;
            }
            var segs = new List<Dictionary<string, string>>();
            foreach (var o in objs.Values) if (o["type"] == "2") segs.Add(o);
            if (segs.Count == 0) { error = "El archivo no tiene carriles de Intersection Controller."; return false; }

            // Zona visible del original (si no, la caja de todos los puntos) → al centro del terreno de 200 × 200.
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var o in objs.Values)
            {
                if (o["type"] != "0") continue;
                float x = F(o, "x"), y = F(o, "y");
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            if (header != null && header.ContainsKey("tlx") && header.ContainsKey("brx"))
            {
                minX = F(header, "tlx"); minY = F(header, "tly"); maxX = F(header, "brx"); maxY = F(header, "bry");
            }
            Vector2 center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            float B = MapData.BigSize;
            Func<float, float, Vector2> P = (x, y) => new Vector2((x - center.x) * K + B * 0.5f, -(y - center.y) * K + B * 0.5f);

            var m = new MapData
            {
                id = MapData.NewId(), name = string.IsNullOrEmpty(name) ? "Mapa importado" : name, author = "Jugador",
                width = B, height = B, featureLevel = MapData.CurrentFeatureLevel, laneFormat = MapData.LaneFormatCurrent,
            };
            float vw = Mathf.Max(4f, (maxX - minX) * K), vh = Mathf.Max(3f, (maxY - minY) * K);
            m.SetView(new Rect(B * 0.5f - vw * 0.5f, B * 0.5f - vh * 0.5f, vw, vh));
            if (header != null)
            {
                float st = F(header, "st", -1f);   // minutos desde la medianoche
                if (st >= 0f && st < 1440f) m.startHour = st / 60f;
                // Modo (m), objetivo de vehículos (t), tiempo en segundos (pt) y probabilidad de lluvia (tvp).
                int mode = I(header, "m", 1);
                m.playMode = mode == 2 ? 1 : 2;
                int goal = I(header, "t", 0);
                if (goal > 0) m.goalVehicles = Mathf.Clamp(goal, 1, 999);
                float secs = F(header, "pt", 0f);
                if (secs > 0f) m.goalMinutes = Mathf.Clamp(secs / 60f, 1f, 60f);
                m.rainChance = Mathf.Clamp01(F(header, "tvp", 0f));
                float amount = F(header, "s", 1f);   // cantidad de tráfico del mapa
                if (amount > 0.05f) m.trafficAmount = Mathf.Clamp(amount, 0.25f, 3f);
            }

            // Nodos: solo los extremos de los carriles (los puntos intermedios son la forma de la curva).
            var nodeOf = new Dictionary<string, int>();
            Func<string, int> NodeFor = oid =>
            {
                int nid;
                if (nodeOf.TryGetValue(oid, out nid)) return nid;
                Dictionary<string, string> o;
                if (!objs.TryGetValue(oid, out o)) return -1;
                Vector2 p = P(F(o, "x"), F(o, "y"));
                // Puntos casi en el mismo sitio (el original a veces repite): un solo nodo.
                foreach (var n in m.nodes) if ((n.Pos - p).sqrMagnitude < 0.0004f) { nodeOf[oid] = n.id; return n.id; }
                var nd = m.AddNode(p);
                if (nd == null) return -1;
                nodeOf[oid] = nd.id;
                return nd.id;
            };

            int made = 0, skipped = 0;
            var lightAt = new List<KeyValuePair<int, int>>();
            var endRoadOf = new Dictionary<string, int>();
            var lightOf = new Dictionary<string, KeyValuePair<int, int>>();   // punto del original con semáforo → (carril, nodo)   // punto final del original → último carril que llega a él
            // Bermas: el jugador usa carriles bici con 0 bicicletas por minuto en su nodo de inicio (st0 = 0) como berma de
            // la calle. Cada grupo de carriles bici unidos cuyos inicios tienen st0 = 0 se dibuja como berma de asfalto, sin bicis.
            var berma = BermaLanes(segs, objs);
            foreach (var s in segs)
            {
                int nc = I(s, "nc");
                var pts = new List<string>();
                for (int i = 0; i < nc; i++) { string v; if (s.TryGetValue("n" + i, out v) && objs.ContainsKey(v)) pts.Add(v); }
                if (pts.Count < 2) { skipped++; continue; }
                int laneType;
                switch (I(s, "stid", 1))
                {
                    case 2: laneType = RoadData.LaneSidewalk; break;
                    case 7 when berma.Contains(s): laneType = RoadData.LaneShoulder; break;   // carril bici sin bicis = berma
                    case 6: laneType = RoadData.LaneRail; break;
                    case 7: laneType = RoadData.LaneBike; break;
                    default: laneType = RoadData.LaneVehicle; break;
                }
                // Curvas largas: en trozos de hasta 4 tramos, cada uno con su curva (bézier por el punto del medio).
                int pieceLen = 4;
                int start = 0;
                // Capa (rl) de los extremos: 0 suelo, 1 y 2 por encima (puentes, aceras elevadas). Entre dos capas distintas,
                // los tramos suben o bajan de una en una (rampas).
                int la = Mathf.Clamp(I(objs[pts[0]], "rl"), 0, 4), lb = Mathf.Clamp(I(objs[pts[pts.Count - 1]], "rl"), 0, 4), curLayer = la;
                var segRoads = new List<RoadData>();
                while (start < pts.Count - 1)
                {
                    int end = Mathf.Min(pts.Count - 1, start + pieceLen);
                    if (pts.Count - 1 - end > 0 && pts.Count - 1 - end < 2) end = pts.Count - 1;   // sin trocito final suelto
                    string ida = pts[start], idb = pts[end];
                    var oa = objs[ida]; var ob = objs[idb];
                    Vector2 pa = P(F(oa, "x"), F(oa, "y")), pb = P(F(ob, "x"), F(ob, "y"));
                    int na = start == 0 ? NodeFor(ida) : NodeFor(ida), nb = NodeFor(idb);
                    if (na < 0 || nb < 0) { skipped++; start = end; continue; }
                    var r = m.AddRoad(na, nb, 1, 0);
                    if (r == null) { skipped++; start = end; continue; }
                    r.laneType = laneType;
                    segRoads.Add(r);
                    if (I(s, "v", 1) == 0) r.hidden = true;   // «Visible» apagado en el original
                    {
                        int want = Mathf.RoundToInt(Mathf.Lerp(la, lb, (float)end / (pts.Count - 1)));
                        r.layer = curLayer;
                        r.ramp = Mathf.Clamp(want - curLayer, -1, 1);
                        curLayer += r.ramp;
                    }
                    if (laneType == RoadData.LaneVehicle) LaneLook(r, s, header);
                    if (laneType == RoadData.LaneShoulder) { float bw = F(s, "w", 18f) * K; if (Mathf.Abs(bw - 0.25f) > 0.02f) r.laneWidth = Mathf.Clamp(bw, 0.15f, 1.5f); }
                    // Como el original, el carril bici (y la berma, que lo es) pinta sus propias líneas.
                    if (laneType == RoadData.LaneBike || laneType == RoadData.LaneShoulder) LaneLines(r, s);
                    // Evitar (b) y texto pintado en la calzada (it), como en el original.
                    if (I(s, "b", 0) != 0) r.avoid = true;
                    if (end - start >= 2)
                    {
                        var mid = objs[pts[(start + end) / 2]];
                        Vector2 pm = P(F(mid, "x"), F(mid, "y"));
                        if ((end - start) % 2 == 1)
                        {
                            var m2 = objs[pts[(start + end) / 2 + 1]];
                            pm = (pm + P(F(m2, "x"), F(m2, "y"))) * 0.5f;
                        }
                        Vector2 bend = 2f * (pm - (pa + pb) * 0.5f);
                        if (bend.sqrMagnitude > 0.0025f) { r.bendX = bend.x; r.bendY = bend.y; }
                    }
                    made++;
                    if (end == pts.Count - 1 && ob.ContainsKey("tlv") && laneType != RoadData.LaneRail)
                    {
                        lightAt.Add(new KeyValuePair<int, int>(r.id, nb));
                        lightOf[idb] = new KeyValuePair<int, int>(r.id, nb);
                    }
                    if (end == pts.Count - 1) endRoadOf[idb] = r.id;
                    start = end;
                }
                if (segRoads.Count > 0) LaneMarks(m, s, pts, objs, P, segRoads, laneType);
            }
            if (made == 0) { error = "No se pudo crear ningún carril del archivo."; return false; }
            foreach (var kv in lightAt) if (m.FindSign(kv.Key, kv.Value, SignType.Light) == null) m.ToggleSign(kv.Key, kv.Value, SignType.Light);

            // Puntos con datos: ceda el paso / stop (ysm / ssm), controlado por el usuario (us), farola (l),
            // tráfico de los nodos de inicio (st0..: segundos entre vehículos de cada tipo) y destinos (td{i}e / td{i}w).
            var lamps = new List<Vector2>();
            float pedRate = 0f; int pedNodes = 0;
            foreach (var kvp in objs)
            {
                var o = kvp.Value;
                if (o["type"] != "0") continue;
                int nid;
                bool isNode = nodeOf.TryGetValue(kvp.Key, out nid);
                int rid;
                if (isNode && endRoadOf.TryGetValue(kvp.Key, out rid))
                {
                    if (I(o, "ssm") != 0 && m.FindSign(rid, nid, SignType.Stop) == null) m.ToggleSign(rid, nid, SignType.Stop);
                    else if (I(o, "ysm") != 0 && m.FindSign(rid, nid, SignType.Yield) == null) m.ToggleSign(rid, nid, SignType.Yield);
                }
                var nd = isNode ? m.Node(nid) : null;
                if (nd != null && I(o, "us") != 0) nd.ctrl = 1;
                if (I(o, "l") != 0 && isNode && endRoadOf.TryGetValue(kvp.Key, out rid))
                {
                    // La farola va junto al carril (a su derecha), no encima.
                    var lr = m.Road(rid);
                    Vector2 lp = P(F(o, "x"), F(o, "y"));
                    if (lr != null)
                    {
                        Vector2 dir = (m.PointOnRoad(lr, 1f) - m.PointOnRoad(lr, 0.9f)).normalized;
                        lp += new Vector2(dir.y, -dir.x) * 0.5f;
                    }
                    bool near = false;
                    foreach (var q in lamps) if ((q - lp).sqrMagnitude < 1f) { near = true; break; }
                    if (!near && m.InBounds(lp)) { lamps.Add(lp); m.PlaceDecorItem(new DecorItem { x = lp.x, y = lp.y, type = DecorType.Lamp }); }
                }
                if (nd == null) continue;
                // Nodo de inicio de coches: st0 coches, st1 camiones, st2 buses, st3 tranvías (segundos entre vehículos;
                // 0 = ninguno). Se traen todos, también los que están en 0.
                if (o.ContainsKey("st0") && (I(o, "stid", 1) == 1))
                {
                    int[] rateIdx = { 0, 1, 2, 4 };
                    for (int t = 0; t < 4; t++)
                    {
                        float sec = F(o, "st" + t, 0f);
                        nd.SetRate(rateIdx[t], sec > 0.1f ? Mathf.Clamp(60f / sec, 0f, 60f) : 0f);
                    }
                    nd.SetRate(3, 0f);   // el original no tiene motos aparte
                }
                // Nodo de inicio de aceras: peatones por minuto (se suman al total de peatones del mapa).
                if (I(o, "stid", 1) == 2 && o.ContainsKey("st0"))
                {
                    float sec = F(o, "st0", 0f);
                    if (sec > 0.1f) pedRate += 60f / sec;
                    pedNodes++;
                }
                for (int t = 0; t < 32; t++)
                {
                    string de;
                    if (!o.TryGetValue("td" + t + "e", out de)) continue;
                    int dn;
                    if (!nodeOf.TryGetValue(de, out dn)) continue;
                    nd.dest.Add(new DestWeight { node = dn, weight = Mathf.Clamp(F(o, "td" + t + "w", 50f), 0f, 100f) });
                }
            }

            if (pedNodes > 0) m.traffic.pedestriansPerMinute = Mathf.Clamp(pedRate, 0f, 60f);   // como el original: la suma de sus aceras

            // Decoración: árboles (stid 2, tt = tipo, s = tamaño), edificios (stid 3, esquinas c1..c4),
            // zonas rectangulares (stid 34: de (x, y) a (ox, oy), f = tipo) y caminos (stid 46: ptx/pty o de (x, y) a (ox, oy)).
            foreach (var o in objs.Values)
            {
                if (o["type"] != "3") continue;
                int st = I(o, "stid", -1);
                Vector2 p = P(F(o, "x"), F(o, "y"));
                if (!m.InBounds(p)) continue;
                if (st == 34)
                {
                    // Valla (opción «Valla» de la decoración): de (x, y) a (ox, oy); f = tipo 1..11 del menú del original.
                    Vector2 q = P(F(o, "ox"), F(o, "oy"));
                    float len = (q - p).magnitude;
                    if (len < 0.05f) continue;
                    int[] fmap = { 0, 0, 2, 1, 3, 4, 5, 6, 7, 8, 9, 10 };
                    int fk = fmap[Mathf.Clamp(I(o, "f", 1), 0, 11)];
                    int pieces = Mathf.Max(1, Mathf.CeilToInt(len / 2.6f));   // cada trozo hasta 2,7 de largo (tamaño 3)
                    float ang = Mathf.Atan2(q.y - p.y, q.x - p.x) * Mathf.Rad2Deg + 0.001f;
                    for (int i = 0; i < pieces; i++)
                    {
                        Vector2 c = Vector2.Lerp(p, q, (i + 0.5f) / pieces);
                        m.PlaceDecorItem(new DecorItem { x = c.x, y = c.y, type = DecorType.Fence, variant = fk, rot = ang, size = len / pieces / 0.9f });
                    }
                    continue;
                }
                if (st == 46)
                {
                    // Líneas pintadas en la calzada (stop, ceda…): una línea por tramo.
                    var line = new List<Vector2>();
                    int pc = I(o, "pc");
                    for (int i = 0; i < pc && i < 64; i++) if (o.ContainsKey("ptx" + i)) line.Add(P(F(o, "ptx" + i), F(o, "pty" + i)));
                    if (line.Count < 2) { line.Clear(); line.Add(p); line.Add(P(F(o, "ox"), F(o, "oy"))); }
                    // f = estilo 1..8 (continua, fina, punteada, discontinua, rayas cortas, rayas largas, triángulos, gruesa), c = color.
                    int[] fstyle = { 0, 0, 4, 5, 1, 6, 7, 8, 9 };   // líneas 1..8 del original → nuestros estilos
                    int style = fstyle[Mathf.Clamp(I(o, "f", 1), 0, 8)];
                    int col = 0;
                    string lhex = TrzColor(I(o, "c", 1), header);
                    if (lhex == "") lhex = "#FFFFFF";   // blanca explícita: sin color, algunos estilos se dibujan amarillos
                    if (style == 8) line.Reverse();     // triángulos: con la punta hacia el mismo lado que en el original
                    for (int i = 0; i + 1 < line.Count; i++)
                    {
                        if ((line[i + 1] - line[i]).sqrMagnitude < 0.0004f) continue;
                        m.PlaceDecorItem(new DecorItem { x = line[i].x, y = line[i].y, x2 = line[i + 1].x, y2 = line[i + 1].y, type = DecorType.Lines, variant = style, color = col, size = 0.8f, hexA = lhex });
                    }
                    continue;
                }
                if (ImportMore(m, o, st, p, P, objs, lightOf, made, header)) continue;
                if (st == 2)
                    m.PlaceDecorItem(new DecorItem { x = p.x, y = p.y, type = DecorType.Tree, variant = Mathf.Clamp(I(o, "tt"), 0, 3), size = Mathf.Clamp(F(o, "s", 0.3f) / 0.3f, 0.4f, 3f) });
                else if (st == 3 && o.ContainsKey("c1x"))
                {
                    Vector2 c1 = P(F(o, "c1x"), F(o, "c1y")), c2 = P(F(o, "c2x"), F(o, "c2y")), c3 = P(F(o, "c3x"), F(o, "c3y")), c4 = P(F(o, "c4x"), F(o, "c4y"));
                    Vector2 c = (c1 + c2 + c3 + c4) * 0.25f;
                    float w = ((c2 - c1).magnitude + (c3 - c4).magnitude) * 0.5f, h = ((c4 - c1).magnitude + (c3 - c2).magnitude) * 0.5f;
                    var d = new DecorItem
                    {
                        x = c.x, y = c.y, type = DecorType.Building, size = 1f,
                        rot = Mathf.Atan2((c2 - c1).y, (c2 - c1).x) * Mathf.Rad2Deg,
                        x2 = w * 0.5f, y2 = h * 0.5f,   // edificio rectangular (semiancho y semialto)
                        height = Mathf.Clamp(I(o, "f", I(o, "h", 2)), 1, 10), roof = I(o, "rt") == 1 ? 0 : 2,   // como el original: tejado a cuatro aguas (rt 1 = dos aguas)
                        lowHeight = I(o, "d") == 1,       // «Reducir altura»
                        wall = I(o, "wt") > 0 ? 1 : 0, color = Mathf.Abs(I(o, "wc")) % 8, roofColor = Mathf.Abs(I(o, "rc")) % 8,
                        hexA = TrzWall(I(o, "wc", -1)), hexB = TrzRoof(I(o, "rc", -1))
                    };
                    if (d.rot == 0f) d.rot = 0.001f;
                    m.PlaceDecorItem(d);
                }
            }
            m.Normalize();
            map = m;
            error = made + " carriles importados" + (skipped > 0 ? " (" + skipped + " tramos omitidos)" : "") + ".";
            return true;
        }
    }
}
