using UnityEngine;
using System.Linq;
using System.Collections.Generic;

/*
 * ReferenceAIController.cs (GÜNCELLENMİŞ - Kule ve Ordu Takviyeli)
 *
 * YENİ GÖREVLER:
 * 1. Ekonomiyi kur (İşçiler).
 * 2. Kışla (Barracks) inşa et.
 * 3. Temel savunma için birkaç Asker (Soldier) üret.
 * 4. Üssün etrafına Savunma Kuleleri (Defense Tower) inşa et.
 * 5. Orduyu maksimum savunma kapasitesine kadar büyütmeye devam et.
 */
public class ReferenceAIController : MonoBehaviour
{
    [Header("Settings")]
    public float decisionRate = 3f;
    public int playerID = 1;

    [Header("Strategy Goals")]
    public int desiredWorkers = 6;    // Daha güçlü ekonomi için artırdık
    public int desiredDefenders = 10; // Daha kalabalık bir ordu
    public int desiredTowers = 4;     // Üssün 4 tarafına kule

    [Header("Prefabs")]
    public GameObject barracksPrefab;
    public GameObject defenseTowerPrefab; // YENİ: Kule Prefabı

    private float decisionTimer = 0f;
    private Base aiBase;
    private ResourceManager resourceManager;
    private GridSystem gridSystem;

    public void Initialize()
    {
        if (GameManager.Instance != null)
        {
            resourceManager = GameManager.Instance.resourceManager;
            if (TilemapVisualizer.Instance != null)
            {
                gridSystem = TilemapVisualizer.Instance.gridSystem;
            }
        }

        aiBase = FindObjectsByType<Base>(FindObjectsSortMode.None).FirstOrDefault(b => b.playerID == playerID);
        if (aiBase == null) Debug.LogError($"[Ref AI] Player {playerID} Base bulunamadı!");
    }

    public void RunAI()
    {
        if (aiBase == null || !aiBase.isFunctional) return;

        decisionTimer += Time.deltaTime;
        if (decisionTimer < decisionRate) return;
        decisionTimer = 0;

        MakeDecision();
    }

    private void MakeDecision()
    {
        // --- DURUM ANALİZİ ---
        List<Unit> myUnits = FindObjectsByType<Unit>(FindObjectsSortMode.None).Where(u => u.playerID == playerID && u.currentHealth > 0).ToList();
        List<Building> myBuildings = FindObjectsByType<Building>(FindObjectsSortMode.None).Where(b => b.playerID == playerID).ToList();

        int workerCount = myUnits.Count(u => u is Worker);
        int soldierCount = myUnits.Count(u => u is Soldier);

        Barracks myBarracks = myBuildings.FirstOrDefault(b => b is Barracks && b.isFunctional) as Barracks;
        int towerCount = myBuildings.Count(b => b is DefenseTower);

        Worker idleWorker = myUnits.Where(u => u is Worker).Cast<Worker>().FirstOrDefault(w => w.IsIdle());
        bool hasPopRoom = resourceManager.GetPlayerResources(playerID).currentPopulation < resourceManager.GetPlayerResources(playerID).maxPopulation;

        // --- KARAR ÖNCELİKLERİ (TURTLE STRATEGY - KAPLUMBAĞA STRATEJİSİ) ---

        // 1. ACİL EKONOMİ: Hiç işçi yoksa veya çok azsa, her şeyden önce işçi bas.
        if (workerCount < 3 && hasPopRoom)
        {
            if (TryTrainWorker()) return;
        }

        // 2. KIŞLA İNŞAATI: İşler rayına girdiğinde ilk bina.
        if (myBarracks == null && workerCount >= 3 && idleWorker != null)
        {
            if (TryBuildBuilding(idleWorker, barracksPrefab, 4)) return;
        }

        // 3. TEMEL SAVUNMA: Kışla varsa, hemen birkaç asker bas (Kulelerden önce).
        if (myBarracks != null && soldierCount < 3 && hasPopRoom)
        {
            if (TryTrainSoldier(myBarracks)) return;
        }

        // 4. EKONOMİYİ BÜYÜT: Temel savunma varken işçi sayısını hedefe ulaştır.
        if (workerCount < desiredWorkers && hasPopRoom)
        {
            if (TryTrainWorker()) return;
        }

        // 5. KULE SAVUNMASI: Ekonomi tamam, şimdi üssü sağlamlaştır.
        if (towerCount < desiredTowers && idleWorker != null)
        {
            // Kuleleri üsten biraz daha uzağa (örn: 5-8 birim) kurmaya çalış
            if (TryBuildBuilding(idleWorker, defenseTowerPrefab, Random.Range(5, 9))) return;
        }

        // 6. ORDUYU TAMAMLA: Kuleler de bitince asker sayısını hedefe ulaştır.
        if (myBarracks != null && soldierCount < desiredDefenders && hasPopRoom)
        {
            if (TryTrainSoldier(myBarracks)) return;
        }

        // 7. KAYNAK TOPLAMA (Boşta kalan herkes çalışsın)
        if (idleWorker != null)
        {
            ManageGathering(idleWorker);
        }
    }

    // --- YARDIMCI EYLEM FONKSİYONLARI ---

    private bool TryTrainWorker()
    {
        if (resourceManager.CanAfford(playerID, 0, 0, aiBase.workerProductionCost.meatCost))
        {
            aiBase.StartTrainingWorker();
            return true;
        }
        return false;
    }

    private bool TryTrainSoldier(Barracks barracks)
    {
        if (resourceManager.CanAfford(playerID, barracks.soldierProductionCost.woodCost, barracks.soldierProductionCost.stoneCost, barracks.soldierProductionCost.meatCost))
        {
            barracks.StartTrainingSoldier();
            return true;
        }
        return false;
    }

    private bool TryBuildBuilding(Worker worker, GameObject buildingPrefab, int radius)
    {
        Building prefabCost = buildingPrefab.GetComponent<Building>();
        if (resourceManager.CanAfford(playerID, prefabCost.cost.woodCost, prefabCost.cost.stoneCost, prefabCost.cost.meatCost))
        {
            // Kaynağı burada harcamıyoruz, Worker (İşçi) inşaata başlayınca harcanacak 
            // (Eğer Worker.cs'inizi 'StartBuildingTask' içinde kaynak harcamıyorsa,
            // ReferenceAI (Referans Yapay Zeka) bedava bina yapıyor olabilir. Bunu kontrol etmek lazım.)
            // DÜZELTME: BuildingPlacer.cs kaynak harcıyor, Worker.cs harcamıyor.
            // Bu yüzden Referans AI (Yapay Zeka) için burada manuel harcama yapmalıyız:

            if (resourceManager.SpendResources(playerID, prefabCost.cost.woodCost, prefabCost.cost.stoneCost, prefabCost.cost.meatCost))
            {
                worker.StartBuildingTask(FindDefensiveBuildPosition(radius), buildingPrefab);
                return true;
            }
        }
        return false;
    }

    private void ManageGathering(Worker worker)
    {
        // Basitçe en yakın ODUN veya TAŞ kaynağına gönder
        ResourceNode resource = FindObjectsByType<ResourceNode>(FindObjectsSortMode.None)
            .FirstOrDefault(r => (r.resourceType == ResourceType.Wood || r.resourceType == ResourceType.Stone) && Worker.CheckIfGatherable(r));

        if (resource != null)
        {
            worker.Gather(resource);
        }
    }

    private Node FindDefensiveBuildPosition(int radius)
    {
        if (gridSystem == null || aiBase == null) return null;
        Node baseNode = TilemapVisualizer.Instance.NodeFromWorldPoint(aiBase.transform.position);

        for (int i = 0; i < 50; i++)
        {
            // Üssün etrafında rastgele bir yer (belirtilen yarıçapta)
            int x = baseNode.x + Random.Range(-radius, radius + 1);
            int y = baseNode.y + Random.Range(-radius, radius + 1);

            Node node = gridSystem.GetNode(x, y);
            // Yürünebilir, çimen ve üsse çok yakın olmayan bir yer
            if (node != null && node.isWalkable && node.type == NodeType.Grass)
            {
                if (Vector2Int.Distance(new Vector2Int(baseNode.x, baseNode.y), new Vector2Int(x, y)) > 2)
                {
                    return node;
                }
            }
        }
        return null;
    }
}