using RTS.Simulation.Data;

namespace RTS.Simulation.Core
{
    // Bu sınıf, sahnede o an kim patron ise (RL Manager veya Maze Manager)
    // Dünya verisini buraya koyar. Input ve UI buradan okur.
    public static class SimGameContext
    {
        public static SimWorldState ActiveWorld;
    }
}