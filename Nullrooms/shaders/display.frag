#version 430 core
in vec2 uv;
out vec4 fragColor;
uniform sampler2D accumulation;
uniform sampler2D bloomTexture;
uniform sampler2D exposureTexture;
uniform sampler2D overlayTexture;
uniform vec2 windowSize;
uniform int ditheringEnabled;
uniform int paletteReduction;
uniform int temperature;
uniform int paletteSteps;
uniform int autoExposure;
uniform int postEnabled;
uniform int bloomEnabled;
uniform float bloomStrength;
uniform int tonemapMode;
uniform float exposure;
uniform float saturation;
uniform float contrast;
uniform vec3 colorMix;
uniform float transitionDark;
uniform float transitionFlash;
const int bayer8[64] = int[64](
     0,48,12,60, 3,51,15,63,
    32,16,44,28,35,19,47,31,
     8,56, 4,52,11,59, 7,55,
    40,24,36,20,43,27,39,23,
     2,50,14,62, 1,49,13,61,
    34,18,46,30,33,17,45,29,
    10,58, 6,54, 9,57, 5,53,
    42,26,38,22,41,25,37,21
);
// Hable's Uncharted 2 filmic curve: filmicworlds.com/blog/filmic-tonemapping-operators/
vec3 hableCurve(vec3 x) {
    return ((x*(0.15*x+0.05)+0.004)/(x*(0.15*x+0.50)+0.06))-0.02/0.30;
}
vec3 filmicTonemap(vec3 x) {
    return clamp(hableCurve(x*2.0)/hableCurve(vec3(11.2)),0.0,1.0);
}
// Narkowicz's ACES fit: knarkowicz.wordpress.com/2016/01/06/aces-filmic-tone-mapping-curve/
vec3 acesTonemap(vec3 x) {
    return clamp((x*(2.51*x+0.03))/(x*(2.43*x+0.59)+0.14),0.0,1.0);
}
// Adapted from Khronos PBR Neutral (Apache-2.0); renamed and clamped input for Beanrooms.
// github.com/KhronosGroup/ToneMapping/PBR_Neutral
vec3 neutralTonemap(vec3 color) {
    color=max(color,vec3(0.0));
    float x=min(color.r,min(color.g,color.b));
    float offset=x<0.08 ? x-6.25*x*x : 0.04;
    color-=offset;
    float peak=max(color.r,max(color.g,color.b));
    float startCompression=0.76;
    if(peak<startCompression) return color;
    float d=1.0-startCompression;
    float newPeak=1.0-d*d/(peak+d-startCompression);
    color*=newPeak/peak;
    float g=1.0-1.0/(0.15*(peak-newPeak)+1.0);
    return mix(color,vec3(newPeak),g);
}
void main() {
    vec2 contentSize = windowSize;
    if (windowSize.x * 3.0 > windowSize.y * 4.0) contentSize.x = windowSize.y * (4.0/3.0);
    else contentSize.y = windowSize.x * (3.0/4.0);
    vec2 contentPosition = gl_FragCoord.xy - (windowSize-contentSize)*0.5;
    if (any(lessThan(contentPosition,vec2(0.0))) || any(greaterThanEqual(contentPosition,contentSize))) {
        fragColor=vec4(0.0,0.0,0.0,1.0);
        return;
    }
    vec2 imageUv = contentPosition/contentSize;
    ivec2 size = textureSize(accumulation,0);
    ivec2 pixel = clamp(ivec2(imageUv * vec2(size)),ivec2(0),size-1);
    vec3 c = texelFetch(accumulation,pixel,0).rgb;
    float exposureScale = postEnabled != 0 ? exposure * (autoExposure != 0 ? texelFetch(exposureTexture,ivec2(0),0).r : 1.0) : 1.0;
    c *= exposureScale;
    // Sample bloom once per game pixel so its smooth filter cannot vary inside an enlarged pixel.
    vec2 gamePixelUv = (vec2(pixel)+0.5)/vec2(size);
    if (postEnabled != 0 && bloomEnabled != 0) c += texture(bloomTexture,gamePixelUv).rgb*bloomStrength*exposureScale;
    if (postEnabled == 0) c=clamp(c,0.0,1.0);
    else if (tonemapMode == 1) c=filmicTonemap(c);
    else if (tonemapMode == 2) c=acesTonemap(c);
    else if (tonemapMode == 3) c=neutralTonemap(c);
    else c=clamp(c,0.0,1.0);
    c = pow(clamp(c,0.0,1.0),vec3(1.0/2.2));
    if (postEnabled != 0) {
        float warmth = clamp(float(temperature)/100.0,-1.0,1.0);
        c *= vec3(1.0+0.45*warmth,1.0+0.10*warmth,1.0-0.55*warmth) * colorMix;
        float luminance = dot(c,vec3(0.2126,0.7152,0.0722));
        c = vec3(luminance) + (c-vec3(luminance))*saturation;
        c = clamp((c-0.5)*contrast+0.5,0.0,1.0);
    }
    if (paletteReduction != 0) {
        float threshold = (float(bayer8[(pixel.y & 7)*8 + (pixel.x & 7)])+0.5)/64.0;
        float levels = float(max(paletteSteps-1,1));
        float dither = ditheringEnabled != 0 ? (threshold-0.5)*0.825/levels : 0.0;
        c = floor(clamp(c+vec3(dither),0.0,1.0)*levels+0.5)/levels;
    }
    c *= 1.0-clamp(transitionDark,0.0,1.0);
    c = mix(c,vec3(1.0),clamp(transitionFlash,0.0,1.0));
    ivec2 overlaySize = textureSize(overlayTexture,0);
    ivec2 overlayPixel = clamp(ivec2(imageUv.x*float(overlaySize.x),
        (1.0-imageUv.y)*float(overlaySize.y)),ivec2(0),overlaySize-1);
    vec4 hud = texelFetch(overlayTexture,overlayPixel,0);
    fragColor = vec4(mix(c,hud.rgb,hud.a),1.0);
}
