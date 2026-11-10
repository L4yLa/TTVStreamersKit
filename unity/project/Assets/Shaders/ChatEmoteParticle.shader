Shader "ChatEmoteParticles/Emote"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color   ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Transparent"
            "RenderType"      = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType"     = "Plane"
        }

        Cull    Off
        Lighting Off
        ZWrite  Off
        ZTest   LEqual

        // RGB は通常のアルファブレンド。
        // アルファ側は出力先を 0 へ寄せる（Zero, OneMinusSrcAlpha）。
        // Beat Saber はフレームバッファのアルファを Bloom（グロー）の強度マスクとして
        // 使うため、ここへ書き込むと白飛びする。ゲーム内 UINoGlow と同じ考え方。
        Blend SrcAlpha OneMinusSrcAlpha, Zero OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0

            // ── VR（Single Pass Instanced）対応 ───────────────────────────
            // multi_compile_instancing だけでは不十分。AssetBundle 生成時に
            // 「エディタ内のマテリアルが使っていないバリアント」として
            // STEREO_INSTANCING_ON が剥がされ、片目にしか描画されなくなる。
            // キーワードを明示宣言してバリアントの存在を保証する。
            // あわせて Graphics Settings の Instancing Variants を Keep All にすること。
            #pragma multi_compile_instancing
            #pragma multi_compile _ STEREO_INSTANCING_ON
            #pragma multi_compile _ STEREO_MULTIVIEW_ON

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                fixed4 color  : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            fixed4    _Color;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos   = UnityObjectToClipPos(v.vertex);
                o.uv    = TRANSFORM_TEX(v.uv, _MainTex);

                // 頂点カラーは ParticleSystem の startColor（＝不透明度設定）。
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                fixed4 c = tex2D(_MainTex, i.uv) * i.color;

                // ほぼ透明な画素は捨てる。半透明同士のソート由来のちらつきを防ぐ。
                clip(c.a - 0.003);

                return c;
            }
            ENDCG
        }
    }

    // フォールバックを持たせるとバリアントが増えるうえ、
    // 代替パスで Bloom マスクが書かれてしまうため無効にする。
    Fallback Off
}
