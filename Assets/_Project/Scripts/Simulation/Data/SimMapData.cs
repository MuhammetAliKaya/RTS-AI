namespace RTS.Simulation.Data
{
    [System.Serializable]
    public class SimMapNode
    {
        public int x, y;
        public SimTileType Type;
        public bool IsWalkable;

        // Bu karede duran bir ünite veya bina ID'si (Yoksa -1)
        public int OccupantID = -1;
    }

    [System.Serializable]
    public class SimMapData
    {
        public SimMapNode[,] Grid;
        public int Width;
        public int Height;

        public SimMapData(int w, int h)
        {
            Width = w;
            Height = h;
            Grid = new SimMapNode[w, h];

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    Grid[x, y] = new SimMapNode
                    {
                        x = x,
                        y = y,
                        Type = SimTileType.Grass,
                        IsWalkable = true
                    };
                }
            }
        }

        public bool IsInBounds(int2 pos)
        {
            return pos.x >= 0 && pos.x < Width && pos.y >= 0 && pos.y < Height;
        }
    }
}