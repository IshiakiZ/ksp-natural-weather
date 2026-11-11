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
uniform vec4 _CloudOver;     // x: the power that turns light into shown colour (1/2.2); y: how much of the clouds to show (0 to 1); z: how much brighter (the eye); w: 1 where this camera's veil of weather (Natural Light's) was laid on before the clouds, and they take it here: Camera 00's (the only camera on Direct3D; on OpenGL the near one, whose clouds are laid on within some hundreds of metres of their shell); 0 for Camera 01's, which its own veil covers after them; and 2 more for setting one against the other: the four dots taken alike whatever stood in front of them (as until 2026-11-04); and 4 more for the second form's clouds: the shoulder from 0.82 (brighter tops); and 8 more for the second form's shoulder rolled over by a colour's brightest channel, its hue kept, where it is little tinged
uniform sampler2D _CloudHist;    // the clouds, small: their light, and how much shows through them
uniform sampler2D _CloudHistD;   // and how far off each is
uniform sampler2D _CameraDepthTexture;
uniform vec4 _CloudVeilUp;          // xyz: up, at the camera, in the scene; w: the world's radius (m)
uniform vec4 _WeatherVeilAir;       // the weather in the air (Natural Light's veil, handed to every shader): x how thick (the light lost in each metre; 0: none); y its top, above the camera (m); z how soon it thins out above its top (m)
uniform vec4 _WeatherVeilColour;    // rgb: its own light, lit from all round (as it shows)
uniform vec4 _WeatherVeilSun;       // xyz: towards the sun, in the scene; w: how strongly what is in the air throws light forward
uniform vec4 _WeatherVeilSunColour; // rgb: what the sun adds to it looking straight towards the sun (as it shows); a: how much of the sunlight its top throws back up
in vec2 vs_TEXCOORD0;
out vec4 SV_Target0;

// The weather along a stretch of line that lies above the weather's top, from a to b metres out, counted in metres of the
// weather as thick as it is under its top: the veil's own sum (veil.glsl, which says why it is summed so; three times wrong
// before, 2026-10-25). On a round world the line's height over the camera after s metres is mu s + s^2 / 2R, lowest at
// s = -mu R: six pieces bunched towards that lowest point from the end it is at, or six each side of it where it is inside,
// each straight between the line's own heights over the top at its two ends (both at least 0, so every power of e is at
// most 1), the weather summed exactly along each.
float above(float a, float b, float mu, float top, float soft, float R)
{
    float low = clamp(-mu * R, a, b);
    bool both = low > a && low < b;
    float hLow = max(mu * low + low * low / (2.0 * R) - top, 0.0), eLow = exp(-hLow / soft);
    float sum = 0.0, sWas = low, hWas = hLow, eWas = eLow;
    for (int i = 1; i <= 12; i++)
    {
        float to, t;
        if (both)
        {
            to = i <= 6 ? a : b;
            t = float(i <= 6 ? i : i - 6) / 6.0;
            if (i == 7) { sWas = low; hWas = hLow; eWas = eLow; }
        }
        else
        {
            if (i > 6) break;
            to = low <= a ? b : a;
            t = float(i) / 6.0;
        }
        float s = low + (to - low) * t * t;
        float h = max(mu * s + s * s / (2.0 * R) - top, 0.0);
        float e = exp(-h / soft);
        float len = abs(s - sWas);
        float k = (h - hWas) / soft;
        sum += abs(k) < 0.001 ? eWas * len * (1.0 - 0.5 * k) : len * (eWas - e) / k;
        sWas = s; hWas = h; eWas = e;
    }
    return sum;
}

// Interleaved gradient noise (Jimenez 2014): 0 to 1, a different value at each pixel of the screen, the same every frame.
float dither(vec2 p) { return fract(52.9829189 * fract(dot(p, vec2(0.06711056, 0.00583715)))); }

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
    float weights = 0.0, farSum = 0.0, hiding = 0.0;
    // (A dot of the clouds' smaller picture whose line of sight stopped at something much nearer than what this pixel sees, a
    // craft's edge, holds none of the cloud behind it: taken alike with the others, as until 2026-11-04, every pixel just
    // outside a craft's outline took a share of that nothing, and the clouds behind the craft were cut away in a band round
    // it as wide as a dot (the owner, 2026-11-04: "an invisible thing that cuts out the background clouds ... around the
    // rocket, there's a very thin line that doesn't render"). So each dot counts for little where what stood before it, as the
    // walk saw it (the nearest of the picture's depths there), is much nearer than this pixel's.)
    bool edges = mod(floor(_CloudOver.w / 2.0), 2.0) < 0.5;
    for (int j = 0; j < 2; j++)
        for (int i = 0; i < 2; i++)
        {
            vec2 tap = (corner + vec2(float(i), float(j)) + 0.5) / size;
            float w = (i == 0 ? 1.0 - f.x : f.x) * (j == 0 ? 1.0 - f.y : f.y) + 1e-4;
            if (edges && _CloudRight.w > 0.5)
            {
                float zt = textureLod(_CameraDepthTexture, tap, 0.0).r;
                bool none = _CloudAhead.w > 0.5 ? zt <= 0.000001 : zt >= 0.999999;
                float before = none ? 1e9 : 1.0 / (_ZBufferParams.z * zt + _ZBufferParams.w) / max(dot(dir, _CloudAhead.xyz), 1e-4);
                if (before < scene * 0.7 - 20.0) w *= 0.01;
            }
            vec4 c = textureLod(_CloudHist, tap, 0.0);
            float d = textureLod(_CloudHistD, tap, 0.0).r * 1000.0;     // (kept in kilometres)
            // (a cloud whose light comes from beyond what is drawn here is hidden by it)
            if (d > 0.0 && d > scene * 1.03 + 30.0) c = vec4(0.0, 0.0, 0.0, 1.0);
            sum += c * w;
            weights += w;
            // (and how far off the cloud here is, each of the four by how much of it it hides)
            if (d > 0.0)
            {
                float hides = max(1.0 - c.a, 0.0) * w;
                farSum += d * hides;
                hiding += hides;
            }
        }
    vec4 cloud = sum / weights;
    float cover = clamp((1.0 - cloud.a) * _CloudOver.y, 0.0, 1.0);
    if (cover < 0.002) discard;
    vec3 colour = max(cloud.rgb, vec3(0.0)) / max(1.0 - cloud.a, 1e-3) * _CloudOver.z;
    // (a soft shoulder above 0.7: the far camera's picture holds nothing brighter than white, and a sunlit top cut off flat
    // there loses all its modelling; rolled over, its brighter and dimmer billows still show)
    // (Thin cloud that is brighter than white: a veil of ice or a heap's edge near the line to the sun, which throw the
    // sun's light on towards the eye. Laid over the picture as a colour, as all cloud was until 2026-10-13, the brightest
    // a thin cloud could make a pixel was white times the little it hides: a veil in front of the sun was milk, with no
    // glare round the sun and nothing of the sun's ring. What it has beyond white is added to the picture instead, where
    // the cloud is thin (a thick cloud's sunlit top is a surface, and keeps the shoulder below).)
    vec3 beyond = max(colour - 1.0, vec3(0.0)) * (0.7 * cover * (1.0 - cover) * (1.0 - cover));
    // (The second form's clouds, after the owner's spec of Blackrack's (2026-11-04, research/clouds-r5/spec-gaps-2026-11-04.md,
    // item 8): the shoulder from 0.82, _CloudOver.w's 4. His brightest tenth of a heap at the pad stands at about 215 of 255 and
    // ours, rolled over from 0.7, at about 200: tops greyed where his are white. The middle, under 0.7, is the same either way.)
    float knee = mod(floor(_CloudOver.w / 4.0), 2.0) > 0.5 ? 0.82 : 0.7;
    vec3 over = max(colour - knee, vec3(0.0));
    vec3 byChannel = min(colour, vec3(knee)) + (1.0 - knee) * (1.0 - exp(-over / (1.0 - knee)));
    if (mod(floor(_CloudOver.w / 8.0), 2.0) > 0.5)
    {
        // (The second form's, _CloudOver.w's 8 (2026-11-08): a colour little tinged rolled over by its brightest channel and the
        // others in proportion, so that its hue is kept; a strongly tinged one by each channel by itself as before, wholly from
        // a tinge (its brightest channel less its dimmest, against its brightest) of 0.3, from 0.15 on. Each channel by itself,
        // a sunlit top's warmth went as it neared white: the clouds' brightest tenth all but grey, red less blue +1 to +3 of
        // 255 in the five views with clouds where Blackrack's are +5 to +15, his tops cream; and the sun made warmer did not
        // show (its green and blue 0.93 and 0.84 of its red moved nothing; halved, they warmed the pad's brightest tenth by ten
        // and dimmed it by twelve). The hue kept everywhere, the tops +7 to +13, but the far bands near the horizon salmon
        // (high_layer's: of its bright pixels 9 % warm and tinged where his are 0.03 %, red less blue +48 at the 99th
        // percentile against his +22): their own colour is strongly tinged, and the shoulder by channel greys it toward
        // lilac. As it is, the tops +4 to +7, and as few of the bright pixels tinged as before (0 to 1.2 %, before 0 to 1.3 %;
        // the warmest 1 % +11 to +19, his +13 to +22; Direct3D stills, recordings/2026-11-08-pc/hue-d3d, hue2-d3d).
        // Clouds.HueKept false sets it back.)
        float peak = max(colour.r, max(colour.g, colour.b));
        float rolled = peak <= knee ? peak : knee + (1.0 - knee) * (1.0 - exp(-(peak - knee) / (1.0 - knee)));
        float tinged = (peak - min(colour.r, min(colour.g, colour.b))) / max(peak, 1e-6);
        colour = mix(colour * (rolled / max(peak, 1e-6)), byChannel, smoothstep(0.15, 0.3, tinged));
    }
    else
        colour = byChannel;
    colour = pow(colour, vec3(_CloudOver.x));
    // ---- the weather in the air between the eye and the cloud: rain, snow, fog, dust (Natural Light's veil: its numbers, and
    // the same sums as its own pass, veil.glsl), where this camera's veil was laid on before the clouds (_CloudOver.w). That
    // pass has taken what lies behind the cloud already, so the cloud's own colour is veiled by its own distance and laid on
    // by as much as it hides, as before: which is exactly what the veil over the whole line comes to. (Until 2026-10-25 they
    // were laid on over Camera 00's veil untouched: in 280 m of fog on the pad the ground and the sky were white and the
    // heaps over them crisp and blue, on Direct3D, where that camera is the only one.)
    if (mod(_CloudOver.w, 2.0) > 0.5 && _WeatherVeilAir.x > 0.0 && hiding > 0.0)
    {
        // (no further than the veil takes the sky behind it: Natural Light's Veil.cs)
        float far = min(farSum / hiding, 60000.0);
        float mu = dot(dir, _CloudVeilUp.xyz);
        float top = _WeatherVeilAir.y;
        float soft = max(_WeatherVeilAir.z, 1.0);
        float R = max(_CloudVeilUp.w, 1000.0);
        // (on a round world the weather's top curves away under the line of sight: veil.glsl)
        float meets = mu * mu + 2.0 * top / R;
        float tau, inLayer = 0.0;
        if (top > 0.0)
        {
            // ---- the camera in the weather: all of it until the line rises out through the top, then thinning out
            float out1 = R * (-mu + sqrt(max(meets, 0.0)));
            if (far <= out1) tau = far;
            else tau = out1 + above(out1, far, mu, top, soft, R);
        }
        else if (mu >= 0.0 || meets <= 0.0)
        {
            // ---- the camera above its top, and the line never comes down to it
            tau = above(0.0, far, mu, top, soft, R);
        }
        else
        {
            // ---- the camera above its top, the line down into the weather and (if it goes on far enough) out of it again
            float in1 = R * (-mu - sqrt(meets)), out1 = R * (-mu + sqrt(meets));
            if (far <= in1) tau = above(0.0, far, mu, top, soft, R);
            else { inLayer = min(far, out1) - in1; tau = above(0.0, in1, mu, top, soft, R) + inLayer + (far > out1 ? above(out1, far, mu, top, soft, R) : 0.0); }
        }
        float veiled = 1.0 - exp(-_WeatherVeilAir.x * tau);
        // (its light: from all round, and towards the sun more, by a Henyey-Greenstein lobe made 1 straight towards the sun;
        // and from over its top, the sunlight it throws back up, by as much of what is veiled as lies under its top)
        float g = clamp(_WeatherVeilSun.w, 0.0, 0.95);
        float c = dot(dir, _WeatherVeilSun.xyz);
        float lobe = (1.0 - g) * (1.0 - g) / pow(max(1.0 + g * g - 2.0 * g * c, 0.0001), 1.5) * (1.0 - g * g) / (1.0 + g);
        float lid = 3.0 * _WeatherVeilSunColour.a * inLayer / max(tau, 1e-3);
        colour = colour * (1.0 - veiled) + (_WeatherVeilColour.rgb + _WeatherVeilSunColour.rgb * (lobe + lid)) * veiled;
        beyond *= 1.0 - veiled;
    }
    // (and dithered by half a level of 255 each way, by how much the cloud hides: the picture is kept in 255 levels, and a glow
    // fading through cloud over hundreds of pixels came out in rings a level apart (the side-by-side's A8, deck_rain). Fixed to
    // the screen, the same every frame, so that nothing in it flickers.)
    colour += (dither(gl_FragCoord.xy) - 0.5) / 255.0;
    SV_Target0 = vec4(colour * cover + beyond, cover);
}
#endif
