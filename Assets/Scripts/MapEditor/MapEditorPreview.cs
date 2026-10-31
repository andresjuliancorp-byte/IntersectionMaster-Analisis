using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Core;
using IntersectionMaster.Data;
using IntersectionMaster.Traffic;

namespace IntersectionMaster.MapEditing
{
    /// <summary>
    /// Vista previa de la calle mientras se dibuja o está pendiente de confirmar: calzada con su ancho real, bordes,
    /// línea central, divisiones de carril, flechas de sentido, longitud aproximada, ángulo, indicadores de conexión
    /// (válida o no) y botones ✓ / ✗ sobre el mapa. Es solo una superposición del editor: no cambia el mapa.
    /// </summary>
    public partial class MapEditorController
    {
        readonly List<TextMesh> previewLabels = new List<TextMesh>();
        int previewLabelsUsed;

        static readonly Color PrevAsphalt = new Color(0.20f, 0.21f, 0.24f, 0.92f);
        static readonly Color PrevAsphaltBad = new Color(0.50f, 0.16f, 0.16f, 0.9f);
        static readonly Color PrevGreen = new Color(0.30f, 0.90f, 0.40f, 1f);
        static readonly Color PrevRed = new Color(1f, 0.32f, 0.30f, 1f);

        void BeginPreviewLabels() { previewLabelsUsed = 0; }

        void EndPreviewLabels()
        {
            for (int i = 0; i < previewLabels.Count; i++)
                if (previewLabels[i] != null) previewLabels[i].gameObject.SetActive(i < previewLabelsUsed);
        }

        /// <summary>Texto sobre el mapa con tamaño constante en pantalla y una caja oscura detrás para que se lea sobre cualquier fondo.</summary>
        void PreviewLabel(string text, Vector2 pos, float screenPx, Color col)
        {
            TextMesh tm;
            if (previewLabelsUsed < previewLabels.Count) tm = previewLabels[previewLabelsUsed];
            else
            {
                var go = new GameObject("PreviewText");
                go.transform.SetParent(overlayRoot, false);
                tm = go.AddComponent<TextMesh>();
                var font = IntersectionMaster.UI.UIKit.DefaultFont;
                tm.font = font;
                tm.fontSize = 64;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    if (font != null) mr.sharedMaterial = font.material;
                    mr.sortingOrder = 70;
                }
                previewLabels.Add(tm);
            }
            previewLabelsUsed++;
            float h = screenPx * Wpp;
            tm.gameObject.SetActive(true);
            tm.transform.position = new Vector3(pos.x, pos.y, 0f);
            tm.text = text;
            tm.characterSize = h / 6f;
            tm.color = col;
            var lines = text.Split('\n');
            int maxLen = 1;
            foreach (var ln in lines) if (ln.Length > maxLen) maxLen = ln.Length;
            float boxW = maxLen * h * 0.62f + h * 0.8f;
            Spr(SpriteFactory.Square, new Color(0.05f, 0.07f, 0.1f, 0.82f), pos, new Vector2(boxW, h * (0.5f + lines.Length)), 69);
        }

        /// <summary>Comprueba si la calle que se va a crear es posible. Devuelve el motivo si no lo es.</summary>
        bool PreviewCheck(Vector2 a, Vector2 b, int startNode, int endNode, out string problem)
        {
            problem = "";
            if (Vector2.Distance(a, b) < (Map != null && Map.IsLaneMap ? 0.4f : 0.7f)) { problem = "Muy corta"; return false; }
            if (startNode >= 0 && endNode >= 0 && Map.FindRoad(startNode, endNode) != null) { problem = "Ese carril ya existe"; return false; }
            if (Map.roads.Count >= MapData.MaxRoads) { problem = "Límite de carriles"; return false; }
            return true;
        }

        /// <summary>Ángulo agudo entre la calle nueva (saliendo del punto en la dirección dirOut) y las calles ya existentes en ese punto. -1 si no hay.</summary>
        float JunctionAngle(int nodeId, int roadId, Vector2 at, Vector2 dirOut)
        {
            if (dirOut.sqrMagnitude < 1e-6f) return -1f;
            float best = -1f;
            if (nodeId >= 0)
            {
                var node = Map.Node(nodeId);
                if (node == null) return -1f;
                foreach (var r in Map.RoadsAt(nodeId))
                {
                    Vector2 other = Map.PointOnRoad(r, r.a == nodeId ? 0.12f : 0.88f);
                    Vector2 d = other - node.Pos;
                    if (d.sqrMagnitude < 1e-6f) continue;
                    float ang = Vector2.Angle(dirOut, d);
                    if (best < 0f || ang < best) best = ang;
                }
            }
            else if (roadId >= 0)
            {
                var r = Map.Road(roadId);
                if (r == null) return -1f;
                float dist;
                float t = Map.NearestParam(r, at, out dist);
                Vector2 t0 = Map.PointOnRoad(r, Mathf.Clamp01(t - 0.05f));
                Vector2 t1 = Map.PointOnRoad(r, Mathf.Clamp01(t + 0.05f));
                Vector2 d = t1 - t0;
                if (d.sqrMagnitude < 1e-6f) return -1f;
                float ang = Vector2.Angle(dirOut, d);
                best = Mathf.Min(ang, 180f - ang);
            }
            return best;
        }

        /// <summary>Posiciones de los botones ✓ (confirmar) y ✗ (cancelar) de la calle pendiente, a los lados del tirador central.</summary>
        void PendingButtonPositions(out Vector2 ok, out Vector2 no)
        {
            if (Map != null && Map.IsLaneMap)
            {
                Vector2 a1, a2, a3;
                LaneBoxParts(out ok, out no, out a1, out a2, out a3);
                return;
            }
            Vector2 c = (pS + pE) * 0.5f + pBend;
            Vector2 hp = Geo.Quad(pS, c, pE, 0.5f);
            Vector2 chord = pE - pS;
            Vector2 n = chord.sqrMagnitude > 1e-6f ? new Vector2(-chord.y, chord.x).normalized : Vector2.up;
            int lanes = Mathf.Clamp(NewRoadLanes, 1, 3) * (NewRoadOneWay ? 1 : 2);
            float off = lanes * RoadNetworkManager.LaneWidth * 0.5f + 62f * Wpp;
            ok = hp + n * off;
            no = hp - n * off;
        }

        /// <summary>Botón de invertir sentido: a lo largo de la calle, junto al tirador central.</summary>
        Vector2 PendingFlipPosition()
        {
            Vector2 c = (pS + pE) * 0.5f + pBend;
            Vector2 hp = Geo.Quad(pS, c, pE, 0.5f);
            Vector2 chord = pE - pS;
            Vector2 d = chord.sqrMagnitude > 1e-6f ? chord.normalized : Vector2.right;
            return hp + d * (70f * Wpp);
        }

        void DrawFlipButton(Vector2 pos)
        {
            float wpp = Wpp;
            float r = 26f * wpp;
            Spr(SpriteFactory.Circle, Color.white, pos, Vector2.one * r * 2.2f, 60);
            Spr(SpriteFactory.Circle, new Color(0.2f, 0.45f, 0.85f, 1f), pos, Vector2.one * r * 2f, 61);
            float lw = 4f * wpp;
            Vector2 up = pos + new Vector2(0f, 0.28f) * r, dn = pos - new Vector2(0f, 0.28f) * r;
            Segment(up - new Vector2(0.55f, 0f) * r, up + new Vector2(0.45f, 0f) * r, lw, Color.white, 62);
            Spr(SpriteFactory.Triangle, Color.white, up + new Vector2(0.55f, 0f) * r, Vector2.one * r * 0.5f, 62, 0f);
            Segment(dn + new Vector2(0.55f, 0f) * r, dn - new Vector2(0.45f, 0f) * r, lw, Color.white, 62);
            Spr(SpriteFactory.Triangle, Color.white, dn - new Vector2(0.55f, 0f) * r, Vector2.one * r * 0.5f, 62, 180f);
        }

        void DrawRoundButton(Vector2 pos, Color col, bool check)
        {
            float wpp = Wpp;
            float r = 26f * wpp;
            Spr(SpriteFactory.Circle, Color.white, pos, Vector2.one * r * 2.2f, 60);
            Spr(SpriteFactory.Circle, col, pos, Vector2.one * r * 2f, 61);
            Color w = Color.white;
            float lw = 5f * wpp;
            if (check)
            {
                Vector2 p0 = pos + new Vector2(-0.45f, 0.0f) * r, p1 = pos + new Vector2(-0.12f, -0.38f) * r, p2 = pos + new Vector2(0.5f, 0.42f) * r;
                Segment(p0, p1, lw, w, 62);
                Segment(p1, p2, lw, w, 62);
            }
            else
            {
                Segment(pos + new Vector2(-0.4f, -0.4f) * r, pos + new Vector2(0.4f, 0.4f) * r, lw, w, 62);
                Segment(pos + new Vector2(-0.4f, 0.4f) * r, pos + new Vector2(0.4f, -0.4f) * r, lw, w, 62);
            }
        }

        /// <summary>Dibuja la calle tal como quedará: calzada, bordes, línea central, carriles, flechas, medidas y conexiones.</summary>
        void DrawRoadPreview(Vector2 a, Vector2 b, Vector2 bend, int startNode, int startRoad, int endNode, int endRoad, bool pending)
        {
            if (Map != null && Map.IsLaneMap) { DrawLanePreview(a, b, bend, startNode, startRoad, endNode, endRoad, pending); return; }
            float wpp = Wpp;
            string problem;
            bool valid = PreviewCheck(a, b, startNode, endNode, out problem);

            int lanes = Mathf.Clamp(NewRoadLanes, 1, 3);
            int lAB = lanes, lBA = NewRoadOneWay ? 0 : lanes;
            float lw = RoadNetworkManager.LaneWidth;
            float w = (lAB + lBA) * lw;
            Vector2 c = (a + b) * 0.5f + bend;

            float approxLen = Vector2.Distance(a, c) + Vector2.Distance(c, b);
            int n = Mathf.Clamp(Mathf.CeilToInt(approxLen / 0.2f), 6, 48);
            var pts = new Vector2[n + 1];
            var nrm = new Vector2[n + 1];   // normal a la derecha del sentido a → b
            var tan = new Vector2[n + 1];
            for (int i = 0; i <= n; i++) pts[i] = Geo.Quad(a, c, b, i / (float)n);
            float len = 0f;
            for (int i = 0; i < n; i++) len += Vector2.Distance(pts[i], pts[i + 1]);
            for (int i = 0; i <= n; i++)
            {
                Vector2 d = pts[Mathf.Min(i + 1, n)] - pts[Mathf.Max(i - 1, 0)];
                tan[i] = d.sqrMagnitude > 1e-8f ? d.normalized : Vector2.right;
                nrm[i] = new Vector2(tan[i].y, -tan[i].x);
            }

            // Calzada.
            Color asph = valid ? PrevAsphalt : PrevAsphaltBad;
            for (int i = 0; i < n; i++) Segment(pts[i], pts[i + 1], w, asph, 44);
            for (int i = 1; i < n; i++) Spr(SpriteFactory.Circle, asph, pts[i], Vector2.one * w, 44);

            // Bordes.
            Color edge = new Color(1f, 1f, 1f, 0.9f);
            for (int s = -1; s <= 1; s += 2)
            {
                float o = s * (w * 0.5f - 0.035f);
                for (int i = 0; i < n; i++) Segment(pts[i] + nrm[i] * o, pts[i + 1] + nrm[i + 1] * o, 0.045f, edge, 45);
            }

            // Línea central amarilla (doble sentido) y divisiones de carril discontinuas.
            Color yellow = new Color(1f, 0.82f, 0.15f, 1f);
            Color dash = new Color(1f, 1f, 1f, 0.75f);
            if (lAB > 0 && lBA > 0)
            {
                for (int i = 0; i < n; i++) Segment(pts[i], pts[i + 1], 0.05f, yellow, 45);
                for (int k = 1; k < lanes; k++)
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        float o = sgn * k * lw;
                        for (int i = 0; i < n; i += 2) Segment(pts[i] + nrm[i] * o, pts[i + 1] + nrm[i + 1] * o, 0.04f, dash, 45);
                    }
            }
            else
            {
                for (int k = 1; k < lanes; k++)
                {
                    float o = (k - lanes * 0.5f) * lw;
                    for (int i = 0; i < n; i += 2) Segment(pts[i] + nrm[i] * o, pts[i + 1] + nrm[i + 1] * o, 0.04f, dash, 45);
                }
            }

            // Flechas de sentido (una por carril; más si la calle es larga).
            Color arrow = new Color(1f, 1f, 1f, 0.95f);
            var marks = len > 4.5f ? new[] { 0.25f, 0.75f } : new[] { 0.5f };
            for (int lane = 0; lane < lAB + lBA; lane++)
            {
                bool forward; float off;
                if (lBA == 0) { forward = true; off = (lane - (lAB - 1) * 0.5f) * lw; }
                else if (lane < lAB) { forward = true; off = (lane + 0.5f) * lw; }       // sentido a → b: a la derecha
                else { forward = false; off = -((lane - lAB) + 0.5f) * lw; }              // sentido b → a: a la izquierda
                for (int m = 0; m < marks.Length; m++)
                {
                    int idx = Mathf.Clamp(Mathf.RoundToInt(n * marks[m]), 1, n - 1);
                    Vector2 dir = forward ? tan[idx] : -tan[idx];
                    Vector2 pos = pts[idx] + nrm[idx] * off;
                    float al = Mathf.Min(0.34f, lw * 0.95f);
                    Segment(pos - dir * al * 0.6f, pos + dir * al * 0.1f, 0.05f, arrow, 46);
                    Spr(SpriteFactory.Triangle, arrow, pos + dir * al * 0.42f, new Vector2(al * 0.7f, al * 0.7f), 46, Geo.Angle(dir));
                }
            }

            // Extremos: conexión válida (verde), libre (blanco) o imposible (rojo).
            DrawPreviewEnd(a, startNode >= 0, startRoad >= 0, valid);
            DrawPreviewEnd(b, endNode >= 0, endRoad >= 0, valid);

            // Medidas: longitud aproximada y ángulo de orientación.
            Vector2 chord = b - a;
            float heading = Mathf.Repeat(Geo.Angle(chord), 360f);
            string txt = Mathf.RoundToInt(len * MetersPerUnit) + " m  ·  " + Mathf.RoundToInt(heading) + "°";
            if (!valid) txt = problem;
            int mid = n / 2;
            int li = Mathf.Max(1, n / 5);   // cerca del inicio y al lado contrario de los botones de confirmar
            PreviewLabel(txt, pts[li] + nrm[li] * (w * 0.5f + 30f * wpp), 20f, valid ? Color.white : PrevRed);

            // Ángulo con la calle a la que se conecta (si se conecta a algo).
            if (valid)
            {
                float angS = JunctionAngle(startNode, startRoad, a, tan[0]);
                float angE = JunctionAngle(endNode, endRoad, b, -tan[n]);
                if (angS >= 0f) PreviewLabel(JunctionText(angS), a - tan[0] * (34f * wpp) + nrm[0] * (34f * wpp), 17f, angS < 25f ? new Color(1f, 0.7f, 0.25f) : PrevGreen);
                if (angE >= 0f) PreviewLabel(JunctionText(angE), b + tan[n] * (34f * wpp) + nrm[n] * (34f * wpp), 17f, angE < 25f ? new Color(1f, 0.7f, 0.25f) : PrevGreen);
            }

            if (pending)
            {
                // Tirador de curvatura y botones ✓ / ✗ sobre el mapa.
                Vector2 hp = pts[mid];
                Spr(SpriteFactory.Square, Color.white, hp, Vector2.one * 26f * wpp, 58, 45f);
                Spr(SpriteFactory.Square, PrevGreen, hp, Vector2.one * 18f * wpp, 59, 45f);
                Vector2 ok, no;
                PendingButtonPositions(out ok, out no);
                DrawRoundButton(ok, valid ? new Color(0.18f, 0.7f, 0.3f, 1f) : new Color(0.45f, 0.45f, 0.5f, 1f), true);
                DrawRoundButton(no, new Color(0.85f, 0.2f, 0.2f, 1f), false);
                DrawFlipButton(PendingFlipPosition());
                // Tiradores de los extremos: arrástralos para alargar, acortar o cambiar la dirección.
                Spr(SpriteFactory.Ring, Color.white, a, Vector2.one * 58f * wpp, 59);
                Spr(SpriteFactory.Ring, Color.white, b, Vector2.one * 58f * wpp, 59);
            }
        }

        /// <summary>Centro del recuadro de confirmación del carril pendiente (al lado del extremo final).</summary>
        Vector2 LaneBoxCenter()
        {
            Vector2 chord = pE - pS;
            Vector2 dir = chord.sqrMagnitude > 1e-6f ? chord.normalized : Vector2.right;
            Vector2 side = new Vector2(-dir.y, dir.x);
            if (side.x < 0f) side = -side;
            return pE + side * (95f * Wpp) + dir * (10f * Wpp);
        }

        /// <summary>Vista previa del carril (modelo por carriles): franja verde (roja si no vale), eje azul con chevrones y medidas.</summary>
        void DrawLanePreview(Vector2 a, Vector2 b, Vector2 bend, int startNode, int startRoad, int endNode, int endRoad, bool pending)
        {
            float wpp = Wpp;
            string problem;
            bool valid = PreviewCheck(a, b, startNode, endNode, out problem);
            string typeProb = LaneTypeProblem(startNode, startRoad) ?? LaneTypeProblem(endNode, endRoad);
            if (valid && typeProb != null) { valid = false; problem = typeProb; }

            float w = RoadData.WidthOf(NewLaneType);   // del ancho real del carril, como el original
            Vector2 c = (a + b) * 0.5f + bend;
            float approxLen = Vector2.Distance(a, c) + Vector2.Distance(c, b);
            int n = Mathf.Clamp(Mathf.CeilToInt(approxLen / 0.15f), 6, 64);
            var pts = new Vector2[n + 1];
            for (int i = 0; i <= n; i++) pts[i] = Geo.Quad(a, c, b, i / (float)n);
            float len = 0f;
            for (int i = 0; i < n; i++) len += Vector2.Distance(pts[i], pts[i + 1]);

            Color band = valid ? new Color(0f, 0.85f, 0.05f, 0.95f) : new Color(0.95f, 0.08f, 0.08f, 0.95f);
            for (int i = 0; i < n; i++) Segment(pts[i], pts[i + 1], w, band, 44);
            for (int i = 1; i < n; i++) Spr(SpriteFactory.Circle, band, pts[i], Vector2.one * w, 44);
            Color axis = new Color(0.1f, 0.2f, 0.95f, 1f);
            for (int i = 0; i < n; i++) Segment(pts[i], pts[i + 1], 0.035f, axis, 45);
            float acc = 0f, next = 0.3f;
            for (int i = 0; i < n; i++)
            {
                float sl = Vector2.Distance(pts[i], pts[i + 1]);
                while (acc + sl >= next)
                {
                    float k = (next - acc) / Mathf.Max(sl, 1e-5f);
                    Vector2 p = Vector2.Lerp(pts[i], pts[i + 1], k);
                    Vector2 t = (pts[i + 1] - pts[i]).normalized;
                    Vector2 r = new Vector2(t.y, -t.x);
                    float cs = 0.07f;
                    Segment(p, p - t * cs + r * cs, 0.025f, axis, 46);
                    Segment(p, p - t * cs - r * cs, 0.025f, axis, 46);
                    next += 0.4f;
                }
                acc += sl;
            }

            // Medida grande junto al carril y ángulo de giro respecto al carril al que se une (0 = sigue recto).
            int li = Mathf.Max(1, n / 2);
            Vector2 tm = (pts[Mathf.Min(li + 1, n)] - pts[li - 1]).normalized;
            Vector2 nm = new Vector2(-tm.y, tm.x);
            PreviewLabel(Mathf.RoundToInt(len * MetersPerUnit) + "m", pts[li] + nm * (w * 0.5f + 40f * wpp), 30f, Color.white);
            float angS = JoinTurn(startNode, a, (pts[1] - pts[0]), true);
            float angE = JoinTurn(endNode, b, (pts[n] - pts[n - 1]), false);
            PreviewLabel(angS < 0f ? "0" : Mathf.RoundToInt(angS).ToString(), a - (pts[1] - pts[0]).normalized * (30f * wpp), 24f, Color.white);
            PreviewLabel(angE < 0f ? "0" : Mathf.RoundToInt(angE).ToString(), b + (pts[n] - pts[n - 1]).normalized * (30f * wpp), 24f, Color.white);

            if (!pending)
            {
                if (!valid) PreviewLabel(WrapText(problem, 24), pts[li] - nm * (w * 0.5f + 40f * wpp), 18f, PrevRed);
                return;
            }

            // Tirador central (curvatura) y extremos.
            Vector2 hp = Geo.Quad(a, c, b, 0.5f);
            Spr(SpriteFactory.Square, Color.white, hp, Vector2.one * 24f * wpp, 58, 45f);
            Spr(SpriteFactory.Square, axis, hp, Vector2.one * 16f * wpp, 59, 45f);
            Spr(SpriteFactory.Ring, Color.white, a, Vector2.one * 50f * wpp, 59);
            Spr(SpriteFactory.Ring, Color.white, b, Vector2.one * 50f * wpp, 59);

            // Recuadro como el de la referencia: rampa, ángulo, ✓ y ✗ (y el motivo en rojo si no se puede).
            DrawLaneBox(valid, WrapText(problem, 24), Mathf.Repeat(Geo.Angle(b - a), 360f));
        }

        static string WrapText(string text, int width)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= width) return text;
            var words = text.Split(' ');
            var sb = new System.Text.StringBuilder();
            int line = 0;
            foreach (var w in words)
            {
                if (line > 0 && line + 1 + w.Length > width) { sb.Append('\n'); line = 0; }
                else if (line > 0) { sb.Append(' '); line++; }
                sb.Append(w); line += w.Length;
            }
            return sb.ToString();
        }

        /// <summary>Giro (grados) entre el carril nuevo y el carril existente con el que se une en ese nodo. -1 si no hay.</summary>
        float JoinTurn(int nodeId, Vector2 at, Vector2 dirNew, bool atStart)
        {
            if (nodeId < 0 || dirNew.sqrMagnitude < 1e-8f) return -1f;
            float best = -1f;
            foreach (var r in Map.RoadsAt(nodeId))
            {
                var pl = Map.CenterOf(r);
                // Al empezar, se continúa un carril que llega (r.b == nodo); al terminar, se entra en uno que sale (r.a == nodo).
                Vector2 d;
                if (atStart && r.b == nodeId) d = pl.TangentAt(pl.Length);
                else if (!atStart && r.a == nodeId) d = pl.TangentAt(0f);
                else continue;
                float ang = Vector2.Angle(dirNew, d);
                if (best < 0f || ang < best) best = ang;
            }
            return best;
        }

        static string JunctionText(float ang)
        {
            return ang < 25f ? "Ángulo cerrado " + Mathf.RoundToInt(ang) + "°" : "Cruce " + Mathf.RoundToInt(ang) + "°";
        }

        void DrawPreviewEnd(Vector2 p, bool onNode, bool onRoad, bool valid)
        {
            float wpp = Wpp;
            if (!valid)
            {
                Spr(SpriteFactory.Ring, PrevRed, p, Vector2.one * 44f * wpp, 58);
                return;
            }
            if (onNode || onRoad)
            {
                Spr(SpriteFactory.Ring, PrevGreen, p, Vector2.one * 46f * wpp, 58);
                Spr(SpriteFactory.Circle, PrevGreen, p, Vector2.one * 16f * wpp, 58);
            }
            else
            {
                Spr(SpriteFactory.Circle, Color.white, p, Vector2.one * 20f * wpp, 58);
            }
        }
    }
}
