using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Data;

namespace IntersectionMaster.Systems
{
    public enum GameMode { Free, Infinite, Campaign, Challenge, Test, ScoreCounter }

    public enum GameState { Running, Won, Lost }

    public class LevelDefinition
    {
        public string id;
        public string title;
        public string description;
        public string mapId;
        public GameMode mode;
        public ObjectiveData objective = new ObjectiveData();
        public float spawnPerMinute = 24f;
        public float emergencyChance = 0.04f;
        public bool safeSignals = true;

        /// <summary>Copia del mapa con los ajustes de tráfico del nivel aplicados.</summary>
        public MapData BuildMap()
        {
            var m = SampleMaps.Find(mapId) ?? CampaignMaps.Find(mapId);
            if (m == null) return null;
            m.traffic.spawnPerMinute = spawnPerMinute;
            m.traffic.emergencyChance = emergencyChance;
            m.traffic.safeSignals = safeSignals;
            m.objective = objective;
            return m;
        }
    }

    /// <summary>Define los niveles y decide si la partida se gana o se pierde.</summary>
    public class LevelManager
    {
        public GameMode Mode { get; private set; }
        public LevelDefinition Level { get; private set; }
        public GameState State { get; private set; }
        public ObjectiveData Objective { get; private set; }
        public string EndReason { get; private set; }

        /// <summary>
        /// Campaña: los 5 mundos (12 niveles cada uno, en el orden del original) y al final los 10 niveles clásicos
        /// de Intersection Master (pestaña «Clásicos»), que conservan sus estrellas.
        /// </summary>
        public static readonly List<LevelDefinition> CampaignLevels = BuildCampaign();

        static List<LevelDefinition> ClassicLevels() { return new List<LevelDefinition>
        {
            Lv("c1", "1. Primer cruce", "Deja pasar 20 vehículos. Toca un semáforo para cambiarlo.",
                SampleMaps.CrossX, GameMode.Campaign, 20, 0, 3, 16f, 0.02f, true),
            Lv("c2", "2. Hora punta", "Más tráfico en el mismo cruce: 40 vehículos.",
                SampleMaps.CrossX, GameMode.Campaign, 40, 0, 2, 30f, 0.04f, true),
            Lv("c3", "3. Cruce en T", "La calle secundaria compite con la avenida.",
                SampleMaps.TJunction, GameMode.Campaign, 40, 0, 2, 30f, 0.04f, true),
            Lv("c4", "4. Doble cruce", "Dos cruces seguidos y sin semáforos de seguridad.",
                SampleMaps.DoubleCross, GameMode.Campaign, 60, 0, 1, 36f, 0.05f, false),
            Lv("c5", "5. La ciudad", "Cuatro cruces conectados. Máximo un choque.",
                SampleMaps.Grid, GameMode.Campaign, 80, 0, 1, 44f, 0.05f, false),
            Lv("c6", "6. Rotonda", "Aquí no hay semáforos: la rotonda se regula sola. Observa cómo ceden el paso.",
                SampleMaps.Round, GameMode.Campaign, 40, 0, 2, 30f, 0.04f, true),
            Lv("c7", "7. Señales", "Stop, ceda y prioridad. Los semáforos sólo mandan donde no hay señales.",
                SampleMaps.Signs, GameMode.Campaign, 50, 0, 2, 30f, 0.04f, true),
            Lv("c8", "8. Un sentido", "Dos avenidas de sentido único y tráfico denso. Controla los dos cruces con semáforo.",
                SampleMaps.OneWay, GameMode.Campaign, 60, 0, 1, 38f, 0.05f, true),
            Lv("c9", "9. Dos carriles", "Avenida de dos carriles por sentido con dos cruces. Sin ayudas de seguridad.",
                SampleMaps.Lanes, GameMode.Campaign, 70, 0, 1, 44f, 0.05f, false),
            Lv("c10", "10. Curvas y peatones", "Hay peatones cruzando: los coches se detienen, pero no atasques el cruce.",
                SampleMaps.Curves, GameMode.Campaign, 60, 0, 1, 30f, 0.05f, true),
        }; }

        static List<LevelDefinition> BuildCampaign()
        {
            var list = new List<LevelDefinition>();
            for (int w = 0; w < CampaignMaps.Worlds; w++)
                for (int l = 0; l < CampaignMaps.LevelsPerWorld; l++) list.Add(Camp(w, l));
            list.AddRange(ClassicLevels());
            return list;
        }

        /// <summary>Pestañas de la campaña: los 5 mundos y «Clásicos».</summary>
        public static int WorldTabs { get { return CampaignMaps.Worlds + 1; } }
        public static string WorldName(int w) { return w < CampaignMaps.Worlds ? CampaignMaps.WorldNames[w] : "Clásicos"; }
        public static string WorldHelp(int w) { return w < CampaignMaps.Worlds ? CampaignMaps.WorldHelp[w] : "Los niveles de siempre de Intersection Master."; }

        /// <summary>Mundo (pestaña) de un nivel de la campaña.</summary>
        public static int WorldOf(int index) { return Mathf.Min(index / CampaignMaps.LevelsPerWorld, CampaignMaps.Worlds); }

        /// <summary>Niveles superados para abrir el mundo siguiente (no hace falta acabar todo el mundo anterior).</summary>
        public const int WorldUnlockWins = 6;

        public static int WinsInWorld(int world)
        {
            int n = 0;
            for (int i = 0; i < CampaignLevels.Count; i++) if (WorldOf(i) == world && SaveSystem.Stars(CampaignLevels[i].id) >= 1) n++;
            return n;
        }

        public static int LevelsInWorld(int world)
        {
            int n = 0;
            for (int i = 0; i < CampaignLevels.Count; i++) if (WorldOf(i) == world) n++;
            return n;
        }

        public static bool CampaignUnlocked(int index)
        {
            if (index <= 0) return true;
            int w = WorldOf(index);
            if (w == CampaignMaps.Worlds && WorldOf(index - 1) != w) return true;   // los clásicos siempre se pueden empezar
            if (WorldOf(index - 1) != w) return WinsInWorld(w - 1) >= WorldUnlockWins || SaveSystem.Stars(CampaignLevels[index - 1].id) >= 1;
            return SaveSystem.Stars(CampaignLevels[index - 1].id) >= 1;
        }

        static LevelDefinition Camp(int world, int level)
        {
            int target = CampaignMaps.Target(world, level);
            float secs = CampaignMaps.Seconds(world);
            bool sim = CampaignMaps.IsSimWorld(world);
            int cross = new[] { 1, 2, 1, 2, 3, 4, 3, 4, 6, 6, 8, 9 }[level];
            string[] flavor =
            {
                "Simulación: rotondas y ceda el paso.",
                "Barrio de casas.",
                level >= 3 ? "Calles de un sentido y canales." : "Canales y tejados rojos.",
                level < 8 ? "Sin semáforos: toca los stop para dejar pasar." : "Camiones y trenes.",
                "Simulación: avenidas anchas."
            };
            string when = sim ? " en 2,5 minutos, " : " en 6 minutos, ";
            var lv = Lv(CampaignMaps.Id(world, level), CampaignMaps.WorldNames[world] + " " + (level + 1),
                target + " vehículos" + when + cross + (cross == 1 ? " cruce. " : " cruces. ") + flavor[world],
                CampaignMaps.Id(world, level), GameMode.Campaign, target, secs, sim ? 3 : (level < 4 ? 3 : (level < 8 ? 2 : 1)),
                sim ? 18f + level : Mathf.Clamp(target / 6f * 1.45f, 14f, 60f), world == 4 ? 0.05f : 0.04f, level < 4);
            return lv;
        }

        public static readonly List<LevelDefinition> ChallengeLevels = new List<LevelDefinition>
        {
            Lv("d1", "200 sin choques", "Deja pasar 200 vehículos sin ningún accidente.",
                SampleMaps.DoubleCross, GameMode.Challenge, 200, 0, 0, 60f, 0.04f, true),
            Lv("d2", "Aguanta 3 minutos", "Sobrevive 180 segundos con tráfico intenso.",
                SampleMaps.CrossX, GameMode.Challenge, 0, 180, 1, 40f, 0.04f, false),
            Lv("d3", "Emergencias", "Muchas ambulancias y patrullas: entrega 15 vehículos.",
                SampleMaps.CrossX, GameMode.Challenge, 15, 0, 1, 30f, 0.35f, true),
            Lv("d4", "Cuadrícula intensa", "100 vehículos en 4 minutos.",
                SampleMaps.Grid, GameMode.Challenge, 100, 240, 2, 54f, 0.05f, true),
            Lv("d5", "Rotonda sin choques", "100 vehículos por la rotonda sin ningún accidente.",
                SampleMaps.Round, GameMode.Challenge, 100, 0, 0, 44f, 0.04f, true),
            Lv("d6", "Hora punta total", "150 vehículos en 5 minutos por la avenida de dos carriles.",
                SampleMaps.Lanes, GameMode.Challenge, 150, 300, 1, 60f, 0.05f, false),
        };

        /// <summary>Reto del día: mapa y condiciones elegidos por la fecha (igual para todos los jugadores ese día).</summary>
        public static LevelDefinition Daily()
        {
            var today = System.DateTime.UtcNow.Date;
            int seed = today.Year * 10000 + today.Month * 100 + today.Day;
            var rnd = new System.Random(seed);
            string[] maps =
            {
                SampleMaps.CrossX, SampleMaps.TJunction, SampleMaps.DoubleCross, SampleMaps.Grid, SampleMaps.Round,
                SampleMaps.Signs, SampleMaps.OneWay, SampleMaps.Lanes, SampleMaps.Curves
            };
            string mapId = maps[rnd.Next(maps.Length)];
            var sample = SampleMaps.Find(mapId);
            int target = 30 + rnd.Next(0, 6) * 10;
            float spawn = 28f + rnd.Next(0, 6) * 4f;
            int maxAcc = rnd.Next(0, 3);
            bool safe = rnd.Next(2) == 0;
            return new LevelDefinition
            {
                id = "daily_" + seed,
                title = "Reto del día",
                description = "Mapa «" + (sample != null ? sample.name : mapId) + "»: " + target + " vehículos, máximo " + maxAcc + " choque(s)" + (safe ? "." : ", sin semáforos de seguridad."),
                mapId = mapId,
                mode = GameMode.Challenge,
                objective = new ObjectiveData { targetDelivered = target, timeLimitSeconds = 0f, maxAccidents = maxAcc },
                spawnPerMinute = spawn,
                emergencyChance = 0.06f,
                safeSignals = safe
            };
        }

        static LevelDefinition Lv(string id, string title, string desc, string mapId, GameMode mode,
            int target, float time, int maxAcc, float spawn, float emergency, bool safe)
        {
            return new LevelDefinition
            {
                id = id, title = title, description = desc, mapId = mapId, mode = mode,
                objective = new ObjectiveData { targetDelivered = target, timeLimitSeconds = time, maxAccidents = maxAcc },
                spawnPerMinute = spawn, emergencyChance = emergency, safeSignals = safe
            };
        }

        public const float ScoreCounterSeconds = 360f;

        public void Begin(GameMode mode, LevelDefinition level, ObjectiveData objective)
        {
            Mode = mode;
            Level = level;
            Objective = objective ?? new ObjectiveData();
            State = GameState.Running;
            EndReason = "";
        }

        public void Evaluate(ScoringSystem sc)
        {
            if (State != GameState.Running) return;
            switch (Mode)
            {
                case GameMode.Free:
                case GameMode.Test:
                    return;

                case GameMode.ScoreCounter:
                    // Contador de puntuación: 6 minutos sumando puntos; no se pierde (los choques y esperas restan).
                    if (sc.elapsed >= ScoreCounterSeconds) End(GameState.Won, "Se acabó el tiempo: ¡" + sc.Score + " puntos!");
                    return;

                case GameMode.Infinite:
                    // Puntuación más alta (como el original): se acaba con el primer choque o el primer conductor que espera demasiado.
                    if (sc.accidents >= 1) End(GameState.Lost, "Ha habido un choque");
                    else if (sc.blockages >= 1) End(GameState.Lost, "Un conductor esperó demasiado");
                    return;

                default:
                    if (sc.accidents > Objective.maxAccidents)
                    {
                        End(GameState.Lost, "Demasiados choques");
                    }
                    else if (Objective.targetDelivered > 0 && sc.delivered >= Objective.targetDelivered)
                    {
                        End(GameState.Won, "¡Objetivo cumplido!");
                    }
                    else if (Objective.timeLimitSeconds > 0f && sc.elapsed >= Objective.timeLimitSeconds)
                    {
                        if (Objective.targetDelivered <= 0) End(GameState.Won, "¡Has sobrevivido!");
                        else End(GameState.Lost, "Se acabó el tiempo");
                    }
                    return;
            }
        }

        void End(GameState s, string reason)
        {
            State = s;
            EndReason = reason;
        }
    }
}
