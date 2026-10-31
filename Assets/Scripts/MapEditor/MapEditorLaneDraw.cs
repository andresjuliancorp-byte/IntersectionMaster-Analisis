using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Core;
using IntersectionMaster.Data;

namespace IntersectionMaster.MapEditing
{
    /// <summary>
    /// Dibujo de carriles como en Intersection Controller 2: recuadro de confirmación junto al final del carril (rampa de
    /// puente, ángulo, ✓ y ✗) que nunca queda debajo de los paneles, cuadraditos a los lados para dibujar el carril de al
    /// lado, capas de -4 a 4 y enganche preciso a los extremos de los carriles.
    /// </summary>
    public partial class MapEditorController
    {
        /// <summary>Rampa del carril pendiente: -1 baja una capa, 0 plano, +1 sube una capa.</summary>
        public int PendingRamp;
        int lastLaneId = -1;

        // ---------------- Capas ----------------

        public void SetCurrentLayer(int layer)
        {
            CurrentLayer = Mathf.Clamp(layer, MapData.MinLayer, MapData.MaxLayer);
            if (MapRenderer.EditLayer != CurrentLayer) { MapRenderer.EditLayer = CurrentLayer; Rebuild(true); }
            Status = CurrentLayer == 0 ? "Capa 0: el suelo." : (CurrentLayer > 0 ? "Capa " + CurrentLayer + ": por encima (puentes)." : "Capa " + CurrentLayer + ": por debajo (túneles).");
        }

        public void SetSelectedLayer(int layer)
        {
            Edit(() =>
            {
                var r = SelectedRoad; if (r == null) return false;
                r.layer = Mathf.Clamp(layer, MapData.MinLayer, MapData.MaxLayer);
                if (r.layer + r.ramp < MapData.MinLayer || r.layer + r.ramp > MapData.MaxLayer) r.ramp = 0;
                return true;
            }, true);
        }

        public void SetSelectedRamp(int ramp)
        {
            Edit(() =>
            {
                var r = SelectedRoad; if (r == null) return false;
                int nr = Mathf.Clamp(ramp, -1, 1);
                if (r.layer + nr < MapData.MinLayer || r.layer + nr > MapData.MaxLayer) return false;
                r.ramp = nr;
                return true;
            }, true);
        }

        public void SetPendingRamp(int ramp)
        {
            int nr = Mathf.Clamp(ramp, -1, 1);
            if (CurrentLayer + nr < MapData.MinLayer || CurrentLayer + nr > MapData.MaxLayer) { Status = "No hay más capas en ese sentido."; return; }
            PendingRamp = nr;
            Status = nr == 0 ? "Carril plano (capa " + CurrentLayer + ")." : (nr > 0 ? "Rampa de subida: de la capa " + CurrentLayer + " a la " + (CurrentLayer + 1) + "." : "Rampa de bajada: de la capa " + CurrentLayer + " a la " + (CurrentLayer - 1) + ".");
        }

        // ---------------- Enganche preciso ----------------

        /// <summary>
        /// Distancia de enganche a un nodo en mapas por carriles: generosa (el dedo tapa mucho), pero por debajo de la
        /// separación entre carriles vecinos; entre varios candidatos siempre gana el más cercano.
        /// </summary>
        float LaneNodeTol() { return Mathf.Clamp(Tol(70f), 0.25f, 0.45f); }

        /// <summary>
        /// Nodo más adecuado bajo el dedo: al empezar se prefieren finales libres de carril (para continuarlos); al terminar,
        /// inicios libres (para entrar en ellos). La preferencia solo desempata entre nodos casi igual de cerca.
        /// </summary>
        NodeData PickLaneNode(Vector2 p, bool starting, int exclude)
        {
            float tol = LaneNodeTol();
            NodeData best = null;
            float bestScore = float.MaxValue;
            foreach (var n in Map.nodes)
            {
                if (n.id == exclude) continue;
                float d = Vector2.Distance(n.Pos, p);
                if (d > tol) continue;
                bool ins = false, outs = false;
                foreach (var r in Map.roads) { if (r.b == n.id) ins = true; if (r.a == n.id) outs = true; }
                bool preferred = starting ? (ins && !outs) : (outs && !ins);
                float score = d - (preferred ? 0.05f : 0f);
                if (score < bestScore) { bestScore = score; best = n; }
            }
            return best;
        }

        /// <summary>Nodo que ya ocupa (casi) el mismo punto: así un extremo que cae en la cuadrícula sobre otro nodo se une a él.</summary>
        NodeData NodeAt(Vector2 q, int exclude)
        {
            NodeData best = null;
            float bd = 0.2f;
            foreach (var n in Map.nodes)
            {
                if (n.id == exclude) continue;
                float d = Vector2.Distance(n.Pos, q);
                if (d < bd) { bd = d; best = n; }
            }
            return best;
        }

        /// <summary>Carril cuyo eje pasa cerca del punto (para partirlo y unirse por el medio): dentro de su propio ancho.</summary>
        RoadData PickLaneAxis(Vector2 p, out float t)
        {
            RoadData best = null;
            t = 0f;
            float bd = float.MaxValue;
            foreach (var r in Map.roads)
            {
                if (r.layer != CurrentLayer && r.layer + r.ramp != CurrentLayer) continue;
                float tol = Mathf.Clamp(Tol(36f), 0.12f, r.Width * 0.45f);
                float d;
                float tt = Map.NearestParam(r, p, out d);
                if (d < tol && d < bd) { bd = d; best = r; t = tt; }
            }
            return best;
        }

        /// <summary>
        /// Punto final de un trazo de carril: nodo cercano, cuadradito lateral, eje de otro carril o la cuadrícula (y, si en
        /// la cuadrícula ya hay un nodo, ese nodo). Lo usan el dibujo y el arrastre de extremos del carril pendiente.
        /// </summary>
        void PickLaneEnd(Vector2 p, bool starting, int otherNode, int otherRoad, out int node, out int road, out Vector2 pos)
        {
            node = -1; road = -1;
            var ln = PickLaneNode(p, starting, otherNode);
            Vector2 side;
            bool hasSide = HitSideSquare(p, out side);
            if (ln != null && (!hasSide || Vector2.Distance(ln.Pos, p) <= Vector2.Distance(side, p))) { node = ln.id; pos = ln.Pos; return; }
            if (hasSide) { pos = side; return; }
            float tt;
            var lr = PickLaneAxis(p, out tt);
            if (lr != null && lr.id != otherRoad && !(otherNode >= 0 && (lr.a == otherNode || lr.b == otherNode)))
            {
                int rn, rr; Vector2 rp;
                ResolveRoadSnap(lr, tt, out rn, out rr, out rp);
                if (rn < 0 || rn != otherNode) { node = rn; road = rr; pos = rp; return; }
            }
            pos = Snap(p);
            var at = NodeAt(pos, otherNode);
            if (at != null) { node = at.id; pos = at.Pos; }
        }

        /// <summary>
        /// Si el carril que se dibuja va pegado a otro (empieza y termina a un ancho de sus extremos, en el mismo sentido o
        /// en el contrario), devuelve la curva que lo hace paralelo a él: así no queda hueco ni pico entre los dos y la
        /// línea del medio sale entera.
        /// </summary>
        bool ParallelBend(ref Vector2 a, bool aFree, ref Vector2 b, bool bFree, out Vector2 bend)
        {
            bend = Vector2.zero;
            if (Map == null || !Map.IsLaneMap) return false;
            const float tol = 0.12f, near = 0.18f;   // «near»: extremo libre casi pegado (se acomoda solo, sin hueco)
            float newW = RoadData.WidthOf(NewLaneType);
            foreach (var r in Map.roads)
            {
                if (r.layer != CurrentLayer && r.layer + r.ramp != CurrentLayer) continue;
                var pl = Map.CenterOf(r);
                if (pl.Length < 0.3f) continue;
                Vector2 A = pl.Start, B = pl.End;
                Vector2 nA = Geo.Right(pl.TangentAt(0f).normalized), nB = Geo.Right(pl.TangentAt(pl.Length).normalized);
                float w = (r.Width + newW) * 0.5f;   // de centro a centro: los dos bordes se tocan
                // Si el vecino es curvo y el final se suelta cerca de su par, el final se ajusta para quedar paralelo
                // (como el original: el segundo carril curvo sigue al primero aunque no se acierte el punto exacto).
                float loose = r.Curved && bFree ? Mathf.Max(0.45f, w * 1.6f) : (bFree ? near : tol);
                float tolA = aFree ? near : tol;
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    Vector2 oA = A + nA * (sgn * w), oB = B + nB * (sgn * w);
                    bool same = Vector2.Distance(a, oA) < tolA && Vector2.Distance(b, oB) < loose;
                    bool opp = Vector2.Distance(a, oB) < tolA && Vector2.Distance(b, oA) < loose;
                    if (!same && !opp) continue;
                    if (aFree) a = same ? oA : oB;
                    if (bFree) b = same ? oB : oA;
                    if (!r.Curved) { bend = Vector2.zero; return true; }
                    if (Vector2.Dot(nA, nB) < -0.5f) continue;   // curva de más de 120°: no se puede copiar bien
                    // Ajuste por mínimos cuadrados: el punto de control que deja la curva nueva lo más cerca posible de
                    // la curva desplazada un ancho (la copia exacta de una curva cuadrática no es otra cuadrática).
                    // Se pide 4 mm menos de separación para que, como mucho, se monten un poco y nunca quede hueco.
                    Vector2 C = Map.ControlPoint(r);
                    const int N = 24;
                    Vector2 acc = Vector2.zero; float den = 0f;
                    for (int k = 0; k < N; k++)
                    {
                        float t = (k + 0.5f) / N, u = 1f - t;
                        Vector2 L = Geo.Quad(A, C, B, t);
                        Vector2 dL = 2f * u * (C - A) + 2f * t * (B - C);
                        if (dL.sqrMagnitude < 1e-8f) continue;
                        Vector2 P = L + Geo.Right(dL.normalized) * (sgn * (w - 0.004f));
                        Vector2 R = P - u * u * oA - t * t * oB;
                        float kk = 2f * t * u;
                        acc += R * kk; den += kk * kk;
                    }
                    if (den < 1e-6f) continue;
                    Vector2 c = acc / den;
                    bend = Vector2.ClampMagnitude(c - (a + b) * 0.5f, 14f);
                    return true;
                }
            }
            return false;
        }

        // ---------------- Cuadraditos laterales ----------------

        /// <summary>
        /// Puntos para empezar (o terminar) el carril de al lado: a un ancho de carril a cada lado de los extremos de los
        /// carriles de la capa actual, como en el original. No se pone donde ya hay un nodo.
        /// </summary>
        void SideSquares(List<Vector2> list)
        {
            list.Clear();
            if (Map == null || !Map.IsLaneMap) return;
            Rect vis = VisibleWorldRect();
            foreach (var r in Map.roads)
            {
                if (r.layer != CurrentLayer && r.layer + r.ramp != CurrentLayer) continue;
                var pl = Map.CenterOf(r);
                if (pl.Length < 0.2f) continue;
                float w = (r.Width + RoadData.WidthOf(NewLaneType)) * 0.5f;   // acera o bici quedan pegadas, sin hueco
                for (int end = 0; end < 2; end++)
                {
                    Vector2 at = end == 0 ? pl.Start : pl.End;
                    if (!vis.Contains(at)) continue;
                    Vector2 rt = Geo.Right(pl.TangentAt(end == 0 ? 0f : pl.Length));
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        Vector2 q = at + rt * (w * sgn);
                        if (NodeAt(q, -1) != null) continue;
                        bool dup = false;
                        foreach (var o in list) if ((o - q).sqrMagnitude < 0.01f) { dup = true; break; }
                        if (dup) continue;
                        list.Add(q);
                        if (list.Count >= 300) return;
                    }
                }
            }
        }

        Rect VisibleWorldRect()
        {
            Vector2 a = cam.ScreenToWorld(Vector2.zero);
            Vector2 b = cam.ScreenToWorld(new Vector2(Screen.width, Screen.height));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x) - 1f, Mathf.Min(a.y, b.y) - 1f, Mathf.Max(a.x, b.x) + 1f, Mathf.Max(a.y, b.y) + 1f);
        }

        readonly List<Vector2> sideTmp = new List<Vector2>();

        bool HitSideSquare(Vector2 p, out Vector2 at)
        {
            at = Vector2.zero;
            SideSquares(sideTmp);
            float bd = Mathf.Clamp(Tol(44f), 0.15f, 0.24f);
            bool hit = false;
            foreach (var q in sideTmp)
            {
                float d = Vector2.Distance(q, p);
                if (d < bd) { bd = d; at = q; hit = true; }
            }
            return hit;
        }

        void DrawSideSquares(float wpp)
        {
            SideSquares(sideTmp);
            foreach (var q in sideTmp)
            {
                Spr(SpriteFactory.Square, new Color(0.2f, 0.2f, 0.22f, 0.9f), q, Vector2.one * 16f * wpp, 57);
                Spr(SpriteFactory.Square, new Color(0.55f, 0.6f, 0.55f, 1f), q, Vector2.one * 11f * wpp, 58);
            }
        }

        // ---------------- Recuadro de confirmación ----------------

        float BoxScale { get { return Mathf.Clamp(Screen.height / 2340f, 0.55f, 1.6f); } }
        bool boxHasError;
        Vector2 boxScreen;

        bool OverUi(Vector2 screen)
        {
            if (screen.x < 0f || screen.y < 0f || screen.x > Screen.width || screen.y > Screen.height) return true;
            return IsOverUi != null && IsOverUi(screen);
        }

        /// <summary>Elige dónde va el recuadro: junto al final del carril, dentro de la pantalla y sin tapar ningún panel.</summary>
        Vector2 ComputeBoxScreen(bool withError)
        {
            float k = BoxScale;
            Vector2 size = new Vector2(300f, withError ? 360f : 290f) * k;
            Vector3 e3 = cam.cam.WorldToScreenPoint(new Vector3(pE.x, pE.y, 0f));
            Vector2 e = new Vector2(e3.x, e3.y);
            Vector2[] offs =
            {
                new Vector2(size.x * 0.75f, 0f), new Vector2(-size.x * 0.75f, 0f),
                new Vector2(0f, -size.y * 0.75f), new Vector2(0f, size.y * 0.75f),
                new Vector2(size.x * 0.75f, -size.y * 0.6f), new Vector2(-size.x * 0.75f, -size.y * 0.6f),
                new Vector2(size.x * 0.75f, size.y * 0.6f), new Vector2(-size.x * 0.75f, size.y * 0.6f)
            };
            float m = 12f * k;
            foreach (var o in offs)
            {
                Vector2 c = e + o;
                c.x = Mathf.Clamp(c.x, size.x * 0.5f + m, Screen.width - size.x * 0.5f - m);
                c.y = Mathf.Clamp(c.y, size.y * 0.5f + m, Screen.height - size.y * 0.5f - m);
                bool clear = true;
                for (int ix = -1; ix <= 1 && clear; ix++)
                    for (int iy = -1; iy <= 1 && clear; iy++)
                        if (OverUi(c + new Vector2(ix * size.x * 0.5f, iy * size.y * 0.5f))) clear = false;
                if (clear) return c;
            }
            // Si todo está tapado (pantalla muy llena): en el centro, un poco abajo.
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.35f);
        }

        Vector2 BoxWorld(Vector2 offsetPx)
        {
            return cam.ScreenToWorld(boxScreen + offsetPx * BoxScale);
        }

        void LaneBoxParts(out Vector2 ok, out Vector2 no, out Vector2 rampDown, out Vector2 rampFlat, out Vector2 rampUp)
        {
            ok = BoxWorld(new Vector2(-62f, -48f));
            no = BoxWorld(new Vector2(62f, -48f));
            rampDown = BoxWorld(new Vector2(-92f, 95f));
            rampFlat = BoxWorld(new Vector2(0f, 95f));
            rampUp = BoxWorld(new Vector2(92f, 95f));
        }

        /// <summary>Toque sobre el recuadro: rampa, ✓ o ✗. Devuelve true si lo atendió.</summary>
        bool HitLaneBox(Vector2 p)
        {
            Vector2 ok, no, rd, rf, ru;
            LaneBoxParts(out ok, out no, out rd, out rf, out ru);
            float r = 46f * BoxScale * Wpp;
            if (Vector2.Distance(ok, p) < r) { ConfirmPending(); return true; }
            if (Vector2.Distance(no, p) < r) { CancelPending(); return true; }
            if (Vector2.Distance(rd, p) < r) { SetPendingRamp(-1); return true; }
            if (Vector2.Distance(rf, p) < r) { SetPendingRamp(0); return true; }
            if (Vector2.Distance(ru, p) < r) { SetPendingRamp(1); return true; }
            // Toque en el resto del recuadro: no hace nada (no debe empezar otro gesto debajo).
            Vector2 c = BoxWorld(Vector2.zero);
            Vector2 half = new Vector2(150f, boxHasError ? 180f : 145f) * BoxScale * Wpp;
            Vector2 cc = boxHasError ? BoxWorld(new Vector2(0f, -35f)) : c;
            return Mathf.Abs(p.x - cc.x) < half.x && Mathf.Abs(p.y - cc.y) < half.y;
        }

        void DrawLaneBox(bool valid, string problem, float heading)
        {
            boxHasError = !valid;
            boxScreen = ComputeBoxScreen(!valid);
            float k = BoxScale;
            float wpp = Wpp;
            Vector2 center = boxHasError ? BoxWorld(new Vector2(0f, -35f)) : BoxWorld(Vector2.zero);
            Vector2 size = new Vector2(300f, boxHasError ? 360f : 290f) * k * wpp;
            Spr(SpriteFactory.Square, new Color(0.07f, 0.07f, 0.08f, 0.94f), center, size, 64);

            Vector2 ok, no, rd, rf, ru;
            LaneBoxParts(out ok, out no, out rd, out rf, out ru);
            float d = 78f * k * wpp;
            Vector2[] rp = { rd, rf, ru };
            string[] icons = { "rampdown", "rampflat", "rampup" };
            for (int i = 0; i < 3; i++)
            {
                bool on = PendingRamp == i - 1;
                Spr(SpriteFactory.Circle, on ? new Color(0.91f, 0.47f, 0.13f, 1f) : new Color(0.33f, 0.33f, 0.35f, 1f), rp[i], Vector2.one * d, 65);
                Spr(IntersectionMaster.UI.IconKit.Get(icons[i]), Color.white, rp[i], Vector2.one * d * 0.7f, 66);
            }
            Vector2 ang = BoxWorld(new Vector2(0f, 28f));
            PreviewLabel(Mathf.RoundToInt(heading) + "°", ang, 44f * k, Color.white);
            Spr(SpriteFactory.Square, new Color(0.6f, 0.6f, 0.62f, 1f), BoxWorld(new Vector2(0f, 2f)), new Vector2(200f * k * wpp, 5f * k * wpp), 66);

            float bd = 86f * k * wpp;
            Spr(SpriteFactory.Circle, valid ? new Color(0.91f, 0.47f, 0.13f, 1f) : new Color(0.33f, 0.33f, 0.35f, 1f), ok, Vector2.one * bd, 65);
            Spr(IntersectionMaster.UI.IconKit.Get("check"), valid ? Color.white : new Color(1f, 1f, 1f, 0.45f), ok, Vector2.one * bd * 0.62f, 66);
            Spr(SpriteFactory.Circle, new Color(0.33f, 0.33f, 0.35f, 1f), no, Vector2.one * bd, 65);
            Spr(IntersectionMaster.UI.IconKit.Get("close"), Color.white, no, Vector2.one * bd * 0.55f, 66);
            if (!valid) PreviewLabel(problem, BoxWorld(new Vector2(0f, -132f)), 22f * k, new Color(1f, 0.25f, 0.2f, 1f));
        }
    }
}
