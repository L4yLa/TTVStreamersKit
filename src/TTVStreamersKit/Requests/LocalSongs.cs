using IPA.Utilities;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TTVStreamersKit.Requests
{
    /// <summary>
    /// 曲のフォルダーにすでに同じ曲（同じハッシュ）があるかを調べる。SongCore が読み込めなかった曲も対象にする。
    /// ハッシュは SongCore と同じ方法で計算する（Info.dat の文字列に、譜面のファイルの中身を続けた SHA-1）。
    /// </summary>
    internal static class LocalSongs
    {
        /// <summary>同じ曲のフォルダーを返す。なければ null。別スレッドで呼んでよい。</summary>
        public static string FindFolder(MapInfo map)
        {
            if (map == null || string.IsNullOrEmpty(map.Key) || string.IsNullOrEmpty(map.Hash)) {
                return null;
            }
            // 調べるのは「キー (」で始まるフォルダーだけにする（全部の曲を計算しない）
            var prefix = map.Key + " (";
            foreach (var root in Roots()) {
                if (!Directory.Exists(root)) {
                    continue;
                }
                foreach (var dir in Directory.EnumerateDirectories(root)) {
                    if (!Path.GetFileName(dir).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }
                    try {
                        if (string.Equals(ComputeHash(dir), map.Hash, StringComparison.OrdinalIgnoreCase)) {
                            return dir;
                        }
                    }
                    catch (Exception e) {
                        Plugin.Log.Debug($"Requests: could not hash {dir}: {e.Message}");
                    }
                }
            }
            return null;
        }

        private static IEnumerable<string> Roots()
        {
            var data = Path.Combine(UnityGame.InstallPath, "Beat Saber_Data");
            yield return Path.Combine(data, "CustomLevels");
            yield return Path.Combine(data, "CustomWIPLevels");
        }

        /// <summary>フォルダーの曲のハッシュ（大文字の16進数）。Info.dat がなければ null。</summary>
        private static string ComputeHash(string folder)
        {
            var infoPath = Directory.EnumerateFiles(folder)
                .FirstOrDefault(f => string.Equals(Path.GetFileName(f), "Info.dat", StringComparison.OrdinalIgnoreCase));
            if (infoPath == null) {
                return null;
            }
            // SongCore は Info.dat を文字列として読み、UTF-8 に直したものを使う（BOM は含まれない）
            var infoText = File.ReadAllText(infoPath);
            var info = JObject.Parse(infoText);
            var files = new List<string>();
            if (info["_difficultyBeatmapSets"] is JArray sets) {
                // 2.x / 3.x の形式
                foreach (var set in sets.OfType<JObject>()) {
                    if (set["_difficultyBeatmaps"] is JArray diffs) {
                        files.AddRange(diffs.OfType<JObject>().Select(d => (string)d["_beatmapFilename"]));
                    }
                }
            }
            else if (info["difficultyBeatmaps"] is JArray diffs4) {
                // 4.x の形式。音のデータのファイルが先頭に入る
                files.Add((string)info["audio"]?["audioDataFilename"]);
                foreach (var d in diffs4.OfType<JObject>()) {
                    files.Add((string)d["beatmapDataFilename"]);
                    files.Add((string)d["lightshowDataFilename"]);
                }
            }
            using (var sha1 = SHA1.Create()) {
                var head = Encoding.UTF8.GetBytes(infoText);
                sha1.TransformBlock(head, 0, head.Length, null, 0);
                var buffer = new byte[81920];
                foreach (var name in files) {
                    if (string.IsNullOrEmpty(name)) {
                        continue;
                    }
                    var path = Path.Combine(folder, name);
                    if (!File.Exists(path)) {
                        continue;
                    }
                    using (var stream = File.OpenRead(path)) {
                        int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) {
                            sha1.TransformBlock(buffer, 0, read, null, 0);
                        }
                    }
                }
                sha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return BitConverter.ToString(sha1.Hash).Replace("-", "");
            }
        }
    }
}
