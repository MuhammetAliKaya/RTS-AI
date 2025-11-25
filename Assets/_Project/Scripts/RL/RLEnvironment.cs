using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/*
 * RLEnvironment.cs (COMMANDER MODE)
 * * Ajan? gerçek bir oyuncu gibi davranmaya zorlar.
 * - Kaynaklar? matematiksel olarak eklemez.
 * - ??çilere (Worker.cs) gerçek emirler verir.
 * - Oyunun fizi?i ve zamanlamas? neyse ona uyar.
 */

public class RLEnvironment : MonoBehaviour
{
    [Header("RL Training Configuration")]
    public bool isTrainingMode = false;
    public bool hasBuiltBarracks { get; private set; } = false;
    public int maxEpisodesSteps = 1000; // Fiziksel oyun daha yava? oldu?u için ad?m? art?rd?k

    [Header("References")]
    public ResourceManager resourceManager;
    public GameObject workerPrefab; // Sadece Recruit i?lemi için gerekli
    public GameObject barracksPrefab; // Sadece Recruit i?lemi için gerekli

    // Barracks prefab referans?n? Base scripti yönetecek, burada sadece emir veriyoruz.

    // State Takibi
    private int currentStep = 0;
    public bool episodeCompleted = false;
    private Base playerBase;

    // State Encode Ayarlar? (Ajan?n Dünyay? Görme Biçimi)
    // Bunu senin 64'lük state yap?na sad?k kalarak basitle?tirdim
    private readonly int[] woodThresholds = { 0, 50, 100, 200 };
    private readonly int[] stoneThresholds = { 0, 50, 100, 200 };
    private readonly int[] meatThresholds = { 0, 50 };

    void Start()
    {
        if (resourceManager == null && GameManager.Instance != null)
            resourceManager = GameManager.Instance.resourceManager;

        FindPlayerBase();
    }

    private void FindPlayerBase()
    {
        // Sahnede Player 1'e ait üssü bul
        var bases = FindObjectsByType<Base>(FindObjectsSortMode.None);
        playerBase = bases.FirstOrDefault(b => b.playerID == 1);
    }

    // --- 1. GÖZLEM (STATE) ---
    public int GetCurrentState()
    {
        if (resourceManager == null) return 0;

        PlayerResourceData res = resourceManager.GetPlayerResources(1);

        // 1. ??çi Durumu: Hiç i?çim var m?? (Yoksa acil üretmeli)
        int popState = (res.currentPopulation > 0) ? 1 : 0;

        // 2. Kaynak Durumlar?
        int woodState = GetResourceState(res.wood, woodThresholds);
        int stoneState = GetResourceState(res.stone, stoneThresholds);
        int meatState = GetResourceState(res.meat, meatThresholds);

        // State Formülü: (Wood*16) + (Stone*4) + (Meat*2) + Pop
        // Toplam 64 State
        return (woodState * 16) + (stoneState * 4) + (meatState * 2) + popState;
    }

    private int GetResourceState(int amount, int[] thresholds)
    {
        for (int i = thresholds.Length - 1; i >= 0; i--)
            if (amount >= thresholds[i]) return i;
        return 0;
    }

    // --- 2. EYLEM (ACTION) ---
    // Ajan?n beyninden gelen emri, gerçek oyuna uygular.
    public float ExecuteAction(int action)
    {
        if (episodeCompleted) return 0f;

        currentStep++;
        float reward = -0.1f; // Küçük zaman cezas? (H?zl? bitirmesi için)

        // Sahnede Player 1'e ait ve BO?TA (Idle) olan bir i?çi bul
        // Worker.cs'deki IsIdle() fonksiyonunu kullan?yoruz.
        Worker idleWorker = FindIdleWorker();

        switch (action)
        {
            case 0: // Odun Topla
                reward += OrderGather(idleWorker, ResourceType.Wood);
                break;
            case 1: // Ta? Topla
                reward += OrderGather(idleWorker, ResourceType.Stone);
                break;
            case 2: // Et Topla
                reward += OrderGather(idleWorker, ResourceType.Meat);
                break;
            case 3: // ??çi Üret (Recruit)
                reward += OrderRecruitWorker();
                break;
            case 4: // K??la ?n?a Et (Build Barracks)
                reward += OrderBuildBarracks(idleWorker);
                break;
        }

        // Bölüm bitti mi kontrolü (Maksimum ad?m veya Hedef Ba?ar?s?)
        Debug.Log("currentStep" + currentStep + "=>    maxEpisodesSteps" + maxEpisodesSteps);
        if (currentStep >= maxEpisodesSteps) episodeCompleted = true;



        return reward;
    }

    // --- GERÇEK EM?R FONKS?YONLARI ---

    private float OrderGather(Worker worker, ResourceType type)
    {
        // E?er bo?ta i?çi yoksa bu emri veremeyiz (Ceza verilebilir veya nötr geçilir)
        if (worker == null) return -1f; // "Bo?ta i?çin yokken emir verme" cezas?

        // Haritada o türden EN YAKIN kayna?? bul
        ResourceNode targetNode = FindNearestResource(worker.transform.position, type);

        if (targetNode == null) return -1f; // Haritada kaynak kalmam??

        // F?Z?KSEL EYLEM? BA?LAT
        // Bu, senin Worker.cs içindeki Gather fonksiyonunu çal??t?r?r.
        // ??çi pathfinding yapar, yürür, toplar.
        worker.Gather(targetNode);

        // Not: Kaynak hemen gelmedi?i için büyük ödül vermiyoruz.
        // "Do?ru emir verdin" ödülü veriyoruz.
        return 0.1f;
    }

    private float OrderRecruitWorker()
    {
        // Base (Ana Üs) var m??
        if (playerBase == null)
        {
            FindPlayerBase(); // Kaybolduysa tekrar ara
            if (playerBase == null) return -1f;
        }

        // Paran yetiyor mu? (ResourceManager kontrolü)
        // 50 Et laz?m (ResourceData'dan çekebilirsin ama hardcode h?zl?d?r)
        if (!resourceManager.CanAfford(1, 0, 0, 50)) return -1f; // Paran yok

        // Nüfus limitine tak?ld?n m??
        var pData = resourceManager.GetPlayerResources(1);
        if (pData.currentPopulation >= pData.maxPopulation) return -1f;

        // F?Z?KSEL EYLEM: Kayna?? harca ve i?çiyi üret
        // Base scriptindeki fonksiyonu ça??rmak en do?rusu, ama yoksa burada manuel yapal?m:
        // resourceManager.SpendResources(1, 0, 0, 50);
        playerBase.StartTrainingWorker(); // Base.cs içinde bu fonksiyon var, onu tetikliyoruz!

        return 5.0f; // ??çi üretmek iyidir, ödül ver.
    }

    private float OrderBuildBarracks(Worker worker)
    {
        // 1. Zaten K??la Var m?? (Varsa tekrar yapma, ceza ver)
        // Bu kontrolü yapmazsak sürekli k??la dikip puan kasmaya çal???r (Reward Hacking)
        // var existingBarracks = FindObjectsByType<Barracks>(FindObjectsSortMode.None).FirstOrDefault(b => b.playerID == 1);
        // if (existingBarracks != null)
        // {
        //     return -5f; // "Zaten var, yapmana gerek yok" cezas?
        // }

        // 2. ??çi ve Kaynak Kontrolü
        if (worker == null) return -1f;
        if (!resourceManager.CanAfford(1, 180, 180, 0)) return -1f;

        // 3. Yer Bulma
        Node buildNode = FindValidBuildLocation(worker.transform.position);
        if (buildNode == null) return -2f;

        // 4. Harcama ve ?n?aat Emri
        bool success = worker.StartBuildingTask(buildNode, barracksPrefab);

        if (success)
        {
            // --- KR?T?K GÜNCELLEME: HIZLI ?N?A ÖDÜLÜ ---

            // Formül: Sabit Ödül + (Maksimum Ad?m - ?u Anki Ad?m)
            // Örnek: MaxStep=1000. 
            // 50. ad?mda yaparsa: 1000 + (1000-50) = 1950 Puan (ÇOK ?Y?)
            // 900. ad?mda yaparsa: 1000 + (1000-900) = 1100 Puan (?DARE EDER)
            episodeCompleted = true;
            hasBuiltBarracks = true;
            float speedBonus = (maxEpisodesSteps - currentStep);
            float totalReward = 1000.0f + 4 * speedBonus;

            Debug.Log($"KI?LA ?N?A EMR? VER?LD?! Ad?m: {currentStep}, Ödül: {totalReward}");

            return totalReward;
        }
        else
        {

            return -1f;
        }
    }

    private Node FindValidBuildLocation(Vector3 centerPos)
    {
        if (TilemapVisualizer.Instance == null) return null;
        GridSystem grid = TilemapVisualizer.Instance.gridSystem;

        Node centerNode = TilemapVisualizer.Instance.NodeFromWorldPoint(centerPos);
        if (centerNode == null) return null;

        int scanRadius = 4; // Ne kadar uza?a bak?ls?n?

        // Basit kare tarama (?stersen spiral algoritmas?yla de?i?tirebilirsin)
        for (int x = -scanRadius; x <= scanRadius; x++)
        {
            for (int y = -scanRadius; y <= scanRadius; y++)
            {
                // Kendisinin oldu?u kareye yapmas?n, etraf?na baks?n
                if (x == 0 && y == 0) continue;

                Node candidateNode = grid.GetNode(centerNode.x + x, centerNode.y + y);

                if (candidateNode != null)
                {
                    // Kriterler:
                    // 1. Yürünebilir olmal? (isWalkable)
                    // 2. Üzerinde kaynak olmamal? (Node Type kontrolü - Forest/Stone/Water olmamal?)
                    // Not: Senin Grid sisteminde isWalkable=true ise genelde bo?tur.
                    if (candidateNode.isWalkable && candidateNode.type != NodeType.Water)
                    {
                        return candidateNode;
                    }
                }
            }
        }
        return null; // Uygun yer bulunamad?
    }

    // --- YARDIMCILAR ---

    private Worker FindIdleWorker()
    {
        // Sahnede Player 1'e ait tüm i?çileri bul
        var allWorkers = FindObjectsByType<Worker>(FindObjectsSortMode.None);

        foreach (var w in allWorkers)
        {
            // Benim i?çim mi VE Bo?ta m??
            if (w.playerID == 1 && w.IsIdle())
            {
                return w;
            }
        }
        return null;
    }

    private ResourceNode FindNearestResource(Vector3 position, ResourceType type)
    {
        var resources = FindObjectsByType<ResourceNode>(FindObjectsSortMode.None);
        ResourceNode best = null;
        float minDst = float.MaxValue;

        foreach (var r in resources)
        {
            if (r.resourceType == type)
            {
                float d = Vector3.Distance(position, r.transform.position);
                if (d < minDst)
                {
                    minDst = d;
                    best = r;
                }
            }
        }
        return best;
    }

    // --- RESET (YEN? OYUN) ---

    public void ResetEpisode()
    {
        currentStep = 0;
        hasBuiltBarracks = false;
        episodeCompleted = false;

        // 1. Kaynaklar? ve Nüfusu S?f?rla (HATA DÜZELTMES?: MaxPop'u da s?f?rla)
        // ResourceManager'a 'ResetPlayerStats' metodunu ekledi?ini varsay?yorum.
        // Yoksa manuel set et:
        if (resourceManager != null)
        {
            // resourceManager.SetResources(1, 0, 0, 50); // Ba?lang?ç: 50 Et
            // resourceManager.SetPopulation(1, 0); // 0 ??çi

            resourceManager.ResetPlayerForTraining(1);

            // Önemli: Max Population'? da ba?lang?ç de?erine çekmen laz?m
            // Bunu ResourceManager'da public bir de?i?ken veya method ile yapmal?s?n.
            // resourceManager.GetPlayerResources(1).maxPopulation = 5; 
        }

        // 2. Haritay? S?f?rla
        if (TilemapVisualizer.Instance != null)
        {
            if (isTrainingMode) TilemapVisualizer.Instance.spawnEnemy = false;
            TilemapVisualizer.Instance.ResetSimulation();
        }

        // 3. Base'i tekrar bul (Çünkü map resetlendi, eski referans öldü)
        FindPlayerBase();
    }

    public bool IsTerminal() { return episodeCompleted; }
}