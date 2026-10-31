using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Core;
using IntersectionMaster.Data;
using IntersectionMaster.Systems;

namespace IntersectionMaster.MapEditing
{
    /// <summary>
    /// Acciones del editor para el modelo por carriles (como Intersection Controller 2): propiedades del carril nuevo y
    /// del seleccionado, y señales del nodo (semáforo, ceda el paso, stop, farola, fase).
    /// </summary>
    public partial class MapEditorController
    {
        /// <summary>Copia en el carril recién creado el tipo y las propiedades elegidas en Herramienta.</summary>
        void ApplyNewLaneProps(RoadData lane)
        {
            var p = NewLaneProps;
            lane.laneType = Mathf.Clamp(NewLaneType, 0, RoadData.LaneTypeNames.Length - 1);
            lane.hidden = p.hidden;
            lane.noLaneChange = p.noLaneChange;
            lane.allow = lane.laneType == RoadData.LaneVehicle ? p.allow : 0;
            lane.speedKmh = p.speedKmh;
            lane.lineL = p.lineL;
            lane.lineR = p.lineR;
            lane.lineSame = p.lineSame;
            lane.lineOpp = p.lineOpp;
            lane.lineEdge = p.lineEdge;
            lane.noMarks = p.noMarks;
            lane.cables = p.cables;
            lane.rails = p.rails;
            lane.color = p.color;
            lane.colorHex = p.colorHex;
            lane.tram = p.tram;
        }

        /// <summary>Como el original: el carril nuevo sale con la configuración del último carril creado o editado.</summary>
        void RememberLane(RoadData r)
        {
            var p = NewLaneProps;
            p.hidden = r.hidden; p.noLaneChange = r.noLaneChange; p.allow = r.allow; p.speedKmh = r.speedKmh;
            p.lineL = r.lineL; p.lineR = r.lineR; p.lineSame = r.lineSame; p.lineOpp = r.lineOpp; p.lineEdge = r.lineEdge;
            p.noMarks = r.noMarks; p.cables = r.cables; p.rails = r.rails; p.color = r.color; p.colorHex = r.colorHex; p.tram = r.tram;
        }

        public void SetNewLaneType(int type)
        {
            NewLaneType = Mathf.Clamp(type, 0, RoadData.LaneTypeNames.Length - 1);
            if (Pending) UpdatePendingStatus();
            else Status = "Tipo: " + RoadData.LaneTypeNames[NewLaneType] + ". Arrastra para dibujarlo.";
        }

        // ---- Propiedades del carril nuevo ----

        public void ToggleNewLaneHidden() { NewLaneProps.hidden = !NewLaneProps.hidden; }
        public void ToggleNewLaneChange() { NewLaneProps.noLaneChange = !NewLaneProps.noLaneChange; }
        public void CycleNewLaneSpeed() { NewLaneProps.speedKmh = NextSpeed(NewLaneProps.speedKmh); }
        public void CycleNewLaneLine(bool left)
        {
            if (left) NewLaneProps.lineL = (NewLaneProps.lineL + 1) % RoadData.LineNames.Length;
            else NewLaneProps.lineR = (NewLaneProps.lineR + 1) % RoadData.LineNames.Length;
        }
        public void CycleNewLaneColor() { NewLaneProps.color = (NewLaneProps.color + 1) % MapRenderer.RoadColorNames.Length; }
        public void ToggleNewLaneCables() { NewLaneProps.cables = !NewLaneProps.cables; }
        public void ToggleNewLaneRails() { NewLaneProps.rails = !NewLaneProps.rails; }
        public void CycleNewLaneAllow() { NewLaneProps.NextAllowPreset(); }

        static int NextSpeed(int cur)
        {
            var steps = RoadData.SpeedSteps;
            for (int i = 0; i < steps.Length; i++) if (steps[i] == cur) return steps[(i + 1) % steps.Length];
            return steps[0];
        }

        /// <summary>Texto del límite de velocidad: «50» (normal) o el valor propio.</summary>
        /// <summary>Velocidad en las unidades elegidas en Ajustes (km/h o mph).</summary>
        public static string SpeedLabel(int kmh)
        {
            int v = kmh <= 0 ? 50 : kmh;
            return SaveSystem.Progress.mph ? Mathf.RoundToInt(v * 0.621371f).ToString() : v.ToString();
        }
        public static string SpeedUnit { get { return SaveSystem.Progress.mph ? "mph" : "km/h"; } }

        // ---- Propiedades del carril seleccionado ----

        public void CycleSelectedSpeed()
        {
            Edit(() => { var r = SelectedRoad; if (r == null) return false; r.speedKmh = NextSpeed(r.speedKmh); RememberLane(r); Status = "Velocidad: " + SpeedLabel(r.speedKmh) + " " + SpeedUnit + "."; return true; }, false);
        }

        public void CycleSelectedLine(bool left)
        {
            Edit(() =>
            {
                var r = SelectedRoad; if (r == null) return false;
                if (left) r.lineL = (r.lineL + 1) % RoadData.LineNames.Length;
                else r.lineR = (r.lineR + 1) % RoadData.LineNames.Length;
                if (r.noMarks) r.noMarks = false;
                RememberLane(r);
                Status = (left ? "Línea izquierda: " : "Línea derecha: ") + RoadData.LineNames[left ? r.lineL : r.lineR] + ".";
                return true;
            }, true);
        }

        static readonly int[] ArrowCycle = { 0, 2, 1, 4, 3, 6, 5, 7 };
        public static readonly string[] ArrowCycleNames = { "Sin flecha", "Recto", "Izquierda", "Derecha", "Recto e izquierda", "Recto y derecha", "Izquierda y derecha", "Todas", "Curva izquierda", "Curva derecha" };

        /// <summary>Flecha pintada en el carril seleccionado (en la mitad): cambia entre los dibujos posibles.</summary>
        public void CycleSelectedArrow()
        {
            Edit(() =>
            {
                var r = SelectedRoad; if (r == null) return false;
                int cur = Map.LaneArrowMoves(r.id, true, 0);
                int idx = 0;
                for (int i = 0; i < ArrowCycle.Length; i++) if (ArrowCycle[i] == cur) idx = i;
                int next = ArrowCycle[(idx + 1) % ArrowCycle.Length];
                Map.arrows.RemoveAll(a => a.road == r.id);
                if (next != 0) Map.AddArrow(r.id, true, 0, 0.5f, next);
                Status = "Flecha: " + ArrowCycleNames[(idx + 1) % ArrowCycle.Length] + ".";
                return true;
            }, true);
        }

        public int SelectedArrowIndex()
        {
            var r = SelectedRoad;
            if (r == null) return 0;
            int cur = Map.LaneArrowMoves(r.id, true, 0);
            var sa = SelectedLaneArrow;
            if (sa != null && sa.style == 1 && (cur == 1 || cur == 4)) return cur == 1 ? 8 : 9;
            for (int i = 0; i < ArrowCycle.Length; i++) if (ArrowCycle[i] == cur) return i;
            return 0;
        }

        // ---- Nodo: semáforo, ceda el paso, stop, farola y fase ----

        List<RoadData> LanesInto(int nodeId)
        {
            var list = new List<RoadData>();
            foreach (var r in Map.roads) if (r.b == nodeId) list.Add(r);
            return list;
        }

        /// <summary>¿Tienen la señal todos los carriles que llegan al nodo?</summary>
        public bool NodeHasSign(int nodeId, SignType type)
        {
            if (Map == null) return false;
            if (type == SignType.Light && IsSidewalkNode(nodeId)) return HasLightAt(nodeId);
            var ins = LanesInto(nodeId);
            if (ins.Count == 0) return false;
            foreach (var r in ins) if (Map.FindSign(r.id, nodeId, type) == null) return false;
            return true;
        }

        /// <summary>Pone (o quita, si ya está) la señal en todos los carriles que llegan al nodo.</summary>
        public bool ToggleNodeSign(int nodeId, SignType type)
        {
            if (type == SignType.Light && IsSidewalkNode(nodeId)) return TogglePedNodePair(nodeId);
            var ins = LanesInto(nodeId);
            if (ins.Count == 0) { Status = "A ese nodo no llega ningún carril: la señal va donde terminan los carriles."; return false; }
            bool all = NodeHasSign(nodeId, type);
            foreach (var r in ins)
            {
                bool has = Map.FindSign(r.id, nodeId, type) != null;
                if (has == all) Map.ToggleSign(r.id, nodeId, type);
            }
            string name = type == SignType.Light ? "Semáforo" : type == SignType.Yield ? "Ceda el paso" : type == SignType.Stop ? "Stop" : "Señal";
            Status = name + (all ? " quitado." : " puesto.");
            return true;
        }

        // ---- Semáforo peatonal (como en la vida real: uno a cada lado del cruce de la acera) ----

        /// <summary>¿El punto está sobre la calzada (un carril de vehículos o bici de esa capa)?</summary>
        bool OnCarriageway(Vector2 p, int layer)
        {
            foreach (var o in Map.roads)
            {
                if (o.laneType != RoadData.LaneVehicle && o.laneType != RoadData.LaneBike) continue;
                if (o.layer != layer && o.layer + o.ramp != layer) continue;
                float d;
                Map.NearestParam(o, p, out d);
                if (d < o.Width * 0.5f - 0.01f) return true;
            }
            return false;
        }

        public bool SidewalkCrossesRoad(RoadData r)
        {
            var pl = Map.CenterOf(r);
            for (float s = 0f; s <= pl.Length; s += 0.03f) if (OnCarriageway(pl.PointAt(s), r.layer)) return true;
            return false;
        }

        bool HasLightAt(int nodeId)
        {
            foreach (var x in Map.RoadsAt(nodeId)) if (Map.FindSign(x.id, nodeId, SignType.Light) != null) return true;
            return false;
        }

        void RemoveLightsAt(int nodeId)
        {
            foreach (var x in Map.RoadsAt(nodeId)) { var sg = Map.FindSign(x.id, nodeId, SignType.Light); if (sg != null) Map.signs.Remove(sg); }
        }

        /// <summary>Nodo al que solo llegan aceras.</summary>
        bool IsSidewalkNode(int nodeId)
        {
            var at = Map.RoadsAt(nodeId);
            if (at.Count == 0) return false;
            foreach (var r in at) if (r.laneType != RoadData.LaneSidewalk) return false;
            return true;
        }

        /// <summary>
        /// Semáforo puesto desde un nodo de acera (como en el original, con «Semáforo» del nodo): si ese nodo es un borde
        /// de un cruce de calle, se pone (o quita) también en el otro borde, para que haya uno a cada lado.
        /// </summary>
        bool TogglePedNodePair(int nodeId)
        {
            RoadData cross = null;
            foreach (var o in Map.RoadsAt(nodeId))
                if (o.laneType == RoadData.LaneSidewalk && SidewalkCrossesRoad(o)) { cross = o; break; }
            if (cross == null)
            {
                Status = "Ese nodo de acera no está junto a un cruce de calle. Selecciona la acera que cruza y usa «Semáforo peatonal».";
                return false;
            }
            int other = cross.a == nodeId ? cross.b : cross.a;
            if (HasLightAt(nodeId) || HasLightAt(other))
            {
                RemoveLightsAt(nodeId); RemoveLightsAt(other);
                Status = "Semáforo peatonal quitado (de los dos lados).";
                return true;
            }
            return PlacePedLights(cross);
        }

        /// <summary>
        /// Pone el semáforo peatonal a los dos lados del cruce de la acera «r» sin mover ningún nodo. En cada lado los
        /// peatones esperan fuera de la calzada:
        ///  · si la acera sale bastante de la calle, se parte a 0,3 del borde (ahí esperan);
        ///  · si sale poco y en ese nodo sigue otra acera, esperan en el nodo (sobre la acera que llega);
        ///  · si sale poco y ahí termina la acera, los peatones aparecen en ese extremo solo con el semáforo en verde.
        /// </summary>
        bool PlacePedLights(RoadData r)
        {
            const float Wait = 0.3f, Room = 0.26f;   // un tramo de acera mide al menos 0,25
            var pl = Map.CenterOf(r);
            float s0, s1;
            if (!CrossSpan(r, pl, out s0, out s1)) { Status = "Esta acera no cruza ninguna calle: el semáforo peatonal va donde la acera cruza la calzada."; return false; }
            float roomA = s0, roomB = pl.Length - s1;
            if (roomA < 0.04f || roomB < 0.04f) { Status = "La acera no sale de la calle: alárgala un poco por fuera para poner el semáforo peatonal."; return false; }
            // Dónde se parte cada lado (-1 = no se parte: se usa el nodo que ya existe; si ahí termina la acera, los
            // peatones solo aparecen ahí cuando el semáforo está en verde).
            float cA = roomA - Wait >= Room ? s0 - Wait : -1f;
            float cB = roomB - Wait >= Room ? s1 + Wait : -1f;
            Vector2 pB = cB >= 0f ? pl.PointAt(cB) : Vector2.zero;
            RoadData cross = r;
            int nA = r.a, nB = r.b;
            if (cA >= 0f)
            {
                var n0 = Map.SplitRoad(r.id, pl.PointAt(cA));
                if (n0 == null) { Status = "No se pudo partir la acera (límite de nodos o carriles)."; return false; }
                cross = null;
                foreach (var o in Map.RoadsAt(n0.id)) if (o.a == n0.id && o.laneType == RoadData.LaneSidewalk) cross = o;
                if (cross == null) return false;
                nA = n0.id;
            }
            if (cB >= 0f)
            {
                var n1 = Map.SplitRoad(cross.id, pB);
                if (n1 == null) { Status = "No se pudo partir la acera (límite de nodos o carriles)."; return false; }
                nB = n1.id;   // el tramo «cross» queda entre nA y nB
            }
            Map.ToggleSign(cross.id, nA, SignType.Light);
            Map.ToggleSign(cross.id, nB, SignType.Light);
            Status = "Semáforo peatonal puesto a los dos lados del cruce. Los peatones esperan fuera de la calle cuando está en rojo.";
            return true;
        }

        bool CrossSpan(RoadData r, Polyline pl, out float s0, out float s1)
        {
            s0 = s1 = -1f;
            for (float s = 0f; s <= pl.Length; s += 0.02f)
                if (OnCarriageway(pl.PointAt(s), r.layer)) { if (s0 < 0f) s0 = s; s1 = s; }
            return s0 >= 0f;
        }

        /// <summary>
        /// Con una acera seleccionada que cruza la calle: pone un semáforo peatonal a cada lado del cruce (parte la acera en
        /// los dos bordes de la calzada; ahí esperan los peatones). Si ya lo tiene, lo quita.
        /// </summary>
        public bool SelectedHasPedLight()
        {
            var r = SelectedRoad;
            return r != null && Map != null && (HasLightAt(r.a) || HasLightAt(r.b));
        }

        public void TogglePedestrianLights()
        {
            Edit(() =>
            {
                var r = SelectedRoad;
                if (r == null || r.laneType != RoadData.LaneSidewalk) { Status = "Selecciona una acera que cruce la calle."; return false; }

                // Quitar: si la acera (o el tramo del cruce junto a ella) ya tiene semáforo peatonal.
                var ends = new List<int> { r.a, r.b };
                bool had = false;
                foreach (int n in ends.ToArray())
                {
                    if (!HasLightAt(n)) continue;
                    had = true;
                    RemoveLightsAt(n);
                    foreach (var o in Map.RoadsAt(n))
                        if (o.laneType == RoadData.LaneSidewalk && SidewalkCrossesRoad(o)) { RemoveLightsAt(o.a); RemoveLightsAt(o.b); }
                }
                if (had) { Status = "Semáforo peatonal quitado."; return true; }

                return PlacePedLights(r);
            }, true);
        }

        public void ToggleSelectedNodeSign(SignType type)
        {
            int id = SelectedNodeId;
            if (Map == null || Map.Node(id) == null) return;
            Edit(() => ToggleNodeSign(id, type), true);
        }

        /// <summary>Fase del semáforo del nodo (los de la misma fase se ponen en verde juntos).</summary>
        public int NodeLightPhase(int nodeId)
        {
            foreach (var r in LanesInto(nodeId))
            {
                var s = Map.FindSign(r.id, nodeId, SignType.Light);
                if (s != null) return s.phase;
            }
            return 0;
        }

        public void ShiftSelectedNodePhase(int delta)
        {
            int id = SelectedNodeId;
            if (Map == null || Map.Node(id) == null) return;
            Edit(() =>
            {
                bool any = false;
                int cur = NodeLightPhase(id);
                int next = Mathf.Clamp(cur + delta, 0, 6);
                foreach (var r in LanesInto(id))
                {
                    var s = Map.FindSign(r.id, id, SignType.Light);
                    if (s == null) continue;
                    s.phase = next;
                    any = true;
                }
                Status = any ? (next == 0 ? "Fase automática." : "Fase " + next + ": los semáforos de la misma fase cambian juntos.") : "Este nodo no tiene semáforo.";
                return any;
            }, true);
        }

        /// <summary>Semáforos del nodo (uno por carril que llega).</summary>
        List<SignItem> NodeLights(int nodeId)
        {
            var list = new List<SignItem>();
            foreach (var r in LanesInto(nodeId))
            {
                var s = Map.FindSign(r.id, nodeId, SignType.Light);
                if (s != null) list.Add(s);
            }
            return list;
        }

        public SignItem NodeLight(int nodeId)
        {
            var l = Map != null ? NodeLights(nodeId) : null;
            return l != null && l.Count > 0 ? l[0] : null;
        }

        void EditNodeLights(System.Action<SignItem> f)
        {
            int id = SelectedNodeId;
            if (Map == null || Map.Node(id) == null) return;
            Edit(() =>
            {
                var l = NodeLights(id);
                foreach (var s in l) f(s);
                return l.Count > 0;
            }, true);
        }

        public void SetSelectedNodeLightModel(int model) { EditNodeLights(s => s.model = Mathf.Clamp(model, 0, SignItem.ModelNames.Length - 1)); }
        public void SetSelectedNodeLightSide(int side) { EditNodeLights(s => s.side = Mathf.Clamp(side, 0, 1)); }
        public void ToggleSelectedNodeLightVisible()
        {
            var first = NodeLight(SelectedNodeId);
            if (first == null) return;
            bool hide = !first.hidden;
            EditNodeLights(s => s.hidden = hide);
        }
        public void AdjustSelectedNodeLightSize(float d) { EditNodeLights(s => s.size = Mathf.Clamp((s.size <= 0.01f ? 1f : s.size) + d, 0.6f, 1.8f)); }

        // ---- Nodo de inicio (como el original): tráfico propio y probabilidad de destino ----

        /// <summary>¿Empiezan carriles de vehículos aquí sin que llegue ninguno? (de ahí salen los coches)</summary>
        public bool IsStartNode(int nodeId)
        {
            if (Map == null || !Map.IsLaneMap) return false;
            bool outs = false;
            foreach (var r in Map.roads)
            {
                if (r.b == nodeId) return false;
                if (r.a == nodeId && r.laneType == RoadData.LaneVehicle) outs = true;
            }
            return outs;
        }

        /// <summary>Nodos finales de carriles de vehículos (destinos posibles), en un orden estable.</summary>
        public List<NodeData> EndNodes()
        {
            var list = new List<NodeData>();
            if (Map == null) return list;
            foreach (var n in Map.nodes)
            {
                bool ins = false, outs = false;
                foreach (var r in Map.roads) { if (r.b == n.id && r.laneType == RoadData.LaneVehicle) ins = true; if (r.a == n.id) outs = true; }
                if (ins && !outs) list.Add(n);
            }
            list.Sort((p, q) => p.y != q.y ? q.y.CompareTo(p.y) : p.x.CompareTo(q.x));
            return list;
        }

        static readonly float[] StartDefaults = { 10f, 3f, 1f, 1f, 1f };

        /// <summary>Vehículos por minuto de un tipo (v &lt; 0 = automático: vuelve al tráfico general del mapa).</summary>
        public void SetSelectedNodeRate(int type, float v)
        {
            var n = SelectedNode;
            if (n == null) return;
            Edit(() =>
            {
                if (v < 0f) { for (int i = 0; i < NodeData.RateCount; i++) n.SetRate(i, -1f); return true; }
                if (!n.CustomRates) for (int i = 0; i < NodeData.RateCount; i++) n.SetRate(i, StartDefaults[i]);
                n.SetRate(type, Mathf.Clamp(v, 0f, 60f));
                return true;
            }, true);
        }

        public float DestWeightOf(NodeData start, int endId)
        {
            if (start == null || start.dest == null) return 50f;
            foreach (var d in start.dest) if (d.node == endId) return d.weight;
            return 50f;
        }

        public void AdjustSelectedDest(int endId, float delta)
        {
            var n = SelectedNode;
            if (n == null) return;
            Edit(() =>
            {
                if (n.dest == null) n.dest = new List<DestWeight>();
                DestWeight dw = null;
                foreach (var d in n.dest) if (d.node == endId) dw = d;
                if (dw == null) { dw = new DestWeight { node = endId, weight = 50f }; n.dest.Add(dw); }
                dw.weight = Mathf.Clamp(dw.weight + delta, 0f, 100f);
                return true;
            }, true);
        }

        public void ResetSelectedDest()
        {
            var n = SelectedNode;
            if (n == null) return;
            Edit(() => { if (n.dest == null || n.dest.Count == 0) return false; n.dest.Clear(); return true; }, true);
        }

        /// <summary>Con un nodo de inicio seleccionado: número de cada destino sobre el mapa (el de la ventana de destinos).</summary>
        void DrawDestNumbers(float wpp)
        {
            var n = SelectedNode;
            if (Tool != EditorTool.Edit || n == null || !IsStartNode(n.id)) return;
            var ends = EndNodes();
            for (int i = 0; i < ends.Count; i++)
            {
                Spr(SpriteFactory.Circle, new Color(0.91f, 0.47f, 0.13f, 1f), ends[i].Pos, Vector2.one * 34f * wpp, 59);
                PreviewLabel((i + 1).ToString(), ends[i].Pos, 22f, Color.white);
            }
        }

        /// <summary>Estilo del stop o del ceda el paso del nodo (ver SignItem.style).</summary>
        public int NodeSignStyle(int nodeId, SignType type)
        {
            if (Map == null) return 0;
            foreach (var r in LanesInto(nodeId)) { var s = Map.FindSign(r.id, nodeId, type); if (s != null) return s.style; }
            return 0;
        }

        /// <summary>Pone el stop o el ceda con un estilo (style &lt; 0 = quitarlo). El otro de los dos se quita: solo uno por nodo.</summary>
        public void SetSelectedNodeSign(SignType type, int style)
        {
            int id = SelectedNodeId;
            if (Map == null || Map.Node(id) == null) return;
            Edit(() =>
            {
                var ins = LanesInto(id);
                if (ins.Count == 0) { Status = "A ese nodo no llega ningún carril: la señal va donde terminan los carriles."; return false; }
                SignType other = type == SignType.Stop ? SignType.Yield : SignType.Stop;
                foreach (var r in ins)
                {
                    if (Map.FindSign(r.id, id, other) != null) Map.ToggleSign(r.id, id, other);
                    bool has = Map.FindSign(r.id, id, type) != null;
                    if (style < 0) { if (has) Map.ToggleSign(r.id, id, type); continue; }
                    if (!has) Map.ToggleSign(r.id, id, type);
                    var s = Map.FindSign(r.id, id, type);
                    if (s != null) s.style = Mathf.Clamp(style, 0, 2);
                }
                return true;
            }, true);
        }

        public void SetSelectedNodeSignStyle(SignType type, int style)
        {
            int id = SelectedNodeId;
            if (Map == null || Map.Node(id) == null) return;
            Edit(() =>
            {
                bool any = false;
                foreach (var r in LanesInto(id)) { var s = Map.FindSign(r.id, id, type); if (s != null) { s.style = Mathf.Clamp(style, 0, 2); any = true; } }
                return any;
            }, true);
        }

        /// <summary>Farola junto al nodo (se pone o se quita).</summary>
        public void ToggleSelectedNodeLamp()
        {
            var n = SelectedNode;
            if (n == null) return;
            Edit(() =>
            {
                int idx = Map.DecorNear(n.Pos, 0.6f);
                if (idx >= 0 && Map.decor[idx].type == DecorType.Lamp) { Map.decor.RemoveAt(idx); Status = "Farola quitada."; return true; }
                Vector2 side = Vector2.zero;
                var ins = LanesInto(n.id);
                if (ins.Count > 0)
                {
                    var pl = Map.CenterOf(ins[0]);
                    side = Geo.Right(pl.TangentAt(pl.Length)) * (ins[0].Width * 0.5f + 0.25f);
                }
                bool ok = Map.PlaceDecorItem(new DecorItem { x = n.x + side.x, y = n.y + side.y, type = DecorType.Lamp });
                Status = ok ? "Farola puesta." : "No cabe la farola.";
                return ok;
            }, true);
        }

        public bool NodeHasLamp(int nodeId)
        {
            var n = Map != null ? Map.Node(nodeId) : null;
            if (n == null) return false;
            int idx = Map.DecorNear(n.Pos, 0.6f);
            return idx >= 0 && Map.decor[idx].type == DecorType.Lamp;
        }

        // ---- Opciones de las ventanitas (Propiedades del carril nuevo o Segmento seleccionado) ----

        /// <summary>Carril cuyas propiedades se editan: el que se va a dibujar (Herramienta) o el seleccionado.</summary>
        public RoadData PropTarget { get { return Tool == EditorTool.Road ? NewLaneProps : SelectedRoad; } }

        public void ApplyLaneProp(System.Action<RoadData> f)
        {
            if (Tool == EditorTool.Road) { f(NewLaneProps); return; }
            Edit(() => { var r = SelectedRoad; if (r == null) return false; f(r); RememberLane(r); return true; }, true);
        }

        public LaneArrowItem SelectedLaneArrow
        {
            get
            {
                var r = SelectedRoad;
                if (r == null || Map == null) return null;
                foreach (var a in Map.arrows) if (a.road == r.id) return a;
                return null;
            }
        }

        /// <summary>Pone la flecha pintada del carril seleccionado (moves = 0 la quita). Conserva posición y color.</summary>
        public void SetSelectedArrow(int moves, int style = 0)
        {
            Edit(() =>
            {
                var r = SelectedRoad; if (r == null) return false;
                var old = SelectedLaneArrow;
                float t = old != null ? old.t : 0.5f;
                int col = old != null ? old.color : 0;
                Map.arrows.RemoveAll(a => a.road == r.id);
                if (moves != 0)
                {
                    var na = Map.AddArrow(r.id, true, 0, t, moves);
                    if (na != null) { na.color = col; na.style = style; }
                }
                return true;
            }, true);
        }

        public void ShiftSelectedArrowPos(float delta)
        {
            Edit(() =>
            {
                var a = SelectedLaneArrow; if (a == null) return false;
                a.t = Mathf.Clamp(a.t + delta, 0.05f, 0.95f);
                return true;
            }, true);
        }

        public void SetSelectedArrowColor(int color)
        {
            Edit(() =>
            {
                var a = SelectedLaneArrow; if (a == null) return false;
                a.color = Mathf.Clamp(color, 0, 1);
                return true;
            }, true);
        }
    }
}
