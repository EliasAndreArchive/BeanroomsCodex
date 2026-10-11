#version 430 core
in vec2 uv;
out vec4 fragColor;
uniform sampler2D sceneTex;
uniform sampler2D previousExposure;
uniform float brightenSpeed;
uniform float darkenSpeed;
uniform float deltaTime;

void main() {
    int lastLevel = textureQueryLevels(sceneTex) - 1;
    vec3 average = textureLod(sceneTex,vec2(0.5),float(lastLevel)).rgb;
    float luminance = dot(average,vec3(0.2126,0.7152,0.0722));
    float target = clamp(0.22/(luminance+0.02),0.5,3.0);
    float previous = texelFetch(previousExposure,ivec2(0),0).r;
    float rate = target > previous ? brightenSpeed : darkenSpeed;
    float blend = 1.0-exp(-rate*clamp(deltaTime,0.0,0.1));
    fragColor = vec4(mix(previous,target,blend),0.0,0.0,1.0);
}
