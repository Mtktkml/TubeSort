using System.Collections;
using NUnit.Framework;
using TubeSort.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace TubeSort.Tests.PlayMode
{
    /// <summary>
    /// Tıklama zincirini parça parça doğrular. Fare girdisi olmadan çalışır:
    /// amaç "collider'lar doğru yerde mi" sorusunu input'tan ayırmak.
    /// </summary>
    public class ClickDetectionTests
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

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // Test sahnesi bomboş açılır; BoardView Camera.main beklediği için kurmalıyız.
            cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 8f;
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            boardObject = new GameObject("BoardView");
            boardObject.AddComponent<BoardView>();

            // Start() çalışsın ve fizik dünyası collider'ları kaydetsin.
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        [Test]
        public void BoardView_CreatesOneColliderPerTube()
        {
            var views = boardObject.GetComponentsInChildren<TubeView>();

            // Kesin sayı iddia edilmiyor: BoardView tahtasını kendi kurduğu için
            // test onu bilemez, sayıyı buraya elle yazmak test tahtası her
            // değiştiğinde kırılır. Tahta dışarıdan verilebilir olduğunda
            // (level üreticiyle birlikte) burada tam sayı kontrol edilebilir.
            Assert.Greater(views.Length, 0, "Hiç tüp görünümü oluşmadı");

            foreach (TubeView view in views)
            {
                var box = view.GetComponent<BoxCollider2D>();
                Assert.IsNotNull(box, $"Tüp {view.Index} collider'sız");
            }
        }

        [Test]
        public void OverlapPoint_HitsTubeAtItsOwnPosition()
        {
            var views = boardObject.GetComponentsInChildren<TubeView>();

            foreach (TubeView view in views)
            {
                // Collider'ın kendi merkezi: tahta ekrana sığmak için ölçeklense
                // bile bu nokta tüpün içinde kalır.
                Vector2 point = view.GetComponent<BoxCollider2D>().bounds.center;
                Collider2D hit = Physics2D.OverlapPoint(point);

                Assert.IsNotNull(hit, $"Tüp {view.Index} noktasında hiçbir collider yok");
                Assert.AreSame(view.gameObject, hit.gameObject,
                    $"Tüp {view.Index} yerine başka bir nesne yakalandı");
            }
        }
    }
}
