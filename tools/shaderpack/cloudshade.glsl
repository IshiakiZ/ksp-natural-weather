#ifdef VERTEX
#version 150
uniform vec4 _CloudSheet;    // xy: 1 (the sheet as large as the screen), z: its depth, w: 1
in vec3 in_POSITION0;
out vec2 vs_TEXCOORD0;
void main()
{
    gl_Position = vec4(in_POSITION0.xy * _CloudSheet.xy, _CloudSheet.z, _CloudSheet.w);
    vs_TEXCOORD0 = in_POSITION0.xy * 0.5 + 0.5;
}
#endif
#ifdef FRAGMENT
#version 150
// The clouds' shadow, laid on everything the camera has drawn (Natural Weather's Clouds.cs; the shadow itself was worked
// out by clouds.glsl, from points on the ground round the camera, as how much cloud lies towards the sun and between what
// heights). The scene was drawn with the sun already dimmed by the cloud above the craft. For each pixel the place drawn
// there is found from its depth, followed along the sun's line down to that ground, and its light put right for the cloud
// above it instead: darker in a shadow the craft is not in, lighter in a gap of sunlight when the craft is in shade, each by
// the sun's share of the light on that face (none for a face turned from the sun). Further off, the pattern fades into the
// craft's own light, under the air's.
uniform vec4 _ZBufferParams;
uniform vec4 _CloudRight;    // xyz: the camera's right, times the tangent of half its width of view; w: 1 if its depth picture can be used
uniform vec4 _CloudUp;       // xyz: the camera's up, times the tangent of half its height of view
uniform vec4 _CloudAhead;    // xyz: the way the camera looks; w: 1 if depths run backwards (nearest is 1)
uniform vec4 _CloudCam;      // xyz: the camera, from the planet's centre (m, the scene's axes); w: the planet's radius (m)
uniform vec4 _CloudSun;      // xyz: towards the sun (the scene's axes)
uniform vec4 _CloudShadeAt;  // xyz: the middle of the shadow's square, from the planet's centre (the scene's axes, m); w: its side (m)
uniform vec4 _CloudShadeX;   // xyz: the square's two sides, in the scene's axes; w (X): the height of the ground it lies on (m above the radius)
uniform vec4 _CloudShadeY;
uniform vec4 _CloudShade;    // x: how much brighter the sun makes what faces it straight on than the sky alone (about 1.7 by day); y: how far off the pattern has faded to half (m); z: the least the camera draws (m); w: the most
uniform vec4 _CloudShadeSun; // x: what the sun was dimmed by for the whole scene (the cloud above the craft), 0 to 1; y: 1 if the world has a sea
uniform sampler2D _CloudShadowMap;   // r: how much cloud lies towards the sun; g, b: the lowest and highest of it (km above the radius)
uniform sampler2D _CameraDepthTexture;
in vec2 vs_TEXCOORD0;
out vec4 SV_Target0;

void main()
{
    vec2 uv = vs_TEXCOORD0;
    if (_CloudRight.w < 0.5) discard;
    float z = textureLod(_CameraDepthTexture, uv, 0.0).r;
    bool nothing = _CloudAhead.w > 0.5 ? z <= 0.000001 : z >= 0.999999;
    if (nothing) discard;
    vec3 dir = normalize(_CloudAhead.xyz + _CloudRight.xyz * (uv.x * 2.0 - 1.0) + _CloudUp.xyz * (uv.y * 2.0 - 1.0));
    float t = 1.0 / (_ZBufferParams.z * z + _ZBufferParams.w) / max(dot(dir, _CloudAhead.xyz), 1e-4);
    // (the sea is drawn by Scatterer after the depth picture is made, which holds the seabed under it: where the line of sight
    // meets the sea's surface first, that is what is shaded)
    if (_CloudShadeSun.y > 0.5)
    {
        float b = dot(_CloudCam.xyz, dir), c = dot(_CloudCam.xyz, _CloudCam.xyz) - _CloudCam.w * _CloudCam.w;
        float disc = b * b - c;
        if (disc > 0.0 && c > 0.0)
        {
            float sea = -b - sqrt(disc);
            if (sea > 0.0 && sea < t) t = sea;
        }
    }
    if (t < _CloudShade.z || t > _CloudShade.w) discard;
    vec3 near = dir * t;                       // from the camera (small numbers: their differences are clean)
    vec3 p = _CloudCam.xyz + near;             // from the planet's centre
    float R = _CloudCam.w;
    float r = length(p);
    vec3 up = p / max(r, 1.0);
    vec3 sun = _CloudSun.xyz;
    float sunUp = dot(up, sun);
    if (sunUp < -0.03) discard;
    // Which way the surface faces (from how the place changes from one pixel to the next, turned to face the camera).
    vec3 facing = cross(dFdx(near), dFdy(near));
    float size = length(facing);
    facing = size > 1e-10 ? facing / size : -dir;
    if (dot(facing, dir) > 0.0) facing = -facing;
    float ndl = dot(facing, sun);
    if (ndl <= 0.0) discard;
    // Down the sun's line to the ground the shadow was worked out on.
    float h = r - R;
    vec3 g = p - sun * ((h - _CloudShadeX.w) / max(sunUp, 0.06));
    vec3 rel = g - _CloudShadeAt.xyz;
    vec2 st = vec2(dot(rel, _CloudShadeX.xyz), dot(rel, _CloudShadeY.xyz)) / _CloudShadeAt.w + 0.5;
    if (st.x <= 0.0 || st.x >= 1.0 || st.y <= 0.0 || st.y >= 1.0) discard;
    vec4 m = textureLod(_CloudShadowMap, st, 0.0);
    float lowest = m.g * 1000.0, highest = m.b * 1000.0;
    // (only the cloud above this place takes its sun: a craft flying between two layers is shaded by the upper one alone)
    float above = m.r * clamp((highest - h) / max(highest - lowest, 60.0), 0.0, 1.0);
    // (the edge of the square fades, and so does a far shadow, into the light the craft is in)
    vec2 edge = min(st, 1.0 - st);
    float fade = clamp(min(edge.x, edge.y) * 12.0, 0.0, 1.0) * _CloudShade.y / (_CloudShade.y + t);
    float through = mix(_CloudShadeSun.x, exp(-above), fade);
    // The sun's share of the light on this face, as it would be undimmed; the light as drawn, and as it should be.
    float k = _CloudShade.x * ndl;
    float share = k / (k + 1.0);
    float should = 1.0 - share + share * through, drawn = 1.0 - share + share * _CloudShadeSun.x;
    float factor = clamp(should / max(drawn, 0.05), 0.0, 2.0);
    if (!(abs(factor - 1.0) > 0.004)) discard;
    // (drawn doubled: what is there is multiplied by twice this, so it can be lightened as well as darkened)
    SV_Target0 = vec4(vec3(factor * 0.5), 1.0);
}
#endif
