using System.Collections.Generic;

namespace RTS.Simulation.Data
{
    [System.Serializable]
    public class SimUnitData
    {
        public int ID;
        public int PlayerID;
        public SimUnitType UnitType; // Worker mı Soldier mı?

        // --- KONUM & HAREKET ---
        public int2 GridPosition;
        public List<int2> Path = new List<int2>();
        public float MoveProgress;
        public float MoveSpeed;

        // --- SAVAŞ (COMBAT) İSTATİSTİKLERİ ---
        public int Health;
        public int MaxHealth;
        public int Damage;          // Vuruş gücü
        public float AttackRange;   // Menzil
        public float AttackSpeed;   // Saldırı hızı (sn)
        public float AttackTimer;   // Saldırı bekleme süresi

        // --- GÖREV DURUMU ---
        public SimTaskType State;
        public int TargetID;       // Hedef (Kaynak, Bina veya Düşman Unit ID'si)
        public float ActionTimer;  // Toplama/İnşaat süresi
    }
}