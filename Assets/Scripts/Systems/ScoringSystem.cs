using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Traffic;

namespace IntersectionMaster.Systems
{
    /// <summary>Estadísticas en tiempo real, puntuación y estrellas de una partida.</summary>
    public class ScoringSystem
    {
        public int delivered;
        public int accidents;
        public int blockages;
        public int emergencyDelivered;
        public int pedestriansCrossed;
        public float elapsed;
        public int currentQueue;
        public int peakQueue;
        public float emergencyWaitTotal;

        float totalWait;
        float fluiditySum;
        float fluidityTime;

        public float AverageWait { get { return delivered == 0 ? 0f : totalWait / delivered; } }
        public float Fluidity { get { return fluidityTime <= 0f ? 100f : fluiditySum / fluidityTime * 100f; } }

        public int Score
        {
            get
            {
                int s = delivered * 10 + emergencyDelivered * 40 + pedestriansCrossed * 3 - accidents * 150 - blockages * 20
                    + Mathf.RoundToInt(Fluidity * 2f);
                return Mathf.Max(0, s);
            }
        }

        public void Reset()
        {
            delivered = accidents = blockages = emergencyDelivered = pedestriansCrossed = 0;
            elapsed = 0f;
            currentQueue = peakQueue = 0;
            emergencyWaitTotal = totalWait = fluiditySum = fluidityTime = 0f;
        }

        public void OnDelivered(VehicleController v)
        {
            delivered++;
            totalWait += v.totalWait;
            if (v.spec.emergency)
            {
                emergencyDelivered++;
                emergencyWaitTotal += v.totalWait;
            }
        }

        public void OnAccident() { accidents++; }
        public void OnBlockage() { blockages++; }

        public void Sample(float dt, List<VehicleController> vehicles)
        {
            elapsed += dt;
            int moving = 0, queue = 0;
            for (int i = 0; i < vehicles.Count; i++)
            {
                var v = vehicles[i];
                if (v.agent != 0 || v.obstacle) continue;   // peatones, trenes, bicis y barcos no cuentan para la fluidez del tráfico
                if (!v.crashed && v.speed > 0.2f * v.spec.maxSpeed) moving++;
                if (v.speed < 0.05f) queue++;
            }
            currentQueue = queue;
            if (queue > peakQueue) peakQueue = queue;
            int cars = 0;
            for (int i = 0; i < vehicles.Count; i++) if (vehicles[i].agent == 0 && !vehicles[i].obstacle) cars++;
            if (cars > 0)
            {
                fluiditySum += (float)moving / cars * dt;
                fluidityTime += dt;
            }
        }

        /// <summary>1 estrella por completar, 2 sin accidentes, 3 sin accidentes y con buena fluidez.</summary>
        public int Stars(bool won)
        {
            if (!won) return 0;
            int stars = 1;
            if (accidents == 0)
            {
                stars = 2;
                if (Fluidity >= 55f && AverageWait <= 12f) stars = 3;
            }
            return stars;
        }
    }
}
