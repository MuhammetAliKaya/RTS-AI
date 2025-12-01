// using UnityEngine;
// using System.Diagnostics;

// public class SimulationSpeedTest : MonoBehaviour
// {
//     public int tickCount = 1000;
//     public float simulatedDt = 0.1f;

//     [ContextMenu("Run Speed Test")]
//     public void RunTest()
//     {
//         if (SimulationManager.Instance == null)
//         {
//             UnityEngine.Debug.LogError("SimulationManager not found!");
//             return;
//         }

//         Stopwatch sw = new Stopwatch();
//         sw.Start();

//         for (int i = 0; i < tickCount; i++)
//         {
//             SimulationManager.Instance.Tick(simulatedDt);
//         }

//         sw.Stop();
//         UnityEngine.Debug.Log($"Ran {tickCount} ticks (Simulated time: {tickCount * simulatedDt}s) in {sw.ElapsedMilliseconds}ms real time.");
//     }
// }
