#version 430 core
in vec2 uv;
out vec4 fragColor;
uniform sampler2D worldGrid;
uniform sampler2D heightGrid;
uniform sampler2D pitMap;
uniform sampler2D wallTex;
uniform sampler2D floorTex;
uniform sampler2D ceilingTex;
uniform sampler2D fixtureMap;
uniform sampler2D faceTex;
uniform sampler2D eyeTex;
uniform sampler2D probeDirectTex;
uniform sampler2D probeBounceTex;
uniform vec3 cameraPos;
uniform mat3 cameraBasis;
uniform vec2 resolution;
uniform int frameIndex;
uniform int lightsEnabled;
uniform int worldSeed;
uniform int hybridMode;
uniform int fixedPathMode;
uniform int probeSize;
uniform int hybridDirectSamples;
uniform int sampleCount;
uniform int bounceCount;
uniform float bounceStrength;
uniform float aoStrength;
uniform float lightDensity;
uniform float lightIntensity;
uniform int volumetricsEnabled;
uniform float fogDensity;
uniform int fogSteps;
uniform int objectCount;
uniform sampler2D objectMap;
vec4 objectData(int index) { return texelFetch(objectMap,ivec2(0,index),0); }
vec4 objectRotation(int index) { return texelFetch(objectMap,ivec2(1,index),0); }
vec3 objectColor(int index) { return texelFetch(objectMap,ivec2(2,index),0).rgb; }
bool objectEmissive(int index) { return texelFetch(objectMap,ivec2(2,index),0).a>0.5; }
uniform int remoteCount;
uniform vec4 remoteData[4];
uniform vec3 remoteColor[4];
uniform vec4 monsterData;

const float PI = 3.141592653589793;
const float CELL = 4.0;
const int GRID = 64;
const int FIXTURE_GRID = 33;
const float WORLD = 256.0;
const float SPACING = 8.0;

float ceilingAt(vec2 p) {
    ivec2 cell=clamp(ivec2(floor(p/CELL)),ivec2(0),ivec2(GRID-1));
    return texelFetch(heightGrid,cell,0).r;
}
bool pitAt(vec2 p) {
    ivec2 size=textureSize(pitMap,0);
    ivec2 cell=ivec2(floor(p*2.0));
    return all(greaterThanEqual(cell,ivec2(0))) && all(lessThan(cell,size)) &&
        texelFetch(pitMap,cell,0).r>0.5;
}

uint state;
int pathSampleOrdinal;
int pathBounceOrdinal;
float lightRotation;
uint hash32(uint value) {
    value ^= value >> 16u;
    value *= 0x7feb352du;
    value ^= value >> 15u;
    value *= 0x846ca68bu;
    return value ^ (value >> 16u);
}
float rand() {
    if (fixedPathMode != 0) {
        state = state * 747796405u + 2891336453u;
        uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
        word = (word >> 22u) ^ word;
        return float(word >> 8u) * (1.0 / 16777216.0);
    }
    state = state * 1664525u + 1013904223u;
    return float(state >> 8u) * (1.0 / 16777216.0);
}
vec2 fixtureCenter(ivec2 id) { return vec2(id) * SPACING; }
bool fixtureOn(ivec2 id) {
    if (lightsEnabled == 0 || any(lessThan(id,ivec2(0))) || any(greaterThanEqual(id,ivec2(FIXTURE_GRID)))) return false;
    return texelFetch(fixtureMap,id,0).r > 0.5;
}
bool fixtureRed(ivec2 id) {
    return texelFetch(fixtureMap,id,0).g > 0.5;
}
vec3 fixtureEmission(ivec2 id) {
    return fixtureRed(id) ? vec3(18.0, 1.8, 1.2) : vec3(16.5, 15.5, 13.2);
}
vec3 fixtureEmissionAt(vec3 p) {
    ivec2 id = ivec2(floor(p.xz / SPACING + 0.5));
    return fixtureEmission(id);
}
bool panelAt(vec3 p) {
    ivec2 id = ivec2(floor(p.xz / SPACING + 0.5));
    return fixtureOn(id) && all(lessThan(abs(p.xz - fixtureCenter(id)), vec2(1.0)));
}
bool panelInstalledAt(vec3 p) {
    ivec2 id = ivec2(floor(p.xz / SPACING + 0.5));
    if(any(lessThan(id,ivec2(0))) || any(greaterThanEqual(id,ivec2(FIXTURE_GRID)))) return false;
    return texelFetch(fixtureMap,id,0).b > 0.5 &&
        all(lessThan(abs(p.xz - fixtureCenter(id)),vec2(1.0)));
}
float smoothLightRange(float distance, float fullRange, float endRange) {
    return 1.0 - smoothstep(fullRange, endRange, distance);
}

vec3 rotateQ(vec4 q,vec3 v) { return v+2.0*cross(q.xyz,cross(q.xyz,v)+q.w*v); }
struct Hit { float t; vec3 pos; vec3 normal; vec3 baseColor; int material; };
Hit trace(vec3 ro, vec3 rd, float limit) {
    Hit h; h.t = limit; h.material = 0; h.normal = vec3(0.0); h.baseColor = vec3(1.0); h.pos = ro + rd * limit;
    if (rd.y < -0.00001) {
        float t = -ro.y / rd.y;
        if (t > 0.001 && t < h.t && !pitAt(ro.xz+rd.xz*t))
        { h.t=t; h.material=2; h.normal=vec3(0,1,0); }
        else if(t>0.001 && pitAt(ro.xz+rd.xz*t)) {
            float bottom=(-5.0-ro.y)/rd.y;
            if(bottom>t && bottom<h.t) { h.t=bottom;h.material=12;h.normal=vec3(0,1,0); }
            for(int step=1;step<=32;step++) {
                float sideT=mix(t,bottom,float(step)/32.0);
                if(sideT>=h.t) break;
                if(!pitAt(ro.xz+rd.xz*sideT))
                { h.t=sideT;h.material=13;h.normal=normalize(vec3(-rd.x,0,-rd.z));break; }
            }
        }
    } else if (rd.y > 0.00001) {
        for(int tier=0;tier<3;tier++) {
            float ceiling=tier==0?3.4:tier==1?4.9:7.2;
            float t=(ceiling-ro.y)/rd.y;
            if(t>0.001&&t<h.t&&abs(ceilingAt(ro.xz+rd.xz*t)-ceiling)<0.05)
            { h.t=t;h.material=3;h.normal=vec3(0,-1,0); }
        }
    }
    for(int object=0;object<objectCount;object++) {
        vec4 data=objectData(object);
        float radius=data.w*1.733;
        vec3 toCenter=data.xyz-ro;
        float along=dot(toCenter,rd);
        if(along < -radius || along > h.t+radius || dot(toCenter-rd*along,toCenter-rd*along)>radius*radius) continue;
        vec4 inverseRotation=vec4(-objectRotation(object).xyz,objectRotation(object).w);
        vec3 localOrigin=rotateQ(inverseRotation,ro-data.xyz);
        vec3 localDirection=rotateQ(inverseRotation,rd);
        vec3 safeDirection=vec3(
            localDirection.x>=0.0?max(localDirection.x,0.000001):min(localDirection.x,-0.000001),
            localDirection.y>=0.0?max(localDirection.y,0.000001):min(localDirection.y,-0.000001),
            localDirection.z>=0.0?max(localDirection.z,0.000001):min(localDirection.z,-0.000001));
        vec3 nearT=(-vec3(data.w)-localOrigin)/safeDirection;
        vec3 farT=(vec3(data.w)-localOrigin)/safeDirection;
        vec3 lo=min(nearT,farT), hi=max(nearT,farT);
        float entry=max(max(lo.x,lo.y),lo.z);
        float leave=min(min(hi.x,hi.y),hi.z);
        if(entry>0.001 && entry<leave && entry<h.t) {
            vec3 n=lo.x>lo.y && lo.x>lo.z ? vec3(-sign(localDirection.x),0,0) :
                   lo.y>lo.z ? vec3(0,-sign(localDirection.y),0) : vec3(0,0,-sign(localDirection.z));
            h.t=entry; h.normal=rotateQ(objectRotation(object),n); h.baseColor=objectColor(object);
            h.material=objectEmissive(object) ? 5 : 4;
        }
    }
    for(int player=0;player<remoteCount;player++) {
        vec4 avatar=remoteData[player];
        float radius=0.43;
        float lower=radius;
        float upper=clamp(avatar.y+0.2,1.15,2.05)-radius;
        vec2 sideOrigin=ro.xz-avatar.xz;
        float a=dot(rd.xz,rd.xz);
        float b=dot(sideOrigin,rd.xz);
        float c=dot(sideOrigin,sideOrigin)-radius*radius;
        float disc=b*b-a*c;
        vec3 capsuleColor=remoteColor[player];
        if(a>0.000001 && disc>=0.0) {
            float t=(-b-sqrt(disc))/a;
            float y=ro.y+rd.y*t;
            if(t>0.001 && t<h.t && y>=lower && y<=upper) {
                h.t=t;
                h.normal=normalize(vec3(ro.x+t*rd.x-avatar.x,0.0,ro.z+t*rd.z-avatar.z));
                h.baseColor=capsuleColor;
                h.material=6+player;
            }
        }
        for(int cap=0;cap<2;cap++) {
            vec3 center=vec3(avatar.x,cap==0?lower:upper,avatar.z);
            vec3 relative=ro-center;
            float sphereB=dot(relative,rd);
            float sphereC=dot(relative,relative)-radius*radius;
            float sphereDisc=sphereB*sphereB-sphereC;
            if(sphereDisc<0.0) continue;
            float t=-sphereB-sqrt(sphereDisc);
            float y=ro.y+rd.y*t;
            if(t>0.001 && t<h.t && (cap==0?y<=lower:y>=upper)) {
                h.t=t;
                h.normal=normalize(ro+rd*t-center);
                h.baseColor=capsuleColor;
                h.material=6+player;
            }
        }
    }
    // The face is a separate alpha-cutout plane just in front of each capsule.
    // It keeps the original height and rotates with the player's yaw.
    bool cameraRay=dot(ro-cameraPos,ro-cameraPos)<0.0016;
    if(cameraRay) for(int player=0;player<remoteCount;player++) {
        vec4 avatar=remoteData[player];
        vec2 facing=vec2(sin(avatar.w),cos(avatar.w));
        vec3 normal=vec3(facing.x,0.0,facing.y);
        float denominator=dot(rd,normal);
        if(denominator>=-0.0001) continue;
        float top=clamp(avatar.y+0.2,1.15,2.05);
        vec3 center=vec3(avatar.x+facing.x*0.46,top-0.505,
                         avatar.z+facing.y*0.46);
        float t=dot(center-ro,normal)/denominator;
        if(t<=0.001 || t>=h.t) continue;
        vec3 point=ro+rd*t;
        vec3 right=vec3(facing.y,0.0,-facing.x);
        vec2 texcoord=vec2(0.5+dot(point-center,right)/0.86,
                           0.5-(point.y-center.y)/0.63);
        if(any(lessThan(texcoord,vec2(0.0))) || any(greaterThan(texcoord,vec2(1.0)))) continue;
        float lod=max(0.0,log2(t*512.0/(resolution.y*0.63)));
        float ink=textureLod(faceTex,texcoord,lod).a;
        if(ink>0.28) {
            h.t=t;h.normal=normal;h.baseColor=vec3(0.003);h.material=11;
        }
    }
    // Camera-facing alpha cutout. The source image keeps its soft transparent edge.
    // Only camera rays hit the sprite; its flat plane does not cast fake GI shadows.
    if(cameraRay && monsterData.x>0.0) {
        vec3 center=vec3(monsterData.x,monsterData.y,monsterData.z);
        vec2 toward=normalize(cameraPos.xz-center.xz);
        vec3 normal=vec3(toward.x,0.0,toward.y);
        float denominator=dot(rd,normal);
        if(denominator < -0.0001) {
            float t=dot(center-ro,normal)/denominator;
            if(t>0.001 && t<h.t) {
                vec3 point=ro+rd*t;
                vec3 right=vec3(toward.y,0.0,-toward.x);
                vec2 texcoord=vec2(0.5+dot(point-center,right)/2.7,
                                   0.5-(point.y-center.y)/2.7);
                if(all(greaterThanEqual(texcoord,vec2(0.0))) && all(lessThanEqual(texcoord,vec2(1.0)))) {
                    float lod=max(0.0,log2(t*320.0/(resolution.y*2.7)));
                    vec4 texel=textureLod(eyeTex,texcoord,lod);
                    float alphaThreshold=hybridMode!=0 ?
                        fract(sin(dot(floor(gl_FragCoord.xy),vec2(12.9898,78.233)))*43758.5453) : rand();
                    if(texel.a>alphaThreshold) {
                        h.t=t; h.material=10; h.normal=normal;
                        h.baseColor=texel.rgb;
                    }
                }
            }
        }
    }
    ivec2 cell = ivec2(floor(ro.xz / CELL));
    ivec2 stepDir = ivec2(rd.x >= 0.0 ? 1 : -1, rd.z >= 0.0 ? 1 : -1);
    vec2 safeDir = vec2(abs(rd.x) < 0.000001 ? (rd.x >= 0.0 ? 0.000001 : -0.000001) : rd.x,
                        abs(rd.z) < 0.000001 ? (rd.z >= 0.0 ? 0.000001 : -0.000001) : rd.z);
    vec2 inv = 1.0 / abs(safeDir);
    vec2 delta = CELL * inv;
    vec2 edge = (vec2(cell) + vec2(rd.x >= 0.0 ? 1.0 : 0.0, rd.z >= 0.0 ? 1.0 : 0.0)) * CELL;
    vec2 side = abs(edge - ro.xz) * inv;
    for (int i=0; i<150; i++) {
        if (any(lessThan(cell, ivec2(0))) || any(greaterThanEqual(cell, ivec2(GRID)))) break;
        vec4 b = texelFetch(worldGrid, cell, 0);
        float wallHeight=texelFetch(heightGrid,cell,0).g;
        if (b.x >= 0.0) {
            vec2 near2 = (b.xy - ro.xz) / safeDir;
            vec2 far2 = (b.zw - ro.xz) / safeDir;
            vec2 lo = min(near2, far2);
            vec2 hi = max(near2, far2);
            float entry = max(lo.x, lo.y);
            float leave = min(hi.x, hi.y);
            if (entry > 0.001 && entry < limit && entry <= h.t + 0.002 && entry <= leave + 0.0001 && entry <= min(side.x,side.y)+0.002 && ro.y + entry*rd.y >= -0.002 && ro.y + entry*rd.y <= wallHeight+0.002) {
                h.t = entry; h.material = 1;
                h.normal = lo.x > lo.y ? vec3(-sign(rd.x),0,0) : vec3(0,0,-sign(rd.z));
                break;
            }
            if(rd.y< -0.00001) {
                float topT=(wallHeight-ro.y)/rd.y;
                vec2 topPoint=ro.xz+rd.xz*topT;
                if(topT>0.001&&topT<h.t&&topT<min(side.x,side.y)+0.002&&
                    all(greaterThanEqual(topPoint,b.xy))&&all(lessThanEqual(topPoint,b.zw)))
                { h.t=topT;h.material=1;h.normal=vec3(0,1,0);break; }
            }
        }
        if (min(side.x,side.y) > h.t) break;
        float crossing=min(side.x,side.y);
        ivec2 nextCell=cell+(side.x<side.y?ivec2(stepDir.x,0):ivec2(0,stepDir.y));
        if(all(greaterThanEqual(nextCell,ivec2(0)))&&all(lessThan(nextCell,ivec2(GRID)))) {
            float oldCeiling=texelFetch(heightGrid,cell,0).r;
            float newCeiling=texelFetch(heightGrid,nextCell,0).r;
            float y=ro.y+crossing*rd.y;
            if(abs(oldCeiling-newCeiling)>0.05&&crossing>0.001&&crossing<h.t&&
                y>min(oldCeiling,newCeiling)&&y<max(oldCeiling,newCeiling)) {
                h.t=crossing;h.material=1;
                h.normal=side.x<side.y?vec3(-stepDir.x,0,0):vec3(0,0,-stepDir.y);
                break;
            }
        }
        if (side.x < side.y) { cell.x += stepDir.x; side.x += delta.x; }
        else { cell.y += stepDir.y; side.y += delta.y; }
    }
    h.pos = ro + rd * h.t;
    return h;
}

vec3 materialColor(Hit h) {
    if(h.material==12) return vec3(0.055,0.050,0.040);
    if(h.material==13) return vec3(0.22,0.20,0.13);
    if(h.material==10) return max(h.baseColor,vec3(0.003));
    if(h.material==11) return h.baseColor;
    if(h.material==4) return h.baseColor;
    if(h.material==5) return h.baseColor;
    if(h.material>=6 && h.material<10) return h.baseColor;
    vec3 view = normalize(h.pos - cameraPos);
    float grazing = max(abs(dot(view,h.normal)),0.12);
    float footprint = max(h.t * 1.55 / resolution.y / grazing, 0.001);
    if (h.material == 1) {
        vec2 coord = abs(h.normal.x) > 0.5 ? h.pos.zy : h.pos.xy;
        float lod = max(0.0,log2(footprint * float(textureSize(wallTex,0).x) / 2.5) - 0.65);
        return textureLod(wallTex, coord / vec2(2.5, 2.5),lod).rgb * vec3(0.88,0.83,0.72);
    }
    if (h.material == 2) {
        float lod = max(0.0,log2(footprint * float(textureSize(floorTex,0).x) / 3.4) - 0.65);
        return textureLod(floorTex, h.pos.xz / 3.4,lod).rgb * vec3(0.78,0.74,0.65);
    }
    if(panelInstalledAt(h.pos)) {
        ivec2 id=ivec2(floor(h.pos.xz / SPACING + 0.5));
        vec2 local=abs(h.pos.xz-fixtureCenter(id));
        float rim=smoothstep(0.91,0.96,max(local.x,local.y));
        return mix(vec3(0.70,0.70,0.66),vec3(0.20,0.21,0.20),rim);
    }
    // The 128px ceiling texture covers the same world area as the 128px floor texture.
    float lod = max(0.0,log2(footprint * float(textureSize(ceilingTex,0).x) / 3.4) - 0.65);
    return textureLod(ceilingTex, h.pos.xz / 3.4,lod).rgb * vec3(0.78,0.77,0.73);
}

vec3 cosineDirection(vec3 n) {
    float a = rand() * 2.0 * PI;
    float r = sqrt(rand());
    vec3 t = normalize(cross(abs(n.y) < 0.9 ? vec3(0,1,0) : vec3(1,0,0), n));
    vec3 b = cross(n,t);
    return normalize(t * (r*cos(a)) + b * (r*sin(a)) + n * sqrt(1.0-r*r));
}

// Ceilings see broad patches of illuminated carpet. Sampling those patches
// explicitly avoids relying on a few random paths to find the floor.
vec3 ceilingBounce(Hit h,vec3 albedo) {
    if(lightsEnabled==0 || h.material!=3) return vec3(0.0);
    ivec2 base=ivec2(floor(h.pos.xz/SPACING));
    vec3 bounced=vec3(0.0);
    for(int z=0;z<2;z++) for(int x=0;x<2;x++) {
        ivec2 id=base+ivec2(x,z);
        if(!fixtureOn(id)) continue;
        vec2 center=fixtureCenter(id);
        if(abs(ceilingAt(center)-h.pos.y)>0.05 || pitAt(center)) continue;
        vec2 toward=center-h.pos.xz;
        float distanceSquared=dot(toward,toward)+h.pos.y*h.pos.y;
        float heightWeight=0.45+0.55*smoothstep(3.4,7.2,h.pos.y);
        bounced+=fixtureEmission(id)*lightIntensity*
            (0.40*1.2*heightWeight)/(distanceSquared+4.0);
    }
    return albedo*bounced;
}

vec3 directLight(Hit h, vec3 albedo, int samples) {
    vec3 cubeLight = vec3(0);
    for(int object=0;object<objectCount;object++) {
        if(!objectEmissive(object)) continue;
        vec4 cube=objectData(object);
        vec3 toward=cube.xyz-h.pos;
        float distanceSquared=dot(toward,toward);
        if(distanceSquared<0.01) continue;
        float distance=sqrt(distanceSquared);
        float rangeFade=smoothLightRange(distance,7.0,16.0);
        if(rangeFade<=0.001) continue;
        vec3 wi=toward/distance;
        float facing=max(dot(h.normal,wi),0.0);
        if(facing<=0.0) continue;
        Hit blocker=trace(h.pos+h.normal*0.015,wi,max(0.01,distance-cube.w*1.8));
        if(blocker.material!=0) continue;
        cubeLight+=albedo*objectColor(object)*vec3(7.0,8.5,12.0)*facing*rangeFade*(cube.w*cube.w*4.0)/(distanceSquared+0.6);
    }
    // Ceiling panels face downward. Their own plane cannot receive their area
    // light because the sampled light direction has a negative Y component.
    if(lightsEnabled==0 || h.material==3) return cubeLight+ceilingBounce(h,albedo);
    vec3 total = vec3(0);
    ivec2 base = ivec2(floor(h.pos.xz / SPACING + 0.5));
    float weights[25];
    float weightSum = 0.0;
    for (int j=0; j<25; j++) {
        ivec2 id = base + ivec2(j % 5 - 2, j / 5 - 2);
        float weight = 0.0;
        if(fixtureOn(id)) {
            vec2 center=fixtureCenter(id);
            vec2 horizontal = center-h.pos.xz;
            float verticalDistance=ceilingAt(center)-h.pos.y;
            float distanceSquared=dot(horizontal,horizontal)+verticalDistance*verticalDistance;
            if(distanceSquared<576.0) {
                float fade=smoothLightRange(sqrt(distanceSquared),8.0,24.0);
                weight=fade/(distanceSquared+verticalDistance*verticalDistance+2.0);
            }
        }
        weights[j] = weight;
        weightSum += weight;
    }
    if (weightSum <= 0.0) return cubeLight;
    for (int i=0; i<samples; i++) {
        float selector = rand();
        if (fixedPathMode != 0 && pathBounceOrdinal == 0) {
            float ordinal=float(pathSampleOrdinal*samples+i);
            selector=fract((ordinal+selector)/float(sampleCount*samples)+lightRotation);
        }
        float target = selector * weightSum;
        int choice = 24;
        for (int j=0; j<25; j++) {
            target -= weights[j];
            if (target < 0.0) { choice = j; break; }
        }
        ivec2 id = base + ivec2(choice % 5 - 2, choice / 5 - 2);
        vec2 center = fixtureCenter(id);
        vec3 lp = vec3(center.x + (rand()-0.5)*2.0,
            ceilingAt(center)-0.012, center.y + (rand()-0.5)*2.0);
        vec3 v = lp - h.pos;
        float dist2 = dot(v,v);
        float dist = sqrt(dist2);
        vec3 wi = v / dist;
        float geometry = max(dot(h.normal, wi), 0.0) * max(wi.y, 0.0);
        if (geometry <= 0.0) continue;
        Hit blocker = trace(h.pos + h.normal * 0.012, wi, dist - 0.035);
        if (blocker.material != 0) continue;
        vec3 emission = fixtureEmission(id) * lightIntensity;
        vec2 horizontal=center-h.pos.xz;
        float verticalDistance=ceilingAt(center)-h.pos.y;
        float centerDistanceSquared=dot(horizontal,horizontal)+verticalDistance*verticalDistance;
        // The selected weight already contains the falloff. Cancel it against the PDF.
        float weightedFalloff=weightSum*(centerDistanceSquared+verticalDistance*verticalDistance+2.0);
        vec3 contribution = albedo * emission * geometry * (4.0 * weightedFalloff / (PI * dist2));
        total += min(contribution, vec3(12.0));
    }
    return total / float(samples) + cubeLight;
}

vec2 hybridPanelOffset(int sampleIndex,int samples) {
    if(samples==1) return vec2(0.0);
    if(samples==2) return sampleIndex==0 ? vec2(-0.5,-0.5) : vec2(0.5,0.5);
    if(samples==4) return vec2((sampleIndex & 1)==0 ? -0.5 : 0.5,
                                (sampleIndex & 2)==0 ? -0.5 : 0.5);
    float u=(float(sampleIndex)+0.5)/float(samples)*2.0-1.0;
    float v=fract((float(sampleIndex)+0.5)*0.61803398875)*2.0-1.0;
    return vec2(u,v);
}

// Deterministic quadrature over the brightest nearby area lights. Sample points
// live on the fixtures in world space, so turning the camera cannot drag a
// fixed screen-space Monte Carlo pattern across the walls and floor.
vec3 hybridDirectLight(Hit h,vec3 albedo) {
    vec3 cubeLight=vec3(0.0);
    for(int object=0;object<objectCount;object++) {
        if(!objectEmissive(object)) continue;
        vec4 cube=objectData(object);
        vec3 toward=cube.xyz-h.pos;
        float distanceSquared=dot(toward,toward);
        if(distanceSquared<0.01) continue;
        float distance=sqrt(distanceSquared);
        float fade=smoothLightRange(distance,7.0,16.0);
        if(fade<=0.001) continue;
        vec3 wi=toward/distance;
        float facing=max(dot(h.normal,wi),0.0);
        if(facing<=0.0) continue;
        Hit blocker=trace(h.pos+h.normal*0.015,wi,max(0.01,distance-cube.w*1.8));
        if(blocker.material!=0) continue;
        // Dynamic emissive cubes and powered lamps are injected before the
        // probe bounce pass, giving them the same soft indirect response as
        // the ceiling panels instead of a camera dependent glow.
        cubeLight+=albedo*objectColor(object)*vec3(8.0,9.5,13.5)*facing*fade*
            (cube.w*cube.w*4.0)/(distanceSquared+0.6);
    }
    if(lightsEnabled==0 || h.material==3) return cubeLight+ceilingBounce(h,albedo);
    float scores[8];
    ivec2 ids[8];
    for(int i=0;i<8;i++) { scores[i]=0.0;ids[i]=ivec2(-1); }
    ivec2 base=ivec2(floor(h.pos.xz/SPACING+0.5));
    for(int z=-2;z<=2;z++) for(int x=-2;x<=2;x++) {
        ivec2 id=base+ivec2(x,z);
        if(!fixtureOn(id)) continue;
        vec2 center=fixtureCenter(id);
        vec2 horizontal=center-h.pos.xz;
        float vertical=ceilingAt(center)-h.pos.y;
        float d2=dot(horizontal,horizontal)+vertical*vertical;
        if(d2>=576.0) continue;
        float fade=smoothLightRange(sqrt(d2),8.0,24.0);
        float score=fade/(d2+vertical*vertical+2.0);
        for(int rank=0;rank<8;rank++) {
            if(score<=scores[rank]) continue;
            for(int shift=7;shift>rank;shift--) { scores[shift]=scores[shift-1];ids[shift]=ids[shift-1]; }
            scores[rank]=score;ids[rank]=id;
            break;
        }
    }
    vec3 total=cubeLight;
    for(int light=0;light<8;light++) {
        if(scores[light]<=0.0) break;
        ivec2 id=ids[light];
        vec2 center=fixtureCenter(id);
        vec3 emission=fixtureEmission(id)*lightIntensity;
        for(int sampleIndex=0;sampleIndex<8;sampleIndex++) {
            if(sampleIndex>=hybridDirectSamples) break;
            vec2 offset=hybridPanelOffset(sampleIndex,hybridDirectSamples);
            vec3 lp=vec3(center.x+offset.x,ceilingAt(center)-0.012,center.y+offset.y);
            vec3 toward=lp-h.pos;
            float distanceSquared=dot(toward,toward);
            float distance=sqrt(distanceSquared);
            vec3 wi=toward/distance;
            float geometry=max(dot(h.normal,wi),0.0)*max(wi.y,0.0);
            if(geometry<=0.0) continue;
            Hit blocker=trace(h.pos+h.normal*0.012,wi,distance-0.035);
            if(blocker.material!=0) continue;
            float fade=smoothLightRange(distance,8.0,24.0);
            total+=albedo*emission*geometry*fade*
                (4.0/(PI*distanceSquared*float(hybridDirectSamples)));
        }
    }
    return total;
}

float ambientOcclusion(Hit h) {
    if (h.material == 0 || (h.material == 3 && panelAt(h.pos))) return 1.0;
    if (h.material >= 4) return 1.0;
    if (h.material == 1) {
        float floorContact = exp(-max(h.pos.y,0.0) / 0.48);
        float ceilingContact = exp(-max(ceilingAt(h.pos.xz)-h.pos.y,0.0) / 0.48);
        return clamp(1.0 - aoStrength * (0.46*floorContact + 0.38*ceilingContact),0.2,1.0);
    }
    ivec2 base = ivec2(floor(h.pos.xz / CELL));
    float nearestWall = 5.0;
    for (int z=-1; z<=1; z++) for (int x=-1; x<=1; x++) {
        ivec2 cell = base + ivec2(x,z);
        if (any(lessThan(cell,ivec2(0))) || any(greaterThanEqual(cell,ivec2(GRID)))) continue;
        vec4 box = texelFetch(worldGrid,cell,0);
        if (box.x < 0.0) continue;
        if (h.material == 3 && texelFetch(heightGrid,cell,0).g < h.pos.y-0.05) continue;
        vec2 outside = max(max(box.xy-h.pos.xz,h.pos.xz-box.zw),vec2(0.0));
        nearestWall = min(nearestWall,length(outside));
    }
    float contact = 0.58 * exp(-nearestWall / 0.72);
    return clamp(1.0 - aoStrength * contact,0.2,1.0);
}

bool probeBlocked(vec2 a,vec2 b) {
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
    for(int i=0;i<8;i++) {
        if(any(lessThan(cell,ivec2(0))) || any(greaterThanEqual(cell,ivec2(GRID)))) return true;
        vec4 box=texelFetch(worldGrid,cell,0);
        if(box.x>=0.0) {
            vec2 t0=(box.xy-a)/safe,t1=(box.zw-a)/safe;
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

vec3 probeIndirect(Hit h) {
    vec2 p=h.pos.xz+h.normal.xz*0.07;
    vec2 gridPos=p*(float(probeSize)/WORLD)-0.5;
    ivec2 base=ivec2(floor(gridPos));
    vec2 fraction=fract(gridPos);
    vec3 sum=vec3(0.0);
    float weightSum=0.0;
    for(int z=0;z<2;z++) for(int x=0;x<2;x++) {
        ivec2 id=base+ivec2(x,z);
        if(any(lessThan(id,ivec2(0))) || any(greaterThanEqual(id,ivec2(probeSize)))) continue;
        vec4 bounce=texelFetch(probeBounceTex,id,0);
        if(bounce.a<0.5) continue;
        vec2 center=(vec2(id)+0.5)*(WORLD/float(probeSize));
        if(probeBlocked(p,center)) continue;
        vec3 direct=texelFetch(probeDirectTex,id,0).rgb;
        float weight=(x==0?1.0-fraction.x:fraction.x)*(z==0?1.0-fraction.y:fraction.y);
        sum+=max(bounce.rgb-direct,vec3(0.0))*weight;
        weightSum+=weight;
    }
    // The small normal term prevents light leaking across wall edges while
    // retaining enough diffuse fill for open rooms.
    float orientation=0.48+0.52*max(h.normal.y,0.0);
    return weightSum>0.0001 ? sum/weightSum*orientation : vec3(0.0);
}

// A small, single-scattering test pass. March only the visible camera segment;
// each sample checks the two nearest ceiling panels for wall occlusion.
vec4 volumetricLight(vec3 origin,vec3 direction,float surfaceDistance) {
    if(volumetricsEnabled == 0 || lightsEnabled == 0 || fogDensity <= 0.00001)
        return vec4(0.0,0.0,0.0,1.0);
    float distanceLimit=min(surfaceDistance,30.0);
    if(distanceLimit<=0.04) return vec4(0.0,0.0,0.0,1.0);
    int steps=clamp(fogSteps,2,8);
    float stepLength=distanceLimit/float(steps);
    vec3 scattering=vec3(0.0);
    for(int sampleIndex=0;sampleIndex<8;sampleIndex++) {
        if(sampleIndex>=steps) break;
        float travel=(float(sampleIndex)+0.5)*stepLength;
        vec3 samplePosition=origin+direction*travel;
        ivec2 base=ivec2(floor(samplePosition.xz/SPACING+0.5));
        float nearestDistance[2]=float[2](1e20,1e20);
        ivec2 nearestId[2]=ivec2[2](ivec2(-1),ivec2(-1));
        for(int z=-1;z<=1;z++) for(int x=-1;x<=1;x++) {
            ivec2 id=base+ivec2(x,z);
            if(!fixtureOn(id)) continue;
            vec2 center=fixtureCenter(id);
            vec3 toLight=vec3(center.x,ceilingAt(center)-0.07,center.y)-samplePosition;
            float distanceSquared=dot(toLight,toLight);
            if(distanceSquared<nearestDistance[0]) {
                nearestDistance[1]=nearestDistance[0];nearestId[1]=nearestId[0];
                nearestDistance[0]=distanceSquared;nearestId[0]=id;
            } else if(distanceSquared<nearestDistance[1]) {
                nearestDistance[1]=distanceSquared;nearestId[1]=id;
            }
        }
        for(int light=0;light<2;light++) {
            float distanceSquared=nearestDistance[light];
            if(distanceSquared>18.0*18.0 || distanceSquared<0.03) continue;
            ivec2 id=nearestId[light];
            vec2 center=fixtureCenter(id);
            vec3 toLight=vec3(center.x,ceilingAt(center)-0.07,center.y)-samplePosition;
            float distance=sqrt(distanceSquared);
            vec3 toward=toLight/distance;
            float fade=smoothLightRange(distance,2.5,11.0);
            if(fade<=0.001) continue;
            Hit blocker=trace(samplePosition+toward*0.035,toward,distance-0.10);
            if(blocker.material!=0) continue;
            float phase=1.0+1.2*pow(max(dot(direction,toward),0.0),6.0);
            float transmittance=exp(-fogDensity*(travel+distance));
            scattering+=fixtureEmission(id)*lightIntensity*transmittance*
                fogDensity*stepLength*fade*phase*0.075/(4.0+distanceSquared*0.35);
        }
        int closestCube=-1;
        float cubeDistanceSquared=9.0*9.0;
        for(int object=0;object<objectCount;object++) {
            if(!objectEmissive(object)) continue;
            vec3 offset=objectData(object).xyz-samplePosition;
            float distanceSquared=dot(offset,offset);
            if(distanceSquared<cubeDistanceSquared) {
                cubeDistanceSquared=distanceSquared;closestCube=object;
            }
        }
        if(closestCube>=0 && cubeDistanceSquared>0.04) {
            vec4 cube=objectData(closestCube);
            float distance=sqrt(cubeDistanceSquared);
            vec3 toward=(cube.xyz-samplePosition)/distance;
            float fade=smoothLightRange(distance,2.0,9.0);
            float shadowLimit=distance-cube.w*1.8;
            if(fade>0.001 && shadowLimit>0.05) {
                Hit blocker=trace(samplePosition+toward*0.035,toward,shadowLimit);
                if(blocker.material==0) {
                    float phase=1.0+1.2*pow(max(dot(direction,toward),0.0),6.0);
                    float transmittance=exp(-fogDensity*(travel+distance));
                    scattering+=objectColor(closestCube)*vec3(7.0,8.5,12.0)*
                        fogDensity*stepLength*fade*phase*transmittance*0.075/
                        (3.0+cubeDistanceSquared*0.35);
                }
            }
        }
    }
    return vec4(min(scattering,vec3(0.5)),exp(-fogDensity*distanceLimit));
}

void main() {
    state = fixedPathMode != 0 ?
        hash32(uint(gl_FragCoord.x)*0x9e3779b9u ^ uint(gl_FragCoord.y)*0x85ebca6bu ^
               uint(frameIndex)*0xc2b2ae35u ^ uint(worldSeed)) :
        uint(gl_FragCoord.x) * 1973u + uint(gl_FragCoord.y) * 9277u +
        uint(frameIndex) * 26699u + uint(worldSeed)*911u + 1u;
    vec2 p = (gl_FragCoord.xy - resolution*0.5) / resolution.y;
    vec3 rd = normalize(cameraBasis * vec3(p * 1.55, 1.0));
    vec3 ro = cameraPos;
    Hit primary = trace(ro,rd,150.0);
    vec4 fog=volumetricLight(ro,rd,primary.t);
    if(hybridMode != 0) {
        Hit h=primary;
        vec3 surfaceColor;
        if(h.material==0) surfaceColor=vec3(0.015,0.014,0.012);
        else if(h.material==3 && panelAt(h.pos)) surfaceColor=fixtureEmissionAt(h.pos)*lightIntensity;
        else if(h.material==5) surfaceColor=h.baseColor*vec3(8.0,9.5,13.0);
        else if(h.material==10) surfaceColor=h.baseColor*3.0+vec3(0.002);
        else {
            vec3 albedo=materialColor(h);
            vec3 direct=min(hybridDirectLight(h,albedo),vec3(5.0));
            vec3 indirect=albedo*probeIndirect(h);
            surfaceColor=min(direct+indirect,vec3(6.0))*ambientOcclusion(h);
        }
        fragColor=vec4(surfaceColor*fog.a+fog.rgb,1.0);
        return;
    }
    vec3 totalRadiance = vec3(0.0);
    vec2 subpixelRotation=vec2(0.0);
    if(fixedPathMode != 0) {
        subpixelRotation=vec2(rand(),rand());
        lightRotation=rand();
    }
    for (int sampleIndex=0; sampleIndex<sampleCount; sampleIndex++) {
        vec2 offset=fixedPathMode != 0 ?
            fract(vec2((float(sampleIndex)+0.5)/float(sampleCount),
                       float(bitfieldReverse(uint(sampleIndex)))*(1.0/4294967296.0))+
                  subpixelRotation)-0.5 : vec2(rand()-0.5,rand()-0.5);
        vec2 subpixel = (gl_FragCoord.xy + offset - resolution*0.5) / resolution.y;
        rd = normalize(cameraBasis * vec3(subpixel * 1.55, 1.0));
        ro = cameraPos;
        pathSampleOrdinal=sampleIndex;
        vec3 throughput = vec3(1.0);
        vec3 radiance = vec3(0.0);
        for (int bounce=0; bounce<bounceCount; bounce++) {
            pathBounceOrdinal=bounce;
            Hit h = trace(ro, rd, 150.0);
            if (h.material == 0) break;
            if (h.material == 5) { radiance += throughput*h.baseColor*vec3(8.0,9.5,13.0); break; }
            if (h.material == 10) { radiance += throughput*(h.baseColor*3.0+vec3(0.002)); break; }
            if (h.material == 3 && panelAt(h.pos)) {
                if (bounce == 0) radiance += throughput * fixtureEmissionAt(h.pos) * lightIntensity;
                break;
            }
            vec3 albedo = materialColor(h);
            // At high SPP, two light samples per camera ray still give hundreds of
            // direct samples while avoiding half of the primary shadow traces.
            int lightSamples=bounce==0 ? (sampleCount>=64 ? 2 : 4) : 1;
            vec3 incoming = min(directLight(h, albedo, lightSamples), vec3(bounce==0 ? 5.0 : 3.5));
            radiance += throughput * incoming * (bounce==0 ? 1.0 : bounceStrength);
            throughput *= albedo;
            rd = cosineDirection(h.normal);
            ro = h.pos + h.normal * 0.014;
        }
        totalRadiance += min(radiance, vec3(6.0));
    }
    vec3 surfaceColor=totalRadiance / float(sampleCount) * ambientOcclusion(primary);
    fragColor = vec4(surfaceColor*fog.a+fog.rgb, 1.0);
}
