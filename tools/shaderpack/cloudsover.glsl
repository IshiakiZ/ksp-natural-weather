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
// The clouds put on the picture (Natural Weather's Clouds.cs; clouds.glsl drew them, small, and took them together over
// frames). Each pixel takes the four nearest of the small picture, leaving out any whose cloud lies behind what is drawn at
// this pixel (a mountain, a building, the craft): so a ridge in front of a cloud keeps its edge. The colour is worked out
// as light and turned into the game's own way of showing colours at the end, and laid over what is there by how much of it
// the cloud hides.
uniform vec4 _ZBufferParams;
uniform vec4 _CloudRight;    // xyz: the camera's right, times the tangent of half its width of view; w: 1 if its depth picture can be used
uniform vec4 _CloudUp;       // xyz: the camera's up, times the tangent of half its height of view
uniform vec4 _CloudAhead;    // xyz: the way the camera looks; w: 1 if depths run backwards (nearest is 1)
uniform vec4 _CloudOver;     // x: the power that turns light into shown colour (1/2.2); y: how much of the clouds to show (0 to 1); z: how much brighter (the eye); w: unused
uniform sampler2D _CloudHist;    // the clouds, small: their light, and how much shows through them
uniform sampler2D _CloudHistD;   // and how far off each is
uniform sampler2D _CameraDepthTexture;
in vec2 vs_TEXCOORD0;
out vec4 SV_Target0;

void main()
{
    vec2 uv = vs_TEXCOORD0;
    vec3 dir = normalize(_CloudAhead.xyz + _CloudRight.xyz * (uv.x * 2.0 - 1.0) + _CloudUp.xyz * (uv.y * 2.0 - 1.0));
    float scene = 1e9;
    if (_CloudRight.w > 0.5)
    {
        float z = textureLod(_CameraDepthTexture, uv, 0.0).r;
        bool nothing = _CloudAhead.w > 0.5 ? z <= 0.000001 : z >= 0.999999;
        if (!nothing) scene = 1.0 / (_ZBufferParams.z * z + _ZBufferParams.w) / max(dot(dir, _CloudAhead.xyz), 1e-4);
    }
    vec2 size = vec2(textureSize(_CloudHist, 0));
    vec2 at = uv * size - 0.5;
    vec2 f = fract(at);
    vec2 corner = floor(at);
    vec4 sum = vec4(0.0);
    float weights = 0.0;
    for (int j = 0; j < 2; j++)
        for (int i = 0; i < 2; i++)
        {
            vec2 tap = (corner + vec2(float(i), float(j)) + 0.5) / size;
            float w = (i == 0 ? 1.0 - f.x : f.x) * (j == 0 ? 1.0 - f.y : f.y) + 1e-4;
            vec4 c = textureLod(_CloudHist, tap, 0.0);
            float d = textureLod(_CloudHistD, tap, 0.0).r * 1000.0;     // (kept in kilometres)
            // (a cloud whose light comes from beyond what is drawn here is hidden by it)
            if (d > 0.0 && d > scene * 1.03 + 30.0) c = vec4(0.0, 0.0, 0.0, 1.0);
            sum += c * w;
            weights += w;
        }
    vec4 cloud = sum / weights;
    float cover = clamp((1.0 - cloud.a) * _CloudOver.y, 0.0, 1.0);
    if (cover < 0.002) discard;
    vec3 colour = max(cloud.rgb, vec3(0.0)) / max(1.0 - cloud.a, 1e-3) * _CloudOver.z;
    // (a soft shoulder above 0.7: the far camera's picture holds nothing brighter than white, and a sunlit top cut off flat
    // there loses all its modelling; rolled over, its brighter and dimmer billows still show)
    vec3 over = max(colour - 0.7, vec3(0.0));
    colour = min(colour, vec3(0.7)) + 0.3 * (1.0 - exp(-over / 0.3));
    colour = pow(colour, vec3(_CloudOver.x));
    SV_Target0 = vec4(colour * cover, cover);
}
#endif
