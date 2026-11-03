using System.Linq;
using TTVStreamersKit.Utilities;
using UnityEngine;
using UnityEngine.Rendering;

namespace TTVStreamersKit.Emotes
{
    /// <summary>
    /// パーティクルのマテリアル。同梱のシェーダー（TTVStreamersKit の AssetBundle の ChatEmoteParticle）を使う。
    /// ゲームのシェーダーは UI 用で白飛びするなどの問題があるため、同梱のものが読めないときだけ代わりに使う。
    /// </summary>
    internal static class EmoteMaterial
    {
        private const int TransparentQueue = 3000;

        private static readonly string[] FallbackShaders = {
            "Custom/CustomParticles",
            "UI/Default",
            "Sprites/Default",
            "Legacy Shaders/Particles/Alpha Blended",
        };

        private static Material s_template;
        private static bool s_resolved;

        /// <summary>使えるシェーダーがない。</summary>
        internal static bool Failed { get; private set; }

        /// <summary>エモート1種類分のマテリアルを作る。作れなければ null。</summary>
        internal static Material CreateInstance()
        {
            var template = GetTemplate();
            // 使っていないアセットの解放（Resources.UnloadUnusedAssets）で消されないようにする
            return template != null
                ? new Material(template) { name = "TTVStreamersKit Emote (instance)", hideFlags = HideFlags.DontUnloadUnusedAsset }
                : null;
        }

        internal static void ApplyTexture(Material material, Texture texture)
        {
            if (material == null) {
                return;
            }
            material.mainTexture = texture;
            if (material.HasProperty("_MainTex")) {
                material.SetTexture("_MainTex", texture);
            }
        }

        private static Material GetTemplate()
        {
            // 破棄されたテンプレートは Unity の == で null になる。そのときは作り直す
            if (s_resolved && (s_template != null || Failed)) {
                return s_template;
            }
            if (s_resolved) {
                Plugin.Log.Warn("Emotes: the template material was destroyed. It is created again.");
            }
            s_resolved = true;
            ModAssets.Load();
            if (ModAssets.EmoteParticleMaterial != null) {
                s_template = new Material(ModAssets.EmoteParticleMaterial) {
                    renderQueue = TransparentQueue,
                    hideFlags = HideFlags.DontUnloadUnusedAsset,
                };
                return s_template;
            }

            Plugin.Log.Error("Emotes: the bundled shader could not be loaded. A game shader is used instead, and emotes may look washed out.");
            var shaders = Resources.FindObjectsOfTypeAll<Shader>();
            foreach (var name in FallbackShaders) {
                var shader = shaders.FirstOrDefault(s => s != null && s.name == name) ?? Shader.Find(name);
                if (shader != null) {
                    s_template = new Material(shader) {
                        name = "TTVStreamersKit Emote (fallback)",
                        hideFlags = HideFlags.DontUnloadUnusedAsset,
                    };
                    ConfigureFallback(s_template);
                    return s_template;
                }
            }
            Failed = true;
            Plugin.Log.Error("Emotes: no usable shader was found. Emotes are not shown.");
            return null;
        }

        /// <summary>代わりのシェーダー向けの最低限の設定。</summary>
        private static void ConfigureFallback(Material m)
        {
            m.renderQueue = TransparentQueue;
            SetIf(m, "_ZWrite", 0f);
            SetIf(m, "_Cull", (float)CullMode.Off);
            SetIf(m, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetIf(m, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            SetIf(m, "_BlendSrcFactor", (float)BlendMode.One);
            SetIf(m, "_BlendDstFactor", (float)BlendMode.OneMinusSrcAlpha);
            SetIf(m, "_BlendSrcFactorA", (float)BlendMode.Zero);
            SetIf(m, "_BlendDstFactorA", (float)BlendMode.OneMinusSrcAlpha);
            SetIf(m, "_BloomMultiplier", 0f);
            if (m.HasProperty("_Color")) {
                m.SetColor("_Color", Color.white);
            }
            if (m.HasProperty("_TintColor")) {
                m.SetColor("_TintColor", Color.white);
            }
        }

        private static void SetIf(Material m, string name, float value)
        {
            if (m.HasProperty(name)) {
                m.SetFloat(name, value);
            }
        }
    }
}
