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
    """(karar, durum_sayisi, cozum_uzunlugu) doner.

    Karar: 'SOLVABLE' | 'UNSOLVABLE' | 'OUT_OF_BUDGET'.
    OUT_OF_BUDGET ile UNSOLVABLE ayrimi bilincli: butceyle elenen tahta
    "cozulemez" degil "bilinmiyor"dur (Murase 1996 dersi).
    Cozum uzunlugu yalniz SOLVABLE'da anlamli, digerlerinde None.
    """
    board = tuple(tuple(t) for t in board)
    if is_solved(board, cap):
        return "SOLVABLE", 0, 0

    visited = {canonical(board)}
    # Python'da ozyineleme limiti dar; acik yigin kullaniyoruz.
    # Yigina (tahta, derinlik) atilir: derinlik = o ana kadarki hamle sayisi.
    stack = [(board, 0)]
    states = 0

    while stack:
        cur, depth = stack.pop()

        if states >= max_states:
            return "OUT_OF_BUDGET", states, None
        states += 1

        # C# ile ayni gezinme sirasi icin hamleler ters itilir
        # (yigin LIFO: son itilen ilk islenir).
        for i, j in reversed(gen_moves(cur, cap)):
            nxt = pour(cur, cap, i, j)
            if is_solved(nxt, cap):
                return "SOLVABLE", states, depth + 1
            key = canonical(nxt)
            if key not in visited:
                visited.add(key)
                stack.append((nxt, depth + 1))

    return "UNSOLVABLE", states, None


# ------------------------------------------------- capraz dogrulama verisi

# C# benchmark ciktisindan alinan SOMUT tahtalar. Beklenen durum sayisi
# yalniz cozulemezlerde birebir kiyaslanir: kanit, erisilebilir uzayin
# tamamini tuketmeyi gerektirdigi icin gezinme sirasindan bagimsizdir.
# Cozulebilirlerde gezilen durum sayisi hamle sirasina bagli oldugundan
# yalniz KARAR kiyaslanir.
CROSS_CHECKS = [
    ("4x4 cozulemez (1 bos, ana tablo)", 4,
     [[2, 0, 1, 2], [1, 0, 2, 0], [1, 3, 3, 3], [1, 3, 0, 2], []],
     "UNSOLVABLE", 18),
    ("Oyundaki cozulemez test tahtasi (58 durum)", 4,
     [[2, 2, 3, 0], [2, 0, 3, 1], [3, 0, 3, 1], [2, 0, 1, 1], []],
     "UNSOLVABLE", 58),
    ("7x7 cozulemez (2 bos)", 7,
     [[5, 2, 4, 3, 5, 5, 0], [0, 3, 6, 1, 2, 3, 0], [2, 4, 0, 3, 1, 1, 6],
      [4, 0, 1, 6, 5, 1, 4], [6, 4, 6, 1, 2, 2, 0], [2, 0, 5, 3, 3, 1, 4],
      [5, 6, 4, 3, 5, 2, 6], [], []],
     "UNSOLVABLE", 639),
    ("8x8 cozulemez (2 bos)", 8,
     [[3, 4, 3, 3, 4, 2, 3, 7], [4, 6, 5, 7, 6, 6, 5, 2], [2, 3, 3, 6, 4, 2, 0, 0],
      [3, 7, 1, 7, 5, 1, 0, 1], [0, 7, 5, 5, 2, 6, 6, 4], [1, 0, 0, 7, 7, 2, 6, 5],
      [5, 1, 7, 4, 2, 6, 0, 4], [1, 4, 0, 5, 3, 2, 1, 1], [], []],
     "UNSOLVABLE", 6159),
    ("9x9 cozulemez (2 bos)", 9,
     [[2, 4, 4, 4, 1, 0, 6, 5, 8], [8, 8, 1, 3, 2, 7, 0, 4, 8],
      [3, 8, 1, 2, 5, 0, 7, 4, 5], [8, 7, 6, 2, 4, 8, 7, 2, 3],
      [6, 4, 0, 6, 0, 0, 7, 4, 7], [3, 1, 5, 2, 1, 2, 5, 7, 1],
      [0, 4, 0, 3, 5, 7, 7, 3, 3], [8, 8, 5, 1, 6, 1, 3, 2, 6],
      [2, 3, 6, 6, 5, 5, 6, 0, 1], [], []],
     "UNSOLVABLE", 927),
    ("10x10 cozulemez (2 bos)", 10,
     [[4, 1, 8, 6, 8, 2, 7, 2, 4, 9], [9, 8, 1, 7, 0, 7, 6, 0, 3, 1],
      [1, 3, 9, 5, 1, 8, 5, 7, 8, 4], [9, 2, 1, 9, 8, 5, 5, 0, 3, 3],
      [9, 1, 0, 3, 5, 6, 6, 6, 5, 8], [6, 7, 0, 4, 2, 7, 1, 2, 6, 7],
      [4, 4, 4, 3, 2, 7, 5, 6, 0, 0], [3, 3, 7, 0, 4, 1, 5, 0, 2, 2],
      [3, 5, 9, 3, 1, 6, 4, 2, 2, 9], [9, 9, 5, 8, 8, 0, 6, 7, 8, 4], [], []],
     "UNSOLVABLE", 3856),
    ("4x4 cozulebilir (2 bos, ana tablo)", 4,
     [[0, 2, 1, 1], [2, 2, 1, 3], [0, 2, 3, 1], [0, 0, 3, 3], [], []],
     "SOLVABLE", None),
    ("Oyundaki varsayilan test tahtasi", 4,
     [[0, 1, 1, 2], [3, 0, 2, 1], [2, 3, 3, 0], [1, 2, 0, 3], [], []],
     "SOLVABLE", None),
]


def run_cross_checks(out):
    out.append("## Capraz dogrulama: C# ornekleri Python'da\n")
    out.append("| Tahta | Beklenen | Python karari | Beklenen durum | Python durum | Sonuc |")
    out.append("|---|---|---|---|---|---|")

    all_ok = True
    for name, cap, board, want_verdict, want_states in CROSS_CHECKS:
        t0 = time.perf_counter()
        verdict, states, _ = solve(board, cap)
        ms = (time.perf_counter() - t0) * 1000

        ok = verdict == want_verdict
        states_note = "-"
        if want_states is not None:
            states_note = str(want_states)
            ok = ok and states == want_states

        all_ok = all_ok and ok
        mark = "ESLESTI" if ok else "**UYUSMAZLIK**"
        out.append(f"| {name} | {want_verdict} | {verdict} ({ms:.0f} ms) "
                   f"| {states_note} | {states} | {mark} |")

    out.append("")
    out.append("**TOPLU SONUC: " + ("TUM KONTROLLER ESLESTI**" if all_ok
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
    """Istenen karari veren ilk tahtayi arar; (deneme, durum, ms) doner."""
    wall = time.perf_counter()
    for attempt in range(1, tries + 1):
        if time.perf_counter() - wall > time_limit:
            return None
        board = generate(colors, cap, empties, rng)
        if want == "UNSOLVABLE" and not gen_moves(board, cap):
            continue  # hamlesiz cikmazlar ilginc degil
        t0 = time.perf_counter()
        verdict, states, sol_len = solve(board, cap)
        ms = (time.perf_counter() - t0) * 1000
        if verdict == want:
            return attempt, states, ms, sol_len
    return None


def run_benchmark(out):
    out.append("## Benchmark (Python): N renk x N kapasite")
    out.append("")
    out.append("Sureler Python'a ait — C# ile karsilastirilamaz (10-50x yavas).")
    out.append("")
    out.append("| Boyut | Cozulebilir (2 bos): hamle / durum / sure | Cozulemez (1 bos): durum / sure |")
    out.append("|---|---|---|")

    rng = random.Random(7)
    for n in range(3, 11):
        set_stage(f"benchmark {n}x{n} kosuyor")
        s = find(n, n, 2, "SOLVABLE", rng)
        s_cell = (f"{s[3]} / {s[1]} / {s[2]:.1f} ms" if s else "bulunamadi")

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
