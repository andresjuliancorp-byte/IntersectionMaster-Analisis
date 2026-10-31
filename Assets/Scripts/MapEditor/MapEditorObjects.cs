using System;
using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Core;
using IntersectionMaster.Data;
using IntersectionMaster.Traffic;

namespace IntersectionMaster.MapEditing
{
    /// <summary>
    /// Objetos del mapa: decoración especial, puntos lógicos (parada, peaje, detector), botones de grupo,
    /// controlador de semáforos por nodo, restricciones por tipo de vehículo y la sesión Confirmar / Cancelar.
    /// </summary>
    public partial class MapEditorController
    {
        /// <summary>Tipo de objeto que coloca la herramienta Colocar.</summary>
        public DecorType Brush = DecorType.Tree;

        public int SelectedDecor = -1;
        public int SelectedPointId = -1;
        public int SelectedButtonId = -1;
        /// <summary>Botón de grupo que está vinculando semáforos (los toques en calles alternan vínculos).</summary>
        public int LinkingButtonId = -1;

        Vector2 grabOffset, shapeStart, shapeEnd;

        public DecorItem SelectedDecorItem { get { return Map != null && SelectedDecor >= 0 && SelectedDecor < Map.decor.Count ? Map.decor[SelectedDecor] : null; } }
        public PointItem SelectedPoint { get { return Map == null ? null : Map.Point(SelectedPointId); } }
        public GroupButton SelectedButton { get { return Map == null ? null : Map.Button(SelectedButtonId); } }

        /// <summary>Argumento de la herramienta activa para marcar su icono (Recta/Curva, tipo de objeto).</summary>
        public int ToolArg
        {
            get
            {
                if (Tool == EditorTool.Road) return NewRoadCurved ? 1 : 0;
                if (Tool == EditorTool.Place) return (int)Brush;
                return -1;
            }
        }

        public static string DecorName(DecorType t)
        {
            switch (t)
            {
                case DecorType.Building: return "Edificio";
                case DecorType.Tree: return "Árbol";
                case DecorType.Park: return "Parque";
                case DecorType.Lamp: return "Farola";
                case DecorType.ParkedCar: return "Coche aparcado";
                case DecorType.Fence: return "Valla";
                case DecorType.Hedge: return "Seto";
                case DecorType.Smoke: return "Humo";
                case DecorType.Trailer: return "Remolque";
                case DecorType.FloodLight: return "Luz de inundación";
                case DecorType.GlowLight: return "Luz";
                case DecorType.RailSign: return "Señal de ferrocarril";
                case DecorType.PhonePole: return "Poste de teléfono";
                case DecorType.RoadSign: return "Señal de tráfico";
                case DecorType.RoundStructure: return "Estructura redonda";
                case DecorType.BusShelter: return "Parada de autobús";
                case DecorType.Containers: return "Contenedores";
                case DecorType.PowerLine: return "Tendido eléctrico";
                case DecorType.Crane: return "Grúa de contenedores";
                case DecorType.Sticker: return "Pegatina";
                case DecorType.TextLabel: return "Texto";
                case DecorType.Lines: return "Líneas";
                case DecorType.Misc: return "Misceláneo";
                case DecorType.Boat: return "Barco";
                case DecorType.Material: return "Material";
                case DecorType.Parking: return "Aparcamiento";
                case DecorType.StopLine: return "Línea de detención";
                case DecorType.Sidewalk: return "Acera";
                case DecorType.Median: return "Mediana / separador";
                case DecorType.TrafficSign: return "Señal de tráfico";
                case DecorType.Zone: return "Zona";
                case DecorType.Trail: return "Camino";
                case DecorType.WallSticker: return "Pegatina de pared";
            }
            return t.ToString();
        }

        static string ToolHintExt(EditorTool t)
        {
            switch (t)
            {
                case EditorTool.Place: return "Toca o arrastra para colocar el objeto. Las líneas, el tendido eléctrico, los materiales y los aparcamientos se dibujan arrastrando de un punto a otro.";
                case EditorTool.Restrictions: return "Toca un carril para cambiar qué vehículos pueden circular (todos, sin pesados, ligeros, emergencias, bus y taxi). El color indica la restricción.";
                case EditorTool.Controller: return "Toca un cruce para ajustar su controlador de semáforos: manual o ciclo automático, y los tiempos.";
                case EditorTool.BusStop: return "Toca un carril: los autobuses que pasen por ese carril se detendrán unos segundos. Toca de nuevo para quitarla.";
                case EditorTool.Toll: return "Toca un carril para poner un peaje: todos los vehículos (salvo emergencias) se detienen un momento. Toca de nuevo para quitarlo.";
                case EditorTool.Detector: return "Toca un carril antes de un semáforo: si un vehículo espera sobre el detector, el semáforo pide verde.";
                case EditorTool.GroupButton: return "Toca un punto libre para crear un botón; tócalo para seleccionarlo y pulsa Vincular para elegir los semáforos que cambia a la vez.";
            }
            return "";
        }

        // ---------------- Flechas funcionales ----------------

        public int SelectedArrowId = -1;
        public LaneArrowItem SelectedArrow { get { return Map == null ? null : Map.Arrow(SelectedArrowId); } }

        /// <summary>Carril (de la red construida) al que pertenece una flecha.</summary>
        RoadSeg ArrowLane(LaneArrowItem a)
        {
            var net = mapRenderer != null ? mapRenderer.Net : null;
            if (net == null || a == null) return null;
            foreach (var nr in net.roads)
            {
                if (nr.data.id != a.road) continue;
                var lanes = a.forward ? nr.lanesAB : nr.lanesBA;
                return a.lane >= 0 && a.lane < lanes.Length ? lanes[a.lane] : null;
            }
            return null;
        }

        bool ArrowPose(LaneArrowItem a, out Vector2 pos, out Vector2 dir)
        {
            pos = Vector2.zero; dir = Vector2.right;
            var lane = ArrowLane(a);
            if (lane == null) return false;
            float sp = Mathf.Clamp01(a.t) * lane.Length;
            pos = lane.path.PointAt(sp);
            dir = lane.path.TangentAt(sp);
            return true;
        }

        /// <summary>Herramienta "Flecha funcional": toca un carril para crear una flecha (o seleccionar la que haya cerca).</summary>
        bool TapArrow(Vector2 p)
        {
            var net = mapRenderer.Net;
            if (net == null) return false;
            RoadSeg best = null; NetRoad bestRoad = null; float bestS = 0f;
            float bd = RoadNetworkManager.LaneWidth * 0.8f + Tol(10f);
            foreach (var nr in net.roads)
            {
                for (int dir = 0; dir < 2; dir++)
                {
                    var lanes = dir == 0 ? nr.lanesAB : nr.lanesBA;
                    foreach (var lane in lanes)
                    {
                        float d;
                        float sp = lane.path.Project(p, out d);
                        if (d < bd) { bd = d; best = lane; bestRoad = nr; bestS = sp; }
                    }
                }
            }
            if (best == null) { Status = "Toca un carril."; return false; }
            var road = Map.Road(bestRoad.data.id);
            if (road == null) return false;
            if (road.locked) { Status = "Calle bloqueada."; return false; }
            bool forward = System.Array.IndexOf(bestRoad.lanesAB, best) >= 0;
            float t = best.Length > 0.01f ? bestS / best.Length : 0.5f;

            // ¿Hay ya una flecha en ese carril cerca del toque? Entonces se selecciona.
            foreach (var ar in Map.arrows)
            {
                if (ar.road != road.id || ar.forward != forward || ar.lane != best.laneIndex) continue;
                if (Mathf.Abs(ar.t - t) * best.Length < 0.45f)
                {
                    SelectedArrowId = ar.id;
                    Status = "Flecha seleccionada: elige sus giros, posición y tamaño en el panel.";
                    return false;
                }
            }
            int moves = best.moves & 7;
            if (moves == 0) moves = 2;
            var a = Map.AddArrow(road.id, forward, best.laneIndex, t, moves);
            if (a == null) { Status = "Límite de flechas alcanzado."; return false; }
            SelectedArrowId = a.id;
            Status = "Flecha funcional creada al " + Mathf.RoundToInt(a.t * 100f) + "% del carril: " + MaskName(a.moves) + ". Ajusta sus giros en el panel.";
            return true;
        }

        public void ToggleArrowMove(int bit)
        {
            Edit(() =>
            {
                var a = SelectedArrow; if (a == null) return false;
                int m = a.moves ^ bit;
                if ((m & 7) == 0) { Status = "La flecha necesita al menos un movimiento."; return false; }
                a.moves = m & 7;
                Status = "Giros de la flecha: " + MaskName(a.moves) + ".";
                return true;
            }, true);
        }

        public void MoveArrow(float delta)
        {
            Edit(() => { var a = SelectedArrow; if (a == null) return false; float nt = Mathf.Clamp(Mathf.Round((a.t + delta) * 100f) / 100f, 0.05f, 0.95f); if (Mathf.Approximately(nt, a.t)) return false; a.t = nt; return true; }, true);
        }

        public void SizeArrow(float delta)
        {
            Edit(() => { var a = SelectedArrow; if (a == null) return false; a.size = Mathf.Clamp(Mathf.Round((a.size + delta) * 10f) / 10f, 0.5f, 2f); return true; }, true);
        }

        public void DuplicateArrow()
        {
            Edit(() =>
            {
                var a = SelectedArrow; if (a == null) return false;
                float nt = a.t > 0.5f ? a.t - 0.2f : a.t + 0.2f;
                var b = Map.AddArrow(a.road, a.forward, a.lane, nt, a.moves);
                if (b == null) return false;
                b.size = a.size;
                SelectedArrowId = b.id;
                Status = "Flecha duplicada.";
                return true;
            }, true);
        }

        public void DeleteArrow()
        {
            int id = SelectedArrowId;
            Edit(() => { int n = Map.arrows.RemoveAll(q => q.id == id); SelectedArrowId = -1; Status = "Flecha borrada."; return n > 0; }, true);
        }

        /// <summary>Copia las flechas de este carril a los demás carriles del mismo sentido.</summary>
        public void CopyArrowToSiblings()
        {
            Edit(() =>
            {
                var a = SelectedArrow; if (a == null) return false;
                var r = Map.Road(a.road); if (r == null) return false;
                int count = a.forward ? r.lanesAB : r.lanesBA;
                int added = 0;
                for (int i = 0; i < count; i++)
                {
                    if (i == a.lane) continue;
                    Map.arrows.RemoveAll(q => q.road == a.road && q.forward == a.forward && q.lane == i);
                    var b = Map.AddArrow(a.road, a.forward, i, a.t, a.moves);
                    if (b != null) { b.size = a.size; added++; }
                }
                Status = added > 0 ? "Flecha copiada a los demás carriles del mismo sentido." : "No hay otros carriles en ese sentido.";
                return added > 0;
            }, true);
        }

        // ---------------- Semáforos: modelo, tamaño, lado y fase ----------------

        public int SelectedSignRoad = -1, SelectedSignNode = -1;
        public SignItem SelectedLight { get { return Map == null || SelectedSignRoad < 0 ? null : Map.FindSign(SelectedSignRoad, SelectedSignNode, SignType.Light); } }

        public void CycleLightModel()
        {
            Edit(() => { var l = SelectedLight; if (l == null) return false; l.model = (l.model + 1) % SignItem.ModelNames.Length; Status = "Modelo: " + SignItem.ModelNames[l.model] + "."; return true; }, true);
        }

        public void AdjustLightSize(float d)
        {
            Edit(() => { var l = SelectedLight; if (l == null) return false; l.size = Mathf.Clamp(Mathf.Round((l.size + d) * 10f) / 10f, 0.6f, 1.8f); return true; }, true);
        }

        public void ToggleLightSide()
        {
            Edit(() => { var l = SelectedLight; if (l == null) return false; l.side = 1 - l.side; return true; }, true);
        }

        public void CycleLightPhase()
        {
            Edit(() => { var l = SelectedLight; if (l == null) return false; l.phase = (l.phase + 1) % 7; Status = l.phase == 0 ? "Semáforo independiente." : "Fase " + l.phase + ": cambiará junto con los demás semáforos de esa fase en este cruce."; return true; }, true);
        }

        public void DeleteSelectedLight()
        {
            int r = SelectedSignRoad, n = SelectedSignNode;
            Edit(() => { int c = Map.signs.RemoveAll(q => q.road == r && q.node == n && q.type == SignType.Light); SelectedSignRoad = SelectedSignNode = -1; Status = "Semáforo quitado."; return c > 0; }, true);
        }

        /// <summary>Copia modelo, tamaño, lado y fase del semáforo seleccionado a los demás semáforos del mismo cruce.</summary>
        public void CopyLightToNode()
        {
            Edit(() =>
            {
                var l = SelectedLight; if (l == null) return false;
                int c = 0;
                foreach (var q in Map.signs)
                    if (q != l && q.type == SignType.Light && q.node == l.node) { q.model = l.model; q.size = l.size; q.side = l.side; c++; }
                Status = c > 0 ? "Aspecto copiado a los demás semáforos del cruce." : "No hay otros semáforos en este cruce.";
                return c > 0;
            }, true);
        }

        // ---------------- Selección de objetos ----------------

        void ClearObjSel()
        {
            SelectedDecor = SelectedPointId = SelectedButtonId = SelectedArrowId = -1;
            SelectedSignRoad = SelectedSignNode = -1;
        }

        void ClampObjSel()
        {
            if (SelectedDecor >= Map.decor.Count) SelectedDecor = -1;
            if (Map.Point(SelectedPointId) == null) SelectedPointId = -1;
            if (Map.Button(SelectedButtonId) == null) { SelectedButtonId = -1; LinkingButtonId = -1; }
            if (Map.Arrow(SelectedArrowId) == null) SelectedArrowId = -1;
            if (SelectedLight == null) SelectedSignRoad = SelectedSignNode = -1;
        }

        public bool HasAnySelection
        {
            get { return SelectedNodeId >= 0 || SelectedRoadId >= 0 || SelectedDecor >= 0 || SelectedPointId >= 0 || SelectedButtonId >= 0 || SelectedArrowId >= 0 || SelectedSignRoad >= 0; }
        }

        // ---- Círculo de giro de la señal de tráfico seleccionada (como el original) ----

        const float SignRingR = 0.55f;

        Vector2 SignHandle(DecorItem d) { return d.Pos + Geo.FromAngle(d.rot) * SignRingR; }

        bool lampDrag;   // se arrastra el tirador del alcance de la farola (no el giro de una señal)

        Vector2 LampHandle(DecorItem d) { return d.Pos + Vector2.right * MapRenderer.LampReach(d); }

        public void AdjustLampLight(float delta)
        {
            Edit(() => { var d = SelectedDecorItem; if (d == null || d.type != DecorType.Lamp) return false; d.light = Mathf.Clamp(Mathf.Round((d.light + delta) * 10f) / 10f, 0.3f, 3f); return true; }, true);
        }

        bool BeginSignRotate(Vector2 p)
        {
            lampDrag = false;
            if (Tool == EditorTool.Edit && BeginVertexDrag(p)) return true;
            var lamp = SelectedDecorItem;
            if (lamp != null && lamp.type == DecorType.Lamp && !lamp.locked && Tool == EditorTool.Edit
                && Vector2.Distance(LampHandle(lamp), p) < Mathf.Max(Tol(34f), 0.18f))
            {
                lampDrag = true;
                gesture = Gesture.RotateObj;
                return true;
            }
            var d = SelectedDecorItem;
            if (d == null || d.type != DecorType.TrafficSign || d.locked) return false;
            if (Vector2.Distance(SignHandle(d), p) > Mathf.Max(Tol(34f), 0.18f)) return false;
            gesture = Gesture.RotateObj;
            return true;
        }

        void UpdateSignRotate(Vector2 p)
        {
            var d = SelectedDecorItem;
            if (d == null) return;
            if (lampDrag)
            {
                // Alcance de la luz: el tirador del círculo, como el original.
                float k = Mathf.Clamp(d.size, 0.4f, 3f);
                float lv = Mathf.Clamp(Mathf.Round((p - d.Pos).magnitude / (MapRenderer.LampReachBase * k) * 20f) / 20f, 0.3f, 3f);
                if (Mathf.Approximately(lv, d.light)) return;
                d.light = lv;
                gestureChanged = true; Dirty = true; Rebuild(true);
                return;
            }
            Vector2 v = p - d.Pos;
            if (v.sqrMagnitude < 0.0025f) return;
            float a = Mathf.Round(Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg / 5f) * 5f;
            if (a == 0f) a = 360f;   // 0 significa «automático» en otros objetos; para la señal da igual
            if (Mathf.Approximately(a, d.rot)) return;
            d.rot = a;
            gestureChanged = true; Dirty = true; Rebuild(true);
        }

        void DrawSignRing(float wpp)
        {
            if (Tool != EditorTool.Edit) return;
            var d = SelectedDecorItem;
            if (d != null && d.type == DecorType.Lamp)
            {
                // Círculo del alcance de la luz con su tirador (como el original).
                float R = MapRenderer.LampReach(d);
                Spr(SpriteFactory.Circle, new Color(1f, 0.9f, 0.5f, 0.12f), d.Pos, Vector2.one * R * 2f, 55);
                Spr(SpriteFactory.Ring, new Color(0.25f, 0.27f, 0.3f, 0.9f), d.Pos, Vector2.one * R * 2f, 56);
                Vector2 lh = LampHandle(d);
                Segment(d.Pos, lh, 4f * wpp, new Color(0.15f, 0.45f, 1f, 1f), 56);
                Spr(SpriteFactory.Circle, new Color(0.15f, 0.45f, 1f, 1f), lh, Vector2.one * 20f * wpp, 57);
                Spr(SpriteFactory.Square, new Color(0.15f, 0.45f, 1f, 1f), d.Pos, Vector2.one * 16f * wpp, 57);   // centro: arrastrar para mover
                return;
            }
            if (d == null || d.type != DecorType.TrafficSign) return;
            var ring = new Color(0.25f, 0.27f, 0.3f, 0.9f);
            Spr(SpriteFactory.Ring, ring, d.Pos, Vector2.one * SignRingR * 2f, 56);
            Vector2 h = SignHandle(d);
            Segment(d.Pos, h, 4f * wpp, new Color(0.15f, 0.45f, 1f, 1f), 56);
            Spr(SpriteFactory.Circle, new Color(0.15f, 0.45f, 1f, 1f), h, Vector2.one * 20f * wpp, 57);
            Spr(SpriteFactory.Circle, new Color(0.15f, 0.45f, 1f, 1f), d.Pos, Vector2.one * 10f * wpp, 57);
        }

        bool BeginObjectEdit(Vector2 p)
        {
            var pt = Map.PointNear(p, Tol(26f));
            if (pt != null)
            {
                SelectedPointId = pt.id;
                grabOffset = Vector2.zero;
                gesture = Gesture.MoveObj;
                Status = "Punto seleccionado. Arrástralo a lo largo del carril.";
                return true;
            }
            var bt = Map.ButtonNear(p, Tol(28f));
            if (bt != null)
            {
                SelectedButtonId = bt.id;
                grabOffset = bt.Pos - p;
                gesture = Gesture.MoveObj;
                Status = "Botón seleccionado. Arrástralo para moverlo.";
                return true;
            }
            int di = Map.DecorNear(p, Tol(16f));
            // Material bajo carril: si debajo del dedo hay un carril, gana el carril.
            if (di >= 0 && Map.decor[di].type == DecorType.Material && !Map.decor[di].overLane && Map.DistanceToRoads(p) < RoadNetworkManagerLane() * 0.5f) di = -1;
            if (di >= 0)
            {
                SelectedDecor = di;
                if (QuadNotCenter(Map.decor[di], p)) { gesture = Gesture.None; Status = "Material seleccionado. Arrastra el punto del centro para moverlo, o una esquina para cambiar su forma."; return true; }
                if (Map.decor[di].locked) { Status = DecorName(Map.decor[di].type) + " bloqueado: desbloquéalo para moverlo."; gesture = Gesture.None; return true; }
                grabOffset = Map.decor[di].Pos - p;
                gesture = Gesture.MoveObj;
                Status = DecorName(Map.decor[di].type) + " seleccionado. Arrástralo para moverlo; ajusta sus propiedades en el panel.";
                return true;
            }
            return false;
        }

        /// <summary>Se movió un objeto sin volver a dibujar el mapa: se dibuja al soltarlo.</summary>
        bool redrawOnRelease;

        void UpdateMoveObj(Vector2 p)
        {
            bool moved = false;
            if (SelectedPointId >= 0)
            {
                var pt = Map.Point(SelectedPointId);
                var r = pt == null ? null : Map.Road(pt.road);
                if (r == null) return;
                float dist;
                float t = Mathf.Clamp(Map.NearestParam(r, p, out dist), 0.05f, 0.95f);
                if (!Mathf.Approximately(t, pt.t)) { pt.t = t; moved = true; }
            }
            else if (SelectedButtonId >= 0)
            {
                var b = Map.Button(SelectedButtonId);
                if (b == null) return;
                Vector2 np = Snap(p + grabOffset);
                if (np != b.Pos) { b.x = np.x; b.y = np.y; moved = true; }
            }
            else if (SelectedDecor >= 0 && SelectedDecor < Map.decor.Count)
            {
                var d = Map.decor[SelectedDecor];
                Vector2 np = Snap(p + grabOffset);
                Vector2 delta = np - d.Pos;
                if (delta.sqrMagnitude > 0.0001f)
                {
                    Vector2 ne = d.IsLine() ? d.End + delta : d.End;
                    if (Map.InBounds(np) && (!d.IsLine() || Map.InBounds(ne)))
                    {
                        d.x = np.x; d.y = np.y;
                        if (d.IsLine()) { d.x2 = ne.x; d.y2 = ne.y; }
                        // Como el original, fluido: solo se desplaza lo dibujado del objeto; el mapa se vuelve a dibujar al soltarlo.
                        bool quick = mapRenderer != null && mapRenderer.MoveDecorParts(d, delta);
                        // Los demás miembros de su grupo se desplazan igual (salvo los bloqueados).
                        if (d.group != 0)
                            foreach (var o in Map.decor)
                            {
                                if (o == d || o.group != d.group || o.locked) continue;
                                o.x += delta.x; o.y += delta.y;
                                if (o.IsLine()) { o.x2 += delta.x; o.y2 += delta.y; }
                                quick &= mapRenderer != null && mapRenderer.MoveDecorParts(o, delta);
                            }
                        gestureChanged = true; Dirty = true;
                        if (quick) redrawOnRelease = true; else Rebuild(true);
                    }
                }
                return;
            }
            if (moved) { gestureChanged = true; Dirty = true; Rebuild(true); }
        }

        // ---------------- Colocar objetos ----------------

        float BrushStep()
        {
            switch (Brush)
            {
                case DecorType.Park: return 1.6f;
                case DecorType.Building: case DecorType.Crane: case DecorType.RoundStructure: case DecorType.Containers: return 1.4f;
                case DecorType.Lamp: case DecorType.ParkedCar: case DecorType.Sticker: case DecorType.Misc: case DecorType.RoadSign: case DecorType.TrafficSign: return 0.7f;
                case DecorType.TextLabel: case DecorType.Boat: case DecorType.Trailer: return 1.2f;
            }
            return 0.9f;
        }

        static float MinRoadDistance(DecorType t)
        {
            switch (t)
            {
                case DecorType.Building: return 1.3f;
                case DecorType.Park: case DecorType.RoundStructure: case DecorType.Crane: return 1.0f;
                case DecorType.Containers: return 0.9f;
                case DecorType.Fence: case DecorType.Hedge: return 0.8f;
                case DecorType.Tree: case DecorType.Trailer: return 0.7f;
                case DecorType.Lamp: case DecorType.FloodLight: case DecorType.BusShelter: return 0.6f;
                case DecorType.ParkedCar: return 0.55f;
                case DecorType.Smoke: case DecorType.RailSign: case DecorType.PhonePole: case DecorType.RoadSign: return 0.5f;
                case DecorType.Misc: return 0.3f;
            }
            return 0f;     // pegatinas, texto, líneas, materiales, aparcamientos, luces y barcos pueden ir sobre la calle
        }

        DecorItem NewDecor(DecorType type, Vector2 p)
        {
            DecorItem tpl;
            if (Map.IsLaneMap && type != DecorType.TrafficSign && decorTemplates.TryGetValue(type, out tpl))
            {
                var c = tpl.Copy(Vector2.zero);
                c.x = p.x; c.y = p.y; c.locked = false; c.group = 0; c.text = tpl.text;
                if (DecorItem.IsLine(type)) { c.x2 = p.x; c.y2 = p.y; }
                c.pts = new List<Vector2>();
                if (DecorItem.IsPoly(type)) c.pts.Add(Vector2.zero);
                return c;
            }
            var d = new DecorItem { x = p.x, y = p.y, type = type, variant = Mathf.Abs(Mathf.RoundToInt(p.x * 31f + p.y * 17f)) % 4 };
            switch (type)
            {
                case DecorType.Sticker: case DecorType.Lines: case DecorType.Material: case DecorType.Misc: case DecorType.Boat: case DecorType.TextLabel:
                case DecorType.StopLine: case DecorType.Sidewalk: case DecorType.Median:
                case DecorType.Zone: case DecorType.Trail: case DecorType.WallSticker:
                    d.variant = 0; break;
            }
            if (type == DecorType.TextLabel) d.text = "Texto";
            if (DecorItem.IsPoly(type)) d.pts.Add(Vector2.zero);
            if (type == DecorType.PhonePole) d.carFlags = 1;   // con cables al poste más cercano
            if (Map.IsLaneMap && (type == DecorType.Tree || type == DecorType.Fence || type == DecorType.Parking || type == DecorType.ParkedCar)) d.variant = 0;
            if (Map.IsLaneMap && type == DecorType.Building) d.color = Mathf.Abs(Mathf.RoundToInt(p.x * 13f + p.y * 7f)) % MapRenderer.WallColorNames.Length;
                        if (type == DecorType.TrafficSign)
            {
                d.variant = SignCatalog.Clamp(NewSignFace);
                d.color = 0;
                d.rot = 90f;   // derecha y con el poste hacia abajo; se gira con el círculo
            }
            if (type == DecorType.Sticker && Map.roads.Count > 0) d.rot = Mathf.Round(Map.RoadAngleNear(p) / 15f) * 15f;
            return d;
        }

        // ---------------- Zonas y caminos de muchos puntos ----------------

        /// <summary>Se están añadiendo puntos a la zona o camino seleccionado (cada toque pone uno).</summary>
        public bool PolyAdding;
        int dragVertex = -1;

        bool PlacePolyPoint(Vector2 sp)
        {
            var d = SelectedDecorItem;
            if (PolyAdding && d != null && d.type == Brush && d.IsPoly())
            {
                // Tocar el primer punto cierra la zona.
                if (d.type == DecorType.Zone && d.pts.Count >= 3 && Vector2.Distance(sp, d.PolyPoint(0)) < Mathf.Max(Tol(26f), 0.2f))
                {
                    PolyAdding = false;
                    Status = "Zona cerrada. Arrastra los puntos para ajustarla.";
                    return true;
                }
                if (d.pts.Count >= 64) { Status = "Máximo 64 puntos."; return false; }
                if (Vector2.Distance(sp, d.PolyPoint(d.pts.Count - 1)) < 0.1f) return false;
                d.pts.Add(sp - d.Pos);
                Status = d.pts.Count + " puntos. Toca para añadir más" + (d.type == DecorType.Zone ? ", toca el primero para cerrar" : "") + " o pulsa «Listo».";
                return true;
            }
            if (!Map.PlaceDecorItem(NewDecor(Brush, sp))) { Status = "Límite de objetos alcanzado."; return false; }
            SelectedDecor = Map.decor.Count - 1;
            PolyAdding = true;
            Status = DecorName(Brush) + ": toca para poner el siguiente punto.";
            return true;
        }

        public void FinishPoly() { PolyAdding = false; Status = "Listo."; }

        public void RemoveLastPolyPoint()
        {
            Edit(() => { var d = SelectedDecorItem; if (d == null || !d.IsPoly() || d.pts.Count <= 1) return false; d.pts.RemoveAt(d.pts.Count - 1); return true; }, true);
        }

        public void ContinuePoly() { var d = SelectedDecorItem; if (d != null && d.IsPoly()) { PolyAdding = true; Brush = d.type; if (Tool != EditorTool.Place) SetTool(EditorTool.Place); Status = "Toca para añadir puntos."; } }


        /// <summary>Material con esquinas tocado lejos de su punto central: se selecciona pero no se mueve (como el original).</summary>
        bool QuadNotCenter(DecorItem d, Vector2 p)
        {
            return d.type == DecorType.Material && d.pts != null && d.pts.Count >= 3 && Vector2.Distance(d.Pos, p) > Mathf.Max(Tol(26f), 0.16f);
        }

        /// <summary>Arrastrar un punto de la zona o camino seleccionado.</summary>
        bool BeginVertexDrag(Vector2 p)
        {
            var d = SelectedDecorItem;
            bool quad = d != null && d.type == DecorType.Material && d.pts != null && d.pts.Count >= 3;   // esquinas del material
            if (d == null || !(d.IsPoly() || quad) || d.locked || d.pts == null) return false;
            float best = Mathf.Max(Tol(26f), 0.16f);
            int hit = -1;
            for (int i = 0; i < d.pts.Count; i++)
            {
                float dist = Vector2.Distance(d.PolyPoint(i), p);
                if (dist < best) { best = dist; hit = i; }
            }
            if (hit < 0) return false;
            // Mientras se dibuja, tocar el primer punto de una zona la cierra (no se arrastra).
            if (PolyAdding && hit == 0 && d.type == DecorType.Zone && d.pts.Count >= 3) return false;
            dragVertex = hit;
            gesture = Gesture.MoveVertex;
            return true;
        }

        void UpdateVertexDrag(Vector2 p)
        {
            var d = SelectedDecorItem;
            if (d == null || dragVertex < 0 || dragVertex >= d.pts.Count) return;
            Vector2 np = Snap(p);
            if (!Map.InBounds(np)) return;
            Vector2 rel = np - d.Pos;
            if (rel == d.pts[dragVertex]) return;
            d.pts[dragVertex] = rel;
            if (d.type == DecorType.Material)
            {
                float hx = 0.1f, hy = 0.1f;
                foreach (var q in d.pts) { hx = Mathf.Max(hx, Mathf.Abs(q.x)); hy = Mathf.Max(hy, Mathf.Abs(q.y)); }
                d.x2 = hx / Mathf.Max(0.01f, d.size); d.y2 = hy / Mathf.Max(0.01f, d.size);
            }
            gestureChanged = true; Dirty = true; Rebuild(true);
        }

        bool PlaceBrushAt(Vector2 p)
        {
            Vector2 sp = Snap(p);
            if (DecorItem.IsPoly(Brush)) return PlacePolyPoint(sp);
            float min = MinRoadDistance(Brush);
            if (Map.IsLaneMap)
            {
                // Como el original: los objetos se ponen donde quieras (farolas en la acera, junto a la calle…); solo los
                // grandes no van encima del asfalto.
                if (min >= 0.8f && Map.DistanceToVehicleLanes(sp) < 0.05f) { Status = "No se puede poner encima de la calle."; return false; }
            }
            else if (min > 0f && Map.DistanceToRoads(sp) < min) { Status = "Demasiado cerca de un carril."; return false; }
            for (int i = 0; i < Map.decor.Count; i++)
                if (Map.decor[i].type == Brush && Vector2.Distance(Map.decor[i].Pos, sp) < 0.3f) return false;
            if (!Map.PlaceDecorItem(NewDecor(Brush, sp))) { Status = "Límite de objetos alcanzado."; return false; }
            if (Map.IsLaneMap) SelectedDecor = Map.decor.Count - 1;   // queda seleccionado: sus propiedades se ven en el panel
            Status = DecorName(Brush) + " colocado. Selecciónalo con «Seleccionar» para ajustar su tamaño, giro y variante.";
            return true;
        }

        void BeginPlace(Vector2 p)
        {
            if (BeginVertexDrag(p)) return;
            var cur = SelectedDecorItem;
            if (PolyAdding && (!DecorItem.IsPoly(Brush) || cur == null || cur.type != Brush)) PolyAdding = false;
            if (PolyAdding) { gesture = Gesture.PlaceTap; return; }
            if (DecorItem.IsLine(Brush) || DecorItem.IsArea(Brush))
            {
                gesture = Gesture.Shape;
                shapeStart = shapeEnd = Snap(p);
                Status = DecorItem.IsArea(Brush) ? "Arrastra para dibujar el área y suelta." : "Arrastra hasta el punto final y suelta.";
            }
            else if (Map.IsLaneMap)
            {
                // Como el original: tocar un objeto ya puesto lo selecciona y se arrastra para moverlo; tocar en vacío lo
                // pone al soltar (si el dedo se arrastra, se mueve el mapa en vez de poner nada).
                int si = Map.DecorNear(p, Tol(24f));
                if (si >= 0)
                {
                    SelectedRoadId = SelectedNodeId = -1;
                    SelectedDecor = si;
                    if (QuadNotCenter(Map.decor[si], p)) { gesture = Gesture.None; return; }
                    if (Map.decor[si].locked) { Status = "Objeto bloqueado: desbloquéalo para moverlo."; gesture = Gesture.None; return; }
                    grabOffset = Map.decor[si].Pos - p;
                    gesture = Gesture.MoveObj;
                    return;
                }
                gesture = Gesture.PlaceTap;
            }
            else
            {
                gesture = Gesture.Stroke;
                lastStrokePos = new Vector2(-999f, -999f);
                ApplyStroke(p, true);
            }
        }

        void UpdateShape(Vector2 p) { shapeEnd = Snap(p); }

        void EndShape(Vector2 p)
        {
            shapeEnd = Snap(p);
            Vector2 a = shapeStart, b = shapeEnd;
            DecorItem d;
            if (DecorItem.IsLine(Brush))
            {
                if (Vector2.Distance(a, b) < 0.4f) { Status = "Arrastra más lejos para dibujar la línea."; return; }
                d = NewDecor(Brush, a);
                d.x2 = b.x; d.y2 = b.y;
            }
            else
            {
                Vector2 half = new Vector2(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y)) * 0.5f;
                if (half.x < 0.3f || half.y < 0.3f) { Status = "Arrastra en diagonal para dibujar un área."; return; }
                d = NewDecor(Brush, (a + b) * 0.5f);
                d.x2 = half.x; d.y2 = half.y;
            }
            if (!Map.PlaceDecorItem(d)) { Status = "Límite de objetos alcanzado."; return; }
            Commit(gestureSnapshot);
            Dirty = true;
            Rebuild(true);
            Status = DecorName(Brush) + " creado.";
        }

        // ---------------- Borrar objetos ----------------

        bool EraseObjectsAt(Vector2 p)
        {
            var pt = Map.PointNear(p, Tol(18f));
            if (pt != null) { Map.points.Remove(pt); Status = "Punto borrado."; return true; }
            var bt = Map.ButtonNear(p, Tol(22f));
            if (bt != null) { Map.buttons.Remove(bt); SelectedButtonId = -1; Status = "Botón borrado."; return true; }
            return false;
        }

        bool DeleteSelectedObject()
        {
            if (SelectedDecor >= 0 && SelectedDecor < Map.decor.Count)
            {
                int i = SelectedDecor;
                int grp = Map.decor[i].group;
                Edit(() =>
                {
                    if (grp != 0) { int n = Map.decor.RemoveAll(o => o.group == grp && !o.locked); Status = n + " objetos del grupo borrados."; }
                    else { Map.decor.RemoveAt(i); Status = "Objeto borrado."; }
                    SelectedDecor = -1;
                    return true;
                }, true);
                return true;
            }
            if (SelectedPointId >= 0)
            {
                int id = SelectedPointId;
                Edit(() => { Map.points.RemoveAll(q => q.id == id); SelectedPointId = -1; Status = "Punto borrado."; return true; }, true);
                return true;
            }
            if (SelectedButtonId >= 0)
            {
                int id = SelectedButtonId;
                Edit(() => { Map.buttons.RemoveAll(q => q.id == id); SelectedButtonId = -1; LinkingButtonId = -1; Status = "Botón borrado."; return true; }, true);
                return true;
            }
            return false;
        }

        // ---------------- Propiedades de objetos ----------------

        public void AdjustDecorSize(float delta)
        {
            Edit(() => { var d = SelectedDecorItem; if (d == null) return false; d.size = Mathf.Clamp(Mathf.Round((d.size + delta) * 10f) / 10f, 0.4f, 3f); return true; }, true);
        }

        public void RotateDecor(float deg)
        {
            Edit(() => { var d = SelectedDecorItem; if (d == null) return false; d.rot = Mathf.Repeat(d.rot + deg, 360f); return true; }, true);
        }

        public void ResetDecorRotation()
        {
            Edit(() => { var d = SelectedDecorItem; if (d == null) return false; d.rot = 0f; return true; }, true);
        }

        public static int VariantCount(DecorType t)
        {
            switch (t)
            {
                case DecorType.Material: return 6;
                case DecorType.Sticker: return 6 + MapRenderer.Sticker2Names.Length;   // 6 antiguas + las del original
                case DecorType.Fence: return MapRenderer.FenceNames.Length;
                case DecorType.ParkedCar: return MapRenderer.CarModelNames.Length;
                case DecorType.Parking: return MapRenderer.ParkingNames.Length;
                case DecorType.Misc: return MapRenderer.MiscNames.Length;
                case DecorType.RoadSign: return MapRenderer.SignFaces.Length;
                case DecorType.PowerLine: return MapRenderer.PowerLineNames.Length;
                case DecorType.Containers: return 6;
                case DecorType.Lines: return MapRenderer.LineStyles.Length;
                case DecorType.TrafficSign: return SignCatalog.Count;
                case DecorType.Lamp: return 2;   // 0 un brazo, 1 doble sentido
                case DecorType.Median: return 3;
                case DecorType.Sidewalk: return 2;
                case DecorType.StopLine: return 2;
                case DecorType.Zone: return MapRenderer.ZoneNames.Length;
                case DecorType.Trail: return MapRenderer.TrailNames.Length;
                case DecorType.WallSticker: return MapRenderer.WallStickerNames.Length;
            }
            return 4;
        }

        /// <summary>Señal que se pondrá con Decoración → Señal de tráfico.</summary>
        public int NewSignFace;

        public void SetDecorVariant(int v)
        {
            Edit(() => { var d = SelectedDecorItem; if (d == null) return false; d.variant = Mathf.Clamp(v, 0, VariantCount(d.type) - 1); return true; }, true);
        }

        /// <summary>Señal de tráfico con poste o sin él (pegada a una pared o un pórtico).</summary>
        public void ToggleSignPole()
        {
            Edit(() => { var d = SelectedDecorItem; if (d == null || d.type != DecorType.TrafficSign) return false; d.color = d.color == 0 ? 1 : 0; return true; }, true);
        }

        /// <summary>
        /// Cambia una propiedad del objeto seleccionado (con deshacer) y la recuerda para los siguientes de su tipo,
        /// como el original: lo que eliges se aplica también a los que pones después.
        /// </summary>
        public void ApplyDecor(System.Action<DecorItem> change)
        {
            Edit(() => { var d = SelectedDecorItem; if (d == null) return false; change(d); RememberDecor(d); return true; }, true);
        }

        readonly Dictionary<DecorType, DecorItem> decorTemplates = new Dictionary<DecorType, DecorItem>();

        void RememberDecor(DecorItem d) { if (d != null) decorTemplates[d.type] = d.Copy(Vector2.zero); }

                public void CycleDecorVariant()
        {
            Edit(() => { var d = SelectedDecorItem; if (d == null) return false; d.variant = (d.variant + 1) % VariantCount(d.type); return true; }, true);
        }

        public void CycleDecorColor()
        {
            Edit(() => { var d = SelectedDecorItem; if (d == null) return false; d.color = (d.color + 1) % MapRenderer.ObjColors.Length; return true; }, true);
        }

        public void SetDecorText(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) text = "Texto";
            Edit(() => { var d = SelectedDecorItem; if (d == null || d.text == text) return false; d.text = text; return true; }, true);
        }

        public void DuplicateDecor()
        {
            Edit(() =>
            {
                var d = SelectedDecorItem; if (d == null) return false;
                var off = new Vector2(0.6f, -0.6f);
                if (d.group != 0)
                {
                    // Duplica el grupo entero con un número de grupo nuevo.
                    int g = 1; foreach (var o in Map.decor) if (o.group >= g) g = o.group + 1;
                    var members = Map.decor.FindAll(o => o.group == d.group);
                    int first = Map.decor.Count;
                    foreach (var o in members) { var oc = o.Copy(off); oc.group = g; oc.locked = false; Map.PlaceDecorItem(oc); }
                    if (Map.decor.Count == first) return false;
                    SelectedDecor = first;
                    Status = "Grupo duplicado.";
                    return true;
                }
                var c = d.Copy(off);
                c.locked = false;
                if (!Map.PlaceDecorItem(c)) return false;
                SelectedDecor = Map.decor.Count - 1;
                Status = "Objeto duplicado.";
                return true;
            }, true);
        }

        public void AdjustPointSeconds(float delta)
        {
            Edit(() => { var q = SelectedPoint; if (q == null) return false; q.seconds = Mathf.Clamp(Mathf.Round((q.seconds + delta) * 2f) / 2f, 0f, 20f); return true; }, false);
        }

        public void CyclePointDir()
        {
            Edit(() => { var q = SelectedPoint; if (q == null) return false; q.dir = (q.dir + 1) % 3; return true; }, true);
        }

        public static string[] PointDirNames = { "Sentido A→B", "Sentido B→A", "Ambos sentidos" };

        public void ToggleLinking()
        {
            if (SelectedButtonId < 0) return;
            LinkingButtonId = LinkingButtonId == SelectedButtonId ? -1 : SelectedButtonId;
            Status = LinkingButtonId >= 0 ? "Toca el extremo de las carriles cuyos semáforos cambia este botón (toca de nuevo para quitar)." : "Vinculación terminada.";
        }

        public void ClearButtonLinks()
        {
            Edit(() => { var b = SelectedButton; if (b == null || b.links.Count == 0) return false; b.links.Clear(); return true; }, true);
        }

        // ---------------- Controlador de semáforos (por nodo) ----------------

        public static readonly string[] CtrlNames = { "Como el mapa", "Manual", "Ciclo automático" };

        public void CycleNodeCtrl()
        {
            Edit(() => { var n = SelectedNode; if (n == null) return false; n.ctrl = (n.ctrl + 1) % 3; return true; }, false);
        }

        public void AdjustNodeGreen(float delta)
        {
            Edit(() =>
            {
                var n = SelectedNode; if (n == null) return false;
                float g = n.green <= 0.1f ? Map.traffic.greenSeconds : n.green;
                n.green = Mathf.Clamp(g + delta, 3f, 40f);
                return true;
            }, false);
        }

        /// <summary>Controlador: «Espera» con todo en rojo entre fases.</summary>
        public void AdjustNodeAllRed(float delta)
        {
            Edit(() =>
            {
                var n = SelectedNode; if (n == null) return false;
                float v = n.allRed <= 0.05f ? 0.8f : n.allRed;
                n.allRed = Mathf.Clamp(Mathf.Round((v + delta) * 10f) / 10f, 0.1f, 6f);
                return true;
            }, false);
        }

        /// <summary>Controlador: «Sensor activado» (salta las fases sin vehículos).</summary>
        public void ToggleNodeSensor()
        {
            Edit(() => { var n = SelectedNode; if (n == null) return false; n.sensor = !n.sensor; return true; }, false);
        }

        public void AdjustNodeYellow(float delta)
        {
            Edit(() =>
            {
                var n = SelectedNode; if (n == null) return false;
                float y = n.yellow <= 0.1f ? 1.4f : n.yellow;
                n.yellow = Mathf.Clamp(Mathf.Round((y + delta) * 10f) / 10f, 0.5f, 5f);
                return true;
            }, false);
        }

        public void ResetNodeController()
        {
            Edit(() => { var n = SelectedNode; if (n == null) return false; n.ctrl = 0; n.green = 0f; n.yellow = 0f; return true; }, false);
        }

        void TapController(Vector2 p)
        {
            var n = Map.NodeNear(p, Tol(34f));
            if (n == null || (Map.Degree(n.id) < 3 && !n.roundabout))
            {
                SelectedNodeId = -1;
                Status = "Toca un cruce (nodo con 3 o más carriles).";
                return;
            }
            SelectedNodeId = n.id;
            SelectedRoadId = -1;
            Status = NodeHasLights(n)
                ? "Controlador del cruce seleccionado."
                : "Este cruce no tiene semáforos: ponlos con la herramienta Semáforo para que el controlador tenga efecto.";
        }

        // ---------------- Toques: restricciones, puntos, botones ----------------

        bool TapRestrictions(Vector2 p)
        {
            float t, dist;
            var r = FindRoad(p, Tol(16f), out t, out dist);
            if (r == null) { Status = "Toca un carril."; return false; }
            r.NextAllowPreset();
            Status = "Circulación permitida: " + RoadData.AllowLabels[r.AllowPreset] + ".";
            return true;
        }

        bool TapPoint(Vector2 p, PointKind kind)
        {
            float t, dist;
            var r = FindRoad(p, Tol(16f), out t, out dist);
            if (r == null) { Status = "Toca un carril."; return false; }
            int dir = 2;
            if (kind != PointKind.Toll)
            {
                Polyline pl = Map.CenterOf(r);
                float s = pl.Project(p, out dist);
                bool forward = Geo.Cross(pl.TangentAt(s), p - pl.PointAt(s)) <= 0f;
                dir = forward ? 0 : 1;
                if ((dir == 0 && r.lanesAB == 0) || (dir == 1 && r.lanesBA == 0)) dir = dir == 0 ? 1 : 0;
            }
            bool ok = Map.TogglePoint(r.id, t, kind, dir);
            if (!ok) { Status = "No se pudo colocar aquí."; return false; }
            Status = (kind == PointKind.BusStop ? "Parada de autobús" : kind == PointKind.Toll ? "Peaje" : "Detector") + " cambiado.";
            return true;
        }

        bool TapGroupButton(Vector2 p)
        {
            if (LinkingButtonId >= 0)
            {
                var b = Map.Button(LinkingButtonId);
                float t, dist;
                var r = FindRoad(p, Tol(18f), out t, out dist);
                if (b == null || r == null) { Status = "Toca el extremo de un carril para vincular su semáforo."; return false; }
                int node = t < 0.5f ? r.a : r.b;
                int idx = b.links.FindIndex(l => l.road == r.id && l.node == node);
                if (idx >= 0) { b.links.RemoveAt(idx); Status = "Vínculo quitado."; }
                else
                {
                    b.links.Add(new ButtonLink { road = r.id, node = node });
                    bool lit = false;
                    foreach (var s in Map.signs) if (s.road == r.id && s.node == node && s.type == SignType.Light) lit = true;
                    Status = lit ? "Semáforo vinculado (" + b.links.Count + ")." : "Vinculado, pero ese extremo no tiene semáforo.";
                }
                return true;
            }
            var hit = Map.ButtonNear(p, Tol(30f));
            if (hit != null)
            {
                SelectedButtonId = hit.id;
                Status = "Botón seleccionado: pulsa Vincular en el panel para elegir semáforos.";
                return false;
            }
            var nb = Map.AddButton(Snap(p));
            if (nb == null) { Status = "Límite de botones alcanzado."; return false; }
            SelectedButtonId = nb.id;
            Status = "Botón creado. Pulsa Vincular y toca los semáforos que debe cambiar.";
            return true;
        }

        // ---------------- Sesión Confirmar / Cancelar ----------------

        string sessionKey = "", sessionSnap;
        int sessionUndo;
        bool sessionDirty;

        string CurrentSelectionKey()
        {
            if (SelectedNodeId >= 0) return "n" + SelectedNodeId;
            if (SelectedRoadId >= 0) return "r" + SelectedRoadId;
            if (SelectedDecor >= 0) return "d" + SelectedDecor;
            if (SelectedPointId >= 0) return "p" + SelectedPointId;
            if (SelectedButtonId >= 0) return "b" + SelectedButtonId;
            if (SelectedArrowId >= 0) return "a" + SelectedArrowId;
            if (SelectedSignRoad >= 0) return "s" + SelectedSignRoad + ":" + SelectedSignNode;
            return "";
        }

        /// <summary>Abre una sesión de edición al seleccionar algo: guarda el estado para poder cancelar los cambios.</summary>
        void NoteSelection()
        {
            string key = CurrentSelectionKey();
            if (key == sessionKey) return;
            sessionKey = key;
            if (key.Length == 0) { sessionSnap = null; return; }
            sessionSnap = gesture != Gesture.None && gestureSnapshot != null && !gestureChanged ? gestureSnapshot : Snapshot();
            sessionUndo = undo.Count;
            sessionDirty = Dirty;
        }

        public bool HasEditSession { get { return sessionSnap != null && sessionKey.Length > 0; } }

        public bool SessionChanged { get { return HasEditSession && undo.Count > sessionUndo; } }

        /// <summary>Acepta los cambios hechos desde que se seleccionó el elemento y lo deselecciona.</summary>
        public void ConfirmSelection()
        {
            SelectedNodeId = SelectedRoadId = -1;
            ClearObjSel();
            LinkingButtonId = -1;
            sessionKey = ""; sessionSnap = null;
            Status = "Cambios confirmados.";
        }

        /// <summary>Descarta los cambios hechos desde que se seleccionó el elemento (también del historial de deshacer).</summary>
        public void CancelSelection()
        {
            if (HasEditSession && undo.Count > sessionUndo)
            {
                RevertTo(sessionSnap);
                undo.RemoveRange(sessionUndo, undo.Count - sessionUndo);
                redo.Clear();
                Dirty = sessionDirty;
                Status = "Cambios cancelados.";
            }
            else Status = "No había cambios que cancelar.";
            SelectedNodeId = SelectedRoadId = -1;
            ClearObjSel();
            LinkingButtonId = -1;
            sessionKey = ""; sessionSnap = null;
        }

        // ---------------- Dibujo superpuesto de objetos ----------------

        static readonly Color[] RestrictionColors =
        {
            new Color(0, 0, 0, 0), new Color(0.95f, 0.6f, 0.15f, 0.6f), new Color(0.3f, 0.8f, 0.4f, 0.6f),
            new Color(0.9f, 0.25f, 0.25f, 0.6f), new Color(0.3f, 0.55f, 0.95f, 0.6f)
        };

        void DrawObjectOverlay(float wpp)
        {
            var hot = new Color(1f, 0.9f, 0.3f, 1f);
            DrawSignRing(wpp);
            DrawDestNumbers(wpp);

            if (Tool == EditorTool.Restrictions)
            {
                foreach (var r in Map.roads)
                {
                    var c = RestrictionColors[Mathf.Clamp(r.AllowPreset, 0, RestrictionColors.Length - 1)];
                    if (c.a <= 0f) continue;
                    Polyline pl = Map.CenterOf(r);
                    float w = (r.lanesAB + r.lanesBA) * RoadNetworkManagerLane() + 0.2f;
                    for (int i = 0; i < pl.pts.Length - 1; i++) Segment(pl.pts[i], pl.pts[i + 1], w, c, 47);
                }
            }

            if (Tool == EditorTool.Controller)
            {
                foreach (var n in Map.nodes)
                    if (Map.Degree(n.id) >= 3 || n.roundabout)
                        Spr(SpriteFactory.Ring, n.id == SelectedNodeId ? hot : new Color(0.3f, 0.85f, 1f, 0.9f), n.Pos, Vector2.one * 0.9f, 48);
            }

            // Marcadores de puntos lógicos y botones para poder encontrarlos al editar.
            if (Tool == EditorTool.Edit || Tool == EditorTool.BusStop || Tool == EditorTool.Toll || Tool == EditorTool.Detector || Tool == EditorTool.Erase)
            {
                foreach (var q in Map.points)
                {
                    var r = Map.Road(q.road);
                    if (r == null) continue;
                    Vector2 pos = Map.PointOnRoad(r, q.t);
                    Spr(SpriteFactory.Square, q.id == SelectedPointId ? hot : new Color(0.2f, 0.85f, 1f, 0.95f), pos, Vector2.one * 14f * wpp, 53, 45f);
                    if (q.id == SelectedPointId) Spr(SpriteFactory.Ring, hot, pos, Vector2.one * 0.9f, 56);
                }
            }

            if (Tool == EditorTool.LaneMask || SelectedArrowId >= 0)
            {
                foreach (var ar in Map.arrows)
                {
                    Vector2 ap, ad;
                    if (!ArrowPose(ar, out ap, out ad)) continue;
                    bool sel = ar.id == SelectedArrowId;
                    Spr(SpriteFactory.Ring, sel ? hot : new Color(0.3f, 0.85f, 1f, 0.8f), ap, Vector2.one * (sel ? 0.6f : 0.45f) * ar.size, sel ? 56 : 50);
                }
            }

            var sl = SelectedLight;
            if (sl != null)
            {
                var slr = Map.Road(sl.road);
                if (slr != null) Spr(SpriteFactory.Ring, hot, Map.PointOnRoad(slr, sl.node == slr.a ? 0.12f : 0.88f), Vector2.one * 0.8f, 56);
            }

            var sb = SelectedButton;
            if (sb != null)
            {
                Spr(SpriteFactory.Ring, hot, sb.Pos, Vector2.one * 0.95f, 56);
                if (LinkingButtonId == sb.id) Spr(SpriteFactory.Ring, new Color(1f, 0.5f, 0.1f, 0.9f), sb.Pos, Vector2.one * (1.2f + 0.2f * Mathf.Sin(Time.unscaledTime * 6f)), 56);
            }

            var sd = SelectedDecorItem;
            if (sd != null)
            {
                if (sd.IsLine())
                {
                    Segment(sd.Pos, sd.End, 8f * wpp, new Color(1f, 0.9f, 0.3f, 0.5f), 49);
                    Spr(SpriteFactory.Circle, hot, sd.Pos, Vector2.one * 16f * wpp, 57);
                    Spr(SpriteFactory.Circle, hot, sd.End, Vector2.one * 16f * wpp, 57);
                }
                else if (sd.IsPoly())
                {
                    int n = sd.pts.Count;
                    for (int i = 0; i + 1 < n; i++) Segment(sd.PolyPoint(i), sd.PolyPoint(i + 1), 4f * wpp, new Color(1f, 0.9f, 0.3f, 0.7f), 49);
                    if (sd.type == DecorType.Zone && n >= 3 && !PolyAdding) Segment(sd.PolyPoint(n - 1), sd.PolyPoint(0), 4f * wpp, new Color(1f, 0.9f, 0.3f, 0.7f), 49);
                    for (int i = 0; i < n; i++)
                        Spr(SpriteFactory.Circle, i == 0 && PolyAdding && sd.type == DecorType.Zone && n >= 3 ? new Color(0.4f, 1f, 0.5f, 0.95f) : hot, sd.PolyPoint(i), Vector2.one * 18f * wpp, 57);
                }
                else if (sd.type == DecorType.Material && sd.pts != null && sd.pts.Count >= 3)
                {
                    // Como el original: contorno real, un punto en cada esquina (cambia la forma) y uno en el centro (mueve).
                    int n = sd.pts.Count;
                    for (int i = 0; i < n; i++) Segment(sd.PolyPoint(i), sd.PolyPoint((i + 1) % n), 4f * wpp, new Color(1f, 0.9f, 0.3f, 0.7f), 49);
                    for (int i = 0; i < n; i++) Spr(SpriteFactory.Square, hot, sd.PolyPoint(i), Vector2.one * 18f * wpp, 57);
                    Spr(SpriteFactory.Square, hot, sd.Pos, Vector2.one * 18f * wpp, 57);
                }
                else if (sd.IsArea())
                {
                    Vector2 h = new Vector2(Mathf.Abs(sd.x2), Mathf.Abs(sd.y2)) * sd.size;
                    Vector2 c = sd.Pos;
                    float w = 4f * wpp;
                    Segment(c + new Vector2(-h.x, -h.y), c + new Vector2(h.x, -h.y), w, hot, 49);
                    Segment(c + new Vector2(h.x, -h.y), c + new Vector2(h.x, h.y), w, hot, 49);
                    Segment(c + new Vector2(h.x, h.y), c + new Vector2(-h.x, h.y), w, hot, 49);
                    Segment(c + new Vector2(-h.x, h.y), c + new Vector2(-h.x, -h.y), w, hot, 49);
                }
                else Spr(SpriteFactory.Ring, hot, sd.Pos, Vector2.one * Mathf.Max(0.8f, 0.9f * sd.size), 56);
            }

            if (gesture == Gesture.Shape)
            {
                var c = new Color(0.4f, 1f, 0.5f, 0.9f);
                if (DecorItem.IsLine(Brush)) Segment(shapeStart, shapeEnd, 6f * wpp, c, 58);
                else
                {
                    Vector2 a = shapeStart, b = shapeEnd;
                    float w = 4f * wpp;
                    Segment(a, new Vector2(b.x, a.y), w, c, 58); Segment(new Vector2(b.x, a.y), b, w, c, 58);
                    Segment(b, new Vector2(a.x, b.y), w, c, 58); Segment(new Vector2(a.x, b.y), a, w, c, 58);
                }
            }
        }

        static float RoadNetworkManagerLane() { return IntersectionMaster.Traffic.RoadNetworkManager.LaneWidth; }
    }
}
