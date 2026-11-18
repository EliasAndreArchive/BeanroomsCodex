#version 430 core
in vec2 uv;
out vec4 fragColor;
uniform sampler2D accumulation;
uniform sampler2D bloomTexture;
uniform sampler2D exposureTexture;
uniform int postEnabled;
uniform int autoExposure;
uniform int bloomEnabled;
uniform int tonemapMode;
uniform float exposure;
uniform float bloomStrength;
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
    ivec2 size=textureSize(accumulation,0);
    ivec2 pixel=ivec2(gl_FragCoord.xy);
    vec3 c=texelFetch(accumulation,pixel,0).rgb;
    float exposureScale = postEnabled != 0 ? exposure * (autoExposure != 0 ? texelFetch(exposureTexture,ivec2(0),0).r : 1.0) : 1.0;
    c *= exposureScale;
    // Sample bloom once per game pixel so its smooth filter cannot vary inside an enlarged pixel.
    vec2 gamePixelUv = (vec2(pixel)+0.5)/vec2(size);
    if (postEnabled != 0 && bloomEnabled != 0) c += texture(bloomTexture,gamePixelUv).rgb*bloomStrength*exposureScale;
    if (postEnabled == 0) c=clamp(c,0.0,1.0);
    else if (tonemapMode == 2) c=acesTonemap(c);
    else if (tonemapMode == 3) c=neutralTonemap(c);
    else c=clamp(c,0.0,1.0);
    c = pow(clamp(c,0.0,1.0),vec3(1.0/2.2));
    fragColor=vec4(c,1.0);
}
