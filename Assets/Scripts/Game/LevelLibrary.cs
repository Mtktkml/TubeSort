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
            public int capacity;
            public string[] tubes;
        }

        [Serializable]
        private class LevelFile
        {
            public LevelData[] levels;
        }

        /// <summary>
        /// İstenen leveli tahta olarak kurar. Bulunamazsa ya da dosya
        /// bozuksa hata loglayıp null döner; çağıran taraf yedeğe düşer.
        /// </summary>
        public static Board Load(int levelNumber)
        {
            var asset = Resources.Load<TextAsset>("levels");
            if (asset == null)
            {
                Debug.LogError("levels.json bulunamadı (Assets/Resources/levels.json). " +
                               "Tools/SolverBenchmark/generate_levels.py ile üretilmeli.");
                return null;
            }

            LevelFile file = JsonUtility.FromJson<LevelFile>(asset.text);
            if (file?.levels == null)
            {
                Debug.LogError("levels.json çözümlenemedi.");
                return null;
            }

            foreach (LevelData data in file.levels)
            {
                if (data.level != levelNumber) continue;

                var tubes = new List<Tube>(data.tubes.Length);
                foreach (string text in data.tubes)
                    tubes.Add(ParseTube(text, data.capacity));

                return new Board(tubes);
            }

            Debug.LogError($"Level {levelNumber} levels.json içinde yok.");
            return null;
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
