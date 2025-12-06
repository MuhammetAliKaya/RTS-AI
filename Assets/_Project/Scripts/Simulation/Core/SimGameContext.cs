using RTS.Simulation.Data;
using RTS.Simulation.Systems; // Sistemlerin olduğu namespace

namespace RTS.Simulation.Core
{
    // MEVCUT STATİK YAPI KORUNUYOR
    public static class SimGameContext
    {
        public static SimWorldState ActiveWorld;

        // --- YENİ EKLENEN KISIM: Instance (Çoklu Eğitim İçin) ---
        public class ContextInstance
        {
            public SimWorldState World;
            public SimGridSystem GridSystem;
            public SimUnitSystem UnitSystem;
            public SimBuildingSystem BuildingSystem;
            public SimResourceSystem ResourceSystem;

            // Bu constructor, her yeni simülasyon (thread) için taze bir dünya kurar
            public ContextInstance(SimWorldState world)
            {
                World = world;

                // Sistemleri bu "world" ile başlatıyoruz.
                // NOT: Aşağıdaki sistemlerin constructor'larını Adım 2'de güncelleyeceğiz.
                GridSystem = new SimGridSystem(world);
                UnitSystem = new SimUnitSystem(world);
                BuildingSystem = new SimBuildingSystem(world);
                ResourceSystem = new SimResourceSystem(world);
            }
        }
    }
}