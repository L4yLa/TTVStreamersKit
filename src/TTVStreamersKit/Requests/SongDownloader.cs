using IPA.Utilities;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TTVStreamersKit.Requests
{
    /// <summary>BeatSaver の曲の zip をダウンロードして展開する。同時に1件まで。</summary>
    internal static class SongDownloader
    {
        private static bool s_busy;

        public static bool IsBusy => s_busy;

        /// <summary>展開したフォルダーを返す。失敗したら null。<paramref name="progress"/> には 0〜1 の進み具合を渡す（大きさが分からなければ渡さない）。</summary>
        public static async Task<string> DownloadAsync(MapInfo map, Action<float> progress = null)
        {
            if (s_busy || map == null || (string.IsNullOrEmpty(map.DownloadUrl) && string.IsNullOrEmpty(map.Hash))) {
                return null;
            }
            s_busy = true;
            try {
                // 通信はメインスレッドに戻らずに進める（戻ると1フレームに1回しか読めず、遅くなる）。
                // 進み具合だけをメインスレッドで知らせる
                var context = SynchronizationContext.Current;
                Action<float> report = null;
                if (progress != null) {
                    report = context != null ? p => context.Post(_ => progress(p), null) : progress;
                }
                var bytes = await DownloadWithFallbackAsync(map, report);
                if (bytes == null) {
                    return null;
                }
                var root = Path.Combine(UnityGame.InstallPath, "Beat Saber_Data", map.IsWip ? "CustomWIPLevels" : "CustomLevels");
                var folder = UniqueFolder(root, FolderName(map));
                await Task.Run(() => Extract(bytes, folder));
                return folder;
            }
            catch (Exception e) {
                Plugin.Log.Error($"Requests: failed to download {map.Key}: {e.Message}");
                return null;
            }
            finally {
                s_busy = false;
            }
        }

        private const string CdnRoot = "https://cdn.beatsaver.com";
        private const string R2CdnRoot = "https://r2cdn.beatsaver.com";

        // 地域別のミラー。cdn.beatsaver.com に置き換える
        private static readonly string[] RegionalCdnRoots = { "https://as.cdn.beatsaver.com", "https://na.cdn.beatsaver.com" };

        /// <summary>
        /// BeatSaver が返した downloadURL から取得する。地域別のミラー（as. / na.）は cdn.beatsaver.com に置き換える。
        /// r2cdn.beatsaver.com は別のバックエンドのため、置き換えずにそのまま使う。
        /// 取れなければ、ハッシュから作った cdn.beatsaver.com と r2cdn.beatsaver.com の URL で取り直す。
        /// </summary>
        private static async Task<byte[]> DownloadWithFallbackAsync(MapInfo map, Action<float> progress)
        {
            var hash = (map.Hash ?? "").ToLowerInvariant();
            var urls = new[] {
                NormalizeRegionalCdn(map.DownloadUrl),
                string.IsNullOrEmpty(hash) ? null : $"{CdnRoot}/{hash}.zip",
                string.IsNullOrEmpty(hash) ? null : $"{R2CdnRoot}/{hash}.zip",
            }.Where(u => !string.IsNullOrEmpty(u)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var url in urls) {
                var bytes = await DownloadOneAsync(url, progress).ConfigureAwait(false);
                if (bytes != null) {
                    return bytes;
                }
            }
            Plugin.Log.Error($"Requests: failed to download {map.Key}. Tried: {string.Join(", ", urls)}");
            return null;
        }

        // 見つからない（404）とき以外の失敗は、1秒おいて1度だけ取り直す
        private const int AttemptsPerUrl = 2;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

        // 進み具合は 5% 進むごとに知らせる（画面の更新を減らす）
        private const float ProgressStep = 0.05f;

        private static async Task<byte[]> DownloadOneAsync(string url, Action<float> progress)
        {
            for (var attempt = 1; attempt <= AttemptsPerUrl; attempt++) {
                if (attempt > 1) {
                    await Task.Delay(RetryDelay).ConfigureAwait(false);
                }
                try {
                    using (var response = await BeatSaverClient.Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false)) {
                        if (response.StatusCode == HttpStatusCode.NotFound) {
                            Plugin.Log.Warn($"Requests: 404 (Not Found) for {url}");
                            return null;
                        }
                        if (!response.IsSuccessStatusCode) {
                            Plugin.Log.Warn($"Requests: {(int)response.StatusCode} ({response.ReasonPhrase}) for {url} (attempt {attempt}/{AttemptsPerUrl})");
                            continue;
                        }
                        var total = response.Content.Headers.ContentLength ?? -1L;
                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var memory = new MemoryStream(total > 0 ? (int)total : 1 << 20)) {
                            var buffer = new byte[81920];
                            var reported = 0f;
                            progress?.Invoke(0f);
                            int read;
                            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0) {
                                memory.Write(buffer, 0, read);
                                if (total > 0) {
                                    var current = Math.Min(1f, (float)memory.Length / total);
                                    if (current - reported >= ProgressStep) {
                                        reported = current;
                                        progress?.Invoke(current);
                                    }
                                }
                            }
                            return memory.ToArray();
                        }
                    }
                }
                catch (Exception e) {
                    // 時間切れ（TaskCanceledException）も通信の失敗として扱う
                    Plugin.Log.Warn($"Requests: {e.GetType().Name} ({e.Message}) for {url} (attempt {attempt}/{AttemptsPerUrl})");
                }
            }
            return null;
        }

        private static string NormalizeRegionalCdn(string url)
        {
            if (string.IsNullOrEmpty(url)) {
                return url;
            }
            foreach (var root in RegionalCdnRoots) {
                if (url.StartsWith(root, StringComparison.OrdinalIgnoreCase)) {
                    return CdnRoot + url.Substring(root.Length);
                }
            }
            return url;
        }

        /// <summary>「キー (曲名 - 譜面作者)」から、フォルダー名に使えない文字を除いたもの。</summary>
        private static string FolderName(MapInfo map)
        {
            var name = $"{map.Key} ({map.SongName} - {map.LevelAuthorName})";
            var invalid = Path.GetInvalidFileNameChars();
            name = new string(name.Where(c => !invalid.Contains(c)).ToArray());
            name = Regex.Replace(name, @"\s+", " ").Trim().TrimEnd('.');
            return name.Length > 0 ? name : map.Key;
        }

        private static string UniqueFolder(string root, string name)
        {
            var path = Path.Combine(root, name);
            var n = 1;
            while (Directory.Exists(path)) {
                path = Path.Combine(root, $"{name} ({n++})");
            }
            return path;
        }

        private static void Extract(byte[] zip, string folder)
        {
            var full = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(folder);
            try {
                using (var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read)) {
                    foreach (var entry in archive.Entries) {
                        if (string.IsNullOrEmpty(entry.Name)) {
                            continue;
                        }
                        var target = Path.GetFullPath(Path.Combine(folder, entry.FullName));
                        // zip の中のパスでフォルダーの外へ書き出さない
                        if (!target.StartsWith(full, StringComparison.OrdinalIgnoreCase)) {
                            continue;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (var input = entry.Open())
                        using (var output = File.Create(target)) {
                            input.CopyTo(output);
                        }
                    }
                }
            }
            catch {
                try {
                    Directory.Delete(folder, true);
                }
                catch (Exception) {
                }
                throw;
            }
        }
    }
}
