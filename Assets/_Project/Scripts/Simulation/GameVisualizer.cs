using UnityEngine;
using System.Collections.Generic;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core; // SimGameContext

public class GameVisualizer : MonoBehaviour
{
    [Header("İzometrik Ayarlar")]
    public bool IsIsometric = true;
    public float TileWidth = 2.56f;
    public float TileHeight = 1.28f;

    [Header("Görsel Ayarlar")]
    public Color Player2Color = new Color(1f, 0.8f, 0.2f); // Sarımtırak
    public Color DamageFlashColor = Color.red;
    public float AnimationSpeed = 15f; // Titreme hızı

    [Header("Zemin (Tile) Prefabları")]
    public GameObject TileGrass;
    public GameObject TileWater;
    public GameObject TileStoneFloor;
    public GameObject TileForestFloor;

    [Header("Birlik Prefabları")]
    public GameObject WorkerPrefab;
    public GameObject SoldierPrefab;

    [Header("Bina Prefabları")]
    public GameObject BasePrefab;
    public GameObject BarracksPrefab;
    public GameObject TowerPrefab;
    public GameObject WallPrefab;
    public GameObject FarmPrefab;
    public GameObject WoodCutterPrefab;
    public GameObject StonePitPrefab;
    public GameObject HousePrefab;

    [Header("Kaynak Prefabları")]
    public GameObject ResourceWoodPrefab;
    public GameObject ResourceStonePrefab;
    public GameObject ResourceMeatPrefab;

    // --- TAKİP VERİLERİ ---
    private Dictionary<int, GameObject> _spawnedObjects = new Dictionary<int, GameObject>();
    private Dictionary<int, int> _lastKnownHealth = new Dictionary<int, int>(); // Hasar takibi için
    private Dictionary<int, float> _flashTimers = new Dictionary<int, float>(); // Yanıp sönme süresi için

    private GameObject _currentMapParent;

    void Update()
    {
        var world = SimGameContext.ActiveWorld;
        if (world == null) return;

        if (_currentMapParent == null) RegenerateMap(world.Map);

        // BİRLİKLERİ GÜNCELLE
        foreach (var unit in world.Units.Values)
        {
            GameObject prefab = (unit.UnitType == SimUnitType.Worker) ? WorkerPrefab : SoldierPrefab;
            SyncUnit(unit, prefab);
        }

        // BİNALARI GÜNCELLE
        foreach (var b in world.Buildings.Values)
        {
            GameObject prefab = GetBuildingPrefab(b.Type);
            SyncBuilding(b, prefab);
        }

        // KAYNAKLARI GÜNCELLE
        foreach (var r in world.Resources.Values)
        {
            GameObject prefab = GetResourcePrefab(r.Type);
            SyncResource(r, prefab);
        }

        CleanupDespawnedObjects(world);
    }

    // --- ÖZEL SYNC FONKSİYONLARI ---

    void SyncUnit(SimUnitData unit, GameObject prefab)
    {
        GameObject obj = GetOrCreateObject(unit.ID, unit.GridPosition, prefab);
        if (obj == null) return;

        // 1. Pozisyon ve Hareket
        UpdatePosition(obj, unit.GridPosition, true);

        // 2. Animasyon (Gathering / Attacking)
        // İş yapıyorsa "Titreme/Aynalama" efekti ver
        if (unit.State == SimTaskType.Gathering || unit.State == SimTaskType.Attacking)
        {
            float scaleX = Mathf.Sin(Time.time * AnimationSpeed) > 0 ? 1f : -1f;
            obj.transform.localScale = new Vector3(scaleX, 1f, 1f);
        }
        else
        {
            // Normal duruş (Yönüne göre bakabilir ama şimdilik düzeltelim)
            obj.transform.localScale = Vector3.one;
        }

        // 3. Renk ve Hasar Efekti
        UpdateVisualEffects(obj, unit.ID, unit.PlayerID, unit.Health, unit.MaxHealth);

        // 4. Selection Ring
        UpdateSelectionRing(obj, unit.ID);
    }

    void SyncBuilding(SimBuildingData b, GameObject prefab)
    {
        GameObject obj = GetOrCreateObject(b.ID, b.GridPosition, prefab);
        if (obj == null) return;

        UpdatePosition(obj, b.GridPosition, false); // Binalar hareket etmez

        // 1. İnşaat Şeffaflığı
        float alpha = 1.0f;
        if (!b.IsConstructed)
        {
            alpha = 0.3f + (b.ConstructionProgress / 100f * 0.7f);
        }

        // 2. Renk ve Hasar (Alpha'yı koruyarak)
        UpdateVisualEffects(obj, b.ID, b.PlayerID, b.Health, b.MaxHealth, alpha);
    }

    void SyncResource(SimResourceData r, GameObject prefab)
    {
        GameObject obj = GetOrCreateObject(r.ID, r.GridPosition, prefab);
        if (obj == null) return;

        UpdatePosition(obj, r.GridPosition, false);

        // Kaynak azaldıkça soluklaşsın
        float percent = (float)r.AmountRemaining / 250f; // Tahmini max
        float alpha = Mathf.Max(0.3f, percent);

        var sr = obj.GetComponent<SpriteRenderer>();
        if (sr) sr.color = new Color(1f, 1f, 1f, alpha);
    }

    // --- ORTAK GÖRSEL EFEKTLER (CORE LOGIC) ---

    void UpdateVisualEffects(GameObject obj, int id, int playerID, int currentHealth, int maxHealth, float baseAlpha = 1.0f)
    {
        var sr = obj.GetComponent<SpriteRenderer>();
        if (sr == null) return;

        // A. Hasar Kontrolü (Flash Red)
        if (!_lastKnownHealth.ContainsKey(id)) _lastKnownHealth[id] = currentHealth;

        if (currentHealth < _lastKnownHealth[id])
        {
            // Canı azalmış! Flash başlat
            _flashTimers[id] = 0.2f; // 0.2 saniye kırmızı kal
        }
        _lastKnownHealth[id] = currentHealth;

        bool isFlashing = false;
        if (_flashTimers.ContainsKey(id) && _flashTimers[id] > 0)
        {
            _flashTimers[id] -= Time.deltaTime;
            isFlashing = true;
        }

        // B. Temel Renk Belirleme
        Color targetColor = (playerID == 2) ? Player2Color : Color.white;

        // C. Flash Rengi (Kırmızı)
        if (isFlashing) targetColor = DamageFlashColor;

        // D. Can Azalınca Soluklaşma (Health Fade)
        // Can %50'nin altına inince şeffaflaşmaya başlasın, en az %40 görünsün
        float healthPercent = (float)currentHealth / maxHealth;
        float healthAlpha = Mathf.Lerp(0.4f, 1.0f, healthPercent);

        // E. Son Rengi Uygula
        // Hem inşaat durumunu (baseAlpha) hem can durumunu (healthAlpha) birleştiriyoruz
        float finalAlpha = baseAlpha * healthAlpha;

        sr.color = new Color(targetColor.r, targetColor.g, targetColor.b, finalAlpha);
    }

    // --- YARDIMCILAR ---

    void UpdatePosition(GameObject obj, int2 gridPos, bool smooth)
    {
        Vector3 targetPos = GridToWorld(gridPos);

        // Derinlik (Sorting Order)
        var sr = obj.GetComponent<SpriteRenderer>();
        if (sr) sr.sortingOrder = (int)(-targetPos.y * 100);

        // Işınlanma vs Lerp
        float dist = Vector3.Distance(obj.transform.position, targetPos);
        if (smooth && dist < 3.0f)
            obj.transform.position = Vector3.Lerp(obj.transform.position, targetPos, Time.deltaTime * 10f);
        else
            obj.transform.position = targetPos;
    }

    GameObject GetOrCreateObject(int id, int2 gridPos, GameObject prefab)
    {
        if (prefab == null) return null;

        if (!_spawnedObjects.ContainsKey(id))
        {
            Vector3 pos = GridToWorld(gridPos);
            GameObject newObj = Instantiate(prefab, pos, Quaternion.identity);
            newObj.transform.SetParent(this.transform);

            var visualID = newObj.GetComponent<SimEntityVisual>();
            if (visualID == null) visualID = newObj.AddComponent<SimEntityVisual>();
            visualID.ID = id;

            _spawnedObjects.Add(id, newObj);
            return newObj;
        }
        return _spawnedObjects[id];
    }

    void UpdateSelectionRing(GameObject obj, int id)
    {
        Transform ring = obj.transform.Find("SelectionRing");
        if (ring != null)
        {
            bool isSelected = false;

            if (SimInputManager.Instance != null)
            {
                // Hem Ünite hem Bina seçimini kontrol et
                if (SimInputManager.Instance.SelectedUnitID == id) isSelected = true;
                if (SimInputManager.Instance.SelectedBuildingID == id) isSelected = true;
            }

            if (ring.gameObject.activeSelf != isSelected)
                ring.gameObject.SetActive(isSelected);
        }
    }

    // --- PREFAB SEÇİCİLER ---
    GameObject GetBuildingPrefab(SimBuildingType type)
    {
        switch (type)
        {
            case SimBuildingType.Base: return BasePrefab;
            case SimBuildingType.Barracks: return BarracksPrefab;
            case SimBuildingType.Tower: return TowerPrefab;
            case SimBuildingType.Wall: return WallPrefab;
            case SimBuildingType.Farm: return FarmPrefab;
            case SimBuildingType.WoodCutter: return WoodCutterPrefab;
            case SimBuildingType.StonePit: return StonePitPrefab;
            case SimBuildingType.House: return HousePrefab;
            default: return BasePrefab;
        }
    }

    GameObject GetResourcePrefab(SimResourceType type)
    {
        switch (type)
        {
            case SimResourceType.Wood: return ResourceWoodPrefab;
            case SimResourceType.Stone: return ResourceStonePrefab;
            case SimResourceType.Meat: return ResourceMeatPrefab;
            default: return ResourceWoodPrefab;
        }
    }

    // --- HARİTA ---
    public void RegenerateMap(SimMapData map)
    {
        if (_currentMapParent != null) Destroy(_currentMapParent);
        _currentMapParent = new GameObject("Map_Tiles");
        _currentMapParent.transform.SetParent(this.transform);

        for (int x = 0; x < map.Width; x++)
        {
            for (int y = 0; y < map.Height; y++)
            {
                SimMapNode node = map.Grid[x, y];
                GameObject prefab = TileGrass;
                if (node.Type == SimTileType.Water) prefab = TileWater;
                else if (node.Type == SimTileType.Stone) prefab = TileStoneFloor;
                else if (node.Type == SimTileType.Forest) prefab = TileForestFloor;

                if (prefab != null)
                {
                    Vector3 pos = GridToWorld(new int2(x, y));
                    GameObject tile = Instantiate(prefab, pos, Quaternion.identity);
                    tile.transform.SetParent(_currentMapParent.transform);
                    var sr = tile.GetComponent<SpriteRenderer>();
                    if (sr) sr.sortingOrder = -5000 + (int)(-pos.y * 100);
                }
            }
        }
    }

    Vector3 GridToWorld(int2 pos)
    {
        if (IsIsometric)
        {
            float isoX = (pos.x - pos.y) * TileWidth * 0.5f;
            float isoY = (pos.x + pos.y) * TileHeight * 0.5f;
            return new Vector3(isoX, isoY, 0);
        }
        return new Vector3(pos.x, pos.y, 0);
    }

    void CleanupDespawnedObjects(SimWorldState world)
    {
        List<int> toRemove = new List<int>();
        foreach (var id in _spawnedObjects.Keys)
        {
            if (!world.Units.ContainsKey(id) && !world.Buildings.ContainsKey(id) && !world.Resources.ContainsKey(id))
                toRemove.Add(id);
        }
        foreach (var id in toRemove)
        {
            Destroy(_spawnedObjects[id]);
            _spawnedObjects.Remove(id);
            _lastKnownHealth.Remove(id);
            _flashTimers.Remove(id);
        }
    }
    public void ResetVisuals()
    {
        // 1. Önceki harita objesini sil
        if (_currentMapParent != null)
        {
            Destroy(_currentMapParent);
            _currentMapParent = null;
        }

        // 2. Ekranda ne kadar asker/bina varsa sil
        foreach (var obj in _spawnedObjects.Values)
        {
            if (obj != null) Destroy(obj);
        }

        // 3. Listeleri sıfırla
        _spawnedObjects.Clear();
        _lastKnownHealth.Clear();
        _flashTimers.Clear();
    }
}