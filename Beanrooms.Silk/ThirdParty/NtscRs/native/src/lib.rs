use std::{ffi::{c_char, CStr, CString}, panic::{catch_unwind, AssertUnwindSafe}, sync::OnceLock};
use ntsc_rs::{Context, NtscEffect, YiqView, settings::{Settings, SettingsList, SettingDescriptor, SettingKind, AnySetting}, yiq_fielding::{Rgbx, BlitInfo, DeinterlaceMode}};
use serde_json::{json, Value};

struct Effect { ctx: Context, settings: NtscEffect, yiq: Vec<f32>, original: Vec<f32> }
fn descriptors(items: &[SettingDescriptor<NtscEffect>], parent: Option<&str>, out: &mut Vec<Value>) {
    let defaults=NtscEffect::default();
    for d in items {
        let value=match (d.id.get)(&defaults) {
            AnySetting::Enum(v)=>json!(v), AnySetting::Int(v)=>json!(v),
            AnySetting::Float(v)=>json!(v), AnySetting::Bool(v)=>json!(v),
        };
        let mut row=json!({"key":d.id.name,"label":d.label,"description":d.description,"parent":parent,"default":value});
        match &d.kind {
            SettingKind::Enumeration{options}=> {row["kind"]=json!("enum");row["options"]=json!(options.iter().map(|o|json!({"label":o.label,"value":o.index})).collect::<Vec<_>>());},
            SettingKind::Percentage{logarithmic}=> {row["kind"]=json!("float");row["min"]=json!(0);row["max"]=json!(1);row["log"]=json!(logarithmic);},
            SettingKind::FloatRange{range,logarithmic}=> {row["kind"]=json!("float");row["min"]=json!(range.start());row["max"]=json!(range.end());row["log"]=json!(logarithmic);},
            SettingKind::IntRange{range}=> {row["kind"]=json!("int");row["min"]=json!(range.start());row["max"]=json!(range.end());},
            SettingKind::Boolean | SettingKind::Group{..}=>row["kind"]=json!("bool"),
        }
        out.push(row);
        if let SettingKind::Group{children}=&d.kind {descriptors(children,Some(d.id.name),out);}
    }
}
#[unsafe(no_mangle)]
pub extern "C" fn bean_ntsc_schema() -> *const c_char {
    static SCHEMA: OnceLock<CString>=OnceLock::new();
    SCHEMA.get_or_init(|| {let mut rows=Vec::new();descriptors(&NtscEffect::setting_descriptors(),None,&mut rows);CString::new(serde_json::to_string(&rows).unwrap()).unwrap()}).as_ptr()
}
#[unsafe(no_mangle)]
pub extern "C" fn bean_ntsc_create() -> *mut std::ffi::c_void {
    match catch_unwind(|| Box::into_raw(Box::new(Effect{ctx:Context::new(),settings:NtscEffect::default(),yiq:Vec::new(),original:Vec::new()}))) {
        Ok(p)=>p.cast(),Err(_)=>std::ptr::null_mut()
    }
}
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bean_ntsc_destroy(effect: *mut std::ffi::c_void) {
    if !effect.is_null() {drop(unsafe{Box::from_raw(effect.cast::<Effect>())});}
}
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bean_ntsc_settings(effect: *mut std::ffi::c_void, json: *const c_char) -> i32 {
    if effect.is_null() || json.is_null() {return -1;}
    catch_unwind(AssertUnwindSafe(|| {
        let effect=unsafe{&mut *effect.cast::<Effect>()};
        let text=unsafe{CStr::from_ptr(json)}.to_str().map_err(|_|())?;
        effect.settings=SettingsList::<NtscEffect>::new().from_json(text).map_err(|_|())?;
        Ok::<(),()>(())
    })).map_or(-3,|r|if r.is_ok(){0}else{-2})
}
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bean_ntsc_process(effect: *mut std::ffi::c_void, pixels: *mut f32, width: i32, height: i32, frame: u32, strength: f32) -> i32 {
    if effect.is_null() || pixels.is_null() || width<4 || height<4 || width>4096 || height>4096 || !strength.is_finite() {return -1;}
    catch_unwind(AssertUnwindSafe(|| {
        let e=unsafe{&mut *effect.cast::<Effect>()};
        let (w,h)=(width as usize,height as usize);
        let buffer=unsafe{std::slice::from_raw_parts_mut(pixels,w*h*4)};
        if strength<=0.0 {return;}
        if strength<1.0 {e.original.clear();e.original.extend_from_slice(buffer);}
        let field=e.settings.use_field.to_yiq_field(frame as usize);
        let len=YiqView::max_buf_length_for((w,h),e.settings.use_field);
        e.yiq.resize(len,0.0);
        let mut view=YiqView::from_parts(&mut e.yiq,(w,h),field);
        let mut blit=BlitInfo::from_full_frame(w,h,w*16);blit.flip_y=true;
        view.set_from_strided_buffer::<Rgbx,f32,_>(&e.ctx,buffer,blit,());
        e.settings.apply_effect_to_yiq(&e.ctx,&mut view,frame as usize,[1.0,1.0]);
        let mut blit=BlitInfo::from_full_frame(w,h,w*16);blit.flip_y=true;
        view.write_to_strided_buffer::<Rgbx,f32,_>(&e.ctx,buffer,blit,DeinterlaceMode::Bob,());
        for (index,p) in buffer.iter_mut().enumerate() {
            if index%4==3 {*p=1.0;} else {
                *p=if p.is_finite(){p.clamp(0.0,1.0)}else{0.0};
                if strength<1.0 {*p=e.original[index]+(*p-e.original[index])*strength;}
            }
        }
    })).map_or(-3,|_|0)
}
