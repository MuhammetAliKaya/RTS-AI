import os
import numpy as np

try:
    from mlagents.trainers.demo_loader import demo_to_buffer
    from mlagents.trainers.buffer import BufferKey
except ImportError:
    print("HATA: 'mlagents' kütüphanesi bulunamadı. (pip install mlagents)")
    exit()

# --- AYARLAR ---
DEMO_PATH = "Assets/Demonstrations/tr3.demo"  # Dosya yolunu buraya yazın
# ----------------

def analyze_rts_demo(path):
    if not os.path.exists(path):
        print(f"HATA: Dosya bulunamadı -> {path}")
        return

    print(f"Dosya analiz ediliyor: {path}...")
    
    try:
        # Demoyu yükle
        _, buffer = demo_to_buffer(path, sequence_length=None)
        
        # Kesikli hareketleri al ve düzleştir (Flatten)
        # Bu işlem (N, 1) şeklindeki veriyi tek boyutlu listeye çevirir.
        raw_actions = buffer[BufferKey.DISCRETE_ACTION]
        actions = np.array(raw_actions).flatten()
        
    except Exception as e:
        print(f"Demo okuma hatası: {e}")
        return

    total_steps = len(actions)
    print(f"Toplam Kayıtlı Adım (Frame): {total_steps}")
    print("-" * 60)
    print(f"{'SIRA':<6} {'SOURCE (Kim?)':<15} {'ACTION (Ne?)':<15} {'TARGET (Nereye?)':<15}")
    print("-" * 60)

    # --- 3'LÜ YAPIYI TESPİT ETME ALGORİTMASI ---
    # Mantık: Kayıt akışında [..., S, A, T, ...] şeklinde bir desen arıyoruz.
    # 'A' (Action) genellikle 1 ile 12 arasında bir sayıdır (0 'Bekle'dir).
    # Eğer bir sayı 1-12 arasındaysa, kendinden önceki 'Source', sonraki 'Target' olabilir.
    
    detected_count = 0
    i = 1 # 1'den başlayıp sondan bir önceye kadar döneceğiz
    
    while i < total_steps - 1:
        current_val = actions[i]
        prev_val = actions[i-1]
        next_val = actions[i+1]

        # KURAL: Eğer şu anki değer geçerli bir Aksiyon ID ise (1-12 arası)
        # VE önceki değer bir 'Source' (Genelde büyük bir grid indeksi veya 0) ise
        # VE bir önceki tespit ettiğimiz aksiyonla çakışmıyorsa
        
        is_valid_action = (1 <= current_val <= 12)
        
        # Grid indexleri genelde 0'dan başlar ama 1-12 aralığına denk gelebilir.
        # Bu yüzden sadece 'is_valid_action' kontrolü yapacağız ve i'yi atlatacağız.
        
        if is_valid_action:
            # Action isimlerini düzeltiyoruz (Sıralama Önemli!)
            action_name = f"{current_val}"
            
            if current_val == 10: action_name = "10 (ATTACK)"
            elif current_val == 11: action_name = "11 (MOVE)"
            elif current_val == 12: action_name = "12 (GATHER)"
            elif current_val == 3: action_name = "3 (TRAIN WORKER)" # Önce özel durumlar
            elif current_val == 4: action_name = "4 (TRAIN SOLDIER)"
            elif 1 <= current_val <= 9: action_name = f"{current_val} (BUILD)" # Sonra genel durum

            # Çıktı: SIRA | SOURCE | ACTION | TARGET
            print(f"{detected_count+1:<6} {prev_val:<15} {action_name:<20} {next_val:<15}")
            
            detected_count += 1
            i += 2
        else:
            i += 1

    print("-" * 60)
    print(f"Toplam Tespit Edilen Komut Zinciri: {detected_count}")
    
    # Veri Kalitesi Kontrolü
    if detected_count == 0:
        print("\n[UYARI] Hiç mantıklı 3'lü yapı bulunamadı!")
        print("- Kayıt sadece '0' (Bekle) içeriyor olabilir.")
        print("- Ya da Action ID'leri 1-12 aralığının dışında olabilir.")
    else:
        print("\n[BİLGİ] Eğer yukarıdaki liste mantıklı görünüyorsa (örn. Source ve Target mantıklı grid indexleriyse), kayıt başarılıdır.")

if __name__ == "__main__":
    analyze_rts_demo(DEMO_PATH)