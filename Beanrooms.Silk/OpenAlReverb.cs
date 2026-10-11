using System.Runtime.InteropServices;
using Silk.NET.OpenAL;

namespace Beanrooms;

internal readonly record struct RoomReverbSettings(float Density,float Diffusion,float Gain,
    float GainHF,float DecayTime,float DecayHFRatio,float ReflectionsGain,
    float ReflectionsDelay,float LateGain,float LateDelay,float GainLF=1f,float DecayLFRatio=1f)
{
    internal static RoomReverbSettings Backrooms => new(0.9f,0.85f,0.38f,
        0.48f,2.5f,0.68f,0.18f,0.035f,1.35f,0.025f);
}

// One OpenAL EAX effect is the listener room; the others represent Vercidium's grouped rooms.
internal sealed class OpenAlReverb : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Gen(int count,out uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Delete(int count,ref uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetInt(uint id,int parameter,int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetFloat(uint id,int parameter,float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Source3Int(uint source,int parameter,int a,int b,int c);

    readonly AL al;
    readonly Gen genEffect,genSlot;
    readonly Delete deleteEffect,deleteSlot;
    readonly SetFloat effectFloat,slotFloat;
    readonly SetInt effectInt,slotInt;
    readonly Source3Int source3Int;
    readonly List<uint> effects=[],slots=[];
    int activeGroups;
    internal int GroupCapacity => Math.Max(0,slots.Count-1);

    OpenAlReverb(AL al,Gen genEffect,Gen genSlot,Delete deleteEffect,Delete deleteSlot,
        SetFloat effectFloat,SetFloat slotFloat,SetInt effectInt,SetInt slotInt,Source3Int source3Int)
    {
        this.al=al;this.genEffect=genEffect;this.genSlot=genSlot;
        this.deleteEffect=deleteEffect;this.deleteSlot=deleteSlot;
        this.effectFloat=effectFloat;this.slotFloat=slotFloat;
        this.effectInt=effectInt;this.slotInt=slotInt;this.source3Int=source3Int;
    }

    static T? Procedure<T>(AL al,string name) where T:Delegate
    {
        IntPtr address=al.GetProcAddress(name);
        return address==IntPtr.Zero?null:Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    internal static OpenAlReverb? TryCreate(AL al)
    {
        Gen? genEffect=Procedure<Gen>(al,"alGenEffects");
        Delete? deleteEffect=Procedure<Delete>(al,"alDeleteEffects");
        SetInt? effectInt=Procedure<SetInt>(al,"alEffecti");
        SetFloat? effectFloat=Procedure<SetFloat>(al,"alEffectf");
        Gen? genSlot=Procedure<Gen>(al,"alGenAuxiliaryEffectSlots");
        Delete? deleteSlot=Procedure<Delete>(al,"alDeleteAuxiliaryEffectSlots");
        SetInt? slotInt=Procedure<SetInt>(al,"alAuxiliaryEffectSloti");
        SetFloat? slotFloat=Procedure<SetFloat>(al,"alAuxiliaryEffectSlotf");
        Source3Int? source3Int=Procedure<Source3Int>(al,"alSource3i");
        if(genEffect==null||deleteEffect==null||effectInt==null||effectFloat==null||
           genSlot==null||deleteSlot==null||slotInt==null||slotFloat==null||source3Int==null) return null;
        var room=new OpenAlReverb(al,genEffect,genSlot,deleteEffect,deleteSlot,
            effectFloat,slotFloat,effectInt,slotInt,source3Int);
        if(!room.CreatePair(RoomReverbSettings.Backrooms,0.65f))
        { room.Dispose();return null; }
        for(int i=0;i<VercidiumAcoustics.MaximumReverbGroups;i++)
            if(!room.CreatePair(RoomReverbSettings.Backrooms,0f)) break;
        return room;
    }

    bool CreatePair(RoomReverbSettings settings,float gain)
    {
        al.GetError();
        genEffect(1,out uint effect);
        if(effect==0 || al.GetError()!=AudioError.NoError) return false;
        effectInt(effect,0x8001,0x8000); // AL_EFFECT_TYPE = AL_EFFECT_EAXREVERB
        if(al.GetError()!=AudioError.NoError) { deleteEffect(1,ref effect);return false; }
        Apply(effect,settings);
        genSlot(1,out uint slot);
        if(slot==0 || al.GetError()!=AudioError.NoError)
        { deleteEffect(1,ref effect);return false; }
        slotInt(slot,0x0001,(int)effect); // AL_EFFECTSLOT_EFFECT
        slotFloat(slot,0x0002,gain); // AL_EFFECTSLOT_GAIN
        if(al.GetError()!=AudioError.NoError)
        { deleteSlot(1,ref slot);deleteEffect(1,ref effect);return false; }
        effects.Add(effect);slots.Add(slot);
        return true;
    }

    void Apply(uint effect,RoomReverbSettings room)
    {
        effectFloat(effect,0x0001,Math.Clamp(room.Density,0f,1f));
        effectFloat(effect,0x0002,Math.Clamp(room.Diffusion,0f,1f));
        effectFloat(effect,0x0003,Math.Clamp(room.Gain,0f,1f));
        effectFloat(effect,0x0004,Math.Clamp(room.GainHF,0f,1f));
        effectFloat(effect,0x0005,Math.Clamp(room.GainLF,0f,1f));
        effectFloat(effect,0x0006,Math.Clamp(room.DecayTime,0.1f,20f));
        effectFloat(effect,0x0007,Math.Clamp(room.DecayHFRatio,0.1f,2f));
        effectFloat(effect,0x0008,Math.Clamp(room.DecayLFRatio,0.1f,2f));
        effectFloat(effect,0x0009,Math.Clamp(room.ReflectionsGain,0f,3.16f));
        effectFloat(effect,0x000A,Math.Clamp(room.ReflectionsDelay,0f,0.3f));
        effectFloat(effect,0x000C,Math.Clamp(room.LateGain,0f,10f));
        effectFloat(effect,0x000D,Math.Clamp(room.LateDelay,0f,0.1f));
    }

    internal void SetRoom(RoomReverbSettings room)
    {
        if(effects.Count==0) return;
        Apply(effects[0],room);
        slotInt(slots[0],0x0001,(int)effects[0]);
    }

    internal void SetGroups(IReadOnlyList<VercidiumAcoustics.ReverbGroup> groups)
    {
        activeGroups=Math.Min(groups.Count,GroupCapacity);
        for(int i=0;i<GroupCapacity;i++)
        {
            int slotIndex=i+1;
            if(i<activeGroups)
            {
                Apply(effects[slotIndex],groups[i].Settings);
                slotInt(slots[slotIndex],0x0001,(int)effects[slotIndex]);
                slotFloat(slots[slotIndex],0x0002,0.65f*groups[i].Gain);
            }
            else slotFloat(slots[slotIndex],0x0002,0f);
        }
    }

    internal bool Attach(uint source,int group=-1)
    {
        if(slots.Count==0) return false;
        int index=group>=0 && group<activeGroups?group+1:0;
        source3Int(source,0x20006,(int)slots[index],0,0); // AL_AUXILIARY_SEND_FILTER
        return al.GetError()==AudioError.NoError;
    }

    public void Dispose()
    {
        foreach(uint id in slots) { uint slot=id;deleteSlot(1,ref slot); }
        foreach(uint id in effects) { uint effect=id;deleteEffect(1,ref effect); }
        slots.Clear();effects.Clear();
    }
}
