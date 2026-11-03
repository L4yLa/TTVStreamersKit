using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TTVStreamersKit.Requests
{
    /// <summary>
    /// 曲の並べ替えの順。空白で区切った項目を左から順に比べる。
    /// 項目の前の - は大きい順、+（省略可）は小さい順。例: "-rating +id"
    /// 使える項目: rating、id（key）、stars、length、njs、uploaded、upvotes、downvotes。
    /// </summary>
    internal static class SortOrder
    {
        public const string Default = "-rating +id";

        private static readonly Dictionary<string, Func<MapInfo, IComparable>> s_fields =
            new Dictionary<string, Func<MapInfo, IComparable>>(StringComparer.OrdinalIgnoreCase) {
                ["rating"] = m => m.Rating,
                ["id"] = m => KeyValue(m.Key),
                ["key"] = m => KeyValue(m.Key),
                ["stars"] = m => m.Difficulties.Select(d => d.Stars ?? 0f).DefaultIfEmpty(0f).Max(),
                ["length"] = m => m.Duration,
                ["njs"] = m => m.Difficulties.Select(d => d.Njs).DefaultIfEmpty(0f).Max(),
                ["uploaded"] = m => m.Uploaded ?? DateTimeOffset.MinValue,
                ["upvotes"] = m => m.Upvotes,
                ["downvotes"] = m => m.Downvotes,
            };

        private static readonly HashSet<string> s_warned = new HashSet<string>(StringComparer.Ordinal);

        public static IEnumerable<MapInfo> Sort(IEnumerable<MapInfo> maps, string order)
        {
            var keys = Parse(order);
            if (keys.Count == 0) {
                keys = Parse(Default);
            }
            IOrderedEnumerable<MapInfo> sorted = null;
            foreach (var (field, descending) in keys) {
                if (sorted == null) {
                    sorted = descending ? maps.OrderByDescending(field) : maps.OrderBy(field);
                }
                else {
                    sorted = descending ? sorted.ThenByDescending(field) : sorted.ThenBy(field);
                }
            }
            return sorted;
        }

        private static List<(Func<MapInfo, IComparable> Field, bool Descending)> Parse(string order)
        {
            var result = new List<(Func<MapInfo, IComparable>, bool)>();
            foreach (var token in (order ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)) {
                var descending = token[0] == '-';
                var name = token[0] == '-' || token[0] == '+' ? token.Substring(1) : token;
                if (s_fields.TryGetValue(name, out var field)) {
                    result.Add((field, descending));
                }
                else if (s_warned.Add(token)) {
                    Plugin.Log.Warn($"Requests: unknown sort field '{token}' in \"{order}\" is ignored.");
                }
            }
            return result;
        }

        private static long KeyValue(string key) =>
            long.TryParse(key, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : long.MaxValue;
    }
}
