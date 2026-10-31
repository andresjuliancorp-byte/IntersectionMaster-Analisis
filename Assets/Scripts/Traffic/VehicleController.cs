using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Core;
using IntersectionMaster.Data;
using IntersectionMaster.Systems;

namespace IntersectionMaster.Traffic
{
    public class VehicleSpec
    {
        public string label;
        public float length, width;      // unidades de mundo
        public float maxSpeed;           // unidades por segundo
        public float accel, decel;       // unidades por segundo al cuadrado
        public Color color;
        public bool emergency;
        public float turnFactor = 1f;    // multiplicador de velocidad dentro de los giros (vehículos largos giran más despacio)
        public float minGap = 0.08f;     // separación mínima parado (unidades de mundo)
        public float headway = 0.3f;     // tiempo de seguridad respecto al de delante (s)
        public float turnRadius = 0.6f;  // radio de giro mínimo aproximado (para la validación)
        public bool articulated;
    }

    /// <summary>Parámetros del modo caótico por nivel (bajo, medio, alto, extremo).</summary>
    public static class ChaosProfile
    {
        public static readonly string[] Names = { "Caos bajo", "Caos medio", "Caos alto", "Caos extremo" };
        static readonly float[] Violation_ = { 0.10f, 0.25f, 0.50f, 0.80f };
        static readonly float[] SpeedMul = { 1.10f, 1.25f, 1.40f, 1.60f };
        static readonly float[] GapMul = { 0.7f, 0.5f, 0.3f, 0.15f };

        public static int Clamp(int level) { return Mathf.Clamp(level, 0, 3); }
        public static float ViolationChance(TrafficSettings t)
        {
            return t.violationChance >= 0f ? Mathf.Clamp01(t.violationChance) : Violation_[Clamp(t.chaosLevel)];
        }
        public static float Violation(int level) { return Violation_[Clamp(level)]; }
        public static float Speed(int level) { return SpeedMul[Clamp(level)]; }
        public static float Gap(int level) { return GapMul[Clamp(level)]; }
    }

    public static class VehicleCatalog
    {
        /// <summary>Si no está vacío, el siguiente vehículo de ese tipo sale con este modelo (vehículos de servicio de los eventos).</summary>
        public static string NextLabel = "";

        static T Pick<T>(T[] items, float[] weights)
        {
            float sum = 0f;
            foreach (var w in weights) sum += w;
            float r = Random.value * sum;
            for (int i = 0; i < items.Length; i++) { r -= weights[i]; if (r <= 0f) return items[i]; }
            return items[items.Length - 1];
        }

        public static VehicleSpec Get(VehicleType t)
        {
            string forced = NextLabel;
            NextLabel = "";
            if (forced == "Pavimentadora")
            {
                var pv = Make("Pavimentadora", .9f, .34f, .5f, .4f, 1.6f, "#F4C20D", false);
                pv.turnFactor = 0.6f;
                return pv;
            }
            if (forced == "Grúa") return Make("Grúa", .78f, .30f, 1.4f, 1.0f, 2.2f, "#F0932B", false);
            switch (t)
            {
                case VehicleType.Taxi: return Make("Taxi", .48f, .26f, 1.75f, 1.5f, 2.6f, "#F6C945", false);
                case VehicleType.Bus:
                    {
                        // Autobús, articulado y trolebuses (con sus dos pértigas), como el original.
                        string k = Pick(new[] { "Autobús", "Autobús articulado", "Trolebús", "Trolebús articulado" }, new[] { 0.55f, 0.2f, 0.15f, 0.1f });
                        bool art = k.EndsWith("articulado");
                        var b = Make(k, art ? 1.32f : .92f, .32f, art ? 1.20f : 1.30f, art ? 0.85f : 1.0f, art ? 2.0f : 2.2f, k.StartsWith("Trole") ? "#D9A62E" : "#2FA39A", false);
                        if (art) { b.articulated = true; b.turnFactor = 0.6f; b.turnRadius = 1.3f; }
                        return b;
                    }
                case VehicleType.Van:
                    {
                        string k = Pick(new[] { "Furgoneta", "Microbús", "Furgón de caja" }, new[] { 0.6f, 0.2f, 0.2f });
                        if (k == "Microbús") return Make(k, .70f, .30f, 1.50f, 1.2f, 2.3f, "#DADDE2", false);
                        if (k == "Furgón de caja") return Make(k, .66f, .30f, 1.45f, 1.1f, 2.3f, "#F2F2F2", false);
                        return Make("Furgoneta", .58f, .28f, 1.60f, 1.3f, 2.4f, "#E8E8E8", false);
                    }
                case VehicleType.Truck:
                    {
                        // Camiones como los del original: rígido, de caja ligera, con remolque, tráileres largos y tractora sola.
                        string k = Pick(new[] { "Camión", "Camión de caja", "Camión con remolque", "Tráiler europeo", "Tráiler americano", "Tractora" },
                                        new[] { 0.42f, 0.16f, 0.14f, 0.12f, 0.08f, 0.08f });
                        string[] cabs = { "#B7793F", "#3A5BA9", "#C0392B", "#2E8B57", "#7F8C8D", "#E8E8E8" };
                        string cab = cabs[Random.Range(0, cabs.Length)];
                        switch (k)
                        {
                            case "Camión de caja": return Make(k, .70f, .30f, 1.30f, .9f, 2.1f, cab, false);
                            case "Camión con remolque": { var v = Make(k, 1.30f, .32f, 1.10f, .7f, 1.9f, cab, false); v.articulated = true; v.turnFactor = 0.6f; v.turnRadius = 1.2f; return v; }
                            case "Tráiler europeo": { var v = Make(k, 1.45f, .33f, 1.10f, .65f, 1.8f, cab, false); v.articulated = true; v.turnFactor = 0.55f; v.turnRadius = 1.3f; return v; }
                            case "Tráiler americano": { var v = Make(k, 1.62f, .33f, 1.10f, .6f, 1.8f, cab, false); v.articulated = true; v.turnFactor = 0.55f; v.turnRadius = 1.4f; return v; }
                            case "Tractora": return Make(k, .50f, .31f, 1.45f, 1.1f, 2.3f, cab, false);
                        }
                        return Make("Camión", .80f, .32f, 1.20f, .8f, 2.0f, "#B7793F", false);
                    }
                case VehicleType.Motorbike: return Make("Moto", .32f, .15f, 2.20f, 2.0f, 2.9f, "#C0392B", false);
                case VehicleType.Ambulance: return Make("Ambulancia", .54f, .28f, 2.20f, 1.6f, 2.9f, "#FFFFFF", true);
                case VehicleType.FireTruck: return Make("Bomberos", .80f, .30f, 1.90f, 1.3f, 2.4f, "#D62828", true);
                case VehicleType.Police: return Make("Patrulla", .48f, .26f, 2.05f, 1.6f, 2.7f, "#3A5BA9", true);
                case VehicleType.Tram:
                    {
                        // Uno o dos vagones (al azar), como el original.
                        float r = Random.value;
                        bool two = r < 0.45f, old = r > 0.75f;
                        var tm = Make(two ? "Tranvía articulado" : (old ? "Tranvía antiguo" : "Tranvía"), two ? 1.9f : (old ? 1.0f : 1.15f), .32f, old ? 1.05f : 1.25f, .7f, 1.8f, old ? "#E9DFC4" : "#E3E6EA", false);
                        tm.articulated = two; tm.turnFactor = 0.6f; tm.turnRadius = 1.4f; tm.minGap = 0.2f; tm.headway = 0.6f;
                        return tm;
                    }
                default:
                    var palette = RewardSystem.CarPalette();
                    // Compacto, sedán o grande: distintas proporciones.
                    if (Random.value < 0.05f)
                    {
                        // Coche con remolque.
                        var ct = Make("Coche con remolque", .86f, .27f, 1.50f, 1.2f, 2.3f, palette[Random.Range(0, palette.Length)], false);
                        ct.articulated = true; ct.turnFactor = 0.8f; ct.turnRadius = 0.8f;
                        return ct;
                    }
                    int size = Random.Range(0, 3);
                    float len = size == 0 ? .42f : (size == 1 ? .48f : .54f);
                    return Make(size == 0 ? "Compacto" : (size == 1 ? "Sedán" : "Auto grande"), len, size == 2 ? .28f : .26f, 1.75f, size == 0 ? 1.6f : 1.5f, 2.6f, palette[Random.Range(0, palette.Length)], false);
            }
        }

        /// <summary>Usuarios de los otros tipos de carril: 1 peatón, 2 tren, 3 bicicleta, 4 barco.</summary>
        public static VehicleSpec Agent(int agent)
        {
            switch (agent)
            {
                case 1:
                    {
                        string[] cl = { "#3A6EA5", "#C0392B", "#2E8B57", "#8E44AD", "#D35400", "#34495E", "#F1C40F" };
                        var w = Make("Peatón", .16f, .16f, .42f + Random.Range(-0.06f, 0.08f), 1.4f, 3f, cl[Random.Range(0, cl.Length)], false);
                        w.minGap = 0.04f; w.headway = 0.15f; w.turnFactor = 1f; w.turnRadius = 0.1f;
                        return w;
                    }
                case 2:
                    {
                        // Trenes de cercanías (tres épocas) y de mercancías (diésel o eléctrico), como el original.
                        string k = Pick(new[] { "Cercanías moderno", "Cercanías", "Cercanías antiguo", "Mercancías diésel", "Mercancías eléctrico" }, new[] { 0.25f, 0.2f, 0.15f, 0.22f, 0.18f });
                        string col = k == "Cercanías moderno" ? "#E3E6EA" : k == "Cercanías" ? "#C23B22" : k == "Cercanías antiguo" ? "#2E6B3F" : k == "Mercancías diésel" ? "#D9A62E" : "#3A5BA9";
                        bool freight = k.StartsWith("Mercancías");
                        var t = Make(k, freight ? 3.4f : 2.6f, .34f, freight ? 1.6f : 2.1f, freight ? .3f : .45f, freight ? .8f : 1.1f, col, false);
                        t.minGap = 0.4f; t.headway = 1f; t.turnFactor = 0.85f; t.turnRadius = 2f;
                        return t;
                    }
                case 3:
                    {
                        string[] cl = { "#1ABC9C", "#E67E22", "#2980B9", "#E74C3C", "#7F8C8D" };
                        var b = Make("Bicicleta", .3f, .12f, 1.0f, 1.1f, 2.4f, cl[Random.Range(0, cl.Length)], false);
                        b.minGap = 0.06f; b.headway = 0.25f; b.turnFactor = 1f; b.turnRadius = 0.3f;
                        return b;
                    }
                default:
                    {
                        var bt = Make("Barco", .95f, .36f, .75f, .25f, .6f, "#F4F4F4", false);
                        bt.minGap = 0.5f; bt.headway = 1.2f; bt.turnFactor = 0.8f; bt.turnRadius = 1.2f;
                        return bt;
                    }
            }
        }

        static VehicleSpec Make(string label, float len, float wid, float vmax, float acc, float dec, string hex, bool emergency)
        {
            var sp = new VehicleSpec
            {
                label = label, length = len, width = wid, maxSpeed = vmax, accel = acc, decel = dec,
                color = SpriteFactory.Hex(hex), emergency = emergency
            };
            // Perfil de conducción según el tamaño: los largos giran más despacio y dejan más hueco; las motos al revés.
            if (len >= 0.75f) { sp.turnFactor = 0.7f; sp.minGap = 0.13f; sp.headway = 0.45f; sp.turnRadius = 1.0f; }
            else if (len <= 0.35f) { sp.turnFactor = 1.1f; sp.minGap = 0.05f; sp.headway = 0.25f; sp.turnRadius = 0.35f; }
            return sp;
        }
    }

    /// <summary>
    /// Un vehículo: sigue una ruta de segmentos (carriles, conectores de cruce y arcos de rotonda), frena ante semáforos,
    /// señales de stop/ceda, pasos de peatones, cruces ocupados y vehículos delante (incluidos los que confluyen).
    /// La lógica es una clase normal actualizada por TrafficSimulationManager (más barato que un Update por objeto).
    /// </summary>
    public class VehicleController
    {

        public readonly int id;
        public readonly VehicleType type;
        /// <summary>0 vehículo normal; 1 peatón, 2 tren, 3 bicicleta, 4 barco (modelo por carriles).</summary>
        public readonly int agent;
        /// <summary>Está parado (o frenando) por un semáforo en rojo o ámbar: los demás no le ceden el paso.</summary>
        public bool HeldByRed;
        /// <summary>Diagnóstico: qué le hizo frenar por última vez (y el vehículo al que espera, o -1).</summary>
        public string why = "";
        public int whyId = -1;
        public readonly VehicleSpec spec;
        public readonly List<RoadSeg> route;
        public int routeIndex;
        public float s;
        public float speed;
        public bool crashed;
        public float crashTimer;
        public bool arrived;
        public bool reckless;
        /// <summary>Modo caótico: este vehículo ignora semáforos, stops, cesiones y restricciones, conduce más rápido y pega más al de delante.</summary>
        public bool chaotic;
        public float chaosSpeed = 1f;
        public float chaosGap = 1f;
        public float waitTime;
        public float totalWait;
        public float travelTime;
        public bool blockCounted;
        public bool honked;
        /// <summary>Segundos detenido en la línea de parada del carril actual (para desempatar prioridades).</summary>
        public float lineTimer;

        // --- Acciones del jugador y eventos ---
        /// <summary>Detenido por el jugador (Tráfico → Stop): no se mueve hasta que se vuelva a tocar.</summary>
        public bool userStopped;
        /// <summary>Segundos que sigue parado por un evento (avería, taxi recogiendo gente...).</summary>
        public float holdTimer;
        /// <summary>Apartado en la berma: los demás no lo tienen como líder (pasan despacio a su lado).</summary>
        public bool onShoulder;
        /// <summary>Obstáculo de un evento (bomberos, pavimentadora, grúa): bloquea el carril y no cuenta en las estadísticas.</summary>
        public bool obstacle;

        /// <summary>Cambia el color de la carrocería (vehículos de servicio de los eventos).</summary>
        public void Tint(Color c) { if (body != null) body.color = c; }

        /// <summary>Coloca el vehículo en la distancia ns de su carril actual.</summary>
        public void PlaceAt(float ns)
        {
            s = Mathf.Clamp(ns, HalfLength, Seg.Length - 0.01f);
            UpdatePose();
            visualAngle = Geo.Angle(dir);
            Apply();
        }
        float shoulderAmount;   // 0..1, desplazamiento visual hacia el borde
        public float shoulderSide = 1f;   // +1 derecha, -1 izquierda
        /// <summary>Colorear los vehículos según su espera (Tráfico → Paciencia).</summary>
        public static bool ShowPatience;
        SpriteRenderer stopMark, patienceHalo;
        SpriteRenderer[] tails, blinkers, blinkHalos;
        SpriteRenderer shadowSr;
        SpriteRenderer[] beams, tailGlows;   // luces de noche: haz de los faros y brillo de los pilotos
        // Peatón: piernas y brazos que se mueven al caminar y paraguas cuando llueve.
        Transform[] pedLegs, pedArms;
        SpriteRenderer umbrella, umbrellaTip;
        float odo;
        /// <summary>¿Está lloviendo? (los peatones abren el paraguas). Lo pone la lluvia del juego.</summary>
        public static bool RainNow;

        /// <summary>Tramo de carril donde todos van más despacio (eventos).</summary>
        public class SlowZone { public RoadSeg seg; public float s0, s1, factor = 0.35f; }
        public static readonly List<SlowZone> SlowZones = new List<SlowZone>();
        int blinkSide;   // -1 izquierda, 1 derecha, 0 nada
        RoadSeg blinkSeg; int blinkTurn;          // giro anunciado y el carril de giro donde se mantiene
        float laneBlinkT; int laneBlinkSide;      // intermitente tras cambiar de carril

        // --- Cambio de carril ---
        public float blockedTime;      // segundos seguidos frenado por un líder lento en el mismo carril
        public float laneCooldown;
        Vector2 shiftVec;              // desplazamiento visual que se reduce tras cambiar de carril
        SpriteRenderer[] renderers;
        int[] baseOrders;
        int appliedLayer;

        readonly List<StopPoint> served = new List<StopPoint>();
        float dwell;

        bool commitGo;
        bool stopDone;
        bool braking;
        float brakeHold;
        Vector2 pos;
        Vector2 dir = Vector2.right;
        float visualAngle;
        readonly GameObject go;
        readonly Transform tr;
        readonly SpriteRenderer body;
        readonly SpriteRenderer beacon;

        public RoadSeg Seg { get { return route[routeIndex]; } }
        /// <summary>Capa en la que va ahora (en una rampa cambia a mitad de camino).</summary>
        public int Layer { get { return Seg.LayerAt(s); } }

        /// <summary>Desplazamiento de orden de dibujo por capa: los túneles (capas negativas) quedan bajo el suelo.</summary>
        public static int LayerOrderOffset(int layer) { return layer >= 0 ? 10 * layer : 25 * layer; }
        public RoadSeg NextSeg { get { return routeIndex + 1 < route.Count ? route[routeIndex + 1] : null; } }
        public float Length { get { return spec.length; } }
        public float HalfLength { get { return spec.length * 0.5f; } }
        public float Radius { get { return spec.width * 0.5f; } }
        public Vector2 Pos { get { return pos; } }
        public Vector2 VisualPos { get { return pos; } }
        public Vector2 Dir { get { return dir; } }

        public VehicleController(int id, VehicleType type, List<RoadSeg> route, Transform parent, int agent = 0)
        {
            this.id = id;
            this.type = type;
            this.agent = agent;
            this.spec = agent == 0 ? VehicleCatalog.Get(type) : VehicleCatalog.Agent(agent);
            this.route = route;
            s = HalfLength + 0.02f;
            UpdatePose();
            visualAngle = Geo.Angle(dir);

            go = new GameObject("Vehicle_" + id + "_" + spec.label);
            go.transform.SetParent(parent, false);
            tr = go.transform;
            if (agent != 0)
            {
                body = MakeAgentVisual();
                beacon = null;
                renderers = go.GetComponentsInChildren<SpriteRenderer>();
                baseOrders = new int[renderers.Length];
                for (int i = 0; i < renderers.Length; i++) baseOrders[i] = renderers[i].sortingOrder;
                Seg.vehicles.Add(this);
                Apply();
                return;
            }
            // Como el original: sombra hacia la izquierda de la pantalla (se recoloca en Apply al girar) y colores algo
            // más apagados.
            shadowSr = SpriteFactory.Make("Shadow", tr, SpriteFactory.Square, new Color(0, 0, 0, 0.3f), Vector2.zero,
                new Vector2(spec.length * 0.98f, spec.width * 1.02f), 19);
            var paint = Color.Lerp(spec.color, new Color(0.55f, 0.56f, 0.58f), spec.emergency ? 0.08f : 0.28f);
            bool drawn = type != VehicleType.Motorbike && type != VehicleType.Tram;
            if (drawn)
            {
                // Carrocería dibujada como el original (parabrisas, parales, ventanas, luneta y espejos); sin ruedas a la vista.
                var model = CarArt.ModelFor(type, spec.label);
                body = SpriteFactory.Make("Body", tr, CarArt.Get(model, spec.length, spec.width), paint, Vector2.zero,
                    new Vector2(spec.length, spec.length), 20);
                if (type == VehicleType.Taxi)
                    SpriteFactory.Make("TaxiSign", tr, SpriteFactory.Square, SpriteFactory.Hex("#2B2B2B"), new Vector2(-spec.length * 0.04f, 0f), new Vector2(spec.length * 0.06f, spec.width * 0.42f), 21);
            }
            else
            {
                body = SpriteFactory.Make("Body", tr, SpriteFactory.Square, paint, Vector2.zero, new Vector2(spec.length, spec.width), 20);
                SpriteFactory.Make("Roof", tr, SpriteFactory.Square, Color.Lerp(paint, Color.white, 0.18f),
                    new Vector2(-spec.length * 0.08f, 0f), new Vector2(spec.length * 0.36f, spec.width * 0.84f), 21);
            }
            var tyre = new Color(0.07f, 0.07f, 0.08f, 1f);
            if (type == VehicleType.Motorbike)
            {
                SpriteFactory.Make("WheelF", tr, SpriteFactory.Square, tyre, new Vector2(spec.length * 0.42f, 0f), new Vector2(spec.length * 0.2f, spec.width * 0.35f), 19);
                SpriteFactory.Make("WheelR", tr, SpriteFactory.Square, tyre, new Vector2(-spec.length * 0.42f, 0f), new Vector2(spec.length * 0.2f, spec.width * 0.35f), 19);
                SpriteFactory.Make("Rider", tr, SpriteFactory.Circle, new Color(0.15f, 0.15f, 0.18f, 1f), new Vector2(-spec.length * 0.05f, 0f), Vector2.one * spec.width * 0.75f, 22);
            }
            var head = new Color(1f, 0.97f, 0.85f, 1f);
            var tail = new Color(0.85f, 0.1f, 0.1f, 1f);
            float ly = spec.width * 0.34f;   // faros y pilotos en las esquinas, como el original
            Vector2 lsz = new Vector2(spec.length * 0.03f + 0.008f, spec.width * 0.16f);
            SpriteFactory.Make("HeadL", tr, SpriteFactory.Square, head, new Vector2(spec.length * 0.49f, ly), lsz, 22);
            if (type != VehicleType.Motorbike) SpriteFactory.Make("HeadR", tr, SpriteFactory.Square, head, new Vector2(spec.length * 0.49f, -ly), lsz, 22);
            var tl0 = SpriteFactory.Make("TailL", tr, SpriteFactory.Square, tail, new Vector2(-spec.length * 0.49f, ly), lsz, 22);
            if (type != VehicleType.Motorbike)
            {
                var tr0 = SpriteFactory.Make("TailR", tr, SpriteFactory.Square, tail, new Vector2(-spec.length * 0.49f, -ly), lsz, 22);
                tails = new[] { tl0, tr0 };
                // Placa / luz blanca en el centro de atrás, como el original.
                SpriteFactory.Make("Plate", tr, SpriteFactory.Square, new Color(0.85f, 0.86f, 0.88f, 1f), new Vector2(-spec.length * 0.495f, 0f), new Vector2(spec.length * 0.02f + 0.004f, spec.width * 0.2f), 22);
            }
            else tails = new[] { tl0 };
            // De noche: haz de los faros sobre el asfalto y brillo de los pilotos (por encima de la capa de noche).
            if (type != VehicleType.Tram)
            {
                // Como en la vida real: un haz por cada faro (dos en carros, uno en motos), algo abiertos hacia fuera, y
                // detrás los pilotos con un brillo rojo pequeño (fuerte solo al frenar).
                int n = type == VehicleType.Motorbike ? 1 : 2;
                beams = new SpriteRenderer[n];
                tailGlows = new SpriteRenderer[n];
                float bl = Mathf.Clamp(spec.length * 1.4f, 0.75f, 1.2f), bw = type == VehicleType.Motorbike ? 0.36f : spec.width * 1.35f;
                for (int bi = 0; bi < n; bi++)
                {
                    float yy = n == 1 ? 0f : (bi == 0 ? ly : -ly);
                    float ang = n == 1 ? 0f : (bi == 0 ? 5f : -5f);
                    beams[bi] = SpriteFactory.Make("HeadBeam", tr, SpriteFactory.Cone, new Color(1f, 0.97f, 0.88f, 0f), new Vector2(spec.length * 0.49f, yy), new Vector2(bl, bw), 36, ang);
                    tailGlows[bi] = SpriteFactory.Make("TailGlow", tr, SpriteFactory.Soft, new Color(1f, 0.1f, 0.08f, 0f), new Vector2(-spec.length * 0.5f, yy), Vector2.one * 0.13f, 36);
                }
            }
            // Intermitentes (apagados hasta que gira): delante y detrás de cada lado.
            if (type != VehicleType.Motorbike && type != VehicleType.Tram)
            {
                var amber = new Color(1f, 0.65f, 0.1f, 0f);
                Vector2 bsz = new Vector2(spec.length * 0.04f + 0.008f, spec.width * 0.12f);
                blinkers = new[]
                {
                    SpriteFactory.Make("BlinkFL", tr, SpriteFactory.Square, amber, new Vector2(spec.length * 0.48f, spec.width * 0.45f), bsz, 23),
                    SpriteFactory.Make("BlinkRL", tr, SpriteFactory.Square, amber, new Vector2(-spec.length * 0.48f, spec.width * 0.45f), bsz, 23),
                    SpriteFactory.Make("BlinkFR", tr, SpriteFactory.Square, amber, new Vector2(spec.length * 0.48f, -spec.width * 0.45f), bsz, 23),
                    SpriteFactory.Make("BlinkRR", tr, SpriteFactory.Square, amber, new Vector2(-spec.length * 0.48f, -spec.width * 0.45f), bsz, 23)
                };
                // Brillo alrededor de cada intermitente cuando se enciende.
                blinkHalos = new SpriteRenderer[4];
                for (int bi = 0; bi < 4; bi++)
                    blinkHalos[bi] = SpriteFactory.Make("BlinkGlow", tr, SpriteFactory.Soft, amber, blinkers[bi].transform.localPosition, Vector2.one * spec.width * 0.55f, 24);
            }
            if (type == VehicleType.Tram)
            {
                // Franja de color, ventanas a lo largo y pantógrafo en el techo.
                var stripe = SpriteFactory.Hex("#2E7DD7");
                SpriteFactory.Make("TramStripeL", tr, SpriteFactory.Square, stripe, new Vector2(0f, spec.width * 0.44f), new Vector2(spec.length * 0.96f, spec.width * 0.1f), 21);
                SpriteFactory.Make("TramStripeR", tr, SpriteFactory.Square, stripe, new Vector2(0f, -spec.width * 0.44f), new Vector2(spec.length * 0.96f, spec.width * 0.1f), 21);
                int wins = Mathf.RoundToInt(spec.length / 0.16f);
                for (int wi = 0; wi < wins; wi++)
                {
                    float x = -spec.length * 0.44f + wi * spec.length * 0.88f / Mathf.Max(1, wins - 1);
                    SpriteFactory.Make("Win", tr, SpriteFactory.Square, new Color(0.12f, 0.18f, 0.25f, 0.85f), new Vector2(x, 0f), new Vector2(spec.length * 0.6f / wins, spec.width * 0.55f), 21);
                }
                SpriteFactory.Make("Pantograph", tr, SpriteFactory.Square, new Color(0.2f, 0.2f, 0.22f, 1f), new Vector2(spec.length * 0.15f, 0f), new Vector2(0.03f, spec.width * 0.7f), 23);
            }
            if (spec.articulated)
            {
                // Fuelle de la articulación.
                SpriteFactory.Make("Joint", tr, SpriteFactory.Square, new Color(0.15f, 0.15f, 0.17f, 1f), new Vector2(-spec.length * 0.05f, 0f), new Vector2(spec.length * 0.05f, spec.width * 0.96f), 22);
            }
            if (type == VehicleType.Truck)
            {
                string lb = spec.label;
                if (lb == "Tractora")
                    // Tractora sola: la quinta rueda (plato) a la vista detrás de la cabina.
                    SpriteFactory.Make("FifthWheel", tr, SpriteFactory.Circle, new Color(0.12f, 0.12f, 0.13f, 1f), new Vector2(-spec.length * 0.22f, 0f), Vector2.one * spec.width * 0.6f, 21);
                else if (lb == "Pavimentadora")
                {
                    // Pavimentadora: tolva delante y rodillo detrás.
                    SpriteFactory.Make("Hopper", tr, SpriteFactory.Square, new Color(0.18f, 0.17f, 0.16f, 1f), new Vector2(spec.length * 0.3f, 0f), new Vector2(spec.length * 0.3f, spec.width * 0.9f), 21);
                    SpriteFactory.Make("Roller", tr, SpriteFactory.Square, new Color(0.35f, 0.35f, 0.37f, 1f), new Vector2(-spec.length * 0.42f, 0f), new Vector2(spec.length * 0.14f, spec.width * 1.02f), 21);
                }
                else if (lb == "Grúa")
                    SpriteFactory.Make("Boom", tr, SpriteFactory.Square, new Color(0.2f, 0.2f, 0.22f, 1f), new Vector2(-spec.length * 0.2f, 0f), new Vector2(spec.length * 0.55f, 0.03f), 22);
                else
                {
                    // Caja de carga más clara separada de la cabina (en los tráileres, un semirremolque largo).
                    bool semi = lb.StartsWith("Tráiler") || lb == "Camión con remolque";
                    float cabL = lb == "Tráiler americano" ? 0.26f : (semi ? 0.2f : 0.3f);
                    float boxL = 1f - cabL - 0.03f;
                    Color box = Color.Lerp(spec.color, Color.white, 0.55f);
                    if (lb == "Camión con remolque")
                    {
                        // Caja sobre el camión y otra en el remolque, con la lanza entre las dos.
                        SpriteFactory.Make("Cargo", tr, SpriteFactory.Square, box, new Vector2(spec.length * 0.12f, 0f), new Vector2(spec.length * 0.32f, spec.width * 0.94f), 21);
                        SpriteFactory.Make("Cargo2", tr, SpriteFactory.Square, box, new Vector2(-spec.length * 0.3f, 0f), new Vector2(spec.length * 0.38f, spec.width * 0.94f), 21);
                    }
                    else
                        SpriteFactory.Make("Cargo", tr, SpriteFactory.Square, box, new Vector2(spec.length * (0.5f - cabL - 0.03f - boxL * 0.5f), 0f), new Vector2(spec.length * boxL, spec.width * 0.94f), 21);
                }
            }
            if (type == VehicleType.Van && spec.label == "Furgón de caja")
                SpriteFactory.Make("Cargo", tr, SpriteFactory.Square, Color.Lerp(spec.color, Color.white, 0.3f), new Vector2(-spec.length * 0.12f, 0f), new Vector2(spec.length * 0.7f, spec.width * 0.96f), 21);
            if (type == VehicleType.Van && spec.label == "Microbús")
                for (int wi = 0; wi < 4; wi++)
                    SpriteFactory.Make("Win", tr, SpriteFactory.Square, new Color(0.12f, 0.18f, 0.25f, 0.8f), new Vector2(spec.length * (0.18f - wi * 0.17f), 0f), new Vector2(spec.length * 0.12f, spec.width * 0.7f), 21);
            if (type == VehicleType.Bus && spec.label.StartsWith("Trole"))
                // Dos pértigas hacia atrás para tomar la corriente de los cables.
                for (int k = 0; k < 2; k++)
                    SpriteFactory.Make("Pole", tr, SpriteFactory.Square, new Color(0.12f, 0.12f, 0.13f, 1f), new Vector2(-spec.length * 0.12f, (k == 0 ? 1f : -1f) * spec.width * 0.15f), new Vector2(spec.length * 0.55f, 0.018f), 23);
            if (spec.label == "Coche con remolque")
            {
                SpriteFactory.Make("Trailer", tr, SpriteFactory.Square, new Color(0.62f, 0.62f, 0.6f, 1f), new Vector2(-spec.length * 0.3f, 0f), new Vector2(spec.length * 0.36f, spec.width * 0.95f), 21);
                SpriteFactory.Make("Hitch", tr, SpriteFactory.Square, new Color(0.15f, 0.15f, 0.16f, 1f), new Vector2(-spec.length * 0.08f, 0f), new Vector2(spec.length * 0.08f, 0.02f), 21);
            }
            if (type == VehicleType.Tram && spec.label == "Tranvía antiguo")
                SpriteFactory.Make("OldRoof", tr, SpriteFactory.Square, SpriteFactory.Hex("#B03A2E"), Vector2.zero, new Vector2(spec.length * 0.9f, spec.width * 0.3f), 22);
            if (type == VehicleType.Ambulance)
                SpriteFactory.Make("Cross", tr, SpriteFactory.Square, SpriteFactory.Hex("#D62828"),
                    new Vector2(-spec.length * 0.12f, 0f), new Vector2(spec.length * 0.16f, spec.width * 0.5f), 21);
            if (spec.emergency)
                beacon = SpriteFactory.Make("Beacon", tr, SpriteFactory.Circle, Color.red, new Vector2(-spec.length * 0.05f, 0f),
                    new Vector2(spec.width * 0.55f, spec.width * 0.55f), 22);

            // Como el original: el coche se ve algo más pequeño y fino que su hueco en el carril.
            tr.localScale = new Vector3(0.9f, 0.82f, 1f);
            renderers = go.GetComponentsInChildren<SpriteRenderer>();
            baseOrders = new int[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) baseOrders[i] = renderers[i].sortingOrder;
            Seg.vehicles.Add(this);
            Apply();
        }

        /// <summary>Dibujo de peatones, trenes, bicicletas y barcos.</summary>
        SpriteRenderer MakeAgentVisual()
        {
            float L = spec.length, W = spec.width;
            var c = spec.color;
            switch (agent)
            {
                case 1:
                    {
                        // Persona vista desde arriba (como el original): hombros con la ropa, cabeza con el pelo, piernas
                        // y brazos que se balancean al caminar; con lluvia, un paraguas abierto encima.
                        string[] hair = { "#2B1D14", "#4A3020", "#1A1A1A", "#8A6A3C", "#5B3A22" };
                        string[] skin = { "#E8BE9A", "#C68E65", "#8D5A3B", "#F1CDB0" };
                        string[] pants = { "#2E3440", "#3B4A6B", "#4A4038", "#222222" };
                        string[] umb = { "#2B2B2E", "#C0392B", "#2E86C1", "#F1C40F", "#27AE60", "#8E44AD" };
                        Color sk = SpriteFactory.Hex(skin[UnityEngine.Random.Range(0, skin.Length)]);
                        Color pt = SpriteFactory.Hex(pants[UnityEngine.Random.Range(0, pants.Length)]);
                        SpriteFactory.Make("Shadow", tr, SpriteFactory.Circle, new Color(0, 0, 0, 0.22f), new Vector2(-0.015f, -0.02f), new Vector2(L * 0.8f, W * 1.05f), 19);
                        pedLegs = new Transform[2]; pedArms = new Transform[2];
                        for (int k = 0; k < 2; k++)
                        {
                            float y = (k == 0 ? 1f : -1f) * W * 0.17f;
                            pedLegs[k] = SpriteFactory.Make("Leg", tr, SpriteFactory.Circle, pt, new Vector2(0f, y), new Vector2(L * 0.34f, W * 0.2f), 19).transform;
                            pedArms[k] = SpriteFactory.Make("Arm", tr, SpriteFactory.Circle, Color.Lerp(c, sk, 0.35f), new Vector2(0f, y * 2.7f), new Vector2(L * 0.3f, W * 0.16f), 20).transform;
                        }
                        var b = SpriteFactory.Make("Body", tr, SpriteFactory.Circle, c, Vector2.zero, new Vector2(L * 0.42f, W * 0.92f), 21);
                        SpriteFactory.Make("Head", tr, SpriteFactory.Circle, SpriteFactory.Hex(hair[UnityEngine.Random.Range(0, hair.Length)]), new Vector2(0.006f, 0f), Vector2.one * W * 0.44f, 22);
                        umbrella = SpriteFactory.Make("Umbrella", tr, SpriteFactory.Circle, SpriteFactory.Hex(umb[UnityEngine.Random.Range(0, umb.Length)]), new Vector2(0.01f, 0f), Vector2.one * W * 1.55f, 23);
                        umbrellaTip = SpriteFactory.Make("UmbrellaTip", tr, SpriteFactory.Circle, new Color(1f, 1f, 1f, 0.35f), new Vector2(0.01f, 0f), Vector2.one * W * 0.22f, 24);
                        umbrella.enabled = false; umbrellaTip.enabled = false;
                        return b;
                    }
                case 2:
                    {
                        SpriteFactory.Make("Shadow", tr, SpriteFactory.Square, new Color(0, 0, 0, 0.3f), new Vector2(-0.02f, -0.05f), new Vector2(L * 1.02f, W * 1.08f), 19);
                        var b = SpriteFactory.Make("Body", tr, SpriteFactory.Square, Color.Lerp(c, Color.black, 0.2f), Vector2.zero, new Vector2(L, W), 20);
                        bool freight = spec.label.StartsWith("Mercancías");
                        int cars = freight ? 6 : 3;
                        float carL = L / cars;
                        string[] wagon = { "#7A4E2D", "#5D6D7E", "#8E3B2F", "#4A6741", "#B9770E" };
                        for (int k = 0; k < cars; k++)
                        {
                            float x = -L * 0.5f + carL * (k + 0.5f);
                            // Mercancías: la locomotora delante y vagones de colores (contenedores, tolvas…).
                            Color cc = freight && k < cars - 1 ? SpriteFactory.Hex(wagon[(k + id) % wagon.Length]) : c;
                            SpriteFactory.Make("Car", tr, SpriteFactory.Square, cc, new Vector2(x, 0f), new Vector2(carL - 0.05f, W * 0.9f), 21);
                            if (!freight || k == cars - 1)
                                SpriteFactory.Make("Roof", tr, SpriteFactory.Square, Color.Lerp(cc, Color.white, 0.25f), new Vector2(x, 0f), new Vector2(carL * 0.7f, W * 0.5f), 22);
                            else
                                for (int r = 0; r < 2; r++)
                                    SpriteFactory.Make("Rib", tr, SpriteFactory.Square, Color.Lerp(cc, Color.black, 0.3f), new Vector2(x + (r - 0.5f) * carL * 0.35f, 0f), new Vector2(0.02f, W * 0.86f), 22);
                        }
                        if (spec.label == "Mercancías eléctrico" || spec.label.StartsWith("Cercanías"))
                            SpriteFactory.Make("Pantograph", tr, SpriteFactory.Square, new Color(0.15f, 0.15f, 0.17f, 1f), new Vector2(L * 0.38f, 0f), new Vector2(0.03f, W * 0.7f), 23);
                        SpriteFactory.Make("Front", tr, SpriteFactory.Square, new Color(0.1f, 0.12f, 0.16f, 0.9f), new Vector2(L * 0.46f, 0f), new Vector2(L * 0.04f, W * 0.8f), 22);
                        return b;
                    }
                case 3:
                    {
                        var tyre = new Color(0.08f, 0.08f, 0.09f, 1f);
                        SpriteFactory.Make("WheelF", tr, SpriteFactory.Square, tyre, new Vector2(L * 0.34f, 0f), new Vector2(L * 0.3f, W * 0.3f), 19);
                        SpriteFactory.Make("WheelR", tr, SpriteFactory.Square, tyre, new Vector2(-L * 0.34f, 0f), new Vector2(L * 0.3f, W * 0.3f), 19);
                        var b = SpriteFactory.Make("Body", tr, SpriteFactory.Square, new Color(0.25f, 0.25f, 0.28f, 1f), Vector2.zero, new Vector2(L * 0.7f, W * 0.25f), 20);
                        SpriteFactory.Make("Rider", tr, SpriteFactory.Circle, c, new Vector2(-L * 0.05f, 0f), new Vector2(L * 0.42f, W * 1.1f), 21);
                        SpriteFactory.Make("Head", tr, SpriteFactory.Circle, new Color(0.35f, 0.24f, 0.18f, 1f), new Vector2(L * 0.04f, 0f), Vector2.one * W * 0.55f, 22);
                        return b;
                    }
                default:
                    {
                        SpriteFactory.Make("Wake", tr, SpriteFactory.Triangle, new Color(1f, 1f, 1f, 0.35f), new Vector2(-L * 0.62f, 0f), new Vector2(L * 0.5f, W * 1.4f), 19, 90f);
                        var b = SpriteFactory.Make("Hull", tr, SpriteFactory.Square, c, Vector2.zero, new Vector2(L * 0.82f, W), 20);
                        SpriteFactory.Make("Bow", tr, SpriteFactory.Triangle, c, new Vector2(L * 0.45f, 0f), new Vector2(W, L * 0.2f), 20, -90f);
                        SpriteFactory.Make("Cabin", tr, SpriteFactory.Square, new Color(0.25f, 0.42f, 0.62f, 1f), new Vector2(-L * 0.1f, 0f), new Vector2(L * 0.3f, W * 0.6f), 21);
                        return b;
                    }
            }
        }

        void UpdatePose()
        {
            var seg = Seg;
            float ss = Mathf.Clamp(s, 0f, seg.Length);
            pos = seg.path.PointAt(ss);
            dir = seg.path.TangentAt(ss);
        }

        public void Tick(float dt, float speedFactor)
        {
            travelTime += dt;
            if (crashed)
            {
                crashTimer -= dt;
                speed = 0f;
                return;
            }

            RoadSeg seg = Seg;
            RoadSeg next = NextSeg;
            float toEnd = seg.Length - s;

            float cap = seg.speedCap;
            if (next != null && toEnd < 1.0f) cap = Mathf.Min(cap, next.speedCap);
            if (seg.kind == SegKind.Connector && seg.move != 0) cap *= spec.turnFactor;
            else if (next != null && next.kind == SegKind.Connector && next.move != 0 && toEnd < 1.2f) cap = Mathf.Min(cap, next.speedCap * spec.turnFactor);
            float target = spec.maxSpeed * speedFactor * cap * (chaotic ? chaosSpeed : 1f);
            // Zonas lentas de los eventos (agua de una boca de incendios rota…).
            for (int zi = 0; zi < SlowZones.Count; zi++)
            {
                var z = SlowZones[zi];
                if (z.seg == seg && s > z.s0 - 0.6f && s < z.s1) target = Mathf.Min(target, spec.maxSpeed * z.factor);
            }

            // --- Vehículo líder: en este segmento, en los siguientes, o uno que confluya delante de mí ---
            float gap = float.MaxValue;
            float leaderSpeed = 0f;
            int leaderId = -1;
            why = ""; whyId = -1;
            var list = seg.vehicles;
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                if (o == this || o.onShoulder) continue;
                bool ahead = o.s > s || (o.s == s && o.id < id);
                if (!ahead) continue;
                float g = o.s - o.HalfLength - (s + HalfLength);
                if (g < gap) { gap = g; leaderSpeed = o.speed; leaderId = o.id; }
            }
            float sameGap = gap, sameLeaderSpeed = leaderSpeed;
            if (gap == float.MaxValue)
            {
                float dist = toEnd - HalfLength;
                for (int hop = 0; hop < 3; hop++)
                {
                    int ni = routeIndex + 1 + hop;
                    if (ni >= route.Count) break;
                    var nx = route[ni];
                    for (int i = 0; i < nx.vehicles.Count; i++)
                    {
                        var o = nx.vehicles[i];
                        if (o.onShoulder) continue;
                        float g = dist + o.s - o.HalfLength;
                        if (g < gap) { gap = g; leaderSpeed = o.speed; leaderId = o.id; }
                    }
                    if (gap != float.MaxValue) break;
                    dist += nx.Length;
                    if (dist > 4f) break;
                }
            }
            if (next != null && next.ins.Count > 1)
            {
                // Confluencia (cremallera): el que está más lejos del punto de unión cede al que está más cerca.
                // En el modelo por carriles, un carril con ceda/stop al final cede siempre al que no lo tiene.
                int myR = seg.laneMode ? seg.endRank : 2;
                for (int pi = 0; pi < next.ins.Count; pi++)
                {
                    var p = next.ins[pi];
                    if (p == seg) continue;
                    int pR = seg.laneMode ? p.endRank : 2;
                    for (int i = 0; i < p.vehicles.Count; i++)
                    {
                        var w = p.vehicles[i];
                        if (w.HeldByRed || w.crashed) continue;
                        float wEnd = p.Length - w.s;
                        if (wEnd > 3f) continue;
                        float glLine = toEnd - HalfLength - 0.03f;
                        if (pR > myR && !chaotic && glLine > -0.04f)
                        {
                            // Tiene prioridad: espero en la línea si llega pronto (si ya estoy en la línea, sigo).
                            float wt = (wEnd - w.HalfLength) / Mathf.Max(w.speed, 0.25f);
                            if (wt > 2.5f && wEnd > 1f) continue;
                            if (glLine < gap) { gap = glLine; leaderSpeed = 0f; leaderId = w.id; }
                            continue;
                        }
                        if (pR < myR && wEnd - w.HalfLength > -0.01f) continue;   // él me cede el paso (aún no ha pasado la línea)
                        bool ahead = wEnd < toEnd - 0.02f || (Mathf.Abs(wEnd - toEnd) <= 0.02f && w.id < id);
                        if (!ahead) continue;
                        float g = (toEnd - wEnd) - w.HalfLength - HalfLength;
                        if (g < gap) { gap = g; leaderSpeed = w.speed; leaderId = w.id; }
                    }
                }
            }
            if (gap != float.MaxValue)
            {
                float usable = gap - (chaotic ? spec.minGap * chaosGap : spec.minGap);
                float vs = 0f;
                if (usable > 0f)
                {
                    float a = spec.decel;
                    float hw = chaotic ? spec.headway * chaosGap : spec.headway;
                    vs = -a * hw + Mathf.Sqrt(a * a * hw * hw + leaderSpeed * leaderSpeed + 2f * a * usable);
                }
                if (vs < target) { why = "sigue"; whyId = leaderId; }
                target = Mathf.Min(target, vs);
            }

            // --- Pasos de peatones con gente en la calzada ---
            for (int k = 0; k < seg.crosswalks.Count; k++)
            {
                var entry = seg.crosswalks[k];
                if (!entry.cw.busy) continue;
                float d = entry.limitFrontS - (s + HalfLength);
                if (d > -0.05f) target = Mathf.Min(target, Mathf.Sqrt(2f * spec.decel * Mathf.Max(d, 0f)));
            }

            // --- Paradas de autobús y peajes: se detiene en el punto y espera los segundos indicados ---
            for (int k = 0; k < seg.stops.Count; k++)
            {
                var sp = seg.stops[k];
                if (sp.kind == PointKind.Detector || served.Contains(sp)) continue;
                if (sp.kind == PointKind.BusStop && type != VehicleType.Bus) continue;
                if (sp.kind == PointKind.Toll && spec.emergency) continue;
                float d = sp.s - (s + HalfLength);
                if (d < -0.4f) continue;
                target = Mathf.Min(target, Mathf.Sqrt(2f * spec.decel * Mathf.Max(d, 0f)));
                if (d < 0.4f && speed < 0.08f)
                {
                    dwell += dt;
                    if (dwell >= sp.seconds) { served.Add(sp); dwell = 0f; }
                }
            }

            // --- Modelo por carriles: semáforo / stop / ceda en el nodo y cesión de paso donde se cruzan carriles ---
            if (seg.laneMode)
            {
                target = LaneRules(target, seg, next, dt, gap, leaderSpeed);
            }
            // --- Cruce: semáforo, stop / ceda / rotonda, cesión de paso, hueco de salida ---
            else if (seg.kind == SegKind.Lane && next != null && next.kind == SegKind.Connector)
            {
                var ic = seg.toNode.controller;
                if (ic != null)
                {
                    float dist = seg.stopS - (s + HalfLength);
                    if (dist > -0.08f)
                    {
                        Approach app = seg.toApp;
                        SignalState st = ic.GetState(app);
                        bool stop = false;
                        if (st == SignalState.Red)
                        {
                            stop = !reckless && !chaotic && !commitGo;
                        }
                        else if (st == SignalState.Yellow && !chaotic)
                        {
                            float brake = speed * speed / (2f * spec.decel);
                            if (commitGo) stop = false;
                            else if (dist >= brake) stop = true;
                            else commitGo = true;
                        }
                        else
                        {
                            commitGo = false;
                        }

                        bool atLine = speed < 0.05f && dist < 0.3f;
                        if (atLine) lineTimer += dt;

                        if (!chaotic && ic.HasGiveWay(app))
                        {
                            if (dist < 0.8f) target = Mathf.Min(target, spec.maxSpeed * 0.5f);
                            if (ic.MustStopFully(app) && !stopDone)
                            {
                                if (atLine && lineTimer > 0.8f) stopDone = true;
                                else stop = true;
                            }
                        }

                        float look = Mathf.Max(0.5f, speed * speed / (2f * spec.decel) + 0.3f);
                        if (!chaotic && !stop && dist < look && ic.MustYield(this, next)) stop = true;
                        if (!chaotic && !stop && dist < look && !ic.TargetFree(this, next)) stop = true;

                        if (stop)
                        {
                            float d = Mathf.Max(dist, 0f);
                            target = Mathf.Min(target, Mathf.Sqrt(2f * spec.decel * d));
                        }
                    }
                }
            }

            // --- Acciones del jugador y eventos: parado en el sitio ---
            if (holdTimer > 0f) holdTimer -= dt;
            if (userStopped || holdTimer > 0f) { target = 0f; why = userStopped ? "detenido" : "evento"; }
            // Pasar despacio junto a un vehículo apartado en la berma.
            for (int i = 0; i < seg.vehicles.Count; i++)
            {
                var o = seg.vehicles[i];
                if (o == this || !o.onShoulder) continue;
                float dd = o.s - s;
                if (dd > -o.Length && dd < 1.4f) target = Mathf.Min(target, spec.maxSpeed * 0.35f);
            }

            // --- Aceleración / frenado progresivos ---
            float prevSpeed = speed;
            if (speed < target) speed = Mathf.Min(target, speed + spec.accel * dt);
            else speed = Mathf.Max(target, speed - spec.decel * 2.5f * dt);
            if (speed < 0.002f) speed = 0f;
            // Luz de freno como en la vida real: solo al frenar de verdad (no al soltar un poco) o parado; se mantiene un
            // momento para que no parpadee.
            bool brakeNow = (dt > 0f && (prevSpeed - speed) / dt > spec.decel * 0.35f) || (speed < 0.05f && target < 0.05f);
            brakeHold = brakeNow ? 0.35f : Mathf.Max(0f, brakeHold - dt);
            braking = brakeHold > 0f;

            if (laneCooldown > 0f) laneCooldown -= dt;
            if (seg.kind == SegKind.Lane && sameGap < 1.4f && sameLeaderSpeed < spec.maxSpeed * 0.4f && speed < spec.maxSpeed * 0.4f)
                blockedTime += dt;
            else blockedTime = Mathf.Max(0f, blockedTime - dt * 2f);

            if (speed < 0.05f) { waitTime += dt; totalWait += dt; }
            else waitTime = 0f;

            s += speed * dt;
            odo += speed * dt;
            while (true)
            {
                seg = Seg;
                next = NextSeg;
                if (next == null)
                {
                    if (s >= seg.Length - 0.05f) { s = seg.Length; arrived = true; }
                    break;
                }
                if (s < seg.Length) break;
                seg.vehicles.Remove(this);
                s -= seg.Length;
                routeIndex++;
                commitGo = false;
                stopDone = false;
                lineTimer = 0f;
                served.Clear();
                dwell = 0f;
                Seg.vehicles.Add(this);
            }
            UpdatePose();
        }

        // ------------------------------------------------------------------ Modelo por carriles

        float LaneRules(float target, RoadSeg seg, RoadSeg next, float dt, float gap, float leaderSpeed)
        {
            // Peatón cruzando cuando su semáforo ya parpadea o se puso en rojo: camina más rápido para terminar de cruzar.
            if (agent == 1 && seg.laneType == RoadData.LaneSidewalk && CrossesRoad(seg) && PedLightNotGreen(seg)) target *= 1.7f;
            HeldByRed = false;
            float dist = seg.stopS - (s + HalfLength);
            var hub = seg.toApp;
            var ic = seg.toNode != null ? seg.toNode.controller : null;
            bool atLine = speed < 0.05f && dist < 0.3f && dist > -0.12f;
            if (atLine) lineTimer += dt;
            if (next != null && dist > -0.08f)
            {
                bool stop = false;
                bool siren = spec.emergency && agent == 0;   // la emergencia pasa en rojo (los demás le ceden el paso)
                // Peatones: el semáforo peatonal solo los para antes de cruzar la calzada (no al terminar de cruzarla).
                bool walkerFree = seg.laneType == RoadData.LaneSidewalk && !CrossesRoad(next);
                if (!chaotic && !siren && !walkerFree && ic != null && hub != null && hub.hasLight && ic.light.Has(hub))
                {
                    var st = ic.GetState(hub);
                    if (st == SignalState.Red)
                    {
                        stop = !reckless && !commitGo;
                        if (stop) HeldByRed = true;
                    }
                    else if (st == SignalState.Yellow)
                    {
                        float brake = speed * speed / (2f * spec.decel);
                        if (commitGo) stop = false;
                        else if (dist >= brake) { stop = true; HeldByRed = true; }
                        else commitGo = true;
                    }
                    else commitGo = false;
                }
                if (!chaotic && !siren && seg.endRank == 0 && !stopDone)
                {
                    if (atLine && lineTimer > 0.8f) stopDone = true;
                    else stop = true;
                }
                if (!chaotic && seg.endRank == 1 && dist < 0.8f) target = Mathf.Min(target, spec.maxSpeed * 0.5f);
                float lim = Mathf.Sqrt(2f * spec.decel * Mathf.Max(dist, 0f));
                if (stop && lim < target) { why = HeldByRed ? "rojo" : "stop"; whyId = -1; target = lim; }
            }
            if (!chaotic) target = ConflictLimit(target, gap, leaderSpeed);
            return target;
        }

        /// <summary>Tiempo estimado hasta llegar a una zona a distancia d (un vehículo parado tarda en arrancar).</summary>
        static float Eta(VehicleController x, float d)
        {
            return x.speed < 0.05f ? 0.8f + d : d / Mathf.Max(x.speed, 0.25f);
        }

        struct ZoneAhead { public float enter, exit; public bool wait; public string reason; public int blocker, road; }
        readonly List<ZoneAhead> zonesAhead = new List<ZoneAhead>();

        /// <summary>
        /// Limita la velocidad para detenerse antes de una zona de cruce si hay que ceder el paso o no hay sitio al otro lado.
        /// Las zonas seguidas se tratan como un bloque: nunca se para dentro de una zona por tener que esperar en la siguiente.
        /// </summary>
        float ConflictLimit(float target, float gap, float leaderSpeed)
        {
            float off = -(s + HalfLength);
            float look = Mathf.Max(1.4f, speed * speed / (2f * spec.decel) + 1.2f);
            zonesAhead.Clear();
            for (int k = routeIndex; k < route.Count && k <= routeIndex + 5; k++)
            {
                var sg = route[k];
                if (off > look + 3f) break;
                var list = sg.conflicts;
                for (int i = 0; i < list.Count; i++)
                {
                    var c = list[i];
                    float dEnter = off + c.myS - c.clear;
                    float dExit = off + c.myS + c.clear;
                    if (dEnter > look + 3f || dExit < -Length) continue;
                    var z = new ZoneAhead { enter = dEnter, exit = dExit, road = c.other.roadId, blocker = -1 };
                    if (dEnter >= 0f)
                    {
                        lastBlocker = -1;
                        z.wait = MustYieldAt(c, sg, dEnter);
                        z.reason = "cede";
                        z.blocker = lastBlocker;
                        if (z.wait) z.reason = "cede(" + lastBranch + ")";
                        // No bloquear el cruce: solo se entra si hay sitio para salir de la zona (si alguien necesita cruzar por ella).
                        if (!z.wait && waitTime < 25f && c.other.crossRank >= sg.crossRank && gap < float.MaxValue && leaderSpeed < 0.4f
                            && gap < dEnter + 2f * c.clear + Length + spec.minGap && Demand(c))
                        { z.wait = true; z.reason = "despejar"; }
                    }
                    zonesAhead.Add(z);
                }
                off += sg.Length;
            }
            if (zonesAhead.Count == 0) return target;
            zonesAhead.Sort((x, y) => x.enter.CompareTo(y.enter));

            // Primera zona en la que hay que esperar.
            int first = -1;
            for (int i = 0; i < zonesAhead.Count; i++) if (zonesAhead[i].wait && zonesAhead[i].enter <= look) { first = i; break; }
            if (first < 0) return target;
            float stop = zonesAhead[first].enter;
            // Si parado ahí quedaría dentro de una zona anterior, se espera antes de esa (y así hacia atrás).
            for (int i = first - 1; i >= 0; i--)
            {
                var z = zonesAhead[i];
                if (z.enter < 0f) break;   // ya estoy dentro: no se puede retroceder
                if (stop - Length - 0.05f < z.exit) stop = z.enter;
            }
            float lim = Mathf.Sqrt(2f * spec.decel * Mathf.Max(0f, stop - 0.03f));
            if (lim < target)
            {
                target = lim;
                var zf = zonesAhead[first];
                why = zf.reason + ":" + zf.road;
                whyId = zf.blocker;
            }
            return target;
        }

        /// <summary>¿Hay alguien (sin semáforo en rojo) que quiera pasar pronto por esa zona desde el otro carril?</summary>
        static bool Demand(Conflict c)
        {
            var o = c.other;
            for (int i = 0; i < o.vehicles.Count; i++)
            {
                var w = o.vehicles[i];
                if (w.HeldByRed || w.crashed) continue;
                float wd = c.otherS - c.otherClear - (w.s + w.HalfLength);
                if (wd < 3f && wd > -(2f * c.otherClear + w.Length)) return true;
            }
            for (int pi = 0; pi < o.ins.Count; pi++)
            {
                var p = o.ins[pi];
                for (int i = 0; i < p.vehicles.Count; i++)
                {
                    var w = p.vehicles[i];
                    if (w.HeldByRed || w.crashed || w.NextSeg != o) continue;
                    if ((p.Length - w.s - w.HalfLength) + c.otherS - c.otherClear < 3f) return true;
                }
            }
            return false;
        }

        RoadSeg curConflictSeg;

        /// <summary>¿El cuerpo de w está físicamente sobre mi carril (cerca del punto de cruce)? Entonces no se puede pasar.</summary>
        bool PhysicallyInMyWay(VehicleController w, Conflict c)
        {
            if (curConflictSeg == null) return true;
            float d;
            float sp = curConflictSeg.path.Project(w.Pos, out d);
            if (Mathf.Abs(sp - c.myS) > c.clear + w.Length) return false;
            return d < (spec.width + w.spec.width) * 0.5f + w.HalfLength * 0.6f + 0.05f;
        }

        bool MustYieldAt(Conflict c, RoadSeg mySeg, float dEnter)
        {
            curConflictSeg = mySeg;
            var o = c.other;
            int myRank = mySeg.crossRank, oRank = o.crossRank;
            float myT = Eta(this, dEnter);
            var list = o.vehicles;
            for (int i = 0; i < list.Count; i++)
            {
                var w = list[i];
                float wd = c.otherS - c.otherClear - (w.s + w.HalfLength);
                if (Blocks(w, wd, c, myRank, oRank, myT)) return true;
            }
            float startGap = c.otherS - c.otherClear;
            if (startGap < 3.5f)
            {
                for (int pi = 0; pi < o.ins.Count; pi++)
                {
                    var p = o.ins[pi];
                    for (int i = 0; i < p.vehicles.Count; i++)
                    {
                        var w = p.vehicles[i];
                        if (w.NextSeg != o) continue;
                        float wd = (p.Length - w.s - w.HalfLength) + startGap;
                        if (wd > 4f) continue;
                        if (Blocks(w, wd, c, myRank, oRank, myT)) return true;
                    }
                }
            }
            return false;
        }

        int lastBlocker = -1;

        bool Blocks(VehicleController w, float wd, Conflict c, int myRank, int oRank, float myT)
        {
            bool r = BlocksCore(w, wd, c, myRank, oRank, myT);
            if (r) lastBlocker = w.id;
            return r;
        }

        public string lastBranch = "";

        bool BlocksCore(VehicleController w, float wd, Conflict c, int myRank, int oRank, float myT)
        {
            if (w == this || w.arrived) return false;
            if (w.Seg == Seg) return false;   // va en mi mismo carril: el orden lo da el seguimiento del de delante
            // Está parado esperándome a mí: no me paro yo por él… salvo que ya esté metido en mi camino (se paró dentro de
            // la zona) y yo no lo esté esperando a él: entonces freno y, como lo espero, él podrá salir.
            if (w.whyId == id && w.speed < 0.05f && !(wd < 0f && whyId != w.id && PhysicallyInMyWay(w, c))) return false;
            float zone = 2f * c.otherClear;
            if (wd < 0f)
            {
                // Está en la zona (o su cola aún la ocupa). Salida de emergencia: si los dos llevamos mucho parados
                // esperándonos, pasa primero el que más espera (orden único, así no se bloquean para siempre).
                if (-wd >= zone + w.Length) return false;
                if (speed < 0.05f && w.speed < 0.05f && waitTime > 8f && w.waitTime > 8f && !w.crashed && !PhysicallyInMyWay(w, c))
                {
                    // Si el otro está esperando a un tercero (o atrapado detrás de su coche de delante), no puede
                    // aprovechar el turno: paso yo (no estorba mi camino, ya se comprobó arriba).
                    if (w.why == "sigue" || (w.whyId >= 0 && w.whyId != id)) return false;
                    lastBranch = "emerg"; return w.waitTime > waitTime + 0.4f || (Mathf.Abs(w.waitTime - waitTime) <= 0.4f && w.id < id);
                }
                lastBranch = "dentro";
                return true;
            }
            if (w.crashed || w.HeldByRed || w.chaotic && w.speed < 0.05f) return false;
            if (w.onShoulder || w.userStopped && w.speed < 0.05f) return false;
            // Emergencias: todos le ceden el paso; ella solo espera si alguien está ya dentro de la zona.
            if (w.spec.emergency && !spec.emergency && w.agent == 0) { lastBranch = "emergencia"; return Eta(w, wd) < 4f; }
            if (spec.emergency && agent == 0 && !w.spec.emergency) return false;
            float wBrake = w.speed * w.speed / (2f * w.spec.decel);
            if (w.speed > 0.15f && wd < wBrake + 0.12f) { lastBranch = "nofrena"; return true; }  // ya no puede frenar
            if (w.speed < 0.05f)
            {
                // Parado lejos de la zona, o en cola detrás de otro: no va a llegar pronto.
                if (wd > 0.6f || w.why == "sigue") return false;
                // Anti-bloqueo: si los dos llevamos rato parados ante la zona, pasa el que más tiempo lleva esperando.
                if (speed < 0.05f && (waitTime > 2.5f || w.waitTime > 2.5f))
                {
                    if (w.whyId >= 0 && w.whyId != id && w.waitTime > 2.5f) return false;   // espera a un tercero: no usará el turno
                    lastBranch = "orden"; return w.waitTime > waitTime + 0.4f || (Mathf.Abs(w.waitTime - waitTime) <= 0.4f && w.id < id); }
            }
            float wT = Eta(w, wd);
            if (oRank > myRank) { lastBranch = "prio"; return wT < 2.5f; }   // la otra corriente tiene prioridad
            if (oRank < myRank) return false;                        // me cede el paso
            if (wT > 3.5f) return false;
            lastBranch = "eta";
            return wT < myT - 0.15f || (Mathf.Abs(wT - myT) <= 0.15f && w.id < id);
        }

        public void Crash(float seconds)
        {
            if (crashed) return;
            crashed = true;
            crashTimer = seconds;
            speed = 0f;
            body.color = Color.Lerp(body.color, SpriteFactory.Hex("#3A1F1F"), 0.65f);
        }

        /// <summary>Actualiza la posición visual y el parpadeo de emergencia.</summary>
        public void UpdateVisual(float dt, float time)
        {
            visualAngle = Mathf.MoveTowardsAngle(visualAngle, Geo.Angle(dir), 720f * dt);
            if (shiftVec.sqrMagnitude > 0f) shiftVec = Vector2.MoveTowards(shiftVec, Vector2.zero, 0.7f * dt);
            Apply();

            if (beacon != null)
            {
                bool on = Mathf.FloorToInt(time * 4f) % 2 == 0;
                beacon.color = on ? SpriteFactory.Hex("#FF3B30") : SpriteFactory.Hex("#3B82F6");
            }
            shoulderAmount = Mathf.MoveTowards(shoulderAmount, onShoulder ? 1f : 0f, dt * 1.2f);
            if (agent != 0) return;

            // Luces de freno más brillantes al frenar o parado.
            if (tails != null)
                foreach (var t in tails) if (t != null) t.color = braking ? new Color(1f, 0.15f, 0.12f, 1f) : new Color(0.55f, 0.08f, 0.08f, 1f);
            float dark = DayNight.Darkness;
            if (beams != null)
            {
                for (int bi = 0; bi < beams.Length; bi++) beams[bi].color = new Color(1f, 0.97f, 0.88f, 0.42f * dark);
                for (int bi = 0; bi < tailGlows.Length; bi++)
                {
                    tailGlows[bi].color = new Color(1f, 0.1f, 0.08f, (braking ? 0.8f : 0.3f) * dark);
                    tailGlows[bi].transform.localScale = Vector3.one * (braking ? 0.2f : 0.11f);
                }
            }
            if (shadowSr != null) shadowSr.color = new Color(0f, 0f, 0f, 0.3f * DayNight.ShadowAlpha);

            // Intermitentes como en la vida real: unos metros antes de un giro en un cruce (donde hay que elegir entre
            // varios carriles) y durante el giro; al cambiar de carril; y al apartarse a la berma.
            if (blinkers != null)
            {
                int side = 0;
                var nx = NextSeg;
                if (onShoulder || holdTimer > 0f) side = shoulderSide > 0f ? 1 : -1;
                else if (laneBlinkT > 0f) { laneBlinkT -= dt; side = laneBlinkSide; }
                else if (Seg.laneMode)
                {
                    if (nx != null && Seg.Length - s < 3.5f && ChoiceAhead(Seg))
                    {
                        int ts = TurnSide(nx);
                        if (ts != 0) { blinkSeg = nx; blinkTurn = ts; side = ts; }
                    }
                    if (side == 0 && blinkSeg == Seg && s < Seg.Length * 0.7f) side = blinkTurn;
                }
                else if (nx != null && Seg.Length - s < 2.2f)
                {
                    float a = Vector2.SignedAngle(Seg.path.TangentAt(Seg.Length), nx.path.TangentAt(Mathf.Min(nx.Length, 0.6f)));
                    if (a > 25f) side = -1; else if (a < -25f) side = 1;
                }
                bool lit = Mathf.FloorToInt(time * 3f) % 2 == 0;
                var on = new Color(1f, 0.65f, 0.1f, 1f);
                var off = new Color(1f, 0.65f, 0.1f, 0f);
                blinkers[0].color = blinkers[1].color = side == -1 && lit ? on : off;
                blinkers[2].color = blinkers[3].color = side == 1 && lit ? on : off;
                if (blinkHalos != null)
                {
                    var glowOn = new Color(1f, 0.6f, 0.1f, 0.55f);
                    blinkHalos[0].color = blinkHalos[1].color = side == -1 && lit ? glowOn : off;
                    blinkHalos[2].color = blinkHalos[3].color = side == 1 && lit ? glowOn : off;
                }
            }

            // Paciencia: halo verde → amarillo → rojo según lo que lleva esperando.
            if (ShowPatience)
            {
                if (patienceHalo == null)
                    patienceHalo = SpriteFactory.Make("Patience", tr, SpriteFactory.Circle, Color.green, Vector2.zero, new Vector2(spec.length * 1.35f, spec.width * 2.2f), 18);
                patienceHalo.enabled = true;
                float k = Mathf.Clamp01(waitTime / 20f);
                var c = k < 0.5f ? Color.Lerp(new Color(0.2f, 0.85f, 0.3f), new Color(1f, 0.85f, 0.2f), k * 2f) : Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(0.95f, 0.15f, 0.12f), (k - 0.5f) * 2f);
                c.a = 0.55f;
                patienceHalo.color = c;
            }
            else if (patienceHalo != null) patienceHalo.enabled = false;

            // Marca de stop del jugador sobre el vehículo (no gira con él).
            if (userStopped)
            {
                if (stopMark == null)
                {
                    stopMark = SpriteFactory.Make("StopMark", tr, SpriteFactory.Octagon, SpriteFactory.Hex("#D62828"), Vector2.zero, Vector2.one * 0.26f, 60);
                    SpriteFactory.Make("StopBar", stopMark.transform, SpriteFactory.Square, Color.white, Vector2.zero, new Vector2(0.6f, 0.16f), 61);
                }
                stopMark.enabled = true;
                stopMark.transform.rotation = Quaternion.identity;
                stopMark.transform.position = new Vector3(tr.position.x, tr.position.y + spec.width * 0.9f, 0f);
            }
            else if (stopMark != null) stopMark.enabled = false;
        }

        /// <summary>Lado al que gira un carril de cruce (-1 izquierda, 1 derecha, 0 recto): ángulo entre su inicio y su final.</summary>
        /// <summary>¿El carril (de acera) cruza algún carril que no sea de peatones? Es decir, ¿cruza la calzada?</summary>
        static bool PedLightNotGreen(RoadSeg sg)
        {
            foreach (var app in new[] { sg.fromApp, sg.toApp })
            {
                if (app == null || !app.hasLight || app.node == null || app.node.controller == null) continue;
                var ic = app.node.controller;
                if (ic.light.Has(app) && ic.GetState(app) != SignalState.Green) return true;
            }
            return false;
        }

        static bool CrossesRoad(RoadSeg sg)
        {
            if (sg == null) return false;
            foreach (var c in sg.conflicts) if (c.other != null && c.other.laneType != RoadData.LaneSidewalk) return true;
            return false;
        }

        static bool ChoiceAhead(RoadSeg sg)
        {
            if (sg.toApp == null) return false;
            int n = 0;
            foreach (var o in sg.toApp.outLanes) if (o.laneType == sg.laneType && !o.twin) n++;
            return n >= 2;
        }

        static int TurnSide(RoadSeg sg)
        {
            float a = Vector2.SignedAngle(sg.path.TangentAt(0f), sg.path.TangentAt(sg.Length));
            return a > 30f ? -1 : (a < -30f ? 1 : 0);
        }

        /// <summary>Pasa al carril adyacente adj; newRoute empieza en adj y llega hasta la misma salida.</summary>
        public void ChangeLane(RoadSeg adj, List<RoadSeg> newRoute, float newS = -1f)
        {
            Vector2 oldPos = pos;
            RoadSeg cur = Seg;
            float frac = newS >= 0f ? newS / Mathf.Max(0.01f, adj.Length) : (cur.Length > 0.01f ? s / cur.Length : 0f);
            cur.vehicles.Remove(this);
            route.RemoveRange(routeIndex, route.Count - routeIndex);
            route.AddRange(newRoute);
            s = Mathf.Clamp(frac * adj.Length, 0f, adj.Length);
            adj.vehicles.Add(this);
            UpdatePose();
            shiftVec = oldPos - pos;
            // Intermitente hacia el lado al que se pasa, durante el cambio y un poco después.
            Vector2 mv = pos - oldPos;
            if (mv.sqrMagnitude > 1e-4f) { laneBlinkSide = Vector2.Dot(mv, new Vector2(dir.y, -dir.x)) > 0f ? 1 : -1; laneBlinkT = 1.8f; }
            laneCooldown = 6f;
            blockedTime = 0f;
            served.Clear();
            dwell = 0f;
            lineTimer = 0f;
        }

        void Apply()
        {
            Vector2 vp = pos + shiftVec + new Vector2(dir.y, -dir.x) * (shoulderSide * shoulderAmount * Mathf.Max(0.12f, Seg.width * 0.55f));
            int ly = Layer;
            if (renderers != null && appliedLayer != ly)
            {
                appliedLayer = ly;
                for (int i = 0; i < renderers.Length; i++) renderers[i].sortingOrder = baseOrders[i] + LayerOrderOffset(appliedLayer);
            }
            tr.position = new Vector3(vp.x, vp.y, 0f);
            tr.rotation = Quaternion.Euler(0f, 0f, visualAngle);
            if (pedLegs != null)
            {
                // Paso: las piernas van y vienen (una adelante, otra atrás) y los brazos al revés; quieto, todo en su sitio.
                float sw = speed > 0.03f ? Mathf.Sin(odo * 38f) * spec.length * 0.22f : 0f;
                for (int k = 0; k < 2; k++)
                {
                    float sg = k == 0 ? 1f : -1f;
                    var lp = pedLegs[k].localPosition; lp.x = sw * sg; pedLegs[k].localPosition = lp;
                    var ap = pedArms[k].localPosition; ap.x = -sw * sg * 0.7f; pedArms[k].localPosition = ap;
                }
                if (umbrella != null && umbrella.enabled != RainNow)
                {
                    umbrella.enabled = RainNow;
                    if (umbrellaTip != null) umbrellaTip.enabled = RainNow;
                }
            }
            if (shadowSr != null)
            {
                // La sombra cae siempre hacia la izquierda de la pantalla (como la de los postes), gire como gire el coche.
                // Dirección y largo según la hora del día (DayNight); de diseño, hacia la izquierda.
                Vector2 w = DayNight.ShadowDir * (0.056f * DayNight.ShadowLength * (spec.width / 0.26f));
                float ra = -visualAngle * Mathf.Deg2Rad;
                Vector2 lw = new Vector2(w.x * Mathf.Cos(ra) - w.y * Mathf.Sin(ra), w.x * Mathf.Sin(ra) + w.y * Mathf.Cos(ra));
                Vector3 sc = tr.localScale;
                shadowSr.transform.localPosition = new Vector3(lw.x / Mathf.Max(0.01f, sc.x), lw.y / Mathf.Max(0.01f, sc.y), 0f);
            }
        }

        public void Destroy()
        {
            Seg.vehicles.Remove(this);
            if (go != null) Object.Destroy(go);
        }
    }
}
