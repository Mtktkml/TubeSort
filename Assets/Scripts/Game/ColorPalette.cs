using UnityEngine;

namespace TubeSort.Game
{
    /// <summary>
    /// Çekirdeğin kullandığı renk kimliklerini (int) ekrandaki gerçek renklere çevirir.
    /// Çekirdek "2 numaralı renk" der; hangi mavi olduğuna burası karar verir.
    /// </summary>
    public class ColorPalette
    {
        // On renk (kapasite 12 / renk 10 tavanı için) — tasarım ekibinin renk
        // kartelası (color code.png, 4 Ağu), hex birebir. Değerler sRGB;
        // shader'a giderken TubeView.ToShaderColor Linear'a çevirir.
        // Ayırt-edilebilirlik cihazda (Device Simulator) gözle doğrulanır;
        // sıkışık çiftler kontrol: yeşil(3)~elma(9), mavi(2)~camgöbeği(5),
        // kırmızı(0)~turuncu(4)~pembe(7).
        private static readonly Color[] Colors =
        {
            new Color32(0xE5, 0x12, 0x12, 0xFF),   // 0 kırmızı   E51212
            new Color32(0xFF, 0xC3, 0x00, 0xFF),   // 1 sarı      FFC300
            new Color32(0x1E, 0x8F, 0xFF, 0xFF),   // 2 mavi      1E8FFF
            new Color32(0x1E, 0xD3, 0x46, 0xFF),   // 3 yeşil     1ED346
            new Color32(0xFF, 0x7A, 0x00, 0xFF),   // 4 turuncu   FF7A00
            new Color32(0x00, 0xD8, 0xFF, 0xFF),   // 5 camgöbeği 00D8FF (eski petrol yuvası)
            new Color32(0x9C, 0x3B, 0xFF, 0xFF),   // 6 mor       9C3BFF
            new Color32(0xFF, 0x4F, 0xC8, 0xFF),   // 7 pembe     FF4FC8 (eski somon yuvası;
                                                   // saf magenta hata renginden ayrı: B<1)
            new Color32(0x78, 0x44, 0x1B, 0xFF),   // 8 kahve     78441B
            new Color32(0x8D, 0xB6, 0x00, 0xFF),   // 9 elma yeşili 8DB600 (eski gül yuvası)
        };

        public Color Get(int colorId)
        {
            if (colorId < 0 || colorId >= Colors.Length)
                return Color.magenta;   // tanımsız renk göze batsın

            return Colors[colorId];
        }
    }
}
