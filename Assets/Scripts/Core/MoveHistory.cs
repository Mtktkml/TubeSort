using System.Collections.Generic;

namespace TubeSort.Core
{
    /// <summary>
    /// Oynanan hamlelerin geçmişi (undo için). Bilerek Board'a gömülmedi:
    /// Board'u yoğun kullanan taraflar (ör. arama/çözücü) hamle başına
    /// geçmiş tutma maliyeti ödememeli. Kaydeden ve geri alan tarafın aynı
    /// tahtayla çalışması çağıranın sorumluluğundadır.
    /// </summary>
    public class MoveHistory
    {
        private readonly Stack<PourResult> moves = new Stack<PourResult>();

        public int Count => moves.Count;
        public bool IsEmpty => moves.Count == 0;

        /// <summary>Başarılı hamleyi geçmişe ekler; başarısız hamleyi yok sayar.</summary>
        public void Record(PourResult move)
        {
            if (move.Success)
                moves.Push(move);
        }

        /// <summary>
        /// Son hamleyi tahtada geri alır; geri alınan hamleyi undone ile
        /// bildirir (görsel güncelleme bunu kullanır). Geçmiş boşsa false.
        /// </summary>
        public bool TryUndo(Board board, out PourResult undone)
        {
            if (moves.Count == 0)
            {
                undone = PourResult.Fail;
                return false;
            }

            undone = moves.Pop();
            board.UndoPour(undone);
            return true;
        }

        /// <summary>Geçmişi boşaltır (yeni tahta yüklenince çağrılır).</summary>
        public void Clear() => moves.Clear();
    }
}
