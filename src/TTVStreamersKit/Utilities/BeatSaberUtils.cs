using System.Linq;
using UnityEngine;

namespace TTVStreamersKit.Utilities
{
    internal static class BeatSaberUtils
    {
        private static Material s_noGlow;

        /// <summary>ゲームの UI 用のマテリアルのうち、グローを書かないもの。見つかるまでは null。</summary>
        public static Material UINoGlowMaterial
        {
            get {
                if (s_noGlow == null) {
                    s_noGlow = Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(m => m.name == "UINoGlow");
                }
                return s_noGlow;
            }
        }
    }
}
