using RTS.Simulation.Data;
using UnityEngine;
using System.Linq;

namespace RTS.Simulation.Systems
{
    public static class SimBuildingSystem
    {
        // --- 1. TÜM BİNALARI GÜNCELLEME (Tick) ---
        public static void UpdateAllBuildings(SimWorldState world, float dt)
        {
            foreach (var building in world.Buildings.Values)
            {
                // Sadece bitmiş binalar üretim/saldırı yapar
                if (!building.IsConstructed) continue;

                UpdateProduction(building, world, dt);
                UpdateResourceGeneration(building, world, dt);
                UpdateTowerCombat(building, world, dt);
            }
        }

        // --- 2. İNŞAAT İLERLETME (AdvanceConstruction) ---
        // İşçinin (SimUnitSystem) çağırdığı fonksiyon budur.
        // Amount: İşçinin bir adımda yaptığı inşaat miktarı.
        public static bool AdvanceConstruction(SimBuildingData building, SimWorldState world, float amount)
        {
            // Zaten bitmişse işlem yapma
            if (building.IsConstructed) return true;

            building.ConstructionProgress += amount;

            // İlerleme %100 (Max) oldu mu?
            if (building.ConstructionProgress >= SimConfig.BUILDING_MAX_PROGRESS)
            {
                building.ConstructionProgress = SimConfig.BUILDING_MAX_PROGRESS;
                building.IsConstructed = true;

                // İNŞAAT BİTTİ! Etkilerini (Nüfus vb.) uygula.
                OnBuildingCompleted(building, world);

                return true; // Bitti sinyali döndür
            }
            return false; // Devam ediyor
        }

        // --- 3. BİNA TAMAMLANINCA ÇALIŞACAK MANTIK ---
        public static void OnBuildingCompleted(SimBuildingData building, SimWorldState world)
        {
            // Nüfus Artışı (Config'den çekiyoruz)
            if (building.Type == SimBuildingType.Base)
            {
                SimResourceSystem.IncreaseMaxPopulation(world, building.PlayerID, SimConfig.POPULATION_BASE);
            }
            else if (building.Type == SimBuildingType.House)
            {
                SimResourceSystem.IncreaseMaxPopulation(world, building.PlayerID, SimConfig.POPULATION_HOUSE);
            }

            // Debug.Log($"✅ BİNA TAMAMLANDI: {building.Type} (ID: {building.ID})");
        }

        // --- 4. BİNA AYARLARI (Spawn Anında) ---
        public static void InitializeBuildingStats(SimBuildingData b)
        {
            b.MaxHealth = 1000;
            b.Health = 1000;
            // Eğer hazır geldiyse %100, yoksa %0 başla
            b.ConstructionProgress = b.IsConstructed ? SimConfig.BUILDING_MAX_PROGRESS : 0f;

            switch (b.Type)
            {
                case SimBuildingType.Farm:
                    b.IsResourceGenerator = true;
                    b.ResourceType = SimResourceType.Meat;
                    b.ResourceInterval = SimConfig.RESOURCE_GENERATION_INTERVAL;
                    b.ResourceAmountPerCycle = SimConfig.RESOURCE_GENERATION_AMOUNT;
                    break;

                case SimBuildingType.WoodCutter:
                    b.IsResourceGenerator = true;
                    b.ResourceType = SimResourceType.Wood;
                    b.ResourceInterval = SimConfig.RESOURCE_GENERATION_INTERVAL;
                    b.ResourceAmountPerCycle = SimConfig.RESOURCE_GENERATION_AMOUNT;
                    break;

                case SimBuildingType.StonePit:
                    b.IsResourceGenerator = true;
                    b.ResourceType = SimResourceType.Stone;
                    b.ResourceInterval = SimConfig.RESOURCE_GENERATION_INTERVAL;
                    b.ResourceAmountPerCycle = SimConfig.RESOURCE_GENERATION_AMOUNT;
                    break;

                case SimBuildingType.Tower:
                    b.Damage = SimConfig.TOWER_DAMAGE;
                    b.AttackRange = SimConfig.TOWER_ATTACK_RANGE;
                    b.AttackSpeed = SimConfig.TOWER_ATTACK_SPEED;
                    break;
            }
        }

        // --- ALT SİSTEMLER ---

        private static void UpdateProduction(SimBuildingData building, SimWorldState world, float dt)
        {
            if (!building.IsTraining) return;

            building.TrainingTimer += dt;

            float requiredTime = (building.UnitInProduction == SimUnitType.Worker)
                ? SimConfig.WORKER_TRAIN_TIME
                : SimConfig.SOLDIER_TRAIN_TIME;

            if (building.TrainingTimer >= requiredTime)
            {
                SpawnUnit(world, building.GridPosition, building.UnitInProduction, building.PlayerID);
                building.TrainingTimer = 0f;
                building.IsTraining = false;
            }
        }

        private static void UpdateResourceGeneration(SimBuildingData building, SimWorldState world, float dt)
        {
            if (!building.IsResourceGenerator) return;

            building.ResourceTimer += dt;

            if (building.ResourceTimer >= building.ResourceInterval)
            {
                building.ResourceTimer = 0f;
                SimResourceSystem.AddResource(world, building.PlayerID, building.ResourceType, building.ResourceAmountPerCycle);
            }
        }

        private static void UpdateTowerCombat(SimBuildingData building, SimWorldState world, float dt)
        {
            if (building.Type != SimBuildingType.Tower) return;

            building.AttackTimer += dt;
            if (building.AttackTimer < building.AttackSpeed) return;

            SimUnitData target = FindNearestEnemy(world, building.GridPosition, building.AttackRange, building.PlayerID);

            if (target != null)
            {
                target.Health -= building.Damage;
                building.AttackTimer = 0f;
                building.TargetUnitID = target.ID; // Görsel hedef

                if (target.Health <= 0) target.State = SimTaskType.Dead;
            }
            else
            {
                building.TargetUnitID = -1;
            }
        }

        // --- YARDIMCILAR ---

        public static void SpawnUnit(SimWorldState world, int2 basePos, SimUnitType type, int playerID)
        {
            if (!SimResourceSystem.HasPopulationSpace(world, playerID)) return;

            int2? spawnPos = SimGridSystem.FindWalkableNeighbor(world, basePos);
            if (spawnPos == null) return;

            SimUnitData newUnit = new SimUnitData
            {
                ID = world.NextID(),
                PlayerID = playerID,
                UnitType = type,
                GridPosition = spawnPos.Value,
                State = SimTaskType.Idle,
                MaxHealth = (type == SimUnitType.Worker) ? SimConfig.WORKER_MAX_HEALTH : SimConfig.SOLDIER_MAX_HEALTH,
                MoveSpeed = (type == SimUnitType.Worker) ? SimConfig.WORKER_MOVE_SPEED : SimConfig.SOLDIER_MOVE_SPEED,
                Damage = (type == SimUnitType.Soldier) ? SimConfig.SOLDIER_DAMAGE : 5,
                AttackRange = SimConfig.SOLDIER_ATTACK_RANGE,
                AttackSpeed = SimConfig.SOLDIER_ATTACK_SPEED
            };
            newUnit.Health = newUnit.MaxHealth;

            world.Units.Add(newUnit.ID, newUnit);
            world.Map.Grid[spawnPos.Value.x, spawnPos.Value.y].OccupantID = newUnit.ID;

            SimResourceSystem.ModifyPopulation(world, playerID, 1);
        }

        private static SimUnitData FindNearestEnemy(SimWorldState world, int2 towerPos, float range, int myPlayerID)
        {
            SimUnitData bestTarget = null;
            float closestDist = range * range;

            foreach (var unit in world.Units.Values)
            {
                if (unit.PlayerID == myPlayerID) continue;
                if (unit.State == SimTaskType.Dead) continue;

                float dx = unit.GridPosition.x - towerPos.x;
                float dy = unit.GridPosition.y - towerPos.y;
                float distSqr = dx * dx + dy * dy;

                if (distSqr <= closestDist)
                {
                    closestDist = distSqr;
                    bestTarget = unit;
                }
            }
            return bestTarget;
        }
    }
}