using RTS.Simulation.Data;
using RTS.Simulation.Core;
using UnityEngine;
using System.Linq;
using System; // Action için gerekli

namespace RTS.Simulation.Systems
{
    public class SimBuildingSystem
    {
        public static event Action<SimBuildingData> OnBuildingFinished;
        public static event Action<SimUnitData> OnUnitCreated;
        private SimWorldState _world;

        public SimBuildingSystem(SimWorldState world = null)
        {
            _world = world ?? SimGameContext.ActiveWorld;
        }

        // --- INSTANCE WRAPPERS ---
        public void UpdateAllBuildings(float dt) => UpdateAllBuildings(_world, dt);
        public void SpawnUnit(int2 basePos, SimUnitType type, int playerID) => SpawnUnit(_world, basePos, type, playerID);

        // YENİ: Factory Method - Binayı oluşturur, ayarlar ve dünyaya ekler.
        public SimBuildingData CreateBuilding(int playerID, SimBuildingType type, int2 position)
        {
            return CreateBuilding(_world, playerID, type, position);
        }
        // -------------------------

        // --- STATİK VE MANTIK FONKSİYONLARI ---

        /// <summary>
        /// YENİ: Güvenli Bina Oluşturma Fonksiyonu (Factory Pattern)
        /// Bu fonksiyon InitializeBuildingStats'ı otomatik çağırır.
        /// </summary>
        public static SimBuildingData CreateBuilding(SimWorldState world, int playerID, SimBuildingType type, int2 position)
        {
            var building = new SimBuildingData
            {
                ID = world.NextID(),
                PlayerID = playerID,
                Type = type,
                GridPosition = position,
                IsConstructed = false,
                ConstructionProgress = 0f,
                Health = 10, // Başlangıç inşaat canı
                MaxHealth = 100 // Varsayılan
            };

            // KRİTİK ADIM: İstatistikleri ve ResourceGenerator özelliklerini yükle
            InitializeBuildingStats(building);

            // Base gibi özel binaların canını override et
            if (type == SimBuildingType.Base) building.MaxHealth = SimConfig.BASE_MAX_HEALTH;

            // Dünyaya ekle
            world.Buildings.Add(building.ID, building);

            // Haritada yerini işaretle
            if (world.Map.IsInBounds(position))
            {
                var node = world.Map.Grid[position.x, position.y];
                node.OccupantID = building.ID;
                node.IsWalkable = false;
            }

            return building;
        }

        public static void UpdateAllBuildings(SimWorldState world, float dt)
        {
            // Koleksiyon değişimi hatasını önlemek için ToList() veya Keys kopyası alınabilir
            // Ancak basit döngülerde şimdilik foreach yeterli
            foreach (var building in world.Buildings.Values)
            {
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

            // Canı ilerlemeye göre artır
            float healthPct = building.ConstructionProgress / SimConfig.BUILDING_MAX_PROGRESS;
            int hpAdd = Mathf.CeilToInt(building.MaxHealth * healthPct);
            // Mevcut canı güncelle (Min-Max clamp ile)
            building.Health = Mathf.Clamp(hpAdd, 10, building.MaxHealth);

            if (building.ConstructionProgress >= SimConfig.BUILDING_MAX_PROGRESS)
            {
                building.ConstructionProgress = SimConfig.BUILDING_MAX_PROGRESS;
                building.Health = building.MaxHealth;
                building.IsConstructed = true;

                OnBuildingCompleted(building, world);

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
            OnBuildingFinished?.Invoke(building);
        }

        public static void InitializeBuildingStats(SimBuildingData b, bool isMax = false)
        {
            b.MaxHealth = 1000; // Varsayılan yüksek değer
            b.Health = (isMax) ? b.MaxHealth : 10;
            b.IsConstructed = (isMax) ? true : false;
            b.ConstructionProgress = (isMax) ? SimConfig.BUILDING_MAX_PROGRESS : 0f;

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
                case SimBuildingType.Wall:
                    b.MaxHealth = SimConfig.WALL_MAX_HEALTH;
                    break;
            }
        }

        private static void ConfigureGenerator(SimBuildingData b, SimResourceType type)
        {
            b.IsResourceGenerator = true;
            b.ResourceType = type;
            b.ResourceInterval = SimConfig.RESOURCE_GENERATION_INTERVAL;
            b.ResourceAmountPerCycle = SimConfig.RESOURCE_GENERATION_AMOUNT;
            // Debug.Log($"⚙️ Configured Generator: {b.Type} -> {type}"); // Test için açılabilir
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
                bool success = SpawnUnit(world, building.GridPosition, building.UnitInProduction, building.PlayerID);
                // SpawnUnit(world, building.GridPosition, building.UnitInProduction, building.PlayerID);
                if (success)
                {
                    // Ancak başarılı olursa üretimi bitir ve sayacı sıfırla
                    building.TrainingTimer = 0f;
                    building.IsTraining = false;
                }
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

                // Kaynak üretimi logu (Çok sık çıkarsa kapatılabilir)
                // if(SimConfig.EnableLogs) Debug.Log($"💰 {building.ResourceType} (+{building.ResourceAmountPerCycle})");
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

                    // EKLENEN SATIR: Kule adam öldürünce nüfusu düşür
                    SimResourceSystem.ModifyPopulation(world, target.PlayerID, -1);
                }
            }
            else building.TargetUnitID = -1;
        }

        public static bool SpawnUnit(SimWorldState world, int2 basePos, SimUnitType type, int playerID)
        {
            if (!SimResourceSystem.HasPopulationSpace(world, playerID)) return false;

            // ESKİ YÖNTEM: Sadece bitişiğe bakıyordu.
            // int2? spawnPos = SimGridSystem.FindWalkableNeighbor(world, basePos);

            // YENİ YÖNTEM: Spiral Arama (4 birim yarıçapa kadar boş yer arar)
            int2? spawnPos = FindFreeSpawnSpot(world, basePos, 4);

            if (spawnPos == null)
            {
                // Etraf tamamen kapalı, doğacak yer yok!
                return false;
            }

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

            // Grid'i güncelle
            world.Map.Grid[spawnPos.Value.x, spawnPos.Value.y].OccupantID = newUnit.ID;

            SimResourceSystem.ModifyPopulation(world, playerID, 1);
            OnUnitCreated?.Invoke(newUnit);

            return true;
        }

        // --- YENİ EKLENEN YARDIMCI FONKSİYON ---
        private static int2? FindFreeSpawnSpot(SimWorldState world, int2 center, int maxRadius)
        {
            // 1. Önce hızlıca bitişiklere bak (Eski yöntem, performans için)
            int2? simpleCheck = SimGridSystem.FindWalkableNeighbor(world, center);
            if (simpleCheck.HasValue) return simpleCheck;

            // 2. Eğer oralar doluysa dışa doğru genişleyerek (Spiral) ara
            for (int r = 2; r <= maxRadius; r++)
            {
                // Kare şeklinde genişleyen çerçeve
                for (int x = -r; x <= r; x++)
                {
                    for (int y = -r; y <= r; y++)
                    {
                        // Sadece çerçevenin kenarlarına bak (içini zaten önceki döngüde baktık)
                        if (Mathf.Abs(x) != r && Mathf.Abs(y) != r) continue;

                        int2 checkPos = center + new int2(x, y);

                        if (world.Map.IsInBounds(checkPos))
                        {
                            var node = world.Map.Grid[checkPos.x, checkPos.y];
                            // Yürünebilir mi ve üzerinde kimse yok mu?
                            if (node.IsWalkable && node.OccupantID == -1)
                            {
                                return checkPos;
                            }
                        }
                    }
                }
            }
            return null; // Hiçbir yerde boşluk yok
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