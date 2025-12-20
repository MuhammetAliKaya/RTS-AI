using RTS.Simulation.Data;
using UnityEngine;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;

namespace RTS.Simulation.AI
{
    public class HybridAdaptiveAI
    {
        private SimWorldState _world;
        private int _playerID;

        // --- 3 FARKLI STRATEJİ İÇİN GEN HAVUZU ---
        private float[] _ecoGenes;    // Ekonomi odaklı eğitilmiş genler
        private float[] _defGenes;    // Defans odaklı eğitilmiş genler
        private float[] _attackGenes; // Saldırı odaklı eğitilmiş genler

        // O an kullanılan aktif genler
        private float[] _currentGenes;
        private SpecializedMacroAI _aiExecutor; // Genleri uygulayan "Beyin"

        private float _timer;
        private bool _useSwitching; // Anahtarlı mı, anahtarsız mı? (Test için)

        // Mevcut Durum (Raporlama için)
        public string CurrentStateName { get; private set; }
        public float CurrentGSF { get; private set; }

        public HybridAdaptiveAI(SimWorldState world, int playerID,
                                float[] ecoGenes, float[] defGenes, float[] attackGenes,
                                bool useSwitching = true)
        {
            _world = world;
            _playerID = playerID;
            _ecoGenes = ecoGenes;
            _defGenes = defGenes;
            _attackGenes = attackGenes;
            _useSwitching = useSwitching;

            // Başlangıçta Ekonomi genleriyle başla
            _currentGenes = _ecoGenes;
            CurrentStateName = "Economy";

            // SpecializedMacroAI'yi "Motor" olarak kullanıyoruz. 
            // Modu ne olursa olsun, biz ona gen vereceğimiz için "ExecuteParametricBehavior" çalışacak.
            _aiExecutor = new SpecializedMacroAI(world, playerID, _currentGenes, AIStrategyMode.Economic);
        }

        public void Update(float dt)
        {
            // AI motorunu çalıştır (İnşaat, asker basma vs.)
            _aiExecutor.Update(dt);

            // Strateji Değişim Kontrolü (Her 1 saniyede bir kontrol et yeterli)
            _timer += dt;
            if (_timer >= 1.0f)
            {
                _timer = 0;
                if (_useSwitching)
                {
                    EvaluateAndSwitchStrategy();
                }
            }
        }

        private void EvaluateAndSwitchStrategy()
        {
            // 1. GSF Hesapla
            var metrics = SimGameStateAnalyzer.CalculateGSF(_world, _playerID);
            CurrentGSF = metrics.GSF;

            // 2. Eşik Değerlerine Göre Karar Ver
            // Örnek Senaryo:
            // GSF < -80  : Çok zor durumdayım -> DEFANS Moduna geç
            // -80 < GSF < 80 : Durum dengeli -> EKONOMİ/GELİŞİM Moduna geç
            // GSF > 80   : Çok üstünüm -> SALDIRI Moduna geç

            string newState = CurrentStateName;
            float[] newGenes = _currentGenes;

            if (CurrentGSF < -80)
            {
                newState = "Defensive";
                newGenes = _defGenes;
            }
            else if (CurrentGSF > 80)
            {
                newState = "Aggressive";
                newGenes = _attackGenes;
            }
            else
            {
                newState = "Economy";
                newGenes = _ecoGenes;
            }

            // 3. Eğer strateji değiştiyse genleri değiştir
            if (newState != CurrentStateName)
            {
                SwitchGenes(newGenes, newState);
            }
        }

        private void SwitchGenes(float[] targetGenes, string stateName)
        {
            if (SimConfig.EnableLogs)
                Debug.Log($"🔄 HybridAI Switch: {CurrentStateName} -> {stateName} (GSF: {CurrentGSF})");

            _currentGenes = targetGenes;
            CurrentStateName = stateName;

            // Executor'ı yeni genlerle yeniden oluştur veya genleri güncelle
            // (SpecializedMacroAI'yi public bir gen setter ile güncellemek daha performanslı olurdu ama şimdilik yeniden new'leyelim, maliyeti düşük)
            _aiExecutor = new SpecializedMacroAI(_world, _playerID, _currentGenes, AIStrategyMode.Economic);
        }
    }
}