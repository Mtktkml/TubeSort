"""TubeSort — pilot zorluk merdiveni + skor (PARALEL + tahta-basi teshis).

Parametre merdiveni (kapasite, renk, bos) uzerinden OLCULEN zorluk skorunun
merdiven sirasiyla uyusup uyusmadigini sinar. Cikti: analiz raporu
(pilot_ladder.md) + pilot_levels.json + diagnostics.json. Agirlik kalibrasyonu
oyuncu testiyle.

FAZ 0 (paralel makine): aday uretimi + olcum multiprocessing ile 8 cekirdege
dagitilir. Determinizm ADAY BASINA tohumdan gelir (seed_for): paralel calisma
sirasi sonucu DEGISTIRMEZ, her kosu ayni. NOT: eski tek-cekirdek SEED=42
ciktisiyla bire bir ayni DEGIL (cekilis sirasi degisti); hedef "her kosuda
ayni" + dogru, eski 30'un kopyasi degil.

Buyuk tahtada tam-sayim solve() butceyi asarsa evaluate_candidate yalin
solvable_only()'e duser: cozulebilirligi GARANTI eder (metrikler kismi).
Boyle "accepted_partial" adaylar Faz 0 tavaninda (kap<=8) OLUSMAZ; makine
Faz 1'in yuksek tavani icin simdiden hazir. Kismi-metrik skorlama Faz 1 isi.

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

Slot ici secim: 30 aday SKORA gore siralanir, ORTANIN HEMEN USTUNDEKI 2 aday
temsilci alinir (ordered[15], ordered[16]; her tier ekranda X.1/X.2 diye 2
tahta gosterilir). Ogretici tier'lar (1-2) bunun disinda: SABIT, skora girmez.

Butce/OUT_OF_BUDGET ve cozulemez adaylar AYRI loglanir — sessizce elenip
level havuzunu kolaya yamultmasin (Murase 1996 dersi).

Calistirma:  python pilot_ladder.py
"""

import json
import math
import multiprocessing as mp
import os
import random
import time

import crosscheck as cc

# Parametre merdiveni: (kapasite, renk, bos). Her tahta HEP 2 bos tup icerir
# (kullanici kolayca cikmaza girmesin diye); zorluk yalniz (kapasite, renk)
# artisindan gelir. Tup = renk+2, hacim = renk*kap. Siralama OLCULEN skora gore
# (asagida); merdiven yalnizca aday havuzunu tanimlar. Ust uc daha cok renk/
# kapasiteyle zorlar (tavan (8,7,2)); sinirlar: kap<=8 (MaxLayers), renk<=8 (palet).
# FAZ 1'de bu merdiven grid'e genisleyecek (kap<=12, renk<=10); Faz 0'da AYNI.
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
TUTORIAL2_SLOT = 100           # tohum icin ranked slotlardan ayrik kimlik

CANDIDATES_PER_SLOT = 30    # slot basina KABUL edilen (SOLVABLE) hedef sayisi
MAX_ATTEMPTS_FACTOR = 20    # sonsuz donguye karsi: en fazla 30*20 deneme
SEED = 42

# Dort-terim skor agirliklari (toplam 1.0). Boyut (L,C) baskin, tuzak/affetmezlik
# (T,A) ikincil — tup/renk artisinda siralama monoton kalsin diye.
WEIGHTS = {"L": 0.45, "C": 0.25, "A": 0.15, "T": 0.15}

# Paralel isci sayisi: bir cekirdek makine nefes alsin diye bosta birakilir.
WORKERS = max(1, (os.cpu_count() or 2) - 1)


def seed_for(slot_idx, k):
    """Aday basina DETERMINISTIK tohum. Paralel calisma sirasi sonucu
    degistirmesin diye tohum (slot, aday#)'dan turetilir — rng'yi slotlar
    arasi tek iplikte gezdirmek yerine. Buyuk asal karisimi cakismayi onler."""
    return SEED * 1_000_003 + slot_idx * 10_007 + k


def _diag(board, cap, colors, empties, status, t0, *, shortest=None,
          sol_count=None, states=None, states_exact=True, dead=None):
    """evaluate_candidate ve metrics_for'un ortak aday-teshis dict'i.
    board tuple-of-tuple; JSON'a yazarken listeye cevrilir."""
    return {
        "status": status,
        "cap": cap, "colors": colors, "empties": empties,
        "volume": cap * colors,
        "board": board,
        "shortest": shortest,
        "sol_count": sol_count,
        "states": states,
        "states_exact": states_exact,
        "dead": dead,
        "eval_s": time.perf_counter() - t0,
    }


def evaluate_candidate(task):
    """TEK adayi uretir ve olcer — multiprocessing ISCISI.

    Modul duzeyinde olmak ZORUNDA: Windows 'spawn' bu fonksiyonu ve argumani
    pickle'lar. task = (kap, renk, bos, tohum); tohum -> taze random.Random.

    Donen dict'in 'status'u eleme/kabul sinifini verir:
      accepted         — tam metrik (solve tam tarandi; L,C,A,T hepsi var)
      accepted_partial — cozulebilir GARANTI (solvable_only), metrik KISMI
                         (buyuk tahta; tam-sayim duvari asildi). Faz 0'da olusmaz.
      unsolvable       — uzay tukendi, cozum yok (ya da partial'da solvable_only False)
      budget           — tam-sayim asti, solvable_only da onaylayamadi (bilinmiyor)
      short_budget     — solve SOLVABLE ama enKisa BFS butce asti (nadir)
      dead_budget      — solve SOLVABLE ama olu-oran butce asti (~imkansiz)
    """
    cap, colors, empties, seed = task
    rng = random.Random(seed)
    board = cc.generate(colors, cap, empties, rng)

    t0 = time.perf_counter()
    # Rastgele uretim kucuk tahtalarda ZATEN cozulmus bir tahta verebilir
    # (or. 4x2: ~%3/aday). Bu bir "level" degil; ele. (Ayrica states=0 olur,
    # make_scorer'daki log(durum) patlar — bu guard onu da onler.)
    if cc.is_solved(board, cap):
        return _diag(board, cap, colors, empties, "trivial", t0, states=0)

    verdict, states, sol_count, _first = cc.solve(board, cap)

    if verdict == "UNSOLVABLE":
        return _diag(board, cap, colors, empties, "unsolvable", t0, states=states)

    if verdict == "OUT_OF_BUDGET":
        # Tam sayim butceyi asti. Yalin "ilk cozumde dur" ile (daha comert butce,
        # durum basina daha az is) cozulebilirligi GARANTI etmeye calis.
        ok = cc.solvable_only(board, cap)
        if ok is False:
            return _diag(board, cap, colors, empties, "unsolvable", t0, states=states)
        if ok is None:
            return _diag(board, cap, colors, empties, "budget", t0, states=states)
        # ok True: cozulebilir garantili, metrikler kismi (belki None).
        shortest, _s, short_hit = cc.shortest_solution(board, cap)
        dratio, _r, dead_hit = cc.dead_ratio(board, cap)
        return _diag(board, cap, colors, empties, "accepted_partial", t0,
                     shortest=(None if short_hit else shortest),
                     sol_count=None, states=states, states_exact=False,
                     dead=(None if dead_hit else dratio))

    # SOLVABLE — tam olculdu. enKisa (BFS ilk cozum) ve olu-oran cek.
    shortest, _s, short_hit = cc.shortest_solution(board, cap)
    if short_hit or shortest is None:
        return _diag(board, cap, colors, empties, "short_budget", t0, states=states)
    dratio, _r, dead_hit = cc.dead_ratio(board, cap)
    if dead_hit or dratio is None:
        return _diag(board, cap, colors, empties, "dead_budget", t0, states=states)
    return _diag(board, cap, colors, empties, "accepted", t0,
                 shortest=shortest, sol_count=sol_count, states=states,
                 states_exact=(states < cc.BUDGET), dead=dratio)


def build_slot(pool, slot_idx, cap, colors, empties, target=CANDIDATES_PER_SLOT):
    """Bir slot icin >=target kabul edilmis aday uretir — PARALEL batch'lerle.

    Kabule kadar tek tek uretmek yerine, ihtiyac kadar adayi tek batch'te
    havuza (pool) dagitir; kabul yetmezse yeni (kucuk) batch. Determinizm
    seed_for'dan gelir; batch/isci sirasi sonucu degistirmez. cpu_seconds
    (toplam is) ile wall_seconds (gercek gecen, paralel) ayri raporlanir ->
    hizlanma = cpu/wall gorunur.
    """
    accepted = []
    counters = {"trivial": 0, "unsolvable": 0, "budget": 0,
                "short_budget": 0, "dead_budget": 0}
    attempts = 0
    cpu_seconds = 0.0
    max_attempts = target * MAX_ATTEMPTS_FACTOR
    wall0 = time.perf_counter()

    while len(accepted) < target and attempts < max_attempts:
        need = target - len(accepted)
        # Batch en az WORKERS kadar (cekirdekleri doldur), en cok kalan denemedar.
        batch_n = min(max(need, WORKERS), max_attempts - attempts)
        tasks = [(cap, colors, empties, seed_for(slot_idx, attempts + k))
                 for k in range(batch_n)]
        attempts += batch_n
        for r in pool.map(evaluate_candidate, tasks):
            cpu_seconds += r["eval_s"]
            if r["status"] in ("accepted", "accepted_partial"):
                accepted.append(r)
            else:
                counters[r["status"]] += 1

    return {
        "cap": cap, "colors": colors, "empties": empties,
        "accepted": accepted,
        "attempts": attempts,
        "cpu_seconds": cpu_seconds,
        "wall_seconds": time.perf_counter() - wall0,
        **counters,
    }


def make_scorer(all_cands):
    """Tum (TAM-metrik) havuzdan min-max sinirlarini cikarip skorlama
    fonksiyonu doner. Her terim once donusturulur (durum->log, cozum->-log),
    sonra havuz uzerinde [0,1]'e min-max normalize edilir. score(cand) ->
    {L,C,A,T,total}. Sinirlar da doner (rapora yazmak icin)."""
    shortest_vals = [c["shortest"] for c in all_cands]
    logstate_vals = [math.log(c["states"]) for c in all_cands]      # states>=1
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
    temsil eder. Donen [X.1, X.2]: X.1 hafifce kolay, X.2 hafifce zor."""
    ordered = sorted(accepted, key=key_fn)
    n = len(ordered)
    if n == 0:
        raise ValueError("bos aday listesi: temsilci secilemez")
    if n == 1:
        return [ordered[0], ordered[0]]
    lo = min(n // 2, n - 2)   # n=30 -> 15; kucuk n'de listeyi tasmayi engeller
    return [ordered[lo], ordered[lo + 1]]


def metrics_for(board, cap, colors, empties):
    """Elle kurulan (ogretici) bir tahtanin metriklerini hesaplar; ayni
    zamanda cozulebilirligini DOGRULAR (cozulemez/butce asilirsa hata firlatir).
    evaluate_candidate ile ayni teshis dict sekli doner."""
    t0 = time.perf_counter()
    verdict, states, sol_count, _first = cc.solve(board, cap)
    if verdict != "SOLVABLE":
        raise ValueError(f"Ogretici tahta cozulemez ({verdict}): {fmt_tubes(board)}")
    shortest, _s, short_hit = cc.shortest_solution(board, cap)
    if short_hit or shortest is None:
        raise ValueError(f"Ogretici enKisa butce asti: {fmt_tubes(board)}")
    dratio, _r, dead_hit = cc.dead_ratio(board, cap)
    if dead_hit or dratio is None:
        raise ValueError(f"Ogretici dead_ratio butce asti: {fmt_tubes(board)}")
    return _diag(board, cap, colors, empties, "accepted", t0,
                 shortest=shortest, sol_count=sol_count, states=states,
                 states_exact=(states < cc.BUDGET), dead=dratio)


def main():
    print(f"Level merdiveni (PARALEL, {WORKERS} isci) — skor dort-terim agirlikli: "
          f"L{WEIGHTS['L']} C{WEIGHTS['C']} A{WEIGHTS['A']} T{WEIGHTS['T']}\n")
    wall0 = time.perf_counter()

    # 1. RANKED slotlar + ogretici 2: uret+olc (havuz paylasilan multiprocessing
    #    Pool). Pool 'with' bloguyla acilir; tum uretim bu blokta biter.
    slot_results = []
    all_cands = []
    with mp.Pool(processes=WORKERS) as pool:
        for slot, (cap, colors, empties) in enumerate(LADDER, start=1):
            res = build_slot(pool, slot, cap, colors, empties)
            slot_results.append(res)
            all_cands.extend(res["accepted"])
            spd = res["cpu_seconds"] / res["wall_seconds"] if res["wall_seconds"] > 0 else 1.0
            print(f"Slot {slot:2d}  kap={cap} renk={colors} bos={empties} (hacim {cap * colors})")
            print(f"  deneme={res['attempts']:4d}  kabul={len(res['accepted']):3d}  "
                  f"ele: coz-mez={res['unsolvable']} butce={res['budget']} "
                  f"bfs={res['short_budget']} olu={res['dead_budget']} "
                  f"trivial={res['trivial']}  "
                  f"is={res['cpu_seconds']:6.1f}s  gercek={res['wall_seconds']:6.1f}s  "
                  f"hizlanma={spd:.1f}x")

        tut2_res = build_slot(pool, TUTORIAL2_SLOT, *TUTORIAL2_PARAMS)

    if not all_cands:
        print("HIC KABUL EDILEN ADAY YOK — cikiliyor.")
        return

    partials = [c for c in all_cands if c["status"] == "accepted_partial"]
    if partials:
        print(f"\nUYARI: {len(partials)} kismi-metrik aday (olcum duvari asildi). "
              "Kismi-metrik skorlama Faz 1 isi; bu adaylar su an SKORLAMAYA GIRMIYOR.")

    # 2. Skorla (yalniz TAM-metrik havuz) + her ranked slot icin ortadaki 2 sec.
    full = [c for c in all_cands if c["status"] == "accepted"]
    score_fn, bounds = make_scorer(full)
    ranked = []
    for res in slot_results:
        acc = [c for c in res["accepted"] if c["status"] == "accepted"]
        if not acc:
            continue
        two = choose_two(acc, lambda c: score_fn(c)["total"])
        tier_score = (score_fn(two[0])["total"] + score_fn(two[1])["total"]) / 2
        ranked.append({"cap": res["cap"], "colors": res["colors"],
                       "empties": res["empties"], "boards": two, "score": tier_score})
    ranked.sort(key=lambda r: r["score"])

    # 3. Ogretici tier'lar (SABIT, en basa). Tier 1 elle, Tier 2 uretici.
    tut1 = [metrics_for(b, TUTORIAL_CAP, 1, 0) for b in TUTORIAL1_BOARDS]
    tut2_full = [c for c in tut2_res["accepted"] if c["status"] == "accepted"]
    if not tut2_full:
        print("UYARI: ogretici 2 icin cozulebilir tahta uretilemedi — cikiliyor.")
        return
    tut2 = choose_two(tut2_full, lambda c: c["shortest"])

    # 4. Tier listesi: [ogretici1, ogretici2] + skora-gore-sirali ranked.
    tiers = [(TUTORIAL_CAP, tut1), (TUTORIAL2_PARAMS[0], tut2)]
    tiers += [(r["cap"], r["boards"]) for r in ranked]

    # Secilenleri isaretle (konsol + diagnostics.json).
    for t_idx, (cap, boards) in enumerate(tiers, start=1):
        for v_idx, c in enumerate(boards, start=1):
            c["selected"] = True
            c["label"] = f"{t_idx}.{v_idx}"

    total_wall = time.perf_counter() - wall0

    # Konsol: SECILEN tahtalar + skor bilesenleri (mentor istegi: tahta-basi).
    print("\n=== SECILEN TAHTALAR (skor bilesenleriyle) ===")
    for t_idx, (cap, boards) in enumerate(tiers, start=1):
        tip = "ogretici" if t_idx <= 2 else "ranked"
        print(f"Tier {t_idx:2d} ({tip})  kap={cap} renk={boards[0]['colors']} "
              f"bos={boards[0]['empties']}")
        for c in boards:
            if c["status"] == "accepted":
                s = score_fn(c)
                print(f"  {c['label']:>4}  enKisa={c['shortest']:3d}  "
                      f"cozum={c['sol_count']:5d}  durum={c['states']:8d}  "
                      f"olu={c['dead']:.3f}  "
                      f"skor L{s['L']:.2f} C{s['C']:.2f} A{s['A']:.2f} T{s['T']:.2f} "
                      f"= {s['total']:.3f}")
            else:
                print(f"  {c['label']:>4}  (kismi metrik: {c['status']}, "
                      f"enKisa={c['shortest']} olu={c['dead']})")

    levels_path, n_levels = write_pilot_levels(tiers)
    write_report(ranked, tut1, tut2, bounds, total_wall, score_fn)
    diag_path = write_diagnostics(slot_results, tut2_res, tut1, bounds,
                                  score_fn, total_wall)

    total_cpu = sum(r["cpu_seconds"] for r in slot_results) + tut2_res["cpu_seconds"]
    total_attempts = sum(r["attempts"] for r in slot_results) + tut2_res["attempts"]
    spd = total_cpu / total_wall if total_wall > 0 else 1.0
    print(f"\nGENEL: {len(tiers)} tier x2 = {n_levels} tahta  "
          f"toplam deneme={total_attempts}  is={total_cpu:.0f}s  "
          f"gercek={total_wall:.0f}s  hizlanma={spd:.1f}x")
    print(f"Cikti: {levels_path}")
    print(f"       pilot_ladder.md · {os.path.basename(diag_path)}")


def write_pilot_levels(tiers):
    """Tier listesini oyunun okudugu semaya yazar: Assets/Resources/
    pilot_levels.json. Her tier 2 tahta -> label X.1 / X.2 (ekranda "LEVEL
    1.1" gibi). Sema alanlari:
      level          — sirali int (1..N), LevelLibrary'nin indeksi
      label          — "tier.varyant" ("1.1", "1.2", "2.1", ...)
      capacity       — tup kapasitesi
      tubes[]        — dipten yukari virgullu; bos tup "" (ParseTube uyumlu)
      shortest       — enKisa cozum uzunlugu (ham metrik; C# okur, hesaplamaz)
      solutionCount  — cozum sayisi (ham metrik)
    Agirliga bagli SKOR yazilmaz, yalniz ham metrik: agirlik degisince yeniden
    siralanabilsin diye. (Skor + tum teshis -> diagnostics.json.)"""
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


def write_diagnostics(slot_results, tut2_res, tut1, bounds, score_fn, total_wall):
    """Tum teshis verisini makine-okur JSON'a doker: diagnostics.json.

    pilot_levels.json (oyun girdisi) SADECE ham metrik tutar (recalibrate icin);
    burada ise skor bilesenleri + sayaclar + zamanlar da var — analiz/ayar icin.
    Her slot: sayaclar + KABUL edilen tum adaylar (secilen isaretli). Boylece
    agirlik/knob ayari yaparken ham havuz elimizde olur."""
    def cand_json(c):
        sc = score_fn(c) if c["status"] == "accepted" else None
        return {
            "tubes": [list(t) for t in c["board"]],
            "status": c["status"],
            "cap": c["cap"], "colors": c["colors"], "empties": c["empties"],
            "volume": c["volume"],
            "shortest": c["shortest"],
            "solutionCount": c["sol_count"],
            "states": c["states"],
            "statesExact": c["states_exact"],
            "dead": (round(c["dead"], 5) if c["dead"] is not None else None),
            "score": ({k: round(v, 4) for k, v in sc.items()} if sc else None),
            "selected": c.get("selected", False),
            "label": c.get("label"),
            "evalSeconds": round(c["eval_s"], 4),
        }

    def slot_json(res, slot_id):
        return {
            "slot": slot_id,
            "cap": res["cap"], "colors": res["colors"], "empties": res["empties"],
            "volume": res["cap"] * res["colors"],
            "attempts": res["attempts"],
            "accepted": len(res["accepted"]),
            "eliminations": {
                "trivial": res["trivial"], "unsolvable": res["unsolvable"],
                "budget": res["budget"], "short_budget": res["short_budget"],
                "dead_budget": res["dead_budget"],
            },
            "cpuSeconds": round(res["cpu_seconds"], 2),
            "wallSeconds": round(res["wall_seconds"], 2),
            "candidates": [cand_json(c) for c in res["accepted"]],
        }

    data = {
        "config": {
            "seed": SEED,
            "weights": WEIGHTS,
            "candidatesPerSlot": CANDIDATES_PER_SLOT,
            "workers": WORKERS,
            "budget": cc.BUDGET,
            "solvableBudget": cc.SOLVABLE_BUDGET,
            "ceiling": {"note": "Faz 0: kap<=8 renk<=8"},
        },
        "wallSeconds": round(total_wall, 1),
        "bounds": {
            "shortest": [bounds["L"][0], bounds["L"][1]],
            "logStates": [round(bounds["C"][0], 4), round(bounds["C"][1], 4)],
            "negLogSolutions": [round(bounds["A"][0], 4), round(bounds["A"][1], 4)],
            "dead": [round(bounds["T"][0], 5), round(bounds["T"][1], 5)],
        },
        "tutorial1": [cand_json(c) for c in tut1],
        "slots": ([slot_json(res, i) for i, res in enumerate(slot_results, start=1)]
                  + [slot_json(tut2_res, TUTORIAL2_SLOT)]),
    }

    script_dir = os.path.dirname(os.path.abspath(__file__))
    out_path = os.path.join(script_dir, "diagnostics.json")
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)
    return out_path


def write_report(ranked, tut1, tut2, bounds, total_secs, score_fn):
    """pilot_ladder.md: (1) tier ozeti (ogretici + skora sirali ranked, her
    tier 2 tahta), (2) secilen 30 tahta (label ile)."""
    lines = []
    lines.append("# TubeSort — Level Merdiveni Raporu")
    lines.append("")
    lines.append(f"Seed `{SEED}` · slot basina {CANDIDATES_PER_SLOT} aday · "
                 f"tier basina orta-ust 2 aday (X.1, X.2) · PARALEL {WORKERS} isci · "
                 f"gercek {total_secs:.1f}s")
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
