using System;
using System.Collections.Generic;
using System.Linq;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using UnityEngine;

// Global Enum
public enum AIStrategyMode { Economic, Defensive, Aggressive }

namespace RTS.Simulation.AI
{
    public class SpecializedMacroAI
    {
        private SimWorldState _world;
        private int _playerID;
        private float[] _genes;
        private AIStrategyMode _currentMode; // Statik modda hangi kimlikte?
        private float _timer;
        private System.Random _rng;

        // GSF Değişkenleri (Analiz İçin)
        public float MAP, EAP, MDP, EDP, GSF;

        public SpecializedMacroAI(SimWorldState world, int playerID, float[] genes, AIStrategyMode mode, System.Random rng = null)
        {
            _world = world;
            _playerID = playerID;
            _genes = genes; // Eğer null gelirse Statik mod çalışır
            _currentMode = mode;
            _rng = rng ?? new System.Random();
        }

        public void Update(float dt)
        {
            _timer += dt;
            if (_timer < 0.25f) return;
            _timer = 0;

            UpdateStrategicMetrics();

            // --- KARAR ANI ---
            if (_genes == null)
            {
                // Gen yoksa: Statik, kural tabanlı (Rakip) davranış
                ExecuteStaticBehavior();
            }
            else
            {
                // Gen varsa: PSO tarafından eğitilen parametrik davranış
                ExecuteParametricBehavior();
            }
        }

        // --- GSF HESAPLAMA MOTORU ---
        public void UpdateStrategicMetrics()
        {
            var myUnits = _world.Units.Values.Where(u => u.PlayerID == _playerID).ToList();
            var enemyUnits = _world.Units.Values.Where(u => u.PlayerID != _playerID).ToList();
            var myBuildings = _world.Buildings.Values.Where(b => b.PlayerID == _playerID).ToList();
            var enemyBuildings = _world.Buildings.Values.Where(b => b.PlayerID != _playerID).ToList();

            // MAP (My Attack Power): Asker sayısı
            MAP = myUnits.Count(u => u.UnitType == SimUnitType.Soldier) * 10f;

            // EAP (Enemy Attack Power): Rakip asker sayısı
            EAP = enemyUnits.Count(u => u.UnitType == SimUnitType.Soldier) * 10f;

            // MDP (My Defence Power): Kuleler ve Base Sağlığı
            float myBaseHealth = myBuildings.FirstOrDefault(b => b.Type == SimBuildingType.Base)?.Health ?? 0;
            MDP = (myBuildings.Count(b => b.Type == SimBuildingType.Tower && b.IsConstructed) * 50f) + (myBaseHealth * 0.1f);

            // EDP (Enemy Defence Power)
            float enemyBaseHealth = enemyBuildings.FirstOrDefault(b => b.Type == SimBuildingType.Base)?.Health ?? 0;
            EDP = (enemyBuildings.Count(b => b.Type == SimBuildingType.Tower && b.IsConstructed) * 50f) + (enemyBaseHealth * 0.1f);

            // GSF Skoru: Pozitifse biz üstünüz, negatifse rakip üstün
            GSF = (MAP + MDP) - (EAP + EDP);
        }

        // ==================================================================================
        // 1. BÖLÜM: STATİK DAVRANIŞLAR (RAKİPLER)
        // ==================================================================================
        private void ExecuteStaticBehavior()
        {
            var myUnits = _world.Units.Values.Where(u => u.PlayerID == _playerID).ToList();
            var myBuildings = _world.Buildings.Values.Where(b => b.PlayerID == _playerID).ToList();
            var baseB = myBuildings.FirstOrDefault(b => b.Type == SimBuildingType.Base);
            var pData = SimResourceSystem.GetPlayer(_world, _playerID);

            // Ortak: İşçileri yönet
            ManageWorkersDefault(myUnits, pData);

            switch (_currentMode)
            {
                case AIStrategyMode.Economic:
                    // SADECE GELİŞİM: Asla asker basma, kule dikme. Sadece işçi ve ev.
                    int workerCount = myUnits.Count(u => u.UnitType == SimUnitType.Worker);

                    // İşçi Bas (Limit 50)
                    if (baseB != null && !baseB.IsTraining && workerCount < 50)
                    {
                        if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT))
                            SimBuildingSystem.StartTraining(baseB, _world, SimUnitType.Worker);
                    }

                    // Nüfus tıkanırsa ev yap
                    if (pData.MaxPopulation - pData.CurrentPopulation <= 2)
                    {
                        TryBuildBuilding(SimBuildingType.House, myUnits, baseB, SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT);
                    }
                    break;

                case AIStrategyMode.Defensive:
                    // KULE SPAM: Base etrafını kuleyle doldur.
                    int towerCount = myBuildings.Count(b => b.Type == SimBuildingType.Tower);

                    // İşçi Bas (Limit 15 - Defans için yeterli)
                    if (baseB != null && !baseB.IsTraining && myUnits.Count(u => u.UnitType == SimUnitType.Worker) < 15)
                    {
                        if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT))
                            SimBuildingSystem.StartTraining(baseB, _world, SimUnitType.Worker);
                    }

                    // Sürekli Kule Dik (Limit 15 Kule)
                    if (towerCount < 15)
                    {
                        TryBuildBuilding(SimBuildingType.Tower, myUnits, baseB, SimConfig.TOWER_COST_WOOD, SimConfig.TOWER_COST_STONE, SimConfig.TOWER_COST_MEAT);
                    }
                    break;

                case AIStrategyMode.Aggressive:
                    // RUSH: Sürekli asker bas ve saldır.
                    int soldiers = myUnits.Count(u => u.UnitType == SimUnitType.Soldier);
                    var enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != _playerID && b.Type == SimBuildingType.Base);

                    // İşçi Bas (Limit 10 - Hızlı kaynak için min)
                    if (baseB != null && !baseB.IsTraining && myUnits.Count(u => u.UnitType == SimUnitType.Worker) < 10)
                    {
                        if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT))
                            SimBuildingSystem.StartTraining(baseB, _world, SimUnitType.Worker);
                    }

                    // Kışla Yoksa Yap
                    if (!myBuildings.Any(b => b.Type == SimBuildingType.Barracks))
                    {
                        TryBuildBuilding(SimBuildingType.Barracks, myUnits, baseB, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT);
                    }

                    // Asker Bas
                    foreach (var b in myBuildings.Where(x => x.Type == SimBuildingType.Barracks && x.IsConstructed && !x.IsTraining))
                    {
                        if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT))
                            SimBuildingSystem.StartTraining(b, _world, SimUnitType.Soldier);
                    }

                    // 5 Asker olunca saldır
                    if (soldiers >= 5 && enemyBase != null)
                    {
                        foreach (var s in myUnits.Where(u => u.UnitType == SimUnitType.Soldier && u.State == SimTaskType.Idle))
                            SimUnitSystem.OrderAttack(s, enemyBase, _world);
                    }
                    break;
            }
        }

        // ==================================================================================
        // 2. BÖLÜM: PARAMETRİK DAVRANIŞ (EĞİTİLEN AJAN)
        // ==================================================================================
        private void ExecuteParametricBehavior()
        {
            var myUnits = _world.Units.Values.Where(u => u.PlayerID == _playerID).ToList();
            var myBuildings = _world.Buildings.Values.Where(b => b.PlayerID == _playerID).ToList();
            var pData = SimResourceSystem.GetPlayer(_world, _playerID);

            int workers = myUnits.Count(u => u.UnitType == SimUnitType.Worker);
            int soldiers = myUnits.Count(u => u.UnitType == SimUnitType.Soldier);
            int barracksCount = myBuildings.Count(b => b.Type == SimBuildingType.Barracks);
            int towerCount = myBuildings.Count(b => b.Type == SimBuildingType.Tower);
            var baseB = myBuildings.FirstOrDefault(b => b.Type == SimBuildingType.Base);
            var enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != _playerID && b.Type == SimBuildingType.Base);

            // --- GEN OKUMA VE YORUMLAMA (DÜZELTİLDİ) ---
            // Not: Genler 0-40 arasında değer alıyor (PSOManager ayarına göre).

            // İşçi Hedefi: Doğrudan geni kullan (Min 5, Max 60)
            int targetWorker = SimMath.Clamp(SimMath.RoundToInt(_genes[0] * 1.5f), 5, 80);

            int targetSoldier = SimMath.Clamp(SimMath.RoundToInt(_genes[1]), 5, 80);
            int attackThreshold = SimMath.Clamp(SimMath.RoundToInt(_genes[2]), 5, 60);
            float defenseRatio = SimMath.Clamp01(_genes[3] / 20f);
            int targetBarracks = SimMath.Clamp(SimMath.RoundToInt(_genes[4] / 10f), 0, 5); // Bölücüyü artırdık, gereksiz kışlayı engellemek için
            float ecoBias = SimMath.Clamp01(_genes[5] / 40f);

            // EKONOMİ BİNALARI (LİMİTLER AÇILDI)
            // Eskiden / 5f vardı, şimdi doğrudan geni alıyoruz. Gen 40 ise hedef 40 tarla!
            int targetFarm = SimMath.Clamp(SimMath.RoundToInt(_genes[6]), 0, 50);
            int targetWood = SimMath.Clamp(SimMath.RoundToInt(_genes[7]), 0, 50);
            int targetStone = SimMath.Clamp(SimMath.RoundToInt(_genes[8]), 0, 50);
            int houseBuffer = SimMath.Clamp(SimMath.RoundToInt(_genes[9] / 4f), 1, 10);

            // -----------------------------------------------------------------------
            // 1. ÖNCELİK: İŞÇİ YÖNETİMİ VE ÜRETİMİ (ÖNCE İŞÇİ BAS!)
            // -----------------------------------------------------------------------
            ManageWorkersParametric(myUnits, ecoBias, pData);

            int freePop = pData.MaxPopulation - pData.CurrentPopulation;

            // Kritik: Eğer nüfus yerin varsa ve hedefin altındaysan ÖNCE işçi bas.
            // Parayı inşaata harcamadan önce işçiye ayır.
            if (baseB != null && !baseB.IsTraining && workers < targetWorker && freePop > 0)
            {
                if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT))
                {
                    SimBuildingSystem.StartTraining(baseB, _world, SimUnitType.Worker);
                    // İşçi bastıysan bu tick'te parayı bitirmiş olabilirsin, aşağıdakiler başarısız olabilir ama sorun değil.
                }
            }

            // -----------------------------------------------------------------------
            // 2. ÖNCELİK: İNŞAAT KARARLARI
            // -----------------------------------------------------------------------

            // A. Ev Yapımı (Nüfus tıkalıysa en acil durum)
            if (freePop <= houseBuffer)
            {
                TryBuildBuilding(SimBuildingType.House, myUnits, baseB, SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT);
            }

            // B. Ekonomi Binaları (Dengeli Dağılım Algoritması)
            int farmCount = myBuildings.Count(b => b.Type == SimBuildingType.Farm);
            int woodCount = myBuildings.Count(b => b.Type == SimBuildingType.WoodCutter);
            int stoneCount = myBuildings.Count(b => b.Type == SimBuildingType.StonePit);

            // Hangi binaya en çok ihtiyaç var? (Hedefine ulaşmamış ve sayısı en az olan)
            // Basit bir öncelik listesi oluşturuyoruz:
            var ecoQueue = new List<(SimBuildingType type, int count, int target, int w, int s, int m)>();

            ecoQueue.Add((SimBuildingType.Farm, farmCount, targetFarm, SimConfig.FARM_COST_WOOD, SimConfig.FARM_COST_STONE, SimConfig.FARM_COST_MEAT));
            ecoQueue.Add((SimBuildingType.WoodCutter, woodCount, targetWood, SimConfig.WOODCUTTER_COST_WOOD, SimConfig.WOODCUTTER_COST_STONE, SimConfig.WOODCUTTER_COST_MEAT));
            ecoQueue.Add((SimBuildingType.StonePit, stoneCount, targetStone, SimConfig.STONEPIT_COST_WOOD, SimConfig.STONEPIT_COST_STONE, SimConfig.STONEPIT_COST_MEAT));

            // 1. Hedefine ulaşmışları ele.
            // 2. Mevcut sayıya göre Küçükten Büyüğe sırala (Az olan öne gelir).
            var priorityBuild = ecoQueue
                .Where(x => x.count < x.target)
                .OrderBy(x => x.count)
                .FirstOrDefault();

            // Eğer yapılacak bir bina varsa, inşa etmeyi dene
            if (priorityBuild.type != SimBuildingType.None) // None değilse geçerli bir bina seçilmiştir
            {
                // Türüne göre switch-case yerine doğrudan parametreleri kullanıyoruz (Tuple sayesinde)
                // Ancak TryBuildBuilding enum aldığı için çağırmamız yeterli.
                TryBuildBuilding(priorityBuild.type, myUnits, baseB, priorityBuild.w, priorityBuild.s, priorityBuild.m);
            }

            // C. Kışla Yapımı
            if (barracksCount < targetBarracks)
            {
                TryBuildBuilding(SimBuildingType.Barracks, myUnits, baseB, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT);
            }

            // Kule Yapımı
            int neededTowers = SimMath.FloorToInt(soldiers * defenseRatio);
            if (towerCount < neededTowers && towerCount < 20)
            {
                TryBuildBuilding(SimBuildingType.Tower, myUnits, baseB, SimConfig.TOWER_COST_WOOD, SimConfig.TOWER_COST_STONE, SimConfig.TOWER_COST_MEAT);
            }

            // -----------------------------------------------------------------------
            // 3. ÖNCELİK: ASKERİ AKSİYONLAR
            // -----------------------------------------------------------------------

            // Asker Bas (İşçiden ve binadan para arttıysa)
            if (soldiers < targetSoldier && freePop > 0)
            {
                foreach (var b in myBuildings.Where(x => x.Type == SimBuildingType.Barracks && x.IsConstructed && !x.IsTraining))
                {
                    if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT))
                        SimBuildingSystem.StartTraining(b, _world, SimUnitType.Soldier);
                }
            }

            // Saldırı Kararı
            if (soldiers >= attackThreshold && enemyBase != null)
            {
                foreach (var s in myUnits.Where(u => u.UnitType == SimUnitType.Soldier && u.State == SimTaskType.Idle))
                    SimUnitSystem.OrderAttack(s, enemyBase, _world);
            }
        }
        // ==================================================================================
        // YARDIMCI FONKSİYONLAR (HEM STATİK HEM PARAMETRİK İÇİN ORTAK)
        // ==================================================================================

        private SimUnitData GetAvailableWorker(List<SimUnitData> units)
        {
            var w = units.FirstOrDefault(u => u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle);
            if (w != null) return w;
            // Boşta yoksa kaynak toplayanı al
            return units.FirstOrDefault(u => u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Gathering);
        }

        private bool TryBuildBuilding(SimBuildingType type, List<SimUnitData> units, SimBuildingData near, int costWood, int costStone, int costMeat)
        {
            if (!SimResourceSystem.CanAfford(_world, _playerID, costWood, costStone, costMeat)) return false;

            var worker = GetAvailableWorker(units);
            if (worker == null) return false;

            int2 searchCenter = (near != null) ? near.GridPosition : worker.GridPosition;

            int minRadius;
            int maxRadius;

            // --- DÜZELTME BURADA ---
            if (type == SimBuildingType.Tower)
            {
                // EĞER KULE İSE: Genişleme mantığını yoksay, en yakına (dibine) dik!
                minRadius = 2; // 2 birim mesafe (Hemen bitişiği)
                maxRadius = 8; // Çok uzağa gitme, bulamazsan dikme
            }
            else
            {
                // DİĞER BİNALAR: Şehir büyüdükçe dışa doğru genişlemeye devam etsin.
                int buildingCount = _world.Buildings.Values.Count(b => b.PlayerID == _playerID);
                minRadius = 3 + (buildingCount / 5) * 2;
                maxRadius = minRadius + 10;
            }

            int2 pos = FindBuildSpot(searchCenter, minRadius, maxRadius);

            if (pos.x != -1)
            {
                SimResourceSystem.SpendResources(_world, _playerID, costWood, costStone, costMeat);
                var b = SpawnPlaceholder(type, pos);
                SimUnitSystem.OrderBuild(worker, b, _world);
                return true;
            }
            return false;
        }
        private int2 FindBuildSpot(int2 center, int minRadius, int maxRadius)
        {
            for (int r = minRadius; r <= maxRadius; r++)
            {
                for (int x = -r; x <= r; x++)
                {
                    for (int y = -r; y <= r; y++)
                    {
                        if (System.Math.Abs(x) == r || System.Math.Abs(y) == r)
                        {
                            int2 pos = new int2(center.x + x, center.y + y);
                            // Harita sınırları ve yürünebilirlik kontrolü
                            if (pos.x > 1 && pos.x < SimConfig.MAP_WIDTH - 1 && pos.y > 1 && pos.y < SimConfig.MAP_HEIGHT - 1)
                            {
                                if (SimGridSystem.IsWalkable(_world, pos)) return pos;
                            }
                        }
                    }
                }
            }
            return new int2(-1, -1);
        }

        private SimBuildingData SpawnPlaceholder(SimBuildingType type, int2 pos)
        {
            var b = new SimBuildingData
            {
                ID = _world.NextID(),
                PlayerID = _playerID,
                Type = type,
                GridPosition = pos,
                IsConstructed = false,
                ConstructionProgress = 0f
            };
            SimBuildingSystem.InitializeBuildingStats(b);
            _world.Buildings.Add(b.ID, b);
            _world.Map.Grid[pos.x, pos.y].IsWalkable = false;
            _world.Map.Grid[pos.x, pos.y].OccupantID = b.ID;
            return b;
        }

        private void ManageWorkersDefault(List<SimUnitData> units, SimPlayerData pData)
        {
            // Statik basit kaynak toplama mantığı
            var idleWorkers = units.Where(u => u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle).ToList();
            if (idleWorkers.Count == 0) return;

            foreach (var w in idleWorkers)
            {
                SimResourceType targetType = SimResourceType.Wood;
                // Basit mantık: Odun azsa odun, et azsa et
                if (pData.Wood < 100) targetType = SimResourceType.Wood;
                else if (pData.Meat < 100) targetType = SimResourceType.Meat;
                else if (pData.Stone < 50) targetType = SimResourceType.Stone;
                else
                {
                    // Rastgele dağıt
                    double r = _rng.NextDouble();
                    if (r < 0.5) targetType = SimResourceType.Wood;
                    else if (r < 0.8) targetType = SimResourceType.Meat;
                    else targetType = SimResourceType.Stone;
                }

                var res = FindNearestResource(w.GridPosition, targetType);
                if (res == null) res = FindNearestResource(w.GridPosition, SimResourceType.None); // Ne bulursan topla
                if (res != null) SimUnitSystem.TryAssignGatherTask(w, res, _world);
            }
        }

        private void ManageWorkersParametric(List<SimUnitData> units, float ecoBias, SimPlayerData pData)
        {
            // Genetik bias'a göre kaynak toplama
            var idleWorkers = units.Where(u => u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle).ToList();
            foreach (var w in idleWorkers)
            {
                SimResourceType targetType;
                if (pData.Wood < SimConfig.HOUSE_COST_WOOD) targetType = SimResourceType.Wood;
                else if (pData.Meat < SimConfig.WORKER_COST_MEAT) targetType = SimResourceType.Meat;
                else
                {
                    double rng = _rng.NextDouble();
                    // ecoBias arttıkça odun/taşa daha az, ete (asker için) daha çok yönelebilir veya tam tersi stratejiye göre
                    if (rng < 0.4 - (ecoBias * 0.2)) targetType = SimResourceType.Wood;
                    else if (rng < 0.7) targetType = SimResourceType.Meat;
                    else targetType = SimResourceType.Stone;
                }

                var res = FindNearestResource(w.GridPosition, targetType);
                if (res == null) res = FindNearestResource(w.GridPosition, SimResourceType.None);
                if (res != null) SimUnitSystem.TryAssignGatherTask(w, res, _world);
            }
        }

        private SimResourceData FindNearestResource(int2 pos, SimResourceType type)
        {
            SimResourceData best = null;
            float minDst = float.MaxValue;
            foreach (var r in _world.Resources.Values)
            {
                if (type != SimResourceType.None && r.Type != type) continue;
                if (r.AmountRemaining <= 0) continue;

                float d = SimGridSystem.GetDistanceSq(pos, r.GridPosition);
                if (d < minDst) { minDst = d; best = r; }
            }
            return best;
        }
    }
}