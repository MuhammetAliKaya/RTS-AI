// using UnityEngine;
// using System.Collections;
// using System.Collections.Generic;

// /*
//  * UnitVisual.cs
//  * Bu sınıf Logic'i (Unit.cs / Worker.cs) dinler.
//  * Logic "Canım yandı" derse bu sınıf Sprite'ı kırmızı yapar.
//  * Logic "Topladım" derse bu sınıf Sprite'ı sallar.
//  */
// public class UnitVisual : MonoBehaviour
// {
//     [Header("References")]
//     [SerializeField] private GameObject selectionVisual; // Editörden sürükleyebilirsin veya kod bulur

//     private Unit unitLogic;
//     private Worker workerLogic; // Eğer bu bir işçiyse referansını tutacağız
//     private Soldier soldierLogic;
//     private SpriteRenderer spriteRenderer;

//     [Header("Visual Settings")]
//     public List<Color> playerTints; // Renk listesi buraya taşındı
//     private Color originalColor = Color.white;

//     private void Awake()
//     {
//         spriteRenderer = GetComponentInChildren<SpriteRenderer>();
//         if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();

//         // Eğer prefab'de zaten varsa bul, yoksa null kalır
//         if (selectionVisual == null)
//         {
//             // Hiyerarşide adı "SelectionVisual" olan bir çocuk obje varsa bulmayı dene
//             Transform visualTr = transform.Find("SelectionVisual");
//             if (visualTr != null) selectionVisual = visualTr.gameObject;
//         }
//     }

//     private void Start()
//     {
//         // 1. Logic Component'ini bul
//         unitLogic = GetComponent<Unit>();

//         if (unitLogic == null)
//         {
//             Debug.LogError("[UnitVisual] Unit scripti bulunamadı!");
//             return;
//         }

//         if (unitLogic is Soldier)
//         {
//             soldierLogic = (Soldier)unitLogic;
//             soldierLogic.OnAttack += HandleAttackAction;
//         }

//         // 2. Takım Rengini Ayarla (Logic'teki ID'ye göre)
//         SetTeamColor(unitLogic.playerID);

//         // --- EVENT ABONELİKLERİ (Dinleme Modu) ---
//         unitLogic.OnHealthChanged += HandleHealthChanged;
//         unitLogic.OnSelectionChanged += HandleSelectionChanged;
//         unitLogic.OnDeath += HandleDeath;

//         // Eğer bu bir Worker ise, onun özel eventlerine de abone ol
//         if (unitLogic is Worker)
//         {
//             workerLogic = (Worker)unitLogic;
//             workerLogic.OnWorkerAction += HandleWorkerAction;
//         }

//         // Başlangıçta seçim görselini kapat
//         if (selectionVisual != null) selectionVisual.SetActive(false);
//     }

//     private void OnDestroy()
//     {
//         // Obje yok olurken abonelikleri iptal et (Hata almamak için önemli)
//         if (unitLogic != null)
//         {
//             unitLogic.OnHealthChanged -= HandleHealthChanged;
//             unitLogic.OnSelectionChanged -= HandleSelectionChanged;
//             unitLogic.OnDeath -= HandleDeath;
//         }
//         if (soldierLogic != null)
//         {
//             soldierLogic.OnAttack -= HandleAttackAction;
//         }
//         if (workerLogic != null)
//         {
//             workerLogic.OnWorkerAction -= HandleWorkerAction;
//         }
//     }

//     // --- EVENT HANDLERS (Tepkiler) ---

//     private void HandleHealthChanged(int currentHealth)
//     {
//         // Can değişti, kırmızı yanıp sönelim
//         StartCoroutine(FlashRedEffect());
//     }

//     private void HandleAttackAction()
//     {
//         StartCoroutine(AttackThrustEffect());
//     }

//     private void HandleSelectionChanged(bool isSelected)
//     {
//         if (selectionVisual != null)
//         {
//             selectionVisual.SetActive(isSelected);
//         }
//     }

//     private void HandleDeath()
//     {
//         // Ölüm efekti (Particle vs) buraya eklenebilir
//         // Şimdilik sadece renk solsun diyebiliriz veya ses çalabiliriz
//         spriteRenderer.color = Color.grey;
//     }

//     private void HandleWorkerAction(string actionType)
//     {
//         // İşçi bir şey yaptı (Gather veya Build)
//         if (actionType == "Gather")
//         {
//             StartCoroutine(ShakeEffect());
//         }
//         else if (actionType == "Build")
//         {
//             StartCoroutine(ShakeEffect()); // İnşaat için de aynı sallanmayı kullanıyoruz
//         }
//     }

//     // --- GÖRSEL EFEKTLER (Coroutines) ---

//     private IEnumerator FlashRedEffect()
//     {
//         if (spriteRenderer == null) yield break;

//         spriteRenderer.color = Color.red;
//         yield return new WaitForSeconds(0.1f);
//         spriteRenderer.color = originalColor; // Orijinal takım rengine dön
//     }

//     private IEnumerator ShakeEffect()
//     {
//         if (spriteRenderer == null) yield break;

//         // Basit flip efekti (Eski kodunuzdaki mantık)
//         spriteRenderer.flipX = !spriteRenderer.flipX; // Mevcut durumun tersi
//         yield return new WaitForSeconds(0.1f);
//         spriteRenderer.flipX = !spriteRenderer.flipX; // Geri al
//     }

//     // --- YARDIMCI FONKSİYONLAR ---

//     public void SetTeamColor(int playerID)
//     {
//         if (spriteRenderer == null) return;

//         int index = playerID - 1;
//         if (playerTints != null && index >= 0 && index < playerTints.Count)
//         {
//             originalColor = playerTints[index];
//         }
//         else
//         {
//             originalColor = Color.white;
//         }

//         spriteRenderer.color = originalColor;
//     }

//     private IEnumerator AttackThrustEffect()
//     {
//         if (spriteRenderer == null) yield break;

//         float thrustDistance = 0.2f;
//         float duration = 0.1f;
//         // Sprite flipX durumuna göre yön belirle (Sola bakıyorsa -1, Sağa 1 gibi)
//         // Not: flipX true ise genellikle sola bakıyordur, senin assetine göre değişebilir.
//         int direction = spriteRenderer.flipX ? -1 : 1;

//         Vector3 originalPos = transform.position; // Görselin local pozisyonuyla oynamak daha güvenli olabilir ama şimdilik world
//                                                   // Ancak transform.position Logic tarafından yönetiliyor!
//                                                   // Bu yüzden sadece "SpriteRenderer"ın transformu (child ise) oynatılmalı veya
//                                                   // Logic hareket ederken Visual titrememeli.

//         // Basit çözüm: Sprite objesi Logic objesinin altındaysa:
//         Transform spriteTr = spriteRenderer.transform;
//         Vector3 localOrg = spriteTr.localPosition;
//         Vector3 thrustLocal = localOrg + new Vector3(direction * thrustDistance, 0, 0);

//         float timer = 0f;
//         while (timer < duration / 2f)
//         {
//             spriteTr.localPosition = Vector3.Lerp(localOrg, thrustLocal, timer / (duration / 2f));
//             timer += Time.deltaTime;
//             yield return null;
//         }

//         timer = 0f;
//         while (timer < duration / 2f)
//         {
//             spriteTr.localPosition = Vector3.Lerp(thrustLocal, localOrg, timer / (duration / 2f));
//             timer += Time.deltaTime;
//             yield return null;
//         }

//         spriteTr.localPosition = localOrg;
//     }
// }