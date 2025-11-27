namespace RTS.Simulation.Data
{
    [System.Serializable]
    public class SimBuildingData
    {
        public int ID;
        public int PlayerID;
        public SimBuildingType Type;
        public int2 GridPosition;

        // --- SAĞLIK & İNŞAAT ---
        public bool IsConstructed;
        public float ConstructionProgress;
        public int Health;
        public int MaxHealth;

        // --- SAVUNMA (TOWER İÇİN) ---
        public int Damage;          // Kule Hasarı
        public float AttackRange;   // Kule Menzili
        public float AttackSpeed;   // Ateş Hızı
        public float AttackTimer;
        public int TargetUnitID;    // Kime ateş ediyor? (-1 ise kimseye)

        // --- ÜRETİM (BASE/BARRACKS İÇİN) ---
        public bool IsTraining;
        public float TrainingTimer;
        public SimUnitType UnitInProduction; // Ne üretiyor?


        // --- KAYNAK ÜRETİMİ (FARM/WOODCUTTER/STONEPIT İÇİN - YENİ EKLENDİ) ---
        public bool IsResourceGenerator;     // Bu bina kaynak üretiyor mu?
        public SimResourceType ResourceType; // Hangi kaynağı üretiyor? (Meat/Wood/Stone)
        public float ResourceTimer;          // Üretim sayacı
        public float ResourceInterval;       // Kaç saniyede bir kaynak verecek?
        public int ResourceAmountPerCycle;   // Her döngüde kaç kaynak verecek?
    }
}