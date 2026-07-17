using System;
using System.Collections.Generic;

namespace TubeSort.Core
{
    /// <summary>
    /// Bir dökme hamlesinin sonucu. Animasyon katmanı bu bilgiyi kullanır:
    /// "hangi renkten, kaç birim, nereden nereye aktı".
    /// </summary>
    public readonly struct PourResult
    {
        public readonly bool Success;
        public readonly int FromIndex;
        public readonly int ToIndex;
        public readonly int Color;
        public readonly int Amount;

        public PourResult(bool success, int fromIndex, int toIndex, int color, int amount)
        {
            Success = success;
            FromIndex = fromIndex;
            ToIndex = toIndex;
            Color = color;
            Amount = amount;
        }

        public static PourResult Fail => new PourResult(false, -1, -1, -1, 0);
    }

    /// <summary>
    /// Tüplerin tamamı ve oyunun kuralları. Saf C#, Unity'den bağımsız.
    /// </summary>
    public class Board
    {
        private readonly List<Tube> tubes;

        public Board(IEnumerable<Tube> tubes)
        {
            this.tubes = new List<Tube>(tubes);
        }

        public IReadOnlyList<Tube> Tubes => tubes;
        public int TubeCount => tubes.Count;

        public Tube this[int index] => tubes[index];

        /// <summary>
        /// Kaynak tüpten hedefe kaç birim akabileceğini hesaplar. 0 = hamle geçersiz.
        /// Kural: üstteki bitişik segment, hedefteki boş yer kadar akar (hangisi azsa).
        /// </summary>
        public int PourableAmount(int fromIndex, int toIndex)
        {
            if (fromIndex == toIndex) return 0;
            if (fromIndex < 0 || fromIndex >= tubes.Count) return 0;
            if (toIndex < 0 || toIndex >= tubes.Count) return 0;

            Tube from = tubes[fromIndex];
            Tube to = tubes[toIndex];

            if (from.IsEmpty) return 0;
            if (to.IsFull) return 0;

            // Hedef doluysa, üstündeki renk kaynağınkiyle aynı olmalı.
            if (!to.IsEmpty && to.TopColor != from.TopColor) return 0;

            return Math.Min(from.TopSegmentLength, to.FreeSpace);
        }

        public bool IsValidMove(int fromIndex, int toIndex) => PourableAmount(fromIndex, toIndex) > 0;

        /// <summary>Hamleyi uygular. Geçersizse tahtayı değiştirmez ve Fail döner.</summary>
        public PourResult Pour(int fromIndex, int toIndex)
        {
            int amount = PourableAmount(fromIndex, toIndex);
            if (amount == 0) return PourResult.Fail;

            Tube from = tubes[fromIndex];
            int color = from.TopColor;

            from.Pop(amount);
            tubes[toIndex].Push(color, amount);

            return new PourResult(true, fromIndex, toIndex, color, amount);
        }

        /// <summary>Bir hamleyi geri alır (undo için).</summary>
        public void UndoPour(PourResult move)
        {
            if (!move.Success) return;

            tubes[move.ToIndex].Pop(move.Amount);
            tubes[move.FromIndex].Push(move.Color, move.Amount);
        }

        /// <summary>Her tüp ya boş ya da tek renkle dolu mu?</summary>
        public bool IsSolved
        {
            get
            {
                foreach (Tube tube in tubes)
                    if (!tube.IsComplete) return false;

                return true;
            }
        }

        /// <summary>Oynanabilecek hiçbir hamle kalmadı mı? (çıkmaz tespiti)</summary>
        public bool HasAnyValidMove
        {
            get
            {
                for (int i = 0; i < tubes.Count; i++)
                    for (int j = 0; j < tubes.Count; j++)
                        if (IsValidMove(i, j)) return true;

                return false;
            }
        }

        /// <summary>Yeni bir boş tüp ekler ("+1 tüp" özelliği).</summary>
        public void AddTube(int capacity = Tube.DefaultCapacity)
        {
            tubes.Add(new Tube(capacity));
        }

        public Board Clone()
        {
            var copies = new List<Tube>(tubes.Count);
            foreach (Tube tube in tubes)
                copies.Add(tube.Clone());

            return new Board(copies);
        }
    }
}
