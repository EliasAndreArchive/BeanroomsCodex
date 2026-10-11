#version 430 core
in vec2 uv;
out vec4 fragColor;
uniform sampler2D sourceTex;
uniform vec2 direction;
uniform vec2 texelSize;
uniform float radius;

void main() {
    vec2 stepSize = direction*texelSize*radius;
    vec3 color = texture(sourceTex,uv).rgb*0.227027;
    color += (texture(sourceTex,uv+stepSize).rgb+texture(sourceTex,uv-stepSize).rgb)*0.1945946;
    color += (texture(sourceTex,uv+stepSize*2.0).rgb+texture(sourceTex,uv-stepSize*2.0).rgb)*0.1216216;
    color += (texture(sourceTex,uv+stepSize*3.0).rgb+texture(sourceTex,uv-stepSize*3.0).rgb)*0.0540541;
    color += (texture(sourceTex,uv+stepSize*4.0).rgb+texture(sourceTex,uv-stepSize*4.0).rgb)*0.0162162;
    fragColor = vec4(color,1.0);
}
