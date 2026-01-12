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

    // --- KANAL TANIMLARI (TOPLAM 27 KANAL) ---
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
    private const int CH_MY_IDLE_WORKER = 26;

    private const int CH_MY_UNIT_ATTACKING = 27;   // Benim birim saldırıyor mu?
    private const int CH_MY_ENTITY_HEALTH = 28;      // Benim birimimin can yüzdesi (Gradient)

    private const int CH_ENEMY_ENTITY_HEALTH = 29;   // Düşmanın can yüzdesi (Zayıfı seçmek için kritik)

    // Toplam kanal sayısını güncelle
    private const int TOTAL_CHANNELS = 30;

    private int _highlightedUnitIndex = -1;

    public RTSGridSensor(SimWorldState world, SimGridSystem gridSystem, string name = "RTSGridSensor")
    {
        _world = world;
        _gridSystem = gridSystem;
        _name = name;

        int w = (_world != null) ? _world.Map.Width : 32;
        int h = (_world != null) ? _world.Map.Height : 32;

        _shape = new int[] { h, w, TOTAL_CHANNELS };
    }

    public void SetReferences(SimWorldState world, SimGridSystem grid)
    {
        _world = world;
        _gridSystem = grid;
    }

    public void SetSelectedUnitIndex(int index)
    {
        _highlightedUnitIndex = index;
    }

    public string GetName() { return _name; }

    // --- KRİTİK NOKTA 1: Visual Spec ---
    public ObservationSpec GetObservationSpec()
    {
        return ObservationSpec.Visual(_shape[0], _shape[1], _shape[2]);
    }

    // --- KRİTİK NOKTA 2: SIKIŞTIRMAYI KAPATMA ---
    public CompressionSpec GetCompressionSpec()
    {
        // Debug.Log uyarısı ekledim. Unity Console'da bunu görmüyorsanız kod güncellenmemiştir.
        // Debug.Log($"[RTSGridSensor] CompressionSpec called for {_name}. Setting to NONE.");

        // Default() PNG yapar, bu yüzden explicit olarak None (Hiçbiri) seçiyoruz.
        return new CompressionSpec(SensorCompressionType.None);
    }

    public byte[] GetCompressedObservation()
    {
        // Sıkıştırma kapalı olduğu için burası asla çağrılmamalı, null dönüyoruz.
        return null;
    }

    public void Update() { }
    public void Reset() { }

    // --- GÖZLEM YAZMA (RAW DATA) ---
    public int Write(ObservationWriter writer)
    {
        if (_world == null || _world.Map == null) return 0;

        int width = _world.Map.Width;
        int height = _world.Map.Height;

        // 1. ZEMİNİ TARA VE TEMİZLE
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int c = 0; c < TOTAL_CHANNELS; c++) writer[y, x, c] = 0.0f;

                var node = _world.Map.Grid[x, y];
                if (!node.IsWalkable && node.Type != SimTileType.Water) writer[y, x, CH_OBSTACLE] = 1.0f;

                writer[y, x, CH_FOG_OF_WAR] = 1.0f;
            }
        }

        // 2. KAYNAKLAR (YENİ: Miktar Bazlı Gradient)
        foreach (var r in _world.Resources.Values)
        {
            int x = r.GridPosition.x;
            int y = r.GridPosition.y;
            if (x < 0 || x >= width || y < 0 || y >= height) continue;

            // Maksimum kaynak miktarına göre oranla (Örn: 500 max)
            // 500 odun -> 1.0 (Parlak)
            // 50 odun -> 0.1 (Sönük)
            float amountRatio = Mathf.Clamp01((float)r.AmountRemaining / 500f);

            if (r.Type == SimResourceType.Wood) writer[y, x, CH_RES_WOOD] = amountRatio;
            else if (r.Type == SimResourceType.Stone) writer[y, x, CH_RES_STONE] = amountRatio;
            else if (r.Type == SimResourceType.Meat) writer[y, x, CH_RES_FOOD] = amountRatio;
        }

        // 2. BİNALAR
        foreach (var kvp in _world.Buildings)
        {
            var b = kvp.Value;
            if (b.Health <= 0) continue;

            int x = b.GridPosition.x;
            int y = b.GridPosition.y;
            if (x < 0 || x >= width || y < 0 || y >= height) continue;

            bool isMe = (b.PlayerID == 1);
            int channel = -1;

            float healthPct = Mathf.Clamp01((float)b.Health / (float)b.MaxHealth);
            if (isMe)
                writer[y, x, CH_MY_ENTITY_HEALTH] = healthPct; // Benim binamın canı
            else
                writer[y, x, CH_ENEMY_ENTITY_HEALTH] = healthPct; // Düşman binasının canı

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
            float healthPercent = (float)u.Health / (float)u.MaxHealth;
            if (isMe)
            {
                if (u.UnitType == SimUnitType.Soldier) writer[y, x, CH_MY_SOLDIER] = 1.0f;
                else if (u.UnitType == SimUnitType.Worker)
                {
                    writer[y, x, CH_MY_WORKER] = 1.0f;
                    if (u.State == SimTaskType.Idle) writer[y, x, CH_MY_IDLE_WORKER] = 1.0f;
                }
                // --- YENİ: SALDIRI DURUMU ---
                if (u.State == SimTaskType.Attacking)
                {
                    writer[y, x, CH_MY_UNIT_ATTACKING] = 1.0f;
                }
                // --- YENİ: SAĞLIK DURUMU (Gradient) ---
                // Canı %100 ise 1.0, %10 ise 0.1 yazar.
                // Ajan bunu "buradaki değer düşükse birim ölüyor, kaçmalıyım" diye öğrenir.
                writer[y, x, CH_MY_ENTITY_HEALTH] = healthPercent;
            }
            else
            {
                int channel = (u.UnitType == SimUnitType.Soldier) ? CH_ENEMY_SOLDIER : CH_ENEMY_WORKER;
                writer[y, x, channel] = 1.0f;
                writer[y, x, CH_ENEMY_ENTITY_HEALTH] = healthPercent;
            }
        }

        // 4. SEÇİM
        if (_highlightedUnitIndex != -1)
        {
            int hX = _highlightedUnitIndex % width;
            int hY = _highlightedUnitIndex / width;
            if (hX >= 0 && hX < width && hY >= 0 && hY < height)
            {
                writer[hY, hX, CH_SELECTION] = 1.0f;
            }
        }

        return _shape[0] * _shape[1] * _shape[2];
    }
}