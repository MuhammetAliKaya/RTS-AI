using UnityEngine;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Orchestrator;
using System.Linq;

namespace RTS.Simulation.Scenarios
{
    public class EconomyRushScenario : IScenario
    {
        public string ScenarioName => "Economy Rush (Build Barracks)";

        // --- HARİTA KURULUMU ---
        public void SetupMap(SimWorldState world, int seed)
        {
            Random.InitState(seed);

            // Başlangıç Kaynakları
            SimResourceSystem.AddResource(world, 1, SimResourceType.Meat, 50);
            SimResourceSystem.AddResource(world, 1, SimResourceType.Wood, 0);
            SimResourceSystem.AddResource(world, 1, SimResourceType.Stone, 0);

            // Base ve İşçi
            int2 center = new int2(world.Map.Width / 2, world.Map.Height / 2);
            SpawnBuilding(world, SimBuildingType.Base, center, true);
            SimBuildingSystem.SpawnUnit(world, center, SimUnitType.Worker, 1);

            // Kaynak Oluşturma (%10 Doluluk, 250 Miktar)
            var settings = ExperimentManager.Instance;
            if (settings != null)
            {
                int totalTiles = world.Map.Width * world.Map.Height;
                int resourceCount = Mathf.FloorToInt(totalTiles * settings.ResourceDensity);
                GenerateResources(world, resourceCount, settings.ResourceAmountPerNode);
            }
        }

        // --- KAZANMA KOŞULU ---
        // --- KAZANMA KOŞULU (GÜNCELLENDİ) ---
        public bool CheckWinCondition(SimWorldState world, int playerID)
        {
            // ESKİSİ: Hem Barracks olacak HEM DE bitmiş olacak (&& b.IsConstructed)
            // YENİSİ: Listede Barracks varsa (temel atıldıysa) kazanmıştır.

            return world.Buildings.Values.Any(b => b.PlayerID == playerID &&
                                                   b.Type == SimBuildingType.Barracks);
        }

        // --- ÖDÜL HESAPLAMA ---
        public float CalculateReward(SimWorldState world, int playerID, int action)
        {
            float reward = 0f;
            var player = SimResourceSystem.GetPlayer(world, playerID);
            if (player == null) return 0f;

            // 1. KAZANMA ÖDÜLÜ
            if (CheckWinCondition(world, playerID))
            {
                int maxSteps = ExperimentManager.Instance != null ? ExperimentManager.Instance.MaxStepsPerEpisode : 5000;
                int currentSteps = ExperimentManager.Instance != null ? ExperimentManager.Instance.CurrentEpisodeSteps : world.TickCount;

                float speedBonus = (maxSteps - currentSteps) * 10.0f;
                if (speedBonus < 0) speedBonus = 0;
                Debug.Log("speedBonus" + speedBonus);
                return 950.0f + speedBonus; // Büyük final ödülü
            }

            // // Hedefler
            // int targetWood = SimConfig.BARRACKS_COST_WOOD;
            // int targetStone = SimConfig.BARRACKS_COST_STONE;

            // // 2. ARA ÖDÜLLER (BREADCRUMBS) - Yol Gösterici
            // // Kaynak topladıkça (ihtiyaç varsa) ödül ver
            // if (action == 0 && player.Wood < targetWood) reward += 1.0f;
            // if (action == 1 && player.Stone < targetStone) reward += 1.0f;

            // // 3. DOYUMSUZLUK CEZASI
            // if (action == 0 && player.Wood >= targetWood) reward -= 5.0f;

            // if (action == 1 && player.Stone >= targetStone) reward -= 5.0f;

            // // 4. NÜFUS CEZASI
            // // if (action == 2 && player.CurrentPopulation >= player.MaxPopulation) reward -= 5.0f;

            // // 5. FIRSAT TEPME CEZASI (Yumuşatıldı)
            // bool canAffordBarracks = player.Wood >= targetWood && player.Stone >= targetStone;
            // if (canAffordBarracks && action != 4)
            // {
            //     reward -= 1000.0f; // -1000 yerine -10 (Korkutma, uyar)
            // }

            // // 6. YATIRIM FIRSATI CEZASI
            // int workerCost = SimConfig.WORKER_COST_MEAT;
            // bool hasMeat = player.Meat >= workerCost;
            // bool hasSpace = player.CurrentPopulation < player.MaxPopulation;
            // var baseB = world.Buildings.Values.FirstOrDefault(b => b.PlayerID == playerID && b.Type == SimBuildingType.Base);
            // bool isBaseReady = baseB != null && !baseB.IsTraining;

            // if (hasMeat && hasSpace && isBaseReady && action != 3)
            // {
            //     reward -= 500.0f;
            // }

            // if (!hasSpace && action == 2)
            // {
            //     reward -= 500.0f;
            // }

            return reward;
        }

        // --- YARDIMCILAR ---
        private void GenerateResources(SimWorldState world, int count, int amountPerNode)
        {
            for (int i = 0; i < count; i++)
            {
                int x = Random.Range(0, world.Map.Width);
                int y = Random.Range(0, world.Map.Height);
                int2 pos = new int2(x, y);

                if (SimGridSystem.IsWalkable(world, pos))
                {
                    var res = new SimResourceData { ID = world.NextID(), GridPosition = pos, AmountRemaining = amountPerNode };

                    float rng = Random.value;
                    if (rng < 0.4f) res.Type = SimResourceType.Wood;
                    else if (rng < 0.8f) res.Type = SimResourceType.Stone;
                    else res.Type = SimResourceType.Meat;

                    world.Resources.Add(res.ID, res);
                    world.Map.Grid[x, y].IsWalkable = false;

                    if (res.Type == SimResourceType.Wood) world.Map.Grid[x, y].Type = SimTileType.Forest;
                    else if (res.Type == SimResourceType.Stone) world.Map.Grid[x, y].Type = SimTileType.Stone;
                    else world.Map.Grid[x, y].Type = SimTileType.MeatBush;
                }
            }
        }

        private void SpawnBuilding(SimWorldState world, SimBuildingType type, int2 pos, bool constructed)
        {
            var b = new SimBuildingData
            {
                ID = world.NextID(),
                PlayerID = 1,
                Type = type,
                GridPosition = pos,
                IsConstructed = constructed,
                ConstructionProgress = constructed ? 100f : 0f
            };
            SimBuildingSystem.InitializeBuildingStats(b);
            world.Buildings.Add(b.ID, b);
            world.Map.Grid[pos.x, pos.y].IsWalkable = false;

            if (constructed) SimBuildingSystem.OnBuildingCompleted(b, world);
        }
    }
}