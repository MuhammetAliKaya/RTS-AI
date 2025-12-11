import os
import numpy as np

# ML-Agents kütüphanelerini güvenli bir şekilde içe aktar
try:
    from mlagents.trainers.demo_loader import demo_to_buffer
    from mlagents.trainers.buffer import BufferKey
except ImportError:
    print("HATA: 'mlagents' kütüphanesi bulunamadı.")
    print("Lütfen 'pip install mlagents' komutunu çalıştırın.")
    exit()

# --- AYARLAR ---
# Dosya ismini buraya tam olarak yaz (Uzantısı .demo olmalı)
DEMO_PATH = "Assets/Demonstrations/beterDemoTry_8.demo" 
# ----------------

def analyze_demo_final(path):
    if not os.path.exists(path):
        print(f"HATA: Dosya bulunamadı -> {path}")
        return

    print(f"Dosya okunuyor: {path}...")
    
    try:
        # Demoyu yükle
        # behavior_spec: Ajanın özelliklerini tutar
        # buffer: Tüm kayıtlı verileri (pozisyon, aksiyon, ödül vb.) tutar
        behavior_spec, buffer = demo_to_buffer(path, sequence_length=None)
    except Exception as e:
        print(f"Dosya okuma hatası: {e}")
        return

    # Buffer'dan DISCRETE_ACTION (Kesikli Hareketler) verisini al
    if BufferKey.DISCRETE_ACTION not in buffer:
        print("HATA: Demo dosyasında Discrete Action (Kesikli Hareket) bulunamadı!")
        return

    # --- KRİTİK DÜZELTME BURADA ---
    # ML-Agents veriyi 'AgentBufferField' olarak verir. 
    # İşleyebilmek için bunu Numpy dizisine (np.array) çeviriyoruz.
    raw_actions = buffer[BufferKey.DISCRETE_ACTION]
    actions = np.array(raw_actions) 
    # ------------------------------

    total_steps = len(actions)
    print(f"\n--- DEMO ANALİZ SONUCU ---")
    print(f"Veri Tipi: {type(actions)}")
    print(f"Veri Şekli (Shape): {actions.shape}") # Örn: (2675, 3) beklenir
    print(f"Toplam Kaydedilen Adım: {total_steps}")

    # Branch 0: Action Type (0: Bekle, 1: İnşaat, vb.)
    # actions matrisi [Adım Sayısı, Branch Sayısı] şeklindedir.
    # actions[:, 0] -> Tüm adımların 0. dalını (Action Type) alır.
    if actions.ndim > 1:
        action_types = actions[:, 0]
    else:
        # Eğer tek bir branch varsa
        action_types = actions

    # Sayımları yap
    wait_count = np.count_nonzero(action_types == 0)
    real_action_count = total_steps - wait_count
    
    print(f"\nİstatistikler:")
    print(f"  Bekleme (Action 0) Sayısı : {wait_count}")
    print(f"  Gerçek Tıklama Sayısı     : {real_action_count}")
    
    if total_steps > 0:
        ratio = (real_action_count / total_steps) * 100
        print(f"  DOLULUK ORANI: %{ratio:.2f}")
        
        print("-" * 30)
        if ratio < 1.0:
            print("[SONUÇ: KÖTÜ] Veri seti %99 BOŞ (Sadece bekleme var).")
            print("Çözüm: Unity'de kayıt alırken 'DecisionRequester' bileşenini KAPAT.")
        else:
            print("[SONUÇ: İYİ] Veri seti dolu görünüyor. Ajanın öğrenmesi lazım.")
            print("Eğer hala öğrenmiyorsa 'batch_size' değerini demondaki adım sayısına (örn: 128) düşür.")

    # Detaylı Dağılım
    print("\nHangi Aksiyondan Kaç Tane Var?")
    unique, counts = np.unique(action_types, return_counts=True)
    for u, c in zip(unique, counts):
        print(f"  Action ID {u}: {c} adet")

if __name__ == "__main__":
    analyze_demo_final(DEMO_PATH)