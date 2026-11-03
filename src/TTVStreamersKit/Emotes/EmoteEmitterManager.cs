using CameraUtils.Core;
using System;
using System.Collections.Generic;
using TTVCore.Api.V1;
using TTVStreamersKit.Configuration;
using TTVStreamersKit.Emotes.Styles;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;

namespace TTVStreamersKit.Emotes
{
    /// <summary>
    /// チャットのエモートを空間に降らせる（ChatEmoteParticles の後継）。
    /// TTVCore のメッセージ（メインスレッドで届く）からエモートの画像を数え、エモートごとの ParticleSystem で表示する。
    /// 画像の読み込みが終わっていなければ、終わるまで（期限まで）表示を待つ。
    /// </summary>
    internal sealed class EmoteEmitterManager : IInitializable, IDisposable
    {
        private const string GameScene = "GameCore";
        private const int MaxMessagesPerFrame = 4;

        /// <summary>表示を待っている画像。</summary>
        private sealed class Pending
        {
            internal ImageHandle Handle;
            internal int Count;
            internal float Deadline;
        }

        private readonly PluginConfig _pluginConfig;
        private readonly Queue<Dictionary<ImageKey, int>> _messages = new Queue<Dictionary<ImageKey, int>>();
        private readonly List<EmoteEmitter> _emitters = new List<EmoteEmitter>(64);
        private readonly Dictionary<ImageKey, EmoteEmitter> _bound = new Dictionary<ImageKey, EmoteEmitter>();
        private readonly List<Pending> _pending = new List<Pending>();
        private readonly EmoteZoneResolver _zones = new EmoteZoneResolver();
        private readonly IEmoteStyle[] _styles = { new ShowerStyle(), new FireworksStyle(), new FountainStyle(), new BubbleStyle() };
        private readonly System.Random _rng = new System.Random();

        private GameObject _root;
        private bool _inSong;
        private bool _initialized;
        private bool _warmedUp;
        private bool _wasActive;
        private bool _configDirty = true;
        private int _layer = (int)VisibilityLayer.AlwaysVisibleAndReflected;

        public EmoteEmitterManager(PluginConfig config)
        {
            _pluginConfig = config;
        }

        private EmoteConfig C => _pluginConfig.Emotes;

        public void Initialize()
        {
            _root = new GameObject("TTVStreamersKit Emotes");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            // App のコンテナに置くため、毎フレームの処理は自前の MonoBehaviour から呼ぶ
            _root.AddComponent<Ticker>().Tick = this.Tick;
            TTVCoreApi.Chat.MessageReceived += this.OnMessage;
            TTVCoreApi.Notifications.BitsUsed += this.OnBitsUsed;
            SceneManager.activeSceneChanged += this.OnActiveSceneChanged;
            _pluginConfig.OnConfigChanged += this.OnConfigChanged;
            _initialized = true;
        }

        public void Dispose()
        {
            if (!_initialized) {
                return;
            }
            _initialized = false;
            TTVCoreApi.Chat.MessageReceived -= this.OnMessage;
            TTVCoreApi.Notifications.BitsUsed -= this.OnBitsUsed;
            SceneManager.activeSceneChanged -= this.OnActiveSceneChanged;
            _pluginConfig.OnConfigChanged -= this.OnConfigChanged;
            this.ClearPending();
            foreach (var e in _emitters) {
                e?.Destroy();
            }
            _emitters.Clear();
            _bound.Clear();
            if (_root != null) {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
        }

        // BSIPA は設定の読み込みを別のスレッドで行うことがあるため、ここでは印を付けるだけにする
        private void OnConfigChanged() => _configDirty = true;

        private void OnActiveSceneChanged(Scene previous, Scene next)
        {
            _inSong = next.name == GameScene;
        }

        private bool IsActive =>
            _pluginConfig.EmotesEnabled && (_inSong ? this.C.EnabledInSong : this.C.EnabledInMenu);

        // ================= 受信（メインスレッド） =================

        private void OnMessage(ChatMessage message)
        {
            if (!this.IsActive || message?.Fragments == null) {
                return;
            }
            Dictionary<ImageKey, int> counts = null;
            foreach (var f in message.Fragments) {
                var key = ImageOf(f);
                if (!key.IsValid) {
                    continue;
                }
                counts = counts ?? new Dictionary<ImageKey, int>();
                counts.TryGetValue(key, out var n);
                counts[key] = n + 1;
            }
            if (counts != null) {
                this.Enqueue(counts);
            }
        }

        /// <summary>セレブレーションの Power-up で、そのエモートをまとめて降らせる。</summary>
        private void OnBitsUsed(BitsUseNotification n)
        {
            if (!this.IsActive || !this.C.Celebration || n == null
                || n.PowerUp != PowerUpType.Celebration || !n.PowerUpEmoteImage.IsValid) {
                return;
            }
            // 表示数の倍率と1メッセージの上限は掛けない
            this.Enqueue(new Dictionary<ImageKey, int> { [n.PowerUpEmoteImage] = -Math.Max(1, this.C.CelebrationCount) });
        }

        private void Enqueue(Dictionary<ImageKey, int> counts)
        {
            _messages.Enqueue(counts);
            // あふれた分は古いものから捨てる
            while (_messages.Count > Math.Max(1, this.C.MaxQueuedMessages)) {
                _messages.Dequeue();
            }
        }

        private static ImageKey ImageOf(ChatFragment f)
        {
            switch (f) {
                case EmoteFragment e: return e.Image;
                case ThirdPartyEmoteFragment t: return t.Image;
                case EmojiFragment j: return j.Image;
                case CheermoteFragment c: return c.Image;
                default: return default(ImageKey);
            }
        }

        // ================= 毎フレーム =================

        private void Tick()
        {
            if (!_initialized) {
                return;
            }
            var c = this.C;
            if (_configDirty) {
                _configDirty = false;
                this.ApplyConfig(c);
            }
            if (EmoteMaterial.Failed) {
                return;
            }
            if (!this.IsActive) {
                _messages.Clear();
                // 無効になった直後だけ、表示中の粒を消す
                if (_wasActive) {
                    this.ResetAll();
                }
                _wasActive = false;
                this.RecycleFinished();
                return;
            }
            _wasActive = true;
            if (!_inSong) {
                this.PreallocateOne(c);
                this.WarmupOnce();
            }
            this.ProcessEmissions(c);
            this.RecycleFinished();
        }

        private void ApplyConfig(EmoteConfig c)
        {
            _zones.Refresh(c);
            var layer = c.IncludeInReflection ? (int)VisibilityLayer.AlwaysVisibleAndReflected : (int)VisibilityLayer.AlwaysVisible;
            var style = this.CurrentStyle(c);
            // 表示方法や範囲を変えたら、前の設定の粒を残さない
            this.ResetAll();
            _layer = layer;
            foreach (var e in _emitters) {
                if (e == null || !e.IsAlive) {
                    continue;
                }
                e.SetLayer(_layer);
                e.SetMaxParticles(c.MaxConcurrentParticles);
                e.SetLifeTime(c.LifeTime);
                e.ApplyStyle(style);
            }
        }

        private IEmoteStyle CurrentStyle(EmoteConfig c)
        {
            var index = (int)c.Style;
            return _styles[index >= 0 && index < _styles.Length ? index : 0];
        }

        private void ProcessEmissions(EmoteConfig c)
        {
            var capacity = c.MaxConcurrentParticles - this.CountAliveParticles();
            var createdThisFrame = 0;

            // 1) 読み込みを待っていた画像（古いものから）
            var now = Time.unscaledTime;
            for (var i = 0; i < _pending.Count;) {
                var p = _pending[i];
                if (p.Handle.State == ImageState.Loading && now <= p.Deadline) {
                    i++;
                    continue;
                }
                _pending.RemoveAt(i);
                if (p.Handle.State != ImageState.Ready || capacity <= 0) {
                    p.Handle.Dispose();
                    continue;
                }
                var emitter = this.AcquireEmitter(c, p.Handle, ref createdThisFrame);
                if (emitter != null) {
                    capacity -= this.Emit(emitter, Math.Min(p.Count, capacity), c);
                }
            }

            // 2) 新しいメッセージ
            for (var processed = 0; processed < MaxMessagesPerFrame && _messages.Count > 0; processed++) {
                foreach (var kv in _messages.Dequeue()) {
                    // 負の数はセレブレーション（倍率と上限を掛けない）
                    var count = kv.Value < 0
                        ? -kv.Value
                        : Mathf.Clamp(kv.Value * Math.Max(1, c.CountCoefficient), 1, Math.Max(1, c.MaxEmotesPerMessage));
                    if (_bound.TryGetValue(kv.Key, out var bound) && bound.IsAlive) {
                        if (capacity > 0) {
                            capacity -= this.Emit(bound, Math.Min(count, capacity), c);
                        }
                        continue;
                    }
                    this.Request(kv.Key, count, c, ref capacity, ref createdThisFrame);
                }
            }
        }

        /// <summary>まだ割り当てていない画像を取りに行く。読み込み済みならすぐ表示し、読み込み中なら待つ。</summary>
        private void Request(ImageKey key, int count, EmoteConfig c, ref int capacity, ref int createdThisFrame)
        {
            foreach (var p in _pending) {
                if (p.Handle.Key == key) {
                    // 同じ画像は数を足す。上限は1メッセージの上限（セレブレーションならその数）
                    p.Count = Math.Min(p.Count + count, Math.Max(Math.Max(p.Count, count), c.MaxEmotesPerMessage));
                    return;
                }
            }
            var handle = TTVCoreApi.Images.Acquire(key);
            if (handle.State == ImageState.Loading) {
                _pending.Add(new Pending { Handle = handle, Count = count, Deadline = Time.unscaledTime + Mathf.Max(1f, c.ImageWaitTimeoutSec) });
                return;
            }
            if (handle.State != ImageState.Ready || capacity <= 0) {
                handle.Dispose();
                return;
            }
            var emitter = this.AcquireEmitter(c, handle, ref createdThisFrame);
            if (emitter != null) {
                capacity -= this.Emit(emitter, Math.Min(count, capacity), c);
            }
        }

        private int Emit(EmoteEmitter emitter, int count, EmoteConfig c)
        {
            if (count <= 0) {
                return 0;
            }
            var style = this.CurrentStyle(c);
            var z = _zones.GetZ(_inSong);
            var scale = _inSong ? 1f : c.MenuEmoteScale;
            var (min, max) = c.SizeRange();
            var anchor = style.BeginGroup(_zones, _rng, z);
            var p = new ParticleSystem.EmitParams {
                applyShapeToPosition = false,
                startColor = new Color(1f, 1f, 1f, Mathf.Clamp01(c.Alpha)),
                startLifetime = c.LifeTime,
            };
            for (var i = 0; i < count; i++) {
                style.Fill(ref p, _zones, _rng, anchor, z, c.SpeedScale);
                var size = StyleMath.Range(_rng, min * scale, max * scale);
                p.startSize3D = new Vector3(size * emitter.Aspect, size, size);
                emitter.Ps.Emit(p, 1);
            }
            emitter.LastEmitFrame = Time.frameCount;
            return count;
        }

        // ================= エミッター =================

        /// <summary>画像にエミッターを割り当てる。割り当てられなければ handle を返して null。</summary>
        private EmoteEmitter AcquireEmitter(EmoteConfig c, ImageHandle handle, ref int createdThisFrame)
        {
            if (_bound.TryGetValue(handle.Key, out var bound) && bound.IsAlive) {
                handle.Dispose();
                return bound;
            }
            var emitter = _emitters.Find(e => e != null && e.IsAlive && !e.IsBound);
            if (emitter == null && _emitters.Count < c.MaxEmitters && !(_inSong && createdThisFrame >= 1)) {
                // 曲の最中は1フレームに1つまで作る
                emitter = this.CreateEmitter(c);
                if (emitter != null) {
                    createdThisFrame++;
                }
            }
            if (emitter == null) {
                handle.Dispose();
                return null;
            }
            emitter.SetLayer(_layer);
            emitter.SetMaxParticles(c.MaxConcurrentParticles);
            emitter.ApplyStyle(this.CurrentStyle(c));
            var key = handle.Key;
            if (!emitter.Bind(handle, c.LifeTime)) {
                return null;
            }
            _bound[key] = emitter;
            return emitter;
        }

        private EmoteEmitter CreateEmitter(EmoteConfig c)
        {
            if (_root == null) {
                return null;
            }
            var emitter = EmoteEmitter.Create(_root.transform, _layer, c.MaxConcurrentParticles);
            if (emitter == null) {
                return null;
            }
            emitter.SetLifeTime(c.LifeTime);
            emitter.ApplyStyle(this.CurrentStyle(c));
            _emitters.Add(emitter);
            return emitter;
        }

        /// <summary>メニューにいる間に、エミッターを1フレームに1つずつ作っておく。</summary>
        private void PreallocateOne(EmoteConfig c)
        {
            if (_emitters.Count < Math.Min(c.EmitterPoolSize, c.MaxEmitters)) {
                this.CreateEmitter(c);
            }
        }

        private void WarmupOnce()
        {
            if (_warmedUp || _emitters.Count == 0 || _emitters[0] == null || !_emitters[0].IsAlive) {
                return;
            }
            _emitters[0].Warmup(_zones.GetZ(false));
            _emitters[0].Clear();
            _warmedUp = true;
        }

        private int CountAliveParticles()
        {
            var total = 0;
            foreach (var e in _emitters) {
                if (e != null && e.IsAlive) {
                    total += e.ParticleCount;
                }
            }
            return total;
        }

        /// <summary>粒がなくなったエミッターの割り当てを外す。</summary>
        private void RecycleFinished()
        {
            var frame = Time.frameCount;
            foreach (var e in _emitters) {
                if (e == null || !e.IsAlive || !e.IsBound || e.ParticleCount > 0 || frame - e.LastEmitFrame < 2) {
                    continue;
                }
                _bound.Remove(e.BoundKey);
                e.Unbind();
            }
        }

        private void ResetAll()
        {
            foreach (var e in _emitters) {
                if (e != null && e.IsAlive) {
                    e.Unbind();
                }
            }
            _bound.Clear();
            this.ClearPending();
        }

        private void ClearPending()
        {
            foreach (var p in _pending) {
                p.Handle.Dispose();
            }
            _pending.Clear();
        }

        private sealed class Ticker : MonoBehaviour
        {
            // 同じ例外が毎フレーム出るとき、ログの書き込みでゲームが重くならないよう打ち切る
            private const int MaxLoggedErrors = 5;

            public Action Tick;

            private int _errors;

            private void Update()
            {
                try {
                    this.Tick?.Invoke();
                    _errors = 0;
                }
                catch (Exception e) {
                    if (_errors >= MaxLoggedErrors) {
                        return;
                    }
                    _errors++;
                    Plugin.Log.Error($"Emotes: {e}");
                    if (_errors == MaxLoggedErrors) {
                        Plugin.Log.Error("Emotes: the update keeps failing. Further errors are not logged until it recovers.");
                    }
                }
            }
        }
    }
}
