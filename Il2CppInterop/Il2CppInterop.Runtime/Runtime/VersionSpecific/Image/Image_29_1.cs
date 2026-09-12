using System;
using System.Runtime.InteropServices;

namespace Il2CppInterop.Runtime.Runtime.VersionSpecific.Image
{
    [ApplicableToUnityVersionsSince("2022.2.0")]
    public unsafe class NativeImageStructHandler_29_1 : INativeImageStructHandler
    {
        public int Size() => sizeof(Il2CppImage_29_1);

        public INativeImageStruct CreateNewStruct()
        {
            IntPtr ptr = Marshal.AllocHGlobal(Size());
            Il2CppImage_29_1* image = (Il2CppImage_29_1*)ptr;
            *image = default;

            Il2CppImageGlobalMetadata* metadata =
                (Il2CppImageGlobalMetadata*)Marshal.AllocHGlobal(sizeof(Il2CppImageGlobalMetadata));
            *metadata = default;
            metadata->entryPointIndex = -1;
            metadata->image = (Il2CppImage*)image;
            *(Il2CppImageGlobalMetadata**)&image->metadataHandle = metadata;

            return new NativeStructWrapper(ptr);
        }

        public INativeImageStruct Wrap(Il2CppImage* ptr)
        {
            if (ptr == null) return null;
            return new NativeStructWrapper((IntPtr)ptr);
        }

        [StructLayout(LayoutKind.Explicit, Size = 0x48)]
        internal struct Il2CppImage_29_1
        {
            [FieldOffset(0x00)] public Il2CppAssembly* assembly;
            [FieldOffset(0x08)] public byte* nameNoExt;
            [FieldOffset(0x10)] public uint exportedTypeCount;
            [FieldOffset(0x14)] public byte dynamic;
            [FieldOffset(0x18)] public uint typeCount;
            [FieldOffset(0x20)] public byte* name;
            [FieldOffset(0x28)] public Il2CppMetadataImageHandle metadataHandle;
            [FieldOffset(0x30)] public void* nameToClassHashTable;
            [FieldOffset(0x38)] public void* codeGenModule;
            [FieldOffset(0x40)] public uint token;
            [FieldOffset(0x44)] public uint customAttributeCount;
        }

        internal class NativeStructWrapper : INativeImageStruct
        {
            public NativeStructWrapper(IntPtr ptr) => Pointer = ptr;

            public IntPtr Pointer { get; }
            private Il2CppImage_29_1* Value => (Il2CppImage_29_1*)Pointer;
            public Il2CppImage* ImagePointer => (Il2CppImage*)Pointer;
            public bool HasNameNoExt => true;
            public ref Il2CppAssembly* Assembly => ref Value->assembly;
            public ref byte Dynamic => ref Value->dynamic;
            public ref IntPtr Name => ref *(IntPtr*)&Value->name;
            public ref IntPtr NameNoExt => ref *(IntPtr*)&Value->nameNoExt;
        }
    }
}
