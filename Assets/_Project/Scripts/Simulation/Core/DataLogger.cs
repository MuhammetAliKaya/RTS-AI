using UnityEngine;
using System.IO;
using System.Text;
using RTS.Simulation.Data; // SimStats için

public class DataLogger
{
    private string _folderPath;
    private string _generationLogPath;
    private string _bestGenomesLogPath;

    public DataLogger()
    {
        // Klasör Yolu: ProjeKlasörü/PSO_Logs/
        _folderPath = Path.Combine(Application.dataPath, "../PSO_Logs");
        if (!Directory.Exists(_folderPath)) Directory.CreateDirectory(_folderPath);

        string timeStamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        // Dosya 1: Jenerasyon Özeti (Grafik çizmek için)
        _generationLogPath = Path.Combine(_folderPath, $"Gen_History_{timeStamp}.csv");
        InitializeGenLog();

        // Dosya 2: En İyi Genler (Analiz için)
        _bestGenomesLogPath = Path.Combine(_folderPath, $"Best_Genomes_{timeStamp}.csv");
        InitializeGenomeLog();
    }

    private void InitializeGenLog()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("GenID,BestFitness,AvgFitness,WinRate,TimeTaken(sec),");
        sb.Append("AvgWood,AvgMeat,AvgStone,AvgSoldiers,AvgWorkers"); // Detaylar
        File.WriteAllText(_generationLogPath, sb.ToString() + "\n");
    }

    private void InitializeGenomeLog()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("GenID,Fitness,");
        // 14 Genin İsimleri
        sb.Append("WorkerLim,SoldierLim,AttackThres,DefRatio,BarrackCount,EcoBias,");
        sb.Append("FarmT,WoodT,StoneT,HouseBuf,TowerPos,BaseDef,SaveThres,Bloodlust");
        File.WriteAllText(_bestGenomesLogPath, sb.ToString() + "\n");
    }

    public void LogGeneration(int genID, float bestFit, float avgFit, float winRate, float timeTaken, SimStats avgStats)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append($"{genID},{bestFit:F2},{avgFit:F2},{winRate:F2},{timeTaken:F2},");
        sb.Append($"{avgStats.GatheredWood},{avgStats.GatheredMeat},{avgStats.GatheredStone},");
        sb.Append($"{avgStats.SoldierCount},{avgStats.WorkerCount}");

        File.AppendAllText(_generationLogPath, sb.ToString() + "\n");
    }

    public void LogBestGenome(int genID, float fitness, float[] genes)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append($"{genID},{fitness:F2},");

        for (int i = 0; i < genes.Length; i++)
        {
            sb.Append($"{genes[i]:F2}");
            if (i < genes.Length - 1) sb.Append(",");
        }

        File.AppendAllText(_bestGenomesLogPath, sb.ToString() + "\n");
    }
}