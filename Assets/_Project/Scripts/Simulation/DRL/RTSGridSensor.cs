using UnityEngine;
using Unity.MLAgents.Sensors;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;

public class RTSGridSensor
{
    private SimWorldState _world;
    private SimGridSystem _gridSystem;

    private const float MAX_HP = 500f;
    private const float MAX_RESOURCE_AMOUNT = 10000f; // Normalize etmek için
    private const int TEAM_ME = 1; // Bizim Ajanımız (Genellikle ID 1)

    public RTSGridSensor(SimWorldState world, SimGridSystem gridSystem)
    {
        _world = world;
        _gridSystem = gridSystem;
    }

    public void AddGridObservations(VectorSensor sensor)
    {
        int width = _world.Map.Width;
        int height = _world.Map.Height;

        // Grid'i tarıyoruz (CNN için görsel veri oluşturuyoruz)
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var node = _gridSystem.GetNode(x, y);

                // --- GÖZLEM KANALLARI ---
                // Kanal 1: Entity Tipi (0: Boş, 0.3: Bina, 0.6: Ünite, 1.0: Kaynak)
                float entityType = 0f;
                // Kanal 2: Takım Bilgisi (1: Ben, -1: Düşman, 0: Tarafsız/Kaynak)
                float teamInfo = 0f;
                // Kanal 3: Sağlık / Miktar Oranı (0..1 arası)
                float statRatio = 0f;
                // Kanal 4: Kaynak Türü (0: Yok, 0.3: Odun, 0.6: Taş, 1.0: Et)
                float resourceType = 0f;

                if (node != null && node.OccupantID != -1)
                {
                    // 1. DURUM: BİNA MI?
                    if (_world.Buildings.TryGetValue(node.OccupantID, out SimBuildingData b))
                    {
                        entityType = 0.3f; // Bina değeri
                        teamInfo = (b.PlayerID == TEAM_ME) ? 1f : -1f;
                        statRatio = (float)b.Health / MAX_HP;
                    }
                    // 2. DURUM: ÜNİTE Mİ?
                    else if (_world.Units.TryGetValue(node.OccupantID, out SimUnitData u))
                    {
                        entityType = 0.6f; // Ünite değeri
                        teamInfo = (u.PlayerID == TEAM_ME) ? 1f : -1f;
                        statRatio = (float)u.Health / MAX_HP;
                    }
                    // 3. DURUM: KAYNAK MI? (BURASI EKLENDİ)
                    else if (_world.Resources.TryGetValue(node.OccupantID, out SimResourceData r))
                    {
                        entityType = 1.0f; // Kaynak değeri
                        teamInfo = 0f; // Kaynaklar tarafsızdır

                        statRatio = (float)r.AmountRemaining / MAX_RESOURCE_AMOUNT;

                        // Kaynak türünü ayırt etmesi için ekstra bilgi
                        switch (r.Type)
                        {
                            case SimResourceType.Wood: resourceType = 0.3f; break;
                            case SimResourceType.Stone: resourceType = 0.6f; break;
                            case SimResourceType.Meat: resourceType = 1.0f; break;
                        }
                    }
                }

                // Verileri sensöre ekle
                sensor.AddObservation(entityType);
                sensor.AddObservation(teamInfo);
                sensor.AddObservation(statRatio);
                sensor.AddObservation(resourceType); // Yeni kanal
            }
        }
    }

    public void AddGlobalStats(VectorSensor sensor)
    {
        if (_world.Players.ContainsKey(TEAM_ME))
        {
            var player = _world.Players[TEAM_ME];
            // Normalize edilmiş değerler (0-1 arası olması öğrenmeyi hızlandırır)
            sensor.AddObservation(player.Wood / 2000f);
            sensor.AddObservation(player.Meat / 2000f);
            sensor.AddObservation(player.Stone / 2000f);
            sensor.AddObservation((float)player.CurrentPopulation / 20f); // Max 20 pop varsayımı
        }
        else
        {
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
        }
        // Zaman/Step bilgisi (Opsiyonel ama ritmi öğrenmesi için iyi)
        sensor.AddObservation((float)_world.TickCount / 5000f);
    }
}