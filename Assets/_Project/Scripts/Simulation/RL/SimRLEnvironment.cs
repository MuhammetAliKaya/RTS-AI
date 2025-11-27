using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using System.Linq;
using UnityEngine;

namespace RTS.Simulation.RL
{
    public class SimRLEnvironment
    {
        public SimWorldState World { get; private set; }

        // EŞİKLER (MALİYETLERE GÖRE AYARLANDI)
        // 0 -> 500 (Yarım) -> 1000 (Yeterli) -> 2000 (Zengin)
        private readonly int[] woodThresholds = { 0, 500, 1000, 2000 };
        private readonly int[] stoneThresholds = { 0, 500, 1000, 2000 };

        // 0 -> 50 (İşçi Maliyeti) -> 100
        private readonly int[] meatThresholds = { 0, 50, 100 };

        public SimRLEnvironment()
        {
            // Reset çağrısı ExperimentManager'dan yapılacak
        }

        public void Reset(int width, int height)
        {
            World = new SimWorldState(width, height);

            // Başlangıç Kaynakları (Scenario bunları üzerine yazabilir, varsayılan değerler)
            SimResourceSystem.AddResource(World, 1, SimResourceType.Meat, 50);
            SimResourceSystem.AddResource(World, 1, SimResourceType.Wood, 0);
            SimResourceSystem.AddResource(World, 1, SimResourceType.Stone, 0);

            // Varsayılan Base (Scenario bunları da ezecek ama güvenlik için kalsın)
            int2 center = new int2(width / 2, height / 2);
            var b = SpawnBuilding(SimBuildingType.Base, center, true);
            SimBuildingSystem.SpawnUnit(World, center, SimUnitType.Worker, 1);
        }

        // --- STATE ---
        public int GetState()
        {
            SimPlayerData p = SimResourceSystem.GetPlayer(World, 1);
            if (p == null) return 0;

            int popState = (p.CurrentPopulation == p.MaxPopulation) ? 1 : 0;
            int woodState = GetThresholdIndex(p.Wood, woodThresholds);
            int stoneState = GetThresholdIndex(p.Stone, stoneThresholds);
            int meatState = GetThresholdIndex(p.Meat, meatThresholds);

            return (woodState * 16) + (stoneState * 4) + (meatState * 2) + popState;
        }

        // --- STEP ---
        public float Step(int action, float dt)
        {
            float reward = -0.1f; // Zaman cezası

            SimUnitData idleWorker = World.Units.Values.FirstOrDefault(
                u => u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle && u.PlayerID == 1
            );

            if (idleWorker != null)
            {
                switch (action)
                {
                    case 0: reward += OrderGather(idleWorker, SimResourceType.Wood); break;
                    case 1: reward += OrderGather(idleWorker, SimResourceType.Stone); break;
                    case 2: reward += OrderGather(idleWorker, SimResourceType.Meat); break;
                    case 3: reward += OrderRecruitWorker(); break;
                    case 4: reward += OrderBuildBarracks(idleWorker); break;
                }
            }

            // İlerleme
            World.TickCount++;
            SimBuildingSystem.UpdateAllBuildings(World, dt);

            foreach (var unit in World.Units.Values.ToList())
            {
                SimUnitSystem.UpdateUnit(unit, World, dt);
            }

            return reward;
        }

        // --- ACTIONS ---
        private float OrderGather(SimUnitData worker, SimResourceType type)
        {
            SimResourceData best = null;
            float minDst = float.MaxValue;

            foreach (var res in World.Resources.Values)
            {
                if (res.Type == type)
                {
                    float d = SimGridSystem.GetDistance(worker.GridPosition, res.GridPosition);
                    if (d < minDst) { minDst = d; best = res; }
                }
            }

            if (best != null)
            {
                if (SimUnitSystem.TryAssignGatherTask(worker, best, World)) return 5f;
            }
            return -0.5f;
        }

        private float OrderRecruitWorker()
        {
            var b = World.Buildings.Values.FirstOrDefault(x => x.Type == SimBuildingType.Base && x.PlayerID == 1);
            if (b == null || b.IsTraining) return -0.5f;

            if (SimResourceSystem.SpendResources(World, 1, 0, 0, SimConfig.WORKER_COST_MEAT))
            {
                b.IsTraining = true;
                b.UnitInProduction = SimUnitType.Worker;
                b.TrainingTimer = 0f;
                return 50.0f;
            }
            return -1f;
        }

        private float OrderBuildBarracks(SimUnitData worker)
        {
            if (!SimResourceSystem.CanAfford(World, 1, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, 0))
                return -1f;

            int2? buildPos = SimGridSystem.FindWalkableNeighbor(World, worker.GridPosition);
            if (buildPos == null) return -1f;

            SimResourceSystem.SpendResources(World, 1, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, 0);

            var b = SpawnBuilding(SimBuildingType.Barracks, buildPos.Value, false);
            SimBuildingSystem.InitializeBuildingStats(b);
            SimUnitSystem.OrderBuild(worker, b, World);

            return 50.0f;
        }

        public bool IsTerminal()
        {
            return World.Buildings.Values.Any(b => b.Type == SimBuildingType.Barracks);
        }

        // --- HELPER ---
        private SimBuildingData SpawnBuilding(SimBuildingType type, int2 pos, bool built)
        {
            var b = new SimBuildingData
            {
                ID = World.NextID(),
                PlayerID = 1,
                Type = type,
                GridPosition = pos,
                IsConstructed = built,
                ConstructionProgress = built ? 100f : 0f
            };
            World.Buildings.Add(b.ID, b);
            World.Map.Grid[pos.x, pos.y].IsWalkable = false;
            if (built) SimBuildingSystem.OnBuildingCompleted(b, World);
            return b;
        }

        private int GetThresholdIndex(int val, int[] t)
        {
            for (int i = t.Length - 1; i >= 0; i--) if (val >= t[i]) return i;
            return 0;
        }
    }
}