using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using IntersectionMaster.Data;
using IntersectionMaster.Traffic;

namespace IntersectionMaster.Systems
{
    public class SelfTestResult
    {
        public string name;
        public bool passed;
        public string detail;
    }

    /// <summary>
    /// Pruebas de la lógica de mapas que se ejecutan DENTRO del juego (también en el teléfono): construyen mapas por código,
    /// los validan y comprueban conexiones, rutas y guardado/carga. No simulan toques en la pantalla ni la simulación de
    /// tráfico en marcha: eso solo se puede probar a mano (ver TESTING.md). El resultado se muestra en Menú del editor →
    /// «Diagnóstico del sistema» y se guarda en diagnostico.txt dentro de la carpeta de datos de la app.
    /// </summary>
    public static class MapSelfTest
    {
        delegate string Test();   // null = aprobada; texto = motivo del fallo

        static MapData NewMap()
        {
            var m = new MapData { width = 24f, height = 14f };
            m.featureLevel = MapData.CurrentFeatureLevel;
            m.laneFormat = MapData.LaneFormatCurrent;
            m.SetView(new Rect(2f, 2f, 20f, 10f));
            return m;
        }

        /// <summary>Mapa del modelo antiguo (calles de doble sentido) para probar la conversión.</summary>
        static MapData OldMap()
        {
            var m = new MapData { width = 24f, height = 14f };
            m.featureLevel = MapData.CurrentFeatureLevel;
            m.SetView(new Rect(2f, 2f, 20f, 10f));
            return m;
        }

        static NodeData Nd(MapData m, float x, float y)
        {
            return m.AddNode(new Vector2(x, y));
        }

        static NodeData Portal(MapData m, float x, float y)
        {
            var n = m.AddNode(new Vector2(x, y));
            n.entry = true;
            n.exit = true;
            return n;
        }

        /// <summary>Carril de a hacia b con nodos nuevos o existentes.</summary>
        static RoadData Lane(MapData m, NodeData a, NodeData b, int type = 0)
        {
            var r = m.AddRoad(a.id, b.id, 1, 0);
            if (r != null) r.laneType = type;
            return r;
        }

        static bool Has(List<MapIssue> l, IssueSeverity s, IssueCategory c)
        {
            foreach (var i in l) if (i.severity == s && i.category == c) return true;
            return false;
        }

        static string Describe(List<MapIssue> l)
        {
            var sb = new StringBuilder();
            int n = 0;
            foreach (var i in l)
            {
                if (n++ >= 3) { sb.Append(" …"); break; }
                sb.Append("[" + (i.severity == IssueSeverity.Error ? "Error" : "Aviso") + "] " + i.what + " ");
            }
            return sb.ToString();
        }

        static string ErrorsOf(List<MapIssue> l)
        {
            var sb = new StringBuilder();
            foreach (var i in l) if (i.severity == IssueSeverity.Error) sb.Append("[Error] " + i.what + " ");
            return sb.ToString();
        }

        /// <summary>Mapa antiguo de cuatro brazos con entrada y salida en cada borde. Si ring es true, el centro es una rotonda.</summary>
        static MapData FourArms(bool ring)
        {
            var m = OldMap();
            var c = Nd(m, 12f, 7f);
            if (ring) { c.roundabout = true; c.radius = 1.3f; }
            m.AddRoad(Portal(m, 0f, 7f).id, c.id);
            m.AddRoad(c.id, Portal(m, 24f, 7f).id);
            m.AddRoad(Portal(m, 12f, 0f).id, c.id);
            m.AddRoad(c.id, Portal(m, 12f, 14f).id);
            return m;
        }

        public static List<SelfTestResult> RunAll()
        {
            var results = new List<SelfTestResult>();
            Run(results, "1. Carril suelto: validar no cambia el mapa, no da errores falsos y se puede seguir editando", Test1);
            Run(results, "2. Dos carriles unidos comparten el nodo y el tráfico pasa de uno a otro", Test2);
            Run(results, "3. Dos carriles que se cruzan sin nodo: hay zona de cesión y no se crea ningún cruce", Test3);
            Run(results, "4. Semáforo en el nodo: regula los carriles que llegan y forma un grupo", Test4);
            Run(results, "5. Mapa antiguo de cruce: se convierte a carriles, sin errores y con rutas", Test5);
            Run(results, "6. Mapa antiguo con glorieta: se convierte a carriles y es válido", Test6);
            Run(results, "7. Acera que cruza un carril: los coches ceden a los peatones", Test7);
            Run(results, "8. Guardar y cargar conserva carriles, tipos, curvas, líneas y señales", Test8);
            Run(results, "8b. Importar un mapa de Intersection Controller (.trzmap): carriles, curva, acera, semáforo, decoración, líneas y botón", TestTrz);
            Run(results, "9. Los mapas de ejemplo (convertidos) no dan falsos errores", Test9);
            Run(results, "10. Mapa vacío: mensaje claro y sin excepciones", Test10);
            Run(results, "11. Carriles paralelos: se reconocen para cambiar de carril", Test11);
            Run(results, "12. Todos los problemas indican qué falla y cómo corregirlo", Test12);
            return results;
        }

        static void Run(List<SelfTestResult> list, string name, Test t)
        {
            var r = new SelfTestResult { name = name };
            try
            {
                string fail = t();
                r.passed = fail == null;
                r.detail = fail ?? "";
            }
            catch (System.Exception ex)
            {
                r.passed = false;
                r.detail = "Excepción: " + ex.GetType().Name + ": " + ex.Message;
            }
            list.Add(r);
        }

        // ---------------- Pruebas ----------------

        static string Test1()
        {
            var m = NewMap();
            var a = Nd(m, 0.5f, 7f); var b = Nd(m, 23.5f, 7f);
            if (Lane(m, a, b) == null) return "AddRoad devolvió null.";
            string before = JsonUtility.ToJson(m);
            var issues = MapValidator.Validate(m);
            if (JsonUtility.ToJson(m) != before) return "Validar modificó el mapa.";
            if (MapValidator.HasErrors(issues)) return "Un carril que cruza el mapa no debería dar errores: " + ErrorsOf(issues);
            var c = Nd(m, 12f, 13f);
            if (Lane(m, b, c) == null) return "No se pudo seguir editando después de validar.";
            return null;
        }

        static string Test2()
        {
            var m = NewMap();
            var a = Nd(m, 0.5f, 7f); var b = Nd(m, 12f, 7f); var c = Nd(m, 23.5f, 9f);
            var r1 = Lane(m, a, b); var r2 = Lane(m, b, c);
            if (r1 == null || r2 == null) return "No se pudieron crear los carriles.";
            if (m.nodes.Count != 3) return "Hay " + m.nodes.Count + " nodos, se esperaban 3.";
            var net = new RoadNetworkManager();
            net.Build(m);
            if (!net.LaneMode) return "La red no se construyó en modo carriles.";
            var s1 = net.laneByRoad[r1.id]; var s2 = net.laneByRoad[r2.id];
            if (!s1.outs.Contains(s2)) return "El primer carril no continúa en el segundo.";
            if (net.entries.Count != 1 || net.exits.Count != 1) return "Entradas/salidas automáticas: " + net.entries.Count + "/" + net.exits.Count + ", se esperaba 1/1.";
            return null;
        }

        static string Test3()
        {
            var m = NewMap();
            var h = Lane(m, Nd(m, 0.5f, 7f), Nd(m, 23.5f, 7f));
            var v = Lane(m, Nd(m, 12f, 0.5f), Nd(m, 12f, 13.5f));
            if (h == null || v == null) return "No se pudieron crear los carriles.";
            int nodes = m.nodes.Count;
            var issues = MapValidator.Validate(m);
            if (MapValidator.HasErrors(issues)) return "Un cruce de carriles da errores: " + ErrorsOf(issues);
            if (m.nodes.Count != nodes) return "Validar creó nodos.";
            var net = new RoadNetworkManager();
            net.Build(m);
            var sh = net.laneByRoad[h.id];
            if (sh.conflicts.Count != 1) return "El carril horizontal tiene " + sh.conflicts.Count + " zonas de cruce, se esperaba 1.";
            if (Mathf.Abs(sh.conflicts[0].myS - 11.5f) > 0.2f) return "La zona de cruce no está donde se cruzan: s=" + sh.conflicts[0].myS;
            return null;
        }

        static string Test4()
        {
            var m = NewMap();
            var w = Nd(m, 0.5f, 7f); var l = Nd(m, 11f, 7f); var e = Nd(m, 23.5f, 7f);
            var r1 = Lane(m, w, l); Lane(m, l, e);
            var n2 = Nd(m, 12f, 0.5f); var l2 = Nd(m, 12f, 6f); var n3 = Nd(m, 12f, 13.5f);
            var r3 = Lane(m, n2, l2); Lane(m, l2, n3);
            if (!m.ToggleSign(r1.id, l.id, SignType.Light) || !m.ToggleSign(r3.id, l2.id, SignType.Light)) return "No se pudieron poner los semáforos.";
            var net = new RoadNetworkManager();
            net.Build(m);
            var hub = net.HubOf(l.id);
            if (hub == null || !hub.hasLight) return "El nodo con semáforo no quedó regulado.";
            if (net.laneByRoad[r1.id].stopS > net.laneByRoad[r1.id].Length) return "Sin línea de detención.";
            var groups = IntersectionController.BuildLaneGroups(net, m.traffic);
            if (groups.Count != 1) return "Se esperaba 1 grupo de semáforos y hay " + groups.Count + ".";
            if (!groups[0].light.Has(hub)) return "El grupo no controla ese semáforo.";
            if (groups[0].light.phases.Count < 2) return "Los dos semáforos se cruzan y deberían ir en fases distintas.";
            return null;
        }

        static string Test5()
        {
            var m = FourArms(false);
            m.Normalize();
            if (!m.IsLaneMap) return "El mapa antiguo no se convirtió.";
            foreach (var r in m.roads) if (r.lanesAB != 1 || r.lanesBA != 0) return "Quedó una calle con varios carriles.";
            var issues = MapValidator.Validate(m);
            if (MapValidator.HasErrors(issues)) return "El cruce convertido da errores: " + ErrorsOf(issues);
            var net = new RoadNetworkManager();
            net.Build(m);
            if (net.entries.Count < 4 || net.exits.Count < 4) return "Entradas/salidas tras convertir: " + net.entries.Count + "/" + net.exits.Count;
            var pf = new PathfindingSystem();
            foreach (var e in net.entries)
                foreach (var lane in net.SpawnLanes(e))
                    if (pf.FindRoute(lane, net.exits) == null) return "Desde una entrada convertida no se llega a ninguna salida.";
            return null;
        }

        static string Test6()
        {
            var m = FourArms(true);
            m.Normalize();
            if (!m.IsLaneMap) return "La glorieta no se convirtió.";
            var issues = MapValidator.Validate(m);
            if (MapValidator.HasErrors(issues)) return "La glorieta convertida da errores: " + ErrorsOf(issues);
            bool yield = false;
            foreach (var sg in m.signs) if (sg.type == SignType.Yield) yield = true;
            if (!yield) return "Las entradas de la glorieta no tienen ceda el paso.";
            return null;
        }

        static string Test7()
        {
            var m = NewMap();
            var car = Lane(m, Nd(m, 0.5f, 7f), Nd(m, 23.5f, 7f));
            var walk = Lane(m, Nd(m, 12f, 5f), Nd(m, 12f, 9f), RoadData.LaneSidewalk);
            var net = new RoadNetworkManager();
            net.Build(m);
            var sc = net.laneByRoad[car.id]; var sw = net.laneByRoad[walk.id];
            // La acera tiene su sentido contrario (los peatones van en los dos): el carril cruza con los dos.
            bool crossOk = sc.conflicts.Count >= 1 && sc.conflicts.Count <= 2;
            foreach (var cf in sc.conflicts) if (cf.other.roadId != walk.id) crossOk = false;
            if (!crossOk) return "No hay zona de cruce entre la acera y el carril.";
            if (sw.crossRank <= sc.crossRank) return "Los peatones no tienen prioridad en el paso.";
            bool walkerEntry = false;
            foreach (var e in net.entries) foreach (var l in net.SpawnLanes(e)) if (l.laneType == RoadData.LaneSidewalk) walkerEntry = true;
            if (!walkerEntry) return "La acera no genera peatones.";
            return null;
        }

        static string Test8()
        {
            var m = NewMap();
            m.name = "Prueba"; m.id = MapData.NewId();
            var a = Nd(m, 0.5f, 7f); var b = Nd(m, 12f, 7f); var c = Nd(m, 23.5f, 3f);
            var r1 = Lane(m, a, b); var r2 = Lane(m, b, c, RoadData.LaneBike);
            r2.bendX = 1.5f; r2.bendY = -0.8f; r1.lineL = 2; r1.lineR = 3; r1.speedKmh = 80;
            m.ToggleSign(r1.id, b.id, SignType.Yield);
            string json = MapSerializationSystem.ToJson(m);
            MapData m2;
            string err;
            if (!MapSerializationSystem.TryFromJson(json, out m2, out err)) return "No se pudo cargar: " + err;
            if (!m2.IsLaneMap) return "Al cargar dejó de ser un mapa por carriles.";
            if (m2.nodes.Count != m.nodes.Count || m2.roads.Count != m.roads.Count) return "Cambió el número de nodos o carriles.";
            var q1 = m2.Road(r1.id); var q2 = m2.Road(r2.id);
            if (q1 == null || q2 == null) return "Se perdió un carril.";
            if (q1.a != r1.a || q1.b != r1.b) return "Cambió el sentido de un carril.";
            if (q2.laneType != RoadData.LaneBike) return "Cambió el tipo de un carril.";
            if (Mathf.Abs(q2.bendX - 1.5f) > 0.001f || Mathf.Abs(q2.bendY + 0.8f) > 0.001f) return "Cambió la curva.";
            if (q1.lineL != 2 || q1.lineR != 3 || q1.speedKmh != 80) return "Cambiaron las líneas o la velocidad.";
            if (m2.FindSign(r1.id, b.id, SignType.Yield) == null) return "Se perdió el ceda el paso.";
            // Mapa grande (200 × 200, el de los mapas nuevos): al guardar y cargar no se mueve ningún nodo.
            m.ExpandToBig();
            var pa = m.Node(a.id).Pos; var pc = m.Node(c.id).Pos;
            if (!MapSerializationSystem.TryFromJson(MapSerializationSystem.ToJson(m), out m2, out err)) return "No se pudo cargar el mapa grande: " + err;
            if (m2.width < MapData.BigSize - 0.01f) return "El mapa grande se achicó al cargarlo (" + m2.width + ").";
            if ((m2.Node(a.id).Pos - pa).sqrMagnitude > 1e-4f || (m2.Node(c.id).Pos - pc).sqrMagnitude > 1e-4f) return "Al cargar el mapa grande se movieron los nodos.";
            return null;
        }

        static string TestTrz()
        {
            // Mapa de ejemplo escrito a mano con el formato del original (no viene del juego).
            string t = "type:4;id:-1;tlx:-200;tly:-200;brx:200;bry:200;st:540;m:1;t:50;pt:360;tvp:0.5\r\n"
                + "type:0;id:1;n0:2;nc:1;s:10;x:-180;y:0;st0:6\r\n"
                + "type:0;id:2;p0:1;n0:3;pc:1;nc:1;s:10;x:0;y:0;tlv:1\r\n"
                + "type:0;id:3;p0:2;n0:4;s:11;x:40;y:-10\r\n"
                + "type:0;id:4;p0:3;n0:5;s:11;x:70;y:-40\r\n"
                + "type:0;id:5;p0:4;n0:6;s:11;x:80;y:-80\r\n"
                + "type:0;id:6;p0:5;s:11;x:80;y:-180\r\n"
                + "type:2;id:10;n0:1;n1:2;nc:2;stid:1;w:36;b:1;it: BUS \r\n"
                + "type:3;id:40;stid:34;f:2;x:-150;y:150;ox:-100;oy:100;z:0\r\n"
                + "type:3;id:41;stid:46;f:1;c:1;pc:3;ptx0:100;pty0:-100;ptx1:120;pty1:-120;ptx2:150;pty2:-120;x:100;y:-100;ox:150;oy:-120;z:0\r\n"
                + "type:3;id:42;stid:15;tc:1;tp0:2;x:-20;y:40;z:0\r\n"
                + "type:3;id:43;stid:8;c1x:-150;c1y:-60;c2x:-60;c2y:-60;c3x:-60;c3y:-50;c4x:-150;c4y:-50;x:-105;y:-55;z:0\r\n"
                + "type:3;id:44;stid:20;cc:1;ci0:45;x:-100;y:100;z:0\r\n"
                + "type:3;id:45;stid:20;cc:0;x:-20;y:100;z:0\r\n"
                + "type:3;id:46;stid:24;c:2;an:45;x:60;y:60;z:0\r\n"
                + "type:2;id:11;n0:2;n1:3;n2:4;n3:5;n4:6;nc:5;stid:1;w:36;arc:1\r\n"
                + "type:0;id:20;n0:21;s:12;x:-40;y:60\r\n"
                + "type:0;id:21;p0:20;s:12;x:-40;y:-60\r\n"
                + "type:2;id:12;n0:20;n1:21;nc:2;stid:2;w:36\r\n"
                + "type:3;id:30;tt:2;stid:2;s:0.3;x:-120;y:-120;z:0\r\n"
                + "type:3;id:31;stid:3;c1x:100;c1y:100;c2x:160;c2y:100;c3x:160;c3y:140;c4x:100;c4y:140;rt:1;wt:3;x:130;y:120;z:0\r\n";
            MapData m; string err;
            if (!TrzImporter.TryConvertText(t, "Prueba", out m, out err)) return "No se importó: " + err;
            if (!m.IsLaneMap) return "No quedó como mapa por carriles.";
            int veh = 0, walk = 0, curved = 0;
            foreach (var r in m.roads) { if (r.laneType == RoadData.LaneVehicle) veh++; if (r.laneType == RoadData.LaneSidewalk) walk++; if (r.Curved) curved++; }
            if (veh < 2) return "Faltan carriles de coches (" + veh + ").";
            if (walk != 1) return "La acera no se importó.";
            if (curved < 1) return "La curva quedó recta.";
            if (m.signs.Count == 0) return "Se perdió el semáforo.";
            int trees = 0, bld = 0;
            foreach (var d in m.decor) { if (d.type == DecorType.Tree) trees++; if (d.type == DecorType.Building) bld++; }
            if (trees != 1 || bld != 1) return "Árboles " + trees + ", edificios " + bld + " (se esperaba 1 y 1).";
            if (Mathf.Abs(m.startHour - 9f) > 0.01f) return "Hora de inicio " + m.startHour;
            int zones = 0, lines = 0, hedges = 0, poles = 0, cables = 0, cars = 0;
            foreach (var d in m.decor)
            {
                if (d.type == DecorType.Fence) zones++;   // stid 34 = valla
                if (d.type == DecorType.Lines) lines++;
                if (d.type == DecorType.Hedge && d.x2 > 0.5f) hedges++;
                if (d.type == DecorType.PhonePole) poles++;
                if (d.type == DecorType.PowerLine) cables++;
                if (d.type == DecorType.ParkedCar) cars++;
            }
            if (zones != 1 || lines != 2) return "Vallas " + zones + ", líneas " + lines + " (se esperaba 1 y 2).";
            if (hedges != 1 || poles != 2 || cables != 1 || cars != 1) return "Setos " + hedges + ", postes " + poles + ", cables " + cables + ", coches " + cars + " (se esperaba 1, 2, 1, 1).";
            if (m.buttons.Count != 1 || m.buttons[0].links.Count != 1) return "Se perdió el botón de semáforos.";
            if (m.goalVehicles != 50 || Mathf.Abs(m.goalMinutes - 6f) > 0.01f || Mathf.Abs(m.rainChance - 0.5f) > 0.01f || m.playMode != 2)
                return "Cabecera mal: objetivo " + m.goalVehicles + ", minutos " + m.goalMinutes + ", lluvia " + m.rainChance + ", modo " + m.playMode;
            bool bus = false;
            foreach (var r in m.roads) if (r.roadText == "BUS" && r.avoid) bus = true;
            if (!bus) return "Se perdió el texto pintado o «Evitar».";
            bool rate = false;
            foreach (var n in m.nodes) if (Mathf.Abs(n.rateCar - 10f) < 0.01f) rate = true;
            if (!rate) return "Se perdió el tráfico del nodo de inicio.";
            var net = new RoadNetworkManager(); net.Build(m);
            if (net.entries.Count == 0 || net.exits.Count == 0) return "La red importada no tiene entradas o salidas.";
            return null;
        }

        static string Test9()
        {
            var sb = new StringBuilder();
            foreach (var s in SampleMaps.All())
            {
                s.Normalize();
                if (!s.IsLaneMap) { sb.Append(s.name + ": no se convirtió. "); continue; }
                var issues = MapValidator.Validate(s);
                if (MapValidator.HasErrors(issues)) sb.Append(s.name + ": " + ErrorsOf(issues));
            }
            return sb.Length == 0 ? null : sb.ToString();
        }

        static string Test10()
        {
            var issues = MapValidator.Validate(NewMap());
            if (issues.Count == 0) return "Un mapa vacío no da ningún mensaje.";
            if (!MapValidator.HasErrors(issues)) return "Un mapa vacío no puede simularse y no lo indica.";
            if (MapValidator.Readiness(issues) != MapReadiness.Editing) return "El estado de un mapa vacío debería ser «en edición».";
            return null;
        }

        static string Test11()
        {
            var m = NewMap();
            var r1 = Lane(m, Nd(m, 0.5f, 7f), Nd(m, 23.5f, 7f));
            var r2 = Lane(m, Nd(m, 0.5f, 6.5f), Nd(m, 23.5f, 6.5f));
            var net = new RoadNetworkManager();
            net.Build(m);
            var s1 = net.laneByRoad[r1.id];
            if (s1.laneSet == null || s1.laneSet.Length != 2) return "Los dos carriles paralelos no forman un grupo para cambiar de carril.";
            return null;
        }

        static string Test12()
        {
            var all = new List<MapIssue>();
            all.AddRange(MapValidator.Validate(NewMap()));
            var m = NewMap();
            var a = Nd(m, 8f, 7f); var b = Nd(m, 8.1f, 7f); var c = Nd(m, 20f, 3f);
            Lane(m, c, a); Lane(m, b, c);
            all.AddRange(MapValidator.Validate(m));
            var f = FourArms(true); f.Normalize();
            all.AddRange(MapValidator.Validate(f));
            foreach (var i in all)
                if (string.IsNullOrEmpty(i.what) || string.IsNullOrEmpty(i.fix)) return "Mensaje incompleto: " + i.message;
            return null;
        }

        // ---------------- Informe ----------------

        public static string Report(List<SelfTestResult> results)
        {
            var sb = new StringBuilder();
            int pass = 0;
            foreach (var r in results) if (r.passed) pass++;
            sb.AppendLine("Diagnóstico de mapas: " + pass + " de " + results.Count + " pruebas correctas");
            sb.AppendLine("Prueba el modelo por carriles, la conversión de mapas antiguos y la validación; no los toques en pantalla ni el tráfico en marcha.");
            foreach (var r in results)
                sb.AppendLine((r.passed ? "[OK] " : "[FALLA] ") + r.name + (r.passed ? "" : "\n      " + r.detail));
            return sb.ToString();
        }

        public static void SaveReport(string report)
        {
            Debug.Log(report);
            try { File.WriteAllText(Path.Combine(Application.persistentDataPath, "diagnostico.txt"), report); }
            catch (System.Exception) { }
        }
    }
}
