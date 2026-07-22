"""TubeSort — C# benchmark tablolarinin Python karsiligi.

Iki tabloyu Python'da uretir (crosscheck.py'deki, C# ile 8/8 birebir
dogrulanmis kural implementasyonunu kullanarak):

  Tablo 1 — Ana benchmark (N=3..10): cozulebilir ornek (2 bos tup) icin
            hamle/durum/sure; cozulemez ornek (1 bos tup) icin durum/sure.
  Tablo 2 — 2 bos tuple cozulemez tahta avi: boyut basina en fazla 30.000
            deneme ya da zaman tavani; ilk bulunan cozulemezin kanit maliyeti.

NOT: Sureler Python'a aittir, C#/oyun sureleriyle karsilastirilamaz
(Python 10-50x yavas). Karsilastirilabilir olan karar ve durum sayilaridir.
Rastgelelik sabit tohumlu ama Python'un RNG'si C#'inkinden farkli dizi
uretir; bu yuzden "kacinci denemede bulundu" gibi sayilar C# tablosuyla
birebir ayni cikmaz — ayni cikmasi gereken sey davranisin karakteridir.

Calistirma:  python benchmark.py   (rapor: py_benchmark.md)
"""

import os
import random
import threading
import time

import crosscheck as cc


def table2_hunt(out):
    out.append("## Tablo 2 — 2 bos tuple cozulemez tahta avi (Python)")
    out.append("")
    out.append("Boyut basina en fazla 30.000 deneme ya da 240 s.")
    out.append("")
    out.append("| Boyut | Denenen tahta | Sonuc | Cozulemez kaniti: durum / sure |")
    out.append("|---|---|---|---|")

    rng = random.Random(7)
    for n in range(3, 11):
        cc.set_stage(f"av {n}x{n} kosuyor")
        wall = time.perf_counter()
        tries = 0
        found = None

        while tries < 30000 and time.perf_counter() - wall < 240:
            tries += 1
            board = cc.generate(n, n, 2, rng)
            t0 = time.perf_counter()
            verdict, states, _ = cc.solve(board, n)
            ms = (time.perf_counter() - t0) * 1000
            if verdict == "UNSOLVABLE":
                found = (states, ms)
                break

        if found:
            states, ms = found
            out.append(f"| {n}x{n} | {tries}'de ilki | bulundu "
                       f"| {states} / {ms:.1f} ms |")
        else:
            note = " (teorik olarak imkansiz: k(3,3)<=2)" if n == 3 else ""
            out.append(f"| {n}x{n} | {tries} | bulunamadi{note} | - |")

    out.append("")


def main():
    out = ["# TubeSort — Python Benchmark Raporu", ""]
    out.append("Kurallarin Python implementasyonu C# ile 8/8 birebir capraz")
    out.append("dogrulanmistir (bkz. crosscheck.py). Sureler Python'a aittir.")
    out.append("")

    t0 = time.time()
    stop = threading.Event()
    ticker = threading.Thread(target=cc._ticker, args=(stop, t0), daemon=True)
    ticker.start()

    # Tablo 1 — crosscheck.py'deki ana benchmark birebir ayni kurgu.
    cc.set_stage("tablo 1 (ana benchmark)")
    out.append("## Tablo 1 — Ana benchmark (Python)")
    out.append("")
    cc.run_benchmark(out)

    # Tablo 2 — cozulemez avi.
    table2_hunt(out)

    stop.set()
    ticker.join()
    print(f"\r  BITTI  toplam {time.time() - t0:.0f} sn" + " " * 40)

    report = "\n".join(out)
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "py_benchmark.md")
    with open(path, "w", encoding="utf-8") as f:
        f.write(report)

    print()
    print(report)
    print(f"\nRapor dosyasi: {path}")


if __name__ == "__main__":
    main()
