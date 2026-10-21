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
uniform vec4 _CloudShadeSun; // x: what the sun was dimmed by for the whole scene (the cloud above the craft), 0 to 1; y: 1 if the world has a sea; z: how far the camera is above all the clouds (0 to 1: from there the walk shades the ground itself, and this pass by one less as much)
uniform vec4 _CloudWet;      // x: how wet the ground is round the camera (0 to 1: rain has fallen there); y: how far off that still holds (m)
uniform vec4 _CloudShadeTint; // rgb: the sun's light over the sky's in each colour, against green's (the sky's light is bluer than the sun's); w: 1 if given
uniform sampler2D _CloudShadowMap;   // r: how much cloud lies towards the sun; g, b: the lowest and highest of it (km above the radius)
uniform sampler2D _CameraDepthTexture;
uniform sampler2D _CloudHistD;       // the clouds' distance picture as taken together this frame: g, how much the walk's own shadow from above the clouds takes from the land's light there (clouds.glsl)
in vec2 vs_TEXCOORD0;

// The walk's shadow from above the clouds, put right colour by colour. The walk darkens the land itself, by the alpha of what it
// lays over the picture: one number for the three colours, the sun's share of the light in the game's numbers (k / (k + 1), k the
// sun's light over the sky's on level ground) times how much of the sun the clouds take. So the shade from above was the sunlit
// land's own colour darkened, an olive hole in the grass, as the square's shadow below was until 2026-10-19. Here what is there
// is multiplied by what each colour's own share leaves over what the grey one will (the same tint as below): the blue darkened
// less, the red more. 1 where the walk darkens nothing.
vec3 aboveTint(float dark, float sunUp)
{
    if (dark < 0.0005 || _CloudShadeTint.w < 0.5 || sunUp <= 0.0) return vec3(1.0);
    float k = 1.25 * clamp(sunUp * 2.5 + 0.15, 0.0, 1.0) * sunUp;
    vec3 kc = pow((pow(k + 1.0, 2.2) - 1.0) * _CloudShadeTint.rgb + 1.0, vec3(1.0 / 2.2)) - 1.0;
    vec3 ratio = (kc / (kc + 1.0)) / max(k / (k + 1.0), 1e-4);
    return max(1.0 - dark * ratio, 0.0) / max(1.0 - dark, 0.02);
}
out vec4 SV_Target0;

void main()
{
    vec2 uv = vs_TEXCOORD0;
    if (_CloudRight.w < 0.5) discard;
    float z = textureLod(_CameraDepthTexture, uv, 0.0).r;
    // (Nothing drawn here by this camera: the depth exactly as it was cleared. Not "nearly": on OpenGL the near camera sees
    // to 400 m, and the last millionth of its depth is the ground from 399.2 m to 400 m, which got no shadow from this
    // camera's pass and none from the far camera's, whose ground it covers: a pale line across shaded ground.)
    bool nothing = _CloudAhead.w > 0.5 ? z <= 0.0 : z >= 1.0;
    // (from above the clouds: how much the walk's own shadow takes from the land here)
    float dark = _CloudShadeSun.z > 0.0 ? textureLod(_CloudHistD, uv, 0.0).g : 0.0;
    if (nothing)
    {
        // (Where this camera drew nothing, high up, the world is drawn small by the game, and the walk shades it as the ball of
        // the planet's radius: tinted there as well, by the far camera's pass alone, and nothing else done.)
        if (dark < 0.0005 || _CloudShade.w < 1e8) discard;
        vec3 sight = normalize(_CloudAhead.xyz + _CloudRight.xyz * (uv.x * 2.0 - 1.0) + _CloudUp.xyz * (uv.y * 2.0 - 1.0));
        float b = dot(_CloudCam.xyz, sight), c = dot(_CloudCam.xyz, _CloudCam.xyz) - _CloudCam.w * _CloudCam.w;
        if (c <= 0.0 || b * b - c <= 0.0) discard;
        vec3 ball = _CloudCam.xyz + sight * (-b - sqrt(b * b - c));
        vec3 tinted = aboveTint(dark, dot(normalize(ball), _CloudSun.xyz));
        if (!(max(max(abs(tinted.r - 1.0), abs(tinted.g - 1.0)), abs(tinted.b - 1.0)) > 0.004)) discard;
        SV_Target0 = vec4(tinted * 0.5, 1.0);
        return;
    }
    vec3 dir = normalize(_CloudAhead.xyz + _CloudRight.xyz * (uv.x * 2.0 - 1.0) + _CloudUp.xyz * (uv.y * 2.0 - 1.0));
    // (how far off what is drawn here is, along the camera's axis, which is how a camera's sight is measured; and along the line of sight)
    float drawn = 1.0 / (_ZBufferParams.z * z + _ZBufferParams.w);
    float t = drawn / max(dot(dir, _CloudAhead.xyz), 1e-4);
    // (the sea is drawn by Scatterer after the depth picture is made, which holds the seabed under it: where the line of sight
    // meets the sea's surface first, that is what is shaded)
    bool sea = false;
    if (_CloudShadeSun.y > 0.5)
    {
        float b = dot(_CloudCam.xyz, dir), c = dot(_CloudCam.xyz, _CloudCam.xyz) - _CloudCam.w * _CloudCam.w;
        float disc = b * b - c;
        if (disc > 0.0 && c > 0.0)
        {
            float seaAt = -b - sqrt(disc);
            if (seaAt > 0.0 && seaAt < t) { t = seaAt; sea = true; }
        }
    }
    // (Each camera's pass shades what that camera has drawn, whatever of it there is: a camera's sight is measured along
    // its axis, not along the line of sight. Measured along the line of sight, as this first was, ground 300 m to 400 m
    // off towards the sides of the picture was past the near camera's "400 m" and not yet the far camera's: a pale patch
    // each side of the picture where the two hand over. And it is the thing drawn that is asked about, not the sea's
    // surface over it: a seabed the far camera drew is shaded by the far camera's pass though the water over it is the
    // near camera's, or shallows lying across the hand-over had an unshaded wedge of bottom.)
    if (drawn < _CloudShade.z || drawn > _CloudShade.w) discard;
    vec3 near = dir * t;                       // from the camera (small numbers: their differences are clean)
    vec3 p = _CloudCam.xyz + near;             // from the planet's centre
    float R = _CloudCam.w;
    float r = length(p);
    vec3 up = p / max(r, 1.0);
    vec3 sun = _CloudSun.xyz;
    float sunUp = dot(up, sun);
    // Which way the surface faces (from how the place changes from one pixel to the next, turned to face the camera).
    vec3 facing = cross(dFdx(near), dFdy(near));
    float size = length(facing);
    facing = size > 1e-10 ? facing / size : -dir;
    if (dot(facing, dir) > 0.0) facing = -facing;
    // ---- wet ground is darker ground: water fills the roughness that scattered the light back (three tenths less, on
    // what faces the sky; walls less; the sea not at all), day and night, as far off as the wet is known
    float wet = 1.0;
    if (_CloudWet.x > 0.01 && !sea)
        wet = 1.0 - 0.3 * _CloudWet.x * smoothstep(0.15, 0.7, dot(facing, up)) * (1.0 - smoothstep(0.5, 1.0, t / max(_CloudWet.y, 1.0)));
    // ---- the clouds' shadow
    vec3 factor = vec3(1.0);
    for (int lit = sunUp >= -0.03 ? 0 : 1; lit < 1; lit++)
    {
        float ndl = dot(facing, sun);
        if (ndl <= 0.0) break;
        // Down the sun's line to the ground the shadow was worked out on.
        float h = r - R;
        vec3 g = p - sun * ((h - _CloudShadeX.w) / max(sunUp, 0.06));
        vec3 rel = g - _CloudShadeAt.xyz;
        vec2 st = vec2(dot(rel, _CloudShadeX.xyz), dot(rel, _CloudShadeY.xyz)) / _CloudShadeAt.w + 0.5;
        if (st.x <= 0.0 || st.x >= 1.0 || st.y <= 0.0 || st.y >= 1.0) break;
        vec4 m = textureLod(_CloudShadowMap, st, 0.0);
        float lowest = m.g * 1000.0, highest = m.b * 1000.0;
        // (only the cloud above this place takes its sun: a craft flying between two layers is shaded by the upper one alone)
        float above = m.r * clamp((highest - h) / max(highest - lowest, 60.0), 0.0, 1.0);
        // (the edge of the square fades, and so does a far shadow, into the light the craft is in)
        vec2 edge = min(st, 1.0 - st);
        float fade = clamp(min(edge.x, edge.y) * 12.0, 0.0, 1.0) * _CloudShade.y / (_CloudShade.y + t) * (1.0 - _CloudShadeSun.z);
        float through = mix(_CloudShadeSun.x, exp(-above), fade);
        // The sun's share of the light on this face, as it would be undimmed; the light as drawn, and as it should be.
        // (Each colour by its own. What a shadow leaves is the sky's light, and the sky's light is bluer than the sun's: in shade
        // the blue keeps more of itself than the red, and shade is a cool grey-blue, not the sunlit colour darkened. The share
        // is the game's, in its numbers, as before, for green; for red and blue the sun's light over the sky's is as much more
        // or less as the sky's colour against the sun's has it (Clouds.cs), taken as light and turned back into numbers. Grey,
        // as until 2026-10-19, the shadows of heaps read as dark olive holes in the land from a few kilometres up.)
        float k = _CloudShade.x * ndl;
        vec3 tint = _CloudShadeTint.w > 0.5 ? _CloudShadeTint.rgb : vec3(1.0);
        vec3 kc = pow((pow(k + 1.0, 2.2) - 1.0) * tint + 1.0, vec3(1.0 / 2.2)) - 1.0;
        vec3 share = kc / (kc + 1.0);
        // (What the game drew is undone by the grey share: its own dimming of the sun under the cloud above the craft is the same in
        // every colour. Undone by each colour's own, a sunlit place seen while the craft stood in shade was given back more red
        // than blue, and the grass round it turned mustard yellow.)
        float shareGrey = k / (k + 1.0);
        vec3 should = 1.0 - share + share * through;
        float drawn = 1.0 - shareGrey + shareGrey * _CloudShadeSun.x;
        factor = clamp(should / max(drawn, 0.05), 0.0, 2.0);
    }
    factor *= wet;
    factor *= aboveTint(dark, sunUp);
    if (!(max(max(abs(factor.r - 1.0), abs(factor.g - 1.0)), abs(factor.b - 1.0)) > 0.004)) discard;
    // (drawn doubled: what is there is multiplied by twice this, so it can be lightened as well as darkened)
    SV_Target0 = vec4(factor * 0.5, 1.0);
}
#endif
