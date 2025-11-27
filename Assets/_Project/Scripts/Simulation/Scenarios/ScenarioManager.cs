using UnityEngine;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;

namespace RTS.Simulation.Scenarios
{
    public class ScenarioManager : MonoBehaviour
    {
        public IScenario CurrentScenario { get; private set; }

        public void LoadScenario(IScenario scenario, SimWorldState world, int seed)
        {
            CurrentScenario = scenario;

            // Random.InitState(seed); // (Eğer deterministik istersen aç)

            // --- TEMİZLİK ---
            world.Units.Clear();
            world.Buildings.Clear();
            world.Resources.Clear();

            // --- DÜZELTME: OYUNCULARI VE KAYNAKLARI SIFIRLA ---
            world.Players.Clear();
            // Oyuncu 1'i varsayılan (boş) haliyle tekrar ekle
            world.Players.Add(1, new SimPlayerData { PlayerID = 1, MaxPopulation = 0, Wood = 0, Stone = 0, Meat = 0 });

            // Haritayı Sıfırla
            ResetGrid(world);

            // Senaryoyu Kur
            scenario.SetupMap(world, seed);
        }

        private void ResetGrid(SimWorldState world)
        {
            for (int x = 0; x < world.Map.Width; x++)
            {
                for (int y = 0; y < world.Map.Height; y++)
                {
                    var node = world.Map.Grid[x, y];
                    node.OccupantID = -1;
                    node.IsWalkable = true;
                    node.Type = SimTileType.Grass;
                }
            }
        }
    }
}