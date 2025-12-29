namespace RTS.Simulation.Systems
{
    public static class SimConfig
    {
        public static bool EnableLogs = false;
        // --- GENEL SİMÜLASYON ---
        public const float TICK_RATE = 0.1f;
        public const int MAP_WIDTH = 50;
        public const int MAP_HEIGHT = 50;

        // --- BİNA MALİYETLERİ (ODUN / TAŞ / ET) ---

        // Ev (House)
        public const int HOUSE_COST_WOOD = 250;
        public const int HOUSE_COST_STONE = 250;
        public const int HOUSE_COST_MEAT = 50; // Yeni

        // Çiftlik (Farm)
        public const int FARM_COST_WOOD = 0;
        public const int FARM_COST_STONE = 0;
        public const int FARM_COST_MEAT = 250; // Yeni

        // Oduncu (WoodCutter)
        public const int WOODCUTTER_COST_WOOD = 250;
        public const int WOODCUTTER_COST_STONE = 0;
        public const int WOODCUTTER_COST_MEAT = 0; // Örn: İşçiye yemek verip oduncu yapıyoruz

        // Taş Ocağı (StonePit)
        public const int STONEPIT_COST_WOOD = 0;
        public const int STONEPIT_COST_STONE = 250;
        public const int STONEPIT_COST_MEAT = 0; // Yeni

        // Kışla (Barracks)
        public const int BARRACKS_COST_WOOD = 1000;
        public const int BARRACKS_COST_STONE = 1000;
        public const int BARRACKS_COST_MEAT = 0; // Yeni

        // Kule (Tower)
        public const int TOWER_COST_WOOD = 200;
        public const int TOWER_COST_STONE = 200;
        public const int TOWER_COST_MEAT = 0; // Yeni

        // Duvar (Wall)
        public const int WALL_COST_WOOD = 0;
        public const int WALL_COST_STONE = 50;
        public const int WALL_COST_MEAT = 0; // Yeni

        // --- ÜRETİM MALİYETLERİ (ET / ODUN / TAŞ) ---
        public const int WORKER_COST_MEAT = 250;
        public const int WORKER_COST_WOOD = 0;
        public const int WORKER_COST_STONE = 0;

        public const int SOLDIER_COST_MEAT = 250;
        public const int SOLDIER_COST_WOOD = 250;
        public const int SOLDIER_COST_STONE = 0;

        // --- İNŞAAT VE ZAMANLAMA ---
        public const float BUILDING_MAX_PROGRESS = 100f;
        public const float BUILD_AMOUNT_PER_TICK = 5f;
        public const float BUILD_INTERVAL = 0.1f;

        public const float WORKER_TRAIN_TIME = 3.0f;
        public const float SOLDIER_TRAIN_TIME = 6.0f;

        // --- CAN VE SALDIRI ---
        public const int WORKER_MAX_HEALTH = 50;
        public const int SOLDIER_MAX_HEALTH = 120;
        public const int BASE_MAX_HEALTH = 5000;
        public const int BUILDING_DEFAULT_HEALTH = 100;
        public const int WALL_MAX_HEALTH = 10000;


        public const float WORKER_MOVE_SPEED = 5.0f;
        public const float SOLDIER_MOVE_SPEED = 2.0f;

        public const int SOLDIER_DAMAGE = 10;
        public const float SOLDIER_ATTACK_RANGE = 1.5f;
        public const float SOLDIER_ATTACK_SPEED = 1.0f;

        public const int TOWER_DAMAGE = 15;
        public const float TOWER_ATTACK_RANGE = 6.0f;
        public const float TOWER_ATTACK_SPEED = 3f;

        // --- POPÜLASYON ---
        public const int POPULATION_BASE = 10;
        public const int POPULATION_HOUSE = 5;

        // --- KAYNAK DEĞERLERİ ---
        public const float RESOURCE_GENERATION_INTERVAL = 1.0f;
        public const int RESOURCE_GENERATION_AMOUNT = 10;
        public const float GATHER_INTERVAL = 1.0f;
        public const int GATHER_AMOUNT = 10;

        // --- BAŞLANGIÇ AYARLARI ---
        public const int START_WOOD = 300;
        public const int START_MEAT = 200;
        public const int START_STONE = 100;
        public const int START_WORKER_COUNT = 1;

        // --- KULLANILMAYANLAR (YORUMA ALINDI) ---
        // public const float UNIT_VIEW_RANGE = 8.0f; 
        // public const int AI_DECISION_TICK_RATE = 5; 
    }
}