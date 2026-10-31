using System;
using System.Collections.Generic;
using UnityEngine;

namespace IntersectionMaster.Traffic
{
    public enum SignalState { Red, Yellow, Green }

    /// <summary>Grupo de accesos que comparten verde en el ciclo automático.</summary>
    public class SignalPhase
    {
        public string label;
        public readonly HashSet<Approach> approaches = new HashSet<Approach>();
        /// <summary>Segundos en verde de esta etapa (0 = los del controlador).</summary>
        public float green;
    }

    /// <summary>
    /// Semáforos de un cruce. Cada acceso con semáforo (Approach) tiene el suyo y se cambia de uno en uno.
    /// Con safeMode, al pedir verde en un acceso los que se cruzan con él pasan primero a amarillo y rojo, y el verde
    /// no llega hasta que esos movimientos han terminado de despejar el cuadro. Sin safeMode el verde es inmediato
    /// (puede haber choques). El ciclo automático opcional va rotando grupos de accesos (fases).
    /// </summary>
    public class TrafficLightController
    {
        class Light
        {
            public SignalState state = SignalState.Red;
            public float timer;
            public bool wantGreen;
            public bool forced;    // verde pedido por el jugador en modo libre: no espera a nadie
        }

        readonly Dictionary<Approach, Light> lights = new Dictionary<Approach, Light>();
        readonly List<Approach> order = new List<Approach>();

        public readonly List<SignalPhase> phases = new List<SignalPhase>();
        public bool autoCycle;
        public float greenSeconds = 8f;
        public float yellowSeconds = 1.4f;
        public float allRedSeconds = 0.8f;
        public bool safeMode = true;
        /// <summary>Como el original: tocar un semáforo solo cambia ese (y los de su fase), sin apagar los que se cruzan.</summary>
        public bool freeToggle;

        /// <summary>¿Se cruzan los movimientos de dos accesos?</summary>
        public Func<Approach, Approach, bool> conflicts;
        /// <summary>¿Están libres los conectores de los accesos que se cruzan con este?</summary>
        public Func<Approach, bool> isClear;
        /// <summary>Fase compartida de cada acceso (0 = independiente). Los de la misma fase se cambian juntos.</summary>
        public Func<Approach, int> groupOf;
        /// <summary>«Sensor activado»: en el ciclo automático se saltan las fases sin nadie esperando.</summary>
        public bool sensor;
        public Func<Approach, bool> hasDemand;

        int autoPhase;
        float autoTimer;

        public int Count { get { return order.Count; } }
        public int AutoPhase { get { return autoPhase; } }

        /// <summary>Todos los semáforos de este grupo a rojo (y se para el ciclo automático).</summary>
        public void AllRed()
        {
            autoCycle = false;
            for (int i = 0; i < order.Count; i++) RequestRed(order[i]);
        }

        /// <summary>Empieza el ciclo automático desde la primera etapa.</summary>
        public void RestartCycle()
        {
            autoPhase = phases.Count > 0 ? phases.Count - 1 : 0;
            autoTimer = 1e6f;
        }
        public bool Has(Approach a) { return a != null && lights.ContainsKey(a); }
        public IList<Approach> Approaches { get { return order; } }

        public void Register(Approach a)
        {
            if (a == null || lights.ContainsKey(a)) return;
            lights[a] = new Light();
            order.Add(a);
        }

        /// <summary>Estado inicial: la primera fase en verde (o todos los accesos que no se cruzan) y el resto en rojo.</summary>
        public void InitStart()
        {
            autoPhase = 0;
            autoTimer = 0f;
            var greens = new List<Approach>();
            if (phases.Count > 0)
            {
                foreach (var a in order) if (phases[0].approaches.Contains(a)) greens.Add(a);
            }
            else
            {
                foreach (var a in order)
                {
                    bool clash = false;
                    foreach (var g in greens) if (conflicts != null && conflicts(a, g)) { clash = true; break; }
                    if (!clash) greens.Add(a);
                }
            }
            foreach (var a in order)
            {
                var l = lights[a];
                l.state = greens.Contains(a) ? SignalState.Green : SignalState.Red;
                l.wantGreen = false;
                l.timer = 0f;
            }
        }

        public SignalState GetState(Approach a)
        {
            Light l;
            if (a == null || !lights.TryGetValue(a, out l)) return SignalState.Green;
            return l.state;
        }

        /// <summary>true si el semáforo está (o se dispone a estar) en verde.</summary>
        public bool IsOn(Approach a)
        {
            Light l;
            if (!lights.TryGetValue(a, out l)) return true;
            return l.state == SignalState.Green || l.wantGreen;
        }

        /// <summary>Cambia un semáforo: verde → amarillo → rojo, o rojo → (despeja los que se cruzan) → verde.</summary>
        public void Toggle(Approach a)
        {
            if (!lights.ContainsKey(a)) return;
            bool on = IsOn(a);
            int g = groupOf != null ? groupOf(a) : 0;
            if (freeToggle)
            {
                for (int i = 0; i < order.Count; i++)
                {
                    var b = order[i];
                    if (b != a && (g <= 0 || groupOf(b) != g)) continue;
                    if (on) RequestRed(b);
                    else { var lb = lights[b]; lb.wantGreen = true; lb.forced = true; if (lb.state == SignalState.Green) lb.wantGreen = false; }
                }
                return;
            }
            if (g <= 0) { if (on) RequestRed(a); else RequestGreen(a); return; }
            // Fase compartida: todos los semáforos del grupo cambian a la vez; los demás no se tocan salvo los que se cruzan (modo seguro).
            for (int i = 0; i < order.Count; i++)
            {
                var b = order[i];
                if (groupOf(b) != g) continue;
                if (on) RequestRed(b); else RequestGreenInGroup(b, g);
            }
        }

        /// <summary>Como RequestGreen, pero no pone en rojo a los miembros de su propia fase.</summary>
        void RequestGreenInGroup(Approach a, int g)
        {
            Light l;
            if (!lights.TryGetValue(a, out l)) return;
            l.wantGreen = true;
            if (l.state == SignalState.Green) { l.wantGreen = false; return; }
            if (l.state == SignalState.Red) l.timer = 0f;
            if (safeMode && conflicts != null)
                for (int i = 0; i < order.Count; i++)
                {
                    var b = order[i];
                    if (b != a && groupOf(b) != g && conflicts(a, b)) RequestRed(b);
                }
        }

        public void RequestRed(Approach a)
        {
            Light l;
            if (!lights.TryGetValue(a, out l)) return;
            l.wantGreen = false;
            l.forced = false;
            if (l.state == SignalState.Green) { l.state = SignalState.Yellow; l.timer = 0f; }
        }

        public void RequestGreen(Approach a)
        {
            Light l;
            if (!lights.TryGetValue(a, out l)) return;
            l.wantGreen = true;
            if (l.state == SignalState.Green) { l.wantGreen = false; return; }
            if (l.state == SignalState.Red) l.timer = 0f;
            if (safeMode && conflicts != null)
            {
                for (int i = 0; i < order.Count; i++)
                {
                    var b = order[i];
                    if (b != a && conflicts(a, b)) RequestRed(b);
                }
            }
        }

        static bool IsPedApp(Approach a)
        {
            if (a == null || a.inLanes.Count == 0) return false;
            foreach (var l in a.inLanes) if (l.laneType != IntersectionMaster.Data.RoadData.LaneSidewalk) return false;
            return true;
        }

        public void Update(float dt)
        {
            if (order.Count == 0) return;

            if (autoCycle && phases.Count > 1)
            {
                autoTimer += dt;
                float phaseGreen = phases[autoPhase].green > 0.1f ? phases[autoPhase].green : greenSeconds;
                if (autoTimer >= phaseGreen)
                {
                    autoTimer = 0f;
                    int nextPhase = (autoPhase + 1) % phases.Count;
                    if (sensor && hasDemand != null)
                    {
                        nextPhase = -1;
                        for (int k = 1; k <= phases.Count && nextPhase < 0; k++)
                        {
                            int cand = (autoPhase + k) % phases.Count;
                            foreach (var ap in phases[cand].approaches) if (hasDemand(ap)) { nextPhase = cand; break; }
                        }
                        if (nextPhase < 0) nextPhase = autoPhase;   // nadie espera en otra fase: sigue la actual
                    }
                    bool keep = nextPhase == autoPhase && phases.Count > 1 && sensor;
                    autoPhase = nextPhase;
                    for (int i = 0; i < order.Count && !keep; i++)
                    {
                        if (phases[autoPhase].approaches.Contains(order[i]))
                        {
                            int g = groupOf != null ? groupOf(order[i]) : 0;
                            if (g > 0) RequestGreenInGroup(order[i], g); else RequestGreen(order[i]);
                        }
                        else RequestRed(order[i]);
                    }
                }
            }

            for (int i = 0; i < order.Count; i++)
            {
                var a = order[i];
                var l = lights[a];
                l.timer += dt;
                if (l.state == SignalState.Yellow)
                {
                    float y = safeMode ? yellowSeconds : Mathf.Min(yellowSeconds, 0.6f);
                    // Semáforo peatonal: el verde parpadea unos segundos antes del rojo (como en la vida real).
                    if (IsPedApp(a)) y = Mathf.Max(y, 3.5f);
                    if (l.timer >= y) { l.state = SignalState.Red; l.timer = 0f; }
                }
                else if (l.state == SignalState.Red && l.wantGreen)
                {
                    bool go;
                    if (!safeMode || l.forced) go = true;
                    else
                    {
                        go = l.timer >= allRedSeconds;
                        if (go && conflicts != null)
                        {
                            for (int k = 0; k < order.Count && go; k++)
                            {
                                var b = order[k];
                                bool sameGroup = groupOf != null && groupOf(a) > 0 && groupOf(a) == groupOf(b);
                                if (b != a && !sameGroup && conflicts(a, b) && lights[b].state != SignalState.Red) go = false;
                            }
                        }
                        if (go && isClear != null && !isClear(a) && l.timer < 8f) go = false;
                    }
                    if (go) { l.state = SignalState.Green; l.wantGreen = false; l.forced = false; l.timer = 0f; }
                }
            }
        }
    }
}
