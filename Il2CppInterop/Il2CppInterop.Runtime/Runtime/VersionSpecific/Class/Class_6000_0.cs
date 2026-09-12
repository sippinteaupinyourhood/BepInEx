using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.Type;

namespace Il2CppInterop.Runtime.Runtime.VersionSpecific.Class;

[ApplicableToUnityVersionsSince("6000.0.0")]
public unsafe class NativeClassStructHandler_6000_0 : INativeClassStructHandler
{
    public int Size() => sizeof(Il2CppClass_6000_0);

    public INativeClassStruct CreateNewStruct(int vTableSlots)
    {
        var pointer = Marshal.AllocHGlobal(Size() + sizeof(VirtualInvokeData) * vTableSlots);
        *(Il2CppClass_6000_0*)pointer = default;
        return new NativeStructWrapper(pointer);
    }

    public INativeClassStruct Wrap(Il2CppClass* pointer)
        => pointer == null ? null : new NativeStructWrapper((IntPtr)pointer);

    [StructLayout(LayoutKind.Explicit, Size = 0x138)]
    internal struct Il2CppClass_6000_0
    {
        [FieldOffset(0x00)] public Il2CppImage* image;
        [FieldOffset(0x08)] public void* gc_desc;
        [FieldOffset(0x10)] public Il2CppClass* castClass;
        [FieldOffset(0x18)] public byte* namespaze;
        [FieldOffset(0x20)] public NativeTypeStructHandler_27_0.Il2CppType_27_0 byval_arg;
        [FieldOffset(0x30)] public NativeTypeStructHandler_27_0.Il2CppType_27_0 this_arg;
        [FieldOffset(0x40)] public Il2CppClass* element_class;
        [FieldOffset(0x48)] public byte* name;
        [FieldOffset(0x50)] public Il2CppMethodInfo** methods;
        [FieldOffset(0x58)] public Il2CppEventInfo* events;
        [FieldOffset(0x60)] public void* generic_class;
        [FieldOffset(0x68)] public Il2CppMetadataTypeHandle typeMetadataHandle;
        [FieldOffset(0x70)] public Il2CppClass* declaringType;
        [FieldOffset(0x78)] public void* interopData;
        [FieldOffset(0x80)] public Il2CppFieldInfo* fields;
        [FieldOffset(0x88)] public Il2CppClass* parent;
        [FieldOffset(0x90)] public Il2CppPropertyInfo* properties;
        [FieldOffset(0x98)] public Il2CppClass* klass;
        [FieldOffset(0xA0)] public Il2CppClass** nestedTypes;
        [FieldOffset(0xA8)] public Il2CppClass** implementedInterfaces;
        [FieldOffset(0xB0)] public Il2CppRuntimeInterfaceOffsetPair* interfaceOffsets;
        [FieldOffset(0xB8)] public void* static_fields;
        [FieldOffset(0xC0)] public void* rgctx_data;
        [FieldOffset(0xC8)] public Il2CppClass** typeHierarchy;
        [FieldOffset(0xD0)] public void* unity_user_data;
        [FieldOffset(0xD8)] public uint initializationExceptionGCHandle;
        [FieldOffset(0xDC)] public uint cctor_started;
        [FieldOffset(0xE0)] public uint cctor_finished_or_no_cctor;
        [FieldOffset(0xE8)] public IntPtr cctor_thread;
        [FieldOffset(0xF0)] public Il2CppMetadataGenericContainerHandle genericContainerHandle;
        [FieldOffset(0xF8)] public uint instance_size;
        [FieldOffset(0xFC)] public uint stack_slot_size;
        [FieldOffset(0x100)] public uint actualSize;
        [FieldOffset(0x104)] public uint element_size;
        [FieldOffset(0x108)] public int native_size;
        [FieldOffset(0x10C)] public uint static_fields_size;
        [FieldOffset(0x110)] public uint thread_static_fields_size;
        [FieldOffset(0x114)] public int thread_static_fields_offset;
        [FieldOffset(0x118)] public uint flags;
        [FieldOffset(0x11C)] public uint token;
        [FieldOffset(0x120)] public ushort method_count;
        [FieldOffset(0x122)] public ushort event_count;
        [FieldOffset(0x124)] public ushort field_count;
        [FieldOffset(0x126)] public ushort property_count;
        [FieldOffset(0x128)] public ushort nested_type_count;
        [FieldOffset(0x12A)] public ushort vtable_count;
        [FieldOffset(0x12C)] public ushort interfaces_count;
        [FieldOffset(0x12E)] public ushort interface_offsets_count;
        [FieldOffset(0x130)] public byte typeHierarchyDepth;
        [FieldOffset(0x131)] public byte genericRecursionDepth;
        [FieldOffset(0x132)] public byte rank;
        [FieldOffset(0x133)] public byte minimumAlignment;
        [FieldOffset(0x134)] public byte packingSize;
        [FieldOffset(0x135)] public byte bitfield0;
        [FieldOffset(0x136)] public byte bitfield1;
    }

    internal sealed class NativeStructWrapper : INativeClassStruct
    {
        public NativeStructWrapper(IntPtr pointer) => Pointer = pointer;
        public IntPtr Pointer { get; }
        private Il2CppClass_6000_0* Value => (Il2CppClass_6000_0*)Pointer;
        public IntPtr VTable => IntPtr.Add(Pointer, sizeof(Il2CppClass_6000_0));
        public Il2CppClass* ClassPointer => (Il2CppClass*)Pointer;
        public INativeTypeStruct ByValArg => UnityVersionHandler.Wrap((Il2CppTypeStruct*)&Value->byval_arg);
        public INativeTypeStruct ThisArg => UnityVersionHandler.Wrap((Il2CppTypeStruct*)&Value->this_arg);
        public ref uint InstanceSize => ref Value->instance_size;
        public ref ushort VtableCount => ref Value->vtable_count;
        public ref ushort InterfaceCount => ref Value->interfaces_count;
        public ref ushort InterfaceOffsetsCount => ref Value->interface_offsets_count;
        public ref byte TypeHierarchyDepth => ref Value->typeHierarchyDepth;
        public ref int NativeSize => ref Value->native_size;
        public ref uint ActualSize => ref Value->actualSize;
        public ref ushort MethodCount => ref Value->method_count;
        public ref ushort FieldCount => ref Value->field_count;
        public ref Il2CppClassAttributes Flags => ref *(Il2CppClassAttributes*)&Value->flags;
        public ref IntPtr Name => ref *(IntPtr*)&Value->name;
        public ref IntPtr Namespace => ref *(IntPtr*)&Value->namespaze;
        public ref Il2CppImage* Image => ref Value->image;
        public ref Il2CppClass* Parent => ref Value->parent;
        public ref Il2CppClass* ElementClass => ref Value->element_class;
        public ref Il2CppClass* CastClass => ref Value->castClass;
        public ref Il2CppClass* DeclaringType => ref Value->declaringType;
        public ref Il2CppClass* Class => ref Value->klass;
        public ref Il2CppFieldInfo* Fields => ref Value->fields;
        public ref Il2CppMethodInfo** Methods => ref Value->methods;
        public ref Il2CppClass** ImplementedInterfaces => ref Value->implementedInterfaces;
        public ref Il2CppRuntimeInterfaceOffsetPair* InterfaceOffsets => ref Value->interfaceOffsets;
        public ref Il2CppClass** TypeHierarchy => ref Value->typeHierarchy;
        public bool ValueType { get => ByValArg.ValueType && ThisArg.ValueType; set { } }
        public bool Initialized { get => GetBit(1, false); set => SetBit(1, value, false); }
        public bool EnumType { get => GetBit(2, false); set => SetBit(2, value, false); }
        public bool IsGeneric { get => GetBit(4, false); set => SetBit(4, value, false); }
        public bool InitializedAndNoError { get => GetBit(0, false); set => SetBit(0, value, false); }
        public bool SizeInited { get => GetBit(0, true); set => SetBit(0, value, true); }
        public bool HasFinalize { get => GetBit(1, true); set => SetBit(1, value, true); }
        public bool IsVtableInitialized { get => GetBit(5, true); set => SetBit(5, value, true); }

        private bool GetBit(int bit, bool second)
            => (((second ? Value->bitfield1 : Value->bitfield0) >> bit) & 1) != 0;

        private void SetBit(int bit, bool enabled, bool second)
        {
            ref var field = ref second ? ref Value->bitfield1 : ref Value->bitfield0;
            field = enabled ? (byte)(field | (1 << bit)) : (byte)(field & ~(1 << bit));
        }
    }
}
