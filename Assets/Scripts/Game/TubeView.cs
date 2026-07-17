using System.Collections.Generic;
using TubeSort.Core;
using UnityEngine;

namespace TubeSort.Game
{
    /// <summary>
    /// Tek bir tüpü ekranda çizer. Çekirdekteki Tube nesnesine bakar,
    /// içindeki her sıvı birimi için bir dikdörtgen dizer.
    ///
    /// Bu geçici bir görünüm: amaç mantığın doğru çalıştığını gözle görmek.
    /// Gerçek cam sprite'ı ve sıvı shader'ı sonraki adımlarda gelecek.
    /// </summary>
    public class TubeView : MonoBehaviour
    {
        public const float Width = 0.8f;
        public const float UnitHeight = 0.5f;

        private const float SelectedLift = 0.3f;   // seçilince tüp bu kadar yükselir
        private const float LayerInset = 0.06f;    // sıvı katmanı camın içinde kalsın

        private Tube tube;
        private ColorPalette palette;
        private Sprite unitSprite;

        private readonly List<SpriteRenderer> layers = new List<SpriteRenderer>();
        private SpriteRenderer glass;
        private Vector3 restPosition;

        /// <summary>Bu görünümün tahtadaki tüp sırası. Tıklama olayında kullanılır.</summary>
        public int Index { get; private set; }

        public void Initialize(int index, Tube tube, ColorPalette palette, Sprite unitSprite)
        {
            Index = index;
            this.tube = tube;
            this.palette = palette;
            this.unitSprite = unitSprite;
            restPosition = transform.position;

            CreateGlass();
            CreateClickArea();
            Refresh();
        }

        /// <summary>Tüpün arka planı: sıvının nerede durduğunu belli eden koyu bir gövde.</summary>
        private void CreateGlass()
        {
            var go = new GameObject("Glass");
            go.transform.SetParent(transform, false);

            glass = go.AddComponent<SpriteRenderer>();
            glass.sprite = unitSprite;
            glass.color = new Color(0.16f, 0.18f, 0.20f);
            glass.sortingOrder = 0;

            float height = tube.Capacity * UnitHeight;
            go.transform.localScale = new Vector3(Width, height, 1f);
            // Sprite'ın merkezi ortada; tüpün dibi yerel sıfır noktasında dursun.
            go.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
        }

        /// <summary>Tıklamayı yakalayacak görünmez alan. Cam gövdenin tamamını kaplar.</summary>
        private void CreateClickArea()
        {
            var box = gameObject.AddComponent<BoxCollider2D>();
            float height = tube.Capacity * UnitHeight;

            box.size = new Vector2(Width, height);
            box.offset = new Vector2(0f, height * 0.5f);
        }

        /// <summary>
        /// Çekirdekteki tüpün güncel içeriğini ekrana yansıtır.
        /// Hamleden sonra çağrılır; animasyon yok, anında değişir.
        /// </summary>
        public void Refresh()
        {
            EnsureLayerCount(tube.Count);

            for (int i = 0; i < layers.Count; i++)
            {
                bool used = i < tube.Count;
                layers[i].gameObject.SetActive(used);
                if (!used) continue;

                layers[i].color = palette.Get(tube.Liquid[i]);
                // i. birim dipten yukarı i. sırada duruyor.
                layers[i].transform.localPosition = new Vector3(0f, (i + 0.5f) * UnitHeight, 0f);
            }
        }

        /// <summary>
        /// İhtiyaç kadar katman nesnesi olmasını sağlar.
        /// Fazlalıkları yok etmek yerine gizleriz; her hamlede nesne yaratıp
        /// yok etmek gereksiz maliyet olur.
        /// </summary>
        private void EnsureLayerCount(int needed)
        {
            while (layers.Count < needed)
            {
                var go = new GameObject($"Layer{layers.Count}");
                go.transform.SetParent(transform, false);

                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = unitSprite;
                renderer.sortingOrder = 1;
                go.transform.localScale = new Vector3(Width - LayerInset, UnitHeight, 1f);

                layers.Add(renderer);
            }
        }

        /// <summary>Seçili tüp yukarı kalkar; oyuncu neyi seçtiğini görsün.</summary>
        public void SetSelected(bool selected)
        {
            transform.position = selected
                ? restPosition + Vector3.up * SelectedLift
                : restPosition;
        }
    }
}
