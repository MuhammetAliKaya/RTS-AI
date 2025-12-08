using RTS.Simulation.Data;
using RTS.Simulation.Core;
using UnityEngine;
using System.Linq;

namespace RTS.Simulation.Systems
{
    public class SimBuildingSystem
    {
        private SimWorldState _world;

        public SimBuildingSystem(SimWorldState world = null)
        {
            _world = world ?? SimGameContext.ActiveWorld;
        }

        public void UpdateAllBuildings(float dt) => UpdateAllBuildings(_world, dt);
        public void SpawnUnit(int2 basePos, SimUnitType type, int playerID) => SpawnUnit(_world, basePos, type, playerID);

        public static void UpdateAllBuildings(SimWorldState world, float dt)
        {
            foreach (var building in world.Buildings.Values)
            {
                // Sadece tamamlanmış binalar çalışır
                if (!building.IsConstructed) continue;

                UpdateProduction(building, world, dt);
                UpdateResourceGeneration(building, world, dt);
                UpdateTowerCombat(building, world, dt);
            }
        }

        public static bool AdvanceConstruction(SimBuildingData building, SimWorldState world, float amount)
        {
            if (building.IsConstructed) return true;

            building.ConstructionProgress += amount;

            // --- GÖRSEL DÜZELTME: Canı ilerlemeye göre artır ---
            float healthPct = amount / SimConfig.BUILDING_MAX_PROGRESS;
            int hpAdd = Mathf.CeilToInt(building.MaxHealth * healthPct);
            building.Health = Mathf.Min(building.Health + hpAdd, building.MaxHealth);
            // --------------------------------------------------

            if (building.ConstructionProgress >= SimConfig.BUILDING_MAX_PROGRESS)
            {
                building.ConstructionProgress = SimConfig.BUILDING_MAX_PROGRESS;

                // Tamamlandığında canı ve bayrağı kesinleştir
                building.Health = building.MaxHealth;
                building.IsConstructed = true;

                OnBuildingCompleted(building, world);

                // Log ile teyit et
                if (SimConfig.EnableLogs) Debug.Log($"🏗️ BİNA TAMAMLANDI: {building.Type} (ID: {building.ID})");

                return true;
            }
            return false;
        }

        public static void OnBuildingCompleted(SimBuildingData building, SimWorldState world)
        {
            if (building.Type == SimBuildingType.Base)
                SimResourceSystem.IncreaseMaxPopulation(world, building.PlayerID, SimConfig.POPULATION_BASE);
            else if (building.Type == SimBuildingType.House)
                SimResourceSystem.IncreaseMaxPopulation(world, building.PlayerID, SimConfig.POPULATION_HOUSE);
        }

        public static void InitializeBuildingStats(SimBuildingData b)
        {
            b.MaxHealth = 1000;
            b.Health = 10; // Başlangıç canı düşük (İnşaat ilerledikçe artacak)
            b.ConstructionProgress = b.IsConstructed ? SimConfig.BUILDING_MAX_PROGRESS : 0f;

            // Kaynak Üreticisi Ayarları
            switch (b.Type)
            {
                case SimBuildingType.Farm:
                    ConfigureGenerator(b, SimResourceType.Meat);
                    break;
                case SimBuildingType.WoodCutter:
                    ConfigureGenerator(b, SimResourceType.Wood);
                    break;
                case SimBuildingType.StonePit:
                    ConfigureGenerator(b, SimResourceType.Stone);
                    break;
                case SimBuildingType.Tower:
                    b.Damage = SimConfig.TOWER_DAMAGE;
                    b.AttackRange = SimConfig.TOWER_ATTACK_RANGE;
                    b.AttackSpeed = SimConfig.TOWER_ATTACK_SPEED;
                    break;
            }
        }

        // Yardımcı Fonksiyon: Kod tekrarını önler
        private static void ConfigureGenerator(SimBuildingData b, SimResourceType type)
        {
            b.IsResourceGenerator = true;
            b.ResourceType = type;
            b.ResourceInterval = SimConfig.RESOURCE_GENERATION_INTERVAL;
            b.ResourceAmountPerCycle = SimConfig.RESOURCE_GENERATION_AMOUNT;
        }

        public static void StartTraining(SimBuildingData building, SimWorldState world, SimUnitType unitType)
        {
            if (building.IsTraining) return;

            int meat = (unitType == SimUnitType.Worker) ? SimConfig.WORKER_COST_MEAT : SimConfig.SOLDIER_COST_MEAT;
            int wood = (unitType == SimUnitType.Worker) ? SimConfig.WORKER_COST_WOOD : SimConfig.SOLDIER_COST_WOOD;
            int stone = (unitType == SimUnitType.Worker) ? SimConfig.WORKER_COST_STONE : SimConfig.SOLDIER_COST_STONE;

            if (SimResourceSystem.SpendResources(world, building.PlayerID, wood, stone, meat))
            {
                building.IsTraining = true;
                building.UnitInProduction = unitType;
                building.TrainingTimer = 0f;
            }
        }

        private static void UpdateProduction(SimBuildingData building, SimWorldState world, float dt)
        {
            if (!building.IsTraining) return;
            building.TrainingTimer += dt;
            float requiredTime = (building.UnitInProduction == SimUnitType.Worker) ? SimConfig.WORKER_TRAIN_TIME : SimConfig.SOLDIER_TRAIN_TIME;

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

                // Debug Log(Sadece test için açılabilir)
                Debug.Log($"💰 {building.ResourceType} Üretildi! (+{building.ResourceAmountPerCycle})");
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
                building.TargetUnitID = target.ID;
                if (target.Health <= 0)
                {
                    target.State = SimTaskType.Dead;
                    world.Units.Remove(target.ID);
                    world.Map.Grid[target.GridPosition.x, target.GridPosition.y].OccupantID = -1;
                }
            }
            else building.TargetUnitID = -1;
        }

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