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
            verdict, states, sol_len = cc.solve(board, n)
            ms = (time.perf_counter() - t0) * 1000

            if verdict == "SOLVABLE":
                break
            # Cozulemez ya da butce asimi: at, yeniden uret (ayri ayri logla).
            print(f"  Level {index}: deneme {attempts} atildi ({verdict})")

        tubes = [",".join(str(c) for c in tube) for tube in board]
        levels.append({"level": index, "capacity": n, "tubes": tubes})

        print(f"Level {index}  ({n} renk x {n} kapasite + {EMPTIES} bos)  "
              f"deneme={attempts}  cozum={sol_len} hamle  durum={states}  "
              f"dogrulama={ms:.1f} ms")
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
