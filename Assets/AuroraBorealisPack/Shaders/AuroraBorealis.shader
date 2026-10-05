Shader "AuroraBorealis_Pack/AuroraBorealis" {
    Properties {
        _Falloff ("Falloff", 2D) = "white" {}
        _Ramp ("Ramp", 2D) = "white" {}
        _Noise ("Noise", 2D) = "white" {}
        _NoiseEvolutionSpeed ("NoiseEvolutionSpeed", Float ) = 1
        [MaterialToggle] _InvertEvolutionDirection ("InvertEvolutionDirection", Float ) = 1
        [MaterialToggle] _InvertNoiseTexture ("InvertNoiseTexture", Float ) = 0
        _MovementSpeed ("MovementSpeed", Float ) = 1
        _Sharpness ("Sharpness", Range(0, 1)) = 0.1
        _Color ("Color", 2D) = "white" {}
        _Color_Multiplier ("Color_Multiplier", Float ) = 1
        _Dark_Multiplier ("Dark_Multiplier", Float ) = 1
        _Dark_Tint ("Dark_Tint", Color) = (1,1,1,1)
        _Middle_Multiplier ("Middle_Multiplier", Float ) = 1
        _Middle_Tint ("Middle_Tint", Color) = (1,1,1,1)
        _Highlight_Multiplier ("Highlight_Multiplier", Float ) = 1
        _Highlight_Tint ("Highlight_Tint", Color) = (1,1,1,1)
        [HideInInspector]_Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
    }
    SubShader {
        Tags {
            "Queue"="Transparent"
            "RenderType"="TransparentCutout"
        }
        Pass {
            Name "FORWARD"
            Tags {
                "LightMode"="ForwardBase"
            }
            Blend One One
            Cull Off
            ZWrite Off
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define UNITY_PASS_FORWARDBASE
            #include "UnityCG.cginc"
            #pragma multi_compile_fwdbase_fullshadows
            #pragma only_renderers d3d9 d3d11 glcore gles n3ds wiiu 
            #pragma target 3.0
            uniform float4 _TimeEditor;
            uniform sampler2D _Noise; uniform float4 _Noise_ST;
            uniform float _NoiseEvolutionSpeed;
            uniform sampler2D _Ramp; uniform float4 _Ramp_ST;
            uniform float _MovementSpeed;
            uniform float _Color_Multiplier;
            uniform float _Highlight_Multiplier;
            uniform float _Middle_Multiplier;
            uniform float _Dark_Multiplier;
            uniform float _Sharpness;
            uniform fixed _InvertNoiseTexture;
            uniform sampler2D _Fallof; uniform float4 _Fallof_ST;
            uniform sampler2D _Color; uniform float4 _Color_ST;
            uniform float4 _Dark_Tint;
            uniform float4 _Middle_Tint;
            uniform float4 _Highlight_Tint;
            uniform fixed _InvertEvolutionDirection;
            struct VertexInput {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 texcoord0 : TEXCOORD0;
                float2 texcoord1 : TEXCOORD1;
                float4 vertexColor : COLOR;
            };
            struct VertexOutput {
                float4 pos : SV_POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float4 posWorld : TEXCOORD2;
                float3 normalDir : TEXCOORD3;
                float4 vertexColor : COLOR;
            };
            VertexOutput vert (VertexInput v) {
                VertexOutput o = (VertexOutput)0;
                o.uv0 = v.texcoord0;
                o.uv1 = v.texcoord1;
                o.vertexColor = v.vertexColor;
                o.normalDir = UnityObjectToWorldNormal(v.normal);
                o.posWorld = mul(unity_ObjectToWorld, v.vertex);
                o.pos = UnityObjectToClipPos( v.vertex );
                return o;
            }
            float4 frag(VertexOutput i, float facing : VFACE) : COLOR {
                float isFrontFace = ( facing >= 0 ? 1 : 0 );
                float faceSign = ( facing >= 0 ? 1 : -1 );
                i.normalDir = normalize(i.normalDir);
                i.normalDir *= faceSign;
                float3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - i.posWorld.xyz);
                float3 normalDirection = i.normalDir;
                float2 node_7855 = i.uv1.rg;
                float4 node_2643 = _Time + _TimeEditor;
                float node_8140 = node_7855.g;
                float node_4733 = ((node_8140*0.05)+((node_2643.g/1000.0)*lerp( _NoiseEvolutionSpeed, (_NoiseEvolutionSpeed*(-1.0)), _InvertEvolutionDirection )));
                float2 node_3896 = float2((node_7855.r+(node_2643.r*_MovementSpeed*0.1)),node_4733);
                float4 _Noise_var = tex2D(_Noise,TRANSFORM_TEX(node_3896, _Noise));
                float node_2123 = lerp( _Noise_var.r, (1.0 - _Noise_var.r), _InvertNoiseTexture );
                float node_3102 = node_8140;
                float node_7935 = (_Sharpness*20.0+5.0);
                float node_9681 = ((1.0 - ((node_2123-node_3102)*node_7935))*((node_2123-(node_3102+(-0.005)))*node_7935)*3.0);
                clip(saturate((node_9681*100.0)) - 0.5);

                float2 node_6926 = float2(0.5,(1.0 - i.vertexColor.r));
                float4 _Ramp_var = tex2D(_Ramp,TRANSFORM_TEX(node_6926, _Ramp));
                float node_9631 = ((node_9681*_Ramp_var.r)/3.0);
                float node_4310 = i.uv1.r;
                float2 node_9932 = float2(0.5,0.05);
                float4 node_9272 = tex2D(_Color,TRANSFORM_TEX(node_9932, _Color));
                float node_1984 = (1.0 - i.vertexColor.r);
                float node_3430 = (node_1984*-3.333333+1.0);
                float2 node_7181 = float2(0.5,0.5);
                float4 node_8864 = tex2D(_Color,TRANSFORM_TEX(node_7181, _Color));
                float2 node_4422 = float2(0.5,0.95);
                float4 node_5967 = tex2D(_Color,TRANSFORM_TEX(node_4422, _Color));
                float node_2210 = _Sharpness;
                float2 node_7440 = float2(i.uv1.g,i.uv1.r);
                float4 _Fallof_var = tex2D(_Fallof,TRANSFORM_TEX(node_7440, _Fallof));
                float3 emissive = ((saturate((node_9631*saturate((lerp(node_4310,(1.0 - node_4310),step(0.5,node_4310))/0.1))))*((node_9272.rgb*saturate(node_3430)*_Highlight_Multiplier*_Highlight_Tint.rgb)+(node_8864.rgb*saturate(((1.0 - node_3430)*(1.0 - node_1984)))*_Middle_Multiplier*_Middle_Tint.rgb)+(node_5967.rgb*pow(saturate(node_1984),0.5)*_Dark_Multiplier*_Dark_Tint.rgb))*(_Color_Multiplier*saturate((node_2210*1.0+0.5))*(saturate((node_2210*2.0+-1.0))+1.0)))*(1.0 - pow(1.0-max(0,dot(i.normalDir, viewDirection)),1.0))*_Fallof_var.r*((1.0 - i.uv0.r)*i.uv0.r*4.0));
                float3 finalColor = emissive;
                return fixed4(finalColor,1);
            }
            ENDCG
        }
        Pass {
            Name "ShadowCaster"
            Tags {
                "LightMode"="ShadowCaster"
            }
            Offset 1, 1
            Cull Off
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define UNITY_PASS_SHADOWCASTER
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #pragma fragmentoption ARB_precision_hint_fastest
            #pragma multi_compile_shadowcaster
            #pragma only_renderers d3d9 d3d11 glcore gles n3ds wiiu 
            #pragma target 3.0
            uniform float4 _TimeEditor;
            uniform sampler2D _Noise; uniform float4 _Noise_ST;
            uniform float _NoiseEvolutionSpeed;
            uniform float _MovementSpeed;
            uniform float _Sharpness;
            uniform fixed _InvertNoiseTexture;
            uniform fixed _InvertEvolutionDirection;
            struct VertexInput {
                float4 vertex : POSITION;
                float2 texcoord1 : TEXCOORD1;
            };
            struct VertexOutput {
                V2F_SHADOW_CASTER;
                float2 uv1 : TEXCOORD1;
            };
            VertexOutput vert (VertexInput v) {
                VertexOutput o = (VertexOutput)0;
                o.uv1 = v.texcoord1;
                o.pos = UnityObjectToClipPos( v.vertex );
                TRANSFER_SHADOW_CASTER(o)
                return o;
            }
            float4 frag(VertexOutput i, float facing : VFACE) : COLOR {
                float isFrontFace = ( facing >= 0 ? 1 : 0 );
                float faceSign = ( facing >= 0 ? 1 : -1 );
                float2 node_7855 = i.uv1.rg;
                float4 node_2643 = _Time + _TimeEditor;
                float node_8140 = node_7855.g;
                float node_4733 = ((node_8140*0.05)+((node_2643.g/1000.0)*lerp( _NoiseEvolutionSpeed, (_NoiseEvolutionSpeed*(-1.0)), _InvertEvolutionDirection )));
                float2 node_3896 = float2((node_7855.r+(node_2643.r*_MovementSpeed*0.1)),node_4733);
                float4 _Noise_var = tex2D(_Noise,TRANSFORM_TEX(node_3896, _Noise));
                float node_2123 = lerp( _Noise_var.r, (1.0 - _Noise_var.r), _InvertNoiseTexture );
                float node_3102 = node_8140;
                float node_7935 = (_Sharpness*20.0+5.0);
                float node_9681 = ((1.0 - ((node_2123-node_3102)*node_7935))*((node_2123-(node_3102+(-0.005)))*node_7935)*3.0);
                clip(saturate((node_9681*100.0)) - 0.5);
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
