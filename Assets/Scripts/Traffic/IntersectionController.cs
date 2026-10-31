using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Data;

namespace IntersectionMaster.Traffic
{
    /// <summary>Cómo se regula un cruce: sin regular, con semáforo, con señales de prioridad (stop/ceda) o como rotonda.</summary>
    public enum ControlMode { Free, Lights, Signs, Roundabout }

    /// <summary>
    /// Un nodo con 3 o más calles (o una rotonda). Decide si un vehículo puede entrar: semáforo, señales,
    /// cesión de paso según los conflictos entre conectores (prioridad, y orden de llegada si es igual) y hueco de salida.
    /// </summary>
    public partial class IntersectionController
    {
        public readonly NetNode node;
        public readonly ControlMode mode;
        public readonly TrafficLightController light = new TrafficLightController();

        public bool IsLit { get { return mode == ControlMode.Lights; } }
        /// <summary>Semáforo averiado (evento): parpadea en ámbar y los vehículos pasan como en un cruce sin semáforo.</summary>
        public bool broken;

        /// <summary>Vehículos dentro del cuadro del cruce (conectores y anillo).</summary>
        public int Occupants
        {
            get
            {
                int n = 0;
                for (int i = 0; i < node.connectors.Count; i++) n += node.connectors[i].vehicles.Count;
                for (int i = 0; i < node.ringSegs.Count; i++) n += node.ringSegs[i].vehicles.Count;
                return n;
            }
        }

        readonly Dictionary<Approach, HashSet<Approach>> clash = new Dictionary<Approach, HashSet<Approach>>();

        public IntersectionController(NetNode node, TrafficSettings settings)
        {
            this.node = node;
            node.controller = this;

            bool lights = false, rules = false;
            foreach (var a in node.approaches)
            {
                if (a.hasLight && a.inLanes.Count > 0) lights = true;
                if (a.hasRule) rules = true;
            }
            if (node.roundabout) mode = ControlMode.Roundabout;
            else if (lights) mode = ControlMode.Lights;
            else if (rules) mode = ControlMode.Signs;
            else mode = ControlMode.Free;

            for (int i = 0; i < node.connectors.Count; i++) node.connectors[i].priority = ConnectorPriority(node.connectors[i]);

            if (mode != ControlMode.Lights) return;

            // Accesos cuyos movimientos se cruzan (según los conflictos entre conectores).
            for (int i = 0; i < node.connectors.Count; i++)
            {
                var c = node.connectors[i];
                if (c.inApp == null) continue;
                for (int k = 0; k < c.conflicts.Count; k++)
                {
                    var oa = c.conflicts[k].other.inApp;
                    if (oa == null || oa == c.inApp) continue;
                    AddClash(c.inApp, oa);
                    AddClash(oa, c.inApp);
                }
            }

            var phases = BuildPhases(node);
            light.phases.AddRange(phases);
            // Controlador de semáforos del nodo: 0 = ajustes del mapa, 1 = manual, 2 = ciclo automático.
            light.autoCycle = node.ctrl == 0 ? settings.autoCycle : node.ctrl == 2;
            light.greenSeconds = node.green > 0.1f ? node.green : settings.greenSeconds;
            if (node.yellow > 0.1f) light.yellowSeconds = node.yellow;
            light.safeMode = settings.safeSignals;
            light.conflicts = (x, y) => { HashSet<Approach> set; return clash.TryGetValue(x, out set) && set.Contains(y); };
            light.isClear = IsClearFor;
            light.groupOf = x => x.lightPhase;
            foreach (var a in node.approaches) if (a.hasLight && a.inLanes.Count > 0) light.Register(a);
            light.InitStart();
        }

        void AddClash(Approach a, Approach b)
        {
            HashSet<Approach> set;
            if (!clash.TryGetValue(a, out set)) { set = new HashSet<Approach>(); clash[a] = set; }
            set.Add(b);
        }

        bool IsClearFor(Approach a)
        {
            if (laneMode) return LaneClearFor(a);
            HashSet<Approach> set;
            if (!clash.TryGetValue(a, out set)) return true;
            for (int i = 0; i < node.connectors.Count; i++)
            {
                var c = node.connectors[i];
                if (c.inApp == null || !set.Contains(c.inApp)) continue;
                if (c.vehicles.Count > 0) return false;
            }
            return true;
        }

        static List<SignalPhase> BuildPhases(NetNode node)
        {
            var result = new List<SignalPhase>();
            // Fases definidas por el usuario en el editor: los semáforos con el mismo número de fase forman un grupo;
            // los que no tienen fase van cada uno en la suya.
            bool user = false;
            foreach (var a in node.approaches) if (a.hasLight && a.lightPhase > 0) user = true;
            if (user)
            {
                var byNum = new SortedDictionary<int, SignalPhase>();
                int solo = 100;
                foreach (var a in node.approaches)
                {
                    if (!a.hasLight || a.inLanes.Count == 0) continue;
                    int key = a.lightPhase > 0 ? a.lightPhase : solo++;
                    SignalPhase ph;
                    if (!byNum.TryGetValue(key, out ph)) { ph = new SignalPhase { label = key < 100 ? "Fase " + key : "Semáforo" }; byNum[key] = ph; }
                    ph.approaches.Add(a);
                }
                foreach (var kv in byNum) result.Add(kv.Value);
                return result;
            }
            var ins = new List<Approach>();
            foreach (var a in node.approaches) if (a.inLanes.Count > 0) ins.Add(a);
            if (ins.Count < 2) return result;

            var first = new SignalPhase();
            var second = new SignalPhase();
            float axis = ins[0].angle;
            foreach (var a in ins)
            {
                float d = Mathf.Abs(Mathf.DeltaAngle(a.angle, axis));
                if (d < 35f || d > 145f) first.approaches.Add(a); else second.approaches.Add(a);
            }
            if (second.approaches.Count == 0) return result;

            first.label = AxisLabel(axis);
            float axis2 = 0f;
            foreach (var a in second.approaches) { axis2 = a.angle; break; }
            second.label = AxisLabel(axis2);
            if (second.label == first.label) second.label += " (2)";
            result.Add(first);
            result.Add(second);
            return result;
        }

        static string AxisLabel(float angle)
        {
            float a = angle % 180f;
            if (a < 0f) a += 180f;
            if (a < 40f || a > 140f) return "Este-Oeste";
            if (a > 50f && a < 130f) return "Norte-Sur";
            return "Diagonal";
        }

        /// <summary>
        /// Un peatón no aparece ya encima de un cruce con semáforo peatonal (se saltaría el semáforo): aparece en una acera
        /// que llega a él y espera en el borde. Si la acera termina junto a la calle (sin sitio para esperar), aparece en
        /// ese extremo solo cuando su semáforo está en verde; en rojo no aparece.
        /// </summary>
        public static bool BlockedPedStart(RoadSeg l, IList<IntersectionController> ics)
        {
            if (l.laneType != RoadData.LaneSidewalk || l.fromApp == null || !l.fromApp.hasLight) return false;
            bool crosses = false;
            foreach (var c in l.conflicts) if (c.other != null && c.other.laneType != RoadData.LaneSidewalk) { crosses = true; break; }
            if (!crosses) return false;
            bool onlyTwins = l.fromApp.inLanes.Count > 0;
            foreach (var x in l.fromApp.inLanes) if (!x.twin) { onlyTwins = false; break; }
            if (onlyTwins && ics != null)
                foreach (var ic in ics)
                    if (ic.node == l.fromApp.node && ic.light.Has(l.fromApp))
                    {
                        if (ic.GetState(l.fromApp) != SignalState.Green) return true;
                        // Y que no venga un vehículo por la calle que cruza (aparecería delante de él).
                        foreach (var c in l.conflicts)
                        {
                            if (c.other == null || c.other.laneType == RoadData.LaneSidewalk) continue;
                            foreach (var w in c.other.vehicles)
                            {
                                float d = c.otherS - w.s;
                                if (d > -(c.otherClear + w.HalfLength) && d < c.otherClear + w.HalfLength + 2.5f) return true;
                            }
                        }
                        return false;
                    }
            return true;
        }

        public SignalState GetState(Approach a)
        {
            return IsLit && !broken && light.Has(a) ? light.GetState(a) : SignalState.Green;
        }

        /// <summary>Prioridad de una aproximación: 0 = stop, 1 = ceda, 2 = normal, 3 = señal de prioridad.</summary>
        public int Rank(Approach a)
        {
            if (mode == ControlMode.Roundabout) return 1;
            if (a.hasRule)
            {
                switch (a.rule)
                {
                    case SignType.Stop: return 0;
                    case SignType.Yield: return 1;
                    default: return 3;
                }
            }
            return 2;
        }

        int ConnectorPriority(RoadSeg c)
        {
            if (c.inApp == null) return 40;
            return Rank(c.inApp) * 10 + (c.move == 1 ? 1 : 2);
        }

        public bool MustStopFully(Approach a)
        {
            return a.hasRule && a.rule == SignType.Stop;
        }

        public bool HasGiveWay(Approach a)
        {
            return Rank(a) < 2;
        }

        bool FirstCome(VehicleController w, VehicleController v)
        {
            if (w.lineTimer > v.lineTimer + 0.05f) return true;
            return Mathf.Abs(w.lineTimer - v.lineTimer) <= 0.05f && w.id < v.id;
        }

        /// <summary>
        /// ¿Debe esperar el vehículo (en la línea de parada) antes de tomar el conector? Sí si hay alguien en un conector
        /// que se cruza o confluye y sigue ocupándolo, o si llega (o espera con derecho) alguien con más prioridad.
        /// </summary>
        public bool MustYield(VehicleController v, RoadSeg conn)
        {
            if (conn.ringEntry) return RingBlocked(conn);
            float eta = v.waitTime > 10f ? 1.0f : 2.0f;

            for (int i = 0; i < conn.conflicts.Count; i++)
            {
                var c = conn.conflicts[i];
                var o = c.other;
                bool oBeats = o.priority > conn.priority;

                // Vehículos ya dentro del otro conector.
                for (int k = 0; k < o.vehicles.Count; k++)
                {
                    var w = o.vehicles[k];
                    if (w == v) continue;
                    float front = w.s + w.HalfLength;
                    float rear = w.s - w.HalfLength;
                    float margin = oBeats ? 0.9f : 0.15f;
                    if (rear < c.otherS + 0.2f && front > c.otherS - margin) return true;
                }

                // Vehículos que llegan (o esperan en la línea) por el carril que alimenta al otro conector.
                var lane = o.inLane;
                if (lane == null) continue;
                for (int k = 0; k < lane.vehicles.Count; k++)
                {
                    var w = lane.vehicles[k];
                    if (w == v || w.crashed || w.NextSeg != o) continue;
                    bool beats = oBeats || (o.priority == conn.priority && FirstCome(w, v));
                    if (!beats) continue;
                    float toEnd = lane.Length - w.s - w.HalfLength;
                    if (w.speed > 0.2f)
                    {
                        float d = toEnd + c.otherS;
                        if (d < 3.5f && d / w.speed < eta) return true;
                    }
                    else if (lane.stopS - (w.s + w.HalfLength) < 0.6f && GetState(lane.toApp) != SignalState.Red)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        bool RingBlocked(RoadSeg conn)
        {
            var mouth = conn.mouth;
            if (mouth == null) return false;
            if (mouth.vehicles.Count > 0) return true;
            for (int i = 0; i < mouth.ins.Count; i++)
            {
                var prev = mouth.ins[i];
                for (int k = 0; k < prev.vehicles.Count; k++)
                {
                    var w = prev.vehicles[k];
                    if (w.NextSeg == mouth && prev.Length - w.s < 1.8f) return true;
                }
            }
            return false;
        }

        /// <summary>Hay sitio al inicio del carril de salida para que quepa el vehículo (no bloquear el cruce).</summary>
        public bool TargetFree(VehicleController v, RoadSeg conn)
        {
            if (conn == null || conn.outs.Count == 0) return true;
            var target = conn.outs[0];
            var list = target.vehicles;
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                if (o == v) continue;
                if (o.s - o.HalfLength < v.Length + 0.15f) return false;
            }
            return true;
        }
    }
}
