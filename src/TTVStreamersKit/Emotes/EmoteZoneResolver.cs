using System;

namespace TTVStreamersKit.Emotes
{
    /// <summary>矩形の範囲（XY 平面）。</summary>
    internal struct ZoneRect
    {
        internal float XMin;
        internal float XMax;
        internal float YMin;
        internal float YMax;

        internal float PickX(Random rng) => this.XMin + (float)rng.NextDouble() * (this.XMax - this.XMin);
        internal float PickY(Random rng) => this.YMin + (float)rng.NextDouble() * (this.YMax - this.YMin);
    }

    /// <summary>設定から表示範囲の矩形を作り、面積の比で発生位置の範囲を選ぶ。</summary>
    internal sealed class EmoteZoneResolver
    {
        private ZoneRect _above;
        private ZoneRect _left;
        private ZoneRect _right;
        private EmoteZone _zone = EmoteZone.Both;
        private float _aboveWeight = 0.5f;
        private float _songZ = 16f;
        private float _menuZ = 5f;

        internal void Refresh(EmoteConfig config)
        {
            if (config == null) {
                return;
            }
            _zone = config.Zone;
            _aboveWeight = config.AboveLaneWeight();
            _songZ = config.EmoteZ;
            _menuZ = config.MenuEmoteZ;

            var halfX = Math.Abs(config.AboveLaneHalfX);
            _above = new ZoneRect {
                XMin = -halfX,
                XMax = halfX,
                YMin = Math.Min(config.AboveLaneMinY, config.AboveLaneMaxY),
                YMax = Math.Max(config.AboveLaneMinY, config.AboveLaneMaxY),
            };

            var innerX = Math.Abs(config.SideInnerX);
            var outerX = Math.Abs(config.SideOuterX);
            if (innerX > outerX) {
                var tmp = innerX;
                innerX = outerX;
                outerX = tmp;
            }
            var sideYMin = Math.Min(config.SideMinY, config.SideMaxY);
            var sideYMax = Math.Max(config.SideMinY, config.SideMaxY);
            _right = new ZoneRect { XMin = innerX, XMax = outerX, YMin = sideYMin, YMax = sideYMax };
            _left = new ZoneRect { XMin = -outerX, XMax = -innerX, YMin = sideYMin, YMax = sideYMax };
        }

        /// <summary>今のシーンでの奥行き。</summary>
        internal float GetZ(bool inSong) => inSong ? _songZ : _menuZ;

        /// <summary>面積の比で範囲を1つ選ぶ。左右は同じ割合で選ぶ。</summary>
        internal ZoneRect Pick(Random rng)
        {
            switch (_zone) {
                case EmoteZone.AboveLane:
                    return _above;
                case EmoteZone.Sides:
                    return rng.NextDouble() < 0.5 ? _left : _right;
                default:
                    if (rng.NextDouble() < _aboveWeight) {
                        return _above;
                    }
                    return rng.NextDouble() < 0.5 ? _left : _right;
            }
        }
    }
}
