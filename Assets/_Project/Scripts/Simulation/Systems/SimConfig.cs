namespace RTS.Simulation.Systems
{
    public static class SimConfig
    {
        public const float TICK_RATE = 0.1f;

        // --- BİNA MALİYETLERİ ---
        public const int HOUSE_COST_WOOD = 250;
        public const int FARM_COST_WOOD = 250;
        public const int WOODCUTTER_COST_MEAT = 0; // İşçiye yemek verip oduncu yapıyoruz gibi düşün
        public const int STONEPIT_COST_WOOD = 250;
        public const int BARRACKS_COST_WOOD = 1000;
        public const int BARRACKS_COST_STONE = 1000;
        public const int TOWER_COST_WOOD = 500;
        public const int TOWER_COST_STONE = 1000;
        public const int WALL_COST_STONE = 50;

        // --- ÜRETİM MALİYETLERİ ---
        public const int WORKER_COST_MEAT = 50;
        public const int WORKER_COST_WOOD = 0;
        public const int WORKER_COST_STONE = 0;

        public const int SOLDIER_COST_MEAT = 200;
        public const int SOLDIER_COST_WOOD = 200; // Kalkan/Mızrak için
        public const int SOLDIER_COST_STONE = 0;

        // --- İNŞAAT SÜRELERİ (İlerleme puanı olarak, her tickte artar) ---
        public const float BUILDING_MAX_PROGRESS = 100f;
        public const float BUILD_AMOUNT_PER_TICK = 5f; // Her tickte %5 ilerler (2 sn sürer)
        public const float BUILD_INTERVAL = 0.1f;

        // --- ÜRETİM SÜRELERİ (Saniye) ---
        public const float WORKER_TRAIN_TIME = 3.0f;
        public const float SOLDIER_TRAIN_TIME = 5.0f;

        // --- CAN DEĞERLERİ ---
        public const int WORKER_MAX_HEALTH = 50;
        public const int SOLDIER_MAX_HEALTH = 120;
        public const int BASE_MAX_HEALTH = 2000;

        // --- HIZ VE SALDIRI ---
        public const float WORKER_MOVE_SPEED = 5.0f;
        public const float SOLDIER_MOVE_SPEED = 4.0f;

        public const int SOLDIER_DAMAGE = 10;
        public const float SOLDIER_ATTACK_RANGE = 1.5f;
        public const float SOLDIER_ATTACK_SPEED = 1.0f;

        public const int TOWER_DAMAGE = 15;
        public const float TOWER_ATTACK_RANGE = 5.0f;
        public const float TOWER_ATTACK_SPEED = 1.5f;

        // --- POPÜLASYON ---
        public const int POPULATION_BASE = 5;
        public const int POPULATION_HOUSE = 5;

        // --- KAYNAK ÜRETİMİ ---
        public const float RESOURCE_GENERATION_INTERVAL = 2.0f; // 2 saniyede bir
        public const int RESOURCE_GENERATION_AMOUNT = 10;

        // --- TOPLAMA (GATHER) ---
        public const float GATHER_INTERVAL = 1.0f;
        public const int GATHER_AMOUNT = 10;
    }
}