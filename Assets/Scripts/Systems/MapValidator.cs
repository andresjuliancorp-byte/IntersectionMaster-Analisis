using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Core;
using IntersectionMaster.Data;
using IntersectionMaster.Traffic;

namespace IntersectionMaster.Systems
{
    /// <summary>Error: impide iniciar la simulación (el mapa sigue siendo editable). Warning: se puede simular, pero conviene revisarlo.</summary>
    public enum IssueSeverity { Error, Warning }

    /// <summary>Qué parte del mapa falla (sirve para agrupar y explicar el problema).</summary>
    public enum IssueCategory { Setup, Connection, Geometry, Route, Traffic, Info }

    /// <summary>
    /// Estado del mapa según su validación. La edición NUNCA se bloquea: solo se decide si se puede simular.
    /// Editing  = hay problemas que impiden simular (mapa incompleto o con conexiones/geometría rotas).
    /// Warnings = se puede simular, pero hay avisos.
    /// Ready    = sin problemas.
    /// </summary>
    public enum MapReadiness { Editing, Warnings, Ready }

    public class MapIssue
    {
        public IssueSeverity severity;
        public IssueCategory category;
        public string what;      // qué falla (con el elemento y su posición)
        public string fix;       // cómo corregirlo
        public string message;   // texto completo mostrado al jugador: qué + cómo corregirlo
        public bool hasPos;
        public Vector2 pos;
    }

    /// <summary>
    /// Valida un mapa antes de simularlo: datos y conexiones, geometría, entradas/salidas, giros y rutas posibles.
    /// Solo debe llamarse al pulsar «Validar» o al intentar simular: no interviene mientras se construye.
    /// </summary>
    public static class MapValidator
    {
        /// <summary>Dos extremos de calle más cerca que esto sin estar unidos se consideran un hueco (conexión rota).</summary>
        public const float GapTolerance = 0.9f;
        /// <summary>Un extremo más cerca que esto del eje de otra calle (sin unirse) se considera pegado sin conexión.</summary>
        public const float TouchTolerance = 0.3f;

        static string MaskText(int m)
        {
            var parts = new List<string>();
            if ((m & 1) != 0) parts.Add("izquierda");
            if ((m & 2) != 0) parts.Add("recto");
            if ((m & 4) != 0) parts.Add("derecha");
            return string.Join(" + ", parts.ToArray());
        }

        static string P(Vector2 p) { return "(" + p.x.ToString("0.#") + ", " + p.y.ToString("0.#") + ")"; }

        static bool Bad(float f) { return float.IsNaN(f) || float.IsInfinity(f); }

        public static List<MapIssue> Validate(MapData m)
        {
            var issues = new List<MapIssue>();
            try
            {
                Run(m, issues);
            }
            catch (System.Exception ex)
            {
                Add(issues, IssueSeverity.Error, IssueCategory.Geometry,
                    "No se pudo analizar el mapa (" + ex.GetType().Name + ": " + ex.Message + "). Hay datos dañados, normalmente una calle o un nodo mal formado.",
                    "Pulsa Deshacer hasta antes de la última calle creada o movida, o bórrala con la herramienta Borrar.");
            }
            return issues;
        }

        /// <summary>
        /// Validación del modelo por carriles (como Intersection Controller 2): no hay entradas ni salidas que poner;
        /// un carril sin nada antes genera vehículos y uno sin nada después los retira. Solo bloquea la simulación lo que
        /// la hace imposible (datos dañados, ningún carril de entrada, ninguna ruta completa).
        /// </summary>
        static void RunLanes(MapData m, List<MapIssue> issues)
        {
            // Datos dañados.
            bool broken = false;
            foreach (var n in m.nodes)
                if (Bad(n.x) || Bad(n.y))
                {
                    Add(issues, IssueSeverity.Error, IssueCategory.Geometry, "Hay un nodo con posición inválida.", "Pulsa Deshacer o borra ese nodo.");
                    broken = true; break;
                }
            foreach (var r in m.roads)
            {
                if (m.Node(r.a) == null || m.Node(r.b) == null)
                {
                    Add(issues, IssueSeverity.Error, IssueCategory.Connection, "Un carril está unido a un nodo que ya no existe.", "Pulsa Deshacer o borra ese carril.");
                    broken = true; break;
                }
            }
            if (broken) return;

            var net = new RoadNetworkManager();
            net.Build(m);
            var path = new PathfindingSystem();
            Rect view = m.View;

            if (m.traffic.spawnPerMinute <= 0f)
                Add(issues, IssueSeverity.Error, IssueCategory.Traffic,
                    "La cantidad de tráfico está en 0: no aparecerá ningún vehículo.",
                    "Abre Menú → Tabla de tráfico y sube los vehículos por minuto.");

            // Entradas de vehículos (automáticas).
            var vehEntries = new List<RoadSeg>();
            foreach (var e in net.entries)
                foreach (var l in net.SpawnLanes(e))
                    if (l.laneType == RoadData.LaneVehicle) vehEntries.Add(l);
            if (vehEntries.Count == 0)
            {
                Add(issues, IssueSeverity.Error, IssueCategory.Setup,
                    "Ningún carril de vehículos trae coches: todos los carriles tienen otro carril antes (o forman un circuito cerrado).",
                    "Dibuja un carril de vehículos que empiece fuera de la línea roja y lleve al resto de la red.");
            }
            else
            {
                int ok = 0;
                Vector2 firstCut = Vector2.zero; bool haveCut = false; Vector2 firstStart = vehEntries[0].path.Start;
                foreach (var lane in vehEntries)
                {
                    path.Prepare(lane);
                    bool reach = false;
                    foreach (var x in net.exits) if (x != lane.fromNode && path.LastRunReaches(x)) { reach = true; break; }
                    if (reach) ok++;
                    else if (!haveCut) { haveCut = path.LastRunDeadEnd(out firstCut); firstStart = lane.path.Start; }
                }
                if (ok == 0)
                    Add(issues, IssueSeverity.Error, IssueCategory.Route,
                        "Ningún coche tiene adónde ir: desde los carriles de entrada no se llega a ningún final de carril.",
                        "Une los carriles en sus extremos (empieza a dibujar sobre el final de otro carril).", haveCut ? firstCut : firstStart);
            }

            foreach (var l in vehEntries)
                if (view.Contains(l.path.Start))
                    Add(issues, IssueSeverity.Warning, IssueCategory.Setup,
                        "Un carril de vehículos empieza dentro del área de juego " + P(l.path.Start) + ": los coches aparecerán a la vista.",
                        "Alarga ese carril hasta fuera de la línea roja (arrastra su nodo inicial).", l.path.Start);
            foreach (var x in net.exits)
            {
                var hub = net.HubOf(x.id);
                if (hub == null || hub.inLanes.Count == 0 || hub.inLanes[0].laneType != RoadData.LaneVehicle) continue;
                if (view.Contains(x.pos))
                    Add(issues, IssueSeverity.Warning, IssueCategory.Setup,
                        "Un carril de vehículos termina dentro del área de juego " + P(x.pos) + ": los coches desaparecerán a la vista.",
                        "Alarga ese carril hasta fuera de la línea roja o únelo con otro carril.", x.pos);
            }

            // Carriles muy cortos y extremos que casi se tocan sin unirse.
            foreach (var r in m.roads)
            {
                float len = m.CenterOf(r).Length;
                if (len < 0.2f)
                    Add(issues, IssueSeverity.Warning, IssueCategory.Geometry,
                        "Hay un carril diminuto en " + P(m.PointOnRoad(r, 0.5f)) + ".",
                        "Bórralo o une sus dos nodos arrastrando uno sobre el otro.", m.PointOnRoad(r, 0.5f));
            }
            foreach (var x in net.exits)
            {
                foreach (var e in net.entries)
                {
                    if (Vector2.Distance(x.pos, e.pos) > 0.15f) continue;
                    Add(issues, IssueSeverity.Warning, IssueCategory.Connection,
                        "Dos carriles casi se tocan en " + P(x.pos) + " pero no están unidos.",
                        "Arrastra uno de los nodos encima del otro para unirlos.", x.pos);
                }
            }

            // Tipos distintos unidos en un mismo nodo.
            foreach (var n in m.nodes)
            {
                int t = -1; bool mixed = false;
                foreach (var r in m.RoadsAt(n.id)) { if (t < 0) t = r.laneType; else if (r.laneType != t) mixed = true; }
                if (mixed)
                    Add(issues, IssueSeverity.Warning, IssueCategory.Connection,
                        "En " + P(n.Pos) + " se unen carriles de distinto tipo: no se conectan entre sí.",
                        "Separa ese nodo o cambia los carriles para que sean del mismo tipo.", n.Pos);
            }

            foreach (var b in m.buttons)
                if (b.links.Count == 0)
                    Add(issues, IssueSeverity.Warning, IssueCategory.Info,
                        "El botón de " + P(b.Pos) + " no tiene semáforos vinculados.",
                        "Selecciona el botón y vincúlalo a los semáforos que debe controlar.", b.Pos);
        }

        static void Run(MapData m, List<MapIssue> issues)
        {
            if (m.roads.Count == 0)
            {
                Add(issues, IssueSeverity.Error, IssueCategory.Setup,
                    "El mapa todavía no tiene calles.",
                    "Elige la herramienta Calle y arrastra el dedo sobre el mapa para dibujar la primera.");
                return;
            }

            if (m.IsLaneMap) { RunLanes(m, issues); return; }

            // ---------- 1. Datos, conexiones y geometría (antes de construir la red de tráfico) ----------
            int structural = StructuralChecks(m, issues);

            // Si hay calles sin nodos o con números inválidos no se puede construir la red: se informa y se para aquí.
            if (structural < 0) return;

            var net = new RoadNetworkManager();
            net.Build(m);
            var path = new PathfindingSystem();

            // ---------- 2. Configuración mínima para simular ----------
            if (net.entries.Count == 0)
                Add(issues, IssueSeverity.Error, IssueCategory.Setup,
                    "Falta una entrada de vehículos: sin ella nadie aparece.",
                    "Elige la herramienta Entrada y toca el extremo de una calle (fuera del área visible, línea roja).");
            if (net.exits.Count == 0)
                Add(issues, IssueSeverity.Error, IssueCategory.Setup,
                    "Falta una salida de vehículos: los coches no tendrían a dónde ir.",
                    "Elige la herramienta Salida y toca el extremo de otra calle.");
            if (m.traffic.spawnPerMinute <= 0f)
                Add(issues, IssueSeverity.Error, IssueCategory.Traffic,
                    "La cantidad de tráfico está en 0: no aparecerá ningún vehículo.",
                    "Abre Ajustes de mapa → Tabla de tráfico y sube los vehículos por minuto.");

            foreach (var n in net.entries)
            {
                bool any = false;
                foreach (var a in n.approaches) if (a.outLanes.Count > 0) any = true;
                if (!any)
                    Add(issues, IssueSeverity.Error, IssueCategory.Setup,
                        "La entrada en " + P(n.pos) + " apunta contra un sentido único: los coches no pueden salir de ella.",
                        "Invierte el sentido de esa calle (Seleccionar segmento → Invertir) o pon la entrada en el otro extremo.", n.pos);
            }
            foreach (var n in net.exits)
            {
                bool any = false;
                foreach (var a in n.approaches) if (a.inLanes.Count > 0) any = true;
                if (!any)
                    Add(issues, IssueSeverity.Error, IssueCategory.Setup,
                        "La salida en " + P(n.pos) + " apunta contra un sentido único: los coches no pueden llegar a ella.",
                        "Invierte el sentido de esa calle (Seleccionar segmento → Invertir) o pon la salida en el otro extremo.", n.pos);
            }

            Rect view = m.View;
            foreach (var n in net.entries)
                if (view.Contains(n.pos))
                    Add(issues, IssueSeverity.Warning, IssueCategory.Setup,
                        "La entrada en " + P(n.pos) + " está dentro del área visible: los coches aparecerían a la vista.",
                        "Mueve la línea roja (Área visible) para que la entrada quede fuera, o mueve la entrada.", n.pos);
            foreach (var n in net.exits)
                if (view.Contains(n.pos))
                    Add(issues, IssueSeverity.Warning, IssueCategory.Setup,
                        "La salida en " + P(n.pos) + " está dentro del área visible: los coches desaparecerían a la vista.",
                        "Mueve la línea roja (Área visible) para que la salida quede fuera, o mueve la salida.", n.pos);

            // Extremos abiertos: en edición es normal tenerlos mientras se construye, por eso es solo un aviso.
            foreach (var n in net.nodes)
            {
                if (n.approaches.Count == 1 && !n.entry && !n.exit && !n.roundabout)
                    Add(issues, IssueSeverity.Warning, IssueCategory.Connection,
                        "Extremo abierto en " + P(n.pos) + ": la calle termina sin conectar con nada y los coches se quedarían atrapados.",
                        "Conéctala con otra calle (arrastra el extremo sobre un nodo) o márcala como Entrada o Salida.", n.pos);
            }

            // Carriles que llegan a un cruce sin ningún giro posible.
            foreach (var seg in net.segs)
            {
                if (seg.kind != SegKind.Lane) continue;
                var to = seg.toNode;
                if (to.approaches.Count >= 2 && seg.outs.Count == 0 && !to.exit)
                    Add(issues, IssueSeverity.Warning, IssueCategory.Route,
                        "Un carril que llega al cruce de " + P(to.pos) + " no tiene ningún giro permitido.",
                        "Revisa las señales de sentido único, las flechas funcionales y los giros permitidos de ese carril.", to.pos);
            }

            foreach (var n in net.nodes)
            {
                if (!n.roundabout) continue;
                if (n.approaches.Count < 2)
                    Add(issues, IssueSeverity.Warning, IssueCategory.Connection,
                        "La rotonda en " + P(n.pos) + " tiene menos de dos calles conectadas.",
                        "Conecta al menos dos calles a la rotonda: arrastra el extremo de cada calle sobre su nodo.", n.pos);
                for (int i = 0; i < n.approaches.Count && n.approaches.Count > 1; i++)
                {
                    var a = n.approaches[i];
                    var b = n.approaches[(i + 1) % n.approaches.Count];
                    if (Mathf.Abs(Mathf.DeltaAngle(a.angle, b.angle)) < 28f)
                    {
                        Add(issues, IssueSeverity.Warning, IssueCategory.Geometry,
                            "Dos calles de la rotonda en " + P(n.pos) + " están demasiado juntas y sus giros se pisarían.",
                            "Sepáralas moviendo una de ellas o agranda la rotonda (Seleccionar nodo → Radio).", n.pos);
                        break;
                    }
                }
            }

            foreach (var s in m.signs)
            {
                if (s.type == SignType.SpeedLimit) continue;
                if (m.Degree(s.node) < 3)
                    Add(issues, IssueSeverity.Warning, IssueCategory.Info,
                        "Señal sin cruce en " + P(m.PosOf(s.node)) + ": no tendrá ningún efecto.",
                        "Colócala en un nodo donde se unan al menos tres calles o bórrala.", m.PosOf(s.node));
            }

            // Cruces muy pegados.
            for (int i = 0; i < net.nodes.Count; i++)
            {
                if (!net.nodes[i].IsJunction) continue;
                for (int j = i + 1; j < net.nodes.Count; j++)
                {
                    if (!net.nodes[j].IsJunction) continue;
                    if (Vector2.Distance(net.nodes[i].pos, net.nodes[j].pos) < 3f)
                        Add(issues, IssueSeverity.Warning, IssueCategory.Geometry,
                            "Dos cruces muy pegados en " + P(net.nodes[i].pos) + " y " + P(net.nodes[j].pos) + ": pueden bloquearse entre sí.",
                            "Sepáralos o fúndelos en un solo cruce arrastrando un nodo sobre el otro.", net.nodes[i].pos);
                }
            }

            foreach (var d in m.decor)
            {
                if (!DecorItem.OnRoadOk(d.type) && m.DistanceToRoads(d.Pos) < 0.5f)
                    Add(issues, IssueSeverity.Warning, IssueCategory.Info,
                        "Decoración sobre una calle en " + P(d.Pos) + ".",
                        "Muévela fuera de la calzada o bórrala.", d.Pos);
            }

            foreach (var b in m.buttons)
            {
                if (b.links.Count == 0)
                {
                    Add(issues, IssueSeverity.Warning, IssueCategory.Info,
                        "El botón de " + P(b.Pos) + " no tiene semáforos vinculados.",
                        "Selecciona el botón y vincúlalo a los semáforos que debe controlar.", b.Pos);
                    continue;
                }
                foreach (var l in b.links)
                {
                    bool lit = false;
                    foreach (var sg in m.signs) if (sg.road == l.road && sg.node == l.node && sg.type == SignType.Light) { lit = true; break; }
                    if (!lit)
                    {
                        Add(issues, IssueSeverity.Warning, IssueCategory.Info,
                            "Un botón está vinculado a un extremo sin semáforo (" + P(m.PosOf(l.node)) + ").",
                            "Coloca un semáforo en ese extremo o quita el vínculo.", m.PosOf(l.node));
                        break;
                    }
                }
            }
            foreach (var q in m.points)
            {
                if (q.kind != PointKind.Detector) continue;
                var r = m.Road(q.road);
                if (r == null) continue;
                bool lit = false;
                foreach (var sg in m.signs) if (sg.road == r.id && sg.type == SignType.Light) { lit = true; break; }
                if (!lit)
                    Add(issues, IssueSeverity.Warning, IssueCategory.Info,
                        "Un detector de vehículos está en una calle sin semáforos (" + P(m.PointOnRoad(r, q.t)) + ").",
                        "Coloca un semáforo en esa calle o quita el detector.", m.PointOnRoad(r, q.t));
            }
            foreach (var n in m.nodes)
            {
                if (n.ctrl == 0 && n.green <= 0.1f && n.yellow <= 0.1f) continue;
                bool lit = false;
                foreach (var sg in m.signs) if (sg.type == SignType.Light && (m.Road(sg.road) != null) && sg.node == n.id) { lit = true; break; }
                if (!lit)
                    Add(issues, IssueSeverity.Warning, IssueCategory.Info,
                        "El cruce de " + P(n.Pos) + " tiene controlador de semáforos pero ningún semáforo.",
                        "Coloca semáforos en las calles de ese cruce.", n.Pos);
            }

            // ---------- 3. Rutas: ¿se puede ir de alguna entrada a alguna salida? ----------
            if (net.entries.Count > 0 && net.exits.Count > 0)
            {
                var reached = new HashSet<NetNode>();
                int entriesOk = 0;
                var failed = new List<NetNode>();
                var failedCut = new List<Vector2>();
                var failedHasCut = new List<bool>();
                foreach (var entry in net.entries)
                {
                    bool reachable = false;
                    bool anyLane = false;
                    bool cutFound = false;
                    Vector2 cut = Vector2.zero;
                    foreach (var lane in net.SpawnLanes(entry))
                    {
                        anyLane = true;
                        path.Prepare(lane);
                        foreach (var exit in net.exits)
                        {
                            if (exit == entry) continue;
                            if (path.LastRunReaches(exit)) { reachable = true; reached.Add(exit); }
                        }
                        if (!reachable && !cutFound) cutFound = path.LastRunDeadEnd(out cut);
                    }
                    if (reachable) { entriesOk++; continue; }
                    if (!anyLane) continue;   // ya explicado arriba (entrada contra un sentido único)
                    failed.Add(entry);
                    failedCut.Add(cut);
                    failedHasCut.Add(cutFound);
                }
                for (int i = 0; i < failed.Count; i++)
                {
                    var entry = failed[i];
                    string cutText = failedHasCut[i]
                        ? "La ruta se interrumpe en " + P(failedCut[i]) + ": ese carril no tiene continuación."
                        : "No hay una cadena de carriles conectados hasta ninguna salida.";
                    string fix = failedHasCut[i]
                        ? "Une ese extremo con otra calle (arrastra el nodo sobre el siguiente), revisa los sentidos únicos o marca una Salida ahí."
                        : "Conecta las calles entre sí, revisa los sentidos únicos y comprueba que haya una Salida alcanzable.";
                    // Si ninguna entrada funciona no hay simulación posible (Error); si otras sí funcionan, esta solo no generará vehículos (Aviso).
                    var sev = entriesOk == 0 ? IssueSeverity.Error : IssueSeverity.Warning;
                    string extra = entriesOk == 0 ? "" : " Esa entrada no generará vehículos.";
                    Add(issues, sev, IssueCategory.Route,
                        "Ruta incompleta: desde la entrada " + P(entry.pos) + " no se llega a ninguna salida. " + cutText + extra,
                        fix, failedHasCut[i] ? failedCut[i] : entry.pos);
                }
                foreach (var exit in net.exits)
                {
                    if (!reached.Contains(exit))
                        Add(issues, IssueSeverity.Warning, IssueCategory.Route,
                            "Nadie llega a la salida " + P(exit.pos) + " desde ninguna entrada.",
                            "Conecta esa salida con la red o quítala.", exit.pos);
                }
            }

            // ---------- 4. Flechas funcionales que piden giros sin conexión real ----------
            foreach (var ar in m.arrows)
            {
                foreach (var nr in net.roads)
                {
                    if (nr.data.id != ar.road) continue;
                    var lanes = ar.forward ? nr.lanesAB : nr.lanesBA;
                    if (ar.lane < 0 || ar.lane >= lanes.Length) break;
                    var ln = lanes[ar.lane];
                    if (ln.toNode == null || ln.toNode.approaches.Count < 2)
                    {
                        Add(issues, IssueSeverity.Warning, IssueCategory.Route,
                            "Una flecha funcional está en un carril que no llega a ningún cruce.",
                            "Bórrala o muévela a un carril que llegue a un cruce.", ln.path.PointAt(ln.Length * ar.t));
                        break;
                    }
                    int missing = ar.moves & ~ln.moves & 7;
                    if (missing != 0)
                        Add(issues, IssueSeverity.Warning, IssueCategory.Route,
                            "Flecha incompatible en " + P(ln.path.PointAt(ln.Length * ar.t)) + ": pide " + MaskText(missing) + " pero el cruce no tiene esa conexión.",
                            "Conecta una calle en esa dirección o cambia la flecha.", ln.path.PointAt(ln.Length * ar.t));
                    break;
                }
            }

            // ---------- 5. Tráfico permitido por carril ----------
            if (net.entries.Count > 0 && net.exits.Count > 0)
            {
                var t = m.traffic;
                float[] chance = { 1f, t.taxiChance, t.busChance, t.truckChance, t.motorbikeChance, t.emergencyChance, t.emergencyChance, t.emergencyChance, t.vanChance };
                string[] names = { "coches", "taxis", "autobuses", "camiones", "motos", "ambulancias", "bomberos", "policías", "furgonetas" };
                for (int vt = 0; vt < RoadData.VehicleTypeCount; vt++)
                {
                    if (chance[vt] <= 0.001f) continue;
                    bool ok = false;
                    foreach (var entry in net.entries)
                    {
                        foreach (var lane in net.SpawnLanes(entry))
                        {
                            var rt = path.FindRoute(lane, net.exits, 1 << vt);
                            if (rt != null && rt.Count > 0) { ok = true; break; }
                        }
                        if (ok) break;
                    }
                    if (!ok)
                        Add(issues, IssueSeverity.Warning, IssueCategory.Traffic,
                            "Los " + names[vt] + " no tienen ninguna ruta posible: las restricciones de carril los dejan sin salida (no se generarán).",
                            "Permite ese tipo en los carriles de la ruta (Seleccionar segmento → Tráfico permitido) o pon su porcentaje en 0.");
                }
                foreach (var r in m.roads)
                {
                    if (r.pedestrian) continue;
                    for (int dir = 0; dir < 2; dir++)
                    {
                        bool fw = dir == 0;
                        int cnt = fw ? r.lanesAB : r.lanesBA;
                        for (int li = 0; li < cnt; li++)
                        {
                            int mask = r.GetLaneAllow(fw, li);
                            if (mask == 0) continue;
                            bool used = false;
                            for (int vt = 0; vt < RoadData.VehicleTypeCount; vt++) if ((mask & (1 << vt)) != 0 && chance[vt] > 0.001f) used = true;
                            if (!used)
                                Add(issues, IssueSeverity.Warning, IssueCategory.Traffic,
                                    "Un carril exclusivo en " + P(m.PointOnRoad(r, 0.5f)) + " solo admite tipos que el mapa nunca genera.",
                                    "Revisa la Tabla de tráfico o los tipos permitidos de ese carril.", m.PointOnRoad(r, 0.5f));
                        }
                    }
                }
            }

            // ---------- 6. Curvas demasiado cerradas para vehículos largos ----------
            if (m.traffic.busChance > 0.001f || m.traffic.truckChance > 0.001f)
            {
                foreach (var r in m.roads)
                {
                    if (!r.Curved) continue;
                    var pl = m.CenterOf(r);
                    float minR = float.MaxValue; Vector2 at = Vector2.zero;
                    for (float sd = 0f; sd + 0.8f < pl.Length; sd += 0.4f)
                    {
                        float ang = Mathf.Abs(Mathf.DeltaAngle(Geo.Angle(pl.TangentAt(sd)), Geo.Angle(pl.TangentAt(sd + 0.8f)))) * Mathf.Deg2Rad;
                        if (ang < 0.01f) continue;
                        float rad = 0.8f / ang;
                        if (rad < minR) { minR = rad; at = pl.PointAt(sd + 0.4f); }
                    }
                    if (minR < 1.0f)
                        Add(issues, IssueSeverity.Warning, IssueCategory.Geometry,
                            "Curva muy cerrada en " + P(at) + ": autobuses y camiones tendrán dificultades.",
                            "Suaviza la curva (Seleccionar segmento y mueve el punto central) o alarga la calle.", at);
                }
            }

            // ---------- 7. Fases compartidas con movimientos que se cruzan ----------
            foreach (var n in net.nodes)
            {
                bool warned = false;
                foreach (var c in n.connectors)
                {
                    if (warned || c.inApp == null || !c.inApp.hasLight || c.inApp.lightPhase <= 0) continue;
                    foreach (var cf in c.conflicts)
                    {
                        var o = cf.other.inApp;
                        if (cf.merge || o == null || o == c.inApp || !o.hasLight || o.lightPhase != c.inApp.lightPhase) continue;
                        Add(issues, IssueSeverity.Warning, IssueCategory.Info,
                            "La fase " + c.inApp.lightPhase + " del cruce de " + P(n.pos) + " da verde a la vez a movimientos que se cruzan. En modo realista los vehículos cederán el paso; en modo caótico pueden chocar.",
                            "Pon esos semáforos en fases distintas.", n.pos);
                        warned = true;
                        break;
                    }
                }
            }

            // ---------- 8. Semáforos sin modo seguro ----------
            if (!m.traffic.safeSignals)
            {
                foreach (var n in m.nodes)
                {
                    int lights = 0;
                    foreach (var sg in m.signs) if (sg.type == SignType.Light && sg.node == n.id) lights++;
                    if (lights >= 2)
                        Add(issues, IssueSeverity.Warning, IssueCategory.Info,
                            "El cruce de " + P(n.Pos) + " tiene varios semáforos y el modo seguro está desactivado: dos verdes que se cruzan pueden provocar choques.",
                            "Activa el modo seguro en Ajustes de mapa.", n.Pos);
                }
            }
            if (m.traffic.simMode == 1)
                Add(issues, IssueSeverity.Warning, IssueCategory.Info,
                    "Modo caótico activado: habrá infracciones y choques a propósito.",
                    "Cambia a modo realista en Ajustes de mapa si no lo quieres.");
        }

        /// <summary>
        /// Comprobaciones de datos, conexiones y geometría. Devuelve -1 si el mapa está tan dañado que no se puede
        /// construir la red de tráfico (nodos inexistentes o números inválidos); en otro caso, el número de problemas hallados.
        /// </summary>
        static int StructuralChecks(MapData m, List<MapIssue> issues)
        {
            int found = 0;
            bool fatal = false;

            foreach (var n in m.nodes)
            {
                if (Bad(n.x) || Bad(n.y))
                {
                    Add(issues, IssueSeverity.Error, IssueCategory.Geometry,
                        "Un nodo tiene una posición inválida (número no válido).",
                        "Bórralo con la herramienta Borrar o pulsa Deshacer.");
                    fatal = true; found++;
                }
            }

            foreach (var r in m.roads)
            {
                var na = m.Node(r.a);
                var nb = m.Node(r.b);
                if (na == null || nb == null)
                {
                    Add(issues, IssueSeverity.Error, IssueCategory.Connection,
                        "Una calle está unida a un nodo que ya no existe: queda flotando sin conexión.",
                        "Bórrala con la herramienta Borrar y vuelve a dibujarla uniéndola a un nodo existente.");
                    fatal = true; found++;
                    continue;
                }
                if (Bad(r.bendX) || Bad(r.bendY))
                {
                    Add(issues, IssueSeverity.Error, IssueCategory.Geometry,
                        "Una calle tiene la curva dañada en " + P(m.PointOnRoad(r, 0.5f)) + ".",
                        "Selecciona la calle y estírala de nuevo, o bórrala y dibújala otra vez.");
                    fatal = true; found++;
                    continue;
                }
                if (r.lanesAB <= 0 && r.lanesBA <= 0)
                {
                    Add(issues, IssueSeverity.Error, IssueCategory.Geometry,
                        "La calle en " + P(m.PointOnRoad(r, 0.5f)) + " no tiene ningún carril.",
                        "Selecciona la calle y pon al menos un carril, o bórrala.", m.PointOnRoad(r, 0.5f));
                    found++;
                }
                float len = Vector2.Distance(na.Pos, nb.Pos);
                if (len < 0.6f)
                {
                    Add(issues, IssueSeverity.Error, IssueCategory.Geometry,
                        "La calle en " + P(na.Pos) + " es casi un punto (longitud " + len.ToString("0.##") + "): los vehículos no caben.",
                        "Bórrala o aleja uno de sus nodos.", na.Pos);
                    found++;
                }
            }
            if (fatal) return -1;

            // Hueco: dos nodos casi juntos que no comparten calle (los extremos «parecen» unidos pero no lo están).
            for (int i = 0; i < m.nodes.Count; i++)
            {
                var a = m.nodes[i];
                if (a.roundabout) continue;
                for (int j = i + 1; j < m.nodes.Count; j++)
                {
                    var b = m.nodes[j];
                    if (b.roundabout) continue;
                    float d = Vector2.Distance(a.Pos, b.Pos);
                    if (d >= GapTolerance) continue;
                    if (m.FindRoad(a.id, b.id) != null) continue;
                    Add(issues, IssueSeverity.Error, IssueCategory.Connection,
                        "Conexión rota en " + P((a.Pos + b.Pos) * 0.5f) + ": dos extremos están a solo " + d.ToString("0.##") + " unidades pero no están unidos, así que los coches no pasan de una calle a otra.",
                        "Con Seleccionar nodo, arrastra uno de los dos nodos sobre el otro y suéltalo: se fusionan.", (a.Pos + b.Pos) * 0.5f);
                    found++;
                }
            }

            // Extremo pegado al interior de otra calle sin estar unido (cruce en T falso).
            foreach (var n in m.nodes)
            {
                if (n.roundabout || m.Degree(n.id) == 0) continue;
                foreach (var r in m.roads)
                {
                    if (r.a == n.id || r.b == n.id) continue;
                    float dist;
                    float t = m.NearestParam(r, n.Pos, out dist);
                    if (dist >= TouchTolerance || t <= 0.03f || t >= 0.97f) continue;
                    bool sameLayer = true;
                    foreach (var own in m.RoadsAt(n.id)) if (own.layer != r.layer) sameLayer = false;
                    if (!sameLayer) continue;
                    Add(issues, IssueSeverity.Error, IssueCategory.Connection,
                        "Extremo flotante en " + P(n.Pos) + ": toca el lateral de otra calle pero no está unido a ella (cruce en T sin nodo).",
                        "Con Seleccionar nodo, arrastra ese nodo un poco y suéltalo sobre la otra calle: se une y crea el cruce.", n.Pos);
                    found++;
                    break;
                }
            }

            // Nodos sueltos (sin calles): no rompen nada, pero ensucian el mapa.
            foreach (var n in m.nodes)
            {
                if (n.roundabout || m.Degree(n.id) > 0) continue;
                Add(issues, IssueSeverity.Warning, IssueCategory.Connection,
                    "Nodo suelto en " + P(n.Pos) + " sin ninguna calle.",
                    "Bórralo con la herramienta Borrar.", n.Pos);
                found++;
            }

            // Calles que se atraviesan sin tener un nodo común.
            for (int i = 0; i < m.roads.Count; i++)
            {
                var ra = m.roads[i];
                var pa = m.CenterOf(ra);
                for (int j = i + 1; j < m.roads.Count; j++)
                {
                    var rb = m.roads[j];
                    if (ra.a == rb.a || ra.a == rb.b || ra.b == rb.a || ra.b == rb.b) continue;
                    if (ra.layer != rb.layer) continue;
                    float s1, s2;
                    if (Geo.PolylineCross(pa, m.CenterOf(rb), out s1, out s2))
                    {
                        Add(issues, IssueSeverity.Error, IssueCategory.Geometry,
                            "Dos calles se atraviesan en " + P(pa.PointAt(s1)) + " sin formar un cruce: los coches se traspasarían o chocarían.",
                            "Mueve un poco una de las dos calles (Seleccionar nodo) para que el cruce se cree solo, o ponlas en capas distintas (puente).", pa.PointAt(s1));
                        found++;
                    }
                }
            }
            return found;
        }

        // ---------------- Estado resumido ----------------

        public static bool HasErrors(List<MapIssue> issues)
        {
            foreach (var i in issues) if (i.severity == IssueSeverity.Error) return true;
            return false;
        }

        public static int CountOf(List<MapIssue> issues, IssueSeverity s)
        {
            int c = 0;
            foreach (var i in issues) if (i.severity == s) c++;
            return c;
        }

        /// <summary>En edición (hay errores que impiden simular) / con advertencias / listo para simular.</summary>
        public static MapReadiness Readiness(List<MapIssue> issues)
        {
            if (HasErrors(issues)) return MapReadiness.Editing;
            return issues.Count > 0 ? MapReadiness.Warnings : MapReadiness.Ready;
        }

        /// <summary>Texto corto del estado, con el número de problemas.</summary>
        public static string ReadinessText(List<MapIssue> issues)
        {
            int e = CountOf(issues, IssueSeverity.Error), w = CountOf(issues, IssueSeverity.Warning);
            switch (Readiness(issues))
            {
                case MapReadiness.Editing:
                    return "En edición: " + e + (e == 1 ? " problema impide" : " problemas impiden") + " simular" + (w > 0 ? " (y " + w + (w == 1 ? " aviso" : " avisos") + ")" : "") + ". Puedes seguir editando.";
                case MapReadiness.Warnings:
                    return "Listo para simular, con " + w + (w == 1 ? " aviso." : " avisos.");
                default:
                    return "Listo para simular: sin problemas.";
            }
        }

        /// <summary>El primer error (el más urgente) o null. Es el que se muestra al intentar simular.</summary>
        public static MapIssue FirstError(List<MapIssue> issues)
        {
            foreach (var i in issues) if (i.severity == IssueSeverity.Error) return i;
            return null;
        }

        /// <summary>Ordena: errores primero (conexión y geometría antes que configuración), luego avisos. Orden estable.</summary>
        public static void SortForDisplay(List<MapIssue> issues)
        {
            var keyed = new List<KeyValuePair<int, MapIssue>>();
            for (int i = 0; i < issues.Count; i++)
            {
                var it = issues[i];
                int rank = it.severity == IssueSeverity.Error ? 0 : 100;
                switch (it.category)
                {
                    case IssueCategory.Connection: rank += 0; break;
                    case IssueCategory.Geometry: rank += 1; break;
                    case IssueCategory.Setup: rank += 2; break;
                    case IssueCategory.Route: rank += 3; break;
                    case IssueCategory.Traffic: rank += 4; break;
                    default: rank += 5; break;
                }
                keyed.Add(new KeyValuePair<int, MapIssue>(rank * 100000 + i, it));
            }
            keyed.Sort((x, y) => x.Key.CompareTo(y.Key));
            issues.Clear();
            foreach (var kv in keyed) issues.Add(kv.Value);
        }

        static void Add(List<MapIssue> list, IssueSeverity s, IssueCategory c, string what, string fix)
        {
            list.Add(new MapIssue { severity = s, category = c, what = what, fix = fix, message = what + " Cómo corregirlo: " + fix });
        }

        static void Add(List<MapIssue> list, IssueSeverity s, IssueCategory c, string what, string fix, Vector2 pos)
        {
            list.Add(new MapIssue { severity = s, category = c, what = what, fix = fix, message = what + " Cómo corregirlo: " + fix, hasPos = true, pos = pos });
        }
    }
}
