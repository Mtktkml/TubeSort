using UnityEngine;

namespace TubeSort.Game
{
    /// <summary>
    /// Çekirdeğin kullandığı renk kimliklerini (int) ekrandaki gerçek renklere çevirir.
    /// Çekirdek "2 numaralı renk" der; hangi mavi olduğuna burası karar verir.
    /// </summary>
    public class ColorPalette
    {
        // On renk (kapasite 12 / renk 10 tavanı için). Değerler Linear uzayda;
        // shader'a TubeView.ToShaderColor ile çevrilir. Ayırt-edilebilirlik
        // cihazda (Device Simulator) gözle doğrulanır. Sıkışık çiftler kontrol:
        // teal(5)~mavi(2), gül(9)~somon(7)~kırmızı(0), kahve(8)~turuncu(4).
        private static readonly Color[] Colors =
        {
            new Color(0.90f, 0.28f, 0.28f),   // 0 kırmızı
            new Color(0.95f, 0.78f, 0.15f),   // 1 sarı
            new Color(0.28f, 0.58f, 0.90f),   // 2 mavi
            new Color(0.35f, 0.78f, 0.38f),   // 3 yeşil
            new Color(0.95f, 0.58f, 0.22f),   // 4 turuncu
            new Color(0.08f, 0.52f, 0.50f),   // 5 petrol (koyulaştırıldı: açık cam
                                              // zeminde boş tüpten ayrılsın diye)
            new Color(0.62f, 0.38f, 0.75f),   // 6 mor
            new Color(0.88f, 0.55f, 0.48f),   // 7 somon
            new Color(0.55f, 0.36f, 0.22f),   // 8 kahve (yeni)
            new Color(0.86f, 0.30f, 0.55f),   // 9 gül/pembe (yeni; saf magenta hata
                                              // renginden ayrı: G>0, B<1)
        };

        public Color Get(int colorId)
        {
            if (colorId < 0 || colorId >= Colors.Length)
                return Color.magenta;   // tanımsız renk göze batsın

            return Colors[colorId];
        }
    }
}
