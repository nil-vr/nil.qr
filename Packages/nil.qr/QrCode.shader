// This shader renders low-resolution R-only textures.
// The output color is the input color interpolated between the parameters for zero and one.
// The texture is scaled based on [d7samurai's "aa point sampling for pixel art"](https://www.shadertoy.com/view/MlB3D3).

Shader "Unlit/QrCode"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Zero ("Zero", Color) = (0,0,0,1)
        _One ("One", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        LOD 100

        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                fixed4 color0 : COLOR0;
                fixed4 color1 : COLOR1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            fixed4 _Zero;
            fixed4 _One;

            fixed4 _TextureSampleAdd;

            bool _UseClipRect;
            float4 _ClipRect;

            bool _UseAlphaClip;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(o.worldPosition);

                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color0 = v.color * _Zero;
                o.color1 = v.color * _One;

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 pixel = i.texcoord * _MainTex_TexelSize.zw;
                float2 base;
                float2 offset = modf(pixel, base);
                float2 texcoord = base + smoothstep(0.0, 1.0, offset / fwidth(pixel)) - 0.5;
                fixed4 color = tex2D(_MainTex, texcoord * _MainTex_TexelSize.xy) + _TextureSampleAdd;
                color = lerp(i.color0, i.color1, color.r);
                
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip (color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
