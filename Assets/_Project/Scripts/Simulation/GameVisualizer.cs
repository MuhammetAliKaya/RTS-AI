using UnityEngine;
using System.Collections.Generic;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Orchestrator;

public class GameVisualizer : MonoBehaviour
{
    [Header("Yönetici Bağlantısı")]
    public ExperimentManager Manager;

    [Header("İzometrik Ayarlar")]
    public bool IsIsometric = true;
    public float TileWidth = 2.56f;
    public float TileHeight = 1.28f;

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

    // Takip Listeleri
    private Dictionary<int, GameObject> _spawnedObjects = new Dictionary<int, GameObject>();

    // --- YENİLİK: Harita Yenileme Takibi ---
    private int _lastRenderedEpisode = -1;
    private GameObject _currentMapParent; // Zeminleri topluca silmek için referans

    void Update()
    {
        if (Manager == null || Manager.World == null) return;

        // --- HARİTA YENİLEME KONTROLÜ ---
        // Eğer ExperimentManager yeni bir bölüme geçtiyse, zeminleri baştan yarat.
        if (Manager.CurrentEpisode != _lastRenderedEpisode)
        {
            RegenerateMap(Manager.World.Map);
            _lastRenderedEpisode = Manager.CurrentEpisode;
        }

        var world = Manager.World;

        // BİRLİKLER
        foreach (var unit in world.Units.Values)
        {
            GameObject prefab = (unit.UnitType == SimUnitType.Worker) ? WorkerPrefab : SoldierPrefab;
            SyncObject(unit.ID, unit.GridPosition, prefab, true);
        }

        // BİNALAR
        foreach (var b in world.Buildings.Values)
        {
            GameObject prefab = BasePrefab;
            switch (b.Type)
            {
                case SimBuildingType.Base: prefab = BasePrefab; break;
                case SimBuildingType.Barracks: prefab = BarracksPrefab; break;
                case SimBuildingType.Tower: prefab = TowerPrefab; break;
                case SimBuildingType.Wall: prefab = WallPrefab; break;
                case SimBuildingType.Farm: prefab = FarmPrefab; break;
                case SimBuildingType.WoodCutter: prefab = WoodCutterPrefab; break;
                case SimBuildingType.StonePit: prefab = StonePitPrefab; break;
                case SimBuildingType.House: prefab = HousePrefab; break;
            }
            SyncObject(b.ID, b.GridPosition, prefab, false);
        }

        // KAYNAKLAR
        foreach (var r in world.Resources.Values)
        {
            GameObject prefab = ResourceWoodPrefab;
            if (r.Type == SimResourceType.Wood) prefab = ResourceWoodPrefab;
            else if (r.Type == SimResourceType.Stone) prefab = ResourceStonePrefab;
            else if (r.Type == SimResourceType.Meat) prefab = ResourceMeatPrefab;

            SyncObject(r.ID, r.GridPosition, prefab, false);
        }

        CleanupDespawnedObjects(world);
    }

    // --- YENİ HARİTA OLUŞTURMA ---
    void RegenerateMap(SimMapData map)
    {
        // 1. Eski haritayı sil (Eğer varsa)
        if (_currentMapParent != null)
        {
            Destroy(_currentMapParent);
        }

        // 2. Yeni bir taşıyıcı (Parent) oluştur
        _currentMapParent = new GameObject("Map_Tiles");
        _currentMapParent.transform.SetParent(this.transform);

        // 3. Yeni zeminleri döşe
        for (int x = 0; x < map.Width; x++)
        {
            for (int y = 0; y < map.Height; y++)
            {
                SimMapNode node = map.Grid[x, y];
                GameObject prefabToUse = TileGrass;

                switch (node.Type)
                {
                    case SimTileType.Water: prefabToUse = TileWater; break;
                    case SimTileType.Stone: prefabToUse = TileStoneFloor; break;
                    // Orman zeminini veya normal çimi kullan
                    case SimTileType.Forest: prefabToUse = TileForestFloor ?? TileGrass; break;
                    default: prefabToUse = TileGrass; break;
                }

                if (prefabToUse != null)
                {
                    Vector3 pos = GridToWorld(new int2(x, y));
                    GameObject tile = Instantiate(prefabToUse, pos, Quaternion.identity);
                    tile.transform.SetParent(_currentMapParent.transform);

                    // Zemin her zaman en arkada olmalı (-5000)
                    var sr = tile.GetComponent<SpriteRenderer>();
                    if (sr)
                    {
                        sr.sortingOrder = -5000 + (int)(-pos.y * 100);
                    }
                }
            }
        }
    }

    // --- MATEMATİK KÖPRÜSÜ ---
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

    void SyncObject(int id, int2 gridPos, GameObject prefab, bool isMoving)
    {
        if (prefab == null) return;

        Vector3 targetPos = GridToWorld(gridPos);

        if (!_spawnedObjects.ContainsKey(id))
        {
            GameObject newObj = Instantiate(prefab, targetPos, Quaternion.identity);
            newObj.transform.SetParent(this.transform);

            var visualID = newObj.GetComponent<SimEntityVisual>();
            if (visualID == null) visualID = newObj.AddComponent<SimEntityVisual>();
            visualID.ID = id;

            _spawnedObjects.Add(id, newObj);
        }

        GameObject obj = _spawnedObjects[id];
        if (obj == null) return;

        bool isFastMode = (Manager != null && Manager.RunFast);
        float dist = Vector3.Distance(obj.transform.position, targetPos);
        bool shouldTeleport = isFastMode || (dist > 2.0f);

        if (isMoving && !shouldTeleport)
            obj.transform.position = Vector3.Lerp(obj.transform.position, targetPos, Time.deltaTime * 15f);
        else
            obj.transform.position = targetPos;

        var sr = obj.GetComponent<SpriteRenderer>();
        if (sr) sr.sortingOrder = (int)(-obj.transform.position.y * 100);

        Transform ring = obj.transform.Find("SelectionRing");
        if (ring != null)
        {
            bool isSelected = (SimInputManager.Instance != null && SimInputManager.Instance.SelectedUnitID == id);
            if (ring.gameObject.activeSelf != isSelected) ring.gameObject.SetActive(isSelected);
        }

        if (Manager.World.Buildings.TryGetValue(id, out SimBuildingData buildingData))
        {
            if (sr != null)
            {
                Color c = sr.color;
                if (!buildingData.IsConstructed)
                {
                    float progress = buildingData.ConstructionProgress / SimConfig.BUILDING_MAX_PROGRESS;
                    c.a = 0.3f + (progress * 0.7f);
                }
                else c.a = 1.0f;
                sr.color = c;
            }
        }

        if (Manager.World.Resources.TryGetValue(id, out SimResourceData resData))
        {
            if (sr != null)
            {
                Color c = sr.color;
                float maxAmount = 250.0f;
                float percent = (float)resData.AmountRemaining / maxAmount;
                c.a = Mathf.Max(0.2f, percent);
                sr.color = c;
            }
        }
    }

    void CleanupDespawnedObjects(SimWorldState world)
    {
        List<int> idsToRemove = new List<int>();
        foreach (var id in _spawnedObjects.Keys)
        {
            bool exists = world.Units.ContainsKey(id) ||
                          world.Buildings.ContainsKey(id) ||
                          world.Resources.ContainsKey(id);
            if (!exists) idsToRemove.Add(id);
        }
        foreach (var id in idsToRemove)
        {
            Destroy(_spawnedObjects[id]);
            _spawnedObjects.Remove(id);
        }
    }
}