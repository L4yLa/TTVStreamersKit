using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;

namespace TTVStreamersKit.Requests
{
    /// <summary>
    /// 曲が始まる直前を捕まえる（MenuTransitionsHelper の StartStandardLevel / StartMultiplayerLevel / StartMissionLevel）。
    /// ソロはプレイボタン（360 度の確認の後）から、マルチプレイはカウントダウンの終了から同じ呼び出しの中で呼ばれ、
    /// プレイする曲が決まった最初の時点になる。暗転、譜面データとシーンの読み込みはこの後に行われる。
    /// 引数の並びは版や重ね定義で違うが、どれも beatmapLevel を持つため、引数の名前で受け取る。
    /// </summary>
    internal static class LevelStartPatch
    {
        private const string HarmonyId = "com.l4yla.ttvstreamerskit";
        private static readonly string[] s_targets = { "StartStandardLevel", "StartMultiplayerLevel", "StartMissionLevel" };
        private static Harmony s_harmony;

        /// <summary>曲が始まる。重ね定義が内部で呼び合う場合、同じ曲で続けて届くことがある。</summary>
        public static event Action<BeatmapLevel> LevelStarting;

        public static void Apply()
        {
            if (s_harmony != null) {
                return;
            }
            s_harmony = new Harmony(HarmonyId);
            var prefix = new HarmonyMethod(typeof(LevelStartPatch).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic));
            var methods = typeof(MenuTransitionsHelper).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => Array.IndexOf(s_targets, m.Name) >= 0
                            && m.GetParameters().Any(p => p.Name == "beatmapLevel" && p.ParameterType == typeof(BeatmapLevel)))
                .ToList();
            var patched = 0;
            foreach (var method in methods) {
                try {
                    s_harmony.Patch(method, prefix: prefix);
                    patched++;
                }
                catch (Exception e) {
                    Plugin.Log.Error($"Requests: failed to patch MenuTransitionsHelper.{method.Name}: {e.Message}");
                }
            }
            Plugin.Log.Debug($"Requests: patched {patched} level start methods");
        }

        private static void Prefix(BeatmapLevel beatmapLevel)
        {
            try {
                LevelStarting?.Invoke(beatmapLevel);
            }
            catch (Exception e) {
                // 曲の開始を止めないよう、例外はここで止める
                Plugin.Log.Error($"Requests: {e}");
            }
        }
    }
}
