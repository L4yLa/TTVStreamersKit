using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace TTVStreamersKit.Utilities
{
    /// <summary>DLL に埋め込んだ AssetBundle（チャット表示のフォント、ChatEmoteParticles のシェーダー）。</summary>
    internal static class ModAssets
    {
        private const string BundleResource = "TTVStreamersKit.Resources.ttvstreamerskit.bundle";

        private static bool s_loaded;

        /// <summary>BIZ UDPGothic の静的フォントアセット。マテリアルは含まない。無ければ null。</summary>
        public static TMP_FontAsset ChatFont { get; private set; }

        /// <summary>ChatEmoteParticles のマテリアル。無ければ null。</summary>
        public static Material EmoteParticleMaterial { get; private set; }

        /// <summary>初めて呼ばれたときに読み込む。メインスレッドで呼ぶ。</summary>
        public static void Load()
        {
            if (s_loaded) {
                return;
            }
            s_loaded = true;
            try {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BundleResource)) {
                    if (stream == null) {
                        Plugin.Log.Warn("Assets: the AssetBundle is not embedded in this build.");
                        return;
                    }
                    var bundle = AssetBundle.LoadFromStream(stream);
                    if (bundle == null) {
                        Plugin.Log.Error("Assets: failed to load the AssetBundle.");
                        return;
                    }
                    try {
                        ChatFont = bundle.LoadAllAssets<TMP_FontAsset>().FirstOrDefault();
                        EmoteParticleMaterial = bundle.LoadAllAssets<Material>().FirstOrDefault(m => m.name == "ChatEmoteParticle");
                        // バンドルを解放したあと、使っていないアセットの解放で消されないようにする
                        if (ChatFont != null) {
                            ChatFont.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                        }
                        if (EmoteParticleMaterial != null) {
                            EmoteParticleMaterial.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                        }
                    }
                    finally {
                        // 読み込んだアセットは残し、バンドル自体は解放する
                        bundle.Unload(false);
                    }
                }
                Plugin.Log.Info($"Assets: font {(ChatFont != null ? ChatFont.name : "(none)")}, emote material {(EmoteParticleMaterial != null ? "loaded" : "(none)")}");
            }
            catch (Exception e) {
                Plugin.Log.Error($"Assets: {e}");
            }
        }
    }
}
