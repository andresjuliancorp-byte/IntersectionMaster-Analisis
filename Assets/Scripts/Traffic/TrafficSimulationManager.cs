using System;
using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Core;
using IntersectionMaster.Data;
using IntersectionMaster.Systems;

namespace IntersectionMaster.Traffic
{
    /// <summary>
    /// Orquesta la simulación: red vial, cruces, generación de vehículos, paso de simulación a paso fijo
    /// (30 Hz, con aceleración de tiempo), colisiones y puntuación.
    /// </summary>
    public partial class TrafficSimulationManager : MonoBehaviour
    {
        const float Step = 1f / 30f;
        const float CrashSeconds = 8f;
        /// <summary>Paciencia de los conductores: en «Puntuación más alta» se acaba antes con el tiempo (como el original).</summary>
        float BlockageSeconds { get { return rampDifficulty ? Mathf.Max(20f, 45f - SimTime / 12f) : 45f; } }

        public MapData Map { get; private set; }
        public readonly RoadNetworkManager Network = new RoadNetworkManager();
        public readonly PathfindingSystem Pathfinding = new PathfindingSystem();
        public readonly CollisionDetectionSystem Collisions = new CollisionDetectionSystem();
        public readonly ScoringSystem Scoring = new ScoringSystem();
        public readonly PedestrianSystem Pedestrians = new PedestrianSystem();
        public readonly List<VehicleController> Vehicles = new List<VehicleController>();
        /// <summary>Cruces con semáforo (los que el jugador puede controlar).</summary>
        public readonly List<IntersectionController> Intersections = new List<IntersectionController>();
        /// <summary>Todos los cruces: con semáforo, con señales de stop/ceda y entradas de rotonda.</summary>
        public readonly List<IntersectionController> AllControllers = new List<IntersectionController>();

        public bool Running { get; private set; }
        public bool Paused;
        public float TimeScale = 1f;
        /// <summary>Cantidad de tráfico al jugar (0..1), como la barra del original.</summary>
        public float TrafficAmount = 1f;
        public float SimTime { get; private set; }
        public Action<string> OnMessage;

        readonly List<KeyValuePair<VehicleController, VehicleController>> hits = new List<KeyValuePair<VehicleController, VehicleController>>();
        readonly List<NetNode> entryShuffle = new List<NetNode>();
        readonly List<CrashFx> fx = new List<CrashFx>();
        Transform root;
        int nextId;
        float accumulator;
        int pendingSpawns;
        bool rampDifficulty;

        class CrashFx { public Transform t; public SpriteRenderer sr; public float age; }

        public void Begin(MapData map, bool rampDifficulty)
        {
            Stop();
            Map = map;
            this.rampDifficulty = rampDifficulty;
            map.EnsureLanes();
            Network.Build(map);

            Intersections.Clear();
            AllControllers.Clear();
            if (Network.LaneMode)
            {
                foreach (var ic in IntersectionController.BuildLaneGroups(Network, map.traffic))
                {
                    AllControllers.Add(ic);
                    Intersections.Add(ic);
                }
            }
            else
            {
                foreach (var n in Network.nodes)
                {
                    if (!n.IsJunction) continue;
                    var ic = new IntersectionController(n, map.traffic);
                    AllControllers.Add(ic);
                    if (ic.IsLit) Intersections.Add(ic);
                }
            }
            walkersArrived = 0;
            agentTimers[0] = agentTimers[1] = agentTimers[2] = agentTimers[3] = agentTimers[4] = 0f;

            root = new GameObject("SimulationRoot").transform;
            root.SetParent(transform, false);
            Pedestrians.Build(map, Network, root);
            Scoring.Reset();
            SimTime = 0f;
            TrafficAmount = Mathf.Clamp(map.trafficAmount <= 0.01f ? 1f : map.trafficAmount, 0.25f, 3f);
            SpawnedVehicles = SpawnedWalkers = 0;
            ResetEvents();
            Raining = false; rainRoot = null; drops.Clear(); VehicleController.RainNow = false; AudioManager.Instance.SetRain(false);
            accumulator = 0f;
            pendingSpawns = 1;
            nextId = 1;
            TimeScale = 1f;
            Paused = false;
            Running = true;
            DayNight.Begin(map.startHour);   // el día avanza desde la «Hora de inicio» del mapa
        }

        public void Stop()
        {
            Running = false;
            TrainPositions.Clear();
            DayNight.End();
            Vehicles.Clear();
            Intersections.Clear();
            AllControllers.Clear();
            Pedestrians.Clear();
            fx.Clear();
            if (root != null) Destroy(root.gameObject);
            root = null;
        }

        void Update()
        {
            if (!Running || Paused) return;

            float dt = Time.deltaTime * TimeScale;
            DayNight.Advance(dt);
            accumulator += dt;
            int steps = 0;
            while (accumulator >= Step && steps < 10)
            {
                SimulateStep(Step);
                accumulator -= Step;
                steps++;
            }
            if (steps == 10) accumulator = 0f;

            for (int i = 0; i < Vehicles.Count; i++) Vehicles[i].UpdateVisual(dt, SimTime);
            UpdateFx(Time.deltaTime);
        }

        void SimulateStep(float dt)
        {
            SimTime += dt;
            for (int i = 0; i < Intersections.Count; i++) Intersections[i].light.Update(dt);

            SpawnStep(dt);
            EventsStep(dt);

            float speedFactor = Map.traffic.speedFactor;
            for (int i = 0; i < Vehicles.Count; i++) Vehicles[i].Tick(dt, speedFactor);
            for (int i = 0; i < Vehicles.Count; i++) TryLaneChange(Vehicles[i]);
            DetectorStep(dt);

            Pedestrians.Step(dt, Vehicles, Map.traffic.pedestriansPerMinute);
            Scoring.pedestriansCrossed = Network.LaneMode ? walkersArrived : Pedestrians.Crossed;
            AudioManager.Instance.SetTrafficLevel(Vehicles.Count);
            TrainSounds(Time.deltaTime);

            for (int i = Vehicles.Count - 1; i >= 0; i--)
            {
                var v = Vehicles[i];
                if (v.arrived)
                {
                    if (v.agent == 0 && !v.obstacle)
                    {
                        Scoring.OnDelivered(v);
                        AudioManager.Instance.PlayDelivered();
                    }
                    else if (v.agent == 1) walkersArrived++;
                    RemoveAt(i);
                }
                else if (v.crashed && v.crashTimer <= 0f)
                {
                    RemoveAt(i);
                }
            }

            // Como el original: en Diverso y Simulación los vehículos nunca chocan; solo en Controlador de tráfico.
            if (!Network.LaneMode || Map.playMode == 2) Collisions.Detect(Vehicles, hits);
            else hits.Clear();
            for (int i = 0; i < hits.Count; i++)
            {
                var a = hits[i].Key;
                var b = hits[i].Value;
                a.Crash(CrashSeconds);
                b.Crash(CrashSeconds);
                Scoring.OnAccident();
                SpawnCrashFx((a.VisualPos + b.VisualPos) * 0.5f);
                AudioManager.Instance.PlayCrash(a.spec.length >= 0.75f || b.spec.length >= 0.75f);
                Message("¡Choque!");
                if (SaveSystem.Progress.redOnCrash) foreach (var ic in Intersections) ic.light.AllRed();
            }

            for (int i = 0; i < Vehicles.Count; i++)
            {
                var v = Vehicles[i];
                if (v.agent != 0 || v.obstacle) continue;
                if (!v.crashed && !v.honked && v.waitTime > 14f)
                {
                    v.honked = true;
                    AudioManager.Instance.PlayHorn(v.spec.length >= 0.75f);
                }
                if (!v.crashed && !v.blockCounted && v.totalWait > BlockageSeconds)
                {
                    v.blockCounted = true;
                    Scoring.OnBlockage();
                    AudioManager.Instance.PlayPenalty();
                    Message("Vehículo atascado demasiado tiempo");
                }
            }

            Scoring.Sample(dt, Vehicles);
        }

        // ---------------- Generación de tráfico ----------------

        void SpawnStep(float dt)
        {
            float ramp = rampDifficulty ? Mathf.Min(3f, 1f + SimTime / 90f) : 1f;
            float rate = Map.traffic.spawnPerMinute / 60f * ramp * TrafficAmount;
            if (UnityEngine.Random.value < rate * dt && pendingSpawns < 8) pendingSpawns++;
            if (Network.LaneMode)
            {
                // Peatones, bicicletas, trenes y barcos: cada uno por sus carriles y a su ritmo.
                SpawnAgent(dt, 1, RoadData.LaneSidewalk, Map.traffic.pedestriansPerMinute);
                SpawnAgent(dt, 3, RoadData.LaneBike, Map.traffic.spawnPerMinute * 0.25f);
                SpawnAgent(dt, 2, RoadData.LaneRail, 1.5f);
                SpawnAgent(dt, 4, RoadData.LaneBoat, 2f);
            }
            if (CountVehicles() >= Map.traffic.maxVehicles) return;
            if (Network.LaneMode) SpawnFromStartNodes(dt);
            if (pendingSpawns > 0 && TrySpawn()) pendingSpawns--;
        }

        static readonly VehicleType[] StartNodeTypes = { VehicleType.Car, VehicleType.Truck, VehicleType.Bus, VehicleType.Motorbike, VehicleType.Tram };

        /// <summary>Nodos de inicio con tráfico propio (como el original): cada tipo sale a su ritmo en vehículos por minuto.</summary>
        void SpawnFromStartNodes(float dt)
        {
            foreach (var e in Network.entries)
            {
                if (e.data == null || !e.data.CustomRates) continue;
                for (int i = 0; i < NodeData.RateCount; i++)
                {
                    float r = e.data.GetRate(i) * TrafficAmount;
                    if (r <= 0f || UnityEngine.Random.value >= r / 60f * dt) continue;
                    if (CountVehicles() >= Map.traffic.maxVehicles) return;
                    SpawnAt(e, StartNodeTypes[i]);
                }
            }
        }

        bool SpawnAt(NetNode entry, VehicleType type)
        {
            var lanes = Network.SpawnLanes(entry);
            lanes.RemoveAll(l => l.laneType != RoadData.LaneVehicle);
            if (lanes.Count == 0) return false;
            int offset = UnityEngine.Random.Range(0, lanes.Count);
            for (int k = 0; k < lanes.Count; k++)
            {
                var lane = lanes[(offset + k) % lanes.Count];
                if (!LaneStartFree(lane, 0.9f)) continue;
                bool chaotic = Map.traffic.simMode == 1 && UnityEngine.Random.value < ChaosProfile.ViolationChance(Map.traffic);
                var route = Pathfinding.FindRoute(lane, Network.exits, type == VehicleType.Tram ? PathfindingSystem.TramBit : (chaotic ? 0 : 1 << (int)type));
                if (route == null || route.Count == 0) continue;
                var v = new VehicleController(nextId++, type, route, root);
                v.spec.maxSpeed *= TrafficSettings.Mul(Map.traffic.typeSpeed, (int)type);
                v.spec.accel *= TrafficSettings.Mul(Map.traffic.typeAccel, (int)type);
                float gm = TrafficSettings.Mul(Map.traffic.typeGap, (int)type);
                v.spec.minGap *= gm; v.spec.headway *= gm;
                if (chaotic)
                {
                    v.chaotic = true;
                    v.chaosSpeed = ChaosProfile.Speed(Map.traffic.chaosLevel);
                    v.chaosGap = ChaosProfile.Gap(Map.traffic.chaosLevel);
                }
                AddVehicle(v);
                return true;
            }
            return false;
        }

        int walkersArrived;
        /// <summary>Vehículos y peatones que han salido desde el inicio (para «vehículos por min» de la barra de arriba).</summary>
        public int SpawnedVehicles, SpawnedWalkers;

        float trainSoundTimer;
        /// <summary>Dónde están los trenes (para las luces de los pasos a nivel).</summary>
        public static readonly List<Vector2> TrainPositions = new List<Vector2>();

        /// <summary>Tren en marcha (rumor) y campana del paso a nivel mientras un tren se acerca a una calle o la cruza.</summary>
        void TrainSounds(float dt)
        {
            trainSoundTimer -= dt;
            if (trainSoundTimer > 0f) return;
            trainSoundTimer = 0.25f;
            int running = 0; bool bell = false;
            TrainPositions.Clear();
            foreach (var v in Vehicles)
            {
                if (v.agent != 2 || v.crashed) continue;
                running++;
                TrainPositions.Add(v.Pos);
                TrainPositions.Add(v.Pos + v.Dir * v.HalfLength);
                TrainPositions.Add(v.Pos - v.Dir * v.HalfLength);
                var sg = v.Seg;
                if (sg == null) continue;
                foreach (var c in sg.conflicts)
                {
                    if (c.other == null || c.other.laneType == RoadData.LaneRail) continue;
                    float d = c.myS - v.s;
                    if (d > -c.clear - v.HalfLength && d < 6f) { bell = true; break; }
                }
            }
            AudioManager.Instance.SetTrains(running, bell);
        }

        void AddVehicle(VehicleController v)
        {
            Vehicles.Add(v);
            if (Raining && v.agent == 0) RainSpec(v);
            if (v.agent == 0 && v.spec.emergency) AudioManager.Instance.PlaySiren();
            if (v.agent == 2) AudioManager.Instance.PlayTrainHorn();
            if (v.agent == 0 && !v.obstacle) SpawnedVehicles++;
            else if (v.agent == 1) SpawnedWalkers++;
        }
        readonly float[] agentTimers = new float[5];

        int CountVehicles()
        {
            int n = 0;
            for (int i = 0; i < Vehicles.Count; i++) if (Vehicles[i].agent == 0) n++;
            return n;
        }

        void SpawnAgent(float dt, int agent, int laneType, float perMinute)
        {
            if (perMinute <= 0.01f || Vehicles.Count >= Map.traffic.maxVehicles + 60) return;
            agentTimers[agent] += dt;
            float interval = 60f / perMinute;
            if (agentTimers[agent] < interval * UnityEngine.Random.Range(0.6f, 1.4f)) return;
            var starts = new List<RoadSeg>();
            foreach (var e in Network.entries)
                foreach (var l in Network.SpawnLanes(e))
                    if (l.laneType == laneType && LaneStartFree(l, agent == 2 ? 3f : 0.6f) && !LitCrossingStart(l)) starts.Add(l);
            if (starts.Count == 0) { agentTimers[agent] = interval * 0.5f; return; }
            agentTimers[agent] = 0f;
            var lane = starts[UnityEngine.Random.Range(0, starts.Count)];
            var route = Pathfinding.FindRoute(lane, Network.exits, 0);
            if (route == null || route.Count == 0) return;
            AddVehicle(new VehicleController(nextId++, VehicleType.Car, route, root, agent));
        }

        /// <summary>
        /// Un peatón no aparece ya encima de un cruce con semáforo peatonal (se saltaría el semáforo): aparece en una acera
        /// que llega a él y espera en el borde.
        /// </summary>
        bool LitCrossingStart(RoadSeg l) { return IntersectionController.BlockedPedStart(l, AllControllers); }

        static bool LaneStartFree(RoadSeg lane, float room)
        {
            for (int i = 0; i < lane.vehicles.Count; i++)
                if (lane.vehicles[i].s - lane.vehicles[i].HalfLength < room) return false;
            return true;
        }

        readonly List<KeyValuePair<float, NetNode>> entryKeys = new List<KeyValuePair<float, NetNode>>();

        bool TrySpawn()
        {
            if (Network.entries.Count == 0 || Network.exits.Count == 0) return false;

            entryShuffle.Clear();
            // Orden aleatorio ponderado por el peso de la Tabla de tráfico (peso 0 = no genera).
            entryKeys.Clear();
            foreach (var e in Network.entries)
            {
                if (e.weight <= 0.001f) continue;
                if (e.data != null && e.data.CustomRates) continue;   // ese nodo de inicio genera su propio tráfico
                entryKeys.Add(new KeyValuePair<float, NetNode>(-Mathf.Log(Mathf.Max(1e-6f, UnityEngine.Random.value)) / e.weight, e));
            }
            entryKeys.Sort((x, y) => x.Key.CompareTo(y.Key));
            for (int i = 0; i < entryKeys.Count; i++) entryShuffle.Add(entryKeys[i].Value);

            foreach (var entry in entryShuffle)
            {
                var lanes = Network.SpawnLanes(entry);
                if (Network.LaneMode) lanes.RemoveAll(l => l.laneType != RoadData.LaneVehicle);
                if (lanes.Count == 0) continue;
                int offset = UnityEngine.Random.Range(0, lanes.Count);
                for (int k = 0; k < lanes.Count; k++)
                {
                    var lane = lanes[(offset + k) % lanes.Count];
                    bool free = true;
                    for (int i = 0; i < lane.vehicles.Count; i++)
                    {
                        if (lane.vehicles[i].s - lane.vehicles[i].HalfLength < 0.9f) { free = false; break; }
                    }
                    if (!free) continue;
                    // El tipo se elige antes: hay carriles que sólo admiten ciertos vehículos.
                    // En modo caótico, un porcentaje de conductores ignora las restricciones y las normas.
                    bool chaotic = Map.traffic.simMode == 1 && UnityEngine.Random.value < ChaosProfile.ViolationChance(Map.traffic);
                    var type = PickType();
                    int routeBits = type == VehicleType.Tram ? PathfindingSystem.TramBit : (chaotic ? 0 : 1 << (int)type);
                    var route = Pathfinding.FindRoute(lane, Network.exits, routeBits);
                    if ((route == null || route.Count == 0) && (!chaotic || type == VehicleType.Tram))
                    {
                        // El carril de entrada no admite ese tipo: se elige uno de los tipos que sí admite.
                        var allowed = new List<VehicleType>();
                        for (int vt = 0; vt < RoadData.VehicleTypeCount; vt++) if (lane.allow == 0 || (lane.allow & (1 << vt)) != 0) allowed.Add((VehicleType)vt);
                        while (allowed.Count > 0 && (route == null || route.Count == 0))
                        {
                            int pick = UnityEngine.Random.Range(0, allowed.Count);
                            type = allowed[pick];
                            allowed.RemoveAt(pick);
                            route = Pathfinding.FindRoute(lane, Network.exits, 1 << (int)type);
                        }
                    }
                    if (route == null || route.Count == 0) continue;
                    var v = new VehicleController(nextId++, type, route, root);
                    // Perfil del tipo (Ajustes de mapa → Perfiles de vehículo).
                    v.spec.maxSpeed *= TrafficSettings.Mul(Map.traffic.typeSpeed, (int)type);
                    v.spec.accel *= TrafficSettings.Mul(Map.traffic.typeAccel, (int)type);
                    float gm = TrafficSettings.Mul(Map.traffic.typeGap, (int)type);
                    v.spec.minGap *= gm; v.spec.headway *= gm;
                    if (chaotic)
                    {
                        v.chaotic = true;
                        v.chaosSpeed = ChaosProfile.Speed(Map.traffic.chaosLevel);
                        v.chaosGap = ChaosProfile.Gap(Map.traffic.chaosLevel);
                    }
                    AddVehicle(v);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Detectores de vehículos: si hay un vehículo esperando sobre el detector, piden verde para ese acceso.</summary>
        /// <summary>Detectores (ids de punto del mapa) con un vehículo encima ahora mismo; el dibujo los enciende.</summary>
        public static readonly HashSet<int> OccupiedDetectors = new HashSet<int>();

        void DetectorStep(float dt)
        {
            OccupiedDetectors.Clear();
            var lanes = Network.detectorLanes;
            for (int i = 0; i < lanes.Count; i++)
            {
                var lane = lanes[i];
                var ic = lane.toNode != null ? lane.toNode.controller : null;
                for (int k = 0; k < lane.stops.Count; k++)
                {
                    var sp = lane.stops[k];
                    if (sp.kind != PointKind.Detector) continue;
                    // Ocupado: algún vehículo tiene la carrocería sobre el lazo (lógica, aparte del dibujo).
                    bool occupied = false, present = false;
                    for (int v = 0; v < lane.vehicles.Count; v++)
                    {
                        var veh = lane.vehicles[v];
                        if (veh.crashed || veh.agent != 0) continue;
                        float d = sp.s - veh.s;
                        if (Mathf.Abs(d) < veh.HalfLength + 0.08f) occupied = true;
                        if (d > -0.4f && d < 2.5f) present = true;   // llegando o esperando sobre el detector
                    }
                    if (occupied && sp.srcId >= 0) OccupiedDetectors.Add(sp.srcId);
                    // Una sola petición por vehículo: tras pedir verde espera (cooldown) y no vuelve a pedir mientras sigue ahí.
                    if (sp.cooldown > 0f) { sp.cooldown -= dt; continue; }
                    if (!present || ic == null || !ic.IsLit || !ic.light.Has(lane.toApp)) continue;
                    if (!ic.light.IsOn(lane.toApp)) { ic.light.RequestGreen(lane.toApp); sp.cooldown = 8f; }
                }
            }
        }

        readonly List<RoadSeg> altRoute = new List<RoadSeg>();
        readonly List<RoadSeg> curRoute = new List<RoadSeg>();

        /// <summary>Adelanta por el carril contiguo cuando un líder lento bloquea y hay hueco libre.</summary>
        void TryLaneChange(VehicleController v)
        {
            float needBlocked = v.chaotic ? 0.3f : 1.5f;
            if (v.crashed || v.arrived || v.blockedTime < needBlocked || (v.laneCooldown > 0f && !v.chaotic)) return;
            var seg = v.Seg;
            if (seg.kind != SegKind.Lane || seg.noChange || seg.laneSet == null || seg.laneSet.Length < 2) return;
            if (v.NextSeg == null || seg.Length - v.s < 2.5f) return;
            var last = v.route[v.route.Count - 1];
            var exit = last.toNode;
            if (exit == null) return;
            int bit = v.chaotic ? 0 : 1 << (int)v.type;
            float curCost = -1f;
            for (int dir = -1; dir <= 1; dir += 2)
            {
                int idx = seg.laneIndex + dir;
                if (idx < 0 || idx >= seg.laneSet.Length) continue;
                var adj = seg.laneSet[idx];
                if (adj == null || adj == seg) continue;
                if (!v.chaotic && adj.allow != 0 && (adj.allow & bit) == 0) continue;
                float sa;
                if (seg.laneMode)
                {
                    float dd;
                    sa = adj.path.Project(v.Pos, out dd);
                    if (sa < 0.3f || sa > adj.Length - 1.5f) continue;
                }
                else sa = v.s / Mathf.Max(0.01f, seg.Length) * adj.Length;
                bool free = true;
                float ahead = float.MaxValue;
                for (int i = 0; i < adj.vehicles.Count; i++)
                {
                    var o = adj.vehicles[i];
                    float d = o.s - sa;
                    if (Mathf.Abs(d) < v.HalfLength + o.HalfLength + 0.6f) { free = false; break; }
                    if (d > 0f && d < ahead) ahead = d;
                }
                if (!free || ahead < (v.chaotic ? 0.8f : 3f)) continue;
                if (curCost < 0f)
                {
                    curCost = Pathfinding.RouteTo(seg, exit, bit, curRoute);
                    if (curCost < 0f) curCost = 1e9f;
                }
                float altCost = Pathfinding.RouteTo(adj, exit, bit, altRoute);
                if (altCost < 0f || altCost > curCost + 3f) continue;
                v.ChangeLane(adj, altRoute, seg.laneMode ? sa : -1f);
                return;
            }
            v.blockedTime = 0.5f;
        }

        VehicleType PickType()
        {
            var t = Map.traffic;
            float r = UnityEngine.Random.value;
            if ((r -= t.emergencyChance) < 0f)
            {
                int k = UnityEngine.Random.Range(0, 3);
                return k == 0 ? VehicleType.Ambulance : (k == 1 ? VehicleType.FireTruck : VehicleType.Police);
            }
            // Perfil de tráfico del mapa (como el original): el centro tiene más taxis, buses y motos; el industrial,
            // muchos camiones y furgonetas.
            float taxi = t.taxiChance, bus = t.busChance, truck = t.truckChance, moto = t.motorbikeChance, van = t.vanChance;
            if (Map.trafficProfile == 1) { taxi *= 2f; bus *= 1.8f; moto *= 1.5f; truck *= 0.4f; }
            else if (Map.trafficProfile == 2) { truck *= 3.5f; van *= 2f; taxi *= 0.3f; bus *= 0.4f; moto *= 0.5f; }
            if ((r -= taxi) < 0f) return VehicleType.Taxi;
            if ((r -= bus) < 0f) return VehicleType.Bus;
            if ((r -= truck) < 0f) return VehicleType.Truck;
            if ((r -= moto) < 0f) return VehicleType.Motorbike;
            if ((r -= van) < 0f) return VehicleType.Van;
            if (Network != null && Network.LaneMode && (r -= t.tramChance) < 0f) return VehicleType.Tram;
            return VehicleType.Car;
        }

        void RemoveAt(int i)
        {
            Vehicles[i].Destroy();
            Vehicles.RemoveAt(i);
        }

        void Message(string text)
        {
            if (OnMessage != null) OnMessage(text);
        }

        // ---------------- Consultas para la interfaz ----------------

        public IntersectionController FindNearestIntersection(Vector2 world, float maxDist)
        {
            IntersectionController best = null;
            float bestD = maxDist;
            for (int i = 0; i < Intersections.Count; i++)
            {
                float d = Vector2.Distance(Intersections[i].node.pos, world);
                if (d < bestD) { bestD = d; best = Intersections[i]; }
            }
            return best;
        }

        /// <summary>Vehículos detenidos en los carriles que llegan a una aproximación.</summary>
        public int QueueLength(Approach approach)
        {
            int n = 0;
            for (int l = 0; l < approach.inLanes.Count; l++)
            {
                var lane = approach.inLanes[l];
                for (int i = 0; i < lane.vehicles.Count; i++)
                    if (lane.vehicles[i].speed < 0.1f) n++;
            }
            return n;
        }

        // ---------------- Efectos ----------------

        void SpawnCrashFx(Vector2 pos)
        {
            if (root == null) return;
            var sr = SpriteFactory.Make("CrashFx", root, SpriteFactory.Ring, new Color(1f, 0.25f, 0.2f, 0.9f), pos, Vector2.one * 0.2f, 30);
            fx.Add(new CrashFx { t = sr.transform, sr = sr, age = 0f });
        }

        void UpdateFx(float dt)
        {
            for (int i = fx.Count - 1; i >= 0; i--)
            {
                var f = fx[i];
                f.age += dt;
                float k = f.age / 0.9f;
                if (k >= 1f)
                {
                    if (f.t != null) Destroy(f.t.gameObject);
                    fx.RemoveAt(i);
                    continue;
                }
                f.t.localScale = Vector3.one * Mathf.Lerp(0.2f, 1.4f, k);
                var c = f.sr.color; c.a = 0.9f * (1f - k); f.sr.color = c;
            }
        }
    }
}
