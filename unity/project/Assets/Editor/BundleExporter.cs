using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace TTVStreamersKit.EditorTools
{
    /// <summary>
    /// TTVStreamersKit が使うアセット（ChatEmoteParticles のシェーダーとマテリアル、チャット表示の静的フォントアセット）を
    /// AssetBundle へ書き出す。
    ///
    /// <para>GUI からはメニュー <c>TTVStreamersKit > Build AssetBundle</c>。バッチ実行は次のとおり。</para>
    ///
    /// <code>
    /// Unity.exe -batchmode -quit -nographics
    ///           -projectPath &lt;unity/project&gt;
    ///           -executeMethod TTVStreamersKit.EditorTools.BundleExporter.Build
    ///           -logFile &lt;log&gt;
    /// </code>
    ///
    /// <para>AssetBundle は Unity のメジャーバージョンをまたいで読めないため、実行中の Unity の版から
    /// 対象のゲームバージョンを決め、MOD の Resources の下の版ごとのフォルダーへ出力する。</para>
    ///
    /// <para>フォントアセットにはマテリアルを含めない。TMP のシェーダーを同梱しないためで、
    /// マテリアルは MOD がゲームのシェーダーから作る。</para>
    ///
    /// <para>このファイルは unity/project が原本。unity/project-6000 へは build-bundle.ps1 がコピーする。</para>
    /// </summary>
    public static class BundleExporter
    {
        private const string BundleName = "ttvstreamerskit.bundle";

        /// <summary>プロジェクト直下からの相対出力先。この下にゲームバージョン別のフォルダーを作る。</summary>
        private const string RelativeResourcesDir = "../../src/TTVStreamersKit/Resources";

        private const string FontSourcePath = "Assets/Fonts/BIZUDPGothic-Regular.ttf";
        private const string CharsetPath = "Assets/Fonts/charset.txt";
        private const string GeneratedDir = "Assets/Generated";
        private const string FontAssetName = "BIZUDPGothic SDF";
        private const string FontAssetPath = GeneratedDir + "/" + FontAssetName + ".asset";

        // 焼き込みの設定。4096×4096 の Alpha8 で1枚約 16MB。枚数はログに出る
        private const int SamplingPointSize = 36;
        private const int AtlasPadding = 4;
        private const int AtlasSize = 4096;

        private static readonly Dictionary<string, string> TargetByUnityVersion = new Dictionary<string, string>
        {
            { "2022.3.33f1", "1.40.8" },
            { "6000.0.40f1", "1.44.1" },
        };

        private static readonly string[] StaticAssets =
        {
            // マテリアルも含めることでシェーダーのバリアントが確実に保持される
            "Assets/Materials/ChatEmoteParticle.mat",
            "Assets/Shaders/ChatEmoteParticle.shader",
        };

        [MenuItem("TTVStreamersKit/Build AssetBundle")]
        public static void Build()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;

            if (!TargetByUnityVersion.TryGetValue(Application.unityVersion, out var gameVersion)) {
                Fail($"unsupported Unity version {Application.unityVersion} (expected one of: {string.Join(", ", TargetByUnityVersion.Keys)})");
                return;
            }
            Debug.Log($"[TTVStreamersKit] Unity {Application.unityVersion} -> game version {gameVersion}");

            foreach (var asset in StaticAssets) {
                if (!File.Exists(Path.Combine(projectRoot, asset))) {
                    Fail($"asset not found: {asset}");
                    return;
                }
            }

            if (!BuildFontAsset(projectRoot)) {
                return;
            }

            var outputDir = Path.GetFullPath(Path.Combine(projectRoot, RelativeResourcesDir, gameVersion));
            Directory.CreateDirectory(outputDir);

            var assetNames = new List<string>(StaticAssets) { FontAssetPath };
            var build = new AssetBundleBuild
            {
                assetBundleName = BundleName,
                assetNames = assetNames.ToArray(),
            };

            var manifest = BuildPipeline.BuildAssetBundles(
                outputDir,
                new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                BuildTarget.StandaloneWindows64);

            if (manifest == null) {
                Fail("BuildPipeline.BuildAssetBundles returned null");
                return;
            }

            var bundlePath = Path.Combine(outputDir, BundleName);
            if (!File.Exists(bundlePath)) {
                Fail($"bundle was not produced at {bundlePath}");
                return;
            }

            // .manifest や出力フォルダー名のバンドルは使わないため消す
            CleanupSideFiles(outputDir);

            var size = new FileInfo(bundlePath).Length;
            Debug.Log($"[TTVStreamersKit] built '{bundlePath}' ({size:N0} bytes)");

            if (Application.isBatchMode) {
                EditorApplication.Exit(0);
            }
        }

        [MenuItem("TTVStreamersKit/Build Font Asset")]
        public static void BuildFontAssetMenu()
        {
            BuildFontAsset(Directory.GetParent(Application.dataPath).FullName);
        }

        private static bool BuildFontAsset(string projectRoot)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontSourcePath);
            if (font == null) {
                Fail($"font not found: {FontSourcePath}");
                return false;
            }

            var charsetFile = Path.Combine(projectRoot, CharsetPath);
            if (!File.Exists(charsetFile)) {
                Fail($"charset not found: {CharsetPath}");
                return false;
            }
            var characters = File.ReadAllText(charsetFile, new UTF8Encoding(false))
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty)
                .Replace("﻿", string.Empty);

            // CreateFontAsset はマテリアルを作るために TMP のシェーダーを使う
            if (Shader.Find("TextMeshPro/Mobile/Distance Field") == null) {
                Fail("TMP shaders not found. Import TMP Essential Resources (Window > TextMeshPro > Import TMP Essential Resources)");
                return false;
            }

            Debug.Log($"[TTVStreamersKit] generating font asset: {characters.Length} characters, {SamplingPointSize}pt, padding {AtlasPadding}, atlas {AtlasSize}");

            var fontAsset = TMP_FontAsset.CreateFontAsset(
                font, SamplingPointSize, AtlasPadding, GlyphRenderMode.SDFAA, AtlasSize, AtlasSize,
                AtlasPopulationMode.Dynamic, true);
            if (fontAsset == null) {
                Fail("TMP_FontAsset.CreateFontAsset returned null");
                return false;
            }

            if (!fontAsset.TryAddCharacters(characters, out var missing) && !string.IsNullOrEmpty(missing)) {
                Debug.LogWarning($"[TTVStreamersKit] {missing.Length} characters were not added: {missing}");
            }

            // 実行時に文字を追加しない。元のフォントファイルも同梱しない
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;
            var sourceField = typeof(TMP_FontAsset).GetField("m_SourceFontFile", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (sourceField != null) {
                sourceField.SetValue(fontAsset, null);
            }
            else {
                Debug.LogWarning("[TTVStreamersKit] TMP_FontAsset.m_SourceFontFile not found. The font file may be included in the bundle");
            }

            var material = fontAsset.material;
            fontAsset.material = null;
            if (material != null) {
                Object.DestroyImmediate(material);
            }

            if (AssetDatabase.LoadAssetAtPath<Object>(FontAssetPath) != null) {
                AssetDatabase.DeleteAsset(FontAssetPath);
            }
            Directory.CreateDirectory(Path.Combine(projectRoot, GeneratedDir));
            AssetDatabase.Refresh();

            fontAsset.name = FontAssetName;
            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);
            var textures = fontAsset.atlasTextures;
            for (var i = 0; i < textures.Length; i++) {
                if (textures[i] == null) {
                    continue;
                }
                textures[i].name = $"{FontAssetName} Atlas {i}";
                AssetDatabase.AddObjectToAsset(textures[i], fontAsset);
            }
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[TTVStreamersKit] font asset saved: {fontAsset.characterTable.Count} characters, {fontAsset.glyphTable.Count} glyphs, {textures.Length} atlas texture(s)");
            return true;
        }

        private static void CleanupSideFiles(string outputDir)
        {
            foreach (var path in Directory.GetFiles(outputDir)) {
                var fileName = Path.GetFileName(path);
                if (fileName == BundleName) {
                    continue;
                }
                try {
                    File.Delete(path);
                }
                catch (System.Exception ex) {
                    Debug.LogWarning($"[TTVStreamersKit] could not delete '{fileName}': {ex.Message}");
                }
            }
        }

        private static void Fail(string reason)
        {
            Debug.LogError($"[TTVStreamersKit] failed: {reason}");
            if (Application.isBatchMode) {
                EditorApplication.Exit(1);
            }
        }
    }
}
