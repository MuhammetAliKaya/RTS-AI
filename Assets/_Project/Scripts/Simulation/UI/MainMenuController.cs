using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.EventSystems;

public class MainMenuController : MonoBehaviour
{
    [Header("Player 1 UI")]
    public TMP_Dropdown P1_ControllerDropdown;
    public TMP_Dropdown P1_BotTypeDropdown;

    [Header("Player 2 UI")]
    public TMP_Dropdown P2_ControllerDropdown;
    public TMP_Dropdown P2_BotTypeDropdown;

    [Header("Scene")]
    public string GameSceneName = "DRL_vs_Macro_Game_Scene";

    [Header("UI Root")]
    public GameObject MenuPanel;

    private bool isUpdating = false; // Çoklu güncellemeyi engelle

    void Start()
    {
        // 1. Güvenlik Kontrolleri
        if (P1_ControllerDropdown == null || P2_ControllerDropdown == null)
        {
            Debug.LogError("❌ HATA: Controller Dropdown referansları eksik!");
            return;
        }

        // 2. Dropdownları Doldur
        FillDropdown<PlayerControllerType>(P1_ControllerDropdown);
        FillDropdown<AIOpponentType>(P1_BotTypeDropdown);

        FillDropdown<PlayerControllerType>(P2_ControllerDropdown);
        FillDropdown<AIOpponentType>(P2_BotTypeDropdown);

        // 3. Varsayılan Değerler
        P1_ControllerDropdown.value = (int)PlayerControllerType.Human;
        P2_ControllerDropdown.value = (int)PlayerControllerType.Scripted;

        // 4. Listener Ekle - DEĞİŞTİRİLDİ
        P1_ControllerDropdown.onValueChanged.AddListener(OnP1DropdownChanged);
        P2_ControllerDropdown.onValueChanged.AddListener(OnP2DropdownChanged);

        // 5. İlk açılışta hemen güncelle
        UpdateUIImmediate();
    }

    void OnDestroy()
    {
        // Listener'ları temizle
        if (P1_ControllerDropdown != null)
            P1_ControllerDropdown.onValueChanged.RemoveListener(OnP1DropdownChanged);
        if (P2_ControllerDropdown != null)
            P2_ControllerDropdown.onValueChanged.RemoveListener(OnP2DropdownChanged);
    }

    // YENİ: Dropdown değişikliği için güvenli wrapper
    private void OnP1DropdownChanged(int value)
    {
        if (!isUpdating && this != null)
            StartCoroutine(UpdateUIGeneric());
    }

    private void OnP2DropdownChanged(int value)
    {
        if (!isUpdating && this != null)
            StartCoroutine(UpdateUIGeneric());
    }

    // Gecikmeli UI Güncellemesi - GELİŞTİRİLDİ
    private IEnumerator UpdateUIGeneric()
    {
        if (isUpdating) yield break; // Zaten güncelleme yapılıyorsa çık

        isUpdating = true;

        // Dropdown'ın listesinin kapanmasını bekle
        yield return new WaitForEndOfFrame();

        // Bir frame daha bekle (InputSystem için)
        yield return null;

        UpdateUIImmediate();

        isUpdating = false;
    }

    // Gerçek UI Mantığı - GÜVENLİK KONTROL EKLENDİ
    private void UpdateUIImmediate()
    {
        if (this == null || MenuPanel == null) return;

        // --- PLAYER 1 MANTIĞI ---
        if (P1_ControllerDropdown != null && P1_BotTypeDropdown != null)
        {
            PlayerControllerType p1Type = (PlayerControllerType)P1_ControllerDropdown.value;
            bool showBot = (p1Type == PlayerControllerType.Scripted);

            // GameObject'in hala var olduğunu kontrol et
            if (P1_BotTypeDropdown.gameObject != null)
                P1_BotTypeDropdown.gameObject.SetActive(showBot);
        }

        // --- PLAYER 2 MANTIĞI ---
        if (P2_ControllerDropdown != null && P2_BotTypeDropdown != null)
        {
            PlayerControllerType p2Type = (PlayerControllerType)P2_ControllerDropdown.value;
            bool showBot = (p2Type == PlayerControllerType.Scripted);

            // GameObject'in hala var olduğunu kontrol et
            if (P2_BotTypeDropdown.gameObject != null)
                P2_BotTypeDropdown.gameObject.SetActive(showBot);
        }
    }

    private void FillDropdown<T>(TMP_Dropdown dropdown) where T : Enum
    {
        if (dropdown == null) return;
        dropdown.ClearOptions();
        List<string> options = new List<string>();
        foreach (T item in Enum.GetValues(typeof(T)))
        {
            options.Add(item.ToString());
        }
        dropdown.AddOptions(options);
        dropdown.RefreshShownValue();
    }

    public void OnStartGameClicked()
    {
        StartCoroutine(StartGameRoutine());
    }

    private IEnumerator StartGameRoutine()
    {
        // Listener'ları hemen kaldır
        if (P1_ControllerDropdown != null)
            P1_ControllerDropdown.onValueChanged.RemoveAllListeners();
        if (P2_ControllerDropdown != null)
            P2_ControllerDropdown.onValueChanged.RemoveAllListeners();

        // EventSystem'i kapat
        if (EventSystem.current != null)
            EventSystem.current.enabled = false;

        // Verileri Kaydet
        GameSessionSettings.IsLoadedFromMenu = true;

        GameSessionSettings.P1Controller = (PlayerControllerType)P1_ControllerDropdown.value;
        if (P1_BotTypeDropdown != null && P1_BotTypeDropdown.gameObject.activeSelf)
            GameSessionSettings.P1BotType = (AIOpponentType)P1_BotTypeDropdown.value;
        else
            GameSessionSettings.P1BotType = AIOpponentType.Balanced;

        GameSessionSettings.P2Controller = (PlayerControllerType)P2_ControllerDropdown.value;
        if (P2_BotTypeDropdown != null && P2_BotTypeDropdown.gameObject.activeSelf)
            GameSessionSettings.P2BotType = (AIOpponentType)P2_BotTypeDropdown.value;
        else
            GameSessionSettings.P2BotType = AIOpponentType.Balanced;

        // GameSessionSettings.P2Difficulty = AIDifficulty.Aggressive;

        Debug.Log("🚀 Oyun Yükleniyor...");

        if (MenuPanel != null)
            MenuPanel.SetActive(false);

        yield return new WaitForEndOfFrame();
        SceneManager.LoadScene(GameSceneName);
    }
}