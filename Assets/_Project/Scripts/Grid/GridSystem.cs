using UnityEngine;
using System.Collections.Generic;

public class GridSystem
{
    public int width;
    public int height;

    public Node[,] grid;

    public GridSystem(int width, int height)
    {
        this.width = width;
        this.height = height;
        grid = new Node[width, height];
        GenerateRandomMap();
    }

    /// <summary>
    /// UPDATED: Added 'NodeType.MeatBush'.
    /// We rebalanced the probabilities.
    /// </summary>
    private void GenerateRandomMap()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float randomVal = Random.Range(0f, 1f);
                NodeType type;

                // 60% Grass
                if (randomVal < 0.6f)
                {
                    type = NodeType.Grass;
                }
                // 10% Water
                else if (randomVal < 0.7f)
                {
                    type = NodeType.Water;
                }
                // 10% Forest
                else if (randomVal < 0.8f)
                {
                    type = NodeType.Forest;
                }
                // 10% Stone
                else if (randomVal < 0.9f)
                {
                    type = NodeType.Stone;
                }
                // 10% Meat (MeatBush)
                else
                {
                    type = NodeType.MeatBush;
                }

                grid[x, y] = new Node(x, y, type);
            }
        }
    }

    public Node GetNode(int x, int y)
    {
        if (x >= 0 && x < width && y >= 0 && y < height)
        {
            return grid[x, y];
        }
        return null;
    }

    // Clears the spawning zones (Start Points) and makes them walkable.
    public void ClearSpawningZones(Vector2Int p1Pos, Vector2Int p2Pos)
    {
        ClearTileArea(p1Pos.x, p1Pos.y);
        ClearTileArea(p2Pos.x, p2Pos.y);
    }

    private void ClearTileArea(int centerX, int centerY)
    {
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                Node node = GetNode(centerX + x, centerY + y);
                if (node != null)
                {
                    node.type = NodeType.Grass;
                    node.isWalkable = true;
                }
            }
        }
    }
}