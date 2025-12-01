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

    private Dictionary<int, GameObject> _spawnedObjects = new Dictionary<int, GameObject>();
    private GameObject _currentMapParent;

    void Update()
    {
        // Veriyi Context'ten al
        var world = SimGameContext.ActiveWorld;
        if (world == null) return;

        // Harita yoksa oluştur
        if (_currentMapParent == null)
        {
            RegenerateMap(world.Map);
        }

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
                GameObject prefabToUse = TileGrass;

                switch (node.Type)
                {
                    case SimTileType.Water: prefabToUse = TileWater; break;
                    case SimTileType.Stone: prefabToUse = TileStoneFloor; break;
                    case SimTileType.Forest: prefabToUse = TileForestFloor ?? TileGrass; break;
                    default: prefabToUse = TileGrass; break;
                }

                if (prefabToUse != null)
                {
                    Vector3 pos = GridToWorld(new int2(x, y));
                    GameObject tile = Instantiate(prefabToUse, pos, Quaternion.identity);
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
        float dist = Vector3.Distance(obj.transform.position, targetPos);
        bool shouldTeleport = (dist > 3.0f);

        if (isMoving && !shouldTeleport)
            obj.transform.position = Vector3.Lerp(obj.transform.position, targetPos, Time.deltaTime * 10f);
        else
            obj.transform.position = targetPos;

        var sr = obj.GetComponent<SpriteRenderer>();
        if (sr) sr.sortingOrder = (int)(-obj.transform.position.y * 100);

        // --- 1. SELECTION RING (GERİ GELDİ) ---
        Transform ring = obj.transform.Find("SelectionRing");
        if (ring != null)
        {
            bool isSelected = (SimInputManager.Instance != null && SimInputManager.Instance.SelectedUnitID == id);
            if (ring.gameObject.activeSelf != isSelected)
                ring.gameObject.SetActive(isSelected);
        }

        var world = SimGameContext.ActiveWorld;
        if (world == null) return;

        // --- 2. İNŞAAT GÖRSELİ (GERİ GELDİ) ---
        if (world.Buildings.TryGetValue(id, out SimBuildingData bData))
        {
            if (sr != null)
            {
                Color c = sr.color;
                if (!bData.IsConstructed)
                {
                    // İnşaat %sine göre opaklık (0.3 ile 1.0 arası)
                    float progress = bData.ConstructionProgress / 100f; // MaxProgress genelde 100
                    c.a = 0.3f + (progress * 0.7f);
                }
                else c.a = 1.0f;
                sr.color = c;
            }
        }

        // --- 3. KAYNAK GÖRSELİ (GERİ GELDİ) ---
        if (world.Resources.TryGetValue(id, out SimResourceData rData))
        {
            if (sr != null)
            {
                Color c = sr.color;
                // Kalan miktara göre soluklaşma
                float maxAmount = 250.0f; // Tahmini max değer
                float percent = (float)rData.AmountRemaining / maxAmount;
                c.a = Mathf.Max(0.3f, percent);
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