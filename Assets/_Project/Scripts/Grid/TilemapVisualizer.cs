using UnityEngine;
using UnityEngine.Tilemaps;

public class TilemapVisualizer : MonoBehaviour
{
    public static TilemapVisualizer Instance { get; private set; }

    [Header("Map Logic")]
    public GridSystem gridSystem { get; private set; }
    public AStarPathfinder pathfinder { get; private set; }

    [Header("Map Visuals")]
    public Tilemap groundLayerTilemap;

    [Header("Tile Assets")]
    public Tile grassTile;
    public Tile waterTile;
    public Tile stoneDownTile;
    public Tile forestDownTile;
    public Tile meatDownTile;

    [Header("Object Prefabs")]
    public GameObject forestNodePrefab;
    public GameObject stoneNodePrefab;
    public GameObject meatNodePrefab;
    public Transform objectParent; // All spawned map objects go here

    [Header("Initial Spawns")]
    public GameObject basePrefab;
    public Vector2Int player1StartPos = new Vector2Int(10, 10);
    public Vector2Int player2StartPos = new Vector2Int(40, 40);

    [Header("Training Settings")]
    public bool spawnEnemy = true;

    [Header("Map Dimensions")]
    public int mapWidth = 50;
    public int mapHeight = 50;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        InitializeMap();
    }

    public void Initialize()
    {
        if (gridSystem == null)
        {
            InitializeMap();
        }

        DrawTiles();
        SpawnInitialBases();
    }

    private void InitializeMap()
    {
        gridSystem = new GridSystem(mapWidth, mapHeight);
        gridSystem.ClearSpawningZones(player1StartPos, player2StartPos);
        pathfinder = new AStarPathfinder();
    }

    // --- UPDATED: AGGRESSIVE RESET ---
    /// <summary>
    /// Destroys everything in the scene related to the simulation.
    /// Finds and destroys stragglers (objects not under objectParent).
    /// </summary>
    public void ResetSimulation()
    {
        // 1. Clean objectParent children
        if (objectParent != null)
        {
            foreach (Transform child in objectParent)
            {
                child.gameObject.SetActive(false); // Hide immediately so FindObjects doesn't see them
                Destroy(child.gameObject);
            }
        }

        // 2. Aggressive Cleanup: Find ANY Unit or Building that wasn't under objectParent
        // This catches workers spawned by Bases or buildings started by workers at root level.

        // Clean Units (Workers, Soldiers)
        var allUnits = FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (var u in allUnits)
        {
            if (u != null && u.gameObject != null)
            {
                u.gameObject.SetActive(false); // Remove from logic immediately
                Destroy(u.gameObject);
            }
        }

        // Clean Buildings (Unfinished buildings, Bases, etc.)
        var allBuildings = FindObjectsByType<Building>(FindObjectsSortMode.None);
        foreach (var b in allBuildings)
        {
            if (b != null && b.gameObject != null)
            {
                b.gameObject.SetActive(false);
                Destroy(b.gameObject);
            }
        }

        // Clean Resources (if any spawned outside)
        var allResources = FindObjectsByType<ResourceNode>(FindObjectsSortMode.None);
        foreach (var r in allResources)
        {
            if (r != null && r.gameObject != null)
            {
                r.gameObject.SetActive(false);
                Destroy(r.gameObject);
            }
        }

        // 3. Clear Tilemap
        if (groundLayerTilemap != null)
        {
            groundLayerTilemap.ClearAllTiles();
        }

        // 4. Generate NEW Grid & Map Data
        gridSystem = new GridSystem(mapWidth, mapHeight);
        gridSystem.ClearSpawningZones(player1StartPos, player2StartPos);

        // 5. Redraw and Respawn
        DrawTiles();
        SpawnInitialBases();

        Debug.Log("[TilemapVisualizer] Map and Simulation fully reset (Aggressive Clean).");
    }
    // --------------------------------

    void DrawTiles()
    {
        if (groundLayerTilemap == null || gridSystem == null) return;

        for (int x = 0; x < gridSystem.width; x++)
        {
            for (int y = 0; y < gridSystem.height; y++)
            {
                Node node = gridSystem.GetNode(x, y);
                if (node == null) continue;

                RedrawNode(node);

                Vector3 spawnPosition = WorldPositionFromNode(node);

                switch (node.type)
                {
                    case NodeType.Forest:
                        if (forestNodePrefab != null) Instantiate(forestNodePrefab, spawnPosition, Quaternion.identity, objectParent);
                        break;
                    case NodeType.Stone:
                        if (stoneNodePrefab != null) Instantiate(stoneNodePrefab, spawnPosition, Quaternion.identity, objectParent);
                        break;
                    case NodeType.MeatBush:
                        if (meatNodePrefab != null) Instantiate(meatNodePrefab, spawnPosition, Quaternion.identity, objectParent);
                        break;
                }
            }
        }
    }

    private void SpawnInitialBases()
    {
        if (basePrefab == null) return;

        SpawnBase(player1StartPos, 1, "Player 1 Base");

        if (spawnEnemy)
        {
            SpawnBase(player2StartPos, 2, "Player 2 Base");
        }
    }

    private void SpawnBase(Vector2Int pos, int playerID, string name)
    {
        Node node = gridSystem.GetNode(pos.x, pos.y);
        if (node != null && node.isWalkable)
        {
            GameObject baseObj = Instantiate(basePrefab, WorldPositionFromNode(node), Quaternion.identity, objectParent);
            baseObj.name = name;
            Building buildingComp = baseObj.GetComponent<Building>();
            if (buildingComp != null)
            {
                buildingComp.playerID = playerID;
                buildingComp.ForceCompleteBuild();

                Base baseScript = baseObj.GetComponent<Base>();
                // if (baseScript != null) baseScript.StartTrainingWorker();
            }
        }
    }

    public Node NodeFromWorldPoint(Vector3 worldPosition)
    {
        if (gridSystem == null) return null;
        Vector3Int cellPosition = groundLayerTilemap.WorldToCell(worldPosition);
        return gridSystem.GetNode(cellPosition.x, cellPosition.y);
    }

    public Vector3 WorldPositionFromNode(Node node)
    {
        if (groundLayerTilemap == null) return Vector3.zero;
        return groundLayerTilemap.GetCellCenterWorld(new Vector3Int(node.x, node.y, 0));
    }

    public void RedrawNode(Node node)
    {
        if (node == null) return;
        Vector3Int tilePosition = new Vector3Int(node.x, node.y, 0);

        if (node.type == NodeType.Water) { groundLayerTilemap.SetTile(tilePosition, waterTile); }
        else if (node.type == NodeType.Stone) { groundLayerTilemap.SetTile(tilePosition, stoneDownTile); }
        else if (node.type == NodeType.Forest) { groundLayerTilemap.SetTile(tilePosition, forestDownTile); }
        else if (node.type == NodeType.MeatBush) { groundLayerTilemap.SetTile(tilePosition, meatDownTile); }
        else { groundLayerTilemap.SetTile(tilePosition, grassTile); }
    }

    public Bounds GetMapBounds()
    {
        if (groundLayerTilemap == null) return new Bounds();
        return groundLayerTilemap.localBounds;
    }
}