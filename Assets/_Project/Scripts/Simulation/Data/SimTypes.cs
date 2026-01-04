using System;

namespace RTS.Simulation.Data
{
    // Koordinat yapımız (Hız için özel struct)
    [Serializable]
    public struct int2 : IEquatable<int2>
    {
        public int x;
        public int y;

        public int2(int x, int y) { this.x = x; this.y = y; }

        // --- EKLENEN KISIM: Matematik Operatörleri ---
        public static int2 operator +(int2 a, int2 b) => new int2(a.x + b.x, a.y + b.y);
        public static int2 operator -(int2 a, int2 b) => new int2(a.x - b.x, a.y - b.y);
        public static int2 operator *(int2 a, int b) => new int2(a.x * b, a.y * b);
        // ---------------------------------------------

        // Eşitlik Kontrolleri
        public static bool operator ==(int2 a, int2 b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(int2 a, int2 b) => !(a == b);
        public bool Equals(int2 other) => this == other;
        public override bool Equals(object obj) => obj is int2 other && this == other;
        public override int GetHashCode() => x ^ y;
        public override string ToString() => $"({x}, {y})";

        public static int2 Zero => new int2(0, 0);
    }

    // Birlik Tipleri
    public enum SimUnitType
    {
        Worker,
        Soldier
    }

    // Bina Tipleri
    public enum SimBuildingType
    {
        None,
        Base,
        Barracks,
        House,
        Farm,
        StonePit,
        WoodCutter,
        Tower,
        Wall
    }

    // Görev Durumları
    public enum SimTaskType
    {
        Idle,
        Moving,
        Gathering,
        Building,
        Training,
        Attacking,
        Dead
    }

    public enum SimResourceType { None, Wood, Stone, Meat }
    public enum SimTileType { Grass, Water, Forest, Stone, MeatBush }
}