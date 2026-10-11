#version 430 core

in vec2 uv;
out vec4 fragColor;
uniform sampler2D worldGrid;
uniform sampler2D fixtureMap;
uniform sampler2D directTex;
uniform sampler2D previousTex;
uniform int probeSize;
uniform int probeRadius;
uniform int probeLightSamples;
uniform int passIndex;
uniform int lightsEnabled;
uniform float lightIntensity;
uniform float bounceStrength;
uniform int objectCount;
uniform sampler2D objectMap;
vec4 objectData(int index) { return texelFetch(objectMap,ivec2(0,index),0); }
vec3 objectColor(int index) { return texelFetch(objectMap,ivec2(2,index),0).rgb; }
bool objectEmissive(int index) { return texelFetch(objectMap,ivec2(2,index),0).a > 0.5; }

const float PI=3.141592653589793;
const float WORLD=192.0;
const float CELL=4.0;
const float CEILING=3.4;

bool inWall(vec2 p) {
    ivec2 cell=ivec2(floor(p/CELL));
    if(any(lessThan(cell,ivec2(0))) || any(greaterThanEqual(cell,ivec2(48)))) return true;
    vec4 box=texelFetch(worldGrid,cell,0);
    return box.x>=0.0 && all(greaterThan(p,box.xy)) && all(lessThan(p,box.zw));
}

// The layout texture stores full wall rectangles in every covered 4 m cell.
// A segment test along the traversed cells keeps bounced light out of sealed rooms.
bool blocked(vec2 a,vec2 b) {
    vec2 delta=b-a;
    float distance=length(delta);
    if(distance<0.025) return false;
    vec2 direction=delta/distance;
    vec2 safe=vec2(abs(direction.x)<0.000001 ? (direction.x>=0.0?0.000001:-0.000001) : direction.x,
                   abs(direction.y)<0.000001 ? (direction.y>=0.0?0.000001:-0.000001) : direction.y);
    ivec2 cell=ivec2(floor(a/CELL));
    ivec2 endCell=ivec2(floor(b/CELL));
    ivec2 stepDir=ivec2(direction.x>=0.0?1:-1,direction.y>=0.0?1:-1);
    vec2 edge=(vec2(cell)+vec2(direction.x>=0.0?1.0:0.0,direction.y>=0.0?1.0:0.0))*CELL;
    vec2 side=(edge-a)/safe;
    vec2 stepLength=CELL/abs(safe);
    for(int i=0;i<24;i++) {
        if(any(lessThan(cell,ivec2(0))) || any(greaterThanEqual(cell,ivec2(48)))) return true;
        vec4 box=texelFetch(worldGrid,cell,0);
        if(box.x>=0.0) {
            vec2 t0=(box.xy-a)/safe, t1=(box.zw-a)/safe;
            float entry=max(min(t0.x,t1.x),min(t0.y,t1.y));
            float exit=min(max(t0.x,t1.x),max(t0.y,t1.y));
            if(entry<exit && entry<distance-0.035 && exit>0.035) return true;
        }
        if(all(equal(cell,endCell))) break;
        if(side.x<side.y) { cell.x+=stepDir.x;side.x+=stepLength.x; }
        else { cell.y+=stepDir.y;side.y+=stepLength.y; }
    }
    return false;
}

vec3 fixtureLight(vec2 p) {
    if(lightsEnabled==0) return vec3(0.0);
    ivec2 base=ivec2(floor(p/8.0+0.5));
    vec3 total=vec3(0.0);
    for(int z=-2;z<=2;z++) for(int x=-2;x<=2;x++) {
        ivec2 id=base+ivec2(x,z);
        if(any(lessThan(id,ivec2(0))) || any(greaterThanEqual(id,ivec2(25)))) continue;
        vec4 lamp=texelFetch(fixtureMap,id,0);
        if(lamp.r<0.5) continue;
        vec2 center=vec2(id)*8.0;
        vec3 emission=lamp.g>0.5 ? vec3(18.0,1.8,1.2) : vec3(16.5,15.5,13.2);
        for(int sampleIndex=0;sampleIndex<4;sampleIndex++) {
            if(sampleIndex>=probeLightSamples) break;
            vec2 offset=probeLightSamples==1 ? vec2(0.0) :
                vec2((sampleIndex&1)==0?-0.5:0.5,(sampleIndex&2)==0?-0.5:0.5);
            if(probeLightSamples==2 && sampleIndex==1) offset=vec2(0.5,0.5);
            vec2 point=center+offset;
            vec3 toLight=vec3(point.x-p.x,CEILING-1.45,point.y-p.y);
            float distanceSquared=dot(toLight,toLight);
            float distance=sqrt(distanceSquared);
            if(distance>24.0 || blocked(p,point)) continue;
            float fade=1.0-smoothstep(8.0,24.0,distance);
            total+=emission*lightIntensity*fade*(4.0/PI)*
                max(toLight.y/distance,0.0)/(distanceSquared+2.0)/float(probeLightSamples);
        }
    }
    return total;
}

vec3 cubeLight(vec2 p) {
    vec3 total=vec3(0.0);
    for(int i=0;i<objectCount;i++) {
        if(!objectEmissive(i)) continue;
        vec4 cube=objectData(i);
        vec2 toward=cube.xz-p;
        float distanceSquared=dot(toward,toward)+(cube.y-1.45)*(cube.y-1.45);
        float distance=sqrt(distanceSquared);
        if(distance>14.0 || blocked(p,cube.xz)) continue;
        float fade=1.0-smoothstep(7.0,14.0,distance);
        total+=objectColor(i)*vec3(7.0,8.5,12.0)*
            cube.w*cube.w*fade*2.0/(distanceSquared+0.8);
    }
    return total;
}

void main() {
    ivec2 id=ivec2(gl_FragCoord.xy);
    vec2 p=(vec2(id)+0.5)*(WORLD/float(probeSize));
    if(inWall(p)) { fragColor=vec4(0.0);return; }
    if(passIndex==0) {
        fragColor=vec4(fixtureLight(p)+cubeLight(p),1.0);
        return;
    }
    vec3 direct=texelFetch(directTex,id,0).rgb;
    vec3 neighbors=vec3(0.0);
    float weights=0.0;
    for(int z=-2;z<=2;z++) for(int x=-2;x<=2;x++) {
        if(x==0 && z==0) continue;
        if(abs(x)>probeRadius || abs(z)>probeRadius) continue;
        ivec2 other=id+ivec2(x,z);
        if(any(lessThan(other,ivec2(0))) || any(greaterThanEqual(other,ivec2(probeSize)))) continue;
        vec4 light=texelFetch(previousTex,other,0);
        if(light.a<0.5) continue;
        vec2 q=(vec2(other)+0.5)*(WORLD/float(probeSize));
        if(blocked(p,q)) continue;
        float distanceSquared=dot(q-p,q-p);
        float weight=exp(-distanceSquared/8.0);
        neighbors+=light.rgb*weight;
        weights+=weight;
    }
    // Diffuse reflection from the yellow wall and carpet materials.
    vec3 tint=vec3(0.67,0.64,0.47);
    vec3 bounce=weights>0.0 ? neighbors/weights*tint*(0.48*bounceStrength) : vec3(0.0);
    fragColor=vec4(min(direct+bounce,vec3(20.0)),1.0);
}
