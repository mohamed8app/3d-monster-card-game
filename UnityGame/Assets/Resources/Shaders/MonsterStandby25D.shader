Shader "UI/MonsterStandby25D"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _CurrTex ("Current Frame Texture", 2D) = "white" {}
        _NextTex ("Next Frame Texture", 2D) = "white" {}
        _DepthTex ("Depth Map", 2D) = "gray" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _FrameBlend ("Frame Blend", Range(0, 1)) = 0
        _UvRect ("Sprite UV Rect (minX, minY, width, height)", Vector) = (0, 0, 1, 1)
        _ParallaxOffset ("Parallax Offset", Vector) = (0, 0, 0, 0)
        _BreathPhase ("Breath Phase", Range(0, 1)) = 0
        _BreathStrength ("Breath Strength", Float) = 0.022
        _ParallaxStrength ("Parallax Strength", Float) = 0.024
        _PerspectiveSkew ("Perspective Skew", Float) = 0.014
        _RimLightDir ("Rim Light Direction", Vector) = (0.4, 0.7, 0, 0)
        _EmissiveBoost ("Emissive Pulse Boost", Float) = 0.22
        _EnableStandbyFX ("Enable Standby 2.5D", Float) = 1

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
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
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
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            sampler2D _CurrTex;
            sampler2D _NextTex;
            sampler2D _DepthTex;
            float4 _MainTex_TexelSize;
            float4 _CurrTex_TexelSize;

            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

            float _FrameBlend;
            float4 _UvRect;
            float4 _ParallaxOffset;
            float _BreathPhase;
            float _BreathStrength;
            float _ParallaxStrength;
            float _PerspectiveSkew;
            float4 _RimLightDir;
            float _EmissiveBoost;
            float _EnableStandbyFX;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            float2 ComputeDisplacedUV(float2 uv, float depth, float2 normUv)
            {
                float groundPin = smoothstep(0.04, 0.24, normUv.y);
                float depthWeight = max(0.0, depth - 0.12);

                // Chest & torso organic breathing expansion
                float2 chestDelta = normUv - float2(0.50, 0.56);
                float chestMask = exp(-dot(chestDelta, chestDelta) * 5.0);
                float breathLift = _BreathPhase * _BreathStrength * depth * groundPin;
                float2 breathDisp = float2(-chestDelta.x * 0.55, -0.34 - chestDelta.y * 0.42) * breathLift * (0.42 + 0.58 * chestMask);

                // 2.5D depth parallax shift
                float2 paraDisp = -_ParallaxOffset.xy * _ParallaxStrength * depthWeight * groundPin;

                // Subtle 3D perspective keystone foreshortening
                float perspX = (normUv.x - 0.5) * (normUv.y - 0.5) * _PerspectiveSkew * _ParallaxOffset.x * depth;

                return uv + (paraDisp + breathDisp + float2(perspX, 0.0)) * _UvRect.zw;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;
                float2 texelSz = max(_CurrTex_TexelSize.xy, _MainTex_TexelSize.xy);
                float2 uvMin = _UvRect.xy + texelSz * 0.75;
                float2 uvMax = _UvRect.xy + _UvRect.zw - texelSz * 0.75;

                float2 finalUv = uv;
                float depth = 0.5;

                if (_EnableStandbyFX > 0.5)
                {
                    float2 normUv = saturate((uv - _UvRect.xy) / max(_UvRect.zw, float2(0.0001, 0.0001)));
                    float d0 = tex2D(_DepthTex, clamp(uv, uvMin, uvMax)).r;

                    // 2-step midpoint parallax refinement for smooth silhouettes without edge tearing
                    float2 step1Uv = clamp(ComputeDisplacedUV(uv, d0, normUv), uvMin, uvMax);
                    float2 midUv = lerp(uv, step1Uv, 0.55);
                    float d1 = tex2D(_DepthTex, midUv).r;
                    depth = lerp(d0, d1, 0.65);

                    finalUv = clamp(ComputeDisplacedUV(uv, depth, normUv), uvMin, uvMax);
                }

                half4 colA = tex2D(_CurrTex, finalUv);
                half4 colB = tex2D(_NextTex, finalUv);
                half4 color = (lerp(colA, colB, saturate(_FrameBlend)) + _TextureSampleAdd) * IN.color;

                if (_EnableStandbyFX > 0.5)
                {
                    // Estimate subtle 3D surface normal from depth gradients for volumetric rim/armor sheen
                    float2 stepUv = float2(0.0045, 0.0045);
                    float dL = tex2D(_DepthTex, clamp(finalUv - float2(stepUv.x, 0), uvMin, uvMax)).r;
                    float dR = tex2D(_DepthTex, clamp(finalUv + float2(stepUv.x, 0), uvMin, uvMax)).r;
                    float dD = tex2D(_DepthTex, clamp(finalUv - float2(0, stepUv.y), uvMin, uvMax)).r;
                    float dU = tex2D(_DepthTex, clamp(finalUv + float2(0, stepUv.y), uvMin, uvMax)).r;

                    float3 normal3D = normalize(float3((dL - dR) * 2.4, (dD - dU) * 2.4, 0.36));
                    float3 lightDir = normalize(float3(_RimLightDir.x, _RimLightDir.y, 0.65));

                    float rim = pow(saturate(1.0 - normal3D.z), 1.5) * saturate(dot(normal3D.xy, lightDir.xy) * 0.5 + 0.5);
                    float spec = pow(saturate(dot(normal3D, lightDir)), 10.0) * 0.16 * depth;
                    color.rgb += color.rgb * (spec + rim * 0.18 * depth);

                    // Breathing emissive boost for glowing eyes, runes, flames, and arcane energy
                    float maxC = max(color.r, max(color.g, color.b));
                    float minC = min(color.r, min(color.g, color.b));
                    float sat = (maxC > 0.001) ? (maxC - minC) / maxC : 0.0;
                    float glowMask = smoothstep(0.32, 0.82, sat) * smoothstep(0.28, 0.85, maxC) * depth;
                    color.rgb += color.rgb * glowMask * _BreathPhase * _EmissiveBoost;
                }

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
