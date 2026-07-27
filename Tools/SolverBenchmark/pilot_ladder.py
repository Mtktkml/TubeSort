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

Slot ici secim: 30 aday SKORA gore siralanir, ORTANIN HEMEN USTUNDEKI 2 aday
temsilci alinir (ordered[15], ordered[16]; her tier ekranda X.1/X.2 diye 2
tahta gosterilir; mentor karari 27 Tem 2026 — tam-orta 14,15 yerine bir kademe
zora yaslanir). Ogretici tier'lar (1-2) bunun disinda: SABIT, skora girmez.

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
# (4,4,1) KOPRU olarak eklendi: skora gore siralamada bos 2->1 ucurumunu
# (~0.28->0.49) yumusatmak icin; yeri hacimle degil OLCULEN skorla belirlenir.
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
    (4, 4, 1),              # KOPRU (bos=1, az renk): bos 2->1 ucurumu icin
]

# Guvenlik: bos=1 yalniz kap<=4'te (Bulgu 1). Ihlal edilirse uretim cokerdi.
assert all(empties >= 2 or cap <= 4 for cap, _c, empties in LADDER), \
    "bos=1 yalniz kap<=4'te olabilir (kap>=5'te rastgele uretim cokuyor)"

# Ogretici tier'lar: ekranin en basi, SABIT sira (skora gore siralamaya
# GIRMEZLER, yoksa bos=1 ogreticisi tuzak-yogunlugundan zor kumeye kayabilir).
# Referans oyunun ilk 2 seviyesi ornek alindi: az tup, oyuncuyu korkutmasin.
#   Tier 1: tek renk, 2 tup — uretici tek rengi karistiramaz, o yuzden ELLE.
#           Iki varyant: 1+3 ve 2+2 bolunme. Tek amac "dokun-dok, birlesir".
#   Tier 2: 2 renk, 1 bos, 3 tup — uretici uretir. Ilk gercek siralama.
TUTORIAL_CAP = 4
TUTORIAL_COLOR = 0
TUTORIAL1_BOARDS = [
    ((TUTORIAL_COLOR,), (TUTORIAL_COLOR,) * 3),      # 1.1: 1 + 3
    ((TUTORIAL_COLOR,) * 2, (TUTORIAL_COLOR,) * 2),  # 1.2: 2 + 2
]
TUTORIAL2_PARAMS = (4, 2, 1)   # (kap, renk, bos)

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
    Agirliga bagli SKOR yazilmaz: oyun testiyle degisecek (mentor karari)."""
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
    tier 2 tahta), (2) kopru (4,4,1) kontrolu, (3) secilen 30 tahta (label ile)."""
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

    # (2) Kopru (4,4,1) kontrolu
    lines.append("## 2. Kopru (4,4,1) kontrolu")
    lines.append("")
    bridge_idx = next((i for i, r in enumerate(ranked)
                       if (r["cap"], r["colors"], r["empties"]) == (4, 4, 1)), None)
    if bridge_idx is None:
        lines.append("Kopru (4,4,1) ranked icinde bulunamadi (?).")
    else:
        tier_no = bridge_idx + 3
        bscore = ranked[bridge_idx]["score"]
        lines.append(f"Kopru tier **{tier_no}** olarak yerlesti, skor **{bscore:.3f}**.")
        if bridge_idx > 0:
            prev_s = ranked[bridge_idx - 1]["score"]
            lines.append(f"- Onceki tier skoru: {prev_s:.3f}  (fark {bscore - prev_s:+.3f})")
        if bridge_idx + 1 < len(ranked):
            next_s = ranked[bridge_idx + 1]["score"]
            lines.append(f"- Sonraki tier skoru: {next_s:.3f}  (fark {next_s - bscore:+.3f})")
        lines.append("")
        lines.append("Beklenti: kopru bos=2 kumesi ile bos=1 kumesi ARASINA dusmeli; "
                     "iki fark da makul olmali (tek buyuk ucurum kalmamali).")
    lines.append("")

    # (3) Secilen tahtalar (label ile)
    lines.append("## 3. Secilen tahtalar (label)")
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
