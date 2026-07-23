using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TubeSort.Core;
using TubeSort.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace TubeSort.Tests.PlayMode
{
    /// <summary>
    /// Dökme animasyonu donma regresyonu. Kök neden: sıvı-ağızda kontrolü
    /// shader'ın 0.2 kelepçeli eğim formülünü kullanıyordu; kelepçe eğimi
    /// ~tan(78.7°) ile tavanladığı için uzun tüplerde (kapasite >= 5) az
    /// sıvı dökülürken "açı asla yetmiyor" kilidi oluşuyor, AnimatePour
    /// sonsuza dek bekliyordu (tüp eğik donar, isAnimating true kalır,
    /// dokunuşlar yutulur). Bu testler donduran kombinasyonların makul
    /// sürede tamamlandığını doğrular; watchdog tetiklenirse LogError
    /// testi zaten kırmızıya düşürür.
    /// </summary>
    public class PourFreezeTests
    {
        private GameObject boardObject;
        private GameObject cameraObject;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (boardObject != null) Object.Destroy(boardObject);
            if (cameraObject != null) Object.Destroy(cameraObject);

            yield return null;
        }

        private void BuildCamera()
        {
            cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 8f;
            camera.aspect = 0.5f;
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        }

        /// <summary>
        /// Tahtayı kurar, hamleyi başlatır ve animasyonun bitmesini bekler.
        /// Donma varsa 6 sn'lik üst sınır testi kırmızıya düşürür (test
        /// koşucusunu sonsuza dek kilitlemek yerine).
        /// </summary>
        private IEnumerator AssertPourCompletes(Board board, int from, int to, string label)
        {
            BuildCamera();
            boardObject = new GameObject("BoardView");
            var view = boardObject.AddComponent<BoardView>();
            view.LoadBoard(board);
            yield return null; // Start çalışsın

            Assert.IsTrue(view.TryPour(from, to), $"{label}: hamle başlamalıydı");
            Assert.IsTrue(view.IsAnimating, $"{label}: animasyon başlamalıydı");

            float elapsed = 0f;
            while (view.IsAnimating && elapsed < 6f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.IsFalse(view.IsAnimating,
                $"{label}: animasyon {elapsed:F1} sn'de bitmedi — donma regresyonu");
        }

        [UnityTest]
        public IEnumerator Pour_SingleUnitFromCapacity5Tube_Completes()
        {
            // Telefonda donduran kombinasyon (level 3): kapasite 5, tek birim.
            // Eski formülde açı tavanı kenar yüksekliğine yetmiyordu.
            var board = new Board(new List<Tube>
            {
                new Tube(5, 0),
                new Tube(5, 0, 0),
                new Tube(5),
            });

            yield return AssertPourCompletes(board, 0, 1, "kapasite 5 / tek birim");
        }

        [UnityTest]
        public IEnumerator Pour_SingleUnitFromCapacity7Tube_Completes()
        {
            // En kötü en-boy oranı: kapasite 7'de eski açı tavanı fill 0.44'e
            // kadar olan her kaynağı kilitliyordu.
            var board = new Board(new List<Tube>
            {
                new Tube(7, 0),
                new Tube(7, 0, 0),
                new Tube(7),
            });

            yield return AssertPourCompletes(board, 0, 1, "kapasite 7 / tek birim");
        }

        [UnityTest]
        public IEnumerator Pour_EmptyingCapacity7TubeCompletely_Completes()
        {
            // Kaynak tamamen boşalır: fill 0'a inerken açı 90°'yi aşar;
            // 90° ve ötesi "koşulsuz ağızda" kuralını da sınar.
            var board = new Board(new List<Tube>
            {
                new Tube(7, 0, 0, 0),
                new Tube(7, 0),
                new Tube(7),
            });

            yield return AssertPourCompletes(board, 0, 1, "kapasite 7 / tam boşaltma");
        }
    }
}
