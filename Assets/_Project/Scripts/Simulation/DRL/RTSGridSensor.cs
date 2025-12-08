using UnityEngine;
using Unity.MLAgents.Sensors;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;

public class RTSGridSensor
{
    private SimWorldState _world;
    private SimGridSystem _gridSystem;

    private const float MAX_HP = 500f;
    private const float MAX_RESOURCE_AMOUNT = 5000f;
    private const int TEAM_ME = 1;

    public RTSGridSensor(SimWorldState world, SimGridSystem gridSystem)
    {
        _world = world;
        _gridSystem = gridSystem;
    }

    public void AddGridObservations(VectorSensor sensor)
    {
        int width = _world.Map.Width;
        int height = _world.Map.Height;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var node = _gridSystem.GetNode(x, y);

                // --- ONE-HOT ENCODING KANALLARI (Toplam 9 Kanal) ---
                // Bu değerler, tek bir sayı yerine her özellik için ayrı bir kanal (giriş) oluşturur.
                // AI bu sayede "Bu 0.3 mü yoksa 0.6 mı?" diye matematik yapmak zorunda kalmaz.

                float channel_MyUnit = 0f;
                float channel_EnemyUnit = 0f;
                float channel_MyBuilding = 0f;
                float channel_EnemyBuilding = 0f;
                float channel_ResWood = 0f;
                float channel_ResStone = 0f;
                float channel_ResMeat = 0f;

                float channel_HealthRatio = 0f;
                float channel_ConstructionRatio = 0f; // 1.0 = Tamamlandı, 0.5 = Yarısı bitti

                if (node != null && node.OccupantID != -1)
                {
                    // 1. BİNA
                    if (_world.Buildings.TryGetValue(node.OccupantID, out SimBuildingData b))
                    {
                        if (b.PlayerID == TEAM_ME) channel_MyBuilding = 1f;
                        else channel_EnemyBuilding = 1f;

                        channel_HealthRatio = Mathf.Clamp01((float)b.Health / MAX_HP);
                        channel_ConstructionRatio = b.IsConstructed ? 1f : Mathf.Clamp01(b.ConstructionProgress / 100f);
                    }
                    // 2. ÜNİTE
                    else if (_world.Units.TryGetValue(node.OccupantID, out SimUnitData u))
                    {
                        if (u.PlayerID == TEAM_ME) channel_MyUnit = 1f;
                        else channel_EnemyUnit = 1f;

                        channel_HealthRatio = Mathf.Clamp01((float)u.Health / MAX_HP);
                        channel_ConstructionRatio = 1f; // Üniteler hep tamamlanmıştır
                    }
                    // 3. KAYNAK
                    else if (_world.Resources.TryGetValue(node.OccupantID, out SimResourceData r))
                    {
                        channel_HealthRatio = Mathf.Clamp01((float)r.AmountRemaining / MAX_RESOURCE_AMOUNT);

                        switch (r.Type)
                        {
                            case SimResourceType.Wood: channel_ResWood = 1f; break;
                            case SimResourceType.Stone: channel_ResStone = 1f; break;
                            case SimResourceType.Meat: channel_ResMeat = 1f; break;
                        }
                    }
                }

                // Kanalları sırasıyla ekle (Config dosyasında stacked observation otomatik algılanır)
                sensor.AddObservation(channel_MyUnit);        // 1
                sensor.AddObservation(channel_EnemyUnit);     // 2
                sensor.AddObservation(channel_MyBuilding);    // 3
                sensor.AddObservation(channel_EnemyBuilding); // 4
                sensor.AddObservation(channel_ResWood);       // 5
                sensor.AddObservation(channel_ResStone);      // 6
                sensor.AddObservation(channel_ResMeat);       // 7
                sensor.AddObservation(channel_HealthRatio);   // 8
                sensor.AddObservation(channel_ConstructionRatio); // 9
            }
        }
    }

    public void AddGlobalStats(VectorSensor sensor)
    {
        if (_world.Players.ContainsKey(TEAM_ME))
        {
            var player = _world.Players[TEAM_ME];

            // --- NORMALİZASYON ---
            // Değerleri 0 ile 1 arasına sıkıştırıyoruz. 
            // 5000 kaynak miktarı makul bir üst limit.
            sensor.AddObservation(Mathf.Clamp01(player.Wood / 5000f));
            sensor.AddObservation(Mathf.Clamp01(player.Meat / 5000f));
            sensor.AddObservation(Mathf.Clamp01(player.Stone / 5000f));

            // Nüfus (Max 50 varsayımı)
            sensor.AddObservation(Mathf.Clamp01(player.CurrentPopulation / 50f));

            // Boşta işçi oranı (Çok kritik bir veri)
            int idleWorkers = 0;
            int totalWorkers = 0;
            foreach (var u in _world.Units.Values)
            {
                if (u.PlayerID == TEAM_ME && u.UnitType == SimUnitType.Worker)
                {
                    totalWorkers++;
                    if (u.State == SimTaskType.Idle) idleWorkers++;
                }
            }

            // Eğer hiç işçi yoksa 0, varsa oranı
            float idleRatio = totalWorkers > 0 ? (float)idleWorkers / totalWorkers : 0f;
            sensor.AddObservation(idleRatio);
        }
        else
        {
            // Oyuncu yoksa (Hata durumu)
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
        }

        // Zaman / Step Bilgisi (Normalized)
        sensor.AddObservation(Mathf.Clamp01((float)_world.TickCount / 5000f));
    }
}