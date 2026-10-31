using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Data;
using IntersectionMaster.Traffic;
using IntersectionMaster.Systems;

namespace IntersectionMaster.Core
{
    /// <summary>
    /// Dibujo del modelo por carriles (como Intersection Controller 2): cada carril es una franja de su tipo
    /// (asfalto, acera, vía de tren, carril bici o agua). Los carriles contiguos forman la calzada; donde se unen
    /// en un nodo se rellena la junta. En el editor se ven los chevrones de sentido.
    /// </summary>
    public partial class MapRenderer
    {
        static Texture2D grainTex;

        /// <summary>Textura de grano del asfalto: ruido gris con algunas manchas, repetible.</summary>
        static Texture2D GrainTexture
        {
            get
            {
                if (grainTex != null) return grainTex;
                const int N = 128;
                var px = new Color[N * N];
                var rnd = new System.Random(5);
                for (int i = 0; i < px.Length; i++)
                {
                    float g = (float)rnd.NextDouble();
                    float v = g < 0.5f ? 0f : 1f;
                    px[i] = new Color(v, v, v, Mathf.Abs(g - 0.5f) * 1.2f);
                }
                // Manchas suaves (reparaciones y desgaste) que se repiten de forma poco visible.
                for (int b = 0; b < 6; b++)
                {
                    int cx = rnd.Next(N), cy = rnd.Next(N), r = 8 + rnd.Next(14);
                    for (int y = -r; y <= r; y++)
                        for (int x = -r; x <= r; x++)
                        {
                            float d = Mathf.Sqrt(x * x + y * y) / r;
                            if (d > 1f) continue;
                            int ix = (cx + x + N) % N, iy = (cy + y + N) % N;
                            var c = px[iy * N + ix];
                            px[iy * N + ix] = new Color(0f, 0f, 0f, Mathf.Max(c.a, 0.25f * (1f - d)));
                        }
                }
                grainTex = new Texture2D(N, N, TextureFormat.RGBA32, false);
                grainTex.SetPixels(px);
                grainTex.filterMode = FilterMode.Bilinear;
                grainTex.wrapMode = TextureWrapMode.Repeat;
                grainTex.Apply();
                return grainTex;
            }
        }
        static readonly Color Yellow = new Color32(236, 196, 52, 255);

        public static readonly string[] AsphaltNames = { "Normal", "Gris", "Gris oscuro", "Azulado", "Cálido", "Muy claro" };
        public static readonly string[] AsphaltHex = { "#8E8E8E", "#7A7C80", "#5F6166", "#7C838D", "#8A857D", "#A3A3A1" };
        /// <summary>Color del asfalto al jugar (Opciones → Color del asfalto).</summary>
        public static Color PlayAsphalt() { return SpriteFactory.Hex(AsphaltHex[Mathf.Clamp(SaveSystem.Progress.asphaltColor, 0, AsphaltHex.Length - 1)]); }

        /// <summary>
        /// Franja de una berma respecto a su línea central: el borde que da a la calzada queda donde estaría con el ancho
        /// normal (0,25) y el resto del ancho va hacia afuera.
        /// </summary>
        void ShoulderBand(NetRoad r, RoadNetworkManager net, out float lo, out float hi)
        {
            float w = r.data.Width, n0 = RoadData.WidthOf(RoadData.LaneShoulder) * 0.5f;
            float mid = r.length * 0.5f;
            Vector2 c = r.center.PointAt(mid), rt = Geo.Right(r.center.TangentAt(mid).normalized);
            float dR = float.MaxValue, dL = float.MaxValue;
            foreach (var o in net.roads)
            {
                if (o == r || o.data.laneType != RoadData.LaneVehicle) continue;
                float d1, d2;
                o.center.Project(c + rt * 0.3f, out d1);
                o.center.Project(c - rt * 0.3f, out d2);
                dR = Mathf.Min(dR, d1); dL = Mathf.Min(dL, d2);
            }
            if (dR <= dL) { hi = n0; lo = n0 - w; }   // la calzada está a la derecha: crece hacia la izquierda
            else { lo = -n0; hi = -n0 + w; }
        }

        Color LaneColor(RoadData d, bool editorMode)
        {
            if (RoadData.ValidHex(d.colorHex)) { var c = SpriteFactory.Hex(d.colorHex); if (d.laneType == RoadData.LaneBoat) c.a = 0.9f; return c; }
            // El color elegido en la paleta (1..n) vale para todos los tipos, no solo para el asfalto.
            if (d.laneType != RoadData.LaneVehicle && d.laneType != RoadData.LaneBoat && d.color > 0 && d.color < RoadColors.Length)
                return SpriteFactory.Hex(RoadColors[d.color]);
            switch (d.laneType)
            {
                case RoadData.LaneSidewalk: return editorMode ? SpriteFactory.Hex("#F2EEE8") : Color.Lerp(sidewalk, Color.white, 0.35f);
                case RoadData.LaneRail: return SpriteFactory.Hex("#8A7A68");
                case RoadData.LaneBike: return SpriteFactory.Hex("#5E8F62");
                case RoadData.LaneShoulder: return editorMode ? asphalt : PlayAsphalt();   // berma: del color de la calzada
                case RoadData.LaneBoat: return new Color(0.27f, 0.55f, 0.78f, 0.9f);
                default:
                    if (d.color > 0 && d.color < RoadColors.Length) return SpriteFactory.Hex(RoadColors[d.color]);
                    // Como en la referencia: calzada gris clara también al jugar (el tono se elige en Opciones).
                    return editorMode ? asphalt : PlayAsphalt();
            }
        }

        // Capas -4..4: índice = capa + 4. Orden de dibujo coherente con el de los vehículos (VehicleController.LayerOrderOffset).
        const int LaneLayers = MapData.MaxLayer - MapData.MinLayer + 1;
        static int LI(int layer) { return Mathf.Clamp(layer - MapData.MinLayer, 0, LaneLayers - 1); }
        /// <summary>Editor: capa en la que se trabaja. Como el original, con una capa por encima del suelo elegida, lo demás
        /// (el suelo, los objetos y las capas de más arriba) se ve oscurecido.</summary>
        public static int EditLayer;
        /// <summary>Tráfico por la izquierda (Ajustes del mapa → General → Dirección del tráfico).</summary>
        public static bool LeftHand;

        void RoadTextLabel(string text, Vector2 pos, Vector2 dir, float laneW, int order, Color? color = null, float stretch = 2.2f, float maxH = 0.5f)
        {
            var go = new GameObject("RoadText");
            go.transform.SetParent(roadRoot, false);
            go.transform.localPosition = new Vector3(pos.x, pos.y, 0f);
            // La parte de arriba de las letras mira hacia adelante (como se pinta en la vida real) y se estiran a lo largo.
            go.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
            var tm = go.AddComponent<TextMesh>();
            var font = IntersectionMaster.UI.UIKit.DefaultFont;
            tm.font = font;
            tm.text = text;
            tm.fontSize = 64;
            tm.fontStyle = FontStyle.Bold;
            float chars = Mathf.Max(2, text.Length);
            // Alto de letra (antes de estirar): que el texto entre a lo ancho del carril.
            float h = Mathf.Min(laneW * maxH, laneW * 0.85f / (0.6f * chars));
            tm.characterSize = h / 6.4f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color ?? new Color(0.96f, 0.96f, 0.94f, 0.95f);
            go.transform.localScale = new Vector3(1f, stretch, 1f);
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) { if (font != null) mr.sharedMaterial = font.material; mr.sortingOrder = order; }
        }

        /// <summary>
        /// Marca pintada en el carril (como «Marcas» del original), en su posición (markT) y color: velocidad del carril en
        /// un círculo, BUS, rombo, tranvía, ceda, texto, incorporación o carriles. Sin marca, el texto suelto en el medio.
        /// </summary>
        void DrawLaneMark(MeshBatch m, NetRoad r)
        {
            var d = r.data;
            float sm = r.length * (d.markT >= 0f ? Mathf.Clamp01(d.markT) : 0.5f);
            Vector2 p = r.center.PointAt(sm), t = r.center.TangentAt(sm).normalized, rt = Geo.Right(t);
            float w = r.halfWidth * 2f;
            int order = RoadOrder(DrawLayer(d)) + 3;
            Color col = d.markColor == 1 ? Yellow : new Color(0.96f, 0.96f, 0.94f, 0.95f);
            const float lw = 0.03f;
            switch (d.markKind)
            {
                case 1:   // velocidad: el número de la velocidad configurada, dentro de un círculo (en el menú siempre dice 50)
                    {
                        float rr = w * 0.36f;
                        for (int i = 0; i < 28; i++)
                        {
                            float a0 = i / 28f * Mathf.PI * 2f, a1 = (i + 1) / 28f * Mathf.PI * 2f;
                            Vector2 q0 = p + (rt * Mathf.Cos(a0) + t * Mathf.Sin(a0)) * rr, q1 = p + (rt * Mathf.Cos(a1) + t * Mathf.Sin(a1)) * rr;
                            m.Rect((q0 + q1) * 0.5f, (q1 - q0).normalized, (q1 - q0).magnitude + 0.004f, lw, col);
                        }
                        int kmh = d.speedKmh > 0 ? d.speedKmh : 50;
                        RoadTextLabel(kmh.ToString(), p, t, rr * 1.5f, order, col, 1.3f, 0.6f);
                        return;
                    }
                case 2: RoadTextLabel("BUS", p, t, w, order, col); return;
                case 4: RoadTextLabel("TRAM", p, t, w, order, col); return;
                case 3:   // rombo alargado
                    {
                        Vector2 a = p + t * w * 0.6f, b = p + rt * w * 0.22f, c = p - t * w * 0.6f, e = p - rt * w * 0.22f;
                        Vector2[] q = { a, b, c, e };
                        for (int i = 0; i < 4; i++) { Vector2 x0 = q[i], x1 = q[(i + 1) % 4]; m.Rect((x0 + x1) * 0.5f, (x1 - x0).normalized, (x1 - x0).magnitude + lw, lw, col); }
                        return;
                    }
                case 5:   // ceda: triángulo alargado con la punta hacia el conductor que llega
                    {
                        Vector2 apex = p - t * w * 0.55f, b1 = p + t * w * 0.45f + rt * w * 0.32f, b2 = p + t * w * 0.45f - rt * w * 0.32f;
                        Vector2[] q = { apex, b1, b2 };
                        for (int i = 0; i < 3; i++) { Vector2 x0 = q[i], x1 = q[(i + 1) % 3]; m.Rect((x0 + x1) * 0.5f, (x1 - x0).normalized, (x1 - x0).magnitude + lw, lw * 1.4f, col); }
                        return;
                    }
                case 7: LaneArrow(m, p, t, 1, col, 0.9f, 1); return;   // incorporación (se supone: flecha que se cruza a la izquierda)
                case 8:   // carriles: dos flechas rectas una al lado de la otra (se supone)
                    LaneArrow(m, p + rt * w * 0.22f, t, 2, col, 0.6f);
                    LaneArrow(m, p - rt * w * 0.22f, t, 2, col, 0.6f);
                    return;
            }
            if (!string.IsNullOrEmpty(d.roadText)) RoadTextLabel(d.roadText, p, t, w, order, col);
        }

        static int RoadOrder(int layer) { return layer == 0 ? 1 : (layer > 0 ? 16 + 10 * layer : 11 + 25 * layer); }   // capa 1 = 26: por encima de los objetos altos del suelo (TallOrder)
        /// <summary>Capa en la que se dibuja un carril (una rampa, en la más alta de sus dos alturas).</summary>
        static int DrawLayer(RoadData d) { return Mathf.Max(d.layer, d.layer + d.ramp); }

        void BuildLaneRoads(MapData map, RoadNetworkManager net, bool editorMode)
        {
            const int L = LaneLayers;
            laneDraw = true;
            var roadB = new MeshBatch[L]; var marksB = new MeshBatch[L]; var shadowB = new MeshBatch[L]; var waterB = new MeshBatch[L];
            for (int i = 0; i < L; i++) { roadB[i] = new MeshBatch(); marksB[i] = new MeshBatch(); shadowB[i] = new MeshBatch(); waterB[i] = new MeshBatch(); }
            Color shadowCol = new Color(0f, 0f, 0f, 0.32f);
            laneBoxes.Clear();
            foreach (var r in net.roads)
            {
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                foreach (var q in r.center.pts) { x0 = Mathf.Min(x0, q.x); y0 = Mathf.Min(y0, q.y); x1 = Mathf.Max(x1, q.x); y1 = Mathf.Max(y1, q.y); }
                float e = r.halfWidth * 2f + 0.2f;
                laneBoxes[r] = Rect.MinMaxRect(x0 - e, y0 - e, x1 + e, y1 + e);
            }

            // 1) Franjas de cada carril.
            foreach (var r in net.roads)
            {
                var d = r.data;
                if (d.hidden && !editorMode) continue;
                int ly = LI(DrawLayer(d));
                Color col = LaneColor(d, editorMode);
                if (d.hidden) col.a = 0.3f;
                if (editorMode && EditLayer > 0 && DrawLayer(d) > EditLayer) col = Color.Lerp(col, Color.black, 0.55f);   // capas de más arriba
                float hw = r.halfWidth;
                var batch = d.laneType == RoadData.LaneBoat ? waterB[ly] : roadB[ly];
                var cross = d.laneType == RoadData.LaneSidewalk ? SidewalkCrossings(r, net) : null;
                if (cross == null || cross.Count == 0)
                {
                    if (d.laneType == RoadData.LaneSidewalk) batch.Strip(SidewalkSide(r, net, -1f, hw), SidewalkSide(r, net, 1f, hw), col);
                    else if (d.laneType == RoadData.LaneShoulder)
                    {
                        // Berma: siempre pegada a la carretera; si se ensancha, crece hacia afuera (lejos de los coches).
                        float lo, hi;
                        ShoulderBand(r, net, out lo, out hi);
                        batch.Strip(r.center.OffsetPts(lo - 0.006f), r.center.OffsetPts(hi + 0.006f), col);
                    }
                    else if (d.hidden) batch.Strip(r.center.OffsetPts(-hw), r.center.OffsetPts(hw), col);
                    else
                    {
                        // Un pelo más ancho y largo (0,006): los carriles vecinos se solapan y no queda la rayita verde
                        // entre ellos en las curvas ni en la unión de dos tramos.
                        const float ov = 0.006f;
                        var la = r.center.OffsetPts(-hw - ov); var lb = r.center.OffsetPts(hw + ov);
                        Vector2 t0 = r.center.TangentAt(0f).normalized * ov, t1 = r.center.TangentAt(r.length).normalized * ov;
                        la[0] -= t0; lb[0] -= t0; la[la.Length - 1] += t1; lb[lb.Length - 1] += t1;
                        batch.Strip(la, lb, col);
                    }
                    if (d.laneType == RoadData.LaneSidewalk) SidewalkEnds(batch, r, net, hw, col, true, true);
                }
                else
                {
                    // Acera que cruza la calzada: sobre el asfalto no se pinta la acera sino un paso de cebra (franjas
                    // blancas a lo ancho de la acera, paralelas a los coches), dentro de los bordes de la calzada.
                    float prev = 0f;
                    foreach (var iv in cross)
                    {
                        if (iv.x - prev > 0.01f) { var sub = r.center.Sub(prev, iv.x); batch.Strip(sub.OffsetPts(-hw), sub.OffsetPts(hw), col); }
                        prev = iv.y;
                    }
                    if (r.length - prev > 0.01f) { var sub = r.center.Sub(prev, r.length); batch.Strip(sub.OffsetPts(-hw), sub.OffsetPts(hw), col); }
                    SidewalkEnds(batch, r, net, hw, col, cross[0].x > 0.01f, cross[cross.Count - 1].y < r.length - 0.01f);
                    var zm = marksB[ly];
                    var zc = new Color(0.97f, 0.97f, 0.95f, d.hidden ? 0.3f : 0.95f);
                    foreach (var iv in cross) PaintCrossing(zm, r, iv, hw, d.crossStyle, zc);
                }
                int lev = DrawLayer(d);
                if (lev > 0)
                {
                    var off = new Vector2(0.1f, -0.14f) * lev;
                    var la = r.center.OffsetPts(-hw);
                    var lb = r.center.OffsetPts(hw);
                    for (int i = 0; i < la.Length; i++) { la[i] += off; lb[i] += off; }
                    shadowB[ly].Strip(la, lb, shadowCol);
                }
            }

            // 2) Juntas en los nodos: solo se rellena la cuña exterior entre el final de cada carril que llega y el
            //    inicio de cada carril que sale (del mismo tipo). Así no sobresale nada más allá de la calzada
            //    (antes un disco entero dejaba bultos redondos en los extremos).
            foreach (var n in net.nodes)
            {
                var hub = net.HubOf(n.id);
                if (hub == null || hub.inLanes.Count == 0 || hub.outLanes.Count == 0) continue;
                foreach (var il in hub.inLanes)
                {
                    NetRoad ir;
                    if (!net.roadById.TryGetValue(il.roadId, out ir)) continue;
                    if (ir.data.hidden && !editorMode) continue;
                    if (ir.data.laneType == RoadData.LaneSidewalk) continue;   // ya llevan los extremos redondeados
                    Vector2 tIn = il.path.TangentAt(il.Length);
                    foreach (var ol in hub.outLanes)
                    {
                        if (ol.laneType != il.laneType) continue;
                        Vector2 tOut = ol.path.TangentAt(0f);
                        if (Vector2.Dot(tIn, tOut) > 0.9995f) continue;   // sigue recto: no hay hueco
                        float hw = Mathf.Min(ir.halfWidth, ol.width * 0.5f);
                        Vector2 o = n.pos;
                        Vector2 inL = o - Geo.Right(tIn) * hw, inR = o + Geo.Right(tIn) * hw;
                        Vector2 outL = o - Geo.Right(tOut) * hw, outR = o + Geo.Right(tOut) * hw;
                        int ly = LI(DrawLayer(ir.data));
                        Color col = LaneColor(ir.data, editorMode);
                        if (ir.data.hidden) col.a = 0.3f;
                        var b = ir.data.laneType == RoadData.LaneBoat ? waterB[ly] : roadB[ly];
                        // Arco redondeado en el lado exterior del giro (unos pocos triángulos en abanico).
                        float cross = tIn.x * tOut.y - tIn.y * tOut.x;
                        Vector2 a0 = cross > 0f ? inR : inL, a1 = cross > 0f ? outR : outL;
                        Vector2 d0 = a0 - o, d1 = a1 - o;
                        float ang0 = Mathf.Atan2(d0.y, d0.x), ang1 = Mathf.Atan2(d1.y, d1.x);
                        float da = Mathf.DeltaAngle(ang0 * Mathf.Rad2Deg, ang1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                        int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(da) / 0.25f), 1, 12);
                        Vector2 prev = a0;
                        for (int k = 1; k <= steps; k++)
                        {
                            float aa = ang0 + da * k / steps;
                            Vector2 q = o + new Vector2(Mathf.Cos(aa), Mathf.Sin(aa)) * hw;
                            b.Tri(o, prev, q, col);
                            prev = q;
                        }
                    }
                }
            }

            // 3) Detalles por tipo y líneas pintadas.
            foreach (var r in net.roads)
            {
                var d = r.data;
                if (d.hidden && !editorMode) continue;
                int ly = LI(DrawLayer(d));
                var m = marksB[ly];
                float hw = r.halfWidth;
                switch (d.laneType)
                {
                    case RoadData.LaneRail:
                        {
                            var sleeper = SpriteFactory.Hex("#5B4632");
                            var steel = SpriteFactory.Hex("#4A4D52");
                            for (float s = 0.06f; s < r.length; s += 0.16f)
                                m.Rect(r.center.PointAt(s), Geo.Right(r.center.TangentAt(s)), hw * 1.7f, 0.05f, sleeper);
                            m.Line(r.center, -hw * 0.45f, 0.035f, steel);
                            m.Line(r.center, hw * 0.45f, 0.035f, steel);
                            break;
                        }
                    case RoadData.LaneBoat:
                        m.Line(r.center, 0f, hw * 0.5f, new Color(0.45f, 0.72f, 0.9f, 0.5f));
                        break;
                    // Carril bici: sin símbolo de bicicleta (los puntos blancos se retiraron).
                }
                if (!d.noMarks)
                {
                    CrossGaps(r, net);
                    if (d.lineL > 0 || d.lineR > 0)
                    {
                        // Líneas fijadas a mano (mapas convertidos o anteriores), sin pintar donde cruza otro carril.
                        PaintEdgeGaps(m, r.center, -hw, d.lineL);
                        PaintEdgeGaps(m, r.center, hw, d.lineR);
                    }
                    else if (d.laneType == RoadData.LaneVehicle || d.laneType == RoadData.LaneBike
                        || ShoulderWithLines(d))   // berma con líneas (importada del original)
                    {
                        PaintAutoSide(m, r, net, -1);
                        PaintAutoSide(m, r, net, 1);
                    }
                }

                // Chevrones de sentido (solo en el editor), como en la referencia.
                if (editorMode && d.laneType != RoadData.LaneRail)
                {
                    var chev = d.laneType == RoadData.LaneSidewalk ? new Color(0.45f, 0.45f, 0.45f, 0.7f) : new Color(0.3f, 0.3f, 0.32f, 0.75f);
                    // Donde hay una flecha pintada no se dibuja la de sentido: si no, se veían dos flechas encimadas.
                    for (float s = 0.35f; s < r.length - 0.15f; s += 0.55f)
                    {
                        bool underPainted = false;
                        foreach (var ar in map.arrows)
                            if (ar.road == d.id && Mathf.Abs(s - Mathf.Clamp01(ar.t) * r.length) < 0.45f * Mathf.Max(0.5f, ar.size) + 0.15f) { underPainted = true; break; }
                        if (!underPainted) SmallArrow(m, r.center.PointAt(s), r.center.TangentAt(s), Mathf.Min(0.26f, d.Width * 0.5f), chev);
                    }
                }
            }

            // 4) Líneas de detención y marcas de ceda el paso al final de los carriles regulados.
            foreach (var seg in net.segs)
            {
                var hub = seg.toApp;
                if (hub == null || seg.laneType == RoadData.LaneBoat || seg.laneType == RoadData.LaneShoulder) continue;
                bool lightOrStop = hub.hasLight || seg.endRank == 0;
                bool yield = seg.endRank == 1;
                if (!lightOrStop && !yield) continue;
                if (seg.laneType == RoadData.LaneSidewalk) continue;   // en las aceras no se pinta línea de detención (como en la vida real)
                NetRoad nr;
                if (!net.roadById.TryGetValue(seg.roadId, out nr) || (nr.data.hidden && !editorMode)) continue;
                int ly = LI(seg.LayerAt(seg.Length));
                float sp = Mathf.Clamp(seg.stopS - 0.03f, 0f, seg.Length);
                Vector2 p = seg.path.PointAt(sp), t = seg.path.TangentAt(sp);
                int nodeId = seg.toNode != null ? seg.toNode.id : -1;
                var sgn = map.FindSign(seg.roadId, nodeId, yield ? SignType.Yield : SignType.Stop);
                int style = sgn != null ? sgn.style : 0;
                Vector2 rt = Geo.Right(t);
                if (yield)
                {
                    if (style == 1)
                        for (float o = -seg.width * 0.5f + 0.06f; o < seg.width * 0.5f - 0.04f; o += 0.12f)
                            marksB[ly].Rect(p + rt * (o + 0.035f), t, 0.05f, 0.07f, White);
                    else if (style == 2)
                        for (float o = -seg.width * 0.5f + 0.07f; o < seg.width * 0.5f - 0.04f; o += 0.1f)
                            marksB[ly].Disc(p + rt * o, 0.025f, White, 8);
                    else
                    {
                        // Dientes de tiburón: una fila de triángulos con la punta hacia el conductor que llega.
                        int nT = Mathf.Max(2, Mathf.FloorToInt((seg.width - 0.04f) / 0.1f));
                        float tw = (seg.width - 0.04f) / nT;
                        for (int k = 0; k < nT; k++)
                        {
                            Vector2 c = p + rt * (-seg.width * 0.5f + 0.02f + tw * (k + 0.5f));
                            marksB[ly].Tri(c + rt * (tw * 0.42f), c - rt * (tw * 0.42f), c - t * 0.11f, White);
                        }
                    }
                }
                else if (seg.endRank == 0 && style == 1)
                    for (float o = -seg.width * 0.5f + 0.06f; o < seg.width * 0.5f - 0.03f; o += 0.1f)
                        marksB[ly].Rect(p + rt * (o + 0.02f), t, 0.06f, 0.06f, White);
                else marksB[ly].Rect(p, t, seg.endRank == 0 ? 0.09f : 0.05f, seg.width - 0.02f, White);
            }

            // Flechas pintadas.
            foreach (var ar in map.arrows)
            {
                NetRoad nr;
                if (!net.roadById.TryGetValue(ar.road, out nr) || nr.lanesAB.Length == 0) continue;
                if (nr.data.hidden && !editorMode) continue;
                var ln = nr.lanesAB[0];
                float sa = Mathf.Clamp01(ar.t) * ln.Length;
                LaneArrow(marksB[LI(DrawLayer(nr.data))], ln.path.PointAt(sa), ln.path.TangentAt(sa), ar.moves, ar.color == 1 ? Yellow : ArrowCol, ar.size, ar.style);
            }

            // Texto pintado en la calzada (como el original): letras alargadas que lee el conductor que llega.
            foreach (var r in net.roads)
            {
                if ((string.IsNullOrEmpty(r.data.roadText) && r.data.markKind == 0) || (r.data.hidden && !editorMode)) continue;
                DrawLaneMark(marksB[LI(DrawLayer(r.data))], r);
            }

            var over = new MeshBatch();
            foreach (var r in net.roads) if (!(r.data.hidden && !editorMode)) DrawRoadProps(marksB[LI(DrawLayer(r.data))], over, r);

            for (int i = 0; i < L; i++)
            {
                int layer = i + MapData.MinLayer;
                int ro = RoadOrder(layer);
                if (layer > 0) shadowB[i].Build(roadRoot, "Shadow" + layer, ro - 2);
                waterB[i].Build(roadRoot, "Water" + layer, layer == 0 ? -9 : ro - 1);
                roadB[i].Build(roadRoot, "Lanes" + layer, ro);
                // Grano del asfalto (como el original): una textura de ruido suave encima de la calzada.
                if (SaveSystem.Progress.roadGrain) roadB[i].BuildTextured(roadRoot, "Grain" + layer, ro + 1, GrainTexture, 1.6f, new Color(1f, 1f, 1f, 0.16f * Mathf.Clamp(SaveSystem.Progress.grainAmount, 0.3f, 2f)));
                marksB[i].Build(roadRoot, "Markings" + layer, ro + 2);
            }
            over.Build(roadRoot, "Cables", 35);
            if (editorMode && EditLayer > 0)
            {
                // Velo oscuro justo por debajo de la capa elegida: oscurece el suelo y los objetos (no es un truco de zoom).
                var veil = SpriteFactory.Make("LayerVeil", roadRoot, SpriteFactory.Square, new Color(0f, 0f, 0f, 0.55f),
                    new Vector2(map.width * 0.5f, map.height * 0.5f), new Vector2(map.width + 60f, map.height + 60f), RoadOrder(EditLayer) - 1);
                veil.transform.localPosition += new Vector3(0f, 0f, -5f);   // delante de los objetos altos del mismo orden
            }
        }

        // ---- Líneas como el original: dependen de qué hay al lado de cada borde del carril ----

        /// <summary>Estilo de PaintEdge para cada opción (-1 = automática).</summary>
        static int SameStyle(RoadData d) { return SameStyleV(d.lineSame, d.noLaneChange); }
        static int SameStyleV(int v, bool noChange) { if (v < 0) return noChange ? 1 : 3; int[] map = { 0, 3, 1, 4, 2 }; return map[Mathf.Clamp(v, 0, 4)]; }

        /// <summary>
        /// Línea entre dos carriles del mismo sentido. Como el original, cada carril pinta su lado izquierdo: las amarillas
        /// (3 y 4) solo valen para el carril que tiene esa línea a su izquierda.
        /// </summary>
        static int SameBetween(RoadData rd, RoadData od, int side)
        {
            bool rOwner = side < 0;
            int rv = rd.lineSame, ov = od.lineSame;
            if (rv >= 3 && !rOwner) rv = -1;
            if (ov >= 3 && rOwner) ov = -1;
            return rv >= 0 || ov < 0 ? SameStyleV(rv, rd.noLaneChange) : SameStyleV(ov, od.noLaneChange);
        }

        /// <summary>
        /// Línea con el sentido contrario. Como el original, cada carril pinta su mitad: si los dos la tienen amarilla
        /// continua sale doble; si solo uno (y el otro está en automático o discontinua), sencilla continua.
        /// </summary>
        static int OppBetween(RoadData rd, RoadData od)
        {
            int rv = rd.lineOpp, ov = od.lineOpp;
            if (rv == 1 && ov == 1) return 5;
            if ((rv == 1 && (ov < 0 || ov == 4)) || (ov == 1 && (rv < 0 || rv == 4))) return 2;
            return rv >= 0 || ov < 0 ? OppStyle(rd) : OppStyle(od);
        }
        static int OppStyle(RoadData d) { int v = d.lineOpp; if (v < 0) return 2; int[] map = { 0, 2, 5, 1, 4, 3 }; return map[Mathf.Clamp(v, 0, 5)]; }
        static int EdgeStyle(RoadData d) { int v = d.lineEdge; if (v < 0) return 1; return v == 0 ? 0 : (v == 1 ? 1 : 2); }

        /// <summary>Carril pegado a este por un lado en el punto s (null si es borde), y si va en el mismo sentido.</summary>
        // Tramos del carril actual donde cruza otro carril de vehículos o bicis: ahí no se pintan líneas (como el original).
        /// <summary>
        /// Tramos (s0, s1) de una acera que pasan sobre la calzada: carriles de vehículos o bici de su misma capa. Ahí va el
        /// paso de cebra. Se mira a lo largo de la acera si su eje cae dentro del ancho de algún carril.
        /// </summary>
        List<Vector2> SidewalkCrossings(NetRoad r, RoadNetworkManager net)
        {
            var list = new List<Vector2>();
            const float step = 0.02f;
            float start = -1f, prevS = 0f;
            int ly = DrawLayer(r.data);
            System.Func<float, bool> inside = sc =>
            {
                Vector2 p = r.center.PointAt(sc);
                foreach (var o in net.roads)
                {
                    // Como el original: la cebra también se pinta sobre el carril bici (y la berma, que es un carril bici sin bicis).
                    if (o == r || (o.data.laneType != RoadData.LaneVehicle && o.data.laneType != RoadData.LaneBike && o.data.laneType != RoadData.LaneShoulder)) continue;
                    if (DrawLayer(o.data) != ly) continue;
                    Rect box;
                    if (laneBoxes.TryGetValue(o, out box) && !box.Contains(p)) continue;
                    float dist;
                    o.center.Project(p, out dist);
                    if (dist < o.halfWidth) return true;
                }
                return false;
            };
            // Borde exacto del asfalto entre a (valor va) y b: así la acera termina justo en el borde (sin franja encima).
            System.Func<float, float, bool, float> edge = (a, b, va) =>
            {
                for (int it = 0; it < 10; it++) { float m = (a + b) * 0.5f; if (inside(m) == va) a = m; else b = m; }
                return (a + b) * 0.5f;
            };
            for (float s = 0f; s <= r.length + step * 0.5f; s += step)
            {
                float sc = Mathf.Min(s, r.length);
                bool inn = inside(sc);
                if (inn && start < 0f) start = sc <= 0f ? 0f : edge(prevS, sc, false);
                if (!inn && start >= 0f) { list.Add(new Vector2(start, edge(prevS, sc, true))); start = -1f; }
                prevS = sc;
            }
            if (start >= 0f) list.Add(new Vector2(start, r.length));
            // Un extremo de acera metido en la calzada que no sigue en otra acera no es un cruce (no se pinta cebra ahí).
            bool contA = SidewalkContinues(net, r.data.a, r.data.id), contB = SidewalkContinues(net, r.data.b, r.data.id);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                bool touchA = list[i].x <= 0.03f, touchB = list[i].y >= r.length - 0.03f;
                if ((touchA && !contA) || (touchB && !contB)) list.RemoveAt(i);
            }
            // Se juntan los tramos casi pegados (carriles contiguos) en un solo paso.
            for (int i = list.Count - 1; i > 0; i--)
                if (list[i].x - list[i - 1].y < 0.05f) { list[i - 1] = new Vector2(list[i - 1].x, list[i].y); list.RemoveAt(i); }
            return list;
        }

        /// <summary>
        /// Borde de una acera por un lado (sgn -1 izquierda, +1 derecha): si junto a ese lado corre una calle con un hueco
        /// pequeño (por la cuadrícula o un ancho distinto), el borde llega hasta la calle; así la acera queda pegada sin
        /// la raya verde. Si no hay calle al lado, es el borde normal.
        /// </summary>
        Vector2[] SidewalkSide(NetRoad r, RoadNetworkManager net, float sgn, float hw)
        {
            var pts = r.center.OffsetPts(sgn * hw);
            int ly = DrawLayer(r.data);
            for (int i = 0; i < pts.Length; i++)
            {
                float si = r.center.cum[Mathf.Min(i, r.center.cum.Length - 1)];
                Vector2 n = Geo.Right(r.center.TangentAt(si)) * sgn;
                Vector2 q = pts[i];
                float best = -1f;
                foreach (var o in net.roads)
                {
                    if (o == r || (o.data.laneType != RoadData.LaneVehicle && o.data.laneType != RoadData.LaneBike) || DrawLayer(o.data) != ly) continue;
                    Rect box;
                    if (laneBoxes.TryGetValue(o, out box) && (q.x < box.xMin - 0.3f || q.x > box.xMax + 0.3f || q.y < box.yMin - 0.3f || q.y > box.yMax + 0.3f)) continue;
                    float dist;
                    float so = o.center.Project(q, out dist);
                    float gap = dist - o.halfWidth;
                    if (gap <= 0.001f || gap > 0.16f) continue;
                    Vector2 dir = (o.center.PointAt(so) - q).normalized;
                    if (Vector2.Dot(dir, n) < 0.95f) continue;   // la calle va al lado (paralela), no de frente
                    if (best < 0f || gap < best) best = gap;
                }
                if (best > 0f) pts[i] = q + n * (best + 0.004f);
            }
            return pts;
        }

        /// <summary>Extremos de acera: redondeados, o rectos hasta el borde del carril si llegan de frente a uno.</summary>
        void SidewalkEnds(MeshBatch batch, NetRoad r, RoadNetworkManager net, float hw, Color col, bool doStart, bool doEnd)
        {
            for (int e = 0; e < 2; e++)
            {
                if (e == 0 ? !doStart : !doEnd) continue;
                Vector2 outward;
                float gap = SidewalkEndGap(r, e == 0, net, out outward);
                Vector2 p = e == 0 ? r.center.Start : r.center.End;
                if (gap < 0f) { batch.Disc(p, hw, col, 16); continue; }
                if (gap > 0.002f) batch.Rect(p + outward * ((gap + 0.01f) * 0.5f), outward, gap + 0.01f, hw * 2f, col);
            }
        }

        /// <summary>
        /// Paso peatonal pintado sobre la calzada donde cruza la acera, con el estilo elegido (como el original):
        /// 0 sin marcas, 1 bloque, 2 dos líneas, 3 cebra, 4 diagonal, 5 escalera (cebra con líneas a los lados).
        /// </summary>
        static void PaintCrossing(MeshBatch zm, NetRoad r, Vector2 iv, float hw, int style, Color zc)
        {
            float s0 = iv.x + 0.02f, s1 = iv.y - 0.02f, span = s1 - s0;
            if (span < 0.06f || style <= 0) return;
            float inner = hw - 0.025f;
            switch (style)
            {
                case 1:   // bloque
                    {
                        var sub = r.center.Sub(s0, s1);
                        zm.Strip(sub.OffsetPts(-inner), sub.OffsetPts(inner), zc);
                        break;
                    }
                case 2:   // dos líneas a lo largo de los bordes del paso
                case 5:   // escalera: líneas + franjas
                    {
                        var sub = r.center.Sub(s0, s1);
                        float lw = 0.035f;
                        zm.Strip(sub.OffsetPts(-inner), sub.OffsetPts(-inner + lw), zc);
                        zm.Strip(sub.OffsetPts(inner - lw), sub.OffsetPts(inner), zc);
                        if (style == 5) Zebra(zm, r, s0, s1, inner - lw, zc);
                        break;
                    }
                case 4:   // diagonal: franjas inclinadas dentro del paso
                    {
                        float w = 0.05f, pitch = 0.12f, slope = inner * 1.2f;
                        for (float sa = s0 - slope; sa < s1; sa += pitch)
                        {
                            float a0 = Mathf.Clamp(sa, s0, s1), a1 = Mathf.Clamp(sa + w, s0, s1);
                            float b0 = Mathf.Clamp(sa + slope, s0, s1), b1 = Mathf.Clamp(sa + slope + w, s0, s1);
                            if (a1 - a0 < 0.004f && b1 - b0 < 0.004f) continue;
                            Vector2 p0 = r.center.PointAt(a0) + Geo.Right(r.center.TangentAt(a0)) * -inner;
                            Vector2 p1 = r.center.PointAt(a1) + Geo.Right(r.center.TangentAt(a1)) * -inner;
                            Vector2 p2 = r.center.PointAt(b1) + Geo.Right(r.center.TangentAt(b1)) * inner;
                            Vector2 p3 = r.center.PointAt(b0) + Geo.Right(r.center.TangentAt(b0)) * inner;
                            zm.Quad(p0, p1, p2, p3, zc);
                        }
                        break;
                    }
                default:  // cebra
                    Zebra(zm, r, s0, s1, inner, zc);
                    break;
            }
        }

        static void Zebra(MeshBatch zm, NetRoad r, float s0, float s1, float inner, Color zc)
        {
            float span = s1 - s0;
            int n = Mathf.Max(1, Mathf.FloorToInt(span / 0.11f));
            float step = span / n;
            for (int k = 0; k < n; k++)
            {
                float sc = s0 + step * (k + 0.5f);
                zm.Rect(r.center.PointAt(sc), r.center.TangentAt(sc), step * 0.5f, inner * 2f, zc);
            }
        }

        static bool SidewalkContinues(RoadNetworkManager net, int nodeId, int roadId)
        {
            var hub = net.HubOf(nodeId);
            if (hub == null) return false;
            foreach (var l in hub.inLanes) if (l.laneType == RoadData.LaneSidewalk && l.roadId != roadId) return true;
            foreach (var l in hub.outLanes) if (l.laneType == RoadData.LaneSidewalk && l.roadId != roadId) return true;
            return false;
        }

        /// <summary>
        /// Extremo de acera que llega de frente a un carril de vehículos (p. ej. desde un cuadradito lateral): devuelve
        /// cuánto falta hasta el borde del carril (≥ 0) para alargar la acera recta hasta él, o -1 si no toca ninguno.
        /// Así no queda el hueco verde que dejaba el extremo redondeado.
        /// </summary>
        float SidewalkEndGap(NetRoad r, bool atStart, RoadNetworkManager net, out Vector2 outward)
        {
            Vector2 p = atStart ? r.center.Start : r.center.End;
            outward = atStart ? -r.center.TangentAt(0f).normalized : r.center.TangentAt(r.length).normalized;
            float hw = r.halfWidth, best = -1f;
            int ly = DrawLayer(r.data);
            foreach (var o in net.roads)
            {
                if (o == r || (o.data.laneType != RoadData.LaneVehicle && o.data.laneType != RoadData.LaneBike) || DrawLayer(o.data) != ly) continue;
                float dist;
                float so = o.center.Project(p, out dist);
                float g = dist - o.halfWidth;
                if (g < -hw || g > hw + 0.12f) continue;
                Vector2 q = o.center.PointAt(so);
                Vector2 dir = (q - p).sqrMagnitude > 1e-8f ? (q - p).normalized : outward;
                if (g > 0.005f && Vector2.Dot(dir, outward) < 0.6f) continue;
                float gg = Mathf.Max(0f, g);
                if (best < 0f || gg < best) best = gg;
            }
            return best;
        }

        static readonly List<Vector2> gaps = new List<Vector2>();

        static void CrossGaps(NetRoad r, RoadNetworkManager net)
        {
            gaps.Clear();
            RoadSeg seg;
            if (!net.laneByRoad.TryGetValue(r.data.id, out seg)) return;
            foreach (var c in seg.conflicts)
            {
                if (c.merge || c.other == null) continue;
                bool walk = c.other.laneType == RoadData.LaneSidewalk;   // las líneas se cortan también en el paso de cebra
                if (c.other.laneType != RoadData.LaneVehicle && c.other.laneType != RoadData.LaneBike && !walk) continue;
                if (c.other.LayerAt(c.otherS) != seg.LayerAt(c.myS)) continue;
                float m = walk ? c.other.width * 0.5f : c.clear + 0.25f;
                gaps.Add(new Vector2(c.myS - m, c.myS + m));
            }
        }

        static bool InGap(float s)
        {
            for (int i = 0; i < gaps.Count; i++) if (s >= gaps[i].x && s <= gaps[i].y) return true;
            return false;
        }

        static void PaintEdgeGaps(MeshBatch m, Polyline pl, float offset, int style)
        {
            if (style <= 0) return;
            if (gaps.Count == 0) { PaintEdge(m, pl, offset, style); return; }
            const float step = 0.1f;
            float start = -1f;
            for (float s = 0f; s <= pl.Length + step * 0.5f; s += step)
            {
                bool g = s > pl.Length || InGap(s);
                if (!g && start < 0f) start = s;
                if (g && start >= 0f) { PaintEdgeRun(m, pl, start, Mathf.Min(s, pl.Length), offset, style, false); start = -1f; }
            }
            if (start >= 0f) PaintEdgeRun(m, pl, start, pl.Length, offset, style, false);
        }

        static readonly Dictionary<NetRoad, Rect> laneBoxes = new Dictionary<NetRoad, Rect>();

        /// <summary>Berma importada del original (carril bici sin bicis): pinta y comparte líneas como un carril.</summary>
        static bool ShoulderWithLines(RoadData d) { return d.laneType == RoadData.LaneShoulder && (d.lineEdge >= 0 || d.lineSame >= 0 || d.lineOpp >= 0); }

        static NetRoad SideNeighbor(NetRoad r, RoadNetworkManager net, float s, int side, out bool same)
        {
            same = false;
            Vector2 p = r.center.PointAt(s), t = r.center.TangentAt(s);
            Vector2 q = p + Geo.Right(t) * (side * r.halfWidth * 2f);
            int layer = DrawLayer(r.data);
            NetRoad best = null;
            float bd = float.MaxValue;
            foreach (var o in net.roads)
            {
                if (o == r) continue;
                Rect box;
                if (laneBoxes.TryGetValue(o, out box) && !box.Contains(q)) continue;
                if (o.data.laneType != RoadData.LaneVehicle && o.data.laneType != RoadData.LaneBike && !ShoulderWithLines(o.data)) continue;
                if (DrawLayer(o.data) != layer) continue;
                float dist;
                float so = o.center.Project(q, out dist);
                float tol = Mathf.Max(0.08f, (r.halfWidth + o.halfWidth) * 0.35f);
                // El otro carril debe estar a un ancho (de centro a centro) y ser casi paralelo.
                if (dist > tol || dist >= bd) continue;
                Vector2 to = o.center.TangentAt(so);
                float dot = Vector2.Dot(t, to);
                if (Mathf.Abs(dot) < 0.85f) continue;
                bd = dist; best = o; same = dot > 0f;
            }
            return best;
        }

        /// <summary>
        /// Pinta un borde del carril por tramos: junto a un carril del mismo sentido, la línea «mismo sentido»; junto a
        /// uno de sentido contrario, la «doble sentido»; sin vecino, la de «bordes». La línea compartida la pinta solo
        /// uno de los dos carriles (el de id menor), con la opción que no sea automática si alguno la tiene.
        /// </summary>
        static void PaintAutoSide(MeshBatch m, NetRoad r, RoadNetworkManager net, int side)
        {
            float len = r.length;
            if (len < 0.05f) return;
            const float step = 0.2f;
            int n = Mathf.Max(1, Mathf.CeilToInt(len / step));
            float ds = len / n;
            int runStyle = -1; float runStart = 0f;
            for (int i = 0; i <= n; i++)
            {
                int style = -2;   // estilo * 2 + 1 si la línea es compartida (va justo en el límite, sin meterse en el carril)
                if (i < n)
                {
                    float s = (i + 0.5f) * ds;
                    bool same = false;
                    var o = InGap(s) ? null : SideNeighbor(r, net, s, side, out same);
                    if (InGap(s)) style = 0;
                    else if (o == null) style = EdgeStyle(r.data) * 2;
                    else if (o.data.id < r.data.id) style = 0;   // la pinta el otro carril
                    else if (same) style = SameBetween(r.data, o.data, side) * 2 + 1;
                    else style = OppBetween(r.data, o.data) * 2 + 1;   // la línea elegida manda sobre la automática
                }
                if (style != runStyle)
                {
                    if (runStyle >= 2) PaintEdgeRun(m, r.center, runStart, i * ds, side * r.halfWidth, runStyle / 2, (runStyle & 1) == 1);
                    runStyle = style; runStart = i * ds;
                }
            }
        }

        static void PaintEdgeRun(MeshBatch m, Polyline pl, float s0, float s1, float offset, int style, bool shared)
        {
            if (s1 - s0 < 0.02f) return;
            float inset = shared ? 0f : (offset < 0f ? 0.03f : -0.03f);
            switch (style)
            {
                case 1: m.Line(pl, s0, s1, offset + inset, 0.028f, White); break;
                case 2: m.Line(pl, s0, s1, offset + inset, 0.03f, Yellow); break;
                case 3: m.Dashes(pl, s0, s1, offset, 0.028f, 0.26f, 0.24f, new Color(White.r, White.g, White.b, 0.9f)); break;
                case 4: m.Dashes(pl, s0, s1, offset, 0.032f, 0.32f, 0.26f, Yellow); break;
                case 5:
                    m.Line(pl, s0, s1, offset - 0.025f, 0.026f, Yellow);
                    m.Line(pl, s0, s1, offset + 0.025f, 0.026f, Yellow);
                    break;
            }
        }

        static void PaintEdge(MeshBatch m, Polyline pl, float offset, int style)
        {
            if (style <= 0) return;
            float inset = offset < 0f ? 0.03f : -0.03f;
            float len = pl.Length;
            switch (style)
            {
                case 1: m.Line(pl, offset + inset, 0.028f, White); break;
                case 2: m.Line(pl, offset + inset, 0.03f, Yellow); break;
                case 3: m.Dashes(pl, 0f, len, offset, 0.028f, 0.26f, 0.24f, new Color(White.r, White.g, White.b, 0.9f)); break;
                case 4: m.Dashes(pl, 0f, len, offset, 0.032f, 0.32f, 0.26f, Yellow); break;
                case 5:
                    m.Line(pl, offset - 0.025f, 0.026f, Yellow);
                    m.Line(pl, offset + 0.025f, 0.026f, Yellow);
                    break;
            }
        }

        /// <summary>Flecha de sentido del editor, como el original: palo fino con punta (no un simple «^»).</summary>
        static void SmallArrow(MeshBatch m, Vector2 p, Vector2 t, float len, Color col)
        {
            t.Normalize();
            Vector2 rt = Geo.Right(t);
            float headL = len * 0.38f, headW = len * 0.42f, w = Mathf.Max(0.014f, len * 0.09f);
            Vector2 tail = p - t * (len * 0.5f), hb = tail + t * (len - headL);
            m.Rect((tail + hb) * 0.5f, t, len - headL + 0.005f, w, col);
            m.Tri(hb + rt * headW * 0.5f, hb - rt * headW * 0.5f, tail + t * len, col);
        }

        static void Chevron(MeshBatch m, Vector2 p, Vector2 t, float size, Color col)
        {
            t.Normalize();
            Vector2 rt = Geo.Right(t);
            Vector2 tip = p + t * size * 0.5f;
            Vector2 a = tip - t * size + rt * size;
            Vector2 b = tip - t * size - rt * size;
            m.Rect((a + tip) * 0.5f, tip - a, (tip - a).magnitude, 0.018f, col);
            m.Rect((b + tip) * 0.5f, tip - b, (tip - b).magnitude, 0.018f, col);
        }

        /// <summary>Señales del modelo por carriles: junto al final del carril al que se aplican.</summary>
        void DrawLaneSigns(MapData map, RoadNetworkManager net, bool editorMode)
        {
            var stack = new Dictionary<int, int>();
            foreach (var s in map.signs)
            {
                RoadSeg lane;
                if (!net.laneByRoad.TryGetValue(s.road, out lane)) continue;
                if (s.type == SignType.Light) continue;   // semáforos: al jugar, el animado; en el editor, DrawEditorLights
                if (s.type == SignType.SpeedLimit)
                {
                    float sp0 = lane.Length * 0.35f;
                    Vector2 t0 = lane.path.TangentAt(sp0);
                    DrawSign(s.type, lane.path.PointAt(sp0) + Geo.Right(t0) * (lane.width * 0.5f + 0.3f), Geo.Angle(t0));
                    continue;
                }
                int k;
                stack.TryGetValue(s.road, out k);
                stack[s.road] = k + 1;
                float pos = Mathf.Max(0.05f, lane.Length - 0.15f - 0.34f * k);
                Vector2 t = lane.path.TangentAt(pos);
                Vector2 p = lane.path.PointAt(pos) + Geo.Right(t) * (lane.width * 0.5f + 0.3f);
                DrawSign(s.type, p, Geo.Angle(t));
            }
            if (editorMode) DrawEditorLights(map, net);
        }

        /// <summary>Geometría de un semáforo como el original: poste al lado, brazo sobre los carriles y una cabeza por carril.</summary>
        struct LightGeom
        {
            public Vector2 t, pole, armEnd;
            public Vector2[] heads, stops;
            public float[] widths;
        }

        static LightGeom MakeLightGeom(IList<RoadSeg> lanes, int side, float extra = 0f)
        {
            var g = new LightGeom();
            Vector2 t = Vector2.zero;
            foreach (var l in lanes) t += l.path.TangentAt(l.Length);
            t = t.sqrMagnitude > 1e-6f ? t.normalized : Vector2.up;
            Vector2 right = Geo.Right(t);
            Vector2 sideDir = side == 1 ? -right : right;
            g.t = t;
            g.stops = new Vector2[lanes.Count];
            g.widths = new float[lanes.Count];
            float maxSide = float.MinValue, minSide = float.MaxValue;
            RoadSeg edge = lanes[0], far = lanes[0];
            for (int i = 0; i < lanes.Count; i++)
            {
                var l = lanes[i];
                g.stops[i] = l.path.PointAt(Mathf.Clamp(l.stopS - 0.03f, 0f, l.Length));
                g.widths[i] = l.width;
                float sd = Vector2.Dot(g.stops[i], sideDir);
                if (sd > maxSide) { maxSide = sd; edge = l; }
                if (sd < minSide) { minSide = sd; far = l; }
            }
            Vector2 ep = edge.path.PointAt(Mathf.Clamp(edge.stopS - 0.03f, 0f, edge.Length));
            Vector2 fp = far.path.PointAt(Mathf.Clamp(far.stopS - 0.03f, 0f, far.Length));
            // El brazo va justo sobre la línea de parada, a lo ancho de todos los carriles (como el original).
            Vector2 back = Vector2.zero;
            g.pole = ep + sideDir * (edge.width * 0.5f + 0.03f + extra) + back;   // pegado al borde, como el original
            float farD = Vector2.Dot(fp - g.pole, -sideDir) + far.width * 0.25f;
            g.armEnd = g.pole - sideDir * Mathf.Max(0.2f, farD);
            g.heads = new Vector2[lanes.Count];
            for (int i = 0; i < lanes.Count; i++)
            {
                float d = Vector2.Dot(g.stops[i] - g.pole, -sideDir);
                g.heads[i] = g.pole - sideDir * d;
            }
            return g;
        }

        /// <summary>
        /// Dibuja el semáforo como el original: brazo ancho y claro sobre los carriles, poste que «baja» desde el final del
        /// brazo hasta el suelo (con su base) y una cabeza por carril, algo a la izquierda del centro del carril.
        /// Devuelve las luces de cada cabeza (rojo, ámbar, verde) y su halo para animarlas.
        /// </summary>
        List<SpriteRenderer[]> DrawLightHardware(Transform parent, LightGeom g, float k, int order, bool drawPole = true, int groundOrder = -1)
        {
            // Como el original (3D falso: lo que está a una altura z se dibuja z más arriba en pantalla). El poste nace en
            // el borde del carril, en la línea de parada, y sube 0,2; el brazo cruza los carriles a esa altura. Cada cabeza
            // es una caja colgada del lado del brazo que mira a los coches: se ven su tapa y las caras que miran hacia
            // abajo en pantalla; las luces están en la cara de delante (solo se ven si esa cara mira hacia nosotros).
            var heads = new List<SpriteRenderer[]>();
            float Z = 0.2f * k;
            Vector2 up = new Vector2(0f, Z);
            Vector2 shadowV = new Vector2(-0.45f * k, 0f);
            Vector2 armV = g.armEnd - g.pole;
            Vector2 al = armV.sqrMagnitude > 1e-6f ? armV.normalized : Vector2.right;
            Vector2 t = g.t.sqrMagnitude > 1e-6f ? g.t.normalized : Vector2.up;
            float Lb = 0.13f * k, Db = 0.028f * k, Hb = 0.034f * k;
            // El brazo llega hasta la cabeza más lejana de TODO el grupo que comparte el poste (g.armEnd), no solo de
            // los carriles de esta parte: si no, con varios accesos en un poste las cabezas de los otros quedaban sueltas.
            float reach = Mathf.Max(0.05f * k, Vector2.Dot(g.armEnd - g.pole, al));
            foreach (var hp in g.heads) reach = Mathf.Max(reach, Vector2.Dot(hp - g.pole, al) + Lb * 0.5f);
            Vector2 armTip = g.pole + al * reach;
            bool lightsVisible = t.y > -0.15f;   // la cara de las luces mira hacia abajo en pantalla (o de lado)
            bool boxInFront = t.y > 0f;          // la caja está entre el brazo y quien mira
            if (drawPole)
            {
                var shadow = new Color(0f, 0f, 0f, 0.22f);
                int so = groundOrder >= 0 ? groundOrder : order;   // las sombras van en el suelo, bajo los vehículos
                RegPoleShadow(Bar(parent, "LightPoleShadow", g.pole, g.pole + shadowV, 0.016f * k, shadow, so), g.pole, g.pole + shadowV);
                RegArmShadow(Bar(parent, "LightArmShadow", g.pole + shadowV, armTip + shadowV, 0.016f * k, shadow, so), g.pole, armTip, shadowV.magnitude);
                Bar(parent, "LightPole", g.pole, g.pole + up, 0.02f * k, new Color(0.47f, 0.48f, 0.5f, 1f), order + 2);
                Bar(parent, "LightArm", g.pole + up, armTip + up, 0.022f * k, new Color(0.71f, 0.72f, 0.74f, 1f), order + 2);
            }
            var boxes = new MeshBatch();
            var top = new Color(0.07f, 0.07f, 0.08f, 1f);
            var front = new Color(0.17f, 0.09f, 0.09f, 1f);
            var side = new Color(0.12f, 0.12f, 0.13f, 1f);
            Vector2 zb = new Vector2(0f, Z - Hb * 0.5f), zt = new Vector2(0f, Z + Hb * 0.5f);
            for (int i = 0; i < g.heads.Length; i++)
            {
                Vector2 c = g.heads[i] - t * (0.011f * k + Db * 0.5f);
                // Caras verticales que miran hacia abajo en pantalla (normal con y < 0): delante (-t), detrás (+t), extremos (±al).
                for (int f = 0; f < 4; f++)
                {
                    Vector2 n = f == 0 ? -t : f == 1 ? t : f == 2 ? -al : al;
                    if (n.y > -0.02f) continue;
                    Vector2 e = f < 2 ? al : t;
                    float half = f < 2 ? Lb * 0.5f : Db * 0.5f;
                    Vector2 fc = c + n * (f < 2 ? Db * 0.5f : Lb * 0.5f);
                    boxes.Quad(fc - e * half + zb, fc + e * half + zb, fc + e * half + zt, fc - e * half + zt, f == 0 ? front : side);
                }
                Vector2 h0 = c - al * (Lb * 0.5f) - t * (Db * 0.5f), h1 = c + al * (Lb * 0.5f) - t * (Db * 0.5f);
                Vector2 h2 = c + al * (Lb * 0.5f) + t * (Db * 0.5f), h3 = c - al * (Lb * 0.5f) + t * (Db * 0.5f);
                boxes.Quad(h0 + zt, h1 + zt, h2 + zt, h3 + zt, top);

                // Luces en la cara de delante: roja en el extremo lejano al poste, verde en el cercano.
                Vector2 lc = c - t * (Db * 0.5f + 0.002f * k) + up;
                float dx = 0.042f * k, dsz = lightsVisible ? 0.024f * k : 0f;
                var r = SpriteFactory.Make("LightR", parent, SpriteFactory.Circle, new Color(0.25f, 0.07f, 0.07f), lc + al * dx, Vector2.one * dsz, order + 4);
                var y = SpriteFactory.Make("LightY", parent, SpriteFactory.Circle, new Color(0.25f, 0.2f, 0.07f), lc, Vector2.one * dsz, order + 4);
                var gr = SpriteFactory.Make("LightG", parent, SpriteFactory.Circle, new Color(0.07f, 0.22f, 0.1f), lc - al * dx, Vector2.one * dsz, order + 4);
                var halo = SpriteFactory.Make("LightHalo", parent, SpriteFactory.Soft, new Color(1f, 0.25f, 0.2f, 0.6f), lc + al * dx, Vector2.one * 0.14f * k, order + 5);
                heads.Add(new[] { r, y, gr, halo });
            }
            boxes.Build(parent, "LightBoxes", boxInFront ? order + 3 : order + 1);
            return heads;
        }

        /// <summary>¿Es un semáforo peatonal? (todos sus carriles son de acera).</summary>
        static bool IsPedLight(IList<RoadSeg> lanes)
        {
            if (lanes == null || lanes.Count == 0) return false;
            foreach (var l in lanes) if (l.laneType != RoadData.LaneSidewalk) return false;
            return true;
        }

        /// <summary>
        /// Semáforo peatonal como en la vida real: está en la acera de ENFRENTE, mirando hacia donde esperan los peatones
        /// (así lo ven al otro lado de la calle). Poste en el borde, a la derecha del paso visto por quien cruza, y una caja
        /// en alto (3D falso) con la luz roja arriba y la verde abajo en su cara delantera; si esa cara no mira hacia
        /// nosotros solo se ve la caja y el brillo. outs: carriles que salen del nodo (para hallar el cruce y su otro lado).
        /// Devuelve [rojo, ámbar (invisible), verde, brillo].
        /// </summary>
        List<SpriteRenderer[]> DrawPedLight(Transform parent, IList<RoadSeg> lanes, IList<RoadSeg> outs, float k, int order, int groundOrder)
        {
            RoadSeg l = lanes[0];
            foreach (var x in lanes) if (!x.twin) { l = x; break; }
            Vector2 t = l.path.TangentAt(l.Length).normalized, near = l.path.End, far = near + t * 1f;
            // El cruce: el carril de acera que sale de este nodo y atraviesa la calzada; su final es la acera de enfrente.
            if (outs != null)
                foreach (var o in outs)
                {
                    if (o.laneType != RoadData.LaneSidewalk) continue;
                    bool crosses = false;
                    foreach (var c in o.conflicts) if (c.other != null && c.other.laneType != RoadData.LaneSidewalk) { crosses = true; break; }
                    if (!crosses) continue;
                    far = o.path.End; t = (far - near).sqrMagnitude > 1e-6f ? (far - near).normalized : t;
                    break;
                }
            Vector2 side = LeftHand ? Geo.Left(t) : Geo.Right(t);
            Vector2 base0 = PedLightSpot(far, t, side, l.width * 0.5f + 0.05f * k);
            float Z = 0.16f * k;
            Vector2 up = new Vector2(0f, Z);
            RegPoleShadow(Bar(parent, "PedPoleShadow", base0, base0 + new Vector2(-0.35f * k, 0f), 0.014f * k, new Color(0f, 0f, 0f, 0.22f), groundOrder), base0, base0 + new Vector2(-0.35f * k, 0f));
            Bar(parent, "PedPole", base0, base0 + up, 0.018f * k, new Color(0.47f, 0.48f, 0.5f, 1f), order + 2);

            // Caja: ancho a lo largo del borde, fondo hacia el cruce, alto en vertical. Mira hacia los peatones (-t).
            float W = 0.075f * k, D = 0.04f * k, H = 0.12f * k;
            Vector2 cb = base0;                        // centro de la caja en planta (sobre el poste)
            float zc = Z + H * 0.5f;
            var mb = new MeshBatch();
            Vector2 zb = new Vector2(0f, zc - H * 0.5f), zt = new Vector2(0f, zc + H * 0.5f);
            var faceCol = new Color(0.15f, 0.09f, 0.09f, 1f);
            var sideCol = new Color(0.12f, 0.12f, 0.13f, 1f);
            for (int f = 0; f < 4; f++)
            {
                Vector2 n = f == 0 ? -t : f == 1 ? t : f == 2 ? -side : side;
                if (n.y > -0.02f) continue;                      // solo las caras que miran hacia abajo en pantalla
                Vector2 e = f < 2 ? side : t;
                float half = f < 2 ? W * 0.5f : D * 0.5f;
                Vector2 fc = cb + n * (f < 2 ? D * 0.5f : W * 0.5f);
                mb.Quad(fc - e * half + zb, fc + e * half + zb, fc + e * half + zt, fc - e * half + zt, f == 0 ? faceCol : sideCol);
            }
            Vector2 q0 = cb - side * (W * 0.5f) - t * (D * 0.5f), q1 = cb + side * (W * 0.5f) - t * (D * 0.5f);
            Vector2 q2 = cb + side * (W * 0.5f) + t * (D * 0.5f), q3 = cb - side * (W * 0.5f) + t * (D * 0.5f);
            mb.Quad(q0 + zt, q1 + zt, q2 + zt, q3 + zt, new Color(0.07f, 0.07f, 0.08f, 1f));
            mb.Build(parent, "PedBox", order + 3);

            bool faceVisible = (-t).y < -0.2f;   // solo si la cara de las luces mira hacia abajo en pantalla (hacia nosotros)
            Vector2 fcF = cb - t * (D * 0.5f + 0.002f * k);
            Vector2 pr = fcF + new Vector2(0f, zc + H * 0.24f), pg = fcF + new Vector2(0f, zc - H * 0.24f);
            float dsz = faceVisible ? 0.04f * k : 0f;
            var r = SpriteFactory.Make("PedRed", parent, SpriteFactory.Circle, new Color(0.25f, 0.07f, 0.07f), pr, Vector2.one * dsz, order + 4);
            var y = SpriteFactory.Make("PedYellow", parent, SpriteFactory.Circle, new Color(0f, 0f, 0f, 0f), fcF + new Vector2(0f, zc), Vector2.zero, order + 4);
            var g = SpriteFactory.Make("PedGreen", parent, SpriteFactory.Circle, new Color(0.07f, 0.22f, 0.1f), pg, Vector2.one * dsz, order + 4);
            var halo = SpriteFactory.Make("PedHalo", parent, SpriteFactory.Soft, new Color(1f, 0.25f, 0.2f, 0.6f), pr, Vector2.one * 0.14f * k, order + 5);
            return new List<SpriteRenderer[]> { new[] { r, y, g, halo } };
        }

        /// <summary>
        /// Dónde va el poste del semáforo peatonal, como en la vida real: nunca encima de la acera ni de la calle. Desde el
        /// otro lado del cruce se busca el primer sitio libre: si la acera termina, justo después de ella, en medio; si la
        /// acera sigue, a su lado. Sin red (o sin sitio cerca) va al lado derecho del paso, como antes.
        /// </summary>
        Vector2 PedLightSpot(Vector2 far, Vector2 t, Vector2 side, float fallback)
        {
            var net = Net;
            Vector2 fb = far + side * fallback;
            if (net == null) return fb;
            System.Func<Vector2, bool> free = p =>
            {
                foreach (var o in net.roads)
                {
                    float dist;
                    o.center.Project(p, out dist);
                    if (dist < o.halfWidth + 0.05f) return false;
                }
                return true;
            };
            Vector2[] dirs = { t, side, -side };
            for (float d = 0.06f; d <= 1.6f; d += 0.03f)
                for (int i = 0; i < dirs.Length; i++)
                {
                    Vector2 p = far + dirs[i] * d;
                    if (free(p)) return p;
                }
            return fb;
        }

        /// <summary>Un semáforo (los carriles de un nodo o de una aproximación) y el grupo con el que comparte poste.</summary>
        class LightUnit
        {
            public List<RoadSeg> lanes;
            public int side;
            public float k = 1f;
            public bool hidden;
            public LightGeom group;   // geometría del grupo entero: un solo poste y un brazo sobre todos los carriles
            public bool pole;         // este dibuja el poste y el brazo del grupo
            public float groupK = 1f;
        }

        /// <summary>
        /// Como en la vida real: los semáforos de carriles contiguos del mismo sentido comparten un solo poste. El poste va a
        /// la derecha (pasando la acera o el carril bici si los hay); si a la derecha hay otro carril de coches y a la
        /// izquierda no, va a la izquierda.
        /// </summary>
        static void GroupLights(List<LightUnit> units, RoadNetworkManager net)
        {
            int n = units.Count;
            var par = new int[n];
            for (int i = 0; i < n; i++) par[i] = i;
            System.Func<int, int> find = null;
            find = x => par[x] == x ? x : (par[x] = find(par[x]));
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (UnitsAdjacent(units[i], units[j])) par[find(i)] = find(j);
            var groups = new Dictionary<int, List<LightUnit>>();
            for (int i = 0; i < n; i++)
            {
                int r = find(i);
                List<LightUnit> gl;
                if (!groups.TryGetValue(r, out gl)) groups[r] = gl = new List<LightUnit>();
                gl.Add(units[i]);
            }
            foreach (var gl in groups.Values)
            {
                var lanes = new List<RoadSeg>();
                float k = 0.6f;
                bool manualLeft = false;
                foreach (var u in gl) { lanes.AddRange(u.lanes); k = Mathf.Max(k, u.k); if (u.side == 1) manualLeft = true; }
                Vector2 t = Vector2.zero;
                foreach (var l in lanes) t += l.path.TangentAt(l.Length);
                t = t.sqrMagnitude > 1e-6f ? t.normalized : Vector2.up;
                Vector2 right = Geo.Right(t);
                RoadSeg rMost = lanes[0], lMost = lanes[0];
                float rd = float.MinValue, ld = float.MaxValue;
                foreach (var l in lanes)
                {
                    float d = Vector2.Dot(StopOf(l), right);
                    if (d > rd) { rd = d; rMost = l; }
                    if (d < ld) { ld = d; lMost = l; }
                }
                float softR, softL;
                bool carR = CarBeside(net, rMost, right, lanes, out softR);
                bool carL = CarBeside(net, lMost, -right, lanes, out softL);
                int side = manualLeft || (carR && !carL) ? 1 : 0;
                if (LeftHand && !manualLeft) side = carL && !carR ? 0 : 1;   // tráfico por la izquierda: el poste va a la izquierda
                float extra = side == 0 ? (carR ? 0f : softR) : (carL ? 0f : softL);
                var g = MakeLightGeom(lanes, side, extra);
                bool given = false;
                foreach (var u in gl)
                {
                    u.group = g; u.groupK = k;
                    u.pole = !given && !u.hidden;
                    if (u.pole) given = true;
                }
            }
        }

        static Vector2 StopOf(RoadSeg l) { return l.path.PointAt(Mathf.Clamp(l.stopS - 0.03f, 0f, l.Length)); }

        static bool UnitsAdjacent(LightUnit u, LightUnit v)
        {
            foreach (var a in u.lanes)
                foreach (var b in v.lanes)
                {
                    Vector2 ta = a.path.TangentAt(a.Length).normalized, tb = b.path.TangentAt(b.Length).normalized;
                    if (Vector2.Dot(ta, tb) < 0.9f) continue;
                    Vector2 d = StopOf(b) - StopOf(a);
                    if (Mathf.Abs(Vector2.Dot(d, ta)) > 0.35f) continue;
                    float across = Mathf.Abs(Vector2.Dot(d, Geo.Right(ta)));
                    if (Mathf.Abs(across - (a.width + b.width) * 0.5f) < 0.15f) return true;
                }
            return false;
        }

        /// <summary>¿Hay un carril de coches pegado a este lado? Las aceras y carriles bici que haya antes se suman en «soft».</summary>
        static bool CarBeside(RoadNetworkManager net, RoadSeg from, Vector2 dir, List<RoadSeg> exclude, out float soft)
        {
            soft = 0f;
            Vector2 cur = StopOf(from);
            float curW = from.width;
            for (int step = 0; step < 4; step++)
            {
                RoadSeg hit = null;
                Vector2 hitP = Vector2.zero;
                foreach (var sg in net.segs)
                {
                    if (!sg.laneMode || sg.kind != SegKind.Lane || exclude.Contains(sg) || sg == from) continue;
                    Vector2 q = cur + dir * (curW * 0.5f + sg.width * 0.5f);
                    float dist;
                    float sp = sg.path.Project(q, out dist);
                    if (dist > Mathf.Max(0.08f, sg.width * 0.35f)) continue;
                    if (Mathf.Abs(Vector2.Dot(sg.path.TangentAt(sp).normalized, Geo.Right(dir))) < 0.6f) continue;
                    hit = sg; hitP = sg.path.PointAt(sp);
                    break;
                }
                if (hit == null) return false;
                if (hit.laneType == RoadData.LaneVehicle) return true;
                soft += hit.width;
                cur = hitP; curW = hit.width;
            }
            return false;
        }

        /// <summary>Cabezas de un semáforo sobre el brazo de su grupo (una por carril propio).</summary>
        static LightGeom UnitGeom(LightGeom grp, IList<RoadSeg> lanes)
        {
            var g = grp;
            Vector2 arm = grp.armEnd - grp.pole;
            Vector2 along = arm.sqrMagnitude > 1e-6f ? arm.normalized : Vector2.right;
            g.stops = new Vector2[lanes.Count];
            g.widths = new float[lanes.Count];
            g.heads = new Vector2[lanes.Count];
            for (int i = 0; i < lanes.Count; i++)
            {
                g.stops[i] = StopOf(lanes[i]);
                g.widths[i] = lanes[i].width;
                g.heads[i] = grp.pole + along * Vector2.Dot(g.stops[i] - grp.pole, along);
            }
            return g;
        }

        /// <summary>Semáforos en el editor, como el original: poste, brazo con una cabeza por carril y brillo rojo en la línea de parada.</summary>
        void DrawEditorLights(MapData map, RoadNetworkManager net)
        {
            var done = new HashSet<int>();
            var units = new List<LightUnit>();
            foreach (var s in map.signs)
            {
                if (s.type != SignType.Light || !done.Add(s.node)) continue;
                var hub = net.HubOf(s.node);
                if (hub == null || hub.inLanes.Count == 0) continue;
                var lanes = new List<RoadSeg>();
                foreach (var il in hub.inLanes) if (map.FindSign(il.roadId, s.node, SignType.Light) != null) lanes.Add(il);
                if (lanes.Count == 0) continue;
                if (IsPedLight(lanes))
                {
                    // Semáforo peatonal: poste propio en el borde, no se agrupa con los de los coches.
                    if (s.hidden) continue;
                    int lyp = LI(lanes[0].LayerAt(lanes[0].Length));
                    int op = RoadOrder(lyp + MapData.MinLayer) + 4;
                    var ph = DrawPedLight(roadRoot, lanes, hub.outLanes, 1f, op + 1, op);
                    foreach (var h in ph) { h[0].color = SpriteFactory.Hex("#FF3B30"); h[3].transform.localPosition = h[0].transform.localPosition; }
                    continue;
                }
                units.Add(new LightUnit { lanes = lanes, side = s.side, k = Mathf.Clamp(s.size <= 0.01f ? 1f : s.size, 0.6f, 1.8f), hidden = s.hidden });
            }
            GroupLights(units, net);
            foreach (var u in units)
            {
                var lanes = u.lanes;
                var g = MakeLightGeom(lanes, u.side);
                int ly = LI(lanes[0].LayerAt(lanes[0].Length));
                int order = RoadOrder(ly + MapData.MinLayer) + 4;
                float k = u.groupK;
                for (int i = 0; i < lanes.Count; i++)
                {
                    float ang = Geo.Angle(g.t);
                    SpriteFactory.Make("LightGlow", roadRoot, SpriteFactory.Soft, new Color(1f, 0.15f, 0.15f, 0.4f), g.stops[i] - g.t * 0.1f, Vector2.one * (g.widths[i] * 1.8f), order, 0f);
                    SpriteFactory.Make("LightBar", roadRoot, SpriteFactory.Square, new Color(1f, 0.88f, 0.88f, 0.9f), g.stops[i], new Vector2(0.11f, g.widths[i] - 0.02f), order + 1, ang);
                }
                if (u.hidden) continue;
                var heads = DrawLightHardware(roadRoot, UnitGeom(u.group, lanes), k, order + 1, u.pole);
                foreach (var h in heads) { h[0].color = SpriteFactory.Hex("#FF3B30"); h[3].transform.localPosition = h[0].transform.localPosition; }
            }
        }
    }
}
