using IPA.Config.Stores.Attributes;
using IPA.Config.Stores.Converters;
using System;

namespace TTVStreamersKit.Emotes
{
    /// <summary>エモートの表示方法。</summary>
    public enum EmoteStyle
    {
        Shower,
        Fireworks,
        Fountain,
        Bubble,
    }

    /// <summary>エモートを表示する範囲。</summary>
    public enum EmoteZone
    {
        AboveLane,
        Sides,
        Both,
    }

    /// <summary>エモートの演出（ChatEmoteParticles の後継）の設定。項目は ChatEmoteParticles を引き継ぐ。</summary>
    public class EmoteConfig
    {
        public virtual bool EnabledInMenu { get; set; } = true;
        public virtual bool EnabledInSong { get; set; } = true;
        /// <summary>セレブレーションの Power-up で、そのエモートを降らせる。</summary>
        public virtual bool Celebration { get; set; } = true;
        public virtual bool ShowAdvanced { get; set; } = false;

        [UseConverter(typeof(EnumConverter<EmoteStyle>))]
        public virtual EmoteStyle Style { get; set; } = EmoteStyle.Shower;
        [UseConverter(typeof(EnumConverter<EmoteZone>))]
        public virtual EmoteZone Zone { get; set; } = EmoteZone.Both;

        /// <summary>1つのメッセージの中の同じエモートの数に掛ける倍率。</summary>
        public virtual int CountCoefficient { get; set; } = 1;
        public virtual int MaxEmotesPerMessage { get; set; } = 20;
        public virtual int MaxConcurrentParticles { get; set; } = 300;

        public virtual float EmoteSizeMin { get; set; } = 0.80f;
        public virtual float EmoteSizeMax { get; set; } = 2.00f;
        public virtual float Alpha { get; set; } = 0.80f;

        public virtual float EmoteZ { get; set; } = 16.0f;
        public virtual float AboveLaneMinY { get; set; } = 1.80f;
        public virtual float AboveLaneMaxY { get; set; } = 8.00f;
        public virtual float AboveLaneHalfX { get; set; } = 1.20f;
        public virtual float SideInnerX { get; set; } = 1.30f;
        public virtual float SideOuterX { get; set; } = 6.00f;
        public virtual float SideMinY { get; set; } = 0.00f;
        public virtual float SideMaxY { get; set; } = 8.00f;
        public virtual float MenuEmoteZ { get; set; } = 5.0f;
        public virtual float MenuEmoteScale { get; set; } = 0.4f;

        public virtual float LifeTime { get; set; } = 6.0f;
        public virtual float SpeedScale { get; set; } = 1.0f;

        // ===== 設定ファイルだけにあるもの =====
        /// <summary>セレブレーションで降らせる数。</summary>
        public virtual int CelebrationCount { get; set; } = 60;
        public virtual bool IncludeInReflection { get; set; } = true;
        /// <summary>画像の読み込みを待つ時間（秒）。過ぎたら表示しない。</summary>
        public virtual float ImageWaitTimeoutSec { get; set; } = 10.0f;
        public virtual int MaxQueuedMessages { get; set; } = 64;
        public virtual int EmitterPoolSize { get; set; } = 64;
        public virtual int MaxEmitters { get; set; } = 192;

        /// <summary>最小と最大のサイズ。逆に設定されていれば入れ替える。</summary>
        public (float Min, float Max) SizeRange() =>
            this.EmoteSizeMin <= this.EmoteSizeMax ? (this.EmoteSizeMin, this.EmoteSizeMax) : (this.EmoteSizeMax, this.EmoteSizeMin);

        /// <summary>両方の範囲に表示するとき、レーン上方を選ぶ割合（面積の比）。</summary>
        public float AboveLaneWeight()
        {
            var above = this.AboveLaneHalfX * 2f * Math.Max(0f, this.AboveLaneMaxY - this.AboveLaneMinY);
            var sides = 2f * Math.Max(0f, this.SideOuterX - this.SideInnerX) * Math.Max(0f, this.SideMaxY - this.SideMinY);
            var total = above + sides;
            return total > 0f ? above / total : 0.5f;
        }
    }
}
