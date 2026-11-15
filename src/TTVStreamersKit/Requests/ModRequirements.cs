using System;
using System.Collections.Generic;
using System.Linq;

namespace TTVStreamersKit.Requests
{
    /// <summary>
    /// 譜面に必須の MOD が手元にあるか。手元の MOD は SongCore に登録された名前（Collections.capabilities）で判定する。
    /// MOD を無効にしていると登録されないため、遊べないものとして扱う（SongCore がプレイを止める条件と同じ）。
    /// </summary>
    internal static class ModRequirements
    {
        /// <summary>手元の MOD で遊べる難易度が1つでもあるか。難易度の情報がなければ判定しない（true）。</summary>
        public static bool HasPlayableDifficulty(MapInfo map)
        {
            if (map?.Difficulties == null || map.Difficulties.Count == 0) {
                return true;
            }
            var installed = Installed();
            return map.Difficulties.Any(d => MissingOf(d, installed).Count == 0);
        }

        /// <summary>どの難易度にも足りない MOD をまとめたもの（拒否の返信に使う）。</summary>
        public static List<string> MissingForAll(MapInfo map)
        {
            var installed = Installed();
            return (map?.Difficulties ?? new List<MapDifficulty>())
                .SelectMany(d => MissingOf(d, installed))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>指定された難易度に足りない MOD。難易度が見つからなければ空。</summary>
        public static List<string> Missing(MapInfo map, DifficultyResolver.ResolvedDifficulty wanted)
        {
            var diff = map?.Difficulties?.FirstOrDefault(d =>
                string.Equals(d.Difficulty, wanted?.Difficulty, StringComparison.OrdinalIgnoreCase)
                && string.Equals(string.IsNullOrEmpty(d.Characteristic) ? DifficultyResolver.StandardCharacteristic : d.Characteristic,
                                 wanted?.Characteristic, StringComparison.OrdinalIgnoreCase));
            return diff == null ? new List<string>() : MissingOf(diff, Installed());
        }

        private static List<string> MissingOf(MapDifficulty diff, HashSet<string> installed) =>
            (diff.RequiredMods ?? new List<string>()).Where(m => !installed.Contains(m)).ToList();

        private static HashSet<string> Installed() =>
            new HashSet<string>(SongCore.Collections.capabilities, StringComparer.OrdinalIgnoreCase);
    }
}
