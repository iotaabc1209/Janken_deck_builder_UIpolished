Shader "UI/HoleMaskRect"
{
    Properties
    {
        _Color ("Mask Color", Color) = (0,0,0,0.65)
        _HoleRect ("Hole Rect (xMin,yMin,xMax,yMax)", Vector) = (0,0,0,0)
        _UseHole ("Use Hole (0/1)", Float) = 0
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

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]

        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color    : COLOR;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float2 uv       : TEXCOORD0;
                fixed4 color    : COLOR;
            };

            fixed4 _Color;
            float4 _HoleRect; // xMin, yMin, xMax, yMax in UV (0..1)
            float _UseHole;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = _Color;

                if (_UseHole > 0.5)
                {
                    // Inside hole => alpha 0 (transparent)
                    float inside =
                        step(_HoleRect.x, i.uv.x) *
                        step(_HoleRect.y, i.uv.y) *
                        step(i.uv.x, _HoleRect.z) *
                        step(i.uv.y, _HoleRect.w);

                    col.a = col.a * (1.0 - inside);
                }

                return col;
            }
            ENDCG
        }
    }
}
