#version 430 core
in vec2 uv;
out vec4 fragColor;
uniform sampler2D sceneTex;
uniform float strength;
uniform int radius;

// Current-frame edge-aware filter. It uses no accumulated history, so camera
// motion cannot leave a trail. The source remains linear HDR for bloom and tone mapping.
void main() {
    ivec2 size=textureSize(sceneTex,0);
    ivec2 pixel=clamp(ivec2(uv*vec2(size)),ivec2(0),size-1);
    vec3 center=texelFetch(sceneTex,pixel,0).rgb;
    float centerLuma=dot(center,vec3(0.2126,0.7152,0.0722));
    vec3 sum=center;
    float total=1.0;
    int reach=clamp(radius,1,2);
    for(int y=-2;y<=2;y++) for(int x=-2;x<=2;x++) {
        if((x==0 && y==0) || abs(x)>reach || abs(y)>reach) continue;
        ivec2 neighbor=clamp(pixel+ivec2(x,y),ivec2(0),size-1);
        vec3 sampleColor=texelFetch(sceneTex,neighbor,0).rgb;
        float sampleLuma=dot(sampleColor,vec3(0.2126,0.7152,0.0722));
        float contrast=abs(sampleLuma-centerLuma)/(0.12+max(centerLuma,sampleLuma));
        float spatial=exp(-float(x*x+y*y)/(reach==1?1.4:3.2));
        float edge=exp(-contrast*contrast*18.0);
        float weight=spatial*edge;
        sum+=sampleColor*weight;
        total+=weight;
    }
    fragColor=vec4(mix(center,sum/total,clamp(strength,0.0,1.0)),1.0);
}
