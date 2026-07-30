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
            new Color(0.90f, 0.28f, 0.28f),   // 0 kırmızı
            new Color(0.95f, 0.78f, 0.15f),   // 1 sarı
            new Color(0.28f, 0.58f, 0.90f),   // 2 mavi
            new Color(0.35f, 0.78f, 0.38f),   // 3 yeşil
            new Color(0.95f, 0.58f, 0.22f),   // 4 turuncu
            new Color(0.10f, 0.62f, 0.62f),   // 5 petrol (açık/beyaza yakın renk
                                              // açık cam zeminde boş tüpten
                                              // ayırt edilemez)
            new Color(0.62f, 0.38f, 0.75f),   // 6 mor
            new Color(0.88f, 0.55f, 0.48f),   // 7 somon
        };

        public Color Get(int colorId)
        {
            if (colorId < 0 || colorId >= Colors.Length)
                return Color.magenta;   // tanımsız renk göze batsın

            return Colors[colorId];
        }
    }
}
