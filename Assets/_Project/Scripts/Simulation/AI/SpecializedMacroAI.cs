using System;
using System.Collections.Generic;
using System.Linq;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using UnityEngine;


public enum AIStrategyMode { Economic, Defensive, Aggressive, General }

namespace RTS.Simulation.AI
{
    public class SpecializedMacroAI
    {
        private SimWorldState _world;
        private int _playerID;
        private float[] _genes;
        private AIStrategyMode _currentMode;
        private float _timer;
        private System.Random _rng;

        // GSF Değişkenleri (Analiz İçin)
        public float MAP, EAP, MDP, EDP, GSF;

        // --- GEN HARİTASI (14 GEN) ---
        // [0] Target Worker
        // [1] Target Soldier
        // [2] Attack Threshold
        // [3] Defense Ratio
        // [4] Target Barracks
        // [5] Eco Bias
        // [6] Target Farm
        // [7] Target WoodCutter
        // [8] Target StonePit
        // [9] House Buffer
        // [10] Tower Pos Bias
        // --- ÖNCELİK GENLERİ ---
        // [11] Priority: Economy
        // [12] Priority: Defense
        // [13] Priority: Military

        public SpecializedMacroAI(SimWorldState world, int playerID, float[] genes, AIStrategyMode mode, System.Random rng = null)
        {
            _world = world;
            _playerID = playerID;
            _genes = genes;
            _currentMode = mode;
            _rng = rng ?? new System.Random();
        }

        public void Update(float dt)
        {
            _timer += dt;
            if (_timer < 0.25f) return;
            _timer = 0;

            UpdateStrategicMetrics();

            // Gen yoksa Enemy (Statik), varsa Bizimki (Parametrik)
            if (_genes == null) ExecuteStaticBehavior();
            else ExecuteParametricBehavior();
        }

        // --- GSF HESAPLAMA MOTORU ---
        public void UpdateStrategicMetrics()
        {
            var myUnits = _world.Units.Values.Where(u => u.PlayerID == _playerID).ToList();
            var enemyUnits = _world.Units.Values.Where(u => u.PlayerID != _playerID).ToList();
            var myBuildings = _world.Buildings.Values.Where(b => b.PlayerID == _playerID).ToList();
            var enemyBuildings = _world.Buildings.Values.Where(b => b.PlayerID != _playerID).ToList();

            MAP = myUnits.Count(u => u.UnitType == SimUnitType.Soldier) * 10f;
            EAP = enemyUnits.Count(u => u.UnitType == SimUnitType.Soldier) * 10f;

            float myBaseHealth = myBuildings.FirstOrDefault(b => b.Type == SimBuildingType.Base)?.Health ?? 0;
            MDP = (myBuildings.Count(b => b.Type == SimBuildingType.Tower && b.IsConstructed) * 50f) + (myBaseHealth * 0.1f);

            float enemyBaseHealth = enemyBuildings.FirstOrDefault(b => b.Type == SimBuildingType.Base)?.Health ?? 0;
            EDP = (enemyBuildings.Count(b => b.Type == SimBuildingType.Tower && b.IsConstructed) * 50f) + (enemyBaseHealth * 0.1f);

            GSF = (MAP + MDP) - (EAP + EDP);
        }

        // ==================================================================================
        // 1. STATİK DAVRANIŞ (ENEMY AI - TÜM MODLAR DAHİL)
        // ==================================================================================
        private void ExecuteStaticBehavior()
        {
            var myUnits = _world.Units.Values.Where(u => u.PlayerID == _playerID).ToList();
            var myBuildings = _world.Buildings.Values.Where(b => b.PlayerID == _playerID).ToList();
            var baseB = myBuildings.FirstOrDefault(b => b.Type == SimBuildingType.Base);
            var pData = SimResourceSystem.GetPlayer(_world, _playerID);
            var enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != _playerID && b.Type == SimBuildingType.Base);

            // Güvenli merkez noktası (Base yoksa harita ortası)
            int2 basePos = (baseB != null) ? baseB.GridPosition : new int2(SimConfig.MAP_WIDTH / 2, SimConfig.MAP_HEIGHT / 2);

            // --- KAYNAK YÖNETİMİ ---
            // Agresif moddaysa ve kışlası yoksa özel kaynak toplama mantığı çalışır.
            if (_currentMode == AIStrategyMode.Aggressive)
                ManageWorkersAggressive(myUnits, pData, myBuildings);
            else
                ManageWorkersDefault(myUnits, pData);

            switch (_currentMode)
            {
                case AIStrategyMode.Economic:
                    // --- EKONOMİ MODU ---
                    int workerCountEco = myUnits.Count(u => u.UnitType == SimUnitType.Worker);

                    if (pData.MaxPopulation - pData.CurrentPopulation <= 2)
                        TryBuildBuilding(SimBuildingType.House, myUnits, basePos, SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT);

                    if (baseB != null && !baseB.IsTraining && workerCountEco < 40)
                    {
                        if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT))
                            SimBuildingSystem.StartTraining(baseB, _world, SimUnitType.Worker);
                    }
                    TryBuildBalancedEco(myBuildings, myUnits, basePos);
                    break;

                case AIStrategyMode.Defensive:
                    // --- GELİŞTİRİLMİŞ DEFANS MODU ---
                    // Hedef: Hızlı Kule, Taş Odaklı Toplama, Üs Savunması

                    var dWorkers = myUnits
        .Where(u => u.UnitType == SimUnitType.Worker)
        .OrderBy(u => u.ID)
        .ToList();

                    int dwCount = dWorkers.Count;
                    int towerCountDef = myBuildings.Count(b => b.Type == SimBuildingType.Tower);

                    // 1. İşçi Basımı (Ekonomiyi canlı tutmak için 12'ye çıkardık, 7 çok azdı)
                    if (baseB != null && !baseB.IsTraining && dwCount < 12)
                    {
                        if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT))
                            SimBuildingSystem.StartTraining(baseB, _world, SimUnitType.Worker);
                    }

                    SimUnitData builderD = null;

                    for (int i = 0; i < dwCount; i++)
                    {
                        var w = dWorkers[i];

                        // Sonuncu işçiyi İNŞAATÇI yap (Liste sıralı olduğu için bu hep aynı birim olur)
                        if (i == dwCount - 1)
                        {
                            builderD = w;
                            continue;
                        }

                        // Diğerleri Kaynak Toplasın
                        // Sadece boşta ise VEYA yanlış kaynak türü topluyorsa müdahale et (Performans için kritik)
                        bool needsOrder = (w.State == SimTaskType.Idle);

                        // Hangi kaynağı toplaması lazım?
                        SimResourceType targetType = SimResourceType.Wood;
                        if (towerCountDef < 3)
                        {
                            if (i % 2 == 0) targetType = SimResourceType.Stone;
                            else targetType = SimResourceType.Wood;
                        }
                        else
                        {
                            if (i % 3 == 1) targetType = SimResourceType.Stone;
                            else if (i % 3 == 2) targetType = SimResourceType.Meat;
                        }

                        // Eğer zaten bir şey topluyorsa, doğru şeyi mi topluyor kontrol et
                        if (w.State == SimTaskType.Gathering || w.State == SimTaskType.Moving)
                        {
                            // Hedefindeki obje gerçekten istediğimiz tipte bir kaynak mı?
                            if (_world.Resources.TryGetValue(w.TargetID, out SimResourceData currentRes))
                            {
                                if (currentRes.Type != targetType) needsOrder = true; // Yanlış topluyor, değiştir
                            }
                            else if (w.State == SimTaskType.Gathering)
                            {
                                needsOrder = true; // Hedef kaybolmuş
                            }
                        }

                        if (needsOrder)
                        {
                            var res = FindNearestResource(basePos, targetType);
                            if (res != null && w.TargetID != res.ID)
                                SimUnitSystem.TryAssignGatherTask(w, res, _world);
                        }
                    }

                    // 3. İnşaat Mantığı (ÖNCELİK KULE!)
                    if (builderD != null
                    // && builderD.State == SimTaskType.Idle
                    )
                    {
                        // Buradaki TryBuildBuilding çağrıların aynen kalabilir (2 birim, 4 birim vs.)
                        if (pData.MaxPopulation - pData.CurrentPopulation <= 2)
                            TryBuildBuilding(SimBuildingType.House, new List<SimUnitData> { builderD }, basePos, SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT);
                        else if (towerCountDef < 6)
                            TryBuildBuilding(SimBuildingType.Tower, new List<SimUnitData> { builderD }, basePos, SimConfig.TOWER_COST_WOOD, SimConfig.TOWER_COST_STONE, SimConfig.TOWER_COST_MEAT, 4); // <--- Senin istediğin 4 birim ayarı
                        else if (!myBuildings.Any(b => b.Type == SimBuildingType.Barracks))
                            TryBuildBuilding(SimBuildingType.Barracks, new List<SimUnitData> { builderD }, basePos, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT);
                        else
                            TryBuildBuilding(SimBuildingType.Tower, new List<SimUnitData> { builderD }, basePos, SimConfig.TOWER_COST_WOOD, SimConfig.TOWER_COST_STONE, SimConfig.TOWER_COST_MEAT, 4);
                    }

                    // 4. Asker Basımı (Kışla varsa sürekli bas)
                    foreach (var b in myBuildings.Where(x => x.Type == SimBuildingType.Barracks && x.IsConstructed && !x.IsTraining))
                    {
                        if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT))
                            SimBuildingSystem.StartTraining(b, _world, SimUnitType.Soldier);
                    }

                    // 5. Defansif Asker Mantığı (Saldırı YOK, Devriye VAR)
                    // Askerler düşman üssüne gitmesin, kendi üssünün etrafında beklesin.
                    var soldiersd = myUnits.Where(u => u.UnitType == SimUnitType.Soldier).ToList();
                    foreach (var s in soldiersd)
                    {
                        if (s.State == SimTaskType.Idle)
                        {
                            // Base'in biraz etrafında rastgele bir nokta (Devriye gibi)
                            float angle = (float)_rng.NextDouble() * Mathf.PI * 2;
                            int radius = _rng.Next(3, 8); // Base'e yakın dur (3-8 birim)
                            int2 patrolPos = new int2(
                                basePos.x + (int)(Mathf.Cos(angle) * radius),
                                basePos.y + (int)(Mathf.Sin(angle) * radius)
                            );

                            if (SimGridSystem.IsWalkable(_world, patrolPos))
                            {
                                SimUnitSystem.MoveTo(s, patrolPos, _world);
                            }
                        }
                        // Not: SimUnitSystem içinde otomatik saldırı (range içine girince) varsa o çalışmaya devam eder.
                        // Ama biz zorla "OrderAttack" yapıp haritanın öbür ucuna göndermiyoruz.
                    }
                    break;

                case AIStrategyMode.Aggressive:
                    // --- AGRESİF MOD (BASİT SIRALI MANTIK) ---
                    int workerCount = myUnits.Count(u => u.UnitType == SimUnitType.Worker);
                    int soldiers = myUnits.Count(u => u.UnitType == SimUnitType.Soldier);
                    bool hasBarracks = myBuildings.Any(b => b.Type == SimBuildingType.Barracks);

                    // 1. Ev Kontrolü (Acil Durum)
                    if (pData.MaxPopulation - pData.CurrentPopulation <= 2)
                    {
                        TryBuildBuilding(SimBuildingType.House, myUnits, basePos, SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT);
                        // Ev yaparken diğer işleri bloklama, devam etsin
                    }

                    // 2. 5 İşçiye Ulaş
                    if (baseB != null && !baseB.IsTraining && workerCount < 5)
                    {
                        if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT))
                        {
                            SimBuildingSystem.StartTraining(baseB, _world, SimUnitType.Worker);
                        }
                        return; // 5 işçi olana kadar kaynak harcama, bekle!
                    }

                    // 3. Kışla Yap (5 İşçi var, Kışla yoksa)
                    if (!hasBarracks)
                    {
                        TryBuildBuilding(SimBuildingType.Barracks, myUnits, basePos, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT);
                        return; // Kışla bitene kadar kaynak harcama
                    }

                    // 4. Asker Bas (Kışla varsa, sürekli)
                    if (hasBarracks)
                    {
                        foreach (var b in myBuildings.Where(x => x.Type == SimBuildingType.Barracks && x.IsConstructed && !x.IsTraining))
                        {
                            if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT))
                                SimBuildingSystem.StartTraining(b, _world, SimUnitType.Soldier);
                        }
                    }

                    // 5. Saldırı (5 Asker olunca)
                    if (soldiers >= 5 && enemyBase != null)
                    {
                        foreach (var s in myUnits.Where(u => u.UnitType == SimUnitType.Soldier))
                        {
                            if (s.TargetID == -1 || s.State == SimTaskType.Idle)
                                SimUnitSystem.OrderAttack(s, enemyBase, _world);
                        }
                    }
                    break;
                case AIStrategyMode.General:
                    // --- GELİŞTİRİLMİŞ GENERAL (DENGELİ) MOD v2 ---
                    // Düzeltmeler: 
                    // 1. Ev spamı engellendi (Önce Kışla!).
                    // 2. Kuleler base etrafına (Radius 8-12) dikilecek.
                    // 3. Ekonomi ve Asker dengesi kuruldu.

                    int gWorkers = myUnits.Count(u => u.UnitType == SimUnitType.Worker);
                    int gSoldiers = myUnits.Count(u => u.UnitType == SimUnitType.Soldier);

                    int gBarracks = myBuildings.Count(b => b.Type == SimBuildingType.Barracks);
                    int gTowers = myBuildings.Count(b => b.Type == SimBuildingType.Tower);
                    int gFarms = myBuildings.Count(b => b.Type == SimBuildingType.Farm);
                    int gWoodCutters = myBuildings.Count(b => b.Type == SimBuildingType.WoodCutter);
                    int gStonePits = myBuildings.Count(b => b.Type == SimBuildingType.StonePit);

                    // --- 1. KRİTİK BAŞLANGIÇ (İlk 5 İşçi & İlk Kışla) ---
                    // Eğer hiç kışlamız yoksa ve odunumuz azsa, SAKIN ev yapma! Odunu kışlaya sakla.
                    bool saveWoodForBarracks = (gBarracks == 0 && pData.Wood < 400);

                    // İşçi Basımı (Öncelikli)
                    if (baseB != null && !baseB.IsTraining && gWorkers < 25)
                    {
                        if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT))
                            SimBuildingSystem.StartTraining(baseB, _world, SimUnitType.Worker);
                    }

                    // --- 2. AKILLI EV İNŞASI ---
                    // Nüfus limitine 2 kala ev yap AMA kışla parasını yeme.
                    if (pData.MaxPopulation - pData.CurrentPopulation <= 2)
                    {
                        if (!saveWoodForBarracks) // Kışla için para biriktirmiyorsak ev yap
                        {
                            TryBuildBuilding(SimBuildingType.House, myUnits, basePos, SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT);
                        }
                    }

                    // --- 3. İNŞAAT STRATEJİSİ ---
                    SimUnitData builderG = myUnits.FirstOrDefault(u => u.UnitType == SimUnitType.Worker && u.State != SimTaskType.Building);

                    if (builderG != null)
                    {
                        // A. KIŞLA (En Yüksek Öncelik)
                        if (gBarracks < 1)
                        {
                            TryBuildBuilding(SimBuildingType.Barracks, new List<SimUnitData> { builderG }, basePos, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT);
                        }
                        // B. EKONOMİ BİNALARI (Kaynaklar tükenmesin)
                        // Et, Odun veya Taş azaldığında ilgili binayı dik.
                        else if (pData.Meat < 200 && gFarms < 4)
                            TryBuildBuilding(SimBuildingType.Farm, new List<SimUnitData> { builderG }, basePos, SimConfig.FARM_COST_WOOD, SimConfig.FARM_COST_STONE, SimConfig.FARM_COST_MEAT);
                        else if (pData.Wood < 200 && gWoodCutters < 4)
                            TryBuildBuilding(SimBuildingType.WoodCutter, new List<SimUnitData> { builderG }, basePos, SimConfig.WOODCUTTER_COST_WOOD, SimConfig.WOODCUTTER_COST_STONE, SimConfig.WOODCUTTER_COST_MEAT);
                        else if (pData.Stone < 150 && gStonePits < 3)
                            TryBuildBuilding(SimBuildingType.StonePit, new List<SimUnitData> { builderG }, basePos, SimConfig.STONEPIT_COST_WOOD, SimConfig.STONEPIT_COST_STONE, SimConfig.STONEPIT_COST_MEAT);

                        // C. SAVUNMA KULELERİ (Base Etrafına Sur Gibi)
                        // Base etrafında 8-12 birim yarıçapında koruma çemberi oluştur.
                        else if (gTowers < 5)
                        {
                            // Kuleler için "strictRadius" parametresini 10 olarak veriyoruz (Base'in dibine değil, çevresine)
                            TryBuildBuilding(SimBuildingType.Tower, new List<SimUnitData> { builderG }, basePos, SimConfig.TOWER_COST_WOOD, SimConfig.TOWER_COST_STONE, SimConfig.TOWER_COST_MEAT, 10);
                        }

                        // D. İKİNCİ KIŞLA (Orduyu hızlandırmak için)
                        else if (gBarracks < 2 && pData.Wood > 500)
                            TryBuildBuilding(SimBuildingType.Barracks, new List<SimUnitData> { builderG }, basePos, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT);
                    }

                    // --- 4. KAYNAK YÖNETİMİ (İşçileri Yönlendir) ---
                    var idleWorkers = myUnits.Where(u => u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle).ToList();
                    foreach (var w in idleWorkers)
                    {
                        SimResourceType targetRes = SimResourceType.Wood;

                        // İhtiyaca göre dinamik yönlendirme
                        if (gBarracks < 1) targetRes = SimResourceType.Wood; // Kışla yoksa odun
                        else if (pData.Meat < 100) targetRes = SimResourceType.Meat; // Asker basacak et yoksa et
                        else if (gTowers < 5 && pData.Stone < 100) targetRes = SimResourceType.Stone; // Kule için taş
                        else
                        {
                            // Genel Dağılım (%40 Odun, %40 Et, %20 Taş)
                            int r = _rng.Next(0, 100);
                            if (r < 40) targetRes = SimResourceType.Wood;
                            else if (r < 80) targetRes = SimResourceType.Meat;
                            else targetRes = SimResourceType.Stone;
                        }

                        var rData = FindNearestResource(w.GridPosition, targetRes);
                        if (rData != null) SimUnitSystem.TryAssignGatherTask(w, rData, _world);
                    }

                    // --- 5. ASKER ÜRETİMİ ---
                    // Ekonomiyi bozmamak için en az 8 işçi olana kadar asker basma.
                    if (gWorkers >= 8)
                    {
                        foreach (var b in myBuildings.Where(x => x.Type == SimBuildingType.Barracks && x.IsConstructed && !x.IsTraining))
                        {
                            if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT))
                                SimBuildingSystem.StartTraining(b, _world, SimUnitType.Soldier);
                        }
                    }

                    // --- 6. SALDIRI / SAVUNMA KARARLARI ---

                    // A. SALDIRI: 25 Asker olunca topluca düşman üssüne git!
                    if (gSoldiers >= 25 && enemyBase != null)
                    {
                        foreach (var s in myUnits.Where(u => u.UnitType == SimUnitType.Soldier && u.State == SimTaskType.Idle))
                            SimUnitSystem.OrderAttack(s, enemyBase, _world);
                    }
                    // B. SAVUNMA: Saldırı gücüne ulaşana kadar üssü koru.
                    else
                    {
                        // Üsse 25 birim yaklaşan düşman var mı?
                        var nearestEnemy = _world.Units.Values
                            .Where(u => u.PlayerID != _playerID && SimGridSystem.GetDistanceSq(u.GridPosition, basePos) < 25 * 25)
                            .OrderBy(u => SimGridSystem.GetDistanceSq(u.GridPosition, basePos))
                            .FirstOrDefault();

                        if (nearestEnemy != null)
                        {
                            // Tüm boşta askerleri savunmaya çek
                            foreach (var s in myUnits.Where(u => u.UnitType == SimUnitType.Soldier && u.State == SimTaskType.Idle))
                                SimUnitSystem.OrderAttackUnit(s, nearestEnemy, _world);
                        }
                        else
                        {
                            // Düşman yoksa kulelerin etrafında devriye gez (Base önünde birik)
                            // Bu sayede askerler haritanın ucunda tek kalmaz.
                            foreach (var s in myUnits.Where(u => u.UnitType == SimUnitType.Soldier && u.State == SimTaskType.Idle))
                            {
                                float angle = (float)_rng.NextDouble() * Mathf.PI * 2;
                                int patrolRadius = 8;
                                int2 guardPos = new int2(basePos.x + (int)(Mathf.Cos(angle) * patrolRadius), basePos.y + (int)(Mathf.Sin(angle) * patrolRadius));
                                if (SimGridSystem.IsWalkable(_world, guardPos)) SimUnitSystem.MoveTo(s, guardPos, _world);
                            }
                        }
                    }
                    break;
            }
        }

        // ==================================================================================
        // 2. PARAMETRİK DAVRANIŞ (BİZİM AJAN - EĞİTİLEN)
        // ==================================================================================
        private void ExecuteParametricBehavior()
        {
            var myUnits = _world.Units.Values.Where(u => u.PlayerID == _playerID).ToList();
            var myBuildings = _world.Buildings.Values.Where(b => b.PlayerID == _playerID).ToList();
            var pData = SimResourceSystem.GetPlayer(_world, _playerID);
            var baseB = myBuildings.FirstOrDefault(b => b.Type == SimBuildingType.Base);
            var enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID != _playerID && b.Type == SimBuildingType.Base);

            // Güvenli merkez
            int2 basePos = (baseB != null) ? baseB.GridPosition : new int2(25, 25);

            int workers = myUnits.Count(u => u.UnitType == SimUnitType.Worker);
            int soldiers = myUnits.Count(u => u.UnitType == SimUnitType.Soldier);
            int freePop = pData.MaxPopulation - pData.CurrentPopulation;

            // --- GEN OKUMA ---
            int targetWorker = SimMath.Clamp(SimMath.RoundToInt(_genes[0] * 1.5f), 5, 80);
            int targetSoldier = SimMath.Clamp(SimMath.RoundToInt(_genes[1] * 2f), 0, 100);
            int attackThreshold = SimMath.Clamp(SimMath.RoundToInt(_genes[2]), 1, 60);
            float defenseRatio = SimMath.Clamp01(_genes[3] / 20f);
            int targetBarracks = SimMath.Clamp(SimMath.RoundToInt(_genes[4] / 5f), 0, 8);
            float ecoBias = SimMath.Clamp01(_genes[5] / 40f);

            int targetFarm = SimMath.RoundToInt(_genes[6]);
            int targetWood = SimMath.RoundToInt(_genes[7]);
            int targetStone = SimMath.RoundToInt(_genes[8]);
            int houseBuffer = SimMath.Clamp(SimMath.RoundToInt(_genes[9] / 4f), 1, 10);
            float towerPosBias = SimMath.Clamp01(_genes[10] / 40f);

            float prioEco = _genes[11];
            float prioDef = _genes[12];
            float prioMil = _genes[13];

            ManageWorkersParametric(myUnits, ecoBias, pData);

            List<Func<bool>> taskQueue = new List<Func<bool>>();

            // A. EKONOMİ (Para varsa yap, yoksa bloke et)
            taskQueue.Add(() =>
            {
                bool busy = false;
                if (baseB != null && !baseB.IsTraining && workers < targetWorker && freePop > 0)
                {
                    if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT))
                    {
                        SimBuildingSystem.StartTraining(baseB, _world, SimUnitType.Worker);
                        busy = true;
                    }
                    else return true; // İşçi basmam lazım ama param yok, BEKLE
                }

                if (freePop <= houseBuffer)
                {
                    if (TryBuildBuilding(SimBuildingType.House, myUnits, basePos, SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT)) busy = true;
                    else return true;
                }

                if (TryBuildEcoStructuresBalanced(targetFarm, targetWood, targetStone, myBuildings, myUnits, basePos)) busy = true;
                return busy;
            });

            // B. ASKERİ
            taskQueue.Add(() =>
            {
                int barracksCount = myBuildings.Count(b => b.Type == SimBuildingType.Barracks);
                if (barracksCount < targetBarracks)
                {
                    if (TryBuildBuilding(SimBuildingType.Barracks, myUnits, basePos, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT)) return true;
                    else return true;
                }

                if (soldiers < targetSoldier && freePop > 0)
                {
                    bool trainingStarted = false;
                    foreach (var b in myBuildings.Where(x => x.Type == SimBuildingType.Barracks && x.IsConstructed && !x.IsTraining))
                    {
                        if (SimResourceSystem.CanAfford(_world, _playerID, SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT))
                        {
                            SimBuildingSystem.StartTraining(b, _world, SimUnitType.Soldier);
                            trainingStarted = true;
                        }
                        else return true;
                    }
                    if (trainingStarted) return true;
                }
                return false;
            });

            // C. SAVUNMA
            taskQueue.Add(() =>
            {
                int towerCount = myBuildings.Count(b => b.Type == SimBuildingType.Tower);
                int neededTowers = 1 + SimMath.FloorToInt(soldiers * defenseRatio);
                if (prioDef > 30) neededTowers = 7;

                if (towerCount < neededTowers)
                {
                    // Orta Saha Kuralı (Genler istese bile düşman base'in dibine dikemez)
                    int2 targetPos = basePos;
                    if (towerPosBias > 0.5f && enemyBase != null)
                    {
                        targetPos = new int2(
                            (basePos.x + enemyBase.GridPosition.x) / 2,
                            (basePos.y + enemyBase.GridPosition.y) / 2
                        );
                    }

                    if (TryBuildBuilding(SimBuildingType.Tower, myUnits, targetPos, SimConfig.TOWER_COST_WOOD, SimConfig.TOWER_COST_STONE, SimConfig.TOWER_COST_MEAT, 2))
                        return true;

                    return true;
                }
                return false;
            });

            var priorities = new List<(float score, int index)> { (prioEco, 0), (prioMil, 1), (prioDef, 2) };
            var sortedTasks = priorities.OrderByDescending(x => x.score).ToList();

            foreach (var item in sortedTasks)
            {
                bool shouldBlock = taskQueue[item.index].Invoke();
                if (shouldBlock) break;
            }

            if (soldiers >= attackThreshold && enemyBase != null)
            {
                foreach (var s in myUnits.Where(u => u.UnitType == SimUnitType.Soldier))
                {
                    if (s.TargetID == -1 || s.State == SimTaskType.Idle)
                        SimUnitSystem.OrderAttack(s, enemyBase, _world);
                }
            }
        }

        // ==================================================================================
        // YARDIMCI FONKSİYONLAR
        // ==================================================================================

        private void ManageWorkersDefault(List<SimUnitData> units, SimPlayerData pData)
        {
            var idleWorkers = units.Where(u => u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle).ToList();
            if (idleWorkers.Count == 0) return;

            foreach (var w in idleWorkers)
            {
                SimResourceType targetType;
                // Önce ET (60)
                if (pData.Meat < 60) targetType = SimResourceType.Meat;
                // Sonra DENGE
                else
                {
                    if (pData.Wood <= pData.Meat && pData.Wood <= pData.Stone) targetType = SimResourceType.Wood;
                    else if (pData.Meat <= pData.Wood && pData.Meat <= pData.Stone) targetType = SimResourceType.Meat;
                    else targetType = SimResourceType.Stone;
                }

                var res = FindNearestResource(w.GridPosition, targetType);
                if (res == null) res = FindNearestResource(w.GridPosition, SimResourceType.None);
                if (res != null) SimUnitSystem.TryAssignGatherTask(w, res, _world);
            }
        }

        // --- AGRESİF ENEMY İÇİN KAYNAK YÖNETİMİ ---
        private void ManageWorkersAggressive(List<SimUnitData> units, SimPlayerData pData, List<SimBuildingData> myBuildings)
        {
            var idleWorkers = units.Where(u => u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle).ToList();
            bool hasBarracks = myBuildings.Any(b => b.Type == SimBuildingType.Barracks);
            int workerCount = units.Count(u => u.UnitType == SimUnitType.Worker);

            foreach (var w in idleWorkers)
            {
                SimResourceType targetType = SimResourceType.Meat;

                // 1. Eğer 5 işçiden azsak, SADECE ET topla! (Kışla, Odun umrumuzda değil)
                if (workerCount < 5)
                {
                    targetType = SimResourceType.Meat;
                }
                // 2. 5 İşçi tamam ama Kışla yok -> Kışla için Odun/Taş topla
                else if (!hasBarracks)
                {
                    if (pData.Wood < SimConfig.BARRACKS_COST_WOOD) targetType = SimResourceType.Wood;
                    else if (pData.Stone < SimConfig.BARRACKS_COST_STONE) targetType = SimResourceType.Stone;
                    else targetType = SimResourceType.Meat;
                }
                // 3. Kışla var -> Asker için Et/Odun topla
                else
                {
                    if (pData.Meat < 100) targetType = SimResourceType.Meat;
                    else if (pData.Wood < 100) targetType = SimResourceType.Wood;
                }

                var res = FindNearestResource(w.GridPosition, targetType);
                if (res == null) res = FindNearestResource(w.GridPosition, SimResourceType.None);
                if (res != null) SimUnitSystem.TryAssignGatherTask(w, res, _world);
            }
        }

        private void ManageWorkersParametric(List<SimUnitData> units, float ecoBias, SimPlayerData pData)
        {
            var idleWorkers = units.Where(u => u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle).ToList();
            foreach (var w in idleWorkers)
            {
                SimResourceType targetType;
                double rng = _rng.NextDouble();
                float woodProb = 0.6f - (ecoBias * 0.4f);

                if (rng < woodProb) targetType = SimResourceType.Wood;
                else
                {
                    if (_rng.NextDouble() > 0.5) targetType = SimResourceType.Meat;
                    else targetType = SimResourceType.Stone;
                }

                if (pData.Meat < 50) targetType = SimResourceType.Meat;
                if (pData.Wood < 100) targetType = SimResourceType.Wood;

                var res = FindNearestResource(w.GridPosition, targetType);
                if (res == null) res = FindNearestResource(w.GridPosition, SimResourceType.None);
                if (res != null) SimUnitSystem.TryAssignGatherTask(w, res, _world);
            }
        }

        private void TryBuildBalancedEco(List<SimBuildingData> myBuildings, List<SimUnitData> myUnits, int2 basePos)
        {
            TryBuildEcoStructuresBalanced(3, 3, 2, myBuildings, myUnits, basePos);
        }

        private bool TryBuildEcoStructuresBalanced(int tFarm, int tWood, int tStone, List<SimBuildingData> myBuildings, List<SimUnitData> myUnits, int2 basePos)
        {
            int farm = myBuildings.Count(b => b.Type == SimBuildingType.Farm);
            int wood = myBuildings.Count(b => b.Type == SimBuildingType.WoodCutter);
            int stone = myBuildings.Count(b => b.Type == SimBuildingType.StonePit);

            var deficits = new List<(SimBuildingType type, int count, int costW, int costS, int costM)>();

            if (farm < tFarm) deficits.Add((SimBuildingType.Farm, farm, SimConfig.FARM_COST_WOOD, SimConfig.FARM_COST_STONE, SimConfig.FARM_COST_MEAT));
            if (wood < tWood) deficits.Add((SimBuildingType.WoodCutter, wood, SimConfig.WOODCUTTER_COST_WOOD, SimConfig.WOODCUTTER_COST_STONE, SimConfig.WOODCUTTER_COST_MEAT));
            if (stone < tStone) deficits.Add((SimBuildingType.StonePit, stone, SimConfig.STONEPIT_COST_WOOD, SimConfig.STONEPIT_COST_STONE, SimConfig.STONEPIT_COST_MEAT));

            if (deficits.Count == 0) return false;

            var best = deficits.OrderBy(x => x.count).First();
            return TryBuildBuilding(best.type, myUnits, basePos, best.costW, best.costS, best.costM);
        }

        private SimUnitData GetAvailableWorker(List<SimUnitData> units)
        {
            var w = units.FirstOrDefault(u => u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle);
            if (w != null) return w;
            return units.FirstOrDefault(u => u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Gathering);
        }

        // Parametre sonuna 'bool createGate = false' eklendi
        private bool TryBuildBuilding(SimBuildingType type, List<SimUnitData> units, int2 centerPos, int costWood, int costStone, int costMeat, int strictRadius = -1, bool createGate = false)
        {
            if (!SimResourceSystem.CanAfford(_world, _playerID, costWood, costStone, costMeat)) return false;

            SimUnitData worker = null;
            if (units != null && units.Count > 0)
            {
                if (units.Count == 1) worker = units[0];
                else worker = GetAvailableWorker(units);
            }

            if (worker == null) return false;

            int minRadius;
            int maxRadius;

            if (strictRadius > 0)
            {
                minRadius = strictRadius; // Direkt o mesafeden başla
                maxRadius = strictRadius + 4; // Biraz esneme payı
            }
            else if (type == SimBuildingType.Tower)
            {
                minRadius = 6;
                maxRadius = 14;
            }
            else
            {
                int buildingCount = _world.Buildings.Values.Count(b => b.PlayerID == _playerID);
                minRadius = 4 + (buildingCount / 5) * 2;
                maxRadius = minRadius + 10;
            }

            List<int2> avoidTargets = new List<int2>();
            if (type == SimBuildingType.Barracks)
            {
                foreach (var b in _world.Buildings.Values)
                {
                    if (b.PlayerID != _playerID && b.Type == SimBuildingType.Tower)
                        avoidTargets.Add(b.GridPosition);
                }
            }

            // createGate parametresini buraya iletiyoruz
            int2 pos = FindBuildSpot(centerPos, minRadius, maxRadius, avoidTargets, createGate);

            if (pos.x != -1)
            {
                SimResourceSystem.SpendResources(_world, _playerID, costWood, costStone, costMeat);
                var b = SpawnPlaceholder(type, pos);
                SimUnitSystem.OrderBuild(worker, b, _world);
                return true;
            }
            return false;
        }

        // SpecializedMacroAI.cs içinde ilgili fonksiyonu bul ve bununla değiştir:

        // Fonksiyonun imzasına 'bool createGate' parametresini ekledik (Varsayılan: false)
        private int2 FindBuildSpot(int2 center, int minRadius, int maxRadius, List<int2> avoidList = null, bool createGate = false)
        {
            float safeDistSq = 100f;
            float buildingSpacingSq = 2.5f;

            // 1. AŞAMA: İDEAL YER ARA
            for (int r = minRadius; r <= maxRadius; r++)
            {
                List<int2> candidates = new List<int2>();

                for (int x = -r; x <= r; x++)
                {
                    for (int y = -r; y <= r; y++)
                    {
                        if (System.Math.Abs(x) == r || System.Math.Abs(y) == r)
                        {
                            // --- KAPI MANTIĞI (GATE LOGIC) ---
                            // Eğer kapı isteniyorsa ve şu an halkanın ALT kenarındaysak (y == -r),
                            // ve merkeze yatayda yakınsak (|x| < 3), burayı pas geç.
                            // Bu, üssün altında 5 karelik ( -2, -1, 0, 1, 2 ) bir koridor açar.
                            if (createGate)
                            {
                                if (y == -r && System.Math.Abs(x) < 3) continue;
                            }
                            // ---------------------------------

                            int2 pos = new int2(center.x + x, center.y + y);

                            if (IsPosValid(pos))
                            {
                                if (IsSafeFromEnemies(pos, avoidList, safeDistSq))
                                {
                                    if (!IsTooCloseToBuildings(pos, buildingSpacingSq))
                                    {
                                        candidates.Add(pos);
                                    }
                                }
                            }
                        }
                    }
                }

                if (candidates.Count > 0)
                {
                    return candidates[_rng.Next(candidates.Count)];
                }
            }

            // 2. AŞAMA: YEDEK PLAN (Burada da kapı kuralına uyuyoruz)
            for (int r = minRadius; r <= maxRadius + 5; r++)
            {
                for (int x = -r; x <= r; x++)
                {
                    for (int y = -r; y <= r; y++)
                    {
                        if (System.Math.Abs(x) == r || System.Math.Abs(y) == r)
                        {
                            // Yedek planda da kapıyı kapatma!
                            if (createGate)
                            {
                                if (y == -r && System.Math.Abs(x) < 3) continue;
                            }

                            int2 pos = new int2(center.x + x, center.y + y);
                            if (IsPosValid(pos)) return pos;
                        }
                    }
                }
            }

            return new int2(-1, -1);
        }

        // --- YARDIMCI KÜÇÜK FONKSİYONLAR (Okunabilirlik İçin) ---

        private bool IsPosValid(int2 pos)
        {
            if (pos.x <= 1 || pos.x >= SimConfig.MAP_WIDTH - 1 || pos.y <= 1 || pos.y >= SimConfig.MAP_HEIGHT - 1) return false;
            return SimGridSystem.IsWalkable(_world, pos);
        }

        private bool IsSafeFromEnemies(int2 pos, List<int2> avoidList, float safeDist)
        {
            if (avoidList == null || avoidList.Count == 0) return true;
            foreach (var danger in avoidList)
            {
                if (SimGridSystem.GetDistanceSq(pos, danger) < safeDist) return false;
            }
            return true;
        }

        private bool IsTooCloseToBuildings(int2 pos, float spacing)
        {
            foreach (var b in _world.Buildings.Values)
            {
                if (SimGridSystem.GetDistanceSq(pos, b.GridPosition) < spacing) return true;
            }
            return false;
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

        public void SetGenes(float[] newGenes, string strategyName = "")
        {
            // Genleri güncelle
            this._genes = newGenes;

            // Debug için log (Hangi stratejiye geçtiğimizi görmek için)
            if (!string.IsNullOrEmpty(strategyName) && SimConfig.EnableLogs)
            {
                Debug.Log($"🧬 STRATEJİ DEĞİŞTİ: {strategyName} Moduna Geçildi.");
            }
        }
    }
}