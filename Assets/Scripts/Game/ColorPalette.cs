using UnityEngine;

namespace TubeSort.Game
{
    /// <summary>
    /// Çekirdeğin kullandığı renk kimliklerini (int) ekrandaki gerçek renklere çevirir.
    /// Çekirdek "2 numaralı renk" der; hangi mavi olduğuna burası karar verir.
    /// </summary>
    public class ColorPalette
    {
        private static readonly Color[] Colors =
        {
            new Color(0.90f, 0.22f, 0.21f),   // 0 kırmızı
            new Color(0.99f, 0.80f, 0.09f),   // 1 sarı
            new Color(0.13f, 0.59f, 0.95f),   // 2 mavi
            new Color(0.30f, 0.75f, 0.31f),   // 3 yeşil
            new Color(1.00f, 0.60f, 0.20f),   // 4 turuncu
            new Color(0.95f, 0.95f, 0.95f),   // 5 beyaz
            new Color(0.61f, 0.35f, 0.71f),   // 6 mor
            new Color(0.91f, 0.60f, 0.55f),   // 7 somon
        };

        public int Count => Colors.Length;

        public Color Get(int colorId)
        {
            if (colorId < 0 || colorId >= Colors.Length)
                return Color.magenta;   // tanımsız renk göze batsın

            return Colors[colorId];
        }
    }
}
