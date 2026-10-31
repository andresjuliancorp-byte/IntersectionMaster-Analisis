using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Core;
using IntersectionMaster.Data;
using IntersectionMaster.Systems;

namespace IntersectionMaster.Traffic
{
    /// <summary>
    /// Lluvia y eventos aleatorios al jugar (como el original): vehículo de emergencia, avería con grúa, taxi que para,
    /// incendio con bomberos y obras. Cada evento se elige al azar entre los activados en Ajustes de mapa → General.
    /// </summary>
    public partial class TrafficSimulationManager
    {
        // ---------------- Lluvia ----------------

        public bool Raining { get; private set; }
        Transform rainRoot;
        SpriteRenderer rainTint;
        readonly List<Transform> drops = new List<Transform>();

        /// <summary>Activa o quita la lluvia: suelo más oscuro y brillante, gotas y vehículos más lentos que frenan antes.</summary>
        public void SetRain(bool on)
        {
            if (on == Raining && (on == (rainRoot != null))) return;
            Raining = on;
            VehicleController.RainNow = on;
            AudioManager.Instance.SetRain(on);
            for (int i = 0; i < Vehicles.Count; i++)
                if (Vehicles[i].agent == 0) { if (on) RainSpec(Vehicles[i]); else DryVehicle(Vehicles[i]); }
            if (rainRoot != null) { Destroy(rainRoot.gameObject); rainRoot = null; drops.Clear(); }
            if (!on || Map == null) return;
            rainRoot = new GameObject("Rain").transform;
            rainRoot.SetParent(root != null ? root : transform, false);
            // En los mapas grandes (200 × 200, como el original) la lluvia va donde se juega (la zona visible): antes las gotas
            // se repartían por todo el mapa y casi no se veían.
            Rect area = RainArea();
            float w = Map.width, h = Map.height;
            rainTint = SpriteFactory.Make("WetTint", rainRoot, SpriteFactory.Square, new Color(0.08f, 0.12f, 0.2f, 0.22f), new Vector2(w / 2f, h / 2f), new Vector2(w + 40f, h + 40f), 18);
            // Cielo nublado como en el original: todo (calles, edificios, vehículos) se ve más gris y azulado.
            SpriteFactory.Make("OvercastTint", rainRoot, SpriteFactory.Square, new Color(0.24f, 0.32f, 0.42f, 0.42f), new Vector2(w / 2f, h / 2f), new Vector2(w + 40f, h + 40f), 33);   // justo debajo de la capa de noche (34)
            int count = Mathf.Clamp(Mathf.RoundToInt(area.width * area.height * 0.35f), 80, 400);
            for (int i = 0; i < count; i++)
            {
                var d = SpriteFactory.Make("Drop", rainRoot, SpriteFactory.Square, new Color(0.8f, 0.88f, 1f, 0.45f),
                    new Vector2(Random.Range(area.xMin, area.xMax), Random.Range(area.yMin, area.yMax)), new Vector2(0.02f, 0.28f), 300, 12f);
                drops.Add(d.transform);
            }
        }

        /// <summary>Zona donde cae la lluvia: la visible del mapa (con margen) o, si no hay, todo el mapa.</summary>
        Rect RainArea()
        {
            Rect v = Map.View;
            if (v.width > 1f && v.height > 1f) return new Rect(v.xMin - 3f, v.yMin - 3f, v.width + 6f, v.height + 6f);
            return new Rect(-2f, -2f, Map.width + 4f, Map.height + 4f);
        }

        readonly HashSet<VehicleController> wet = new HashSet<VehicleController>();

        void RainSpec(VehicleController v)
        {
            if (!wet.Add(v)) return;
            v.spec.maxSpeed *= 0.85f;
            v.spec.decel *= 0.75f;
            v.spec.headway *= 1.25f;
        }

        void DryVehicle(VehicleController v)
        {
            if (!wet.Remove(v)) return;
            v.spec.maxSpeed /= 0.85f;
            v.spec.decel /= 0.75f;
            v.spec.headway /= 1.25f;
        }

        void UpdateRain(float dt)
        {
            if (!Raining || rainRoot == null || Map == null) return;
            Rect area = RainArea();
            for (int i = 0; i < drops.Count; i++)
            {
                var t = drops[i];
                Vector3 p = t.localPosition + new Vector3(-1.2f, -9f, 0f) * dt;
                if (p.y < area.yMin) p = new Vector3(Random.Range(area.xMin, area.xMax + 2f), area.yMax, 0f);
                t.localPosition = p;
            }
        }

        // ---------------- Eventos aleatorios ----------------

        float eventTimer = -1f;
        readonly List<WorldEvent> events = new List<WorldEvent>();
        /// <summary>Eventos en curso (para los avisos).</summary>
        public int EventCount { get { return events.Count; } }

        class WorldEvent
        {
            public int kind;
            public float t;                         // tiempo desde que empezó
            public VehicleController target;        // vehículo averiado o taxi
            public VehicleController blocker;       // bomberos, grúa o pavimentadora (obstáculo real en el carril)
            public RoadSeg seg;
            public float s;
            public Vector2 pos;
            public readonly List<SpriteRenderer> fx = new List<SpriteRenderer>();
            public Transform fxRoot;
            public bool done;
            public IntersectionController ic;          // semáforo averiado
            public VehicleController.SlowZone zone;    // agua en la calzada
        }

        void ResetEvents()
        {
            foreach (var e in events)
            {
                if (e.fxRoot != null) Destroy(e.fxRoot.gameObject);
                if (e.ic != null) e.ic.broken = false;
            }
            events.Clear();
            VehicleController.SlowZones.Clear();
            eventTimer = Random.Range(120f, 180f);
            wet.Clear();
        }

        void EventsStep(float dt)
        {
            UpdateRain(dt);
            if (Map == null || !Network.LaneMode) return;
            if (eventTimer < 0f) eventTimer = Random.Range(120f, 180f);
            eventTimer -= dt;
            if (eventTimer <= 0f)
            {
                eventTimer = Random.Range(120f, 180f);   // cada 2–3 minutos
                TryStartEvent();
            }
            for (int i = events.Count - 1; i >= 0; i--)
            {
                var e = events[i];
                e.t += dt;
                StepEvent(e, dt);
                if (e.done)
                {
                    if (e.fxRoot != null) Destroy(e.fxRoot.gameObject);
                    events.RemoveAt(i);
                }
            }
        }

        void TryStartEvent()
        {
            var kinds = new List<int>();
            for (int k = 0; k < MapData.EventNames.Length; k++) if ((Map.events & (1 << k)) != 0) kinds.Add(k);
            // Se prueba en orden aleatorio hasta que uno se pueda hacer en este mapa.
            while (kinds.Count > 0)
            {
                int pick = Random.Range(0, kinds.Count);
                int kind = kinds[pick];
                kinds.RemoveAt(pick);
                if (StartEvent(kind)) return;
            }
        }

        /// <summary>Empieza un evento concreto (también para probarlos). Devuelve false si no hay dónde hacerlo.</summary>
        public bool StartEvent(int kind)
        {
            switch (kind)
            {
                case 0: return EmergencyEvent();
                case 1: return BreakdownEvent();
                case 2: return TaxiEvent();
                case 3: return FireEvent();
                case 4: return RoadworksEvent();
                case 5: return BrokenLightEvent();
                case 6: return BrokenLampEvent();
                case 7: return HydrantEvent();
            }
            return false;
        }

        bool BrokenLightEvent()
        {
            var list = new List<IntersectionController>();
            foreach (var ic in Intersections) if (ic.IsLit && !ic.broken) list.Add(ic);
            if (list.Count == 0) return false;
            var pick = list[Random.Range(0, list.Count)];
            pick.broken = true;
            events.Add(new WorldEvent { kind = 5, ic = pick, pos = pick.node != null ? pick.node.pos : Vector2.zero });
            Message("Un semáforo se ha averiado: parpadea en ámbar y los conductores pasan con cuidado.");
            return true;
        }

        bool BrokenLampEvent()
        {
            var lamps = new List<DecorItem>();
            foreach (var d in Map.decor) if (d.type == DecorType.Lamp && Map.View.Contains(d.Pos)) lamps.Add(d);
            if (lamps.Count == 0) return false;
            var l = lamps[Random.Range(0, lamps.Count)];
            var e = new WorldEvent { kind = 6, pos = l.Pos };
            e.fxRoot = new GameObject("BrokenLamp").transform;
            e.fxRoot.SetParent(root, false);
            // Chispazos en la cabeza de la farola (se ve sobre todo de noche).
            e.fx.Add(SpriteFactory.Make("Spark", e.fxRoot, SpriteFactory.Soft, new Color(1f, 0.85f, 0.45f, 0f), l.Pos, Vector2.one * 0.5f, 44));
            e.fx.Add(SpriteFactory.Make("SparkCore", e.fxRoot, SpriteFactory.Circle, new Color(1f, 1f, 0.8f, 0f), l.Pos, Vector2.one * 0.07f, 45));
            events.Add(e);
            Message("Una farola se ha estropeado.");
            return true;
        }

        bool HydrantEvent()
        {
            var cands = new List<RoadSeg>();
            foreach (var sg in Network.segs)
                if (sg.kind == SegKind.Lane && sg.laneType == RoadData.LaneVehicle && sg.Length > 3f) cands.Add(sg);
            while (cands.Count > 0)
            {
                int i = Random.Range(0, cands.Count);
                var sg = cands[i];
                cands.RemoveAt(i);
                float s = Random.Range(1.2f, sg.Length - 1.2f);
                float side = LaneBeside(sg, s, 1f, true) ? 1f : (LaneBeside(sg, s, -1f, true) ? -1f : 0f);
                if (side == 0f || !Map.View.Contains(sg.path.PointAt(s))) continue;
                Vector2 p = sg.path.PointAt(s), t = sg.path.TangentAt(s), r = new Vector2(t.y, -t.x) * side;
                var e = new WorldEvent { kind = 7, seg = sg, s = s, pos = p + r * (sg.width * 0.95f) };
                e.fxRoot = new GameObject("Hydrant").transform;
                e.fxRoot.SetParent(root, false);
                e.fx.Add(SpriteFactory.Make("Puddle", e.fxRoot, SpriteFactory.Circle, new Color(0.45f, 0.62f, 0.8f, 0f), p + r * 0.1f, new Vector2(1.4f, sg.width * 1.3f), 9, Geo.Angle(t)));
                e.fx.Add(SpriteFactory.Make("HydrantBody", e.fxRoot, SpriteFactory.Circle, SpriteFactory.Hex("#D62828"), e.pos, Vector2.one * 0.12f, 30));
                for (int k = 0; k < 10; k++)
                    e.fx.Add(SpriteFactory.Make("Spray", e.fxRoot, SpriteFactory.Circle, new Color(0.85f, 0.93f, 1f, 0.8f), e.pos, Vector2.one * 0.06f, 31));
                e.zone = new VehicleController.SlowZone { seg = sg, s0 = s - 0.9f, s1 = s + 0.9f, factor = 0.35f };
                VehicleController.SlowZones.Add(e.zone);
                events.Add(e);
                Message("¡Una boca de incendios se ha roto! Los coches pasan despacio por el agua.");
                return true;
            }
            return false;
        }

        bool EmergencyEvent()
        {
            var entries = new List<NetNode>(Network.entries);
            while (entries.Count > 0)
            {
                int i = Random.Range(0, entries.Count);
                var e = entries[i];
                entries.RemoveAt(i);
                int k = Random.Range(0, 3);
                var type = k == 0 ? VehicleType.Ambulance : (k == 1 ? VehicleType.Police : VehicleType.FireTruck);
                if (SpawnAt(e, type)) { Message("¡Vehículo de emergencia! Déjalo pasar."); return true; }
            }
            return false;
        }

        /// <summary>¿Hay un carril (o acera, si sidewalk) justo al lado de ese punto del carril, hacia side?</summary>
        bool LaneBeside(RoadSeg seg, float s, float side, bool sidewalk)
        {
            Vector2 p = seg.path.PointAt(s), t = seg.path.TangentAt(s);
            Vector2 q = p + new Vector2(t.y, -t.x) * (side * seg.width);
            foreach (var o in Network.segs)
            {
                if (o == seg || o.kind != SegKind.Lane) continue;
                if (sidewalk ? o.laneType != RoadData.LaneSidewalk : (o.laneType == RoadData.LaneSidewalk)) continue;
                float d;
                o.path.Project(q, out d);
                if (d < (sidewalk ? seg.width * 0.9f + o.width * 0.5f : o.width * 0.6f)) return true;
            }
            return false;
        }

        /// <summary>Lado libre (sin otro carril) hacia el que apartarse: +1 derecha, -1 izquierda, 0 ninguno.</summary>
        float FreeSide(RoadSeg seg, float s)
        {
            if (!LaneBeside(seg, s, 1f, false)) return 1f;
            if (!LaneBeside(seg, s, -1f, false)) return -1f;
            return 0f;
        }

        bool Moving(VehicleController v, VehicleType? only)
        {
            if (v.agent != 0 || v.obstacle || v.crashed || v.arrived || v.onShoulder || v.holdTimer > 0f || v.userStopped || v.spec.emergency) return false;
            if (only.HasValue && v.type != only.Value) return false;
            var sg = v.Seg;
            return sg.kind == SegKind.Lane && v.s > 1f && sg.Length - v.s > 1.5f;
        }

        bool BreakdownEvent()
        {
            var list = new List<VehicleController>();
            foreach (var v in Vehicles) if (Moving(v, null) && v.type != VehicleType.Tram && v.type != VehicleType.Bus) list.Add(v);
            while (list.Count > 0)
            {
                int i = Random.Range(0, list.Count);
                var v = list[i];
                list.RemoveAt(i);
                float side = FreeSide(v.Seg, v.s);
                if (side == 0f) continue;
                v.onShoulder = true;
                v.shoulderSide = side;
                v.holdTimer = 30f;
                events.Add(new WorldEvent { kind = 1, target = v, seg = v.Seg, s = v.s });
                Message("Un vehículo se averió: los demás pasan despacio.");
                return true;
            }
            return false;
        }

        bool TaxiEvent()
        {
            var list = new List<VehicleController>();
            foreach (var v in Vehicles) if (Moving(v, VehicleType.Taxi)) list.Add(v);
            foreach (var v in list)
            {
                float side = 0f;
                if (LaneBeside(v.Seg, v.s, 1f, true)) side = 1f;
                else if (LaneBeside(v.Seg, v.s, -1f, true)) side = -1f;
                if (side == 0f) continue;   // solo si hay acera al lado
                v.onShoulder = true;
                v.shoulderSide = side;
                v.holdTimer = 5f;
                var e = new WorldEvent { kind = 2, target = v, seg = v.Seg, s = v.s };
                events.Add(e);
                return true;
            }
            return false;
        }

        bool FireEvent()
        {
            // Un edificio junto a un carril de vehículos.
            var cands = new List<DecorItem>();
            foreach (var d in Map.decor) if (d.type == DecorType.Building) cands.Add(d);
            while (cands.Count > 0)
            {
                int i = Random.Range(0, cands.Count);
                var b = cands[i];
                cands.RemoveAt(i);
                RoadSeg best = null; float bs = 0f, bd = 2.2f;
                foreach (var sg in Network.segs)
                {
                    if (sg.kind != SegKind.Lane || sg.laneType != RoadData.LaneVehicle || sg.Length < 2.5f) continue;
                    float d;
                    float ss = sg.path.Project(b.Pos, out d);
                    if (d < bd && ss > 1f && ss < sg.Length - 1f) { bd = d; best = sg; bs = ss; }
                }
                if (best == null || !SpotFree(best, bs, 1.2f)) continue;
                var e = new WorldEvent { kind = 3, seg = best, s = bs, pos = b.Pos };
                e.fxRoot = new GameObject("Fire").transform;
                e.fxRoot.SetParent(root, false);
                for (int k = 0; k < 5; k++)
                    e.fx.Add(SpriteFactory.Make("Flame", e.fxRoot, SpriteFactory.Circle, new Color(1f, 0.45f, 0.1f, 0.85f),
                        b.Pos + Random.insideUnitCircle * 0.35f * Mathf.Max(0.6f, b.size), Vector2.one * 0.3f, 40));
                events.Add(e);
                Message("¡Incendio! Llegan los bomberos.");
                return true;
            }
            return false;
        }

        bool RoadworksEvent()
        {
            var cands = new List<RoadSeg>();
            foreach (var sg in Network.segs)
                if (sg.kind == SegKind.Lane && sg.laneType == RoadData.LaneVehicle && sg.Length > 3.5f) cands.Add(sg);
            while (cands.Count > 0)
            {
                int i = Random.Range(0, cands.Count);
                var sg = cands[i];
                cands.RemoveAt(i);
                float s = Random.Range(1.5f, sg.Length - 1.5f);
                if (!SpotFree(sg, s, 1.5f)) continue;
                var e = new WorldEvent { kind = 4, seg = sg, s = s, pos = sg.path.PointAt(s) };
                e.fxRoot = new GameObject("Roadworks").transform;
                e.fxRoot.SetParent(root, false);
                Vector2 t = sg.path.TangentAt(s), r = new Vector2(t.y, -t.x);
                // Bache.
                e.fx.Add(SpriteFactory.Make("Pothole", e.fxRoot, SpriteFactory.Circle, new Color(0.25f, 0.22f, 0.2f, 0.9f), e.pos, new Vector2(0.55f, sg.width * 0.6f), 9, Geo.Angle(t)));
                // Conos alrededor.
                for (int k = -1; k <= 1; k++)
                    for (int side = -1; side <= 1; side += 2)
                        e.fx.Add(SpriteFactory.Make("Cone", e.fxRoot, SpriteFactory.Circle, new Color(1f, 0.5f, 0.08f, 1f),
                            e.pos + t * (k * 0.45f) + r * (side * sg.width * 0.42f), Vector2.one * 0.09f, 30));
                // Pavimentadora parada encima: bloquea el carril.
                VehicleCatalog.NextLabel = "Pavimentadora";
                e.blocker = MakeBlocker(sg, s, VehicleType.Truck, null, 600f);
                if (e.blocker == null) { Destroy(e.fxRoot.gameObject); continue; }
                events.Add(e);
                Message("Obras en la calzada: un carril cortado un rato.");
                return true;
            }
            return false;
        }

        bool SpotFree(RoadSeg sg, float s, float room)
        {
            foreach (var v in sg.vehicles) if (Mathf.Abs(v.s - s) < room + v.HalfLength) return false;
            return true;
        }

        /// <summary>Vehículo de servicio parado en el carril (obstáculo real: los demás esperan o cambian de carril).</summary>
        VehicleController MakeBlocker(RoadSeg sg, float s, VehicleType type, Color? col, float seconds)
        {
            var route = new List<RoadSeg> { sg };
            var v = new VehicleController(nextId++, type, route, root);
            v.obstacle = true;
            v.holdTimer = seconds;
            v.PlaceAt(s);
            if (col.HasValue) v.Tint(col.Value);
            AddVehicle(v);
            return v;
        }

        void RemoveVehicle(VehicleController v)
        {
            if (v == null) return;
            int i = Vehicles.IndexOf(v);
            if (i >= 0) RemoveAt(i);
            wet.Remove(v);
        }

        void StepEvent(WorldEvent e, float dt)
        {
            switch (e.kind)
            {
                case 1:
                    // Avería: a los 12 s llega la grúa (delante, en la berma) y a los 20 s se los lleva a los dos.
                    if (e.target == null || !Vehicles.Contains(e.target)) { e.done = true; return; }
                    e.target.holdTimer = Mathf.Max(e.target.holdTimer, 1f);
                    if (e.blocker == null && e.t > 12f && e.seg.Length - e.target.s > 1.2f)
                    {
                        VehicleCatalog.NextLabel = "Grúa";
                        e.blocker = MakeBlocker(e.seg, e.target.s + e.target.HalfLength + 0.55f, VehicleType.Truck, null, 600f);
                        if (e.blocker != null) { e.blocker.onShoulder = true; e.blocker.shoulderSide = e.target.shoulderSide; }
                    }
                    if (e.t > 20f)
                    {
                        RemoveVehicle(e.target);
                        RemoveVehicle(e.blocker);
                        e.done = true;
                    }
                    break;
                case 2:
                    // Taxi: baja una persona hacia la acera mientras está parado.
                    if (e.target == null || !Vehicles.Contains(e.target)) { e.done = true; return; }
                    if (e.fxRoot == null && e.t > 1.2f)
                    {
                        e.fxRoot = new GameObject("Passenger").transform;
                        e.fxRoot.SetParent(root, false);
                        e.fx.Add(SpriteFactory.Make("Passenger", e.fxRoot, SpriteFactory.Circle, SpriteFactory.Hex("#3A6EA5"), e.target.Pos, Vector2.one * 0.12f, 31));
                    }
                    if (e.fx.Count > 0)
                    {
                        Vector2 d = e.target.Dir;
                        Vector2 side = new Vector2(d.y, -d.x) * e.target.shoulderSide;
                        float k = Mathf.Clamp01((e.t - 1.2f) / 2.5f);
                        e.fx[0].transform.position = e.target.Pos + side * (0.2f + k * 0.6f);
                        e.fx[0].color = new Color(0.23f, 0.43f, 0.65f, 1f - Mathf.Clamp01((e.t - 3.5f) / 1f));
                    }
                    if (e.target.holdTimer <= 0f) { e.target.onShoulder = false; e.done = true; }
                    break;
                case 3:
                    // Incendio: llamas; a los 4 s llegan los bomberos al carril más cercano y echan agua 20 s.
                    for (int i = 0; i < e.fx.Count; i++)
                    {
                        var f = e.fx[i];
                        if (f == null || f.name != "Flame") continue;
                        float fl = 0.25f + 0.12f * Mathf.Sin(SimTime * 9f + i * 1.7f);
                        float fade = e.blocker != null ? Mathf.Clamp01(1f - (e.t - 8f) / 14f) : 1f;
                        f.transform.localScale = Vector3.one * fl * fade;
                    }
                    if (e.blocker == null && e.t > 4f)
                    {
                        if (SpotFree(e.seg, e.s, 0.9f)) e.blocker = MakeBlocker(e.seg, e.s, VehicleType.FireTruck, null, 600f);
                        else if (e.t > 30f) { e.done = true; return; }
                    }
                    if (e.blocker != null)
                    {
                        // Chorro de agua del camión al edificio.
                        if (e.fx.Count < 7)
                        {
                            Vector2 a = e.blocker.Pos, b = e.pos, dv = b - a;
                            e.fx.Add(SpriteFactory.Make("Water", e.fxRoot, SpriteFactory.Square, new Color(0.55f, 0.8f, 1f, 0.7f), (a + b) * 0.5f, new Vector2(dv.magnitude, 0.05f), 41, Geo.Angle(dv)));
                        }
                        if (e.t > 26f) { RemoveVehicle(e.blocker); e.done = true; }
                    }
                    break;
                case 5:
                    // Semáforo averiado: 40 s parpadeando en ámbar; luego vuelve a funcionar.
                    if (e.t > 40f) { if (e.ic != null) { e.ic.broken = false; e.ic.light.RestartCycle(); } e.done = true; Message("El semáforo ya funciona."); }
                    break;
                case 6:
                    {
                        // Farola rota: parpadeos irregulares durante 35 s.
                        float flick = Mathf.PerlinNoise(SimTime * 6f, e.pos.x) > 0.62f ? 1f : 0f;
                        float night = Mathf.Clamp01(DayNight.Darkness * 1.3f + 0.25f);
                        if (e.fx.Count > 1 && e.fx[0] != null) { e.fx[0].color = new Color(1f, 0.85f, 0.45f, 0.5f * flick * night); e.fx[1].color = new Color(1f, 1f, 0.8f, flick); }
                        if (e.t > 35f) e.done = true;
                        break;
                    }
                case 7:
                    {
                        // Boca de incendios: chorro de agua y charco 35 s; luego llegan a cerrarla.
                        float k = Mathf.Clamp01(e.t / 3f) * Mathf.Clamp01((35f - e.t) / 4f);
                        if (e.fx.Count > 0 && e.fx[0] != null) e.fx[0].color = new Color(0.45f, 0.62f, 0.8f, 0.55f * k);
                        for (int i = 2; i < e.fx.Count; i++)
                        {
                            if (e.fx[i] == null) continue;
                            float ph = Mathf.Repeat(SimTime * 1.3f + i * 0.137f, 1f);
                            Vector2 dirv = (e.seg.path.PointAt(e.s) - e.pos).normalized;
                            Vector2 off = dirv * ph * 0.9f + new Vector2(-dirv.y, dirv.x) * Mathf.Sin(i * 2.3f) * ph * 0.35f;
                            e.fx[i].transform.position = e.pos + off;
                            e.fx[i].color = new Color(0.85f, 0.93f, 1f, 0.8f * (1f - ph) * k);
                        }
                        if (e.t > 35f) { VehicleController.SlowZones.Remove(e.zone); e.done = true; }
                        break;
                    }
                case 4:
                    // Obras: a los 25 s la pavimentadora termina, se quitan los conos y el carril queda como nuevo.
                    if (e.blocker == null || !Vehicles.Contains(e.blocker) || e.t > 24f)
                    {
                        RemoveVehicle(e.blocker);
                        e.done = true;
                    }
                    else if (e.fx.Count > 0 && e.fx[0] != null)
                        e.fx[0].color = Color.Lerp(new Color(0.25f, 0.22f, 0.2f, 0.9f), new Color(0.35f, 0.35f, 0.37f, 0f), e.t / 24f);
                    break;
            }
        }
    }
}
