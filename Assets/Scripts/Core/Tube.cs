using System.Collections.Generic;

namespace TubeSort.Core
{
    /// <summary>
    /// Bir tüpün içindeki sıvı. Dipten yukarı doğru birim listesi tutar:
    /// liquid[0] en alttaki birim, liquid[Count-1] en üstteki birim.
    /// Unity'ye hiç bağımlı değildir; ekranda nerede durduğunu bilmez.
    /// </summary>
    public class Tube
    {
        public const int DefaultCapacity = 4;

        private readonly List<int> liquid = new List<int>();

        public int Capacity { get; }

        public Tube(int capacity = DefaultCapacity)
        {
            Capacity = capacity;
        }

        public Tube(int capacity, params int[] unitsFromBottom) : this(capacity)
        {
            liquid.AddRange(unitsFromBottom);
        }

        public IReadOnlyList<int> Liquid => liquid;
        public int Count => liquid.Count;
        public bool IsEmpty => liquid.Count == 0;
        public bool IsFull => liquid.Count == Capacity;
        public int FreeSpace => Capacity - liquid.Count;

        /// <summary>En üstteki rengi verir. Tüp boşsa -1.</summary>
        public int TopColor => IsEmpty ? -1 : liquid[liquid.Count - 1];

        /// <summary>
        /// En üstteki bitişik aynı renkli birim sayısı.
        /// Örn. dipten yukarı [kırmızı, sarı, sarı, sarı] -> 3.
        /// Bu, tek hamlede dökülebilecek maksimum miktardır.
        /// </summary>
        public int TopSegmentLength
        {
            get
            {
                if (IsEmpty) return 0;

                int top = TopColor;
                int length = 0;
                for (int i = liquid.Count - 1; i >= 0 && liquid[i] == top; i--)
                    length++;

                return length;
            }
        }

        /// <summary>
        /// Tüp tamamlanmış mı: ya tamamen boş, ya da tek renkle ağzına kadar dolu.
        /// </summary>
        public bool IsComplete => IsEmpty || (IsFull && TopSegmentLength == Capacity);

        public void Push(int color, int amount = 1)
        {
            for (int i = 0; i < amount; i++)
                liquid.Add(color);
        }

        public void Pop(int amount = 1)
        {
            for (int i = 0; i < amount; i++)
                liquid.RemoveAt(liquid.Count - 1);
        }

        public Tube Clone()
        {
            var copy = new Tube(Capacity);
            copy.liquid.AddRange(liquid);
            return copy;
        }
    }
}
