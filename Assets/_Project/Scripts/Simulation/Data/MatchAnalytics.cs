using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;


[System.Serializable]
public class MatchAnalytics
{
    public AIOpponentType Opponent;
    public bool IsWin;
    public int EpisodeID;

    public int MapSeed;

    // Varlık Dağılımı
    public int TotalWorkersCreated;
    public int TotalSoldiersCreated;
    public int TotalTowersBuilt;

    // Ekonomi (Toplanan vs Harcanan)
    public int TotalWoodGathered, TotalStoneGathered, TotalMeatGathered;
    public int TotalWoodSpent, TotalStoneSpent, TotalMeatSpent;

    // Saldırı Tercihleri (Tür bazlı sayım)
    public Dictionary<string, int> AttackTargets = new Dictionary<string, int>();

    // Isı Haritası (Grid Index -> Tıklanma Sayısı)
    public int[] SourceHeatmap;
    public int[] TargetHeatmap;

    public float MatchDuration;

    public MatchAnalytics(int mapSize)
    {
        SourceHeatmap = new int[mapSize * mapSize];
        TargetHeatmap = new int[mapSize * mapSize];
    }
}