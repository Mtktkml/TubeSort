"""TubeSort — 300-level uretici + zorluk skoru (PARALEL + tahta-basi teshis).

Dengeli (kap, renk) GRID (|kap-renk|<=BALANCE) uzerinde forward-random
uret-ve-test. Cikti: pilot_ladder.md + pilot_levels.json + diagnostics.json.
Agirlik kalibrasyonu oyuncu testiyle.

SIRALAMA (kullanici karari): bantlar hacme gore dizilir -> hem tup DERINLIGI
(kapasite) hem tup SAYISI (renk) GENELDE artar. Buyuk "derin-az tup <-> sig-cok
tup" ziplamasi YOK (BALANCE=2 -> tie'larda en fazla +-2 salinim). Olculebilir
bantlar (hacim, kap)'a; HARD bantlar (skor yok) (hacim, RENK)'e gore siralanir
(renk = zorluk vekili: az->cok renk = kolay->zor). Skora gore YENIDEN SIRALAMA
YOK; olculen skor yalniz BANT ICI kolay->zor tahta seciminde. ~34 durum; 300
level = durum basina ~9 tahta (ayni kap x renk, farkli dizilis).
(STRICT monoton "ikisi de hic azalmaz" integer kap/renk ile ancak ~16 durum ->
durum basi ~19 tahta cok tekrarli olurdu; BALANCE=2 kabul edilen kucuk salinim.)

BASLANGIC KOSULU: hicbir tahtada baslangicta TAMAMLANMIS (dolu tek-renk) tup
olmaz — kullanici hamle yapmadan tipali/cozulmus tup gorunmesin.

Uretim: FORWARD-RANDOM, 2 bos tup (ters-uretim yok). Olcum PARALEL. Determinizm
aday basina tohumdan (seed_for).

Metrik (cozum-sayisi/tam-sayim solve() YOK):
  L <- shortest_solution (enKisa) + cozulebilirlik
  C (durum) + T (olu) <- tek dead_ratio taramasi
Hacim > MEASURABLE_VOLUME_MAX -> yalin solvable_only (cozulebilirlik GARANTI,
metrik kismi; "hard" bantlar).

Skor (BANT ICI): UC-TERIM AGIRLIKLI, olculebilir havuzda [0,1] normalize.
  L 0.45 (enKisa) · C 0.30 (log durum) · T 0.25 (olu-oran)

Calistirma:  python pilot_ladder.py
"""

import json
import math
import multiprocessing as mp
import os
import random
import time

import crosscheck as cc

# --- GRID: dengeli (kap, renk) kombinasyonlari. Kap<=12 (MaxLayers), renk<=10
# (palet). |kap-renk|<=BALANCE buyuk "derin-az <-> sig-cok" ziplamalarini eler.
GRID_CAPS = range(4, 13)      # 4..12
GRID_COLORS = range(3, 11)    # 3..10
# |kap - renk| tavani (cesitlilik <-> flip dengesi)
BALANCE = 2

# Tam metrik bu hacme kadar denenir; ustunde yalin solvable_only (metrik kismi).
MEASURABLE_VOLUME_MAX = 64

TARGET_TOTAL = 300            # ogretici dahil hedef level sayisi
CANDIDATES_PER_SLOT = 18      # durum basina KABUL edilen havuz (~9 secilir)
MAX_ATTEMPTS_FACTOR = 20      # sonsuz donguye karsi tavan
SEED = 42

# Uc-terim skor agirliklari (toplam 1.0) — BANT ICI kolay->zor secim icin.
WEIGHTS = {"L": 0.45, "C": 0.30, "T": 0.25}

# Hard-rejim cozulebilirlik butcesi (cozulemez tahta hizli elensin).
HARD_SOLVABLE_BUDGET = cc.BUDGET

WORKERS = max(1, (os.cpu_count() or 2) - 1)


def _build_ladder():
    """Dengeli GRID'i durum sirasina dizer. Hep hacme gore; olculebilir bantlar
    (hacim, kap)'a (kabul edilen derinlik-artan rampa, gercek skoru var), HARD
    bantlar (hacim, renk)'e (skor yok, renk zorluk vekili). Ikisi de hacimle
    dizildiginden olculebilir (<=tavan) once, hard sonra gelir."""
    combos = [(cap, colors, 2) for cap in GRID_CAPS for colors in GRID_COLORS
              if abs(cap - colors) <= BALANCE]
    meas = sorted((c for c in combos if c[0] * c[1] <= MEASURABLE_VOLUME_MAX),
                  key=lambda t: (t[0] * t[1], t[0]))
    hard = sorted((c for c in combos if c[0] * c[1] > MEASURABLE_VOLUME_MAX),
                  key=lambda t: (t[0] * t[1], t[1]))
    return meas + hard


LADDER = _build_ladder()

# --- Ogretici tier'lar (SABIT, en basta; skora girmez).
TUTORIAL_CAP = 4
TUTORIAL_COLOR = 0
TUTORIAL1_BOARDS = [
    ((TUTORIAL_COLOR,), (TUTORIAL_COLOR,) * 3),      # 1: 1 + 3
    ((TUTORIAL_COLOR,) * 2, (TUTORIAL_COLOR,) * 2),  # 2: 2 + 2
]
TUTORIAL2_PARAMS = (4, 2, 2)   # (kap, renk, bos)
TUTORIAL2_SLOT = 1000          # tohum icin GRID durumlarindan ayrik kimlik


def seed_for(slot_idx, k):
    """Aday basina DETERMINISTIK tohum (paralel sira sonucu degistirmesin)."""
    return SEED * 1_000_003 + slot_idx * 10_007 + k


def _diag(board, cap, colors, status, t0, *, shortest=None, states=None, dead=None):
    """evaluate_candidate ve metrics_for'un ortak aday-teshis dict'i. Elenen
    adaylarda (precomplete/unsolvable/budget) metrik alanlari None kalir; yalniz
    status okunur. states/dead None = olculemedi (kismi ya da elenmis)."""
    return {
        "status": status,
        "cap": cap, "colors": colors,
        "volume": cap * colors,
        "board": board,
        "shortest": shortest,
        "states": states,
        "dead": dead,
        "eval_s": time.perf_counter() - t0,
    }


def evaluate_candidate(task):
    """TEK adayi uretir ve olcer — multiprocessing ISCISI (modul duzeyinde
    ZORUNDA; Windows spawn pickle eder). task=(kap,renk,bos,tohum). Donen
    'status': accepted / accepted_partial / precomplete / unsolvable / budget."""
    cap, colors, empties, seed = task
    rng = random.Random(seed)
    board = cc.generate(colors, cap, empties, rng)
    volume = cap * colors
    t0 = time.perf_counter()

    # Baslangicta TAMAMLANMIS (dolu tek-renk) tup OLMAMALI (bos tupler haric).
    # is_solved bunun ozel hali (tum tupler tamam) -> o da burada elenir.
    if any(t and cc.is_complete(t, cap) for t in board):
        return _diag(board, cap, colors, "precomplete", t0)

    # HARD rejim: tam olcum butceyi asar -> yalin solvable_only (cozulebilirlik
    # GARANTI, metrik kismi), hacme gore siralanir.
    if volume > MEASURABLE_VOLUME_MAX:
        ok = cc.solvable_only(board, cap, max_states=HARD_SOLVABLE_BUDGET)
        if ok is False:
            return _diag(board, cap, colors, "unsolvable", t0)
        if ok is None:
            return _diag(board, cap, colors, "budget", t0)
        return _diag(board, cap, colors, "accepted_partial", t0)

    # OLCULEBILIR rejim: L + cozulebilirlik <- shortest; C + T <- dead_ratio.
    shortest, _, short_hit = cc.shortest_solution(board, cap)
    if shortest is None and not short_hit:
        return _diag(board, cap, colors, "unsolvable", t0)
    if short_hit:                       # enKisa BFS butce asti -> yalin kontrol
        ok = cc.solvable_only(board, cap, max_states=HARD_SOLVABLE_BUDGET)
        if ok is False:
            return _diag(board, cap, colors, "unsolvable", t0)
        if ok is None:
            return _diag(board, cap, colors, "budget", t0)
        return _diag(board, cap, colors, "accepted_partial", t0)

    dratio, reach, dead_hit = cc.dead_ratio(board, cap)
    if dead_hit:                        # nadir: enKisa cozuldu, tam graf bitmedi
        return _diag(board, cap, colors, "accepted_partial", t0, shortest=shortest)
    return _diag(board, cap, colors, "accepted", t0,
                 shortest=shortest, states=reach, dead=dratio)


def build_slot(pool, slot_idx, cap, colors, empties, target=CANDIDATES_PER_SLOT):
    """Bir durum icin >=target kabul edilmis aday uretir — PARALEL batch'lerle.
    cpu_seconds (toplam is) ile wall_seconds (gercek gecen) ayri raporlanir."""
    accepted = []
    counters = {"precomplete": 0, "unsolvable": 0, "budget": 0}
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
        "cap": cap, "colors": colors,
        "accepted": accepted, "attempts": attempts,
        "cpu_seconds": cpu_seconds, "wall_seconds": time.perf_counter() - wall0,
        **counters,
    }


def make_scorer(all_cands):
    """OLCULEBILIR havuzdan min-max sinirlari + skorlama (BANT ICI secim icin).
    score(cand) -> {L,C,T,total}."""
    shortest_vals = [c["shortest"] for c in all_cands]
    logstate_vals = [math.log(c["states"]) for c in all_cands]
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
        return {"L": L, "C": C, "T": T,
                "total": WEIGHTS["L"] * L + WEIGHTS["C"] * C + WEIGHTS["T"] * T}

    return score, bounds


def choose_spread(items, n, key=None):
    """items'i (varsa key'e gore sirali) n temsilciye YAYARAK sec — bant ici
    kolay->zor pürüzsüzlestirme. n>=m ise hepsi doner."""
    ordered = sorted(items, key=key) if key else list(items)
    m = len(ordered)
    if m == 0 or n <= 0:
        return []
    if n >= m:
        return ordered
    if n == 1:
        return [ordered[m // 2]]
    return [ordered[round(i * (m - 1) / (n - 1))] for i in range(n)]


def metrics_for(board, cap, colors):
    """Elle kurulan (ogretici) tahtanin metrikleri; cozulebilirligi DOGRULAR."""
    t0 = time.perf_counter()
    shortest, _, _ = cc.shortest_solution(board, cap)
    if shortest is None:
        raise ValueError(f"Ogretici cozulemez/butce: {fmt_tubes(board)}")
    dratio, reach, dead_hit = cc.dead_ratio(board, cap)
    if dead_hit:
        raise ValueError(f"Ogretici dead_ratio butce: {fmt_tubes(board)}")
    return _diag(board, cap, colors, "accepted", t0,
                 shortest=shortest, states=reach, dead=dratio)


def main():
    print(f"300-level uretimi (PARALEL {WORKERS}) — dengeli GRID {len(LADDER)} durum "
          f"(|kap-renk|<={BALANCE}), hedef {TARGET_TOTAL} level, aday/durum {CANDIDATES_PER_SLOT}")
    print(f"siralama hacme gore (olc:(hacim,kap), hard:(hacim,renk)); skor yalniz "
          f"durum ici secim. olculebilir tavan hacim<={MEASURABLE_VOLUME_MAX}\n")
    wall0 = time.perf_counter()

    # 1. Tum durumlari uret+olc (paylasilan Pool) + ogretici 2.
    band_results = []
    all_measurable = []
    with mp.Pool(processes=WORKERS) as pool:
        for idx, (cap, colors, empties) in enumerate(LADDER, start=1):
            res = build_slot(pool, idx, cap, colors, empties)
            band_results.append(res)
            all_measurable.extend(
                c for c in res["accepted"] if c["status"] == "accepted")
            vol, meas = cap * colors, cap * colors <= MEASURABLE_VOLUME_MAX
            print(f"Durum {idx:2d}/{len(LADDER)} kap={cap:2d} renk={colors:2d} vol={vol:3d} "
                  f"[{'olc ' if meas else 'hard'}] deneme={res['attempts']:3d} "
                  f"kabul={len(res['accepted']):2d} "
                  f"ele(cz{res['unsolvable']}/bt{res['budget']}/pc{res['precomplete']}) "
                  f"is={res['cpu_seconds']:6.1f}s ger={res['wall_seconds']:6.1f}s")
        tut2_res = build_slot(pool, TUTORIAL2_SLOT, *TUTORIAL2_PARAMS)

    if not all_measurable:
        print("HIC olculebilir aday yok — cikiliyor.")
        return

    score_fn, bounds = make_scorer(all_measurable)

    # 2. Durumlar LADDER sirasinda (yeniden siralama yok; yapisal ramp korunur).
    #    Olculebilir durum -> tam-metrik adaylar; hard durum -> kismi adaylar.
    bands = []
    for res in band_results:
        cap, colors = res["cap"], res["colors"]
        vol = cap * colors
        meas = vol <= MEASURABLE_VOLUME_MAX
        want = "accepted" if meas else "accepted_partial"
        cands = [c for c in res["accepted"] if c["status"] == want]
        if not cands:
            print(
                f"UYARI: durum kap={cap} renk={colors} bos aday — atlaniyor.")
            continue
        mean_score = (sum(score_fn(c)["total"] for c in cands) / len(cands)
                      if meas else None)
        bands.append({"cap": cap, "colors": colors, "vol": vol,
                      "measurable": meas, "mean_score": mean_score, "cands": cands})

    # 3. Ogretici (4 tahta) — en basta, sabit.
    tut1 = [metrics_for(bd, TUTORIAL_CAP, 1) for bd in TUTORIAL1_BOARDS]
    tut2_full = [c for c in tut2_res["accepted"] if c["status"] == "accepted"]
    if not tut2_full:
        print("UYARI: ogretici 2 uretilemedi — cikiliyor.")
        return
    tut2 = choose_spread(tut2_full, 2, key=lambda c: c["shortest"])
    tutorial_boards = tut1 + tut2

    # 4. Ranked'i durumlara dagit (ilk/kolay durumlar kalani alir), bant ici sec.
    target_ranked = TARGET_TOTAL - len(tutorial_boards)
    base, extra = divmod(target_ranked, len(bands))
    ranked_boards = []
    for i, b in enumerate(bands):
        b["count"] = base + (1 if i < extra else 0)
        key = (lambda c: score_fn(c)["total"]) if b["measurable"] else None
        b["picks"] = choose_spread(b["cands"], b["count"], key=key)
        ranked_boards.extend(b["picks"])

    all_boards = tutorial_boards + ranked_boards
    for i, c in enumerate(all_boards, start=1):
        c["level"] = i
        c["label"] = str(i)
        c["selected"] = True

    total_wall = time.perf_counter() - wall0

    # 5. Konsol: rampa ozeti (yapisal ramp + level araliklari gorunsun).
    print(
        f"\n=== RAMPA OZETI (hacme gore) — Lv 1-{len(tutorial_boards)}: ogretici ===")
    lvl = len(tutorial_boards) + 1
    for b in bands:
        n = len(b["picks"])
        sc = f"skor~{b['mean_score']:.3f}" if b["measurable"] else "hacme gore "
        print(f"  Lv {lvl:3d}-{lvl + n - 1:3d}  kap={b['cap']:2d} renk={b['colors']:2d} "
              f"vol={b['vol']:3d} [{'olc ' if b['measurable'] else 'hard'}] x{n}  {sc}")
        lvl += n

    levels_path, n_levels = write_pilot_levels(all_boards)
    write_report(bands, tutorial_boards, bounds, total_wall)
    diag_path = write_diagnostics(band_results, tut2_res, tut1, bounds,
                                  score_fn, total_wall)

    total_cpu = sum(r["cpu_seconds"]
                    for r in band_results) + tut2_res["cpu_seconds"]
    spd = total_cpu / total_wall if total_wall > 0 else 1.0
    n_hard = sum(1 for b in bands if not b["measurable"])
    print(f"\nGENEL: {n_levels} level  ({len(bands)} durum, {n_hard} hard)  "
          f"is={total_cpu:.0f}s  ger={total_wall:.0f}s  hizlanma={spd:.1f}x")
    print(f"Cikti: {levels_path}")
    print(f"       pilot_ladder.md · {os.path.basename(diag_path)}")


def write_pilot_levels(all_boards):
    """Oyun semasi: Assets/Resources/pilot_levels.json. C# (LevelLibrary) yalniz
    level/label/capacity/tubes okur; ham metrikler (shortest/states/dead) analiz
    icindir (C# yok sayar; hard tahtalarda None)."""
    levels = [{
        "level": c["level"],
        "label": c["label"],
        "capacity": c["cap"],
        "tubes": [",".join(str(x) for x in tube) for tube in c["board"]],
        "shortest": c["shortest"],
        "states": c["states"],
        "dead": (round(c["dead"], 5) if c["dead"] is not None else None),
    } for c in all_boards]

    script_dir = os.path.dirname(os.path.abspath(__file__))
    out_path = os.path.normpath(os.path.join(
        script_dir, "..", "..", "Assets", "Resources", "pilot_levels.json"))
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump({"levels": levels}, f, indent=2)
    return out_path, len(levels)


def fmt_tubes(board):
    """Tahtayi okunur metne cevir: dolu tupler dipten yukari, bos '()'."""
    return " ".join("[" + ",".join(str(c) for c in tube) + "]" if tube else "()"
                    for tube in board)


def write_report(bands, tutorial_boards, bounds, total_secs):
    """pilot_ladder.md: config + rampa ozeti (durum sirasi, level araliklari)."""
    lines = ["# TubeSort — 300-Level Uretim Raporu (dengeli GRID)", ""]
    lines.append(f"Seed `{SEED}` · dengeli grid {len(LADDER)} durum (|kap-renk|<={BALANCE}) · "
                 f"aday/durum {CANDIDATES_PER_SLOT} · PARALEL {WORKERS} · gercek {total_secs:.1f}s")
    lines.append("")
    lines.append("Siralama hacme gore — olculebilir bantlar (hacim, kap), hard bantlar "
                 f"(hacim, renk). kap + renk GENELDE artar (tie'larda en fazla +-{BALANCE} "
                 "salinim; buyuk deep-few<->shallow-many jar YOK). Skor yalniz BANT ICI "
                 f"kolay->zor secim. (L={WEIGHTS['L']} C={WEIGHTS['C']} T={WEIGHTS['T']}; "
                 f"olculebilir havuzda [0,1] normalize; hacim>{MEASURABLE_VOLUME_MAX} 'hard'.)")
    lines.append("")
    lines.append("Normalizasyon (olculebilir havuz): "
                 f"enKisa `{bounds['L'][0]}..{bounds['L'][1]}`, "
                 f"log(durum) `{bounds['C'][0]:.2f}..{bounds['C'][1]:.2f}`, "
                 f"olu `{bounds['T'][0]:.3f}..{bounds['T'][1]:.3f}`.")
    lines.append("")
    lines.append(f"## Rampa (Lv 1-{len(tutorial_boards)}: ogretici)")
    lines.append("")
    lines.append("| Lv | kap | renk | hacim | tip | adet | skor~ |")
    lines.append("|:--|--:|--:|--:|:--|--:|--:|")
    lvl = len(tutorial_boards) + 1
    for b in bands:
        n = len(b["picks"])
        sc = f"{b['mean_score']:.3f}" if b["measurable"] else "—"
        tip = "olc" if b["measurable"] else "hard"
        lines.append(f"| {lvl}-{lvl + n - 1} | {b['cap']} | {b['colors']} | {b['vol']} "
                     f"| {tip} | {n} | {sc} |")
        lvl += n
    lines.append("")

    script_dir = os.path.dirname(os.path.abspath(__file__))
    with open(os.path.join(script_dir, "pilot_ladder.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")


def write_diagnostics(band_results, tut2_res, tut1, bounds, score_fn, total_wall):
    """Tum teshis -> diagnostics.json (durum sayaclari + tum kabul adaylari,
    secilen isaretli; skor bilesenleri)."""
    def cand_json(c):
        sc = score_fn(c) if c["status"] == "accepted" else None
        return {
            "tubes": [list(t) for t in c["board"]],
            "status": c["status"],
            "cap": c["cap"], "colors": c["colors"], "volume": c["volume"],
            "shortest": c["shortest"], "states": c["states"],
            "dead": (round(c["dead"], 5) if c["dead"] is not None else None),
            "score": ({k: round(v, 4) for k, v in sc.items()} if sc else None),
            "selected": c.get("selected", False),
            "level": c.get("level"), "label": c.get("label"),
        }

    def band_json(res):
        return {
            "cap": res["cap"], "colors": res["colors"],
            "volume": res["cap"] * res["colors"],
            "attempts": res["attempts"], "accepted": len(res["accepted"]),
            "eliminations": {"precomplete": res["precomplete"],
                             "unsolvable": res["unsolvable"], "budget": res["budget"]},
            "cpuSeconds": round(res["cpu_seconds"], 2),
            "wallSeconds": round(res["wall_seconds"], 2),
            "candidates": [cand_json(c) for c in res["accepted"]],
        }

    data = {
        "config": {
            "seed": SEED, "weights": WEIGHTS, "targetTotal": TARGET_TOTAL,
            "candidatesPerSlot": CANDIDATES_PER_SLOT, "workers": WORKERS,
            "budget": cc.BUDGET, "measurableVolumeMax": MEASURABLE_VOLUME_MAX,
            "grid": {"caps": [GRID_CAPS.start, GRID_CAPS.stop - 1],
                     "colors": [GRID_COLORS.start, GRID_COLORS.stop - 1],
                     "balance": BALANCE},
        },
        "wallSeconds": round(total_wall, 1),
        "bounds": {
            "shortest": [bounds["L"][0], bounds["L"][1]],
            "logStates": [round(bounds["C"][0], 4), round(bounds["C"][1], 4)],
            "dead": [round(bounds["T"][0], 5), round(bounds["T"][1], 5)],
        },
        "tutorial1": [cand_json(c) for c in tut1],
        "bands": [band_json(res) for res in band_results] + [band_json(tut2_res)],
    }
    script_dir = os.path.dirname(os.path.abspath(__file__))
    out_path = os.path.join(script_dir, "diagnostics.json")
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)
    return out_path


if __name__ == "__main__":
    main()
