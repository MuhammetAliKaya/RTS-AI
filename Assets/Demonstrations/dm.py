from mlagents_envs.demo_loader import load_demonstration

# ----------------------------------------------------
# LÜTFEN DOSYA YOLUNU GÜNCELLEYİN
# ----------------------------------------------------
DEMO_FILE_PATH = "RTSDemoHuman01_8.demo" # Bu yolu, dosyanın bilgisayarınızdaki tam yoluna güncelleyin.

# DRL Action Translator'daki Eylem Kimlikleri (Referans)
ACTION_MAP = {
    0: "Bekle (NO-OP)",
    1: "EV İNŞA ET (House)",
    2: "KIŞLA İNŞA ET (Barracks)",
    3: "İŞÇİ EĞİT (Train Worker)",
    4: "ASKER EĞİT (Train Soldier)",
    5: "ODUNCU İNŞA ET (Woodcutter)",
    6: "TAŞ OCAĞI İNŞA ET (StonePit)",
    7: "ÇİFTLİK İNŞA ET (Farm)",
    8: "KULE İNŞA ET (Tower) 🚨",
    9: "DUVAR İNŞA ET (Wall) 🚨",
    10: "AKILLI KOMUT (Move/Attack/Gather)"
}

try:
    demo_data = load_demonstration(DEMO_FILE_PATH)
    
    print(f"Demo Dosyası Yüklendi: {DEMO_FILE_PATH}")
    print(f"Toplam Kayıtlı Adım Sayısı: {len(demo_data.behavior_data)}")
    print("-" * 50)
    
    # Eylem Frekanslarını Saymak için
    action_counts = {k: 0 for k in ACTION_MAP.keys()}
    
    # İlk 20 Adımı Detaylı İncele ve Frekansları Say
    for i, step in enumerate(demo_data.behavior_data):
        # Eylem, 3 elemanlı dizinin ilk elemanıdır: actions.DiscreteActions[0]
        # Demo verilerinde eylemler genellikle tek bir satırda tutulur.
        if step.discrete_actions.shape[1] > 0:
            action_type = step.discrete_actions[0, 0]
            
            # Frekansı say
            if action_type in action_counts:
                action_counts[action_type] += 1

            # İlk 20 Adımı yazdır
            if i < 20:
                action_name = ACTION_MAP.get(action_type, f"BİLİNMEYEN ({action_type})")
                source_index = step.discrete_actions[0, 1]
                target_index = step.discrete_actions[0, 2]
                
                print(f"Adım {i + 1: <4} | Eylem ID: {action_type: <3} ({action_name}) | Kaynak: {source_index: <5} | Hedef: {target_index}")
                
    print("-" * 50)
    print("TOPLAM EYLEM FREKANSLARI:")
    
    # Frekansları sırala ve yazdır
    sorted_counts = sorted(action_counts.items(), key=lambda item: item[1], reverse=True)
    
    for action_id, count in sorted_counts:
        name = ACTION_MAP.get(action_id, f"BİLİNMEYEN ({action_id})")
        if count > 0:
            print(f"  {name: <30} : {count} kez")

except Exception as e:
    print(f"HATA: Demo dosyası yüklenemedi veya formatı hatalı. ML-Agents Python kütüphanelerinin kurulu olduğundan emin olun.")
    print(f"Detay: {e}")