using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace TTVStreamersKit.Requests
{
    /// <summary>キューの画面に出すカバー画像。メインスレッドから使う。</summary>
    internal static class CoverCache
    {
        private const int Limit = 200;

        private static readonly Dictionary<string, Sprite> s_sprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<string> s_order = new LinkedList<string>();
        private static readonly HashSet<string> s_loading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static event Action<string> Loaded;

        /// <summary>読み込み済みならその画像、まだなら null を返して読み込みを始める。</summary>
        public static Sprite Get(MapInfo map)
        {
            if (map == null || string.IsNullOrEmpty(map.CoverUrl)) {
                return null;
            }
            var key = map.Hash;
            if (s_sprites.TryGetValue(key, out var sprite) && sprite != null) {
                return sprite;
            }
            if (s_loading.Add(key)) {
                _ = LoadAsync(key, map.CoverUrl);
            }
            return null;
        }

        private static async Task LoadAsync(string key, string url)
        {
            try {
                var bytes = await BeatSaverClient.Http.GetByteArrayAsync(url);
                var texture = new Texture2D(2, 2);
                if (!texture.LoadImage(bytes, true)) {
                    UnityEngine.Object.Destroy(texture);
                    return;
                }
                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                s_sprites[key] = sprite;
                s_order.AddLast(key);
                while (s_order.Count > Limit) {
                    var old = s_order.First.Value;
                    s_order.RemoveFirst();
                    if (s_sprites.TryGetValue(old, out var s) && s != null) {
                        UnityEngine.Object.Destroy(s.texture);
                        UnityEngine.Object.Destroy(s);
                    }
                    s_sprites.Remove(old);
                }
                Loaded?.Invoke(key);
            }
            catch (Exception e) {
                Plugin.Log.Debug($"Requests: cover {url}: {e.Message}");
            }
            finally {
                s_loading.Remove(key);
            }
        }
    }
}
