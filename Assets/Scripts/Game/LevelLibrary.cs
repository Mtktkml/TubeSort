using System;
using System.Collections.Generic;
using TubeSort.Core;
using UnityEngine;

namespace TubeSort.Game
{
    /// <summary>
    /// Python üreticisinin yazdığı levelleri okur (Resources/levels.json).
    /// Üretim ve çözülebilirlik doğrulaması Unity dışında (Python) yapılır;
    /// buraya yalnız veri gelir. JsonUtility iç içe dizi okuyamadığı için
    /// tüpler "dipten yukarı virgüllü metin" olarak taşınır ("" = boş tüp).
    /// </summary>
    public static class LevelLibrary
    {
        [Serializable]
        private class LevelData
        {
            public int level;
            public string label;   // ekran adı, ör. "1.1" (tier.varyant)
            public int capacity;
            public string[] tubes;
        }

        [Serializable]
        private class LevelFile
        {
            public LevelData[] levels;
        }

        /// <summary>
        /// İstenen leveli varsayılan level dosyasından (levels.json) kurar.
        /// Bulunamazsa ya da dosya bozuksa hata loglayıp null döner; çağıran
        /// taraf yedeğe düşer.
        /// </summary>
        public static Board Load(int levelNumber) => LoadFrom("levels", levelNumber);

        /// <summary>
        /// Leveli verilen Resources kaynağından kurar. levels.json dışında bir
        /// dosyadan okumak için (ör. pilot önizleme: pilot_levels.json).
        /// Şema aynıdır; yalnız kaynak adı değişir. Hata mesajları kaynak adını
        /// içerir (kaynak "levels" iken eski mesajlarla birebir aynı kalır).
        /// </summary>
        public static Board LoadFrom(string resourceName, int levelNumber)
        {
            LevelFile file = ReadFile(resourceName);
            if (file?.levels == null) return null;

            foreach (LevelData data in file.levels)
            {
                if (data.level != levelNumber) continue;

                var tubes = new List<Tube>(data.tubes.Length);
                foreach (string text in data.tubes)
                    tubes.Add(ParseTube(text, data.capacity));

                return new Board(tubes);
            }

            Debug.LogError($"Level {levelNumber} {resourceName}.json içinde yok.");
            return null;
        }

        /// <summary>
        /// Verilen kaynaktaki level sayısı (gezinme için: 1..N arası dolaşma).
        /// Dosya yoksa ya da bozuksa 0.
        /// </summary>
        public static int LevelCount(string resourceName)
        {
            LevelFile file = ReadFile(resourceName);
            return file?.levels?.Length ?? 0;
        }

        /// <summary>
        /// Verilen leveldeki "label" alanını döner (ör. "1.1"); ekranda
        /// "LEVEL 1.1" göstermek ve teşhis logu için. Bulunamazsa null.
        /// label, indeksten türetilmez; JSON'daki tek doğruluk kaynağıdır.
        /// </summary>
        public static string LabelOf(string resourceName, int levelNumber)
        {
            LevelFile file = ReadFile(resourceName);
            if (file?.levels == null) return null;

            foreach (LevelData data in file.levels)
                if (data.level == levelNumber) return data.label;

            return null;
        }

        private static LevelFile ReadFile(string resourceName)
        {
            var asset = Resources.Load<TextAsset>(resourceName);
            if (asset == null)
            {
                Debug.LogError($"{resourceName}.json bulunamadı (Assets/Resources/{resourceName}.json). " +
                               "Tools/SolverBenchmark ile üretilmeli.");
                return null;
            }

            LevelFile file = JsonUtility.FromJson<LevelFile>(asset.text);
            if (file?.levels == null)
                Debug.LogError($"{resourceName}.json çözümlenemedi.");

            return file;
        }

        private static Tube ParseTube(string text, int capacity)
        {
            var tube = new Tube(capacity);
            if (string.IsNullOrEmpty(text)) return tube;

            foreach (string unit in text.Split(','))
                tube.Push(int.Parse(unit));

            return tube;
        }
    }
}
