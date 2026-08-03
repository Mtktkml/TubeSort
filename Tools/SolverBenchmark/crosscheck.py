"""TubeSort solver — Python capraz dogrulama + benchmark.

C# Solver.cs'in BAGIMSIZ implementasyonu: ayni algoritma (DFS + budama +
kanonik durum onbellegi), sifirdan Python'da. Amac iki katli dogrulama:

1. Capraz dogrulama: C# benchmark'inin urettigi SOMUT tahtalari cozup
   kararlarin (ve cozulemez kanit sayilarinin) birebir tuttugunu gormek.
   Iki bagimsiz implementasyon ayni sonuca variyorsa ikisine de guven artar.
2. Benchmark: ayni tabloyu Python'da uretmek. NOT: Python sureleri C# ile
   KARSILASTIRILAMAZ (Python 10-50x yavas); karsilastirilabilir olan
   karar ve durum sayilaridir.

Calistirma:  python crosscheck.py
Sonuclar hem ekrana basilir hem py_results.md dosyasina yazilir.
"""

import os
import random
import threading
import time
from collections import deque

# Terminaldeki canli durum satiri: script calistigi surece her saniye
# gecen sureyi ve o anki asamayi ayni satirin ustune yazar.
_stage = {"text": "basliyor"}


def _ticker(stop_event, t0):
    while not stop_event.wait(1.0):
        elapsed = time.time() - t0
        print(f"\r  CALISIYOR  {elapsed:4.0f} sn  |  {_stage['text']}          ",
              end="", flush=True)


def set_stage(text):
    _stage["text"] = text

BUDGET = 2_000_000

# solvable_only() icin AYRI, daha comert butce. Yalnizca "ilk cozumde dur"
# yaptigi ve durum basina hicbir sey saymadigi (sozluk/kenar biriktirmedigi)
# icin state basina maliyeti solve()'tan cok daha dusuktur; ayni duvar-zamanda
# daha genis bir uzayi tarayabilir. Boylece tam-sayim solve() butceyi asip
# "bilinmiyor" dedigi buyuk tahtalarda cozulebilirligi yine de GARANTI eder.
SOLVABLE_BUDGET = 20_000_000

# ---------------------------------------------------------------- kurallar

def top_segment(tube):
    """Ustteki bitisik ayni renk birim sayisi."""
    if not tube:
        return 0
    n, top = 0, tube[-1]
    for c in reversed(tube):
        if c != top:
            break
        n += 1
    return n


def is_complete(tube, cap):
    """Tup tamamlanmis mi: bos ya da tek renkle agzina kadar dolu."""
    return not tube or (len(tube) == cap and all(c == tube[0] for c in tube))


def is_solved(board, cap):
    return all(is_complete(t, cap) for t in board)


def pour(board, cap, i, j):
    """Gecerliyse hamle uygulanmis YENI tahta doner; degilse None."""
    src, dst = board[i], board[j]
    if i == j or not src or len(dst) == cap:
        return None
    if dst and dst[-1] != src[-1]:
        return None

    amount = min(top_segment(src), cap - len(dst))
    color = src[-1]
    new = list(board)
    new[i] = src[:-amount]
    new[j] = dst + (color,) * amount
    return tuple(new)


def gen_moves(board, cap):
    """C#'taki CollectMoves'un aynisi: ayni budamalar, ayni siralama.

    Budamalar: tamamlanmis tupten dokme yok; bos hedeflerden yalniz ilki;
    tek renkli tupu ayni kapasiteli bos tupe tasima yok. Siralama: dolu
    hedefler once, bos hedef sona.
    """
    filled_targets, empty_targets = [], []

    for i, src in enumerate(board):
        if not src or is_complete(src, cap):
            continue

        single_color = all(c == src[0] for c in src)
        empty_used = False

        for j, dst in enumerate(board):
            if i == j:
                continue
            if not dst:
                if empty_used:
                    continue
                empty_used = True
                if single_color:  # kapasiteler ayni: kanonik olarak ayni durum
                    continue
                if pour(board, cap, i, j) is not None:
                    empty_targets.append((i, j))
            elif pour(board, cap, i, j) is not None:
                filled_targets.append((i, j))

    return filled_targets + empty_targets


def canonical(board):
    """Tup sirasi permutasyonlari ve es bos tupler tek duruma iner."""
    return tuple(sorted(board))


# ------------------------------------------------------------------ solver

def solve(board, cap, max_states=BUDGET):
    """(karar, durum_sayisi, cozum_sayisi, ilk_cozum_uzunlugu) doner.

    C# Solver ile ayni sayim semantigi: arama ilk cozumde DURMAZ, erisilebilir
    uzayin tamamini gezer (her kanonik durum en fazla bir kez genisletilir) ve
    cozume dusen her kenari sayar. Cozum KENARDA tespit edilir, cozulmus durum
    genisletilmez. Uzay tuketildigi icin durum ve cozum sayilari gezinme
    sirasindan bagimsizdir.

    Karar: en az 1 cozum -> 'SOLVABLE'; hic cozum bulunamadan butce asilirsa
    'OUT_OF_BUDGET' (bilinmiyor, "cozulemez" degil — Murase 1996 dersi);
    uzay tuketilip cozum yoksa 'UNSOLVABLE'. Butce asilirsa cozum sayisi
    kesin degildir, alt sinirdir ("en az N").
    Ilk cozum uzunlugu ornek amaclidir (EN KISA DEGIL); yalniz SOLVABLE'da
    dolu, digerlerinde None.
    """
    board = tuple(tuple(t) for t in board)
    if is_solved(board, cap):
        # Bastan cozulmus tahta: cozume dusen kenar yok ama tahta cozulu.
        return "SOLVABLE", 0, 0, 0

    visited = {canonical(board)}
    # Python'da ozyineleme limiti dar; acik yigin kullaniyoruz.
    # Yigina (tahta, derinlik) atilir: derinlik = o ana kadarki hamle sayisi.
    stack = [(board, 0)]
    states = 0
    sols = 0
    first_len = None
    budget_hit = False

    while stack:
        cur, depth = stack.pop()

        if states >= max_states:
            budget_hit = True
            break
        states += 1

        # C# ile ayni gezinme sirasi icin hamleler ters itilir
        # (yigin LIFO: son itilen ilk islenir).
        for i, j in reversed(gen_moves(cur, cap)):
            nxt = pour(cur, cap, i, j)
            if is_solved(nxt, cap):
                sols += 1
                if first_len is None:
                    first_len = depth + 1
                continue
            key = canonical(nxt)
            if key not in visited:
                visited.add(key)
                stack.append((nxt, depth + 1))

    if sols > 0:
        return "SOLVABLE", states, sols, first_len
    return ("OUT_OF_BUDGET" if budget_hit else "UNSOLVABLE"), states, 0, None


def solvable_only(board, cap, max_states=SOLVABLE_BUDGET):
    """Yalin cozulebilirlik karari: ILK cozumde durur, hicbir sey saymaz.

    solve() ile AYNI budanmis kanonik graf (ayni gen_moves, ayni canonical);
    tek fark: cozume dusen ilk kenarda hemen durur ve durum/cozum sayimi yapmaz.
    Birlestirme-oncelikli DFS (gen_moves dolu hedefleri one koyar) cozumu az
    dugumle bulur. Amac: buyuk tahtada tam-sayim solve() butceyi asinca
    cozulebilirligi yine de GARANTI etmek (comert SOLVABLE_BUDGET ile).

    Doner:
      True  — en az bir cozum bulundu (cozulebilir).
      False — uzay TAM tukendi, cozum yok (UNSOLVABLE ile ayni karar).
      None  — butce asildi, cozum bulunamadan ("bilinmiyor", cozum yok DEGIL).
    """
    board = tuple(tuple(t) for t in board)
    if is_solved(board, cap):
        return True

    visited = {canonical(board)}
    # Acik yigin; solve() ile ayni gezinme sirasi icin hamleler ters itilir
    # (LIFO: son itilen ilk islenir -> gen_moves'un ilk hamlesi ilk denenir).
    stack = [board]
    states = 0

    while stack:
        cur = stack.pop()
        if states >= max_states:
            return None
        states += 1

        for i, j in reversed(gen_moves(cur, cap)):
            nxt = pour(cur, cap, i, j)
            if is_solved(nxt, cap):
                return True
            key = canonical(nxt)
            if key not in visited:
                visited.add(key)
                stack.append(nxt)

    return False


def shortest_solution(board, cap, max_states=BUDGET):
    """En kisa cozum uzunlugu (hamle sayisi): kanonik graf uzerinde BFS.

    solve() ile AYNI grafi gezer (ayni budamalar, ayni kanonik anahtar);
    tek fark siralama: katman katman genisletildigi icin bir duruma ilk
    varis garantili en kisa varistir — cozume dusen ilk kenarin derinligi
    en kisa cozumdur ve arama orada durabilir (DFS'in ilk yolu boyle bir
    garanti tasimaz, o yuzden metrik olamazdi).

    Zorluk metrigi katmani: uretimde yalniz KABUL edilmis (SOLVABLE)
    tahtalarda kosulur; cozulebilirlik karari solve()'un isidir.

    (uzunluk, durum_sayisi, butce_asildi) doner. Cozum yoksa uzunluk None
    (cozulemez tahtada uzay tukenir); butce asilirsa uzunluk None ve
    bayrak True — 'bilinmiyor', 'cozum yok' degil.
    """
    board = tuple(tuple(t) for t in board)
    if is_solved(board, cap):
        return 0, 0, False

    visited = {canonical(board)}
    queue = deque([(board, 0)])
    states = 0

    while queue:
        cur, depth = queue.popleft()

        if states >= max_states:
            return None, states, True
        states += 1

        for i, j in gen_moves(cur, cap):
            nxt = pour(cur, cap, i, j)
            if is_solved(nxt, cap):
                return depth + 1, states, False
            key = canonical(nxt)
            if key not in visited:
                visited.add(key)
                queue.append((nxt, depth + 1))

    return None, states, False


def dead_ratio(board, cap, max_states=BUDGET):
    """Erisilebilir durumlarin ne kadari OLU (hedefe yol yok) — tuzak yogunlugu.

    Zorluk sinyali T: "oynarken ne kadar kolay sikisirsin". Level cozulebilir
    olsa bile, yanlis hamlelerle dusulen cikmaz durumlarin payi. Cok = zor.

    solve()/shortest_solution() ile AYNI kanonik graf (ayni budamalar, ayni
    kanonik anahtar): once start'tan erisilebilir tum durumlar ve aralarindaki
    kenarlar cikarilir; sonra hedeften GERI erisilebilirlik ile 'canli' (hedefe
    en az bir yol var) durumlar isaretlenir. Olu = erisilebilir ama canli degil.
    oran = olu / erisilebilir.

    'Canli olma' hedefe DOGRUDAN dokebilen durumdan baslar ve geri (predecessor)
    kenarlar boyunca yayilir. Boylece 'hamlesi var ama hepsi olu durumlara
    gidiyor' olan durum dogru sekilde OLU sayilir (HasAnyValidMove'un
    yakalayamadigi gercek cikmaz).

    Tanim gonderme (dogrulama icin): bir durum bu grafta OLU ⟺ o durumdan
    solve() 'UNSOLVABLE' verir (ayni budanmis graf, ayni hedef). Iki bagimsiz
    yol ayni sinifi vermeli — crosscheck bunu kaba-kuvvetle sinar.

    Yalniz uzay TAM tuketildiginde anlamlidir: butce asilirsa (None, states,
    True) doner — cagiran 'bilinmiyor' diye ele alir, oran 0 gibi degil (Murase
    1996 dersi). Bastan cozulmus tahtada erisilebilir uzay bostur -> oran 0.0.

    (oran, erisilebilir_durum_sayisi, butce_asildi) doner.
    """
    board = tuple(tuple(t) for t in board)
    if is_solved(board, cap):
        return 0.0, 0, False

    visited = {canonical(board)}
    stack = [board]
    succ = {}            # kanonik durum -> [kanonik ardil durumlar]
    seed_alive = set()   # hedefe DOGRUDAN dokebilen durumlar (geri yayilimin tohumu)
    states = 0

    while stack:
        cur = stack.pop()

        if states >= max_states:
            return None, states, True
        states += 1

        key = canonical(cur)
        children = []
        for i, j in gen_moves(cur, cap):
            nxt = pour(cur, cap, i, j)
            if is_solved(nxt, cap):
                seed_alive.add(key)
                continue
            nkey = canonical(nxt)
            children.append(nkey)
            if nkey not in visited:
                visited.add(nkey)
                stack.append(nxt)
        succ[key] = children

    # Geri erisilebilirlik: predecessor grafini kur, seed'lerden canliligi yay.
    preds = {}
    for u, children in succ.items():
        for v in children:
            preds.setdefault(v, []).append(u)

    alive = set(seed_alive)
    queue = deque(seed_alive)
    while queue:
        v = queue.popleft()
        for u in preds.get(v, ()):
            if u not in alive:
                alive.add(u)
                queue.append(u)

    reachable = len(visited)   # start dahil tum erisilebilir (hedef-olmayan) durumlar
    dead = reachable - len(alive)
    return dead / reachable, reachable, False


# ------------------------------------------------- capraz dogrulama verisi

# C# benchmark ciktisindan alinan SOMUT tahtalar. Yeni sayim semantiginde
# arama her tahtada erisilebilir uzayi tukettigi icin durum VE cozum sayisi
# gezinme sirasindan bagimsizdir: cozulebilir tahtalarda da karar/durum/cozum
# uclusu birebir kiyaslanir.
# Beklenen degerler C# kosusundan donduruldu.
CROSS_CHECKS = [
    ("4x4 cozulemez (1 bos, ana tablo)", 4,
     [[2, 0, 1, 2], [1, 0, 2, 0], [1, 3, 3, 3], [1, 3, 0, 2], []],
     "UNSOLVABLE", 18, 0),
    ("Oyundaki cozulemez test tahtasi (58 durum)", 4,
     [[2, 2, 3, 0], [2, 0, 3, 1], [3, 0, 3, 1], [2, 0, 1, 1], []],
     "UNSOLVABLE", 58, 0),
    ("7x7 cozulemez (2 bos)", 7,
     [[5, 2, 4, 3, 5, 5, 0], [0, 3, 6, 1, 2, 3, 0], [2, 4, 0, 3, 1, 1, 6],
      [4, 0, 1, 6, 5, 1, 4], [6, 4, 6, 1, 2, 2, 0], [2, 0, 5, 3, 3, 1, 4],
      [5, 6, 4, 3, 5, 2, 6], [], []],
     "UNSOLVABLE", 639, 0),
    ("8x8 cozulemez (2 bos)", 8,
     [[3, 4, 3, 3, 4, 2, 3, 7], [4, 6, 5, 7, 6, 6, 5, 2], [2, 3, 3, 6, 4, 2, 0, 0],
      [3, 7, 1, 7, 5, 1, 0, 1], [0, 7, 5, 5, 2, 6, 6, 4], [1, 0, 0, 7, 7, 2, 6, 5],
      [5, 1, 7, 4, 2, 6, 0, 4], [1, 4, 0, 5, 3, 2, 1, 1], [], []],
     "UNSOLVABLE", 6159, 0),
    ("9x9 cozulemez (2 bos)", 9,
     [[2, 4, 4, 4, 1, 0, 6, 5, 8], [8, 8, 1, 3, 2, 7, 0, 4, 8],
      [3, 8, 1, 2, 5, 0, 7, 4, 5], [8, 7, 6, 2, 4, 8, 7, 2, 3],
      [6, 4, 0, 6, 0, 0, 7, 4, 7], [3, 1, 5, 2, 1, 2, 5, 7, 1],
      [0, 4, 0, 3, 5, 7, 7, 3, 3], [8, 8, 5, 1, 6, 1, 3, 2, 6],
      [2, 3, 6, 6, 5, 5, 6, 0, 1], [], []],
     "UNSOLVABLE", 927, 0),
    ("10x10 cozulemez (2 bos)", 10,
     [[4, 1, 8, 6, 8, 2, 7, 2, 4, 9], [9, 8, 1, 7, 0, 7, 6, 0, 3, 1],
      [1, 3, 9, 5, 1, 8, 5, 7, 8, 4], [9, 2, 1, 9, 8, 5, 5, 0, 3, 3],
      [9, 1, 0, 3, 5, 6, 6, 6, 5, 8], [6, 7, 0, 4, 2, 7, 1, 2, 6, 7],
      [4, 4, 4, 3, 2, 7, 5, 6, 0, 0], [3, 3, 7, 0, 4, 1, 5, 0, 2, 2],
      [3, 5, 9, 3, 1, 6, 4, 2, 2, 9], [9, 9, 5, 8, 8, 0, 6, 7, 8, 4], [], []],
     "UNSOLVABLE", 3856, 0),
    ("4x4 cozulebilir (2 bos, ana tablo)", 4,
     [[0, 2, 1, 1], [2, 2, 1, 3], [0, 2, 3, 1], [0, 0, 3, 3], [], []],
     "SOLVABLE", 256, 16),
    ("Oyundaki varsayilan test tahtasi", 4,
     [[0, 1, 1, 2], [3, 0, 2, 1], [2, 3, 3, 0], [1, 2, 0, 3], [], []],
     "SOLVABLE", 556, 16),
]


def run_cross_checks(out):
    out.append("## Capraz dogrulama: C# ornekleri Python'da\n")
    out.append("| Tahta | Beklenen | Python karari | Durum (C#/Py) | Cozum (C#/Py) | Sonuc |")
    out.append("|---|---|---|---|---|---|")

    all_ok = True
    for name, cap, board, want_verdict, want_states, want_sols in CROSS_CHECKS:
        t0 = time.perf_counter()
        verdict, states, sols, _ = solve(board, cap)
        ms = (time.perf_counter() - t0) * 1000

        ok = (verdict == want_verdict
              and states == want_states
              and sols == want_sols)

        all_ok = all_ok and ok
        mark = "ESLESTI" if ok else "**UYUSMAZLIK**"
        out.append(f"| {name} | {want_verdict} | {verdict} ({ms:.0f} ms) "
                   f"| {want_states} / {states} | {want_sols} / {sols} | {mark} |")

    out.append("")
    out.append("**TOPLU SONUC: " + ("TUM KONTROLLER ESLESTI**" if all_ok
               else "UYUSMAZLIK VAR — INCELE!**"))
    out.append("")
    return all_ok


# ------------------------------------------- dead_ratio dogrulamasi (bagimsiz)

# dead_ratio grafik-tabanli ve hizli; burada YAVAS ama BAGIMSIZ bir referansla
# (her erisilebilir duruma solve() kosup UNSOLVABLE sayarak) capraz-dogrulanir.
# Referans solve() zaten C# ile dogrulanmis; iki yol ayni orani vermeli.
# Tahtalar pilot merdiveninin kucuk slotlari (durum<~400); boyle kaba-kuvvet
# saniyeler yerine milisaniyeler surer. bos=2 -> ~0 (affedici), bos=1 -> >0
# (tuzakli): kontrol hem sifir hem sifir-disi durumu kapsar.
DEAD_RATIO_CHECKS = [
    ("slot1 kap4 renk3 bos2 (durum 123)", 4,
     [[1, 2, 1, 1], [0, 0, 2, 0], [0, 2, 2, 1], [], []]),
    ("slot2 kap4 renk4 bos2 (durum 337)", 4,
     [[0, 2, 0, 3], [3, 2, 2, 1], [3, 3, 0, 1], [2, 1, 1, 0], [], []]),
    ("slot4 kap4 renk5 bos1 (durum 89)", 4,
     [[2, 0, 3, 3], [3, 0, 4, 4], [3, 2, 1, 0], [4, 1, 2, 1], [1, 4, 0, 2], []]),
    ("slot6 kap4 renk6 bos1 (durum 71)", 4,
     [[2, 4, 3, 1], [1, 2, 0, 5], [4, 3, 4, 0], [3, 4, 2, 0], [5, 5, 1, 1],
      [2, 0, 5, 3], []]),
]


def _reachable_nongoal_states(board, cap):
    """Start'tan erisilebilir tum (hedef-olmayan) durumlar, birer temsilci
    tahtayla. dead_ratio'nun gezdigi ayni budanmis graf."""
    board = tuple(tuple(t) for t in board)
    if is_solved(board, cap):
        return []
    seen = {canonical(board): board}
    stack = [board]
    while stack:
        cur = stack.pop()
        for i, j in gen_moves(cur, cap):
            nxt = pour(cur, cap, i, j)
            if is_solved(nxt, cap):
                continue
            key = canonical(nxt)
            if key not in seen:
                seen[key] = nxt
                stack.append(nxt)
    return list(seen.values())


def bruteforce_dead_ratio(board, cap):
    """Bagimsiz referans: her erisilebilir duruma solve() kosup UNSOLVABLE
    olanlari say. (oran, erisilebilir_sayisi) doner. Yavas ama dogrudan
    tanimdan: durum OLU ⟺ o durumdan cozum yok."""
    states = _reachable_nongoal_states(board, cap)
    if not states:
        return 0.0, 0
    dead = 0
    for s in states:
        verdict = solve(s, cap)[0]
        if verdict == "UNSOLVABLE":
            dead += 1
        elif verdict == "OUT_OF_BUDGET":
            raise RuntimeError("alt-durum butce asti — dogrulama guvenilmez")
    return dead / len(states), len(states)


def run_dead_ratio_checks(out):
    out.append("## dead_ratio dogrulamasi: grafik vs kaba-kuvvet\n")
    out.append("Grafik-tabanli `dead_ratio` (hizli, geri-erisilebilirlik) vs "
               "bagimsiz kaba-kuvvet (her duruma `solve()`). Oran ve erisilebilir "
               "durum sayisi birebir tutmali.\n")
    out.append("| Tahta | oran (grafik/kabaguc) | erisilebilir (grafik/kabaguc) | Sonuc |")
    out.append("|---|---|---|---|")

    all_ok = True
    for name, cap, board in DEAD_RATIO_CHECKS:
        g_ratio, g_reach, hit = dead_ratio(board, cap)
        b_ratio, b_reach = bruteforce_dead_ratio(board, cap)
        ok = (not hit
              and g_reach == b_reach
              and abs(g_ratio - b_ratio) < 1e-9)
        all_ok = all_ok and ok
        mark = "ESLESTI" if ok else "**UYUSMAZLIK**"
        out.append(f"| {name} | {g_ratio:.4f} / {b_ratio:.4f} "
                   f"| {g_reach} / {b_reach} | {mark} |")

    out.append("")
    out.append("**TOPLU SONUC: " + ("DEAD_RATIO ESLESTI**" if all_ok
               else "UYUSMAZLIK VAR — INCELE!**"))
    out.append("")
    return all_ok


# --------------------------------------------------------------- benchmark

def generate(colors, cap, empties, rng):
    units = [c for c in range(colors) for _ in range(cap)]
    rng.shuffle(units)
    full = [tuple(units[t * cap:(t + 1) * cap]) for t in range(colors)]
    return tuple(full + [()] * empties)


def find(colors, cap, empties, want, rng, tries=200, time_limit=60.0):
    """Istenen karari veren ilk tahtayi arar.

    (deneme, durum, ms, ilk_yol_uzunlugu, cozum_sayisi) doner; bulamazsa None.
    """
    wall = time.perf_counter()
    for attempt in range(1, tries + 1):
        if time.perf_counter() - wall > time_limit:
            return None
        board = generate(colors, cap, empties, rng)
        if want == "UNSOLVABLE" and not gen_moves(board, cap):
            continue  # hamlesiz cikmazlar ilginc degil
        t0 = time.perf_counter()
        verdict, states, sols, sol_len = solve(board, cap)
        ms = (time.perf_counter() - t0) * 1000
        if verdict == want:
            return attempt, states, ms, sol_len, sols
    return None


def run_benchmark(out):
    out.append("## Benchmark (Python): N renk x N kapasite")
    out.append("")
    out.append("Sureler Python'a ait — C# ile karsilastirilamaz (10-50x yavas).")
    out.append("")
    out.append("| Boyut | Cozulebilir (2 bos): cozum / ilk yol / durum / sure | Cozulemez (1 bos): durum / sure |")
    out.append("|---|---|---|")

    rng = random.Random(7)
    for n in range(3, 11):
        set_stage(f"benchmark {n}x{n} kosuyor")
        s = find(n, n, 2, "SOLVABLE", rng)
        if s:
            # Butce dolduysa cozum sayisi kesin degil, alt sinirdir.
            note = " (alt sinir)" if s[1] >= BUDGET else ""
            s_cell = f"{s[4]}{note} / {s[3]} / {s[1]} / {s[2]:.1f} ms"
        else:
            s_cell = "bulunamadi"

        u = find(n, n, 1, "UNSOLVABLE", rng)
        u_cell = (f"{u[1]} / {u[2]:.1f} ms" if u else "bulunamadi")

        out.append(f"| {n}x{n} | {s_cell} | {u_cell} |")
        set_stage(f"benchmark {n}x{n} tamamlandi")

    out.append("")


def main():
    out = ["# TubeSort Solver — Python Capraz Dogrulama Raporu", ""]

    t0 = time.time()
    stop = threading.Event()
    ticker = threading.Thread(target=_ticker, args=(stop, t0), daemon=True)
    ticker.start()

    set_stage("capraz dogrulama")
    run_cross_checks(out)

    set_stage("dead_ratio dogrulamasi")
    run_dead_ratio_checks(out)

    set_stage("benchmark basliyor")
    run_benchmark(out)

    stop.set()
    ticker.join()
    print(f"\r  BITTI  toplam {time.time() - t0:.0f} sn" + " " * 40)

    report = "\n".join(out)

    # Rapor, calisma dizini nereden olursa olsun scriptin kendi klasorune yazilir.
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "py_results.md")
    with open(path, "w", encoding="utf-8") as f:
        f.write(report)

    print()
    print(report)
    print(f"\nRapor dosyasi: {path}")


if __name__ == "__main__":
    main()
