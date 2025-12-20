using System.Linq;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using UnityEngine;

namespace RTS.Simulation.AI
{
    // Oyun durumunu matematiksel verilere döken statik analiz aracı
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

        public static GameMetrics CalculateGSF(SimWorldState world, int myPlayerID)
        {
            GameMetrics m = new GameMetrics();

            // Oyuncuları ve birimleri ayıkla
            var myUnits = world.Units.Values.Where(u => u.PlayerID == myPlayerID).ToList();
            var enemyUnits = world.Units.Values.Where(u => u.PlayerID != myPlayerID).ToList();

            var myBuildings = world.Buildings.Values.Where(b => b.PlayerID == myPlayerID).ToList();
            var enemyBuildings = world.Buildings.Values.Where(b => b.PlayerID != myPlayerID).ToList();

            // --- GÜÇ FORMÜLLERİ ---

            // 1. Saldırı Gücü (Asker Sayısı + Askerlerin Can Oranı)
            // Sadece sayı yetmez, canı azalan askerin gücü düşmeli mi? Şimdilik sayı üzerinden gidelim.
            // Asker başına 10 puan.
            m.MAP = myUnits.Count(u => u.UnitType == SimUnitType.Soldier) * 10f;
            m.EAP = enemyUnits.Count(u => u.UnitType == SimUnitType.Soldier) * 10f;

            // 2. Savunma Gücü (Kuleler + Ana Bina Sağlığı)
            // Kule başına 50 puan (Kule askeri yener mantığı)
            // Base sağlığı da son kale olduğu için puana dahil edilir (Max 100 puan)

            float myBaseHealth = myBuildings.FirstOrDefault(b => b.Type == SimBuildingType.Base)?.Health ?? 0;
            m.MDP = (myBuildings.Count(b => b.Type == SimBuildingType.Tower && b.IsConstructed) * 50f) + (myBaseHealth * 0.1f);

            float enemyBaseHealth = enemyBuildings.FirstOrDefault(b => b.Type == SimBuildingType.Base)?.Health ?? 0;
            m.EDP = (enemyBuildings.Count(b => b.Type == SimBuildingType.Tower && b.IsConstructed) * 50f) + (enemyBaseHealth * 0.1f);

            // --- GSF HESABI ---
            // GSF = (Benim Toplam Gücüm) - (Rakibin Toplam Gücü)
            // Pozitif (+): Ben üstünüm -> Saldırabilirim
            // Negatif (-): Rakip üstün -> Savunmalıyım

            float myTotalPower = m.MAP + m.MDP;
            float enemyTotalPower = m.EAP + m.EDP;

            m.GSF = myTotalPower - enemyTotalPower;

            return m;
        }
    }
}