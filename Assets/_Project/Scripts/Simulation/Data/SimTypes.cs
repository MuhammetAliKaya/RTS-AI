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

        // Eşitlik Kontrolleri
        public static bool operator ==(int2 a, int2 b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(int2 a, int2 b) => !(a == b);
        public bool Equals(int2 other) => this == other;
        public override bool Equals(object obj) => obj is int2 other && this == other;
        public override int GetHashCode() => x ^ y;
        public override string ToString() => $"({x}, {y})";

        public static int2 Zero => new int2(0, 0);
    }

    // Birlik Tipleri (Enemy yok, PlayerID ile ayrılacak)
    public enum SimUnitType
    {
        Worker,
        Soldier
    }

    // Bina Tipleri (Kule ve Duvar eklendi)
    public enum SimBuildingType
    {
        None,
        Base,
        Barracks,
        House,
        Farm,
        StonePit,
        WoodCutter,
        Tower,      // Savunma Kulesi
        Wall        // Duvar
    }

    // Görev Durumları (Savaş/Combat eklendi)
    public enum SimTaskType
    {
        Idle,
        Moving,
        Gathering,
        Building,
        Training,   // Üretim yapıyor
        Attacking,  // Savaşıyor
        Dead        // Öldü (Silinmeyi bekliyor)
    }

    public enum SimResourceType { None, Wood, Stone, Meat }
    public enum SimTileType { Grass, Water, Forest, Stone, MeatBush }
}