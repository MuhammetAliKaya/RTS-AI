using UnityEngine;
using Unity.MLAgents.Sensors;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;

public class RTSGridSensor : ISensor
{
    private SimWorldState _world;
    private SimGridSystem _gridSystem;
    private string _name;
    private int[] _shape;

    // --- KANAL TANIMLARI (TOPLAM 25 KANAL) ---
    private const int CH_OBSTACLE = 0;
    private const int CH_RES_WOOD = 1;
    private const int CH_RES_STONE = 2;
    private const int CH_RES_FOOD = 3;

    // Oyuncu 1 (BİZ)
    private const int CH_MY_BASE = 4;
    private const int CH_MY_BARRACKS = 5;
    private const int CH_MY_TOWER = 6;
    private const int CH_MY_WALL = 7;
    private const int CH_MY_HOUSE = 8;
    private const int CH_MY_FARM = 9;
    private const int CH_MY_WOOD_BUILDING = 10;
    private const int CH_MY_STONE_BUILDING = 11;
    private const int CH_MY_WORKER = 12;
    private const int CH_MY_SOLDIER = 13;

    // Oyuncu 2 (DÜŞMAN)
    private const int CH_ENEMY_BASE = 14;
    private const int CH_ENEMY_BARRACKS = 15;
    private const int CH_ENEMY_TOWER = 16;
    private const int CH_ENEMY_WALL = 17;
    private const int CH_ENEMY_HOUSE = 18;
    private const int CH_ENEMY_FARM = 19;
    private const int CH_ENEMY_WOOD_BUILDING = 20;
    private const int CH_ENEMY_STONE_BUILDING = 21;
    private const int CH_ENEMY_WORKER = 22;
    private const int CH_ENEMY_SOLDIER = 23;

    private const int CH_FOG_OF_WAR = 24;

    private const int CH_SELECTION = 25;

    private const int CH_MY_IDLE_WORKER = 26; // Yeni kanal ID'si

    private const int TOTAL_CHANNELS = 27;

    private int _highlightedUnitIndex = -1;

    public RTSGridSensor(SimWorldState world, SimGridSystem gridSystem, string name = "RTSGridSensor")
    {
        _world = world;
        _gridSystem = gridSystem;
        _name = name;

        int w = (_world != null) ? _world.Map.Width : 32;
        int h = (_world != null) ? _world.Map.Height : 32;

        // Shape: [Height, Width, Channels]
        _shape = new int[] { h, w, TOTAL_CHANNELS };
    }

    public void SetReferences(SimWorldState world, SimGridSystem grid)
    {
        _world = world;
        _gridSystem = grid;
    }

    // YENİ: Ajanın seçtiği birimi sensöre bildiren metot
    public void SetSelectedUnitIndex(int index)
    {
        _highlightedUnitIndex = index;
    }

    public string GetName() { return _name; }

    public ObservationSpec GetObservationSpec()
    {
        return ObservationSpec.Visual(_shape[0], _shape[1], _shape[2]);
    }

    public CompressionSpec GetCompressionSpec()
    {
        return CompressionSpec.Default();
    }

    public byte[] GetCompressedObservation()
    {
        return null;
    }

    public void Update() { }
    public void Reset() { }

    // --- GÖZLEM YAZMA ---
    public int Write(ObservationWriter writer)
    {
        // ZeroBuffer yerine manuel temizleme yapıyoruz
        // Aynı döngüde zemin verisini de işleyerek performansı koruyoruz.

        if (_world == null || _world.Map == null) return 0;

        int width = _world.Map.Width;
        int height = _world.Map.Height;

        // 1. ZEMİNİ TARA VE TEMİZLE
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                // Önce o pikseldeki tüm kanalları sıfırla (Clean Slate)
                for (int c = 0; c < TOTAL_CHANNELS; c++)
                {
                    writer[y, x, c] = 0.0f;
                }

                // Sonra Zemin Verisini Yaz
                var node = _world.Map.Grid[x, y];
                if (!node.IsWalkable && node.Type != SimTileType.Water) writer[y, x, CH_OBSTACLE] = 1.0f;

                if (node.Type == SimTileType.Forest) writer[y, x, CH_RES_WOOD] = 1.0f;
                else if (node.Type == SimTileType.Stone) writer[y, x, CH_RES_STONE] = 1.0f;
                else if (node.Type == SimTileType.MeatBush) writer[y, x, CH_RES_FOOD] = 1.0f;

                writer[y, x, CH_FOG_OF_WAR] = 1.0f;
            }
        }

        // 2. BİNALAR
        foreach (var kvp in _world.Buildings)
        {
            var b = kvp.Value;
            if (b.Health <= 0) continue;

            int x = b.GridPosition.x;
            int y = b.GridPosition.y;

            // Sınır kontrolü
            if (x < 0 || x >= width || y < 0 || y >= height) continue;

            bool isMe = (b.PlayerID == 1);
            int channel = -1;

            if (isMe)
            {
                switch (b.Type)
                {
                    case SimBuildingType.Base: channel = CH_MY_BASE; break;
                    case SimBuildingType.Barracks: channel = CH_MY_BARRACKS; break;
                    case SimBuildingType.Tower: channel = CH_MY_TOWER; break;
                    case SimBuildingType.Wall: channel = CH_MY_WALL; break;
                    case SimBuildingType.House: channel = CH_MY_HOUSE; break;
                    case SimBuildingType.Farm: channel = CH_MY_FARM; break;
                    case SimBuildingType.WoodCutter: channel = CH_MY_WOOD_BUILDING; break;
                    case SimBuildingType.StonePit: channel = CH_MY_STONE_BUILDING; break;
                }
            }
            else
            {
                switch (b.Type)
                {
                    case SimBuildingType.Base: channel = CH_ENEMY_BASE; break;
                    case SimBuildingType.Barracks: channel = CH_ENEMY_BARRACKS; break;
                    case SimBuildingType.Tower: channel = CH_ENEMY_TOWER; break;
                    case SimBuildingType.Wall: channel = CH_ENEMY_WALL; break;
                    case SimBuildingType.House: channel = CH_ENEMY_HOUSE; break;
                    case SimBuildingType.Farm: channel = CH_ENEMY_FARM; break;
                    case SimBuildingType.WoodCutter: channel = CH_ENEMY_WOOD_BUILDING; break;
                    case SimBuildingType.StonePit: channel = CH_ENEMY_STONE_BUILDING; break;
                }
            }

            if (channel != -1) writer[y, x, channel] = 1.0f;
        }

        // 3. ÜNİTELER
        foreach (var kvp in _world.Units)
        {
            var u = kvp.Value;
            if (u.State == SimTaskType.Dead) continue;

            int x = u.GridPosition.x;
            int y = u.GridPosition.y;

            if (x < 0 || x >= width || y < 0 || y >= height) continue;

            bool isMe = (u.PlayerID == 1);

            if (isMe)
            {
                if (u.UnitType == SimUnitType.Soldier)
                {
                    // Asker Kanalı
                    writer[y, x, CH_MY_SOLDIER] = 1.0f;
                }
                else if (u.UnitType == SimUnitType.Worker)
                {
                    // Genel İşçi Kanalı (Her zaman görünür)
                    writer[y, x, CH_MY_WORKER] = 1.0f;

                    // EKSTRA: Eğer işçi boşta ise (Idle) parlak göster
                    if (u.State == SimTaskType.Idle)
                    {
                        writer[y, x, CH_MY_IDLE_WORKER] = 1.0f;
                    }
                }
            }
            else
            {
                // Düşman birimleri için standart mantık
                int channel = (u.UnitType == SimUnitType.Soldier) ? CH_ENEMY_SOLDIER : CH_ENEMY_WORKER;
                writer[y, x, channel] = 1.0f;
            }
        }

        // 4. SEÇİM VURGUSU (HIGHLIGHT)
        if (_highlightedUnitIndex != -1)
        {
            // Index'i koordinata çevir
            int hX = _highlightedUnitIndex % width;
            int hY = _highlightedUnitIndex / width;

            if (hX >= 0 && hX < width && hY >= 0 && hY < height)
            {
                // Kanal 25'i boya
                writer[hY, hX, CH_SELECTION] = 1.0f;
            }
        }

        return _shape[0] * _shape[1] * _shape[2];
    }
}