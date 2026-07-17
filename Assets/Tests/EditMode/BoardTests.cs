using NUnit.Framework;
using TubeSort.Core;

namespace TubeSort.Tests
{
    // Renkleri okunur kılmak için kısayollar.
    // Çekirdek mantık rengi sadece "int kimlik" olarak görür.
    public class BoardTests
    {
        private const int Red = 0;
        private const int Yellow = 1;
        private const int Blue = 2;

        [Test]
        public void TopSegmentLength_CountsContiguousSameColorUnits()
        {
            // dipten yukarı: kırmızı, sarı, sarı, sarı
            var tube = new Tube(4, Red, Yellow, Yellow, Yellow);
            Assert.AreEqual(3, tube.TopSegmentLength);
        }

        [Test]
        public void TopSegmentLength_StopsAtColorChange()
        {
            // dipten yukarı: sarı, kırmızı, sarı  -> üstteki segment sadece 1
            var tube = new Tube(4, Yellow, Red, Yellow);
            Assert.AreEqual(1, tube.TopSegmentLength);
        }

        [Test]
        public void Pour_TransfersWholeSegmentInOneMove()
        {
            var board = new Board(new[]
            {
                new Tube(4, Yellow, Yellow, Yellow),
                new Tube(4)
            });

            PourResult result = board.Pour(0, 1);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(3, result.Amount, "Üç birim birden akmalıydı");
            Assert.AreEqual(Yellow, result.Color);
            Assert.IsTrue(board[0].IsEmpty);
            Assert.AreEqual(3, board[1].Count);
        }

        [Test]
        public void Pour_TransfersPartiallyWhenTargetLacksSpace()
        {
            // Kaynakta 3 sarı var ama hedefte sadece 1 birim yer kaldı.
            var board = new Board(new[]
            {
                new Tube(4, Yellow, Yellow, Yellow),
                new Tube(4, Red, Red, Yellow)
            });

            PourResult result = board.Pour(0, 1);

            Assert.AreEqual(1, result.Amount, "Sadece sığan kadarı akmalıydı");
            Assert.AreEqual(2, board[0].Count, "Kalan 2 sarı kaynakta kalmalıydı");
            Assert.IsTrue(board[1].IsFull);
        }

        [Test]
        public void Pour_RejectsDifferentColorTarget()
        {
            var board = new Board(new[]
            {
                new Tube(4, Yellow),
                new Tube(4, Red)
            });

            Assert.IsFalse(board.IsValidMove(0, 1));
            Assert.IsFalse(board.Pour(0, 1).Success);
            Assert.AreEqual(1, board[0].Count, "Geçersiz hamle tahtayı değiştirmemeli");
        }

        [Test]
        public void Pour_RejectsFullTarget()
        {
            var board = new Board(new[]
            {
                new Tube(4, Yellow),
                new Tube(4, Yellow, Yellow, Yellow, Yellow)
            });

            Assert.IsFalse(board.IsValidMove(0, 1));
        }

        [Test]
        public void Pour_AcceptsAnyColorIntoEmptyTube()
        {
            var board = new Board(new[]
            {
                new Tube(4, Red, Yellow),
                new Tube(4)
            });

            Assert.IsTrue(board.IsValidMove(0, 1));
        }

        [Test]
        public void Pour_RejectsEmptySource()
        {
            var board = new Board(new[] { new Tube(4), new Tube(4) });
            Assert.IsFalse(board.IsValidMove(0, 1));
        }

        [Test]
        public void UndoPour_RestoresPreviousState()
        {
            var board = new Board(new[]
            {
                new Tube(4, Yellow, Yellow, Yellow),
                new Tube(4, Red, Red, Yellow)
            });

            PourResult move = board.Pour(0, 1);
            board.UndoPour(move);

            Assert.AreEqual(3, board[0].Count);
            Assert.AreEqual(3, board[1].Count);
            Assert.AreEqual(Yellow, board[1].TopColor);
        }

        [Test]
        public void IsSolved_TrueWhenEveryTubeIsSingleColorOrEmpty()
        {
            var board = new Board(new[]
            {
                new Tube(4, Red, Red, Red, Red),
                new Tube(4, Blue, Blue, Blue, Blue),
                new Tube(4)
            });

            Assert.IsTrue(board.IsSolved);
        }

        [Test]
        public void IsSolved_FalseWhenSingleColorTubeIsNotFull()
        {
            // Tek renk ama dolu değil -> henüz bitmiş sayılmaz.
            var board = new Board(new[]
            {
                new Tube(4, Red, Red),
                new Tube(4, Red, Red)
            });

            Assert.IsFalse(board.IsSolved);
        }

        [Test]
        public void HasAnyValidMove_DetectsDeadlock()
        {
            // Hiçbir üst renk eşleşmiyor ve boş tüp yok -> çıkmaz.
            var board = new Board(new[]
            {
                new Tube(2, Red, Yellow),
                new Tube(2, Yellow, Blue),
                new Tube(2, Blue, Red)
            });

            Assert.IsFalse(board.HasAnyValidMove);
        }
    }
}
