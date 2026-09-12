using System;
using System.Runtime.InteropServices;

namespace Il2CppInterop.Runtime.Runtime.VersionSpecific.FieldInfo;

[ApplicableToUnityVersionsSince("6000.0.0")]
public unsafe class NativeFieldInfoStructHandler_6000_0 : INativeFieldInfoStructHandler
{
    public int Size() => sizeof(Il2CppFieldInfo_6000_0);

    public INativeFieldInfoStruct CreateNewStruct()
    {
        var pointer = Marshal.AllocHGlobal(Size());
        *(Il2CppFieldInfo_6000_0*)pointer = default;
        return new NativeStructWrapper(pointer);
    }

    public INativeFieldInfoStruct Wrap(Il2CppFieldInfo* pointer)
        => pointer == null ? null : new NativeStructWrapper((IntPtr)pointer);

    [StructLayout(LayoutKind.Explicit, Size = 0x20)]
    internal struct Il2CppFieldInfo_6000_0
    {
        [FieldOffset(0x00)] public byte* name;
        [FieldOffset(0x08)] public Il2CppTypeStruct* type;
        [FieldOffset(0x10)] public Il2CppClass* parent;
        [FieldOffset(0x18)] public uint token;
        [FieldOffset(0x1C)] public int offset;
    }

    internal sealed class NativeStructWrapper : INativeFieldInfoStruct
    {
        public NativeStructWrapper(IntPtr pointer) => Pointer = pointer;
        public IntPtr Pointer { get; }
        private Il2CppFieldInfo_6000_0* Value => (Il2CppFieldInfo_6000_0*)Pointer;
        public Il2CppFieldInfo* FieldInfoPointer => (Il2CppFieldInfo*)Pointer;
        public ref IntPtr Name => ref *(IntPtr*)&Value->name;
        public ref Il2CppTypeStruct* Type => ref Value->type;
        public ref Il2CppClass* Parent => ref Value->parent;
        public ref int Offset => ref Value->offset;
    }
}
