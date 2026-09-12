using System;
using System.Runtime.InteropServices;

namespace Il2CppInterop.Runtime.Runtime.VersionSpecific.MethodInfo;

[ApplicableToUnityVersionsSince("6000.0.0")]
public unsafe class NativeMethodInfoStructHandler_6000_0 : INativeMethodInfoStructHandler
{
    public int Size() => sizeof(Il2CppMethodInfo_6000_0);

    public INativeMethodInfoStruct CreateNewStruct()
    {
        var pointer = Marshal.AllocHGlobal(Size());
        *(Il2CppMethodInfo_6000_0*)pointer = default;
        return new NativeStructWrapper(pointer);
    }

    public INativeMethodInfoStruct Wrap(Il2CppMethodInfo* pointer)
        => pointer == null ? null : new NativeStructWrapper((IntPtr)pointer);

    [StructLayout(LayoutKind.Explicit, Size = 0x58)]
    internal struct Il2CppMethodInfo_6000_0
    {
        [FieldOffset(0x00)] public void* methodPointer;
        [FieldOffset(0x08)] public void* virtualMethodPointer;
        [FieldOffset(0x10)] public void* invoker_method;
        [FieldOffset(0x18)] public Il2CppTypeStruct* return_type;
        [FieldOffset(0x20)] public Il2CppClass* klass;
        [FieldOffset(0x28)] public byte* name;
        [FieldOffset(0x30)] public Il2CppTypeStruct** parameters;
        [FieldOffset(0x38)] public void* runtime_data;
        [FieldOffset(0x40)] public void* generic_data;
        [FieldOffset(0x48)] public uint token;
        [FieldOffset(0x4C)] public ushort flags;
        [FieldOffset(0x4E)] public ushort iflags;
        [FieldOffset(0x50)] public ushort slot;
        [FieldOffset(0x52)] public byte parameters_count;
        [FieldOffset(0x53)] public byte bitfield0;
    }

    internal sealed class NativeStructWrapper : INativeMethodInfoStruct
    {
        public NativeStructWrapper(IntPtr pointer) => Pointer = pointer;
        public IntPtr Pointer { get; }
        private Il2CppMethodInfo_6000_0* Value => (Il2CppMethodInfo_6000_0*)Pointer;
        public Il2CppMethodInfo* MethodInfoPointer => (Il2CppMethodInfo*)Pointer;
        public ref IntPtr Name => ref *(IntPtr*)&Value->name;
        public ref ushort Slot => ref Value->slot;
        public ref IntPtr MethodPointer => ref *(IntPtr*)&Value->methodPointer;
        public ref IntPtr VirtualMethodPointer => ref *(IntPtr*)&Value->virtualMethodPointer;
        public ref Il2CppClass* Class => ref Value->klass;
        public ref IntPtr InvokerMethod => ref *(IntPtr*)&Value->invoker_method;
        public ref Il2CppTypeStruct* ReturnType => ref Value->return_type;
        public ref Il2CppMethodFlags Flags => ref *(Il2CppMethodFlags*)&Value->flags;
        public ref byte ParametersCount => ref Value->parameters_count;
        public ref Il2CppParameterInfo* Parameters => ref *(Il2CppParameterInfo**)&Value->parameters;
        public ref uint Token => ref Value->token;
        public bool IsGeneric { get => GetBit(0); set => SetBit(0, value); }
        public bool IsInflated { get => GetBit(1); set => SetBit(1, value); }
        public bool IsMarshalledFromNative { get => false; set { } }

        private bool GetBit(int bit) => ((Value->bitfield0 >> bit) & 1) != 0;

        private void SetBit(int bit, bool enabled)
            => Value->bitfield0 = enabled
                ? (byte)(Value->bitfield0 | (1 << bit))
                : (byte)(Value->bitfield0 & ~(1 << bit));
    }
}
