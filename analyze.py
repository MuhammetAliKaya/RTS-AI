import os
import numpy as np

try:
    from mlagents.trainers.demo_loader import demo_to_buffer
    from mlagents.trainers.buffer import BufferKey
except ImportError:
    print("HATA: 'mlagents' kütüphanesi bulunamadı. (pip install mlagents)")
    exit()

# --- AYARLAR: 3 DEMO DOSYASININ YOLUNU BURAYA GİRİN ---
# Not: Dosya isimleri Unity'deki "Behavior Name" ayarına göre değişir.
DEMO_PATHS = {
    "UNIT":   "Assets/Demonstrations/unitselrec1_0.demo",   # Kaynak seçimi kaydı
    "ACTION": "Assets/Demonstrations/actionselrec1_0.demo", # Aksiyon seçimi kaydı
    "TARGET": "Assets/Demonstrations/targetselrec1_0.demo"  # Hedef seçimi kaydı
}
# -------------------------------------------------------

def load_actions_from_demo(path):
    """Verilen demo yolundan kesikli (discrete) aksiyonları çeker."""
    if not os.path.exists(path):
        print(f"[HATA] Dosya bulunamadı: {path}")
        return None
    
    try:
        _, buffer = demo_to_buffer(path, sequence_length=None)
        # (N, 1) boyutundaki diziyi (N,) boyutuna düzleştiriyoruz
        raw_actions = buffer[BufferKey.DISCRETE_ACTION]
        return np.array(raw_actions).flatten()
    except Exception as e:
        print(f"[HATA] {path} okunurken sorun oluştu: {e}")
        return None

def get_action_name(act_id):
    """Action ID'sini okunabilir isme çevirir."""
    if act_id == 0: return "WAIT (0)"
    if act_id == 10: return "ATTACK (10)"
    if act_id == 11: return "MOVE (11)"
    if act_id == 12: return "GATHER (12)"
    if act_id == 3: return "TRAIN WORKER (3)"
    if act_id == 4: return "TRAIN SOLDIER (4)"
    if 1 <= act_id <= 9: return f"BUILD ({act_id})"
    return f"UNKNOWN ({act_id})"

def analyze_triplet_demos():
    print("="*70)
    print("3 BAŞLI RTS AI - DEMO ANALİZİ")
    print("="*70)

    # 1. Verileri Yükle
    unit_actions = load_actions_from_demo(DEMO_PATHS["UNIT"])
    action_actions = load_actions_from_demo(DEMO_PATHS["ACTION"])
    target_actions = load_actions_from_demo(DEMO_PATHS["TARGET"])

    if unit_actions is None or action_actions is None or target_actions is None:
        print("\n[KRİTİK HATA] Dosyalardan biri veya birkaçı yüklenemedi. Yolları kontrol edin.")
        return

    # 2. Senkronizasyon Kontrolü
    # 3 dosyanın da adım sayısının (yaklaşık) eşit olması gerekir.
    len_u, len_a, len_t = len(unit_actions), len(action_actions), len(target_actions)
    min_len = min(len_u, len_a, len_t)

    print(f"Veri Uzunlukları -> Unit: {len_u}, Action: {len_a}, Target: {len_t}")
    
    if len_u != len_a or len_a != len_t:
        print("[UYARI] Dosya uzunlukları birebir aynı değil! En kısa olana göre işlem yapılacak.")
        print("Bu durum, ajanların 'Decision Requester' periyotları farklıysa oluşabilir.")
    
    print("-" * 70)
    print(f"{'ADIM':<6} {'SOURCE (Kim?)':<15} {'ACTION (Ne?)':<20} {'TARGET (Nereye?)':<15}")
    print("-" * 70)

    # 3. Analiz Döngüsü
    valid_records = 0
    
    for i in range(min_len):
        u_val = unit_actions[i]
        a_val = action_actions[i]
        t_val = target_actions[i]

        # FİLTRELEME:
        # Sadece ActionSelection ajanı "Wait" (0) DEMİYORSA ekrana yaz.
        # Çünkü '0' olduğu anlar aksiyon alınmayan boş anlardır.
        if a_val != 0:
            act_name = get_action_name(a_val)
            print(f"{i+1:<6} {u_val:<15} {act_name:<20} {t_val:<15}")
            valid_records += 1

    print("-" * 70)
    print(f"Toplam Anlamlı (Action != 0) Kayıt Sayısı: {valid_records}")
    
    if valid_records == 0:
        print("\n[SORUN VAR] Hiçbir anlamlı aksiyon bulunamadı.")
        print("Sebepler:")
        print("1. ActionSelectionAgent sürekli '0' (Wait) kaydetmiş olabilir.")
        print("2. Kayıt tuşuna basılmasına rağmen 'Heuristic' fonksiyonu 0 döndürmüş olabilir.")
        print("3. Dosyalar yanlış eşleştirilmiş olabilir.")
    else:
        print("\n[BAŞARILI] Kayıtlar senkronize görünüyor. Eğitim için kullanıma hazır!")

if __name__ == "__main__":
    analyze_triplet_demos()