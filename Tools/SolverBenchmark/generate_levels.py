"""TubeSort — ilk 5 level uretici (Python).

Level 1..5 = 3x3, 4x4, 5x5, 6x6, 7x7 (renk x kapasite), her biri +2 bos tup.
Her level rastgele uretilir ve solver ile COZULEBILIR oldugu kanitlanana
kadar yeniden uretilir (generate-and-test). Sonuc Unity'nin okuyacagi
JSON'a yazilir: Assets/Resources/levels.json

Tohum sabittir: her calistirmada ayni leveller uretilir.

Calistirma:  python generate_levels.py
"""

import json
import os
import random
import time

import crosscheck as cc

SIZES = [3, 4, 5, 6, 7]   # level 1..5: N renk x N kapasite
EMPTIES = 2
SEED = 42


def main():
    rng = random.Random(SEED)
    levels = []

    print("Level uretimi basliyor (generate-and-test)...\n")

    for index, n in enumerate(SIZES, start=1):
        attempts = 0
        while True:
            attempts += 1
            board = cc.generate(n, n, EMPTIES, rng)

            t0 = time.perf_counter()
            verdict, states, sol_count, sol_len = cc.solve(board, n)
            ms = (time.perf_counter() - t0) * 1000

            if verdict == "SOLVABLE":
                break
            # Cozulemez ya da butce asimi: at, yeniden uret (ayri ayri logla).
            print(f"  Level {index}: deneme {attempts} atildi ({verdict})")

        tubes = [",".join(str(c) for c in tube) for tube in board]
        levels.append({"level": index, "capacity": n, "tubes": tubes})

        # Metrik makbuzu: zorluk siralamasinin girdileri — uretim parametreleri
        # (kapasite, renk, bos) + olculenler (cozum sayisi, en kisa cozum,
        # durum uzayi). En kisa BFS ile ayrica olculur (solve'un ilk yolu
        # rastlantidir, metrik degildir). Simdilik yalniz log; levels.json
        # semasina tasinmasi pilot merdiven (C asamasi) karari.
        t0 = time.perf_counter()
        shortest, _, short_budget = cc.shortest_solution(board, n)
        short_ms = (time.perf_counter() - t0) * 1000
        shortest_text = "bilinmiyor(butce)" if short_budget else str(shortest)

        print(f"Level {index}  kapasite={n} renk={n} bos={EMPTIES}  "
              f"cozumSayisi={sol_count}  enKisa={shortest_text}  ilkYol={sol_len}  "
              f"durum={states}  deneme={attempts}  sure={ms:.1f}+{short_ms:.1f} ms")
        for t, tube in enumerate(board):
            print(f"    Tube {t}: [{tubes[t]}]")

    # Unity'nin JsonUtility'si ic ice dizi okuyamaz; tupler bu yuzden
    # "dipten yukari virgullu metin" olarak yazilir ("" = bos tup).
    script_dir = os.path.dirname(os.path.abspath(__file__))
    out_path = os.path.normpath(os.path.join(
        script_dir, "..", "..", "Assets", "Resources", "levels.json"))

    with open(out_path, "w", encoding="utf-8") as f:
        json.dump({"levels": levels}, f, indent=2)

    print(f"\nBITTI — {len(levels)} level yazildi: {out_path}")


if __name__ == "__main__":
    main()
