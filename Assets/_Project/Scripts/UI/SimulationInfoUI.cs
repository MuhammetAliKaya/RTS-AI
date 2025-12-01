// using UnityEngine;
// using TMPro;

// public class SimulationInfoUI : MonoBehaviour
// {
//     [Header("UI References")]
//     public TextMeshProUGUI infoText;

//     private float updateRate = 0.2f; // Update UI 5 times a second
//     private float timer = 0f;

//     void Update()
//     {
//         timer += Time.unscaledDeltaTime;
//         if (timer >= updateRate)
//         {
//             timer = 0f;
//             UpdateUI();
//         }
//     }

//     void UpdateUI()
//     {
//         if (infoText == null) return;
//         if (SimulationManager.Instance == null)
//         {
//             infoText.text = "SimulationManager not found!";
//             return;
//         }

//         int tps = SimulationManager.Instance.TicksPerSecond;
//         float simSpeed = SimulationManager.Instance.SimulatedTimePerRealSecond;
//         float realFPS = 1.0f / Time.unscaledDeltaTime;

//         infoText.text = $"<b>Simulation Stats</b>\n" +
//                         $"FPS: {realFPS:F0}\n" +
//                         $"TPS: {tps}\n" +
//                         $"Speed: {simSpeed:F1}x";

//         // Color coding based on speed
//         if (simSpeed > 1.5f) infoText.color = Color.green;
//         else if (simSpeed < 0.8f) infoText.color = Color.yellow;
//         else infoText.color = Color.white;
//     }
// }
