"""TubeSort — pilot zorluk merdiveni + skor (PARALEL + tahta-basi teshis).

Parametre merdiveni (kapasite, renk, bos) uzerinden OLCULEN zorluk skorunun
merdiven sirasiyla uyusup uyusmadigini sinar. Cikti: analiz raporu
(pilot_ladder.md) + pilot_levels.json + diagnostics.json. Agirlik kalibrasyonu
oyuncu testiyle.

Uretim: FORWARD-RANDOM (uret-ve-test), 2 bos tup. Ters-uretim kullanilmiyor
(kullanici karari): erken leveller kucuk + az renk + 2 bos oldugundan ilk-hamle
cikmaz riski dusuk, gozeardi ediliyor.

Olcum PARALEL (multiprocessing, 8 cekirdek). Determinizm ADAY BASINA tohumdan
gelir (seed_for): calisma sirasi sonucu DEGISTIRMEZ. Buyuk tahtada tam olcum
zaten butceyi asar -> rejim-farkindaligiyla (MEASURABLE_VOLUME_MAX) yalin
solvable_only'e dusulur: cozulebilirlik GARANTI, siralama hacme gore. Boyle
"accepted_partial" adaylar Faz 0 tavaninda (kap<=8) OLUSMAZ; makine Faz A/1'in
yuksek tavani icin hazir.

Iki katman (karistirilmasin):
  - Parametre merdiveni: kaba zorluk sinifi + cesitlilik garantisi.
  - Olculen skor: ince siralama + slot ici aday secimi.

Skor: UC-TERIM AGIRLIKLI TOPLAM (eski dordunculu A=cozum-sayisi KALDIRILDI —
boyutla karismis/ters idi, ustelik tam-sayim solve() gerektiriyordu). Her terim
tum aday havuzu uzerinde [0,1]'e min-max normalize edilir.
  L (0.45) = enKisa cozum uzunlugu   -> plan uzunlugu ~= tahta HACMI (buyuk=zor)
  C (0.30) = log(durum sayisi)       -> dolasiklik/arama boyutu (buyuk=zor)
  T (0.25) = olu-durum orani         -> tuzak/affetmezlik yogunlugu (cok=zor)
L, C boyut eksenleri (uzunluk + dolasiklik); T affedicilik eksenidir.

Slot ici secim: adaylar SKORA gore siralanir, ORTANIN HEMEN USTUNDEKI 2 aday
temsilci alinir (her tier ekranda X.1/X.2 diye 2 tahta gosterilir). Ogretici
tier'lar (1-2) bunun disinda: SABIT, skora girmez.

Uretim maliyeti: A gidince tam-sayim solve() atlandi (~%40 kazanc); metrikler
2 taramadan gelir: shortest (L + cozulebilirlik) + dead_ratio (C=durum, T=olu).

Calistirma:  python pilot_ladder.py
"""

import json
import math
import multiprocessing as mp
import os
import random
import time

import crosscheck as cc

# Parametre merdiveni: (kapasite, renk, bos). Her tahta HEP 2 bos tup icerir.
# Zorluk (kapasite, renk) artisindan gelir. Tup = renk+2, hacim = renk*kap.
# FAZ 0: mevcut tavan (kap<=8, renk<=8). FAZ 1'de grid'e genisleyecek
# (kap<=12, renk<=10); merdiven yalnizca aday havuzunu tanimlar, siralama skora.
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

# Ogretici tier'lar: ekranin en basi, SABIT sira (skora GIRMEZLER).
#   Tier 1: tek renk, 2 tup — uretici tek rengi karistiramaz, ELLE.
#   Tier 2: 2 renk, 2 bos, 4 tup — uretici uretir; 2 bos -> cikmaz ~imkansiz.
TUTORIAL_CAP = 4
TUTORIAL_COLOR = 0
TUTORIAL1_BOARDS = [
    ((TUTORIAL_COLOR,), (TUTORIAL_COLOR,) * 3),      # 1.1: 1 + 3
    ((TUTORIAL_COLOR,) * 2, (TUTORIAL_COLOR,) * 2),  # 1.2: 2 + 2
]
TUTORIAL2_PARAMS = (4, 2, 2)   # (kap, renk, bos)
TUTORIAL2_SLOT = 100           # tohum icin ranked slotlardan ayrik kimlik

# slot basina KABUL edilen hedef (30'dan dusuruldu: hiz)
CANDIDATES_PER_SLOT = 20
MAX_ATTEMPTS_FACTOR = 20    # sonsuz donguye karsi tavan
SEED = 42

# Uc-terim skor agirliklari (toplam 1.0). A (cozum sayisi) kaldirildi.
WEIGHTS = {"L": 0.45, "C": 0.30, "T": 0.25}

# Tam metrik (shortest BFS + dead_ratio tam tarama) bu hacme kadar denenir.
# Ustunde tam tarama zaten butceyi asar -> hic denenmez, yalin solvable_only ile
# cozulebilirlik GARANTI edilir ve tahta hacme gore siralanir. Faz 0 tavaninda
# (maks hacim 56) tetiklenmez; Faz A/1'de yuksek tavan icin devreye girer.
MEASURABLE_VOLUME_MAX = 64

# Paralel isci sayisi: bir cekirdek makine nefes alsin diye bosta birakilir.
WORKERS = max(1, (os.cpu_count() or 2) - 1)


def seed_for(slot_idx, k):
    """Aday basina DETERMINISTIK tohum. Paralel calisma sirasi sonucu
    degistirmesin diye tohum (slot, aday#)'dan turetilir."""
    return SEED * 1_000_003 + slot_idx * 10_007 + k


def _diag(board, cap, colors, empties, status, t0, *, shortest=None,
          states=None, states_exact=True, dead=None):
    """evaluate_candidate ve metrics_for'un ortak aday-teshis dict'i."""
    return {
        "status": status,
        "cap": cap, "colors": colors, "empties": empties,
        "volume": cap * colors,
        "board": board,
        "shortest": shortest,   # L (None = kismi/olculemedi)
        "states": states,       # C girdisi = erisilebilir durum sayisi
        "states_exact": states_exact,
        "dead": dead,           # T = olu-durum orani
        "eval_s": time.perf_counter() - t0,
    }


def evaluate_candidate(task):
    """TEK adayi uretir ve olcer — multiprocessing ISCISI (modul duzeyinde
    olmak ZORUNDA: Windows spawn pickle eder). task=(kap,renk,bos,tohum).

    Metrik boru hatti (A/tam-sayim solve() YOK):
      L + cozulebilirlik <- shortest_solution (ilk cozumde durur)
      C (durum) + T (olu) <- tek dead_ratio taramasi

    'status':
      accepted         — tam metrik (L,C,T hepsi var)
      accepted_partial — cozulebilir GARANTI ama metrik kismi (buyuk tahta;
                         olcum duvari). Faz 0 tavaninda olusmaz.
      trivial          — uretim zaten cozulmus tahta verdi, elendi
      unsolvable       — uzay tukendi, cozum yok
      budget           — solvable_only bile onaylayamadi (bilinmiyor)
    """
    cap, colors, empties, seed = task
    rng = random.Random(seed)
    board = cc.generate(colors, cap, empties, rng)
    volume = cap * colors

    t0 = time.perf_counter()
    # Rastgele uretim kucuk tahtada ZATEN cozulmus tahta verebilir -> ele.
    if cc.is_solved(board, cap):
        return _diag(board, cap, colors, empties, "trivial", t0, states=0)

    # REJIM-FARKINDA: buyuk tahtada tam metrik butceyi asar -> denemeyip yalin
    # solvable_only ile cozulebilirligi GARANTI et, hacme gore sirala.
    if volume > MEASURABLE_VOLUME_MAX:
        ok = cc.solvable_only(board, cap)
        if ok is False:
            return _diag(board, cap, colors, empties, "unsolvable", t0)
        if ok is None:
            return _diag(board, cap, colors, empties, "budget", t0)
        return _diag(board, cap, colors, empties, "accepted_partial", t0,
                     shortest=None, states=None, states_exact=False, dead=None)

    # --- OLCULEBILIR REJIM ---
    # L + cozulebilirlik: enKisa BFS. None+bitmis = cozulemez; None+butce = solvable_only'e dus.
    shortest, _ss, short_hit = cc.shortest_solution(board, cap)
    if shortest is None and not short_hit:
        return _diag(board, cap, colors, empties, "unsolvable", t0)
    if short_hit:
        ok = cc.solvable_only(board, cap)
        if ok is False:
            return _diag(board, cap, colors, empties, "unsolvable", t0)
        if ok is None:
            return _diag(board, cap, colors, empties, "budget", t0)
        return _diag(board, cap, colors, empties, "accepted_partial", t0,
                     shortest=None, states=None, states_exact=False, dead=None)

    # C + T: tek dead_ratio taramasi (reachable = durum sayisi, dratio = olu-oran).
    dratio, reach, dead_hit = cc.dead_ratio(board, cap)
    if dead_hit:
        # enKisa cozuldu ama tam graf bitmedi (nadir) -> kismi (L var, C/T yok).
        return _diag(board, cap, colors, empties, "accepted_partial", t0,
                     shortest=shortest, states=None, states_exact=False, dead=None)
    return _diag(board, cap, colors, empties, "accepted", t0,
                 shortest=shortest, states=reach, states_exact=True, dead=dratio)


def build_slot(pool, slot_idx, cap, colors, empties, target=CANDIDATES_PER_SLOT):
    """Bir slot icin >=target kabul edilmis aday uretir — PARALEL batch'lerle.
    cpu_seconds (toplam is) ile wall_seconds (gercek gecen) ayri raporlanir."""
    accepted = []
    counters = {"trivial": 0, "unsolvable": 0, "budget": 0}
    attempts = 0
    cpu_seconds = 0.0
    max_attempts = target * MAX_ATTEMPTS_FACTOR
    wall0 = time.perf_counter()

    while len(accepted) < target and attempts < max_attempts:
        need = target - len(accepted)
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
    fonksiyonu doner. score(cand) -> {L,C,T,total}."""
    shortest_vals = [c["shortest"] for c in all_cands]
    logstate_vals = [math.log(c["states"]) for c in all_cands]   # states>=1
    dead_vals = [c["dead"] for c in all_cands]

    bounds = {
        "L": (min(shortest_vals), max(shortest_vals)),
        "C": (min(logstate_vals), max(logstate_vals)),
        "T": (min(dead_vals), max(dead_vals)),
    }

    def nz(x, lo, hi):
        return 0.0 if hi <= lo else (x - lo) / (hi - lo)

    def score(c):
        L = nz(c["shortest"], *bounds["L"])
        C = nz(math.log(c["states"]), *bounds["C"])
        T = nz(c["dead"], *bounds["T"])
        total = WEIGHTS["L"] * L + WEIGHTS["C"] * C + WEIGHTS["T"] * T
        return {"L": L, "C": C, "T": T, "total": total}

    return score, bounds


def choose_two(accepted, key_fn):
    """key_fn'e gore sirala, ORTANIN HEMEN USTUNDEKI 2 temsilciyi dondur.
    Donen [X.1, X.2]: X.1 hafifce kolay, X.2 hafifce zor."""
    ordered = sorted(accepted, key=key_fn)
    n = len(ordered)
    if n == 0:
        raise ValueError("bos aday listesi: temsilci secilemez")
    if n == 1:
        return [ordered[0], ordered[0]]
    lo = min(n // 2, n - 2)
    return [ordered[lo], ordered[lo + 1]]


def metrics_for(board, cap, colors, empties):
    """Elle kurulan (ogretici) tahtanin metrikleri; cozulebilirligi DOGRULAR."""
    t0 = time.perf_counter()
    shortest, _s, short_hit = cc.shortest_solution(board, cap)
    if shortest is None:
        raise ValueError(f"Ogretici cozulemez/butce asti: {fmt_tubes(board)}")
    dratio, reach, dead_hit = cc.dead_ratio(board, cap)
    if dead_hit:
        raise ValueError(f"Ogretici dead_ratio butce asti: {fmt_tubes(board)}")
    return _diag(board, cap, colors, empties, "accepted", t0,
                 shortest=shortest, states=reach, states_exact=True, dead=dratio)


def main():
    print(f"Level merdiveni (PARALEL, {WORKERS} isci) — skor uc-terim agirlikli: "
          f"L{WEIGHTS['L']} C{WEIGHTS['C']} T{WEIGHTS['T']}\n")
    wall0 = time.perf_counter()

    slot_results = []
    all_cands = []
    with mp.Pool(processes=WORKERS) as pool:
        for slot, (cap, colors, empties) in enumerate(LADDER, start=1):
            res = build_slot(pool, slot, cap, colors, empties)
            slot_results.append(res)
            all_cands.extend(res["accepted"])
            spd = res["cpu_seconds"] / \
                res["wall_seconds"] if res["wall_seconds"] > 0 else 1.0
            print(
                f"Slot {slot:2d}  kap={cap} renk={colors} bos={empties} (hacim {cap * colors})")
            print(f"  deneme={res['attempts']:4d}  kabul={len(res['accepted']):3d}  "
                  f"ele: coz-mez={res['unsolvable']} butce={res['budget']} "
                  f"trivial={res['trivial']}  "
                  f"is={res['cpu_seconds']:6.1f}s  gercek={res['wall_seconds']:6.1f}s  "
                  f"hizlanma={spd:.1f}x")

        tut2_res = build_slot(pool, TUTORIAL2_SLOT, *TUTORIAL2_PARAMS)

    if not all_cands:
        print("HIC KABUL EDILEN ADAY YOK — cikiliyor.")
        return

    # NOT (Faz 1): accepted_partial adaylar SKORLAMAYA girmez (L/C/T yok);
    # hard tier'larda hacme-gore siralama Faz 1 isi. Faz 0 tavaninda olusmaz.
    partials = [c for c in all_cands if c["status"] == "accepted_partial"]
    if partials:
        print(f"\nUYARI: {len(partials)} kismi-metrik aday (olcum duvari). "
              "Hacme-gore siralama Faz 1 isi; su an SKORLAMAYA GIRMIYOR.")

    # Skorla (yalniz tam-metrik) + her ranked slot icin ortadaki 2 sec.
    full = [c for c in all_cands if c["status"] == "accepted"]
    score_fn, bounds = make_scorer(full)
    ranked = []
    for res in slot_results:
        acc = [c for c in res["accepted"] if c["status"] == "accepted"]
        if not acc:
            continue
        two = choose_two(acc, lambda c: score_fn(c)["total"])
        tier_score = (score_fn(two[0])["total"] +
                      score_fn(two[1])["total"]) / 2
        ranked.append({"cap": res["cap"], "colors": res["colors"],
                       "empties": res["empties"], "boards": two, "score": tier_score})
    ranked.sort(key=lambda r: r["score"])

    # Ogretici tier'lar (SABIT, en basa).
    tut1 = [metrics_for(b, TUTORIAL_CAP, 1, 0) for b in TUTORIAL1_BOARDS]
    tut2_full = [c for c in tut2_res["accepted"] if c["status"] == "accepted"]
    if not tut2_full:
        print("UYARI: ogretici 2 icin cozulebilir tahta uretilemedi — cikiliyor.")
        return
    tut2 = choose_two(tut2_full, lambda c: c["shortest"])

    tiers = [(TUTORIAL_CAP, tut1), (TUTORIAL2_PARAMS[0], tut2)]
    tiers += [(r["cap"], r["boards"]) for r in ranked]

    for t_idx, (cap, boards) in enumerate(tiers, start=1):
        for v_idx, c in enumerate(boards, start=1):
            c["selected"] = True
            c["label"] = f"{t_idx}.{v_idx}"

    total_wall = time.perf_counter() - wall0

    # Konsol: SECILEN tahtalar + skor bilesenleri (tahta-basi).
    print("\n=== SECILEN TAHTALAR (skor bilesenleriyle) ===")
    for t_idx, (cap, boards) in enumerate(tiers, start=1):
        tip = "ogretici" if t_idx <= 2 else "ranked"
        print(f"Tier {t_idx:2d} ({tip})  kap={cap} renk={boards[0]['colors']} "
              f"bos={boards[0]['empties']}")
        for c in boards:
            if c["status"] == "accepted":
                s = score_fn(c)
                print(f"  {c['label']:>4}  enKisa={c['shortest']:3d}  "
                      f"durum={c['states']:8d}  olu={c['dead']:.3f}  "
                      f"skor L{s['L']:.2f} C{s['C']:.2f} T{s['T']:.2f} = {s['total']:.3f}")
            else:
                print(
                    f"  {c['label']:>4}  (kismi metrik: {c['status']}, hacim={c['volume']})")

    levels_path, n_levels = write_pilot_levels(tiers)
    write_report(ranked, tut1, tut2, bounds, total_wall, score_fn)
    diag_path = write_diagnostics(slot_results, tut2_res, tut1, bounds,
                                  score_fn, total_wall)

    total_cpu = sum(r["cpu_seconds"]
                    for r in slot_results) + tut2_res["cpu_seconds"]
    total_attempts = sum(r["attempts"]
                         for r in slot_results) + tut2_res["attempts"]
    spd = total_cpu / total_wall if total_wall > 0 else 1.0
    print(f"\nGENEL: {len(tiers)} tier x2 = {n_levels} tahta  "
          f"toplam deneme={total_attempts}  is={total_cpu:.0f}s  "
          f"gercek={total_wall:.0f}s  hizlanma={spd:.1f}x")
    print(f"Cikti: {levels_path}")
    print(f"       pilot_ladder.md · {os.path.basename(diag_path)}")


def write_pilot_levels(tiers):
    """Oyunun okudugu sema: Assets/Resources/pilot_levels.json.
    C# (LevelLibrary) yalniz level/label/capacity/tubes okur; ham metrikler
    (shortest, states, dead) recalibrate/analiz icin tasinir (C# yok sayar).
    Agirliga bagli SKOR yazilmaz -> agirlik degisince yeniden siralanabilir."""
    levels = []
    for t_idx, (cap, boards) in enumerate(tiers, start=1):
        for v_idx, c in enumerate(boards, start=1):
            levels.append({
                "level": len(levels) + 1,
                "label": f"{t_idx}.{v_idx}",
                "capacity": cap,
                "tubes": [",".join(str(x) for x in tube) for tube in c["board"]],
                "shortest": c["shortest"],
                "states": c["states"],
                "dead": (round(c["dead"], 5) if c["dead"] is not None else None),
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
        parts.append("[" + ",".join(str(c)
                     for c in tube) + "]" if tube else "()")
    return " ".join(parts)


def write_diagnostics(slot_results, tut2_res, tut1, bounds, score_fn, total_wall):
    """Tum teshis verisi -> diagnostics.json (skor bilesenleri + sayaclar +
    zamanlar; her slotta KABUL edilen tum adaylar, secilen isaretli)."""
    def cand_json(c):
        sc = score_fn(c) if c["status"] == "accepted" else None
        return {
            "tubes": [list(t) for t in c["board"]],
            "status": c["status"],
            "cap": c["cap"], "colors": c["colors"], "empties": c["empties"],
            "volume": c["volume"],
            "shortest": c["shortest"],
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
                "budget": res["budget"],
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
            "measurableVolumeMax": MEASURABLE_VOLUME_MAX,
            "ceiling": {"note": "Faz 0: kap<=8 renk<=8"},
        },
        "wallSeconds": round(total_wall, 1),
        "bounds": {
            "shortest": [bounds["L"][0], bounds["L"][1]],
            "logStates": [round(bounds["C"][0], 4), round(bounds["C"][1], 4)],
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
    """pilot_ladder.md: (1) tier ozeti, (2) secilen tahtalar (label ile)."""
    lines = []
    lines.append("# TubeSort — Level Merdiveni Raporu")
    lines.append("")
    lines.append(f"Seed `{SEED}` · slot basina {CANDIDATES_PER_SLOT} aday · "
                 f"tier basina orta-ust 2 aday (X.1, X.2) · PARALEL {WORKERS} isci · "
                 f"gercek {total_secs:.1f}s")
    lines.append("")
    lines.append("Skor **uc-terim agirlikli toplam** "
                 f"(L={WEIGHTS['L']} C={WEIGHTS['C']} T={WEIGHTS['T']}); "
                 "her terim ranked havuz uzerinde [0,1]'e min-max normalize. "
                 "L=enKisa, C=log(durum), T=olu-durum orani. "
                 "(A=cozum-sayisi kaldirildi.) Ogretici tier'lar (1-2) SABIT.")
    lines.append("")
    lines.append("Normalizasyon sinirlari (ranked havuz min/maks): "
                 f"enKisa `{bounds['L'][0]}..{bounds['L'][1]}`, "
                 f"log(durum) `{bounds['C'][0]:.2f}..{bounds['C'][1]:.2f}`, "
                 f"olu `{bounds['T'][0]:.3f}..{bounds['T'][1]:.3f}`.")
    lines.append("")

    def tier_row(tier_no, tip, cap, colors, empties, boards, score=None):
        b1, b2 = boards
        sc = f"{score:.3f}" if score is not None else "—"
        return (f"| {tier_no} | {tip} | {cap} | {colors} | {empties} "
                f"| {b1['shortest']},{b2['shortest']} "
                f"| {b1['dead']:.2f},{b2['dead']:.2f} | {sc} |")

    lines.append("## 1. Tier ozeti (ekran sirasi)")
    lines.append("")
    lines.append("Ogretici 1-2 sabit; 3+ skora gore artan. enKisa/olu = "
                 "iki tahtanin (X.1, X.2) degerleri. skor~ = ortalama.")
    lines.append("")
    lines.append(
        "| tier | tip | kap | renk | bos | enKisa(1,2) | olu(1,2) | skor~ |")
    lines.append("|--:|:--|--:|--:|--:|:--|:--|--:|")
    lines.append(tier_row(1, "ogretici", TUTORIAL_CAP, 1, 0, tut1))
    lines.append(tier_row(2, "ogretici", *TUTORIAL2_PARAMS, tut2))
    for i, r in enumerate(ranked, start=3):
        lines.append(tier_row(i, "ranked", r["cap"], r["colors"], r["empties"],
                              r["boards"], r["score"]))
    lines.append("")

    lines.append("## 2. Secilen tahtalar (label)")
    lines.append("")

    def board_lines(tier_no, cap, colors, empties, boards):
        out = []
        for v, c in enumerate(boards, start=1):
            out.append(f"- **{tier_no}.{v}** (kap={cap} renk={colors} bos={empties}) "
                       f"enKisa={c['shortest']} durum={c['states']} "
                       f"olu={c['dead']:.3f}: `{fmt_tubes(c['board'])}`")
        return out

    lines += board_lines(1, TUTORIAL_CAP, 1, 0, tut1)
    lines += board_lines(2, *TUTORIAL2_PARAMS, tut2)
    for i, r in enumerate(ranked, start=3):
        lines += board_lines(i, r["cap"], r["colors"],
                             r["empties"], r["boards"])

    script_dir = os.path.dirname(os.path.abspath(__file__))
    out_path = os.path.join(script_dir, "pilot_ladder.md")
    with open(out_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")


if __name__ == "__main__":
    main()
