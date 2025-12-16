using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using RTS.Simulation.Core;
using RTS.Simulation.Data;
using RTS.Simulation.Systems; // SimConfig vb. için
using System.Linq;

public class UnitSelectionAgent : Agent
{
    private RTSOrchestrator _orchestrator;
    private SimWorldState _world;
    private DRLActionTranslator _translator;

    // Gözlemleri normalize etmek için sabitler
    private const float MAX_RES = 1000f;
    private const float MAX_POP = 50f;
    private const float MAX_UNIT_COUNT = 30f;

    public void Setup(RTSOrchestrator orchestrator, SimWorldState world, DRLActionTranslator translator)
    {
        _orchestrator = orchestrator;
        _world = world;
        _translator = translator;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 1. Veri Güvenliği
        if (_world == null || !_world.Players.ContainsKey(_orchestrator.MyPlayerID))
        {
            // Veri yoksa boş gözlem bas (6 adet float)
            for (int i = 0; i < 6; i++) sensor.AddObservation(0f);
            return;
        }

        var me = _world.Players[_orchestrator.MyPlayerID];

        // 2. Kaynak Durumu (Ekonomi ne durumda?)
        sensor.AddObservation(me.Wood / MAX_RES);
        sensor.AddObservation(me.Stone / MAX_RES);
        sensor.AddObservation(me.Meat / MAX_RES);
        sensor.AddObservation(me.CurrentPopulation / MAX_POP);

        // 3. Ordu/İşçi Sayımı (Neye ihtiyacım var?)
        // LINQ sorguları her frame biraz maliyetli olabilir ama ML-Agents için kabul edilebilir.
        // Daha optimize olması için bu sayılar Orchestrator'da tutulup buraya paslanabilir.
        int workerCount = 0;
        int soldierCount = 0;

        foreach (var u in _world.Units.Values)
        {
            if (u.PlayerID == _orchestrator.MyPlayerID && u.State != SimTaskType.Dead)
            {
                if (u.UnitType == SimUnitType.Worker) workerCount++;
                else if (u.UnitType == SimUnitType.Soldier) soldierCount++;
            }
        }

        sensor.AddObservation(workerCount / MAX_UNIT_COUNT);
        sensor.AddObservation(soldierCount / MAX_UNIT_COUNT);
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        if (_world == null || _translator == null) return;

        int totalMapSize = _world.Map.Width * _world.Map.Height;
        int myID = _orchestrator.MyPlayerID;

        for (int i = 0; i < totalMapSize; i++)
        {
            bool canSelect = false;

            // 1. Bu karede bana ait bir şey (Birim veya Bina) var mı?
            if (_translator.IsUnitOwnedByPlayer(i, myID))
            {
                // Birim mi Bina mı ayırt etmemiz lazım.
                var unit = _translator.GetUnitAtPosIndex(i);

                if (unit != null)
                {
                    // --- BİRİM KONTROLÜ ---
                    // Sadece "Boşta" (Idle) olan birimler seçilebilir.
                    // Eğer kaynak topluyorsa veya inşa ediyorsa, onları rahatsız etme.
                    // (Ancak stratejine göre toplama yapanları da seçmek isteyebilirsin, şimdilik kapatıyoruz)
                    if (unit.State == SimTaskType.Idle)
                    {
                        canSelect = true;
                    }
                }
                else
                {
                    // --- BİNA KONTROLÜ ---
                    // Translator "IsUnitOwned" true döndürdü ama "GetUnit" null döndürdüyse
                    // bu kesinlikle bir binadır.
                    // Binalar (Base, Barracks) her zaman üretim için seçilebilir.
                    // İsterseniz sadece inşaatı bitmiş (IsConstructed) binaları kontrol edebilirsiniz.
                    // (Translator içinde bu kontrol kısmen var ama burada garantiye alalım)
                    SimBuildingType bType = _translator.GetBuildingTypeAt(i);
                    if (bType != SimBuildingType.None)
                    {
                        canSelect = true;
                    }
                }
            }

            // Maskeyi uygula
            actionMask.SetActionEnabled(0, i, canSelect);
        }

        // ÖNEMLİ: Eğer haritada seçecek HİÇBİR ŞEY yoksa (tüm işçiler meşgul, bina yok vb.)
        // Ajanın hata vermemesi için "hiçbir şey yapma" seçeneği olmadığı için (seçim zorunlu),
        // Geçici olarak 0. indeksi açabiliriz veya Orchestrator bu durumu handle etmeli.
        // Şimdilik ML-Agents çökmesin diye en az 1 tane true olduğundan emin olmak iyi bir pratiktir 
        // ama maskeleme mantığı doğruysa gerek kalmaz.
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // Karar verilen grid indeksi (0 ile MapSize-1 arası)
        int sourceIndex = actions.DiscreteActions[0];

        // Orkestratöre bildir: "Ben bu birimi seçtim, sıra ActionSelectionAgent'ta."
        _orchestrator.OnUnitSelected(sourceIndex);
    }
}