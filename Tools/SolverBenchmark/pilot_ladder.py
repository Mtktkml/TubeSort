"""TubeSort — C asamasi: ~15 levellik pilot merdiveni + ilk zorluk skoru.

AMAC (pilot, shippable degil): parametre merdiveni (kapasite, renk, bos) ile
OLCULEN zorluk skorunun uyusup uyusmadigini sinamak. Cikti bir ANALIZ raporu
(pilot_ladder.md); levels.json'a DOKUNULMAZ (o D asamasinin isi).

Iki katman (karistirilmasin):
  - Parametre merdiveni: kaba zorluk sinifi + cesitlilik garantisi.
  - Olculen skor: ince siralama + slot ici aday secimi.
Pilotun isi bu ikisinin uyustugunu SINAMAK, varsaymak degil.

Skor (mentor karari, 24 Tem 2026): LEKSIKOGRAFIK, agirlik YOK.
  birincil  = enKisa cozum uzunlugu (buyuk = zor; kanonik graf uzerinde BFS)
  esitlik bozucu = 1/cozumSayisi (az cozum = dar/zor)
Agirlik icat edilmez: enKisa/cozumSayisi/durum her level icin HAM loglanir;
mentor korelasyonu VERIYLE gorup agirliga (gerekiyorsa D'de) karar verir.
Gerekce: enKisa buyuk olcude tahta boyutunun fonksiyonu (slotlar arasi sirali
ama parametre sinifini tekrar eder); cozumSayisi ise slot ICINI ayiran bagimsiz
sinyal. Yani enKisa disariyi, count icERIYI siralar.

Slot ici secim (mentor karari): 30 adaydan MEDYAN temsilci (sinifin tipik
zorlugu); 30 adayin dagilimi (min/medyan/maks) da raporlanir ki sinif
zorluk-kararsiz mi gorulsun.

Butce/OUT_OF_BUDGET ve cozulemez adaylar AYRI loglanir — sessizce elenip
level havuzunu kolaya yamultmasin (Murase 1996 dersi).

Calistirma:  python pilot_ladder.py
"""

import json
import os
import random
import statistics
import time

import crosscheck as cc

# Parametre merdiveni: (kapasite, renk, bos). Pilot v1'in iki dersiyle revize
# edildi (24 Tem 2026):
#   Bulgu 1 — bos=1 saf rastgele uretimde kap>=5'te COKUYOR (kabul orani ~%0;
#     kap6 renk7 bos1'de 600 denemede 0 kabul). Karar: bos=1 yalniz kap<=4'te
#     sunulur (kabul ~%15-24, tolere edilebilir). Ust uc bos=2 kalir; garantili
#     bos=1 uretimi (ters-uretim) D asamasina birakildi.
#   Bulgu 2 — enKisa ~= 0.8*(renk*kap) = tahta hacmi; slot sirasi degil hacmi
#     takip eder. Kapasite artip renk sifirlaninca enKisa DUSUYORDU (eski slot
#     6, 11). Karar: merdiven HACME gore monoton dizilir; bos=1 ayni hacimde
#     "daha dar" (az cozum) oldugu icin ikizinin hemen ardina konur.
# Hacim (renk*kap) monoton: 12,16,20,20,24,24,25,30,30,35,36,42. Tup = renk+bos.
LADDER = [
    (4, 3, 2),              # 12
    (4, 4, 2),              # 16
    (4, 5, 2), (4, 5, 1),   # 20  (bos=1 = ayni hacim, daha dar)
    (4, 6, 2), (4, 6, 1),   # 24  (bos=1 = ayni hacim, daha dar)
    (5, 5, 2),              # 25
    (5, 6, 2), (6, 5, 2),   # 30  (ayni hacim, farkli kapasite: cap etkisi probu)
    (5, 7, 2),              # 35
    (6, 6, 2),              # 36
    (6, 7, 2),              # 42
]

# Guvenlik: bos=1 yalniz kap<=4'te (Bulgu 1). Ihlal edilirse uretim cokerdi.
assert all(empties >= 2 or cap <= 4 for cap, _c, empties in LADDER), \
    "bos=1 yalniz kap<=4'te olabilir (kap>=5'te rastgele uretim cokuyor)"

CANDIDATES_PER_SLOT = 30    # slot basina KABUL edilen (SOLVABLE) aday sayisi
MAX_ATTEMPTS_FACTOR = 20    # sonsuz donguye karsi: en fazla 30*20 deneme
SEED = 42


def difficulty_key(shortest, sol_count):
    """Leksikografik zorluk anahtari (kucukten buyuge = kolaydan zora).

    birincil enKisa (buyuk=zor); esitlikte 1/cozumSayisi (buyuk=zor, yani az
    cozum). Ayni anahtarla hem siralar hem medyan secilir.
    """
    return (shortest, 1.0 / sol_count)


def build_slot(cap, colors, empties, rng):
    """Bir slot icin CANDIDATES_PER_SLOT kabul edilmis aday uretir ve olcer.

    Kabul edilen adaylarin listesini + eleme sayaclarini doner.
    """
    accepted = []            # (shortest, sol_count, states, board) demetleri
    unsolvable = 0
    budget = 0               # solve OUT_OF_BUDGET
    short_budget = 0         # BFS enKisa butce asti (nadir)
    attempts = 0
    max_attempts = CANDIDATES_PER_SLOT * MAX_ATTEMPTS_FACTOR

    while len(accepted) < CANDIDATES_PER_SLOT and attempts < max_attempts:
        attempts += 1
        board = cc.generate(colors, cap, empties, rng)

        verdict, states, sol_count, _first = cc.solve(board, cap)
        if verdict == "UNSOLVABLE":
            unsolvable += 1
            continue
        if verdict == "OUT_OF_BUDGET":
            budget += 1
            continue

        # SOLVABLE: enKisa BFS ile olculur (solve'un ilk yolu rastlanti, metrik
        # degil). BFS butcesi asilirsa enKisa "bilinmiyor" — adayi temsilci
        # yapamayiz, ayrica logla ve atla.
        shortest, _sstates, short_hit = cc.shortest_solution(board, cap)
        if short_hit or shortest is None:
            short_budget += 1
            continue

        accepted.append((shortest, sol_count, states, board))

    return {
        "accepted": accepted,
        "attempts": attempts,
        "unsolvable": unsolvable,
        "budget": budget,
        "short_budget": short_budget,
    }


def choose_median(accepted):
    """Adaylari zorluk anahtarina gore sirala, medyan (alt-orta) temsilciyi sec."""
    ordered = sorted(accepted, key=lambda a: difficulty_key(a[0], a[1]))
    return ordered[len(ordered) // 2]


def dist(values):
    """(min, medyan, maks) — dagilimi ozetler."""
    return (min(values), statistics.median(values), max(values))


def main():
    rng = random.Random(SEED)
    rows = []

    print("Pilot merdiveni basliyor (skor = leksikografik enKisa -> 1/cozumSayisi)\n")
    wall0 = time.perf_counter()

    for slot, (cap, colors, empties) in enumerate(LADDER, start=1):
        t0 = time.perf_counter()
        result = build_slot(cap, colors, empties, rng)
        secs = time.perf_counter() - t0

        accepted = result["accepted"]
        if not accepted:
            print(f"Slot {slot:2d}  kap={cap} renk={colors} bos={empties}  "
                  f"KABUL EDILEN ADAY YOK ({result['attempts']} deneme) — ATLANDI")
            rows.append({"slot": slot, "cap": cap, "colors": colors,
                         "empties": empties, "empty": True, **result})
            continue

        shortest_vals = [a[0] for a in accepted]
        count_vals = [a[1] for a in accepted]
        chosen = choose_median(accepted)   # (shortest, sol_count, states, board)

        row = {
            "slot": slot, "cap": cap, "colors": colors, "empties": empties,
            "empty": False,
            "tubes": colors + empties,
            "n_accepted": len(accepted),
            "short_dist": dist(shortest_vals),
            "count_dist": dist(count_vals),
            "chosen": chosen,
            "attempts": result["attempts"],
            "unsolvable": result["unsolvable"],
            "budget": result["budget"],
            "short_budget": result["short_budget"],
            "secs": secs,
        }
        rows.append(row)

        smin, smed, smax = row["short_dist"]
        cmin, cmed, cmax = row["count_dist"]
        print(f"Slot {slot:2d}  kap={cap} renk={colors} bos={empties} tup={row['tubes']}  "
              f"enKisa[{smin}/{smed}/{smax}] cozum[{cmin}/{cmed}/{cmax}]  "
              f"secilen(enKisa={chosen[0]},cozum={chosen[1]},durum={chosen[2]})  "
              f"eleme(coz-mez={row['unsolvable']},butce={row['budget']},"
              f"bfs-butce={row['short_budget']})  {secs:.1f}s")

    total_secs = time.perf_counter() - wall0
    write_report(rows, total_secs)
    levels_path, n_levels = write_pilot_levels(rows)
    print(f"\nBITTI — {total_secs:.1f}s. Rapor: pilot_ladder.md · "
          f"Oyun onizleme: {n_levels} level -> {levels_path}")


def write_pilot_levels(rows):
    """Secilen (medyan) tahtalari oyunun okudugu semaya yazar:
    Assets/Resources/pilot_levels.json. Sema levels.json ile AYNI
    ({level, capacity, tubes[]}); zorluk/skor alani YOK (o D'nin sema
    karari). BoardView 'pilot onizleme' modunda bunu okur; levels.json'a
    DOKUNULMAZ. level numaralari 1..n bitisiktir (atlanan slot varsa
    kapanir). tube metni LevelLibrary.ParseTube ile uyumlu: dipten yukari
    virgullu, bos tup "" .
    """
    levels = []
    for r in rows:
        if r["empty"]:
            continue
        board = r["chosen"][3]   # (shortest, sol_count, states, board)
        levels.append({
            "level": len(levels) + 1,
            "capacity": r["cap"],
            "tubes": [",".join(str(c) for c in tube) for tube in board],
        })

    script_dir = os.path.dirname(os.path.abspath(__file__))
    out_path = os.path.normpath(os.path.join(
        script_dir, "..", "..", "Assets", "Resources", "pilot_levels.json"))
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump({"levels": levels}, f, indent=2)
    return out_path, len(levels)


def fmt_tubes(board):
    """Tahtayi okunur metne cevir: dolu tupler dipten yukari, bos '()'."""
    parts = []
    for tube in board:
        parts.append("[" + ",".join(str(c) for c in tube) + "]" if tube else "()")
    return " ".join(parts)


def write_report(rows, total_secs):
    """pilot_ladder.md: ozet tablo (dagilim + secilen) + secilen tahtalar."""
    lines = []
    lines.append("# TubeSort — Pilot Merdiveni Raporu (C asamasi)")
    lines.append("")
    lines.append(f"Seed `{SEED}` · slot basina {CANDIDATES_PER_SLOT} kabul edilen aday · "
                 f"toplam {total_secs:.1f}s")
    lines.append("")
    lines.append("Skor **leksikografik**: birincil enKisa (buyuk=zor), esitlik bozucu "
                 "1/cozumSayisi (az cozum=zor). Agirlik yok — ham sinyaller asagida, "
                 "korelasyon VERIYLE degerlendirilecek. Slot temsilcisi = **medyan** aday.")
    lines.append("")
    lines.append("`enKisa[min/med/maks]` ve `cozum[min/med/maks]` = 30 adayin dagilimi. "
                 "`durum` = solve'un genisletttigi kanonik durum sayisi (uzay boyutu).")
    lines.append("")
    header = ("| # | kap | renk | bos | tup | n | enKisa[min/med/maks] | "
              "cozum[min/med/maks] | sec.enKisa | sec.cozum | sec.durum | "
              "eleme(cm/but/bfs) |")
    lines.append(header)
    lines.append("|--:|--:|--:|--:|--:|--:|:--|:--|--:|--:|--:|:--|")
    for r in rows:
        if r["empty"]:
            lines.append(f"| {r['slot']} | {r['cap']} | {r['colors']} | {r['empties']} "
                         f"| — | 0 | KABUL YOK | — | — | — | — | "
                         f"{r['unsolvable']}/{r['budget']}/{r['short_budget']} |")
            continue
        smin, smed, smax = r["short_dist"]
        cmin, cmed, cmax = r["count_dist"]
        ch = r["chosen"]
        # n = KABUL edilen aday sayisi. Hedef CANDIDATES_PER_SLOT; dusukse
        # (bos=1 aday kitligi) dagilim/medyan temsilsizdir — sutun bunu gorunur
        # kilar ki [39/39/39] "30 aday hep 39" gibi okunmasin.
        lines.append(
            f"| {r['slot']} | {r['cap']} | {r['colors']} | {r['empties']} | {r['tubes']} "
            f"| {r['n_accepted']} | {smin}/{smed}/{smax} | {cmin}/{cmed}/{cmax} "
            f"| {ch[0]} | {ch[1]} | {ch[2]} "
            f"| {r['unsolvable']}/{r['budget']}/{r['short_budget']} |")

    lines.append("")
    lines.append("## Secilen (medyan) tahtalar")
    lines.append("")
    for r in rows:
        if r["empty"]:
            continue
        ch = r["chosen"]
        lines.append(f"- **Slot {r['slot']}** (kap={r['cap']} renk={r['colors']} "
                     f"bos={r['empties']}) enKisa={ch[0]} cozum={ch[1]}: "
                     f"`{fmt_tubes(ch[3])}`")

    script_dir = os.path.dirname(os.path.abspath(__file__))
    out_path = os.path.join(script_dir, "pilot_ladder.md")
    with open(out_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")


if __name__ == "__main__":
    main()
