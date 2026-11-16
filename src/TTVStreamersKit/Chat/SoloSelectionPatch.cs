using HarmonyLib;
using System;
using System.Reflection;

namespace TTVStreamersKit.Chat
{
    /// <summary>
    /// ソロの曲選択の画面が有効になったときを捕まえる（SoloFreePlayFlowCoordinator.SinglePlayerLevelSelectionFlowCoordinatorDidActivate）。
    /// メインメニューからソロに入ったときと、ソロのプレイから戻ったときに呼ばれる。1.40.8 と 1.44.1 で同じ名前。
    /// </summary>
    internal static class SoloSelectionPatch
    {
        private const string HarmonyId = "com.l4yla.ttvstreamerskit.chat";
        private const string TargetName = "SinglePlayerLevelSelectionFlowCoordinatorDidActivate";
        private static Harmony s_harmony;

        public static event Action Activated;

        public static void Apply()
        {
            if (s_harmony != null) {
                return;
            }
            s_harmony = new Harmony(HarmonyId);
            var method = typeof(SoloFreePlayFlowCoordinator).GetMethod(TargetName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (method == null) {
                Plugin.Log.Error($"Chat: SoloFreePlayFlowCoordinator.{TargetName} was not found");
                return;
            }
            try {
                s_harmony.Patch(method, postfix: new HarmonyMethod(typeof(SoloSelectionPatch).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception e) {
                Plugin.Log.Error($"Chat: failed to patch SoloFreePlayFlowCoordinator.{TargetName}: {e.Message}");
            }
        }

        private static void Postfix()
        {
            try {
                Activated?.Invoke();
            }
            catch (Exception e) {
                // 画面の切り替えを止めないよう、例外はここで止める
                Plugin.Log.Error($"Chat: {e}");
            }
        }
    }
}
