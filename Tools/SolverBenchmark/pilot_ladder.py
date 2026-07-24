"""TubeSort — C asamasi: ~15 levellik pilot merdiveni + zorluk skoru.

AMAC (pilot, shippable degil): parametre merdiveni (kapasite, renk, bos) ile
OLCULEN zorluk skorunun uyusup uyusmadigini sinamak. Cikti bir ANALIZ raporu
(pilot_ladder.md); levels.json'a DOKUNULMAZ (o D asamasinin isi).

Iki katman (karistirilmasin):
  - Parametre merdiveni: kaba zorluk sinifi + cesitlilik garantisi.
  - Olculen skor: ince siralama + slot ici aday secimi.
Pilotun isi bu ikisinin uyustugunu SINAMAK, varsaymak degil.

Skor (mentor karari, 24 Tem 2026): AGIRLIKLI TOPLAM. Her terim tum aday havuzu
uzerinde [0,1]'e min-max normalize edilir, boylece agirliklar dogrudan
"zorlugun yuzde kaci" diye okunur.
  T (0.55) = olu-durum orani (dead_ratio)      -> tuzak yogunlugu (cok=zor)
  A (0.20) = -log(cozum sayisi)                -> affetmezlik (az cozum=zor)
  L (0.15) = enKisa cozum uzunlugu             -> plan uzunlugu (buyuk=zor)
  C (0.10) = log(durum sayisi)                 -> arama karmasikligi (buyuk=zor)
Agirliklar oyun testiyle kalibre edildi (24 Tem, 12 pilot leveli oynandi):
  - T baskın: tuzak = kullanicinin yeniden baslama/geri alma sayisi; insan
    zorlugunun asil kaynagi. Playtest: yuksek-T level (11) dusuk-T'den (12)
    ACIK ARA zor hissettirdi, 12'nin daha az cozumu (dusuk A) olmasina ragmen.
  - A ikinci ama "kurtarilabilir": T~0 iken tek cozum bile cok hamleyle sabirla
    bulunur; o yuzden T'den kritik degil.
  - L~C boyut olcer (korele, neredeyse ayni); dusuk agirlik yalniz kolay
    (T~0) levellerin kendi arasi yumusak siralamasi icin. Playtest: en buyuk
    tahta (31 hamle) yalniz 3 hissettirdi -> L doyuyor.
Yapisal parametreler (kap/renk/bos) BILEREK formulde YOK: metrikleri ureten
girdiler, dogrudan konursa hacim cift sayilir. Ham sinyaller (enKisa/cozum/
durum/olu) yine loglanir. NOT: yalniz 2 affetmez orneklem (11,12) var; T/A
dengesi D'nin ters-uretimi cok affetmez level verince kesinlesecek.

Slot ici secim: 30 aday SKORA gore siralanir, MEDYAN temsilci alinir (sinifin
tipik zorlugu); 30 adayin ham dagilimi (min/medyan/maks) da raporlanir ki
sinif zorluk-kararsiz mi gorulsun.

Butce/OUT_OF_BUDGET ve cozulemez adaylar AYRI loglanir — sessizce elenip
level havuzunu kolaya yamultmasin (Murase 1996 dersi).

Calistirma:  python pilot_ladder.py
"""

import json
import math
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

# Dort-terim skor agirliklari (toplam 1.0). Oyun testiyle kalibre edildi
# (24 Tem): T baskin, sonra A, sonra L~C. Gerekce icin modul docstring'ine bak.
WEIGHTS = {"T": 0.55, "A": 0.20, "L": 0.15, "C": 0.10}


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


def choose_median(accepted, score_fn):
    """Adaylari SKORA gore sirala, medyan (alt-orta) temsilciyi sec."""
    ordered = sorted(accepted, key=lambda c: score_fn(c)["total"])
    return ordered[len(ordered) // 2]


def dist(values):
    """(min, medyan, maks) — dagilimi ozetler."""
    return (min(values), statistics.median(values), max(values))


def main():
    rng = random.Random(SEED)

    print("Pilot merdiveni basliyor (skor = dort-terim agirlikli: "
          f"L{WEIGHTS['L']} C{WEIGHTS['C']} A{WEIGHTS['A']} T{WEIGHTS['T']})\n")
    wall0 = time.perf_counter()

    # 1. GECIS: tum slotlarin adaylarini uret + olc (skor henuz yok).
    slot_results = []
    all_cands = []
    for slot, (cap, colors, empties) in enumerate(LADDER, start=1):
        t0 = time.perf_counter()
        result = build_slot(cap, colors, empties, rng)
        secs = time.perf_counter() - t0
        slot_results.append((slot, cap, colors, empties, result, secs))
        all_cands.extend(result["accepted"])
        n = len(result["accepted"])
        print(f"Slot {slot:2d}  kap={cap} renk={colors} bos={empties}  "
              f"kabul={n}  eleme(coz-mez={result['unsolvable']},"
              f"butce={result['budget']},bfs={result['short_budget']},"
              f"olu={result['dead_budget']})  {secs:.1f}s")

    if not all_cands:
        print("HIC KABUL EDILEN ADAY YOK — cikiliyor.")
        return

    # 2. GECIS: havuz uzerinden skorla, her slotun temsilcisini sec.
    score_fn, bounds = make_scorer(all_cands)
    rows = []
    for slot, cap, colors, empties, result, secs in slot_results:
        accepted = result["accepted"]
        if not accepted:
            rows.append({"slot": slot, "cap": cap, "colors": colors,
                         "empties": empties, "empty": True, **result})
            continue

        chosen = choose_median(accepted, score_fn)
        rows.append({
            "slot": slot, "cap": cap, "colors": colors, "empties": empties,
            "empty": False,
            "tubes": colors + empties,
            "n_accepted": len(accepted),
            "short_dist": dist([c["shortest"] for c in accepted]),
            "count_dist": dist([c["sol_count"] for c in accepted]),
            "dead_dist": dist([c["dead"] for c in accepted]),
            "chosen": chosen,
            "cscore": score_fn(chosen),
            "unsolvable": result["unsolvable"],
            "budget": result["budget"],
            "short_budget": result["short_budget"],
            "dead_budget": result["dead_budget"],
            "secs": secs,
        })

    total_secs = time.perf_counter() - wall0
    write_report(rows, bounds, total_secs)
    levels_path, n_levels = write_pilot_levels(rows)
    print(f"\nBITTI — {total_secs:.1f}s. Rapor: pilot_ladder.md · "
          f"Oyun onizleme (SKORA gore sirali): {n_levels} level -> {levels_path}")


def write_pilot_levels(rows):
    """Secilen temsilcileri oyunun okudugu semaya yazar (SKORA gore artan
    sirali): Assets/Resources/pilot_levels.json. Boylece onizlemede ok
    tuslariyla gezerken level 1 = olculen en kolay, level N = en zor —
    Adim 3'te "olculen sira hissettigimle uyusuyor mu" oynanarak sinanir.

    Sema levels.json ile AYNI ({level, capacity, tubes[]}); zorluk/skor alani
    YOK (o D'nin sema karari). levels.json'a DOKUNULMAZ. tube metni
    LevelLibrary.ParseTube ile uyumlu: dipten yukari virgullu, bos tup "".
    """
    scored = [r for r in rows if not r["empty"]]
    scored.sort(key=lambda r: r["cscore"]["total"])

    levels = []
    for r in scored:
        board = r["chosen"]["board"]
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


def write_report(rows, bounds, total_secs):
    """pilot_ladder.md: (1) ham dagilim tablosu [merdiven sirasi],
    (2) secilen temsilci + skor tablosu [merdiven sirasi],
    (3) skora gore sirali ozet, (4) secilen tahtalar."""
    lines = []
    lines.append("# TubeSort — Pilot Merdiveni Raporu (C asamasi)")
    lines.append("")
    lines.append(f"Seed `{SEED}` · slot basina {CANDIDATES_PER_SLOT} kabul edilen aday · "
                 f"toplam {total_secs:.1f}s")
    lines.append("")
    lines.append("Skor **dort-terim agirlikli toplam** "
                 f"(L={WEIGHTS['L']} C={WEIGHTS['C']} A={WEIGHTS['A']} T={WEIGHTS['T']}); "
                 "her terim tum havuz uzerinde [0,1]'e min-max normalize. "
                 "L=enKisa, C=log(durum), A=-log(cozum), T=olu-durum orani. "
                 "Slot temsilcisi = skora gore **medyan** aday. Ham sinyaller de "
                 "verilir (agirlik/egri Adim 3'te veriyle sekillenir).")
    lines.append("")
    lines.append("Normalizasyon sinirlari (havuz min/maks): "
                 f"enKisa `{bounds['L'][0]}..{bounds['L'][1]}`, "
                 f"log(durum) `{bounds['C'][0]:.2f}..{bounds['C'][1]:.2f}`, "
                 f"-log(cozum) `{bounds['A'][0]:.2f}..{bounds['A'][1]:.2f}`, "
                 f"olu `{bounds['T'][0]:.3f}..{bounds['T'][1]:.3f}`.")
    lines.append("")

    # (1) Ham dagilim
    lines.append("## 1. Ham dagilim (merdiven sirasi)")
    lines.append("")
    lines.append("`[min/med/maks]` = 30 adayin dagilimi. `olu` = dead_ratio "
                 "(tuzak yogunlugu). n = kabul edilen aday sayisi.")
    lines.append("")
    lines.append("| # | kap | renk | bos | tup | n | enKisa[min/med/maks] | "
                 "cozum[min/med/maks] | olu[min/med/maks] | eleme(cm/but/bfs/olu) |")
    lines.append("|--:|--:|--:|--:|--:|--:|:--|:--|:--|:--|")
    for r in rows:
        if r["empty"]:
            lines.append(f"| {r['slot']} | {r['cap']} | {r['colors']} | {r['empties']} "
                         f"| — | 0 | KABUL YOK | — | — | "
                         f"{r['unsolvable']}/{r['budget']}/{r['short_budget']}/{r['dead_budget']} |")
            continue
        smin, smed, smax = r["short_dist"]
        cmin, cmed, cmax = r["count_dist"]
        dmin, dmed, dmax = r["dead_dist"]
        lines.append(
            f"| {r['slot']} | {r['cap']} | {r['colors']} | {r['empties']} | {r['tubes']} "
            f"| {r['n_accepted']} | {smin}/{smed}/{smax} | {cmin}/{cmed}/{cmax} "
            f"| {dmin:.2f}/{dmed:.2f}/{dmax:.2f} "
            f"| {r['unsolvable']}/{r['budget']}/{r['short_budget']}/{r['dead_budget']} |")
    lines.append("")

    # (2) Secilen temsilci + skor bilesenleri
    lines.append("## 2. Secilen temsilci + skor (merdiven sirasi)")
    lines.append("")
    lines.append("Ham = secilen adayin ham metrikleri. L/C/A/T = normalize terimler "
                 "[0,1]. skor = agirlikli toplam.")
    lines.append("")
    lines.append("| # | tup | enKisa | cozum | durum | olu | L | C | A | T | **skor** |")
    lines.append("|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|")
    for r in rows:
        if r["empty"]:
            continue
        ch = r["chosen"]
        s = r["cscore"]
        lines.append(
            f"| {r['slot']} | {r['tubes']} | {ch['shortest']} | {ch['sol_count']} "
            f"| {ch['states']} | {ch['dead']:.3f} "
            f"| {s['L']:.2f} | {s['C']:.2f} | {s['A']:.2f} | {s['T']:.2f} "
            f"| **{s['total']:.3f}** |")
    lines.append("")

    # (3) Skora gore sirali (Adim 3'un para gorunumu)
    lines.append("## 3. Skora gore sirali (olculen zorluk artan)")
    lines.append("")
    lines.append("Merdiven sirasi ile bu sira UYUSUYOR mu? Uyusmuyorsa hangi slot "
                 "nereye kaydi — Adim 3'te hissedilen zorlukla kiyaslanacak.")
    lines.append("")
    lines.append("| sira | slot | kap | renk | bos | enKisa | cozum | olu | **skor** |")
    lines.append("|--:|--:|--:|--:|--:|--:|--:|--:|--:|")
    scored = sorted((r for r in rows if not r["empty"]),
                    key=lambda r: r["cscore"]["total"])
    for i, r in enumerate(scored, start=1):
        ch = r["chosen"]
        s = r["cscore"]
        lines.append(
            f"| {i} | {r['slot']} | {r['cap']} | {r['colors']} | {r['empties']} "
            f"| {ch['shortest']} | {ch['sol_count']} | {ch['dead']:.3f} "
            f"| **{s['total']:.3f}** |")
    lines.append("")

    # (4) Secilen tahtalar (skora gore sirali)
    lines.append("## 4. Secilen (medyan) tahtalar — skora gore sirali")
    lines.append("")
    for i, r in enumerate(scored, start=1):
        ch = r["chosen"]
        s = r["cscore"]
        lines.append(f"- **{i}. (slot {r['slot']}, kap={r['cap']} renk={r['colors']} "
                     f"bos={r['empties']})** skor={s['total']:.3f} "
                     f"enKisa={ch['shortest']} cozum={ch['sol_count']} olu={ch['dead']:.3f}: "
                     f"`{fmt_tubes(ch['board'])}`")

    script_dir = os.path.dirname(os.path.abspath(__file__))
    out_path = os.path.join(script_dir, "pilot_ladder.md")
    with open(out_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")


if __name__ == "__main__":
    main()
