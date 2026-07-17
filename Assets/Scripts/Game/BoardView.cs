using System.Collections.Generic;
using TubeSort.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TubeSort.Game
{
    /// <summary>
    /// Tahtayı ekranda kurar ve tıklamaları çekirdeğe iletir.
    ///
    /// Etkileşim: bir tüpe tıkla (seçilir, yukarı kalkar), sonra başka bir tüpe
    /// tıkla (dökülür). Aynı tüpe tekrar tıklarsan seçim iptal olur.
    ///
    /// Sahne kurulumu gerektirmez: boş bir GameObject'e bu bileşeni ekleyip
    /// Play'e basman yeterli, gerisini kod yapar.
    /// </summary>
    public class BoardView : MonoBehaviour
    {
        [Header("Yerleşim")]
        [SerializeField] private float horizontalSpacing = 1.2f;
        [SerializeField] private float verticalSpacing = 3.2f;
        [SerializeField] private int tubesPerRow = 5;

        private Board board;
        private ColorPalette palette;
        private Sprite unitSprite;
        private readonly List<TubeView> tubeViews = new List<TubeView>();

        private int selectedIndex = -1;
        private Camera mainCamera;

        private void Start()
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogError("MainCamera etiketli bir kamera yok; tıklama çalışamaz.");
                enabled = false;
                return;
            }

            palette = new ColorPalette();
            unitSprite = CreateSquareSprite();

            board = CreateTestBoard();
            BuildViews();
        }

        /// <summary>
        /// Elle kurulmuş geçici bir tahta. Level üretici gelene kadar bununla test ediyoruz.
        /// Kasıtlı olarak karışık: hem tam dökme, hem kısmi dökme denenebilsin.
        /// </summary>
        private Board CreateTestBoard()
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

        private void BuildViews()
        {
            for (int i = 0; i < board.TubeCount; i++)
            {
                var go = new GameObject($"Tube{i}");
                go.transform.SetParent(transform, false);
                go.transform.position = LayoutPosition(i);

                var view = go.AddComponent<TubeView>();
                view.Initialize(i, board[i], palette, unitSprite);
                tubeViews.Add(view);
            }
        }

        /// <summary>Tüpleri satırlara böler ve her satırı yatayda ortalar.</summary>
        private Vector3 LayoutPosition(int index)
        {
            int row = index / tubesPerRow;
            int column = index % tubesPerRow;

            int totalRows = Mathf.CeilToInt(board.TubeCount / (float)tubesPerRow);
            int tubesInThisRow = Mathf.Min(tubesPerRow, board.TubeCount - row * tubesPerRow);

            float x = (column - (tubesInThisRow - 1) * 0.5f) * horizontalSpacing;
            float y = ((totalRows - 1) * 0.5f - row) * verticalSpacing;

            return new Vector3(x, y, 0f);
        }

        private void Update()
        {
            // Pointer, Mouse ve Touchscreen'in ortak atasıdır: masaüstünde fare,
            // telefonda (ve Device Simulator'da) parmak aynı kodla okunur.
            Pointer pointer = Pointer.current;
            if (pointer == null) return;

            if (!pointer.press.wasPressedThisFrame) return;

            TubeView clicked = RaycastTube(pointer.position.ReadValue());
            if (clicked != null)
                HandleTubeClick(clicked.Index);
        }

        /// <summary>Ekran koordinatındaki dokunuşun hangi tüpe denk geldiğini bulur.</summary>
        private TubeView RaycastTube(Vector2 screenPosition)
        {
            Vector3 worldPoint = mainCamera.ScreenToWorldPoint(screenPosition);
            Collider2D hit = Physics2D.OverlapPoint(worldPoint);

            return hit != null ? hit.GetComponent<TubeView>() : null;
        }

        private void HandleTubeClick(int index)
        {
            // Henüz seçim yok: boş tüpten dökme yapılamayacağı için boş tüp seçtirmiyoruz.
            if (selectedIndex == -1)
            {
                if (board[index].IsEmpty) return;

                selectedIndex = index;
                tubeViews[index].SetSelected(true);
                return;
            }

            // Aynı tüpe tekrar tıklandı: seçimi iptal et.
            if (selectedIndex == index)
            {
                ClearSelection();
                return;
            }

            PourResult result = board.Pour(selectedIndex, index);

            if (result.Success)
            {
                tubeViews[result.FromIndex].Refresh();
                tubeViews[result.ToIndex].Refresh();
                Debug.Log($"{result.Amount} birim renk#{result.Color}: tüp {result.FromIndex} -> tüp {result.ToIndex}");

                ClearSelection();
                ReportBoardState();
                return;
            }

            // Hamle geçersizdi. Oyuncu muhtemelen yeni bir kaynak seçmek istiyor:
            // seçimi iptal etmek yerine seçimi tıklanan tüpe taşımak daha rahat.
            ClearSelection();
            if (!board[index].IsEmpty)
            {
                selectedIndex = index;
                tubeViews[index].SetSelected(true);
            }
        }

        private void ClearSelection()
        {
            if (selectedIndex == -1) return;

            tubeViews[selectedIndex].SetSelected(false);
            selectedIndex = -1;
        }

        private void ReportBoardState()
        {
            if (board.IsSolved)
                Debug.Log("<color=lime>Tahta çözüldü!</color>");
            else if (!board.HasAnyValidMove)
                Debug.Log("<color=orange>Çıkmaz: oynanacak hamle kalmadı.</color>");
        }

        /// <summary>
        /// Tek beyaz pikselden sprite üretir. SpriteRenderer'ın ölçeğiyle
        /// istediğimiz dikdörtgene dönüşür; hazır bir dosyaya ihtiyaç kalmaz.
        /// </summary>
        private static Sprite CreateSquareSprite()
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}
