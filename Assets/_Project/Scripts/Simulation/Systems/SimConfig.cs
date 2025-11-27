namespace RTS.Simulation.Systems
{
    public static class SimConfig
    {
        // --- GENEL AYARLAR ---
        public const float TICK_RATE = 0.1f; // Simülasyonun zaman dilimi (saniye)

        // --- BİRLİK (UNIT) AYARLARI ---
        public const float WORKER_MOVE_SPEED = 5.0f;
        public const float SOLDIER_MOVE_SPEED = 4.0f;
        public const int WORKER_MAX_HEALTH = 50;
        public const int SOLDIER_MAX_HEALTH = 120;

        // --- SAVAŞ (COMBAT) ---
        public const int SOLDIER_DAMAGE = 15;
        public const float SOLDIER_ATTACK_SPEED = 1.5f;
        public const float SOLDIER_ATTACK_RANGE = 1.5f;

        public const int TOWER_DAMAGE = 25;
        public const float TOWER_ATTACK_SPEED = 2.0f;
        public const float TOWER_ATTACK_RANGE = 5.0f;

        // --- TOPLAMA (GATHERING) ---
        public const float GATHER_INTERVAL = 0.5f;
        public const int GATHER_AMOUNT = 10;

        // --- İNŞAAT (BUILDING) ---
        public const float BUILD_INTERVAL = 1.0f;
        public const float BUILD_AMOUNT_PER_TICK = 10.0f;
        public const float BUILDING_MAX_PROGRESS = 100.0f;

        // --- ÜRETİM (PRODUCTION) ---
        public const float WORKER_TRAIN_TIME = 2.0f;
        public const float SOLDIER_TRAIN_TIME = 8.0f;

        // --- PASİF KAYNAK ÜRETİMİ (FARM/MINE) ---
        public const float RESOURCE_GENERATION_INTERVAL = 5.0f;
        public const int RESOURCE_GENERATION_AMOUNT = 15;

        // --- MALİYETLER (COSTS) - [YENİ EKLENEN KISIM] ---
        public const int WORKER_COST_MEAT = 50;
        public const int BARRACKS_COST_WOOD = 1000;
        public const int BARRACKS_COST_STONE = 1000;

        // --- NÜFUS KAPASİTESİ (POPULATION) ---
        public const int POPULATION_BASE = 10;  // Ana Üs kaç kişi barındırır?
        public const int POPULATION_HOUSE = 5; // Ev kaç kişi barındırır?
    }
}