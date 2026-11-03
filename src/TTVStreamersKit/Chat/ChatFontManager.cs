using BeatSaberMarkupLanguage;
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using TTVStreamersKit.Utilities;
using UnityEngine;
using Zenject;

namespace TTVStreamersKit.Chat
{
    /// <summary>
    /// チャット表示のフォント。同梱の静的フォントアセットを主に使い、収録していない文字は OS のフォントから動的に補う。
    /// </summary>
    internal sealed class ChatFontManager : ITickable, IDisposable
    {
        // 静的アセットにない文字を補う OS のフォント（見つかったものを順に使う）
        private static readonly string[] FallbackFontFiles = { "meiryo", "seguisym", "msgothic" };

        private readonly List<TMP_FontAsset> _created = new List<TMP_FontAsset>();
        private readonly List<Material> _materials = new List<Material>();

        public bool IsInitialized { get; private set; }

        /// <summary>チャットのテキストに使うフォント。初期化前は null。</summary>
        public TMP_FontAsset MainFont { get; private set; }

        public event Action Initialized;

        public void Tick()
        {
            if (this.IsInitialized) {
                return;
            }
            // ゲームの TMP のシェーダー（グローを書かないもの）が使えるまで待つ
            var gameFont = BeatSaberUI.MainTextFont;
            var shader = gameFont != null && gameFont.material != null ? gameFont.material.shader : null;
            if (shader == null) {
                return;
            }
            this.IsInitialized = true;
            try {
                this.Setup(shader);
            }
            catch (Exception e) {
                Plugin.Log.Error($"Font: {e}");
            }
            try {
                this.Initialized?.Invoke();
            }
            catch (Exception e) {
                Plugin.Log.Error($"Font: {e}");
            }
        }

        private void Setup(Shader shader)
        {
            ModAssets.Load();
            var fallbacks = new List<TMP_FontAsset>();
            foreach (var name in FallbackFontFiles) {
                var asset = this.CreateOsFont(name, shader);
                if (asset != null) {
                    fallbacks.Add(asset);
                }
            }

            var main = ModAssets.ChatFont;
            if (main != null) {
                this.PrepareStaticFont(main, shader);
                main.fallbackFontAssetTable = fallbacks;
                this.MainFont = main;
            }
            else if (fallbacks.Count > 0) {
                Plugin.Log.Warn("Font: the bundled font is not available. A system font is used instead.");
                this.MainFont = fallbacks[0];
                fallbacks.RemoveAt(0);
                this.MainFont.fallbackFontAssetTable = fallbacks;
            }
            else {
                Plugin.Log.Error("Font: no font is available. The game font is used.");
                this.MainFont = BeatSaberUI.MainTextFont;
            }
            Plugin.Log.Info($"Font: main {this.MainFont?.name}, {fallbacks.Count} fallback(s)");
        }

        /// <summary>同梱のフォントアセットにゲームのシェーダーでマテリアルを付ける。</summary>
        private void PrepareStaticFont(TMP_FontAsset font, Shader shader)
        {
            var atlas = font.atlasTextures != null && font.atlasTextures.Length > 0 ? font.atlasTextures[0] : null;
            if (atlas == null) {
                throw new InvalidOperationException("the bundled font has no atlas texture");
            }
            var material = new Material(shader) { name = font.name + " Material" };
            material.SetTexture(Shader.PropertyToID("_MainTex"), atlas);
            material.SetFloat(Shader.PropertyToID("_TextureWidth"), atlas.width);
            material.SetFloat(Shader.PropertyToID("_TextureHeight"), atlas.height);
            material.SetFloat(Shader.PropertyToID("_GradientScale"), font.atlasPadding + 1);
            material.SetFloat(Shader.PropertyToID("_WeightNormal"), font.normalStyle);
            material.SetFloat(Shader.PropertyToID("_WeightBold"), font.boldStyle);
            font.material = material;
            _materials.Add(material);

            // 実行時に文字を追加しないため、CPU 側の画素は要らない
            foreach (var texture in font.atlasTextures) {
                if (texture != null && texture.isReadable) {
                    texture.Apply(false, true);
                }
            }
        }

        private TMP_FontAsset CreateOsFont(string fileName, Shader shader)
        {
            try {
                string path = null;
                foreach (var p in Font.GetPathsToOSFonts()) {
                    if (string.Equals(Path.GetFileNameWithoutExtension(p), fileName, StringComparison.OrdinalIgnoreCase)) {
                        path = p;
                        break;
                    }
                }
                if (path == null) {
                    return null;
                }
                var font = new Font(path) { name = fileName };
                var asset = TMP_FontAsset.CreateFontAsset(font);
                if (asset == null) {
                    return null;
                }
                asset.name = fileName + " (TTVStreamersKit)";
                if (asset.material != null) {
                    asset.material.shader = shader;
                }
                _created.Add(asset);
                return asset;
            }
            catch (Exception e) {
                Plugin.Log.Warn($"Font: could not use the system font {fileName}: {e.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            foreach (var asset in _created) {
                if (asset != null) {
                    UnityEngine.Object.Destroy(asset);
                }
            }
            _created.Clear();
            foreach (var m in _materials) {
                if (m != null) {
                    UnityEngine.Object.Destroy(m);
                }
            }
            _materials.Clear();
        }
    }
}
