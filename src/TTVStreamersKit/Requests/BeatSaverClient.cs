using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TTVStreamersKit.Requests
{
    public enum BeatSaverStatus
    {
        Ok,
        NotFound,
        Unavailable,
    }

    /// <summary>BeatSaver の API（https://api.beatsaver.com）。</summary>
    internal static class BeatSaverClient
    {
        private const string ApiBase = "https://api.beatsaver.com";

        private static readonly HttpClient s_http = CreateClient();

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TTVStreamersKit/" + typeof(BeatSaverClient).Assembly.GetName().Version);
            return http;
        }

        public static HttpClient Http => s_http;

        /// <summary>キーで譜面を取得する。</summary>
        public static async Task<(BeatSaverStatus Status, MapInfo Map)> GetByKeyAsync(string key, CancellationToken token = default(CancellationToken))
        {
            try {
                using (var response = await s_http.GetAsync($"{ApiBase}/maps/id/{Uri.EscapeDataString(key)}", token).ConfigureAwait(false)) {
                    if (response.StatusCode == HttpStatusCode.NotFound) {
                        return (BeatSaverStatus.NotFound, null);
                    }
                    if (!response.IsSuccessStatusCode) {
                        Plugin.Log.Warn($"BeatSaver: {(int)response.StatusCode} for key {key}");
                        return (BeatSaverStatus.Unavailable, null);
                    }
                    var json = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                    var map = MapInfo.FromJson(json);
                    return map == null ? (BeatSaverStatus.NotFound, (MapInfo)null) : (BeatSaverStatus.Ok, map);
                }
            }
            catch (Exception e) when (!(e is OperationCanceledException) || !token.IsCancellationRequested) {
                Plugin.Log.Warn($"BeatSaver: {e.Message}");
                return (BeatSaverStatus.Unavailable, null);
            }
        }

        /// <summary>文字列で検索する（最初のページ）。<paramref name="sortOrder"/> は Latest / Relevance など。</summary>
        public static async Task<(BeatSaverStatus Status, List<MapInfo> Maps)> SearchAsync(string query, string sortOrder, CancellationToken token = default(CancellationToken))
        {
            try {
                var url = $"{ApiBase}/search/text/0?sortOrder={Uri.EscapeDataString(sortOrder)}&q={Uri.EscapeDataString(query)}";
                using (var response = await s_http.GetAsync(url, token).ConfigureAwait(false)) {
                    if (!response.IsSuccessStatusCode) {
                        Plugin.Log.Warn($"BeatSaver: {(int)response.StatusCode} for search");
                        return (BeatSaverStatus.Unavailable, null);
                    }
                    var json = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                    var maps = (json["docs"] as JArray)?.OfType<JObject>().Select(MapInfo.FromJson).Where(m => m != null).ToList()
                               ?? new List<MapInfo>();
                    return (BeatSaverStatus.Ok, maps);
                }
            }
            catch (Exception e) when (!(e is OperationCanceledException) || !token.IsCancellationRequested) {
                Plugin.Log.Warn($"BeatSaver: {e.Message}");
                return (BeatSaverStatus.Unavailable, null);
            }
        }
    }
}
