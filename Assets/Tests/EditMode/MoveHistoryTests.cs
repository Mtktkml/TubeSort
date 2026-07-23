using NUnit.Framework;
using TubeSort.Core;

namespace TubeSort.Tests
{
    public class MoveHistoryTests
    {
        private const int Red = 0;
        private const int Yellow = 1;

        [Test]
        public void Record_ThenTryUndo_RestoresBoard()
        {
            var board = new Board(new[]
            {
                new Tube(4, Red, Red),
                new Tube(4)
            });
            var history = new MoveHistory();

            history.Record(board.Pour(0, 1));

            Assert.IsTrue(history.TryUndo(board, out PourResult undone));
            Assert.AreEqual(0, undone.FromIndex);
            Assert.AreEqual(1, undone.ToIndex);
            Assert.AreEqual(2, board[0].Count, "Kaynak tüp eski haline dönmeli");
            Assert.IsTrue(board[1].IsEmpty, "Hedef tüp boşalmalı");
            Assert.IsTrue(history.IsEmpty, "Geri alınan hamle geçmişten çıkmalı");
        }

        [Test]
        public void TryUndo_OnEmptyHistory_ReturnsFalse()
        {
            var board = new Board(new[] { new Tube(4, Red), new Tube(4) });
            var history = new MoveHistory();

            Assert.IsFalse(history.TryUndo(board, out _));
            Assert.AreEqual(1, board[0].Count, "Boş geçmişte tahta değişmemeli");
        }

        [Test]
        public void Record_IgnoresFailedMove()
        {
            var board = new Board(new[]
            {
                new Tube(4, Red),
                new Tube(4, Yellow)
            });
            var history = new MoveHistory();

            // Farklı renk üstüne dökme geçersizdir; Fail kaydedilmemeli.
            history.Record(board.Pour(0, 1));

            Assert.IsTrue(history.IsEmpty);
        }

        [Test]
        public void TryUndo_UndoesInReverseOrder()
        {
            var board = new Board(new[]
            {
                new Tube(4, Red, Yellow),
                new Tube(4),
                new Tube(4)
            });
            var history = new MoveHistory();

            history.Record(board.Pour(0, 1)); // sarı -> tüp 1
            history.Record(board.Pour(0, 2)); // kırmızı -> tüp 2

            // LIFO: önce kırmızı geri gelmeli, sonra sarı.
            Assert.IsTrue(history.TryUndo(board, out PourResult first));
            Assert.AreEqual(2, first.ToIndex);
            Assert.IsTrue(history.TryUndo(board, out PourResult second));
            Assert.AreEqual(1, second.ToIndex);

            Assert.AreEqual(2, board[0].Count, "Tahta başlangıç durumuna dönmeli");
            Assert.AreEqual(Yellow, board[0].TopColor, "Katman sırası korunmalı");
            Assert.IsTrue(board[1].IsEmpty);
            Assert.IsTrue(board[2].IsEmpty);
        }

        [Test]
        public void Clear_EmptiesHistory()
        {
            var board = new Board(new[] { new Tube(4, Red, Red), new Tube(4) });
            var history = new MoveHistory();

            history.Record(board.Pour(0, 1));
            history.Clear();

            Assert.IsTrue(history.IsEmpty);
            Assert.IsFalse(history.TryUndo(board, out _),
                "Temizlenen geçmiş geri alınamamalı");
        }
    }
}
