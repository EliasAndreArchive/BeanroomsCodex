#version 430 core
in vec2 uv;
out vec4 fragColor;
uniform sampler2D accumulation;
uniform sampler2D overlayTexture;
uniform vec2 windowSize;
uniform int ditheringEnabled;
uniform int paletteReduction;
uniform int paletteSteps;
uniform float transitionDark;
uniform float transitionFlash;
uniform int menuMode;
uniform int menuBlur;
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
vec3 scenePixel(ivec2 pixel,ivec2 size) {
    return texelFetch(accumulation,clamp(pixel,ivec2(0),size-1),0).rgb;
}
vec3 blurredScene(ivec2 pixel,ivec2 size) {
    vec3 color=scenePixel(pixel,size)*0.16;
    color+=(scenePixel(pixel+ivec2(2,0),size)+scenePixel(pixel+ivec2(-2,0),size)+
        scenePixel(pixel+ivec2(0,2),size)+scenePixel(pixel+ivec2(0,-2),size))*0.12;
    color+=(scenePixel(pixel+ivec2(2,2),size)+scenePixel(pixel+ivec2(-2,2),size)+
        scenePixel(pixel+ivec2(2,-2),size)+scenePixel(pixel+ivec2(-2,-2),size))*0.09;
    return color;
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
    if(menuMode != 0) {
        ivec2 sceneSize=textureSize(accumulation,0);
        ivec2 sceneCoord=clamp(ivec2(imageUv*vec2(sceneSize)),ivec2(0),sceneSize-1);
        vec3 background=blurredScene(sceneCoord,sceneSize)*0.82;
        ivec2 overlaySize = textureSize(overlayTexture,0);
        ivec2 overlayPixel = clamp(ivec2(imageUv.x*float(overlaySize.x),
            (1.0-imageUv.y)*float(overlaySize.y)),ivec2(0),overlaySize-1);
        vec4 hud=texelFetch(overlayTexture,overlayPixel,0);
        fragColor=vec4(mix(background,hud.rgb,hud.a),1.0);
        return;
    }
    ivec2 size = textureSize(accumulation,0);
    ivec2 pixel = clamp(ivec2(imageUv * vec2(size)),ivec2(0),size-1);
    vec3 c = menuBlur != 0 ? blurredScene(pixel,size) : scenePixel(pixel,size);
    if (paletteReduction != 0) {
        float threshold = (float(bayer8[(pixel.y & 7)*8 + (pixel.x & 7)])+0.5)/64.0;
        float levels = float(max(paletteSteps-1,1));
        float dither = ditheringEnabled != 0 ? (threshold-0.5)*0.825/levels : 0.0;
        c = floor(clamp(c+vec3(dither),0.0,1.0)*levels+0.5)/levels;
    }
    c *= 1.0-clamp(transitionDark,0.0,1.0);
    c = mix(c,vec3(1.0),clamp(transitionFlash,0.0,1.0));
    if(menuBlur != 0) c*=0.72;
    ivec2 overlaySize = textureSize(overlayTexture,0);
    ivec2 overlayPixel = clamp(ivec2(imageUv.x*float(overlaySize.x),
        (1.0-imageUv.y)*float(overlaySize.y)),ivec2(0),overlaySize-1);
    vec4 hud = texelFetch(overlayTexture,overlayPixel,0);
    fragColor = vec4(mix(c,hud.rgb,hud.a),1.0);
}
