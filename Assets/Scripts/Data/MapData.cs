using System;
using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Core;

namespace IntersectionMaster.Data
{
    /// <summary>Tipos de vehículo. Se pueden añadir más al final (cada uno ocupa un bit en las máscaras de carril).</summary>
    public enum VehicleType { Car, Taxi, Bus, Truck, Motorbike, Ambulance, FireTruck, Police, Van, Tram }

    public enum DecorType
    {
        Building, Tree, Park, Lamp, ParkedCar, Fence, Hedge, Smoke,
        Trailer, FloodLight, GlowLight, RailSign, PhonePole, RoadSign, RoundStructure, BusShelter, Containers,
        PowerLine, Crane, Sticker, TextLabel, Lines, Misc, Boat, Material, Parking,
        StopLine, Sidewalk, Median,
        TrafficSign,    // señal del catálogo (SignCatalog): variant = señal; color 0 = con poste, 1 = sin poste
        Zone,           // área de muchos puntos dibujada a mano (césped, agua, arena...): pts relativos a (x, y)
        Trail,          // camino de muchos puntos (sendero, canal, muro...): pts relativos a (x, y), ancho = 0,4 × tamaño
        WallSticker     // pegatina de pared (ventana, puerta, cartel...): rot = dirección de la pared, height = piso
    }

    /// <summary>Puntos lógicos sobre una calle: parada de autobús, peaje y detector de vehículos (pide verde al semáforo).</summary>
    public enum PointKind { BusStop, Toll, Detector }

    /// <summary>Señales de tráfico. Stop/Ceda/Prioridad regulan la prioridad en un cruce; las prohibiciones cierran giros.</summary>
    public enum SignType { Stop, Yield, Priority, NoLeft, NoRight, NoStraight, SpeedLimit, Light }

    /// <summary>
    /// Nodo de la red: un punto libre del mapa. Los extremos de calle pueden ser entrada o salida de vehículos;
    /// con roundabout = true el nodo es una rotonda de radio dado en la que desembocan sus calles.
    /// </summary>
    [Serializable]
    public class NodeData
    {
        public int id;
        public float x;
        public float y;
        public bool entry;
        public bool exit;
        public bool roundabout;
        public float weight = 1f;
        public float radius = 0.9f;
        public int ctrl;          // controlador de semáforos: 0 = el del mapa, 1 = manual, 2 = ciclo automático
        public float green;       // segundos en verde del ciclo automático (0 = el del mapa)
        public float yellow;      // segundos en ámbar (0 = el valor por defecto)
        public float allRed;      // controlador: «Espera» con todo en rojo entre fases (0 = el valor por defecto)
        public bool sensor;       // controlador: «Sensor activado» (salta las fases sin vehículos esperando)
        // Nodo de inicio (como el original): vehículos por minuto de cada tipo; -1 = el tráfico general del mapa.
        public float rateCar = -1f, rateTruck = -1f, rateBus = -1f, rateMoto = -1f, rateTram = -1f;
        // Probabilidad de destino: peso de cada nodo final (vacío = todos por igual / el peso de la Tabla de tráfico).
        public List<DestWeight> dest = new List<DestWeight>();

        public bool CustomRates { get { return rateCar >= 0f || rateTruck >= 0f || rateBus >= 0f || rateMoto >= 0f || rateTram >= 0f; } }

        public const int RateCount = 5;
        public static readonly string[] RateNames = { "Coches", "Camiones", "Buses", "Motos", "Tranvías" };
        public float GetRate(int i) { return i == 0 ? rateCar : i == 1 ? rateTruck : i == 2 ? rateBus : i == 3 ? rateMoto : rateTram; }
        public void SetRate(int i, float v) { if (i == 0) rateCar = v; else if (i == 1) rateTruck = v; else if (i == 2) rateBus = v; else if (i == 3) rateMoto = v; else rateTram = v; }

        public Vector2 Pos { get { return new Vector2(x, y); } }
    }

    /// <summary>Peso (0..100) de un nodo final como destino de los vehículos que salen de un nodo de inicio.</summary>
    [Serializable]
    public class DestWeight
    {
        public int node;
        public float weight = 50f;
    }

    /// <summary>
    /// Calle entre dos nodos (a -> b). lanesAB = carriles en el sentido a->b, lanesBA = en el sentido contrario
    /// (0 = calle de sentido único). El punto de control de la curva es el punto medio + (bendX, bendY).
    /// maskAB / maskBA: giros permitidos por carril al llegar al cruce (1=izquierda, 2=recto, 4=derecha; 0=automático).
    /// </summary>
    [Serializable]
    public class RoadData
    {
        public int id;
        public int a;
        public int b;
        public int lanesAB = 1;
        public int lanesBA = 1;
        public float bendX;
        public float bendY;
        public bool limited;
        public bool noLaneChange;
        public int layer;
        public bool hidden;      // no se dibuja (pero se recorre)
        public bool noMarks;     // sin marcas viales
        public int color;        // paleta de color del asfalto (0 = el del tema)
        public bool cables;      // postes y cables eléctricos a lo largo de la calle
        public bool rails;       // barandillado a ambos lados
        public int allow;        // tipos de vehículo permitidos (bits 1 << VehicleType); 0 = todos
        public string roadText = "";   // texto pintado en la calzada («BUS», «SOLO», «LENTO»…), como el original
        public bool avoid;       // «Evitar»: los vehículos no lo usan en sus rutas si hay otro camino (como el original)
        public bool pedestrian;  // calle peatonal: ningún vehículo entra; se dibuja como plaza peatonal
        public const int PedestrianMask = 1 << 30;
        /// <summary>Tráfico permitido: ninguno (en el original, carril con todo el tráfico desactivado).</summary>
        public const int ClosedMask = 1 << 29;
        // Marca pintada en el carril (como «Marcas» del original): 0 ninguna (roadText suelto, en el medio),
        // 1 velocidad (el número de la velocidad del carril en un círculo), 2 BUS, 3 rombo, 4 tranvía, 5 ceda,
        // 6 texto (roadText), 7 incorporación, 8 carriles. markT = posición en el carril (0 a 1; -1 = en el medio).
        public const int MarkText = 6;
        public int markKind;
        public float markT = -1f;
        public int markColor;    // 0 blanca, 1 amarilla
        public bool locked;      // bloqueada en el editor: no se puede mover, modificar ni borrar

        // ---- Modelo por carriles (como Intersection Controller 2): cada RoadData es UN carril de un solo sentido a → b ----
        public int laneType;     // 0 vehículos, 1 acera (peatones), 2 ferrocarril, 3 carril bici, 4 ruta de barco
        public int lineL;        // línea del borde izquierdo: 0 ninguna, 1 blanca, 2 amarilla, 3 blanca discontinua, 4 amarilla discontinua, 5 doble amarilla
        public int lineR;        // línea del borde derecho (mismos valores)
        // Líneas como el original (si lineL y lineR son 0): -1 = automática («A»).
        public int lineSame = -1;   // junto a un carril del mismo sentido: 0 ninguna, 1 blanca discontinua, 2 blanca continua, 3 amarilla discontinua, 4 amarilla continua
        public int lineOpp = -1;    // junto a un carril de sentido contrario: 0 ninguna, 1 amarilla, 2 doble amarilla, 3 blanca, 4 amarilla discontinua, 5 blanca discontinua
        public int lineEdge = -1;   // borde sin carril al lado: 0 ninguna, 1 blanca, 2 amarilla
        public bool tram;           // el tranvía puede ir por este carril (como «Tranvía» en Tráfico permitido del original)
        public string colorHex = "";  // color propio del carril (#RRGGBB) elegido en la paleta; vacío = el de «color»
        public int speedKmh;     // límite propio en km/h (0 = el normal)
        public int ramp;         // rampa de puente: -1 baja una capa, 0 plano, +1 sube una capa (de a hacia b)
        public int railColor;    // barandillado: 0 acero, 1 amarillo, 2 blanco, 3 oscuro
        public int cableSide;    // cables: 0 a la derecha, 1 a la izquierda
        public float laneWidth;  // ancho propio (0 = el normal de su tipo); los mapas convertidos guardan el antiguo
        public int crossStyle = 3;   // acera que cruza la calzada: 0 sin marcas, 1 bloque, 2 dos líneas, 3 cebra, 4 diagonal, 5 escalera
        public bool priority;    // carril con prioridad (p. ej. el anillo de una glorieta): los demás le ceden el paso al cruzarse

        public const int LaneVehicle = 0, LaneSidewalk = 1, LaneRail = 2, LaneBike = 3, LaneBoat = 4, LaneShoulder = 5;
        public static readonly string[] LaneTypeNames = { "Carril del vehículo", "Acera", "Ferrocarril", "Carril bici", "Ruta de barco", "Berma" };
        /// <summary>¿Es un color «#RRGGBB» válido?</summary>
        public static bool ValidHex(string h)
        {
            if (string.IsNullOrEmpty(h) || h.Length != 7 || h[0] != '#') return false;
            for (int i = 1; i < 7; i++) if (Uri.IsHexDigit(h[i]) == false) return false;
            return true;
        }

        public static readonly string[] LineNames = { "Sin línea", "Blanca", "Amarilla", "Blanca discontinua", "Amarilla discontinua", "Doble amarilla" };
        public static readonly int[] SpeedSteps = { 0, 20, 30, 40, 50, 60, 80, 100, 120 };

        /// <summary>Ancho normal de un carril según su tipo (unidades de mundo). Un carril de vehículos ocupa una celda de 0,5.</summary>
        public static float WidthOf(int laneType)
        {
            switch (laneType)
            {
                case LaneSidewalk: return 0.50f;   // igual que el de coches: encaja en la cuadrícula pegada a la calle (sin hueco verde)
                case LaneRail: return 0.50f;
                case LaneBike: return 0.25f;       // la mitad del de coches
                case LaneBoat: return 0.75f;
                case LaneShoulder: return 0.25f;   // berma: como el carril bici; el ancho se puede cambiar
                default: return 0.50f;
            }
        }

        /// <summary>Ancho de este carril.</summary>
        public float Width { get { return laneWidth > 0.05f ? laneWidth : WidthOf(laneType); } }

        public static readonly string[] AllowLabels = { "Todos", "Sin pesados", "Ligeros", "Solo emergencias", "Bus y taxi" };
        static readonly int[] AllowMasks =
        {
            0,
            (1 << (int)VehicleType.Car) | (1 << (int)VehicleType.Taxi) | (1 << (int)VehicleType.Motorbike) | (1 << (int)VehicleType.Ambulance) | (1 << (int)VehicleType.Police) | (1 << (int)VehicleType.Van),
            (1 << (int)VehicleType.Car) | (1 << (int)VehicleType.Taxi) | (1 << (int)VehicleType.Motorbike) | (1 << (int)VehicleType.Van),
            (1 << (int)VehicleType.Ambulance) | (1 << (int)VehicleType.FireTruck) | (1 << (int)VehicleType.Police),
            (1 << (int)VehicleType.Bus) | (1 << (int)VehicleType.Taxi)
        };

        public int AllowPreset
        {
            get { for (int i = 0; i < AllowMasks.Length; i++) if (AllowMasks[i] == allow) return i; return 0; }
        }

        public void NextAllowPreset() { allow = AllowMasks[(AllowPreset + 1) % AllowMasks.Length]; }
        public int[] maskAB = new int[0];
        public int[] maskBA = new int[0];
        /// <summary>Tráfico permitido por carril (bits 1 << VehicleType). 0 = usa el valor de la calle (allow).</summary>
        public int[] allowAB = new int[0];
        public int[] allowBA = new int[0];

        public const int VehicleTypeCount = 9;
        public const int AllVehicles = (1 << VehicleTypeCount) - 1;
        const int OldAllVehicles = 255;     // antes de añadir la furgoneta
        const int OldNoHeavy = 1 | 2 | 16 | 32 | 128, OldLight = 1 | 2 | 16;

        /// <summary>Adapta máscaras guardadas antes de existir la furgoneta.</summary>
        public void MigrateVehicleMasks()
        {
            if (allow == OldNoHeavy || allow == OldLight) allow |= 1 << (int)VehicleType.Van;
            if (allowAB != null) for (int i = 0; i < allowAB.Length; i++) if (allowAB[i] == OldAllVehicles) allowAB[i] = AllVehicles;
            if (allowBA != null) for (int i = 0; i < allowBA.Length; i++) if (allowBA[i] == OldAllVehicles) allowBA[i] = AllVehicles;
        }

        // ---- Acera y mediana propias de la calle ----
        public float sidewalk = -1f;   // ancho de acera (unidades): -1 = el normal, 0 = sin acera
        public float median;           // ancho de la mediana entre sentidos (0 = sin mediana)
        public int medianType;         // 0 hierba, 1 hormigón, 2 barrera
        public static readonly string[] MedianNames = { "Hierba", "Hormigón", "Barrera" };

        /// <summary>Mediana efectiva: solo existe si hay carriles en los dos sentidos.</summary>
        public float MedianWidth { get { return lanesAB > 0 && lanesBA > 0 ? Mathf.Clamp(median, 0f, 1.5f) : 0f; } }

        /// <summary>Máscara efectiva de un carril: la propia si existe, si no la de la calle. 0 = todos.</summary>
        public int GetLaneAllow(bool forward, int lane)
        {
            if (pedestrian) return PedestrianMask;
            int[] arr = forward ? allowAB : allowBA;
            if (arr != null && lane >= 0 && lane < arr.Length && arr[lane] != 0) return arr[lane] == AllVehicles ? 0 : arr[lane];
            return allow;
        }

        /// <summary>Valor guardado para el carril tal cual (0 = hereda de la calle).</summary>
        public int GetLaneAllowRaw(bool forward, int lane)
        {
            int[] arr = forward ? allowAB : allowBA;
            if (arr != null && lane >= 0 && lane < arr.Length && arr[lane] != 0) return arr[lane];
            return allow == 0 ? AllVehicles : allow;
        }

        public void SetLaneAllow(bool forward, int lane, int value)
        {
            int count = forward ? lanesAB : lanesBA;
            if (lane < 0 || lane >= count) return;
            int[] arr = forward ? allowAB : allowBA;
            if (arr == null || arr.Length != count)
            {
                var n = new int[count];
                if (arr != null) for (int i = 0; i < Mathf.Min(arr.Length, count); i++) n[i] = arr[i];
                arr = n;
                if (forward) allowAB = arr; else allowBA = arr;
            }
            arr[lane] = value;
        }

        public void ResetLaneAllow() { allowAB = new int[0]; allowBA = new int[0]; }

        public bool Curved { get { return bendX * bendX + bendY * bendY > 0.0025f; } }

        public int GetMask(bool forward, int lane)
        {
            int[] arr = forward ? maskAB : maskBA;
            if (arr == null || lane < 0 || lane >= arr.Length) return 0;
            return arr[lane];
        }

        public void SetMask(bool forward, int lane, int value)
        {
            int count = forward ? lanesAB : lanesBA;
            if (lane < 0 || lane >= count) return;
            int[] arr = forward ? maskAB : maskBA;
            if (arr == null || arr.Length != count)
            {
                var n = new int[count];
                if (arr != null) for (int i = 0; i < Mathf.Min(arr.Length, count); i++) n[i] = arr[i];
                arr = n;
                if (forward) maskAB = arr; else maskBA = arr;
            }
            arr[lane] = value;
        }
    }

    /// <summary>Señal en una calle. node = nodo hacia el que se aproxima (-1 para el límite de velocidad, que afecta a toda la calle).</summary>
    [Serializable]
    public class SignItem
    {
        public int road;
        public int node;
        public SignType type;
        // Solo semáforos:
        public int model;          // 0 vertical en poste, 1 horizontal en báculo, 2 con flechas, 3 con semáforo peatonal
        public float size = 1f;    // escala del modelo (0,6 a 1,8)
        public int side;           // 0 = a la derecha de la calzada, 1 = a la izquierda
        public int phase;          // 0 = independiente; 1..6 = fase compartida: los semáforos de la misma fase cambian juntos
        public int style;          // Stop: 0 línea continua, 1 cuadros, 2 controlado por el usuario. Ceda: 0 triángulos, 1 discontinua, 2 puntos
        public bool hidden;        // semáforo invisible: regula igual, pero no se dibuja (como «Visible» del original)

        public static readonly string[] ModelNames = { "Vertical", "Horizontal (báculo)", "Con flechas", "Con peatonal" };
    }

    /// <summary>Paso de peatones en una calle; t = posición entre 0 y 1 a lo largo de su eje.</summary>
    [Serializable]
    public class CrosswalkItem
    {
        public int road;
        public float t = 0.5f;
    }

    [Serializable]
    public class DecorItem
    {
        public float x;
        public float y;
        public DecorType type;
        public int variant;
        public float size = 1f;      // escala (0,4 a 3)
        public float rot;            // giro en grados (0 = orientación automática según la calle en vallas/coches)
        public string text = "";     // Texto
        public float x2, y2;         // líneas: punto final; áreas: semitamaño (ancho/2, alto/2)
        public int color;            // paleta del objeto
        public bool locked;          // bloqueado en el editor
        public int group;            // 0 = suelto; los objetos con el mismo grupo se mueven y duplican juntos
        public float light = 1f;     // farolas: alcance de la luz (0,3 a 3), como el círculo del original

        // Propiedades como el original (mapas por carriles). Los valores por defecto dejan los mapas viejos igual.
        public int height = 1;           // edificio: pisos (1 a 10)
        public bool noShadow;            // edificio: sin sombras
        public bool lowHeight;           // edificio: «Reducir altura» (se ve más bajo)
        public int roof;                 // edificio: tejado (0 dos aguas, 1 plano, 2 cuatro aguas, 3 terraza)
        public int roofColor;            // edificio: color del tejado
        public bool roofItems;           // edificio: «Opciones del techo» (aparatos y claraboyas)
        public int wall;                 // edificio: paredes (0 lisas, 1 ventanas, 2 cristal)
        public float usage = 0.3f;       // aparcamiento: probabilidad de que haya un coche en cada plaza
        public float presence = 1f;      // coche aparcado: probabilidad de que esté al jugar («Uso»)
        public int spaces = 4;           // aparcamiento: plazas por fila
        public bool twoWay;              // aparcamiento: dos filas enfrentadas
        public bool invisible;           // aparcamiento: sin dibujar el suelo ni las rayas (solo los coches)
        public int color2;               // coche aparcado: color B; material: color de las marcas
        public int carFlags;             // coche aparcado: 1 luces, 2 emergencia, 4 intermitente izq., 8 intermitente der.
        public int bars;                 // material: barras (0 ninguna, 1 «/», 2 «//», 3 «////»)
        public bool overLane;            // material: por encima de los carriles (sin «Bajo carril»)
        public float transparency;       // material: transparencia (0 a 0,9)
        public string hexA = "", hexB = "";
        public float flashPeriod;          // luz: «Destello» (segundos de un ciclo encendido/apagado; 0 = fija)   // colores elegidos con el selector (si están, mandan sobre la paleta)

        /// <summary>Zona y camino: puntos relativos a (x, y), en orden.</summary>
        public List<Vector2> pts = new List<Vector2>();

        public Vector2 Pos { get { return new Vector2(x, y); } }
        public Vector2 End { get { return new Vector2(x2, y2); } }
        public static bool IsPoly(DecorType t) { return t == DecorType.Zone || t == DecorType.Trail; }
        public bool IsPoly() { return IsPoly(type); }
        public Vector2 PolyPoint(int i) { return new Vector2(x + pts[i].x, y + pts[i].y); }
        public float TrailWidth { get { return 0.4f * Mathf.Clamp(size, 0.3f, 4f); } }

        /// <summary>Tipos que pueden estar sobre la calzada (marcas, textos, materiales, luces...).</summary>
        public static bool OnRoadOk(DecorType t)
        {
            switch (t)
            {
                case DecorType.Sticker: case DecorType.TextLabel: case DecorType.Lines: case DecorType.Material: case DecorType.Parking:
                case DecorType.GlowLight: case DecorType.Boat: case DecorType.PowerLine:
                case DecorType.StopLine: case DecorType.Sidewalk: case DecorType.Median: case DecorType.TrafficSign:
                case DecorType.Zone: case DecorType.Trail: return true;
            }
            return false;
        }
        public static bool IsLine(DecorType t) { return t == DecorType.PowerLine || t == DecorType.Lines || t == DecorType.StopLine || t == DecorType.Sidewalk || t == DecorType.Median; }
        public static bool IsArea(DecorType t) { return t == DecorType.Material || t == DecorType.Parking; }
        public bool IsLine() { return IsLine(type); }
        public bool IsArea() { return IsArea(type); }

        /// <summary>Distancia desde p al objeto (0 si p está dentro de un área).</summary>
        public float DistTo(Vector2 p)
        {
            if (type == DecorType.TrafficSign)
            {
                // Se puede tocar la base o la placa (que está encima del poste).
                float h = color == 0 ? 0.19f * size * 0.85f : 0f;
                float t;
                return Mathf.Max(0f, Geo.DistToSegment(p, Pos, Pos + new Vector2(0f, h), out t) - 0.1f * size);
            }
            if (IsLine())
            {
                float t;
                return Geo.DistToSegment(p, Pos, End, out t);
            }
            if (IsPoly() && pts != null && pts.Count > 0)
            {
                float best = Vector2.Distance(p, PolyPoint(0));
                int n = pts.Count;
                bool closed = type == DecorType.Zone && n >= 3;
                for (int i = 0; i + 1 < n || (closed && i < n); i++)
                {
                    float t;
                    best = Mathf.Min(best, Geo.DistToSegment(p, PolyPoint(i), PolyPoint((i + 1) % n), out t));
                }
                if (type == DecorType.Trail) return Mathf.Max(0f, best - TrailWidth * 0.5f);
                if (closed)
                {
                    // ¿Dentro del polígono? (regla del rayo)
                    bool inside = false;
                    for (int i = 0, j = n - 1; i < n; j = i++)
                    {
                        Vector2 a = PolyPoint(i), b = PolyPoint(j);
                        if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
                    }
                    if (inside) return 0f;
                }
                return best;
            }
            if (type == DecorType.Material && pts != null && pts.Count >= 3)
            {
                // Material con esquinas (como el original): su forma real, no el rectángulo que la encierra.
                int n = pts.Count;
                float best = float.MaxValue;
                bool inside = false;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    Vector2 a = PolyPoint(i), b = PolyPoint(j);
                    float t;
                    best = Mathf.Min(best, Geo.DistToSegment(p, a, b, out t));
                    if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
                }
                return inside ? 0f : best;
            }
            if (IsArea())
            {
                float hx = Mathf.Abs(x2) * size, hy = Mathf.Abs(y2) * size;
                float dx = Mathf.Max(Mathf.Abs(p.x - x) - hx, 0f), dy = Mathf.Max(Mathf.Abs(p.y - y) - hy, 0f);
                return Mathf.Sqrt(dx * dx + dy * dy);
            }
            return Vector2.Distance(p, Pos);
        }

        public DecorItem Copy(Vector2 offset)
        {
            var c = (DecorItem)MemberwiseClone();   // copia todas las propiedades (también las nuevas)
            c.pts = pts != null ? new List<Vector2>(pts) : new List<Vector2>();
            c.x = x + offset.x; c.y = y + offset.y;
            if (IsLine()) { c.x2 = x2 + offset.x; c.y2 = y2 + offset.y; }
            return c;
        }
    }

    /// <summary>Punto lógico en una calle. t = posición a lo largo del eje; dir: 0 = sentido a→b, 1 = b→a, 2 = ambos.</summary>
    [Serializable]
    public class PointItem
    {
        public int id;
        public int road;
        public float t = 0.5f;
        public PointKind kind;
        public float seconds = 3f;
        public int dir = 2;
    }

    /// <summary>
    /// Flecha FUNCIONAL pintada en un carril: define los movimientos permitidos al llegar al cruce (1 = izquierda, 2 = recto,
    /// 4 = derecha). Si un carril tiene varias flechas se suman sus movimientos. A diferencia de la pegatina (decorativa),
    /// cambia la red de tráfico. forward = carril del sentido a→b; t = posición 0..1 a lo largo del carril.
    /// </summary>
    [Serializable]
    public class LaneArrowItem
    {
        public int id;
        public int road;
        public bool forward = true;
        public int lane;
        public float t = 0.8f;
        public int moves = 2;
        public float size = 1f;
        public int color;        // 0 blanca, 1 amarilla
        public int style;        // 0 giro normal, 1 curva suave (como las del original para curvas)
        public bool deco;        // solo pintada (importada del original): no cambia por dónde van los coches
    }

    [Serializable]
    public class ButtonLink
    {
        public int road;
        public int node;      // nodo hacia el que se aproxima el semáforo vinculado
    }

    /// <summary>Botón del mapa que, al tocarlo en juego, cambia a la vez todos los semáforos vinculados.</summary>
    [Serializable]
    public class GroupButton
    {
        public int id;
        public float x, y;
        public List<ButtonLink> links = new List<ButtonLink>();
        public Vector2 Pos { get { return new Vector2(x, y); } }
    }

    [Serializable]
    public class TrafficSettings
    {
        public float spawnPerMinute = 24f;      // vehículos por minuto (total del mapa)
        public float speedFactor = 1f;          // multiplicador global de velocidad
        public float emergencyChance = 0.04f;   // probabilidad de que un vehículo sea de emergencia
        public float taxiChance = 0.12f;
        public float busChance = 0.06f;
        public float truckChance = 0.06f;
        public float motorbikeChance = 0.08f;
        public float vanChance = 0.08f;
        public float tramChance = 0.08f;        // solo sale si hay carriles con tranvía permitido
        // Perfiles por tipo de vehículo (multiplicadores, índice = VehicleType; vacío = 1)
        public float[] typeSpeed = new float[0];
        public float[] typeAccel = new float[0];
        public float[] typeGap = new float[0];

        public static float Mul(float[] arr, int type) { return arr != null && type >= 0 && type < arr.Length && arr[type] > 0.01f ? arr[type] : 1f; }

        public static float[] SetMul(float[] arr, int type, float value)
        {
            var n = new float[RoadData.VehicleTypeCount];
            for (int i = 0; i < n.Length; i++) n[i] = Mul(arr, i);
            n[type] = Mathf.Clamp(value, 0.5f, 1.6f);
            return n;
        }
        public bool safeSignals = true;         // true: los semáforos esperan a que el cruce se vacíe
        public bool autoCycle = false;          // ciclo automático de semáforos
        public float greenSeconds = 8f;
        public int maxVehicles = 60;            // vehículos simultáneos como máximo (rendimiento en móvil)
        public int simMode = 0;                 // 0 = realista, 1 = caótico
        public int chaosLevel = 1;              // 0 bajo, 1 medio, 2 alto, 3 extremo
        public float violationChance = -1f;     // probabilidad de infracción; <0 = la del nivel de caos
        public float pedestriansPerMinute = 10f; // peatones que quieren cruzar por minuto (0 = sin peatones)
    }

    [Serializable]
    public class ObjectiveData
    {
        public int targetDelivered;
        public float timeLimitSeconds;
        public int maxAccidents = 3;
    }

    /// <summary>Contenido copiado de una zona rectangular del mapa (posiciones relativas a su esquina inferior izquierda).</summary>
    public class MapRegion
    {
        public float width, height;
        public readonly List<NodeData> nodes = new List<NodeData>();
        public readonly List<RoadData> roads = new List<RoadData>();
        public readonly List<SignItem> signs = new List<SignItem>();
        public readonly List<CrosswalkItem> crosswalks = new List<CrosswalkItem>();
        public readonly List<DecorItem> decor = new List<DecorItem>();
        public readonly List<PointItem> points = new List<PointItem>();
        public readonly List<GroupButton> buttons = new List<GroupButton>();
        public readonly List<LaneArrowItem> arrows = new List<LaneArrowItem>();

        public bool IsEmpty { get { return nodes.Count == 0 && decor.Count == 0; } }
    }

    /// <summary>
    /// Datos serializables de un mapa. Versionado: ver MapSerializationSystem.
    /// v1-v2: cuadrícula de celdas (ya no se admite). v3: red libre de nodos y calles con carriles, curvas y rotondas.
    /// Sistema de coordenadas: unidades de mundo; el mapa ocupa [0,width] x [0,height].
    /// </summary>
    [Serializable]
    public class MapData
    {
        public const int CurrentVersion = 3;
        public const float MinSize = 8f;
        public const float MaxSize = 60f;
        public const float GridStep = 0.25f;
        public const int MaxNodes = 1500;
        public const int MaxRoads = 2000;
        public const int MaxDecor = 4000;   // los mapas grandes importados del original traen miles de objetos
        public const int MinLayer = -4, MaxLayer = 4;

        public int version = CurrentVersion;
        public string id = "";
        public string name = "Mapa nuevo";
        public string author = "Jugador";
        public string description = "";
        public int difficulty = 1;
        public float width = 24f;
        public float height = 14f;
        public int nextId = 0;
        // Área visible al jugar (la línea roja del editor). Con viewW <= 0 se usa el mapa entero con un margen.
        public float viewX, viewY, viewW, viewH;
        public List<NodeData> nodes = new List<NodeData>();
        public List<RoadData> roads = new List<RoadData>();
        public List<SignItem> signs = new List<SignItem>();
        public List<CrosswalkItem> crosswalks = new List<CrosswalkItem>();
        public List<DecorItem> decor = new List<DecorItem>();
        public List<PointItem> points = new List<PointItem>();
        public List<GroupButton> buttons = new List<GroupButton>();
        public List<LaneArrowItem> arrows = new List<LaneArrowItem>();
        /// <summary>true = dibujar flechas automáticas en los carriles sin flecha (como antes). Por defecto no: solo las colocadas a mano.</summary>
        public bool autoArrows;
        /// <summary>true = pasos de peatones automáticos en cada brazo de cruce (como antes). Por defecto se colocan a mano.</summary>
        public bool autoCrosswalks;
        /// <summary>true = líneas de detención automáticas en los cruces. Por defecto se dibujan a mano (Línea de detención).</summary>
        public bool autoStopLines;
        /// <summary>Nivel de funciones del archivo: los mapas anteriores (0) conservan sus marcas automáticas.</summary>
        public int featureLevel;
        public const int CurrentFeatureLevel = 2;
        public TrafficSettings traffic = new TrafficSettings();
        public ObjectiveData objective = new ObjectiveData();
        public string thumbnailBase64 = "";
        public string createdUtc = "";
        public string modifiedUtc = "";

        /// <summary>0 = mapa antiguo (calles con N carriles por sentido); 1 = mapa por carriles (cada calle es un carril).</summary>
        public int laneFormat;
        public const int LaneFormatCurrent = 1;

        // ---- Ajustes de mapa como el original ----
        /// <summary>0 = Diverso (libre), 1 = Simulación (los semáforos van solos), 2 = Controlador de tráfico (objetivo y tiempo).</summary>
        public int playMode;
        public int trafficProfile;             // perfil de tráfico: 0 normal, 1 centro de la ciudad, 2 industrial
        public float trafficAmount = 1f;       // cantidad de tráfico (multiplicador de vehículos por minuto)
        public static readonly string[] TrafficProfileNames = { "Normal", "Centro", "Industrial" };
        public bool leftHand;                  // dirección del tráfico: por la izquierda (postes y semáforos peatonales a la izquierda)
        public int goalVehicles = 70;          // Controlador de tráfico: vehículos que deben llegar
        public float goalMinutes = 8.75f;      // Controlador de tráfico: tiempo de juego
        public float startHour = 17.75f;       // Hora de inicio (para el día y la noche, más adelante)
        public int terrain;                    // color del suelo (MapRenderer.TerrainColors)
        public float rainChance;               // probabilidad de lluvia al empezar (0..1)
        public int events = EventAll;          // eventos aleatorios activados (bits de EventNames)
        public const int EventEmergency = 1, EventBreakdown = 2, EventTaxi = 4, EventFire = 8, EventRoadworks = 16,
            EventBrokenLight = 32, EventBrokenLamp = 64, EventHydrant = 128, EventAll = 255;
        public static readonly string[] EventNames = { "Vehículo de\nemergencia", "Avería y grúa", "Taxi que para", "Incendio", "Obras",
            "Semáforo\naveriado", "Farola rota", "Boca de\nincendios rota" };
        public static readonly string[] PlayModeNames = { "Diverso", "Simulación", "Controlador de tráfico" };
        public static readonly string[] PlayModeHelp =
        {
            "Este es un modo para mapas creativos y otro contenido.",
            "En este modo los vehículos se pueden chocar y siguen las normas de tráfico sin tu ayuda. Los semáforos cambian solos; puedes jugar con ellos o ver cómo funciona el cruce.",
            "En este modo de juego tienes un objetivo y un tiempo limitado para alcanzarlo. Tendrás que controlar los semáforos para que los coches no se choquen."
        };
        public bool IsLaneMap { get { return laneFormat >= LaneFormatCurrent; } }

        /// <summary>Convierte el mapa al modelo por carriles si todavía es del modelo antiguo. No hace nada si ya lo es.</summary>
        public void EnsureLanes()
        {
            if (IsLaneMap) return;
            if (roads == null || roads.Count == 0) { laneFormat = LaneFormatCurrent; return; }
            IntersectionMaster.Traffic.LaneConverter.Convert(this);
            laneFormat = LaneFormatCurrent;
        }

        public static string NewId()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 12);
        }

        public int NewObjectId()
        {
            nextId++;
            return nextId;
        }

        // ---------------- Flechas funcionales ----------------

        /// <summary>Movimientos que permiten las flechas de un carril (0 = el carril no tiene flechas).</summary>
        public int LaneArrowMoves(int roadId, bool forward, int lane)
        {
            int m = 0;
            if (arrows == null) return 0;
            for (int i = 0; i < arrows.Count; i++)
            {
                var a = arrows[i];
                if (a.road == roadId && a.forward == forward && a.lane == lane && !a.deco) m |= a.moves & 7;
            }
            return m;
        }

        public LaneArrowItem Arrow(int id)
        {
            for (int i = 0; i < arrows.Count; i++) if (arrows[i].id == id) return arrows[i];
            return null;
        }

        public LaneArrowItem AddArrow(int roadId, bool forward, int lane, float t, int moves)
        {
            if (arrows.Count >= 400) return null;
            var a = new LaneArrowItem { id = NewObjectId(), road = roadId, forward = forward, lane = lane, t = Mathf.Clamp(t, 0.05f, 0.95f), moves = moves & 7 };
            if (a.moves == 0) a.moves = 2;
            arrows.Add(a);
            return a;
        }

        /// <summary>Convierte las máscaras de giro antiguas (por carril) en flechas funcionales colocadas cerca del cruce.</summary>
        void MigrateLaneMasks()
        {
            foreach (var r in roads)
            {
                for (int dir = 0; dir < 2; dir++)
                {
                    bool fw = dir == 0;
                    int count = fw ? r.lanesAB : r.lanesBA;
                    bool any = false;
                    for (int i = 0; i < count; i++)
                    {
                        int m = r.GetMask(fw, i) & 7;
                        if (m == 0) continue;
                        any = true;
                        if (LaneArrowMoves(r.id, fw, i) == 0) AddArrow(r.id, fw, i, 0.85f, m);
                    }
                    if (any) { if (fw) r.maskAB = new int[0]; else r.maskBA = new int[0]; }
                }
            }
        }

        // ---------------- Consultas ----------------

        public NodeData Node(int id)
        {
            for (int i = 0; i < nodes.Count; i++) if (nodes[i].id == id) return nodes[i];
            return null;
        }

        public RoadData Road(int id)
        {
            for (int i = 0; i < roads.Count; i++) if (roads[i].id == id) return roads[i];
            return null;
        }

        public Vector2 PosOf(int nodeId)
        {
            var n = Node(nodeId);
            return n == null ? Vector2.zero : n.Pos;
        }

        public const float DefaultViewInset = 2f;
        public const float MinViewSize = 5f;

        /// <summary>Zona visible al jugar. Los coches y peatones aparecen fuera de ella.</summary>
        public Rect View
        {
            get
            {
                if (viewW >= MinViewSize && viewH >= MinViewSize)
                {
                    float x = Mathf.Clamp(viewX, 0f, Mathf.Max(0f, width - MinViewSize));
                    float y = Mathf.Clamp(viewY, 0f, Mathf.Max(0f, height - MinViewSize));
                    return new Rect(x, y, Mathf.Min(viewW, width - x), Mathf.Min(viewH, height - y));
                }
                float i = Mathf.Min(DefaultViewInset, (Mathf.Min(width, height) - MinViewSize) * 0.5f);
                i = Mathf.Max(0f, i);
                return new Rect(i, i, width - 2f * i, height - 2f * i);
            }
        }

        /// <summary>Tamaño del mapa de los mapas por carriles (como el original: un terreno grande; lo que se juega es la línea roja).</summary>
        public const float BigSize = 200f;

        /// <summary>
        /// Agranda un mapa por carriles a BigSize × BigSize: todo se desplaza para que la línea roja quede en el centro.
        /// No hace nada si ya es grande. Devuelve true si cambió.
        /// </summary>
        public bool ExpandToBig()
        {
            if (!IsLaneMap || (width >= BigSize - 0.01f && height >= BigSize - 0.01f)) return false;
            Rect v = View;
            Vector2 off = new Vector2(BigSize * 0.5f, BigSize * 0.5f) - v.center;
            off = new Vector2(Mathf.Round(off.x), Mathf.Round(off.y));
            foreach (var n in nodes) { n.x += off.x; n.y += off.y; }
            foreach (var d in decor)
            {
                d.x += off.x; d.y += off.y;
                if (DecorItem.IsLine(d.type)) { d.x2 += off.x; d.y2 += off.y; }
            }
            foreach (var b in buttons) { b.x += off.x; b.y += off.y; }
            width = BigSize; height = BigSize;
            viewX = v.x + off.x; viewY = v.y + off.y; viewW = v.width; viewH = v.height;
            return true;
        }

        public void SetView(Rect r)
        {
            float x0 = Mathf.Clamp(r.xMin, 0f, width - MinViewSize);
            float y0 = Mathf.Clamp(r.yMin, 0f, height - MinViewSize);
            float x1 = Mathf.Clamp(r.xMax, x0 + MinViewSize, width);
            float y1 = Mathf.Clamp(r.yMax, y0 + MinViewSize, height);
            viewX = x0; viewY = y0; viewW = x1 - x0; viewH = y1 - y0;
        }

        public bool InBounds(Vector2 p)
        {
            return p.x >= 0f && p.y >= 0f && p.x <= width && p.y <= height;
        }

        /// <summary>Rumbo (grados) de la calle más cercana al punto; 0 si no hay calles. Sirve para orientar coches aparcados y vallas.</summary>
        public float RoadAngleNear(Vector2 p)
        {
            RoadData best = null;
            float bd = float.MaxValue, bt = 0f;
            for (int i = 0; i < roads.Count; i++)
            {
                float d;
                float t = NearestParam(roads[i], p, out d);
                if (d < bd) { bd = d; best = roads[i]; bt = t; }
            }
            if (best == null) return 0f;
            var pl = CenterOf(best);
            Vector2 tan = pl.TangentAt(pl.Length * Mathf.Clamp01(bt));
            return Mathf.Atan2(tan.y, tan.x) * Mathf.Rad2Deg;
        }

        public Vector2 Clamp(Vector2 p)
        {
            return new Vector2(Mathf.Clamp(p.x, 0f, width), Mathf.Clamp(p.y, 0f, height));
        }

        public int Degree(int nodeId)
        {
            int n = 0;
            for (int i = 0; i < roads.Count; i++) if (roads[i].a == nodeId || roads[i].b == nodeId) n++;
            return n;
        }

        public List<RoadData> RoadsAt(int nodeId)
        {
            var list = new List<RoadData>();
            for (int i = 0; i < roads.Count; i++) if (roads[i].a == nodeId || roads[i].b == nodeId) list.Add(roads[i]);
            return list;
        }

        /// <summary>Carril de a hacia b (solo ese sentido).</summary>
        public RoadData FindLane(int a, int b)
        {
            for (int i = 0; i < roads.Count; i++) if (roads[i].a == a && roads[i].b == b) return roads[i];
            return null;
        }

        public RoadData FindRoad(int a, int b)
        {
            for (int i = 0; i < roads.Count; i++)
            {
                var r = roads[i];
                if ((r.a == a && r.b == b) || (r.a == b && r.b == a)) return r;
            }
            return null;
        }

        public NodeData NodeNear(Vector2 p, float tol)
        {
            NodeData best = null;
            float bd = tol;
            for (int i = 0; i < nodes.Count; i++)
            {
                float d = Vector2.Distance(nodes[i].Pos, p);
                if (d <= bd) { bd = d; best = nodes[i]; }
            }
            return best;
        }

        public Vector2 ControlPoint(RoadData r)
        {
            Vector2 a = PosOf(r.a), b = PosOf(r.b);
            return (a + b) * 0.5f + new Vector2(r.bendX, r.bendY);
        }

        /// <summary>Eje de la calle (de a hacia b) como polilínea; las curvas son bézier cuadráticas.</summary>
        public Polyline CenterOf(RoadData r)
        {
            Vector2 a = PosOf(r.a), b = PosOf(r.b);
            if (!r.Curved) return new Polyline(new[] { a, b });
            Vector2 c = ControlPoint(r);
            float len = Vector2.Distance(a, c) + Vector2.Distance(c, b);
            // Curvas finas (puntos cada 6 cm, y más en curvas cerradas): bordes lisos, sin dientes.
            float bendiness = Vector2.Distance(c, (a + b) * 0.5f) / Mathf.Max(0.1f, Vector2.Distance(a, b));
            float step = bendiness > 0.4f ? 0.04f : 0.06f;
            int n = Mathf.Clamp(Mathf.CeilToInt(len / step), 12, 240);
            var pts = new Vector2[n + 1];
            for (int i = 0; i <= n; i++) pts[i] = Geo.Quad(a, c, b, i / (float)n);
            return new Polyline(pts);
        }

        /// <summary>Parámetro t (0..1) de la curva más cercano a p, y distancia al eje.</summary>
        public float NearestParam(RoadData r, Vector2 p, out float dist)
        {
            Vector2 a = PosOf(r.a), b = PosOf(r.b);
            if (!r.Curved)
            {
                float t;
                dist = Geo.DistToSegment(p, a, b, out t);
                return t;
            }
            Vector2 c = ControlPoint(r);
            const int n = 64;
            float best = float.MaxValue, bestT = 0f;
            Vector2 prev = a;
            for (int i = 1; i <= n; i++)
            {
                Vector2 q = Geo.Quad(a, c, b, i / (float)n);
                float u;
                float d = Geo.DistToSegment(p, prev, q, out u);
                if (d < best) { best = d; bestT = (i - 1 + u) / n; }
                prev = q;
            }
            dist = best;
            return bestT;
        }

        public Vector2 PointOnRoad(RoadData r, float t)
        {
            Vector2 a = PosOf(r.a), b = PosOf(r.b);
            if (!r.Curved) return Vector2.Lerp(a, b, t);
            return Geo.Quad(a, ControlPoint(r), b, t);
        }

        public RoadData NearestRoad(Vector2 p, float tol, out float t)
        {
            RoadData best = null;
            float bd = tol;
            t = 0f;
            for (int i = 0; i < roads.Count; i++)
            {
                float d;
                float tt = NearestParam(roads[i], p, out d);
                if (d < bd) { bd = d; best = roads[i]; t = tt; }
            }
            return best;
        }

        /// <summary>Distancia mínima desde p a cualquier calle o rotonda (para colocar decoración fuera de la calzada).</summary>
        /// <summary>Mapas por carriles: distancia al borde del asfalto de coches/bicis más cercano (negativa si está encima).</summary>
        public float DistanceToVehicleLanes(Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < roads.Count; i++)
            {
                var r = roads[i];
                if (r.laneType != RoadData.LaneVehicle && r.laneType != RoadData.LaneBike) continue;
                float d;
                NearestParam(r, p, out d);
                d -= r.Width * 0.5f;
                if (d < best) best = d;
            }
            return best;
        }

        public float DistanceToRoads(Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < roads.Count; i++)
            {
                float d;
                NearestParam(roads[i], p, out d);
                if (d < best) best = d;
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!nodes[i].roundabout) continue;
                float d = Mathf.Max(0f, Vector2.Distance(nodes[i].Pos, p) - nodes[i].radius - 0.3f);
                if (d < best) best = d;
            }
            return best;
        }

        // ---------------- Edición de la red ----------------

        public NodeData AddNode(Vector2 p)
        {
            if (nodes.Count >= MaxNodes) return null;
            p = Clamp(p);
            var n = new NodeData { id = NewObjectId(), x = p.x, y = p.y };
            nodes.Add(n);
            return n;
        }

        public RoadData AddRoad(int a, int b, int lanesAB = 1, int lanesBA = 1)
        {
            if (a == b || roads.Count >= MaxRoads || Node(a) == null || Node(b) == null) return null;
            if (FindRoad(a, b) != null) return null;   // también el sentido contrario: quedarían dos carriles superpuestos
            if (Vector2.Distance(PosOf(a), PosOf(b)) < (IsLaneMap ? 0.25f : 0.6f)) return null;
            if (IsLaneMap) { lanesAB = 1; lanesBA = 0; }
            var r = new RoadData { id = NewObjectId(), a = a, b = b, lanesAB = lanesAB, lanesBA = lanesBA };
            roads.Add(r);
            return r;
        }

        public void RemoveRoad(int roadId)
        {
            var r = Road(roadId);
            if (r == null) return;
            roads.Remove(r);
            signs.RemoveAll(s => s.road == roadId);
            crosswalks.RemoveAll(c => c.road == roadId);
            points.RemoveAll(q => q.road == roadId);
            arrows.RemoveAll(q => q.road == roadId);
            foreach (var bt in buttons) bt.links.RemoveAll(l => l.road == roadId);
            RemoveOrphan(r.a);
            RemoveOrphan(r.b);
        }

        void RemoveOrphan(int nodeId)
        {
            if (Degree(nodeId) == 0) nodes.RemoveAll(n => n.id == nodeId);
        }

        public void RemoveNode(int nodeId)
        {
            var list = RoadsAt(nodeId);
            for (int i = 0; i < list.Count; i++) RemoveRoad(list[i].id);
            nodes.RemoveAll(n => n.id == nodeId);
        }

        public void MoveNode(int nodeId, Vector2 p)
        {
            var n = Node(nodeId);
            if (n == null) return;
            p = Clamp(p);
            n.x = p.x;
            n.y = p.y;
        }

        /// <summary>
        /// Parte una calle en el punto de su eje más cercano a p, creando un nodo (un cruce si luego se une otra calle).
        /// Devuelve el nodo creado o null.
        /// </summary>
        public NodeData SplitRoad(int roadId, Vector2 p)
        {
            var r = Road(roadId);
            if (r == null || nodes.Count >= MaxNodes || roads.Count >= MaxRoads) return null;
            float dist;
            float t = Mathf.Clamp(NearestParam(r, p, out dist), 0.02f, 0.98f);
            var node = AddNode(PointOnRoad(r, t));
            if (node == null) return null;
            if (SplitWithNode(r, node, t) == null) { nodes.Remove(node); return null; }
            return node;
        }

        /// <summary>Parte una calle usando un nodo que ya existe (situado sobre ella). Devuelve la segunda mitad (node -> b).</summary>
        public RoadData SplitAtNode(int roadId, int nodeId)
        {
            var r = Road(roadId);
            var node = Node(nodeId);
            if (r == null || node == null || roads.Count >= MaxRoads || r.a == nodeId || r.b == nodeId) return null;
            float dist;
            float t = Mathf.Clamp(NearestParam(r, node.Pos, out dist), 0.02f, 0.98f);
            return SplitWithNode(r, node, t);
        }

        RoadData SplitWithNode(RoadData r, NodeData node, float t)
        {
            if (roads.Count >= MaxRoads) return null;
            Vector2 a = PosOf(r.a), b = PosOf(r.b);
            Vector2 c = ControlPoint(r);
            bool curved = r.Curved;
            Vector2 q0 = Vector2.Lerp(a, c, t), q1 = Vector2.Lerp(c, b, t);
            Vector2 m = node.Pos;

            var second = new RoadData
            {
                id = NewObjectId(),
                a = node.id,
                b = r.b,
                lanesAB = r.lanesAB,
                lanesBA = r.lanesBA,
                limited = r.limited,
                noLaneChange = r.noLaneChange,
                layer = r.layer,
                allow = r.allow,
                sidewalk = r.sidewalk, median = r.median, medianType = r.medianType, color = r.color, noMarks = r.noMarks, hidden = r.hidden, cables = r.cables, rails = r.rails, pedestrian = r.pedestrian,
                laneType = r.laneType, lineL = r.lineL, lineR = r.lineR, lineSame = r.lineSame, lineOpp = r.lineOpp, lineEdge = r.lineEdge, colorHex = r.colorHex, tram = r.tram, speedKmh = r.speedKmh, priority = r.priority, laneWidth = r.laneWidth, crossStyle = r.crossStyle, ramp = r.ramp, railColor = r.railColor, cableSide = r.cableSide
            };
            second.allowAB = r.allowAB == null ? new int[0] : (int[])r.allowAB.Clone();
            second.allowBA = r.allowBA == null ? new int[0] : (int[])r.allowBA.Clone();
            int oldB = r.b;
            second.maskAB = r.maskAB;   // las máscaras de llegada a b pasan a la segunda parte
            r.maskAB = new int[0];
            r.b = node.id;
            second.maskBA = new int[0];

            if (r.ramp != 0)
            {
                // Rampa partida: la primera mitad sigue a la altura de inicio y la segunda hace la subida o bajada.
                second.ramp = r.ramp;
                r.ramp = 0;
            }
            if (curved)
            {
                r.bendX = q0.x - (a.x + m.x) * 0.5f; r.bendY = q0.y - (a.y + m.y) * 0.5f;
                second.bendX = q1.x - (m.x + b.x) * 0.5f; second.bendY = q1.y - (m.y + b.y) * 0.5f;
            }
            roads.Add(second);

            int count = signs.Count;
            for (int i = 0; i < count; i++)
            {
                if (signs[i].road == r.id && signs[i].node == oldB) signs[i].road = second.id;
                else if (signs[i].road == r.id && signs[i].node == -1 && r.limited)
                    signs.Add(new SignItem { road = second.id, node = -1, type = SignType.SpeedLimit });
            }
            for (int i = 0; i < crosswalks.Count; i++)
            {
                var cw = crosswalks[i];
                if (cw.road != r.id) continue;
                if (cw.t >= t) { cw.road = second.id; cw.t = Mathf.Clamp01((cw.t - t) / Mathf.Max(0.01f, 1f - t)); }
                else cw.t = Mathf.Clamp01(cw.t / Mathf.Max(0.01f, t));
            }
            for (int i = 0; i < points.Count; i++)
            {
                var pt = points[i];
                if (pt.road != r.id) continue;
                if (pt.t >= t) { pt.road = second.id; pt.t = Mathf.Clamp01((pt.t - t) / Mathf.Max(0.01f, 1f - t)); }
                else pt.t = Mathf.Clamp01(pt.t / Mathf.Max(0.01f, t));
            }
            for (int i = 0; i < arrows.Count; i++)
            {
                var ar = arrows[i];
                if (ar.road != r.id) continue;
                if (ar.t >= t) { ar.road = second.id; ar.t = Mathf.Clamp((ar.t - t) / Mathf.Max(0.01f, 1f - t), 0.05f, 0.95f); }
                else ar.t = Mathf.Clamp(ar.t / Mathf.Max(0.01f, t), 0.05f, 0.95f);
            }
            foreach (var bt in buttons)
                foreach (var l in bt.links)
                    if (l.road == r.id && l.node == oldB) l.road = second.id;
            return second;
        }

        /// <summary>Une el nodo "remove" al nodo "keep": sus calles pasan a "keep" (se descartan las repetidas o de longitud casi nula).</summary>
        public void MergeNodes(int keepId, int removeId)
        {
            if (keepId == removeId || Node(keepId) == null || Node(removeId) == null) return;
            var list = RoadsAt(removeId);
            foreach (var r in list)
            {
                if (r.a == removeId) r.a = keepId;
                if (r.b == removeId) r.b = keepId;
            }
            var dead = new List<int>();
            for (int i = 0; i < roads.Count; i++)
            {
                var r = roads[i];
                if (r.a == r.b) { dead.Add(r.id); continue; }
                for (int j = 0; j < i; j++)
                {
                    var o = roads[j];
                    if ((o.a == r.a && o.b == r.b) || (o.a == r.b && o.b == r.a)) { dead.Add(r.id); break; }
                }
            }
            foreach (int id in dead) RemoveRoad(id);
            var rn = Node(removeId);
            var kn = Node(keepId);
            if (rn != null && kn != null)
            {
                kn.entry |= rn.entry;
                kn.exit |= rn.exit;
                kn.roundabout |= rn.roundabout;
                if (rn.roundabout) kn.radius = rn.radius;
            }
            nodes.RemoveAll(n => n.id == removeId);
        }

        /// <summary>Intercambia los extremos de la calle (invierte su sentido), manteniendo carriles, curva y máscaras.</summary>
        public void FlipRoad(RoadData r)
        {
            int t = r.a; r.a = r.b; r.b = t;
            int l = r.lanesAB; r.lanesAB = r.lanesBA; r.lanesBA = l;
            var m = r.maskAB; r.maskAB = r.maskBA; r.maskBA = m;
            var al = r.allowAB; r.allowAB = r.allowBA; r.allowBA = al;
            for (int i = 0; i < arrows.Count; i++)
                if (arrows[i].road == r.id) { arrows[i].forward = !arrows[i].forward; arrows[i].t = 1f - arrows[i].t; }
            for (int i = 0; i < crosswalks.Count; i++) if (crosswalks[i].road == r.id) crosswalks[i].t = 1f - crosswalks[i].t;
            for (int i = 0; i < points.Count; i++)
                if (points[i].road == r.id) { points[i].t = 1f - points[i].t; if (points[i].dir < 2) points[i].dir = 1 - points[i].dir; }
            // El punto de control es relativo al punto medio, que no cambia al invertir la calle.
        }

        // ---------------- Señales ----------------

        public SignItem FindSign(int roadId, int nodeId, SignType type)
        {
            for (int i = 0; i < signs.Count; i++)
            {
                var s = signs[i];
                if (s.road == roadId && s.type == type && (type == SignType.SpeedLimit || s.node == nodeId)) return s;
            }
            return null;
        }

        static bool IsPriorityGroup(SignType t)
        {
            return t == SignType.Stop || t == SignType.Yield || t == SignType.Priority;
        }

        /// <summary>Pone la señal; si ya existe la misma, la quita. Stop/Ceda/Prioridad se sustituyen entre sí.</summary>
        public bool ToggleSign(int roadId, int nodeId, SignType type)
        {
            var r = Road(roadId);
            if (r == null) return false;
            if (type == SignType.SpeedLimit)
            {
                r.limited = !r.limited;
                signs.RemoveAll(s => s.road == roadId && s.type == SignType.SpeedLimit);
                if (r.limited) signs.Add(new SignItem { road = roadId, node = -1, type = SignType.SpeedLimit });
                return true;
            }
            if (nodeId != r.a && nodeId != r.b) return false;
            var existing = FindSign(roadId, nodeId, type);
            if (existing != null) { signs.Remove(existing); return true; }
            if (IsPriorityGroup(type)) signs.RemoveAll(s => s.road == roadId && s.node == nodeId && IsPriorityGroup(s.type));
            signs.Add(new SignItem { road = roadId, node = nodeId, type = type });
            return true;
        }

        // ---------------- Pasos de peatones y decoración ----------------

        public bool ToggleCrosswalk(int roadId, float t)
        {
            var r = Road(roadId);
            if (r == null) return false;
            float len = CenterOf(r).Length;
            for (int i = 0; i < crosswalks.Count; i++)
            {
                if (crosswalks[i].road == roadId && Mathf.Abs(crosswalks[i].t - t) * len < 0.45f)
                {
                    crosswalks.RemoveAt(i);
                    return true;
                }
            }
            crosswalks.Add(new CrosswalkItem { road = roadId, t = Mathf.Clamp(t, 0.05f, 0.95f) });
            return true;
        }

        public bool PlaceDecor(Vector2 p, DecorType type)
        {
            if (decor.Count >= MaxDecor || !InBounds(p)) return false;
            for (int i = 0; i < decor.Count; i++)
                if (Vector2.Distance(decor[i].Pos, p) < 0.3f) return false;
            decor.Add(new DecorItem { x = p.x, y = p.y, type = type, variant = Mathf.Abs(Mathf.RoundToInt(p.x * 31f + p.y * 17f)) % 4 });
            return true;
        }

        public bool EraseDecorNear(Vector2 p, float tol)
        {
            int best = DecorNear(p, tol);
            if (best < 0 || decor[best].locked) return false;
            decor.RemoveAt(best);
            return true;
        }

        /// <summary>Índice del objeto decorativo más cercano a p (los más pequeños y los puntuales ganan a las áreas), o -1.</summary>
        public int DecorNear(Vector2 p, float tol)
        {
            int best = -1;
            float bd = float.MaxValue;
            for (int i = 0; i < decor.Count; i++)
            {
                var d = decor[i];
                float dist = d.DistTo(p);
                if (dist > tol) continue;
                // Las áreas pierden ante lo que está encima; el material bajo carril, el último de todos (como el original).
                float score = dist + (d.type == DecorType.Material && !d.overLane ? 2f : (d.IsArea() ? 0.5f : 0f));
                if (score < bd) { bd = score; best = i; }
            }
            return best;
        }

        public PointItem PointNear(Vector2 p, float tol)
        {
            PointItem best = null;
            float bd = tol;
            foreach (var pt in points)
            {
                var r = Road(pt.road);
                if (r == null) continue;
                float d = Vector2.Distance(PointOnRoad(r, pt.t), p);
                if (d < bd) { bd = d; best = pt; }
            }
            return best;
        }

        public GroupButton ButtonNear(Vector2 p, float tol)
        {
            GroupButton best = null;
            float bd = tol;
            foreach (var b in buttons)
            {
                float d = Vector2.Distance(b.Pos, p);
                if (d < bd) { bd = d; best = b; }
            }
            return best;
        }

        public GroupButton Button(int id)
        {
            for (int i = 0; i < buttons.Count; i++) if (buttons[i].id == id) return buttons[i];
            return null;
        }

        public PointItem Point(int id)
        {
            for (int i = 0; i < points.Count; i++) if (points[i].id == id) return points[i];
            return null;
        }

        /// <summary>Añade un punto lógico; si ya hay uno del mismo tipo cerca, lo quita (alternar). Devuelve true si cambió algo.</summary>
        public bool TogglePoint(int roadId, float t, PointKind kind, int dir)
        {
            var r = Road(roadId);
            if (r == null) return false;
            float len = CenterOf(r).Length;
            for (int i = 0; i < points.Count; i++)
            {
                var q = points[i];
                if (q.road == roadId && q.kind == kind && Mathf.Abs(q.t - t) * len < 0.7f && (q.dir == dir || q.dir == 2 || dir == 2))
                {
                    points.RemoveAt(i);
                    return true;
                }
            }
            if (points.Count >= 120) return false;
            var np = new PointItem { id = NewObjectId(), road = roadId, t = Mathf.Clamp(t, 0.08f, 0.92f), kind = kind, dir = dir };
            np.seconds = kind == PointKind.BusStop ? 4f : (kind == PointKind.Toll ? 1.5f : 0.5f);
            points.Add(np);
            return true;
        }

        public GroupButton AddButton(Vector2 p)
        {
            if (buttons.Count >= 20) return null;
            var b = new GroupButton { id = NewObjectId(), x = p.x, y = p.y };
            buttons.Add(b);
            return b;
        }

        public bool PlaceDecorItem(DecorItem d)
        {
            if (decor.Count >= MaxDecor || d == null || !InBounds(d.Pos)) return false;
            decor.Add(d);
            return true;
        }

        // ---------------- Zonas (copiar / cortar / pegar) ----------------

        public MapRegion ExtractRegion(Rect rect)
        {
            var reg = new MapRegion { width = rect.width, height = rect.height };
            Vector2 o = rect.min;
            var inside = new HashSet<int>();
            foreach (var n in nodes)
            {
                if (!rect.Contains(n.Pos)) continue;
                inside.Add(n.id);
                reg.nodes.Add(new NodeData { id = n.id, x = n.x - o.x, y = n.y - o.y, entry = n.entry, exit = n.exit, roundabout = n.roundabout, radius = n.radius, weight = n.weight, ctrl = n.ctrl, green = n.green, yellow = n.yellow, rateCar = n.rateCar, rateTruck = n.rateTruck, rateBus = n.rateBus, rateMoto = n.rateMoto, dest = new List<DestWeight>() });
            }
            var roadIds = new HashSet<int>();
            foreach (var r in roads)
            {
                if (!inside.Contains(r.a) || !inside.Contains(r.b)) continue;
                roadIds.Add(r.id);
                reg.roads.Add(new RoadData
                {
                    id = r.id, a = r.a, b = r.b, lanesAB = r.lanesAB, lanesBA = r.lanesBA, bendX = r.bendX, bendY = r.bendY,
                    limited = r.limited, noLaneChange = r.noLaneChange, avoid = r.avoid, roadText = r.roadText, markKind = r.markKind, markT = r.markT, markColor = r.markColor, layer = r.layer, hidden = r.hidden, noMarks = r.noMarks, color = r.color, cables = r.cables, rails = r.rails, pedestrian = r.pedestrian, laneType = r.laneType, lineL = r.lineL, lineR = r.lineR, lineSame = r.lineSame, lineOpp = r.lineOpp, lineEdge = r.lineEdge, colorHex = r.colorHex, tram = r.tram, speedKmh = r.speedKmh, priority = r.priority, laneWidth = r.laneWidth, ramp = r.ramp, railColor = r.railColor, cableSide = r.cableSide, locked = r.locked, sidewalk = r.sidewalk, median = r.median, medianType = r.medianType, allow = r.allow, allowAB = r.allowAB == null ? new int[0] : (int[])r.allowAB.Clone(), allowBA = r.allowBA == null ? new int[0] : (int[])r.allowBA.Clone(), maskAB = (int[])r.maskAB.Clone(), maskBA = (int[])r.maskBA.Clone()
                });
            }
            foreach (var s in signs) if (roadIds.Contains(s.road)) reg.signs.Add(new SignItem { road = s.road, node = s.node, type = s.type, model = s.model, size = s.size, side = s.side, phase = s.phase });
            foreach (var c in crosswalks) if (roadIds.Contains(c.road)) reg.crosswalks.Add(new CrosswalkItem { road = c.road, t = c.t });
            foreach (var d in decor)
                if (rect.Contains(d.Pos)) reg.decor.Add(d.Copy(-o));
            foreach (var q in points) if (roadIds.Contains(q.road)) reg.points.Add(new PointItem { id = q.id, road = q.road, t = q.t, kind = q.kind, seconds = q.seconds, dir = q.dir });
            foreach (var q in arrows) if (roadIds.Contains(q.road)) reg.arrows.Add(new LaneArrowItem { id = q.id, road = q.road, forward = q.forward, lane = q.lane, t = q.t, moves = q.moves, size = q.size, color = q.color, style = q.style, deco = q.deco });
            foreach (var bt in buttons)
            {
                if (!rect.Contains(bt.Pos)) continue;
                var nb = new GroupButton { id = bt.id, x = bt.x - o.x, y = bt.y - o.y };
                foreach (var l in bt.links) if (roadIds.Contains(l.road) && inside.Contains(l.node)) nb.links.Add(new ButtonLink { road = l.road, node = l.node });
                reg.buttons.Add(nb);
            }
            return reg;
        }

        public void ClearRegion(Rect rect)
        {
            var ids = new List<int>();
            foreach (var n in nodes)
            {
                if (!rect.Contains(n.Pos)) continue;
                bool locked = false;
                foreach (var r in RoadsAt(n.id)) if (r.locked) locked = true;
                if (!locked) ids.Add(n.id);      // las calles bloqueadas no se borran con la zona
            }
            foreach (int id in ids) RemoveNode(id);
            decor.RemoveAll(d => rect.Contains(d.Pos) && !d.locked);
            buttons.RemoveAll(b => rect.Contains(b.Pos));
        }

        /// <summary>Pega la zona con su esquina inferior izquierda en origin. Las calles pegadas son independientes del resto.</summary>
        public bool PasteRegion(MapRegion reg, Vector2 origin)
        {
            if (reg == null || reg.IsEmpty) return false;
            var map = new Dictionary<int, int>();
            var roadMap = new Dictionary<int, int>();
            foreach (var n in reg.nodes)
            {
                if (nodes.Count >= MaxNodes) break;
                var p = Clamp(n.Pos + origin);
                var nn = new NodeData { id = NewObjectId(), x = p.x, y = p.y, entry = n.entry, exit = n.exit, roundabout = n.roundabout, radius = n.radius, weight = n.weight, ctrl = n.ctrl, green = n.green, yellow = n.yellow };
                nodes.Add(nn);
                map[n.id] = nn.id;
            }
            foreach (var r in reg.roads)
            {
                int na, nb;
                if (roads.Count >= MaxRoads || !map.TryGetValue(r.a, out na) || !map.TryGetValue(r.b, out nb)) continue;
                var nr = new RoadData
                {
                    id = NewObjectId(), a = na, b = nb, lanesAB = r.lanesAB, lanesBA = r.lanesBA, bendX = r.bendX, bendY = r.bendY,
                    limited = r.limited, noLaneChange = r.noLaneChange, avoid = r.avoid, roadText = r.roadText, markKind = r.markKind, markT = r.markT, markColor = r.markColor, layer = r.layer, hidden = r.hidden, noMarks = r.noMarks, color = r.color, cables = r.cables, rails = r.rails, pedestrian = r.pedestrian, laneType = r.laneType, lineL = r.lineL, lineR = r.lineR, lineSame = r.lineSame, lineOpp = r.lineOpp, lineEdge = r.lineEdge, colorHex = r.colorHex, tram = r.tram, speedKmh = r.speedKmh, priority = r.priority, laneWidth = r.laneWidth, ramp = r.ramp, locked = r.locked, sidewalk = r.sidewalk, median = r.median, medianType = r.medianType, allow = r.allow, allowAB = r.allowAB == null ? new int[0] : (int[])r.allowAB.Clone(), allowBA = r.allowBA == null ? new int[0] : (int[])r.allowBA.Clone(), maskAB = (int[])r.maskAB.Clone(), maskBA = (int[])r.maskBA.Clone()
                };
                roads.Add(nr);
                roadMap[r.id] = nr.id;
            }
            foreach (var s in reg.signs)
            {
                int rid;
                if (!roadMap.TryGetValue(s.road, out rid)) continue;
                int nid = -1;
                if (s.node != -1 && !map.TryGetValue(s.node, out nid)) continue;
                signs.Add(new SignItem { road = rid, node = nid, type = s.type, model = s.model, size = s.size, side = s.side, phase = s.phase });
            }
            foreach (var c in reg.crosswalks)
            {
                int rid;
                if (roadMap.TryGetValue(c.road, out rid)) crosswalks.Add(new CrosswalkItem { road = rid, t = c.t });
            }
            foreach (var d in reg.decor)
            {
                if (decor.Count >= MaxDecor) break;
                var p = d.Pos + origin;
                if (InBounds(p)) decor.Add(d.Copy(origin));
            }
            foreach (var q in reg.points)
            {
                int rid;
                if (points.Count < 120 && roadMap.TryGetValue(q.road, out rid))
                    points.Add(new PointItem { id = NewObjectId(), road = rid, t = q.t, kind = q.kind, seconds = q.seconds, dir = q.dir });
            }
            foreach (var q in reg.arrows)
            {
                int rid;
                if (arrows.Count < 400 && roadMap.TryGetValue(q.road, out rid))
                    arrows.Add(new LaneArrowItem { id = NewObjectId(), road = rid, forward = q.forward, lane = q.lane, t = q.t, moves = q.moves, size = q.size, color = q.color, style = q.style, deco = q.deco });
            }
            foreach (var bt in reg.buttons)
            {
                var p = bt.Pos + origin;
                if (!InBounds(p) || buttons.Count >= 20) continue;
                var nb = new GroupButton { id = NewObjectId(), x = p.x, y = p.y };
                foreach (var l in bt.links)
                {
                    int rid, nid;
                    if (roadMap.TryGetValue(l.road, out rid) && map.TryGetValue(l.node, out nid)) nb.links.Add(new ButtonLink { road = rid, node = nid });
                }
                buttons.Add(nb);
            }
            Normalize();
            return true;
        }

        // ---------------- Saneamiento ----------------

        /// <summary>Elimina referencias inválidas (nodos o calles inexistentes, duplicados, valores fuera de rango).</summary>
        public void Normalize()
        {
            if (traffic == null) traffic = new TrafficSettings();
            traffic.simMode = 0;   // el modo caótico se quitó
            traffic.chaosLevel = Mathf.Clamp(traffic.chaosLevel, 0, 3);
            traffic.maxVehicles = traffic.maxVehicles <= 0 ? 60 : Mathf.Clamp(traffic.maxVehicles, 10, 200);
            traffic.vanChance = float.IsNaN(traffic.vanChance) ? 0f : Mathf.Clamp01(traffic.vanChance);
            traffic.tramChance = float.IsNaN(traffic.tramChance) ? 0f : Mathf.Clamp01(traffic.tramChance);
            playMode = Mathf.Clamp(playMode, 0, 2);
            goalVehicles = Mathf.Clamp(goalVehicles, 5, 999);
            goalMinutes = float.IsNaN(goalMinutes) ? 8.75f : Mathf.Clamp(goalMinutes, 1f, 60f);
            startHour = float.IsNaN(startHour) ? 17.75f : Mathf.Repeat(startHour, 24f);
            terrain = Mathf.Max(0, terrain);
            rainChance = float.IsNaN(rainChance) ? 0f : Mathf.Clamp01(rainChance);
            trafficProfile = Mathf.Clamp(trafficProfile, 0, 2);
            trafficAmount = float.IsNaN(trafficAmount) || trafficAmount <= 0.01f ? 1f : Mathf.Clamp(trafficAmount, 0.25f, 3f);
            events &= EventAll;
            if (description == null) description = "";
            bool legacyFeatures = featureLevel < CurrentFeatureLevel;
            if (legacyFeatures)
            {
                // Mapa anterior: conserva el aspecto que tenía (flechas, pasos y líneas de detención automáticos).
                if (featureLevel < 2) { autoArrows = true; autoCrosswalks = true; autoStopLines = true; }
                featureLevel = CurrentFeatureLevel;
            }
            if (nodes == null) nodes = new List<NodeData>();
            if (roads == null) roads = new List<RoadData>();
            if (signs == null) signs = new List<SignItem>();
            if (crosswalks == null) crosswalks = new List<CrosswalkItem>();
            if (decor == null) decor = new List<DecorItem>();
            if (points == null) points = new List<PointItem>();
            if (buttons == null) buttons = new List<GroupButton>();

            if (nodes.Count > MaxNodes) nodes.RemoveRange(MaxNodes, nodes.Count - MaxNodes);
            if (roads.Count > MaxRoads) roads.RemoveRange(MaxRoads, roads.Count - MaxRoads);
            if (decor.Count > MaxDecor) decor.RemoveRange(MaxDecor, decor.Count - MaxDecor);

            // Nodos: ids únicos y positivos, posiciones dentro del mapa.
            int maxId = 0;
            foreach (var n in nodes) if (n != null && n.id > maxId) maxId = n.id;
            foreach (var r in roads) if (r != null && r.id > maxId) maxId = r.id;
            foreach (var q in points) if (q != null && q.id > maxId) maxId = q.id;
            foreach (var b in buttons) if (b != null && b.id > maxId) maxId = b.id;
            if (arrows == null) arrows = new List<LaneArrowItem>();
            foreach (var q in arrows) if (q != null && q.id > maxId) maxId = q.id;
            if (nextId < maxId) nextId = maxId;

            var seenNodes = new HashSet<int>();
            nodes.RemoveAll(n => n == null || n.id <= 0 || !seenNodes.Add(n.id));
            foreach (var n in nodes)
            {
                if (float.IsNaN(n.x) || float.IsInfinity(n.x)) n.x = 0f;
                if (float.IsNaN(n.y) || float.IsInfinity(n.y)) n.y = 0f;
                n.x = Mathf.Clamp(n.x, 0f, width);
                n.y = Mathf.Clamp(n.y, 0f, height);
                n.radius = Mathf.Clamp(float.IsNaN(n.radius) ? 0.9f : n.radius, 0.6f, 2f);
                for (int ri = 0; ri < NodeData.RateCount; ri++) { float rv = n.GetRate(ri); n.SetRate(ri, float.IsNaN(rv) || rv < 0f ? -1f : Mathf.Min(rv, 60f)); }
                if (n.dest == null) n.dest = new List<DestWeight>();
                n.dest.RemoveAll(dw => dw == null || float.IsNaN(dw.weight));
                foreach (var dw in n.dest) dw.weight = Mathf.Clamp(dw.weight, 0f, 100f);
            }

            var seenRoads = new HashSet<int>();
            var pairs = new HashSet<long>();
            roads.RemoveAll(r =>
            {
                if (r == null || r.id <= 0 || !seenRoads.Add(r.id)) return true;
                if (r.a == r.b || !seenNodes.Contains(r.a) || !seenNodes.Contains(r.b)) return true;
                // En mapas por carriles a→b y b→a son carriles distintos (uno por sentido).
                long key = IsLaneMap ? (long)r.a * 1000003L + r.b : (long)Mathf.Min(r.a, r.b) * 1000003L + Mathf.Max(r.a, r.b);
                return !pairs.Add(key);
            });
            bool laneMap = IsLaneMap || roads.Count == 0;
            if (laneMap) laneFormat = LaneFormatCurrent;
            foreach (var r in roads)
            {
                if (r.roadText == null) r.roadText = ""; else if (r.roadText.Length > 14) r.roadText = r.roadText.Substring(0, 14);
                r.lanesAB = Mathf.Clamp(r.lanesAB, 0, 3);
                r.lanesBA = Mathf.Clamp(r.lanesBA, 0, 3);
                if (r.lanesAB + r.lanesBA == 0) { r.lanesAB = 1; r.lanesBA = 1; }
                r.laneType = Mathf.Clamp(r.laneType, 0, RoadData.LaneTypeNames.Length - 1);
                r.lineL = Mathf.Clamp(r.lineL, 0, RoadData.LineNames.Length - 1);
                r.lineR = Mathf.Clamp(r.lineR, 0, RoadData.LineNames.Length - 1);
                r.lineSame = Mathf.Clamp(r.lineSame, -1, 4);
                r.markKind = Mathf.Clamp(r.markKind, 0, 8);
                if (float.IsNaN(r.markT) || r.markT > 1f) r.markT = -1f;
                r.markColor = Mathf.Clamp(r.markColor, 0, 1);
                r.lineOpp = Mathf.Clamp(r.lineOpp, -1, 5);
                r.lineEdge = Mathf.Clamp(r.lineEdge, -1, 2);
                if (!RoadData.ValidHex(r.colorHex)) r.colorHex = "";
                r.speedKmh = Mathf.Clamp(r.speedKmh, 0, 200);
                r.laneWidth = float.IsNaN(r.laneWidth) ? 0f : Mathf.Clamp(r.laneWidth, 0f, 2f);
                r.layer = Mathf.Clamp(r.layer, laneMap ? MinLayer : 0, MaxLayer);
                r.ramp = Mathf.Clamp(r.ramp, -1, 1);
                r.railColor = Mathf.Clamp(r.railColor, 0, 3);
                r.cableSide = Mathf.Clamp(r.cableSide, 0, 1);
                if (r.layer + r.ramp < MinLayer || r.layer + r.ramp > MaxLayer) r.ramp = 0;
                if (laneMap)
                {
                    // Un carril de un solo sentido de a hacia b (si venía al revés, se invierte).
                    if (r.lanesAB == 0 && r.lanesBA > 0) { int t0 = r.a; r.a = r.b; r.b = t0; }
                    r.lanesAB = 1; r.lanesBA = 0;
                    if (r.pedestrian) { r.laneType = RoadData.LaneSidewalk; r.pedestrian = false; }
                    r.median = 0f;
                }
                if (float.IsNaN(r.bendX) || float.IsInfinity(r.bendX)) r.bendX = 0f;
                if (float.IsNaN(r.bendY) || float.IsInfinity(r.bendY)) r.bendY = 0f;
                r.bendX = Mathf.Clamp(r.bendX, -30f, 30f);
                r.bendY = Mathf.Clamp(r.bendY, -30f, 30f);
                r.maskAB = FitMasks(r.maskAB, r.lanesAB);
                r.maskBA = FitMasks(r.maskBA, r.lanesBA);
            }

            // Un nodo con rotonda necesita al menos una calle.
            var roadsPerNode = new Dictionary<int, int>();
            foreach (var r in roads)
            {
                int c;
                roadsPerNode.TryGetValue(r.a, out c); roadsPerNode[r.a] = c + 1;
                roadsPerNode.TryGetValue(r.b, out c); roadsPerNode[r.b] = c + 1;
            }
            nodes.RemoveAll(n => !roadsPerNode.ContainsKey(n.id));

            var roadById = new Dictionary<int, RoadData>();
            foreach (var r in roads) roadById[r.id] = r;

            var sgSeen = new HashSet<string>();
            signs.RemoveAll(s =>
            {
                RoadData r;
                if (s == null || !roadById.TryGetValue(s.road, out r) || !Enum.IsDefined(typeof(SignType), s.type)) return true;
                if (s.type == SignType.SpeedLimit) { s.node = -1; r.limited = true; }
                else if (s.node != r.a && s.node != r.b) return true;
                return !sgSeen.Add(s.road + "," + s.node + "," + (int)s.type);
            });
            foreach (var sg in signs)
            {
                sg.model = Mathf.Clamp(sg.model, 0, SignItem.ModelNames.Length - 1);
                sg.style = Mathf.Clamp(sg.style, 0, 2);
                sg.size = float.IsNaN(sg.size) || sg.size <= 0.01f ? 1f : Mathf.Clamp(sg.size, 0.6f, 1.8f);
                sg.side = Mathf.Clamp(sg.side, 0, 1);
                sg.phase = Mathf.Clamp(sg.phase, 0, 6);
            }
            foreach (var r in roads)
            {
                if (!r.limited) continue;
                bool has = false;
                foreach (var s in signs) if (s.road == r.id && s.type == SignType.SpeedLimit) { has = true; break; }
                if (!has) signs.Add(new SignItem { road = r.id, node = -1, type = SignType.SpeedLimit });
            }

            var cwSeen = new HashSet<int>();
            crosswalks.RemoveAll(c => c == null || !roadById.ContainsKey(c.road) || float.IsNaN(c.t) || cwSeen.Count > 200);
            foreach (var c in crosswalks) c.t = Mathf.Clamp(c.t, 0.05f, 0.95f);

            decor.RemoveAll(d => d == null || float.IsNaN(d.x) || float.IsNaN(d.y) || !InBounds(d.Pos) || !Enum.IsDefined(typeof(DecorType), d.type));
            foreach (var d in decor)
            {
                d.size = float.IsNaN(d.size) ? 1f : Mathf.Clamp(d.size, 0.4f, 3f);
                if (float.IsNaN(d.rot) || float.IsInfinity(d.rot)) d.rot = 0f;
                if (float.IsNaN(d.x2) || float.IsInfinity(d.x2)) d.x2 = 0f;
                if (float.IsNaN(d.y2) || float.IsInfinity(d.y2)) d.y2 = 0f;
                if (d.text == null) d.text = "";
                if (d.hexA == null) d.hexA = "";
                if (d.hexB == null) d.hexB = "";
                if (d.pts == null) d.pts = new List<Vector2>();
                d.pts.RemoveAll(q => float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsInfinity(q.x) || float.IsInfinity(q.y));
                if (d.pts.Count > 64) d.pts.RemoveRange(64, d.pts.Count - 64);
                if (d.IsPoly() && d.pts.Count == 0) d.pts.Add(Vector2.zero);
                if (d.text.Length > 40) d.text = d.text.Substring(0, 40);
                if (d.IsArea() && (Mathf.Abs(d.x2) < 0.3f || Mathf.Abs(d.y2) < 0.3f)) { d.x2 = Mathf.Max(0.3f, Mathf.Abs(d.x2)); d.y2 = Mathf.Max(0.3f, Mathf.Abs(d.y2)); }
                d.height = Mathf.Clamp(d.height <= 0 ? 1 : d.height, 1, 10);
                d.usage = float.IsNaN(d.usage) ? 0.3f : Mathf.Clamp01(d.usage);
                d.presence = float.IsNaN(d.presence) ? 1f : Mathf.Clamp01(d.presence);
                d.spaces = Mathf.Clamp(d.spaces <= 0 ? 4 : d.spaces, 1, 60);
                d.transparency = float.IsNaN(d.transparency) ? 0f : Mathf.Clamp(d.transparency, 0f, 0.9f);
                d.bars = Mathf.Clamp(d.bars, 0, 3);
                d.roof = Mathf.Clamp(d.roof, 0, 3); d.wall = Mathf.Clamp(d.wall, 0, 2);
                d.roofColor = Mathf.Abs(d.roofColor); d.color2 = Mathf.Abs(d.color2); d.carFlags &= 15;
            }

            if (points == null) points = new List<PointItem>();
            if (buttons == null) buttons = new List<GroupButton>();
            points.RemoveAll(q => q == null || !roadById.ContainsKey(q.road) || float.IsNaN(q.t) || !Enum.IsDefined(typeof(PointKind), q.kind));
            foreach (var q in points)
            {
                q.t = Mathf.Clamp(q.t, 0.05f, 0.95f);
                q.dir = Mathf.Clamp(q.dir, 0, 2);
                q.seconds = float.IsNaN(q.seconds) ? 2f : Mathf.Clamp(q.seconds, 0f, 20f);
                if (q.id <= 0) q.id = NewObjectId();
            }
            arrows.RemoveAll(q => q == null || !roadById.ContainsKey(q.road) || float.IsNaN(q.t) || float.IsNaN(q.size)
                || q.lane < 0 || q.lane >= (q.forward ? roadById[q.road].lanesAB : roadById[q.road].lanesBA));
            foreach (var q in arrows)
            {
                q.t = q.deco ? Mathf.Clamp01(q.t) : Mathf.Clamp(q.t, 0.05f, 0.95f);
                q.size = Mathf.Clamp(q.size, 0.5f, 2f);
                q.moves &= 7;
                if (q.moves == 0) q.moves = 2;
                if (q.id <= 0) q.id = NewObjectId();
            }
            MigrateLaneMasks();
            foreach (var r in roads)
            {
                if (legacyFeatures) r.MigrateVehicleMasks();
                r.sidewalk = float.IsNaN(r.sidewalk) ? -1f : (r.sidewalk < 0f ? -1f : Mathf.Clamp(r.sidewalk, 0f, 1f));
                r.median = float.IsNaN(r.median) ? 0f : Mathf.Clamp(r.median, 0f, 1.5f);
                r.medianType = Mathf.Clamp(r.medianType, 0, RoadData.MedianNames.Length - 1);
            }
            buttons.RemoveAll(b => b == null || float.IsNaN(b.x) || float.IsNaN(b.y) || !InBounds(b.Pos));
            foreach (var b in buttons)
            {
                if (b.id <= 0) b.id = NewObjectId();
                if (b.links == null) b.links = new List<ButtonLink>();
                var seen = new HashSet<long>();
                b.links.RemoveAll(l => l == null || !roadById.ContainsKey(l.road) || (l.node != roadById[l.road].a && l.node != roadById[l.road].b) || !seen.Add((long)l.road * 1000003L + l.node));
            }
            foreach (var n in nodes)
            {
                n.ctrl = Mathf.Clamp(n.ctrl, 0, 2);
                n.green = float.IsNaN(n.green) ? 0f : Mathf.Clamp(n.green, 0f, 60f);
                n.yellow = float.IsNaN(n.yellow) ? 0f : Mathf.Clamp(n.yellow, 0f, 6f);
            }
            if (!IsLaneMap && roads.Count > 0) EnsureLanes();
        }

        static int[] FitMasks(int[] masks, int count)
        {
            var result = new int[count];
            if (masks != null)
                for (int i = 0; i < Mathf.Min(masks.Length, count); i++) result[i] = masks[i] & 7;
            return result;
        }

        /// <summary>Tamaño máximo: los mapas por carriles llegan a 200 × 200 (como el original); los antiguos a 60.</summary>
        public float MaxSizeAllowed { get { return IsLaneMap ? Mathf.Max(MaxSize, BigSize) : MaxSize; } }

        public void Resize(float w, float h)
        {
            width = Mathf.Clamp(w, MinSize, MaxSizeAllowed);
            height = Mathf.Clamp(h, MinSize, MaxSizeAllowed);
            foreach (var n in nodes)
            {
                n.x = Mathf.Min(n.x, width);
                n.y = Mathf.Min(n.y, height);
            }
            decor.RemoveAll(d => !InBounds(d.Pos));
            if (viewW > 0f) SetView(View);
            Normalize();
        }

        public void Touch()
        {
            modifiedUtc = DateTime.UtcNow.ToString("o");
            if (string.IsNullOrEmpty(createdUtc)) createdUtc = modifiedUtc;
        }

        public MapData Clone()
        {
            return JsonUtility.FromJson<MapData>(JsonUtility.ToJson(this));
        }
    }
}
