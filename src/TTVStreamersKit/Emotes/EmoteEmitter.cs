using TTVCore.Api.V1;
using TTVStreamersKit.Emotes.Styles;
using UnityEngine;
using UnityEngine.Rendering;

namespace TTVStreamersKit.Emotes
{
    /// <summary>
    /// エモート1種類分の ParticleSystem。<see cref="EmoteEmitterManager"/> がまとめて管理する。
    /// 画像は TTVCore の <see cref="ImageHandle"/> の Sheet から、フレームごとの範囲を Sprite にして使う
    /// （Sheet には他の画像が含まれることがあるため、テクスチャ全体は使わない）。
    /// </summary>
    internal sealed class EmoteEmitter
    {
        internal GameObject Root { get; private set; }
        internal ParticleSystem Ps { get; private set; }
        internal Material Mat { get; private set; }

        /// <summary>割り当てている画像。なければ無効な値。</summary>
        internal ImageKey BoundKey { get; private set; }

        internal bool IsBound => this.BoundKey.IsValid;

        /// <summary>横 / 縦。</summary>
        internal float Aspect { get; private set; } = 1f;

        /// <summary>最後に Emit したフレーム。回収の判定に使う。</summary>
        internal int LastEmitFrame { get; set; } = -10;

        internal int ParticleCount => this.Ps != null ? this.Ps.particleCount : 0;

        internal bool IsAlive => this.Root != null && this.Ps != null;

        private ImageHandle _handle;
        private Sprite[] _sprites;

        internal static EmoteEmitter Create(Transform parent, int layer, int maxParticles)
        {
            var material = EmoteMaterial.CreateInstance();
            if (material == null) {
                return null;
            }
            var go = new GameObject("TTVStreamersKit Emote");
            go.transform.SetParent(parent, false);
            go.layer = layer;
            var ps = go.AddComponent<ParticleSystem>();
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            ConfigureSystem(ps, maxParticles);
            ConfigureRenderer(renderer, material);
            ps.Play();
            return new EmoteEmitter { Root = go, Ps = ps, Mat = material };
        }

        private static void ConfigureSystem(ParticleSystem ps, int maxParticles)
        {
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 3600f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            // ポーズ中も動かし続ける
            main.useUnscaledTime = true;
            main.startSize3D = true;
            // 初速は EmitParams で与える
            main.startSpeed = 0f;
            main.startLifetime = 6f;
            main.startRotation = 0f;
            main.gravityModifier = 0f;
            main.maxParticles = Mathf.Max(1, maxParticles);
            main.cullingMode = ParticleSystemCullingMode.Automatic;
            main.ringBufferMode = ParticleSystemRingBufferMode.Disabled;
            main.startColor = Color.white;

            // 発生は EmitParams だけで行う
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var collision = ps.collision; collision.enabled = false;
            var trails = ps.trails; trails.enabled = false;
            var lights = ps.lights; lights.enabled = false;
            var subEmitters = ps.subEmitters; subEmitters.enabled = false;
            var externalForces = ps.externalForces; externalForces.enabled = false;
            var inheritVelocity = ps.inheritVelocity; inheritVelocity.enabled = false;
            var velocityOverLifetime = ps.velocityOverLifetime; velocityOverLifetime.enabled = false;
            var limitVelocity = ps.limitVelocityOverLifetime; limitVelocity.enabled = false;
            var forceOverLifetime = ps.forceOverLifetime; forceOverLifetime.enabled = false;
            var rotationOverLifetime = ps.rotationOverLifetime; rotationOverLifetime.enabled = false;
            var rotationBySpeed = ps.rotationBySpeed; rotationBySpeed.enabled = false;
            var sizeBySpeed = ps.sizeBySpeed; sizeBySpeed.enabled = false;
            var colorBySpeed = ps.colorBySpeed; colorBySpeed.enabled = false;
            var customData = ps.customData; customData.enabled = false;
            var noise = ps.noise; noise.enabled = false;
            var colorOverLifetime = ps.colorOverLifetime; colorOverLifetime.enabled = false;
            var sizeOverLifetime = ps.sizeOverLifetime; sizeOverLifetime.enabled = false;
            var textureSheet = ps.textureSheetAnimation; textureSheet.enabled = false;
        }

        private static void ConfigureRenderer(ParticleSystemRenderer renderer, Material material)
        {
            // Mesh モードは三人称のカメラで潰れて見えるため Billboard にする
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortMode = ParticleSystemSortMode.None;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.allowRoll = false;
            renderer.sharedMaterial = material;
        }

        internal void SetLayer(int layer)
        {
            if (this.Root != null) {
                this.Root.layer = layer;
            }
        }

        internal void SetMaxParticles(int maxParticles)
        {
            if (this.Ps != null) {
                var main = this.Ps.main;
                main.maxParticles = Mathf.Max(1, maxParticles);
            }
        }

        internal void SetLifeTime(float lifeTime)
        {
            if (this.Ps != null) {
                var main = this.Ps.main;
                main.startLifetime = Mathf.Max(0.05f, lifeTime);
            }
        }

        /// <summary>
        /// 読み込みの終わった画像を割り当てる。handle の所有はこのエミッターに移る。
        /// 各フレームの範囲から中心を基準にした Sprite を作り、textureSheetAnimation で切り替える。
        /// </summary>
        internal bool Bind(ImageHandle handle, float lifeTime)
        {
            if (!this.IsAlive || handle == null || handle.State != ImageState.Ready || handle.Sheet == null) {
                handle?.Dispose();
                return false;
            }
            this.ReleaseImage();
            _handle = handle;
            this.BoundKey = handle.Key;
            this.SetLifeTime(lifeTime);
            this.Aspect = handle.Height > 0 ? (float)handle.Width / handle.Height : 1f;
            if (float.IsNaN(this.Aspect) || float.IsInfinity(this.Aspect) || this.Aspect <= 0f) {
                this.Aspect = 1f;
            }

            var sheet = handle.Sheet;
            var uvs = handle.FrameUVs;
            var sheetModule = this.Ps.textureSheetAnimation;
            sheetModule.enabled = true;
            sheetModule.mode = ParticleSystemAnimationMode.Sprites;
            ClearSprites(sheetModule);
            _sprites = new Sprite[uvs.Count];
            for (var i = 0; i < uvs.Count; i++) {
                var uv = uvs[i];
                var x = Mathf.Clamp(Mathf.Round(uv.x * sheet.width), 0f, sheet.width - 1);
                var y = Mathf.Clamp(Mathf.Round(uv.y * sheet.height), 0f, sheet.height - 1);
                var w = Mathf.Clamp(Mathf.Round(uv.width * sheet.width), 1f, sheet.width - x);
                var h = Mathf.Clamp(Mathf.Round(uv.height * sheet.height), 1f, sheet.height - y);
                // Sheet は読み出しできないことがあるため、画素を読まない FullRect にする
                _sprites[i] = Sprite.Create(sheet, new Rect(x, y, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                sheetModule.AddSprite(_sprites[i]);
            }
            EmoteMaterial.ApplyTexture(this.Mat, sheet);

            sheetModule.animation = ParticleSystemAnimationType.WholeSheet;
            sheetModule.startFrame = 0f;
            sheetModule.cycleCount = 1;
            if (_sprites.Length > 1) {
                sheetModule.timeMode = ParticleSystemAnimationTimeMode.FPS;
                sheetModule.fps = Fps(handle);
                sheetModule.frameOverTime = new ParticleSystem.MinMaxCurve(1f);
            }
            else {
                sheetModule.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
                sheetModule.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            }
            return true;
        }

        /// <summary>フレームの表示時間の平均から求めた速さ。表示時間が不揃いな GIF は平均の速さで動く。</summary>
        private static float Fps(ImageHandle handle)
        {
            var durations = handle.FrameDurations;
            var total = 0f;
            for (var i = 0; i < durations.Count; i++) {
                total += durations[i];
            }
            return total > 0f ? Mathf.Clamp(durations.Count / total, 1f, 60f) : 10f;
        }

        internal void Unbind()
        {
            if (this.Ps != null) {
                this.Ps.Clear(true);
            }
            this.ReleaseImage();
        }

        internal void Clear()
        {
            if (this.Ps != null) {
                this.Ps.Clear(true);
            }
        }

        internal void Destroy()
        {
            this.ReleaseImage();
            if (this.Mat != null) {
                Object.Destroy(this.Mat);
            }
            if (this.Root != null) {
                Object.Destroy(this.Root);
            }
            this.Mat = null;
            this.Root = null;
            this.Ps = null;
        }

        /// <summary>Sprite を破棄し、画像の参照を返す。モジュールの参照を先に外す。</summary>
        private void ReleaseImage()
        {
            if (this.Ps != null) {
                var sheetModule = this.Ps.textureSheetAnimation;
                ClearSprites(sheetModule);
                sheetModule.enabled = false;
            }
            if (_sprites != null) {
                foreach (var s in _sprites) {
                    if (s != null) {
                        Object.Destroy(s);
                    }
                }
                _sprites = null;
            }
            _handle?.Dispose();
            _handle = null;
            this.BoundKey = default(ImageKey);
            this.Aspect = 1f;
        }

        private static void ClearSprites(ParticleSystem.TextureSheetAnimationModule module)
        {
            for (var i = module.spriteCount - 1; i >= 0; i--) {
                module.RemoveSprite(i);
            }
        }

        internal void ApplyStyle(IEmoteStyle style)
        {
            if (!this.IsAlive || style == null) {
                return;
            }
            var main = this.Ps.main;
            main.gravityModifier = style.GravityAccel / StyleMath.GravityUnit;

            var noise = this.Ps.noise;
            noise.enabled = style.UseNoise;
            if (style.UseNoise) {
                noise.quality = ParticleSystemNoiseQuality.Low;
                noise.strength = new ParticleSystem.MinMaxCurve(style.NoiseStrength);
                noise.frequency = style.NoiseFrequency;
                noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.5f);
                noise.damping = false;
                noise.separateAxes = false;
            }

            var colorOverLifetime = this.Ps.colorOverLifetime;
            var sizeOverLifetime = this.Ps.sizeOverLifetime;
            colorOverLifetime.enabled = style.FadeOut;
            sizeOverLifetime.enabled = style.FadeOut;
            if (style.FadeOut) {
                colorOverLifetime.color = new ParticleSystem.MinMaxGradient(FadeGradient);
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, FadeCurve);
            }
        }

        private static Gradient s_fadeGradient;
        private static AnimationCurve s_fadeCurve;

        // 多数のエミッターに一度に適用するため、使い回す
        private static Gradient FadeGradient
        {
            get {
                if (s_fadeGradient == null) {
                    s_fadeGradient = new Gradient();
                    s_fadeGradient.SetKeys(
                        new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
                }
                return s_fadeGradient;
            }
        }

        private static AnimationCurve FadeCurve => s_fadeCurve ?? (s_fadeCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.35f));

        /// <summary>シェーダーの初回の準備をメニューで済ませるため、ごく小さい粒を1つ出す。</summary>
        internal void Warmup(float z)
        {
            if (!this.IsAlive || this.Mat == null) {
                return;
            }
            EmoteMaterial.ApplyTexture(this.Mat, Texture2D.whiteTexture);
            var p = new ParticleSystem.EmitParams {
                position = new Vector3(0f, -1000f, z),
                velocity = Vector3.zero,
                startSize3D = new Vector3(0.001f, 0.001f, 0.001f),
                startColor = new Color(1f, 1f, 1f, 0.01f),
                startLifetime = 0.05f,
                applyShapeToPosition = false,
            };
            this.Ps.Emit(p, 1);
        }
    }
}
