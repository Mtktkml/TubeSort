"""TubeSort — pilot zorluk merdiveni + skor.

Parametre merdiveni (kapasite, renk, bos) uzerinden OLCULEN zorluk skorunun
merdiven sirasiyla uyusup uyusmadigini sinar. Cikti: analiz raporu
(pilot_ladder.md) + pilot_levels.json. Agirlik kalibrasyonu oyuncu testiyle.

Iki katman (karistirilmasin):
  - Parametre merdiveni: kaba zorluk sinifi + cesitlilik garantisi.
  - Olculen skor: ince siralama + slot ici aday secimi.

Skor: AGIRLIKLI TOPLAM. Her terim tum aday havuzu uzerinde [0,1]'e min-max
normalize edilir, boylece agirliklar dogrudan "zorlugun yuzde kaci" diye okunur.
  L (0.45) = enKisa cozum uzunlugu   -> plan uzunlugu ~= tahta HACMI (buyuk=zor)
  C (0.25) = log(durum sayisi)       -> arama karmasikligi (buyuk=zor)
  A (0.15) = -log(cozum sayisi)      -> affetmezlik (az cozum=zor)
  T (0.15) = olu-durum orani         -> tuzak yogunlugu (cok=zor)
Boyut (L,C) baskin tutulur ki siralama tahta hacmini izlesin ve tup/renk
artisinda monoton kalsin; tuzak (T)/affetmezlik (A) ikincil eslik-bozuculardir.
Yapisal parametreler (kap/renk/bos) formulde DOGRUDAN yok: L zaten hacmi temsil
eder, kap/renk'i ayrica koymak hacmi cift sayardi.
UYARI: L hacmi (renk*kap) izler ama tup=renk+bos ve kapasite degisken; boyuta
gore siralamak tup VE rengi ayni anda birebir monoton yapmayabilir.

Slot ici secim: 30 aday SKORA gore siralanir, ORTANIN HEMEN USTUNDEKI 2 aday
temsilci alinir (ordered[15], ordered[16]; her tier ekranda X.1/X.2 diye 2
tahta gosterilir). Ogretici tier'lar (1-2) bunun disinda: SABIT, skora girmez.

Butce/OUT_OF_BUDGET ve cozulemez adaylar AYRI loglanir — sessizce elenip
level havuzunu kolaya yamultmasin (Murase 1996 dersi).

Calistirma:  python pilot_ladder.py
"""

import json
import math
import os
import random
import time

import crosscheck as cc

# Parametre merdiveni: (kapasite, renk, bos). Her tahta HEP 2 bos tup icerir
# (kullanici kolayca cikmaza girmesin diye); zorluk yalniz (kapasite, renk)
# artisindan gelir. Tup = renk+2, hacim = renk*kap. Siralama OLCULEN skora gore
# (asagida); merdiven yalnizca aday havuzunu tanimlar. Ust uc daha cok renk/
# kapasiteyle zorlar (tavan (8,7,2)); sinirlar: kap<=8 (MaxLayers), renk<=8 (palet).
LADDER = [
    (4, 3, 2),   # hacim 12, 5 tup
    (4, 4, 2),   # 16, 6
    (4, 5, 2),   # 20, 7
    (4, 6, 2),   # 24, 8
    (5, 5, 2),   # 25, 7
    (5, 6, 2),   # 30, 8
    (6, 5, 2),   # 30, 7   (ayni hacim, farkli kapasite: cap etkisi probu)
    (5, 7, 2),   # 35, 9
    (6, 6, 2),   # 36, 8
    (6, 7, 2),   # 42, 9
    (7, 6, 2),   # 42, 8
    (7, 7, 2),   # 49, 9
    (8, 7, 2),   # 56, 9   (tavan)
]

# Hepsi 2 bos: kullaniciyi kolay cikmazdan korur.
assert all(empties == 2 for _cap, _colors, empties in LADDER), \
    "her slot 2 bos tup icermeli (kolay cikmaz onlemi)"

# Ogretici tier'lar: ekranin en basi, SABIT sira (skora GIRMEZLER; kucuk
# ogreticiler skorla siralanirsa yanlis yere kayabilir, hep en basta dursunlar).
# Referans oyunun ilk 2 seviyesi ornek alindi: az tup, oyuncuyu korkutmasin.
#   Tier 1: tek renk, 2 tup — uretici tek rengi karistiramaz, o yuzden ELLE.
#           Iki varyant: 1+3 ve 2+2 bolunme. Tek amac "dokun-dok, birlesir".
#   Tier 2: 2 renk, 2 bos, 4 tup — uretici uretir. 2 bos tup: cikmaz neredeyse
#           imkansiz (ogretici cikmaza dusmesin; 1 bos tup kilitlenmeye acikti).
TUTORIAL_CAP = 4
TUTORIAL_COLOR = 0
TUTORIAL1_BOARDS = [
    ((TUTORIAL_COLOR,), (TUTORIAL_COLOR,) * 3),      # 1.1: 1 + 3
    ((TUTORIAL_COLOR,) * 2, (TUTORIAL_COLOR,) * 2),  # 1.2: 2 + 2
]
TUTORIAL2_PARAMS = (4, 2, 2)   # (kap, renk, bos) — 2 bos: cikmaz-guvenli ogretici

CANDIDATES_PER_SLOT = 30    # slot basina KABUL edilen (SOLVABLE) aday sayisi
MAX_ATTEMPTS_FACTOR = 20    # sonsuz donguye karsi: en fazla 30*20 deneme
SEED = 42

# Dort-terim skor agirliklari (toplam 1.0). Boyut (L,C) baskin, tuzak/affetmezlik
# (T,A) ikincil — tup/renk artisinda siralama monoton kalsin diye.
WEIGHTS = {"L": 0.45, "C": 0.25, "A": 0.15, "T": 0.15}


def build_slot(cap, colors, empties, rng):
    """Bir slot icin CANDIDATES_PER_SLOT kabul edilmis aday uretir ve olcer.

    Kabul edilen adaylar (ham metrik dict'leri) + eleme sayaclarini doner.
    Skor burada HESAPLANMAZ: normalizasyon tum havuzu gerektirir, o yuzden
    skorlama main'de ikinci geciste yapilir.
    """
    accepted = []            # ham metrik dict'leri (asagida)
    unsolvable = 0
    budget = 0               # solve OUT_OF_BUDGET
    short_budget = 0         # BFS enKisa butce asti (nadir)
    dead_budget = 0          # dead_ratio butce asti (solve gecerse ~imkansiz)
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

        # T: olu-durum orani. solve gectiyse uzay zaten <=BUDGET'te tukendi,
        # yani dead_ratio'nun butce asmasi pratikte imkansiz; yine de korunur.
        dratio, _reach, dead_hit = cc.dead_ratio(board, cap)
        if dead_hit or dratio is None:
            dead_budget += 1
            continue

        accepted.append({
            "shortest": shortest,
            "sol_count": sol_count,
            "states": states,
            "dead": dratio,
            "board": board,
        })

    return {
        "accepted": accepted,
        "attempts": attempts,
        "unsolvable": unsolvable,
        "budget": budget,
        "short_budget": short_budget,
        "dead_budget": dead_budget,
    }


def make_scorer(all_cands):
    """Tum havuzdan min-max sinirlarini cikarip bir skorlama fonksiyonu doner.

    Her terim once donusturulur (durum->log, cozum->-log), sonra havuz uzerinde
    [0,1]'e min-max normalize edilir. score(cand) -> {L,C,A,T,total} dict'i.
    Sinirlar da doner (rapora yazmak icin).
    """
    shortest_vals = [c["shortest"] for c in all_cands]
    logstate_vals = [math.log(c["states"]) for c in all_cands]     # states>=1
    negcount_vals = [-math.log(c["sol_count"]) for c in all_cands]  # sol_count>=1
    dead_vals = [c["dead"] for c in all_cands]

    bounds = {
        "L": (min(shortest_vals), max(shortest_vals)),
        "C": (min(logstate_vals), max(logstate_vals)),
        "A": (min(negcount_vals), max(negcount_vals)),
        "T": (min(dead_vals), max(dead_vals)),
    }

    def nz(x, lo, hi):
        return 0.0 if hi <= lo else (x - lo) / (hi - lo)

    def score(c):
        L = nz(c["shortest"], *bounds["L"])
        C = nz(math.log(c["states"]), *bounds["C"])
        A = nz(-math.log(c["sol_count"]), *bounds["A"])
        T = nz(c["dead"], *bounds["T"])
        total = (WEIGHTS["L"] * L + WEIGHTS["C"] * C
                 + WEIGHTS["A"] * A + WEIGHTS["T"] * T)
        return {"L": L, "C": C, "A": A, "T": T, "total": total}

    return score, bounds


def choose_two(accepted, key_fn):
    """Adaylari key_fn'e gore sirala, ORTANIN HEMEN USTUNDEKI 2 temsilciyi
    dondur (n=30 icin ordered[15], ordered[16]). Tam merdiven ortasi (14,15)
    yerine bir kademe zora yaslanir — sinifin tipik-ama-biraz-zor tarafini
    temsil eder (random katilmaz). Donen [X.1, X.2]: X.1 hafifce kolay (dusuk
    key), X.2 hafifce zor."""
    ordered = sorted(accepted, key=key_fn)
    n = len(ordered)
    if n == 0:
        raise ValueError("bos aday listesi: temsilci secilemez")
    if n == 1:
        return [ordered[0], ordered[0]]
    lo = min(n // 2, n - 2)   # n=30 -> 15; kucuk n'de listeyi tasmayi engeller
    return [ordered[lo], ordered[lo + 1]]


def metrics_for(board, cap):
    """Elle kurulan (ogretici) bir tahtanin ham metriklerini hesaplar ve ayni
    zamanda cozulebilirligini DOGRULAR (cozulemez/butce asilirsa hata firlatir)."""
    verdict, states, sol_count, _first = cc.solve(board, cap)
    if verdict != "SOLVABLE":
        raise ValueError(f"Ogretici tahta cozulemez ({verdict}): {fmt_tubes(board)}")
    shortest, _s, short_hit = cc.shortest_solution(board, cap)
    if short_hit or shortest is None:
        raise ValueError(f"Ogretici enKisa butce asti: {fmt_tubes(board)}")
    dratio, _r, dead_hit = cc.dead_ratio(board, cap)
    if dead_hit or dratio is None:
        raise ValueError(f"Ogretici dead_ratio butce asti: {fmt_tubes(board)}")
    return {"shortest": shortest, "sol_count": sol_count, "states": states,
            "dead": dratio, "board": board}


def main():
    rng = random.Random(SEED)

    print("Level merdiveni basliyor (skor = dort-terim agirlikli: "
          f"L{WEIGHTS['L']} C{WEIGHTS['C']} A{WEIGHTS['A']} T{WEIGHTS['T']})\n")
    wall0 = time.perf_counter()

    # 1. RANKED slotlar: uret + olc. Bunlar skora gore KENDI aralarinda dizilir.
    slot_results = []
    all_cands = []
    for slot, (cap, colors, empties) in enumerate(LADDER, start=1):
        t0 = time.perf_counter()
        result = build_slot(cap, colors, empties, rng)
        secs = time.perf_counter() - t0
        slot_results.append((cap, colors, empties, result))
        all_cands.extend(result["accepted"])
        print(f"Slot {slot:2d}  kap={cap} renk={colors} bos={empties}  "
              f"kabul={len(result['accepted'])}  "
              f"eleme(coz-mez={result['unsolvable']},butce={result['budget']},"
              f"bfs={result['short_budget']},olu={result['dead_budget']})  {secs:.1f}s")

    if not all_cands:
        print("HIC KABUL EDILEN ADAY YOK — cikiliyor.")
        return

    # 2. Skorla; her ranked slot icin ORTADAKI 2 adayi sec, tier'lari skora sirala.
    score_fn, bounds = make_scorer(all_cands)
    ranked = []
    for cap, colors, empties, result in slot_results:
        acc = result["accepted"]
        if not acc:
            continue
        two = choose_two(acc, lambda c: score_fn(c)["total"])
        tier_score = (score_fn(two[0])["total"] + score_fn(two[1])["total"]) / 2
        ranked.append({"cap": cap, "colors": colors, "empties": empties,
                       "boards": two, "score": tier_score})
    ranked.sort(key=lambda r: r["score"])

    # 3. Ogretici tier'lar (SABIT, en basa). Tier 1 elle, Tier 2 uretici.
    tut1 = [metrics_for(b, TUTORIAL_CAP) for b in TUTORIAL1_BOARDS]
    tut2_res = build_slot(*TUTORIAL2_PARAMS, rng)
    if not tut2_res["accepted"]:
        print("UYARI: ogretici 2 icin cozulebilir tahta uretilemedi — cikiliyor.")
        return
    tut2 = choose_two(tut2_res["accepted"], lambda c: c["shortest"])

    # 4. Tier listesi: [ogretici1, ogretici2] + skora-gore-sirali ranked.
    tiers = [(TUTORIAL_CAP, tut1), (TUTORIAL2_PARAMS[0], tut2)]
    tiers += [(r["cap"], r["boards"]) for r in ranked]

    total_secs = time.perf_counter() - wall0
    levels_path, n_levels = write_pilot_levels(tiers)
    write_report(ranked, tut1, tut2, bounds, total_secs, score_fn)
    print(f"\nBITTI — {total_secs:.1f}s. Rapor: pilot_ladder.md · "
          f"{len(tiers)} tier x 2 = {n_levels} tahta -> {levels_path}")


def write_pilot_levels(tiers):
    """Tier listesini oyunun okudugu semaya yazar: Assets/Resources/
    pilot_levels.json. Her tier 2 tahta -> label X.1 / X.2 (ekranda "LEVEL
    1.1" gibi gosterilir). Sema alanlari:
      level          — sirali int (1..N), LevelLibrary'nin indeksi
      label          — "tier.varyant" ("1.1", "1.2", "2.1", ...)
      capacity       — tup kapasitesi
      tubes[]        — dipten yukari virgullu; bos tup "" (ParseTube uyumlu)
      shortest       — enKisa cozum uzunlugu (ham metrik; C# okur, hesaplamaz)
      solutionCount  — cozum sayisi (ham metrik)
    Agirliga bagli SKOR yazilmaz, yalniz ham metrik: agirlik degisince
    yeniden siralanabilsin diye."""
    levels = []
    for t_idx, (cap, boards) in enumerate(tiers, start=1):
        for v_idx, c in enumerate(boards, start=1):
            levels.append({
                "level": len(levels) + 1,
                "label": f"{t_idx}.{v_idx}",
                "capacity": cap,
                "tubes": [",".join(str(x) for x in tube) for tube in c["board"]],
                "shortest": c["shortest"],
                "solutionCount": c["sol_count"],
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


def write_report(ranked, tut1, tut2, bounds, total_secs, score_fn):
    """pilot_ladder.md: (1) tier ozeti (ogretici + skora sirali ranked, her
    tier 2 tahta), (2) secilen 30 tahta (label ile)."""
    lines = []
    lines.append("# TubeSort — Level Merdiveni Raporu")
    lines.append("")
    lines.append(f"Seed `{SEED}` · slot basina {CANDIDATES_PER_SLOT} aday · "
                 f"tier basina orta-ust 2 aday (X.1, X.2) · toplam {total_secs:.1f}s")
    lines.append("")
    lines.append("Skor **dort-terim agirlikli toplam** "
                 f"(L={WEIGHTS['L']} C={WEIGHTS['C']} A={WEIGHTS['A']} T={WEIGHTS['T']}); "
                 "her terim ranked havuz uzerinde [0,1]'e min-max normalize. "
                 "L=enKisa, C=log(durum), A=-log(cozum), T=olu-durum orani. "
                 "Ogretici tier'lar (1-2) SABIT, skora girmez.")
    lines.append("")
    lines.append("Normalizasyon sinirlari (ranked havuz min/maks): "
                 f"enKisa `{bounds['L'][0]}..{bounds['L'][1]}`, "
                 f"log(durum) `{bounds['C'][0]:.2f}..{bounds['C'][1]:.2f}`, "
                 f"-log(cozum) `{bounds['A'][0]:.2f}..{bounds['A'][1]:.2f}`, "
                 f"olu `{bounds['T'][0]:.3f}..{bounds['T'][1]:.3f}`.")
    lines.append("")

    def tier_row(tier_no, tip, cap, colors, empties, boards, score=None):
        b1, b2 = boards
        sc = f"{score:.3f}" if score is not None else "—"
        return (f"| {tier_no} | {tip} | {cap} | {colors} | {empties} "
                f"| {b1['shortest']},{b2['shortest']} "
                f"| {b1['sol_count']},{b2['sol_count']} "
                f"| {b1['dead']:.2f},{b2['dead']:.2f} | {sc} |")

    # (1) Tier ozeti (ekran sirasi)
    lines.append("## 1. Tier ozeti (ekran sirasi)")
    lines.append("")
    lines.append("Ogretici 1-2 sabit; 3+ skora gore artan. enKisa/cozum/olu = "
                 "iki tahtanin (X.1, X.2) degerleri. skor~ = iki tahtanin ortalamasi.")
    lines.append("")
    lines.append("| tier | tip | kap | renk | bos | enKisa(1,2) | cozum(1,2) | olu(1,2) | skor~ |")
    lines.append("|--:|:--|--:|--:|--:|:--|:--|:--|--:|")
    lines.append(tier_row(1, "ogretici", TUTORIAL_CAP, 1, 0, tut1))
    lines.append(tier_row(2, "ogretici", *TUTORIAL2_PARAMS, tut2))
    for i, r in enumerate(ranked, start=3):
        lines.append(tier_row(i, "ranked", r["cap"], r["colors"], r["empties"],
                              r["boards"], r["score"]))
    lines.append("")

    # (2) Secilen tahtalar (label ile)
    lines.append("## 2. Secilen tahtalar (label)")
    lines.append("")

    def board_lines(tier_no, cap, colors, empties, boards):
        out = []
        for v, c in enumerate(boards, start=1):
            out.append(f"- **{tier_no}.{v}** (kap={cap} renk={colors} bos={empties}) "
                       f"enKisa={c['shortest']} cozum={c['sol_count']} "
                       f"olu={c['dead']:.3f}: `{fmt_tubes(c['board'])}`")
        return out

    lines += board_lines(1, TUTORIAL_CAP, 1, 0, tut1)
    lines += board_lines(2, *TUTORIAL2_PARAMS, tut2)
    for i, r in enumerate(ranked, start=3):
        lines += board_lines(i, r["cap"], r["colors"], r["empties"], r["boards"])

    script_dir = os.path.dirname(os.path.abspath(__file__))
    out_path = os.path.join(script_dir, "pilot_ladder.md")
    with open(out_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")


if __name__ == "__main__":
    main()
