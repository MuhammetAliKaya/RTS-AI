using UnityEngine;
using System.IO;
using System.Text;
using RTS.Simulation.Data;
using System.Globalization; // EKLENDİ: Formatlama için gerekli

public class DataLogger
{
    private string _folderPath;
    private string _generationLogPath;
    private string _bestGenomesLogPath;

    public DataLogger()
    {
        _folderPath = Path.Combine(Application.dataPath, "../PSO_Logs");
        if (!Directory.Exists(_folderPath)) Directory.CreateDirectory(_folderPath);

        string timeStamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        _generationLogPath = Path.Combine(_folderPath, $"Gen_History_{timeStamp}.csv");
        InitializeGenLog();

        _bestGenomesLogPath = Path.Combine(_folderPath, $"Best_Genomes_{timeStamp}.csv");
        InitializeGenomeLog();
    }

    private void InitializeGenLog()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("GenID,BestFitness,AvgFitness,WinRate,TimeTaken,");
        sb.Append("AvgWood,AvgMeat,AvgStone,AvgSoldiers,AvgWorkers");
        File.WriteAllText(_generationLogPath, sb.ToString() + "\n");
    }

    private void InitializeGenomeLog()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("GenID,Fitness,");
        sb.Append("WorkerLim,SoldierLim,AttackThres,DefRatio,BarrackCount,EcoBias,");
        sb.Append("FarmT,WoodT,StoneT,HouseBuf,TowerPos,BaseDef,SaveThres,Bloodlust");
        File.WriteAllText(_bestGenomesLogPath, sb.ToString() + "\n");
    }

    // --- DÜZELTME BURADA ---
    // Kültürden bağımsız (Nokta ondalıklı) formatlama fonksiyonu
    private string F(float val)
    {
        return val.ToString("F2", CultureInfo.InvariantCulture);
    }

    public void LogGeneration(int genID, float bestFit, float avgFit, float winRate, float timeTaken, SimStats avgStats)
    {
        StringBuilder sb = new StringBuilder();

        // Helper fonksiyon F() kullanarak formatlama yapıyoruz
        sb.Append($"{genID},{F(bestFit)},{F(avgFit)},{F(winRate)},{F(timeTaken)},");
        sb.Append($"{F(avgStats.GatheredWood)},{F(avgStats.GatheredMeat)},{F(avgStats.GatheredStone)},");
        sb.Append($"{F(avgStats.SoldierCount)},{F(avgStats.WorkerCount)}");

        File.AppendAllText(_generationLogPath, sb.ToString() + "\n");
    }

    public void LogBestGenome(int genID, float fitness, float[] genes)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append($"{genID},{F(fitness)},");

        for (int i = 0; i < genes.Length; i++)
        {
            sb.Append(F(genes[i])); // Burada da F() kullanıyoruz
            if (i < genes.Length - 1) sb.Append(",");
        }

        File.AppendAllText(_bestGenomesLogPath, sb.ToString() + "\n");
    }
}