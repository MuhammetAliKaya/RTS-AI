using UnityEngine;
using Unity.MLAgents.Sensors;
using Unity.MLAgents;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;

public class RTSGridSensorComponent : SensorComponent
{
    [Header("Configurations")]
    public string SensorName = "RTSMapSensor"; // YAML'daki ile aynı olmalı!

    // Sensörü burada saklıyoruz
    private RTSGridSensor _sensor;

    // Agent'ın Awake/Start'ında bu fonksiyonu çağırmalısın (RTSAgent.cs içinde yapıyorsun zaten)
    public void InitializeSensor(SimWorldState world, SimGridSystem gridSys)
    {
        // Eğer sensör daha önce oluşturulduysa (CreateSensors çalıştıysa), referanslarını güncelle.
        if (_sensor != null)
        {
            _sensor.SetReferences(world, gridSys);
        }
        else
        {
            // Eğer sensör henüz yoksa (Garip bir durum ama önlem), oluştur.
            // Not: CreateSensors genelde InitializeSensor'dan önce çalışır.
            _sensor = new RTSGridSensor(world, gridSys, SensorName);
        }
    }


    // ML-Agents bu fonksiyonu otomatik çağırır.
    public override ISensor[] CreateSensors()
    {
        // Sensörü İLK KEZ burada oluşturuyoruz.
        // DİKKAT: Henüz world ve gridSys yok, o yüzden null veriyoruz.
        // Ama endişelenme, InitializeSensor bunları sonradan dolduracak.
        // if (_sensor == null)
        // {
        _sensor = new RTSGridSensor(null, null, SensorName);
        // }
        return new ISensor[] { _sensor };
    }

    // Ajan bir birim seçtiğinde bunu görselleştirmek için
    public void SetHighlight(int index)
    {
        if (_sensor != null)
        {
            _sensor.SetSelectedUnitIndex(index);
        }
    }
}