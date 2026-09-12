using System;
using System.Runtime.InteropServices;

namespace Il2CppInterop.Runtime.Runtime.VersionSpecific.FieldInfo
{
    [ApplicableToUnityVersionsSince("2022.2.0")]
    public unsafe class NativeFieldInfoStructHandler_29_1 : INativeFieldInfoStructHandler
    {
        public int Size() => sizeof(Il2CppFieldInfo_29_1);

        public INativeFieldInfoStruct CreateNewStruct()
        {
            var pointer = Marshal.AllocHGlobal(Size());
            *(Il2CppFieldInfo_29_1*)pointer = default;
            return new NativeStructWrapper(pointer);
        }

        public INativeFieldInfoStruct Wrap(Il2CppFieldInfo* pointer)
            => pointer == null ? null : new NativeStructWrapper((IntPtr)pointer);

        [StructLayout(LayoutKind.Explicit, Size = 0x28)]
        internal struct Il2CppFieldInfo_29_1
        {
            [FieldOffset(0x00)] public int offset;
            [FieldOffset(0x08)] public Il2CppClass* parent;
            [FieldOffset(0x10)] public Il2CppTypeStruct* type;
            [FieldOffset(0x18)] public uint token;
            [FieldOffset(0x20)] public byte* name;
        }

        internal class NativeStructWrapper : INativeFieldInfoStruct
        {
            public NativeStructWrapper(IntPtr pointer) => Pointer = pointer;
            public IntPtr Pointer { get; }
            private Il2CppFieldInfo_29_1* Value => (Il2CppFieldInfo_29_1*)Pointer;
            public Il2CppFieldInfo* FieldInfoPointer => (Il2CppFieldInfo*)Pointer;
            public ref IntPtr Name => ref *(IntPtr*)&Value->name;
            public ref Il2CppTypeStruct* Type => ref Value->type;
            public ref Il2CppClass* Parent => ref Value->parent;
            public ref int Offset => ref Value->offset;
        }
    }
}
