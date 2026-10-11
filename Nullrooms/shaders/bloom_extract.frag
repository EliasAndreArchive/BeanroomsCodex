#version 430 core
in vec2 uv;
out vec4 fragColor;
uniform sampler2D sceneTex;
uniform float threshold;

void main() {
    vec3 color = textureLod(sceneTex,uv,0.0).rgb;
    float brightness = max(max(color.r,color.g),color.b);
    float knee = max(threshold*0.5,0.0001);
    float soft = clamp(brightness-threshold+knee,0.0,2.0*knee);
    soft = soft*soft/(4.0*knee);
    float contribution = max(brightness-threshold,soft)/max(brightness,0.0001);
    fragColor = vec4(color*contribution,1.0);
}
