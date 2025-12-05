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


        public static void StartTraining(SimBuildingData building, SimWorldState world, SimUnitType unitType)
        {
            if (building.IsTraining)
            {
                if (SimConfig.EnableLogs) Debug.LogWarning($"⛔ Üretim Reddedildi: Bina {building.ID} zaten meşgul.");
                return;
            }

            // Maliyet Hesabı
            int meatCost = (unitType == SimUnitType.Worker) ? SimConfig.WORKER_COST_MEAT : SimConfig.SOLDIER_COST_MEAT;
            int woodCost = (unitType == SimUnitType.Worker) ? SimConfig.WORKER_COST_WOOD : SimConfig.SOLDIER_COST_WOOD;
            int stoneCost = (unitType == SimUnitType.Worker) ? SimConfig.WORKER_COST_STONE : SimConfig.SOLDIER_COST_STONE;

            // Kaynak Harca
            if (SimResourceSystem.SpendResources(world, building.PlayerID, woodCost, stoneCost, meatCost))
            {
                building.IsTraining = true;
                building.UnitInProduction = unitType;
                building.TrainingTimer = 0f;

                if (SimConfig.EnableLogs)
                    Debug.Log($"✅ ÜRETİM BAŞLADI: {unitType} @ {building.GridPosition} (Player {building.PlayerID})");
            }
            else
            {
                if (SimConfig.EnableLogs)
                    Debug.LogWarning($"💸 Yetersiz Kaynak ({unitType}): Odun:{woodCost} Taş:{stoneCost} Et:{meatCost}");
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

            // En yakın düşmanı bul
            SimUnitData target = FindNearestEnemy(world, building.GridPosition, building.AttackRange, building.PlayerID);

            if (target != null)
            {
                // Hasar Ver
                target.Health -= building.Damage;
                building.AttackTimer = 0f;

                // Görselleştirici için hedefi kaydet (Lazer/Ok çizmek istersen)
                building.TargetUnitID = target.ID;

                // Öldü mü?
                if (target.Health <= 0)
                {
                    target.State = SimTaskType.Dead;
                    world.Units.Remove(target.ID);
                    world.Map.Grid[target.GridPosition.x, target.GridPosition.y].OccupantID = -1;
                }
            }
            else
            {
                building.TargetUnitID = -1; // Kimseye sıkmıyor
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