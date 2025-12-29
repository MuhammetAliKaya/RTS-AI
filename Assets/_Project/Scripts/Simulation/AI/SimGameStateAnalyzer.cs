using System.Linq;
using RTS.Simulation.Data;
using RTS.Simulation.Core;
using RTS.Simulation.Systems;
using UnityEngine;

namespace RTS.Simulation.AI
{
    public static class SimGameStateAnalyzer
    {
        public struct GameMetrics
        {
            public float MAP; // My Attack Power
            public float EAP; // Enemy Attack Power
            public float MDP; // My Defense Power
            public float EDP; // Enemy Defense Power
            public float GSF; // Global Situation Factor
        }

        // PARAMETRELERİ GÜNCELLEDİK: Artık kısıtlamaları da alıyor
        public static GameMetrics CalculateGSF(SimWorldState world, int myPlayerID, float inactivityTime,
                                               int minDefSteps, int minTowers,
                                               int matSoldier, int matRes)
        {
            GameMetrics m = new GameMetrics();

            // --- TEMEL VERİLERİ ÇEK ---
            var myUnits = world.Units.Values.Where(u => u.PlayerID == myPlayerID).ToList();
            var enemyUnits = world.Units.Values.Where(u => u.PlayerID != myPlayerID).ToList();
            var myBuildings = world.Buildings.Values.Where(b => b.PlayerID == myPlayerID).ToList();
            var enemyBuildings = world.Buildings.Values.Where(b => b.PlayerID != myPlayerID).ToList();

            var pData = SimResourceSystem.GetPlayer(world, myPlayerID);
            int totalRes = (pData != null) ? (pData.Wood + pData.Stone + pData.Meat) : 0;

            // --- 1. GÜÇ HESAPLARI (KLASİK) ---
            m.MAP = myUnits.Count(u => u.UnitType == SimUnitType.Soldier) * 10f;

            float totalEnemyThreat = 0f;
            var myBase = myBuildings.FirstOrDefault(b => b.Type == SimBuildingType.Base);
            int2 myBasePos = (myBase != null) ? myBase.GridPosition : new int2(SimConfig.MAP_WIDTH / 2, SimConfig.MAP_HEIGHT / 2);

            foreach (var u in enemyUnits)
            {
                if (u.UnitType == SimUnitType.Soldier)
                {
                    float baseScore = 20f;
                    float dist = SimMath.Distance(u.GridPosition, myBasePos);
                    if (dist < 15f) baseScore *= 10.0f;
                    else if (dist < 30f) baseScore *= 5f;
                    totalEnemyThreat += baseScore;
                }
            }
            m.EAP = totalEnemyThreat;

            m.MDP = (myBuildings.Count(b => b.Type == SimBuildingType.Tower && b.IsConstructed) * 50f);
            float myBaseHp = (myBase != null) ? myBase.Health : 0;
            m.MDP += myBaseHp * 0.1f;

            m.EDP = (enemyBuildings.Count(b => b.Type == SimBuildingType.Tower && b.IsConstructed) * 50f);
            float enemyBaseHp = enemyBuildings.FirstOrDefault(b => b.Type == SimBuildingType.Base)?.Health ?? 0;
            m.EDP += enemyBaseHp * 0.1f;

            // --- 2. GSF HESAPLAMA (TEMEL) ---
            m.GSF = (m.MAP + m.MDP) - (m.EAP + m.EDP);

            // --- 3. EKSTRA PUAN GİRDİLERİ (INJECTION) ---

            // A. Pasiflik Bonusu (Fırsat)
            if (inactivityTime > 60f)
            {
                float bonus = (inactivityTime - 60f) * 0.5f;
                if (bonus > 150f) bonus = 150f;
                m.GSF += bonus;
            }

            // B. Zorunlu Defans Cezası (Early Game Constraint)
            // Eğer oyun başındaysa VE kule eksikse -> GSF'ye devasa ceza ver.
            // Bu ceza, skoru -500'e çeker ve AI doğal olarak Savunma Modu'na girer.
            int myTowerCount = myBuildings.Count(b => b.Type == SimBuildingType.Tower);
            if (world.TickCount < minDefSteps && myTowerCount < minTowers)
            {
                m.GSF -= 1000f; // -40 eşiğini kesinlikle deler
            }

            // C. Saldırı Olgunluğu Bonusu (Late Game Trigger)
            // Eğer asker veya kaynak hedefi tuttuysa -> GSF'ye devasa bonus ver.
            // Bu bonus, skoru +500'e çeker ve AI doğal olarak Saldırı Modu'na girer.
            int mySoldierCount = myUnits.Count(u => u.UnitType == SimUnitType.Soldier);
            if (mySoldierCount >= matSoldier || totalRes >= matRes)
            {
                m.GSF += 1000f; // +50 eşiğini kesinlikle deler
            }

            return m;
        }
    }
}