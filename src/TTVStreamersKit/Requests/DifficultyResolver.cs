// Copyright (c) 2026 L4yLa. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TTVStreamersKit.Requests
{
    /// <summary>
    /// チャットで指定された難易度の文字列を、譜面の難易度（difficulty / characteristic / label）に解決する。
    /// 照合は大文字小文字を区別しない完全一致だけで、標準の難易度名とその別名、譜面のカスタムラベルの順に見る。
    /// </summary>
    public static class DifficultyResolver
    {
        public const string StandardCharacteristic = "Standard";

        private static readonly string[] s_difficultyOrder = { "Easy", "Normal", "Hard", "Expert", "ExpertPlus" };

        /// <summary>標準の難易度の別名。1 文字の別名（e / n / h）は Easy と Expert などで曖昧になるため使わない。</summary>
        private static readonly Dictionary<string, string> s_aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "easy", "Easy" }, { "ez", "Easy" }, { "es", "Easy" }, { "イージー", "Easy" },
            { "normal", "Normal" }, { "norm", "Normal" }, { "nor", "Normal" }, { "nml", "Normal" }, { "ノーマル", "Normal" },
            { "hard", "Hard" }, { "hrd", "Hard" }, { "hd", "Hard" }, { "ハード", "Hard" },
            { "expert", "Expert" }, { "exp", "Expert" }, { "ex", "Expert" }, { "エキスパート", "Expert" },
            { "expertplus", "ExpertPlus" }, { "expert+", "ExpertPlus" }, { "exp+", "ExpertPlus" },
            { "ex+", "ExpertPlus" }, { "e+", "ExpertPlus" }, { "ep", "ExpertPlus" },
            { "エキスパートプラス", "ExpertPlus" }, { "エキプラ", "ExpertPlus" },
        };

        public sealed class ResolvedDifficulty
        {
            /// <summary>BeatSaver の表記（Easy / Normal / Hard / Expert / ExpertPlus）。</summary>
            public string Difficulty { get; set; }
            /// <summary>Standard / OneSaber / 360Degree など。</summary>
            public string Characteristic { get; set; }
            /// <summary>カスタムの難易度ラベル。なければ空。</summary>
            public string Label { get; set; }

            public string DisplayName => ToDisplayName(this.Difficulty);

            public string FullDisplayName => string.IsNullOrEmpty(this.Label) ? this.DisplayName : $"{this.Label} ({this.DisplayName})";
        }

        public static bool TryResolve(MapInfo map, string input, out ResolvedDifficulty resolved)
        {
            resolved = null;
            var entries = map?.Difficulties;
            if (entries == null || entries.Count == 0 || string.IsNullOrWhiteSpace(input)) {
                return false;
            }
            var trimmed = input.Trim();

            MapDifficulty hit = null;
            if (s_aliases.TryGetValue(trimmed, out var standard)) {
                hit = FindBest(entries, e => string.Equals(e.Difficulty, standard, StringComparison.OrdinalIgnoreCase));
            }
            if (hit == null) {
                hit = FindBest(entries, e => !string.IsNullOrEmpty(e.Label)
                                             && string.Equals(e.Label.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
            }
            if (hit == null) {
                return false;
            }
            resolved = new ResolvedDifficulty {
                Difficulty = hit.Difficulty,
                Characteristic = string.IsNullOrEmpty(hit.Characteristic) ? StandardCharacteristic : hit.Characteristic,
                Label = hit.Label ?? "",
            };
            return true;
        }

        /// <summary>指定できる難易度の一覧。Standard があれば Standard のものだけを並べる。</summary>
        public static string DescribeAvailable(MapInfo map)
        {
            var entries = map?.Difficulties;
            if (entries == null || entries.Count == 0) {
                return "";
            }
            var target = entries.Where(e => string.Equals(e.Characteristic, StandardCharacteristic, StringComparison.OrdinalIgnoreCase)).ToList();
            if (target.Count == 0) {
                target = entries;
            }
            var sb = new StringBuilder();
            foreach (var e in target.OrderBy(e => Array.IndexOf(s_difficultyOrder, e.Difficulty))) {
                if (sb.Length > 0) {
                    sb.Append(" / ");
                }
                sb.Append(ToDisplayName(e.Difficulty));
                if (!string.IsNullOrEmpty(e.Label)) {
                    sb.Append('(').Append(e.Label).Append(')');
                }
            }
            return sb.ToString();
        }

        /// <summary>BeatSaver の表記をゲームの表記にする（ExpertPlus → Expert+）。</summary>
        public static string ToDisplayName(string difficulty) =>
            string.Equals(difficulty, "ExpertPlus", StringComparison.OrdinalIgnoreCase) ? "Expert+" : difficulty ?? "";

        private static MapDifficulty FindBest(List<MapDifficulty> entries, Func<MapDifficulty, bool> predicate) =>
            entries.FirstOrDefault(e => predicate(e) && string.Equals(e.Characteristic, StandardCharacteristic, StringComparison.OrdinalIgnoreCase))
            ?? entries.FirstOrDefault(predicate);
    }
}
