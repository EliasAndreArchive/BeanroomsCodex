#version 430 core
in vec2 uv;
out vec4 fragColor;

uniform int beanCount;
uniform vec3 beanPosition[7];
uniform vec4 beanRotation[7];
uniform vec3 beanColor[7];

vec3 rotateQ(vec4 q,vec3 v) {
    return v+2.0*cross(q.xyz,cross(q.xyz,v)+q.w*v);
}

void main() {
    vec2 screen=uv*2.0-1.0;
    screen.x*=4.0/3.0;
    vec3 origin=vec3(0.0,0.0,-6.0);
    vec3 ray=normalize(vec3(screen*vec2(0.84,0.69),1.25));
    float nearest=100.0;
    vec3 normal=vec3(0.0),color=vec3(0.0);
    int hitBean=-1;
    for(int i=0;i<beanCount;i++) {
        vec4 inverseRotation=vec4(-beanRotation[i].xyz,beanRotation[i].w);
        vec3 ro=rotateQ(inverseRotation,origin-beanPosition[i]);
        vec3 rd=rotateQ(inverseRotation,ray);
        float t=100.0;
        vec3 n=vec3(0.0);
        float a=dot(rd.xz,rd.xz);
        float b=dot(ro.xz,rd.xz);
        float c=dot(ro.xz,ro.xz)-0.46*0.46;
        float disc=b*b-a*c;
        if(a>0.000001 && disc>=0.0) {
            float candidate=(-b-sqrt(disc))/a;
            float y=ro.y+rd.y*candidate;
            if(candidate>0.0 && abs(y)<=0.4) {
                t=candidate;
                n=normalize(vec3(ro.x+rd.x*t,0.0,ro.z+rd.z*t));
            }
        }
        for(int cap=0;cap<2;cap++) {
            vec3 center=vec3(0.0,cap==0?-0.4:0.4,0.0);
            vec3 offset=ro-center;
            float halfB=dot(offset,rd);
            float sphereDisc=halfB*halfB-dot(offset,offset)+0.46*0.46;
            if(sphereDisc<0.0) continue;
            float candidate=-halfB-sqrt(sphereDisc);
            float y=ro.y+rd.y*candidate;
            if(candidate>0.0 && candidate<t && (cap==0?y<=-0.4:y>=0.4)) {
                t=candidate;
                n=normalize(ro+rd*t-center);
            }
        }
        if(t<nearest) {
            nearest=t;
            normal=rotateQ(beanRotation[i],n);
            color=beanColor[i];
            hitBean=i;
        }
    }

    vec3 background=mix(vec3(0.040,0.050,0.070),vec3(0.015,0.020,0.033),smoothstep(-0.9,0.8,screen.y));
    float halo=exp(-dot(screen-vec2(0.08,0.06),screen-vec2(0.08,0.06))*1.7);
    background+=vec3(0.035,0.055,0.08)*halo;
    if(hitBean>=0) {
        vec3 light=normalize(vec3(-0.55,0.8,-0.6));
        float diffuse=max(dot(normal,light),0.0);
        float rim=pow(1.0-max(dot(normal,-ray),0.0),2.5);
        vec3 shaded=color*(0.38+0.75*diffuse)+color*rim*0.32;
        shaded+=vec3(0.32,0.46,0.60)*pow(max(dot(reflect(-light,normal),-ray),0.0),24.0)*0.35;
        background=shaded;
    }
    background*=1.0-0.14*dot(screen,screen);
    fragColor=vec4(clamp(background,0.0,1.0),1.0);
}
