using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Parser;
using BeatSaberMarkupLanguage.ViewControllers;
using System;
using System.Collections.Generic;
using TTVStreamersKit.Emotes;
using TTVStreamersKit.Utilities;

namespace TTVStreamersKit.UI
{
    /// <summary>エモートの演出の設定。値は変えたときにすぐ設定ファイルへ反映する。</summary>
    internal sealed class EmoteSettingsViewController : BSMLResourceViewController
    {
        private const string AdvancedTag = "advanced";

        public override string ResourceName => "TTVStreamersKit.UI.EmoteSettingsView.bsml";

        public override string Content => Localization.Bsml(base.Content);

        [UIParams]
        private BSMLParserParams _parserParams = null;

        private static EmoteConfig C => Plugin.Config.Emotes;

        [UIAction("#post-parse")]
        private void PostParse() => this.SetAdvancedVisible(C.ShowAdvanced);

        // トグルの on-change は値を設定へ反映する前に呼ばれるため、渡された値を使う
        [UIAction("on-advanced-changed")]
        private void OnAdvancedChanged(object value) => this.SetAdvancedVisible(value is bool show ? show : C.ShowAdvanced);

        private void SetAdvancedVisible(bool show)
        {
            var objects = _parserParams?.GetObjectsWithTag(AdvancedTag);
            if (objects == null) {
                return;
            }
            foreach (var o in objects) {
                if (o != null) {
                    o.SetActive(show);
                }
            }
        }

        [UIValue("enabled-in-menu")]
        private bool EnabledInMenu { get => C.EnabledInMenu; set => C.EnabledInMenu = value; }

        [UIValue("enabled-in-song")]
        private bool EnabledInSong { get => C.EnabledInSong; set => C.EnabledInSong = value; }

        [UIValue("celebration")]
        private bool Celebration { get => C.Celebration; set => C.Celebration = value; }

        [UIValue("show-advanced")]
        private bool ShowAdvanced { get => C.ShowAdvanced; set => C.ShowAdvanced = value; }

        [UIValue("style-options")]
        private readonly List<object> _styleOptions = new List<object> { EmoteStyle.Shower, EmoteStyle.Fireworks, EmoteStyle.Fountain, EmoteStyle.Bubble };

        [UIValue("zone-options")]
        private readonly List<object> _zoneOptions = new List<object> { EmoteZone.AboveLane, EmoteZone.Sides, EmoteZone.Both };

        [UIValue("style")]
        private EmoteStyle Style { get => C.Style; set => C.Style = value; }

        [UIValue("zone")]
        private EmoteZone Zone { get => C.Zone; set => C.Zone = value; }

        [UIAction("style-formatter")]
        private string StyleFormatter(object value)
        {
            switch (value is EmoteStyle s ? s : EmoteStyle.Shower) {
                case EmoteStyle.Fireworks: return Localization.T("Fireworks", "花火");
                case EmoteStyle.Fountain: return Localization.T("Fountain", "噴水");
                case EmoteStyle.Bubble: return Localization.T("Bubble", "バブル");
                default: return Localization.T("Shower", "シャワー");
            }
        }

        [UIAction("zone-formatter")]
        private string ZoneFormatter(object value)
        {
            switch (value is EmoteZone z ? z : EmoteZone.Both) {
                case EmoteZone.AboveLane: return Localization.T("Above the lane", "レーンの上方");
                case EmoteZone.Sides: return Localization.T("Sides", "左右");
                default: return Localization.T("Both", "両方");
            }
        }

        [UIValue("count-coefficient")]
        private int CountCoefficient { get => C.CountCoefficient; set => C.CountCoefficient = value; }

        [UIValue("max-emotes-per-message")]
        private int MaxEmotesPerMessage { get => C.MaxEmotesPerMessage; set => C.MaxEmotesPerMessage = value; }

        [UIValue("max-concurrent-particles")]
        private int MaxConcurrentParticles { get => C.MaxConcurrentParticles; set => C.MaxConcurrentParticles = value; }

        [UIValue("emote-size-min")]
        private float EmoteSizeMin { get => C.EmoteSizeMin; set => C.EmoteSizeMin = value; }

        [UIValue("emote-size-max")]
        private float EmoteSizeMax { get => C.EmoteSizeMax; set => C.EmoteSizeMax = value; }

        [UIValue("alpha")]
        private float Alpha { get => C.Alpha; set => C.Alpha = value; }

        [UIValue("emote-z")]
        private float EmoteZ { get => C.EmoteZ; set => C.EmoteZ = value; }

        [UIValue("above-lane-half-x")]
        private float AboveLaneHalfX { get => C.AboveLaneHalfX; set => C.AboveLaneHalfX = value; }

        [UIValue("above-lane-min-y")]
        private float AboveLaneMinY { get => C.AboveLaneMinY; set => C.AboveLaneMinY = value; }

        [UIValue("above-lane-max-y")]
        private float AboveLaneMaxY { get => C.AboveLaneMaxY; set => C.AboveLaneMaxY = value; }

        [UIValue("side-inner-x")]
        private float SideInnerX { get => C.SideInnerX; set => C.SideInnerX = value; }

        [UIValue("side-outer-x")]
        private float SideOuterX { get => C.SideOuterX; set => C.SideOuterX = value; }

        [UIValue("side-min-y")]
        private float SideMinY { get => C.SideMinY; set => C.SideMinY = value; }

        [UIValue("side-max-y")]
        private float SideMaxY { get => C.SideMaxY; set => C.SideMaxY = value; }

        [UIValue("menu-emote-z")]
        private float MenuEmoteZ { get => C.MenuEmoteZ; set => C.MenuEmoteZ = value; }

        [UIValue("menu-emote-scale")]
        private float MenuEmoteScale { get => C.MenuEmoteScale; set => C.MenuEmoteScale = value; }

        [UIValue("lifetime")]
        private float LifeTime { get => C.LifeTime; set => C.LifeTime = value; }

        [UIValue("speed-scale")]
        private float SpeedScale { get => C.SpeedScale; set => C.SpeedScale = value; }
    }
}
