using TubeSort.Core;

namespace TubeSort.Tests.PlayMode
{
    /// <summary>
    /// Testlerin BoardView'a enjekte ettiği standart tahta. Görünüm testleri
    /// level verisinden bağımsız ve deterministik kalsın diye sabit dizilim.
    /// </summary>
    public static class TestBoards
    {
        /// <summary>6 tüp, kapasite 4: 4 karışık dolu + 2 boş — hem tam hem
        /// kısmi dökme denenebilir.</summary>
        public static Board Classic()
        {
            const int Red = 0, Yellow = 1, Blue = 2, Green = 3;

            return new Board(new[]
            {
                new Tube(4, Red, Yellow, Yellow, Blue),
                new Tube(4, Green, Red, Blue, Yellow),
                new Tube(4, Blue, Green, Green, Red),
                new Tube(4, Yellow, Blue, Red, Green),
                new Tube(4),
                new Tube(4)
            });
        }
    }
}
