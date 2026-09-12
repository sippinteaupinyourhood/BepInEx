using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.Type;
namespace Il2CppInterop.Runtime.Runtime.VersionSpecific.Class
{
    [ApplicableToUnityVersionsSince("2022.2.0")]
    public unsafe class NativeClassStructHandler_29_1 : INativeClassStructHandler
    {
        public int Size() => sizeof(Il2CppClass_29_1);
        public INativeClassStruct CreateNewStruct(int vTableSlots)
        {
            IntPtr ptr = Marshal.AllocHGlobal(Size() + sizeof(VirtualInvokeData) * vTableSlots);
            Il2CppClass_29_1* _ = (Il2CppClass_29_1*)ptr;
            *_ = default;
            return new NativeStructWrapper(ptr);
        }
        public INativeClassStruct Wrap(Il2CppClass* ptr)
        {
            if (ptr == null) return null;
            return new NativeStructWrapper((IntPtr)ptr);
        }
        [StructLayout(LayoutKind.Explicit, Size = 0x138)]
        internal unsafe struct Il2CppClass_29_1
        {
            [FieldOffset(0x00)] public Il2CppImage* image;
            [FieldOffset(0x08)] public void* gc_desc;
            [FieldOffset(0x10)] public Il2CppClass* klass;
            [FieldOffset(0x18)] public byte* namespaze;
            [FieldOffset(0x20)] public NativeTypeStructHandler_27_0.Il2CppType_27_0 byval_arg;
            [FieldOffset(0x30)] public NativeTypeStructHandler_27_0.Il2CppType_27_0 this_arg;
            [FieldOffset(0x40)] public Il2CppClass* element_class;
            [FieldOffset(0x48)] public Il2CppClass* declaringType;
            [FieldOffset(0x50)] public Il2CppClass** implementedInterfaces;
            [FieldOffset(0x58)] public Il2CppEventInfo* events;
            [FieldOffset(0x60)] public void* generic_class;
            [FieldOffset(0x68)] public Il2CppMetadataTypeHandle typeMetadataHandle;
            [FieldOffset(0x70)] public void* interopData;
            [FieldOffset(0x78)] public byte* name;
            [FieldOffset(0x80)] public Il2CppClass** nestedTypes;
            [FieldOffset(0x88)] public Il2CppPropertyInfo* properties;
            [FieldOffset(0x90)] public Il2CppClass* parent;
            [FieldOffset(0x98)] public Il2CppMethodInfo** methods;
            [FieldOffset(0xA0)] public Il2CppClass* castClass;
            [FieldOffset(0xA8)] public Il2CppFieldInfo* fields;
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
            [FieldOffset(0x122)] public ushort property_count;
            [FieldOffset(0x124)] public ushort field_count;
            [FieldOffset(0x126)] public ushort event_count;
            [FieldOffset(0x128)] public ushort nested_type_count;
            [FieldOffset(0x12A)] public ushort vtable_count;
            [FieldOffset(0x12C)] public ushort interfaces_count;
            [FieldOffset(0x12E)] public ushort interface_offsets_count;
            [FieldOffset(0x130)] public byte typeHierarchyDepth;
            [FieldOffset(0x131)] public byte genericRecursionDepth;
            [FieldOffset(0x132)] public byte rank;
            [FieldOffset(0x133)] public byte minimumAlignment;
            [FieldOffset(0x134)] public byte packingSize;
            [FieldOffset(0x135)] public Bitfield0 _bitfield0;
            [FieldOffset(0x136)] public Bitfield1 _bitfield1;
            internal enum Bitfield0 : byte
            {
                BIT_initialized_and_no_error = 0,
                initialized_and_no_error = (1 << BIT_initialized_and_no_error),
                BIT_initialized = 1,
                initialized = (1 << BIT_initialized),
                BIT_enumtype = 2,
                enumtype = (1 << BIT_enumtype),
                BIT_nullabletype = 3,
                nullabletype = (1 << BIT_nullabletype),
                BIT_is_generic = 4,
                is_generic = (1 << BIT_is_generic),
                BIT_has_references = 5,
                has_references = (1 << BIT_has_references),
                BIT_init_pending = 6,
                init_pending = (1 << BIT_init_pending),
                BIT_size_init_pending = 7,
                size_init_pending = (1 << BIT_size_init_pending),
            }

            internal enum Bitfield1 : byte
            {
                BIT_size_inited = 0,
                size_inited = (1 << BIT_size_inited),
                BIT_has_finalize = 1,
                has_finalize = (1 << BIT_has_finalize),
                BIT_has_cctor = 2,
                has_cctor = (1 << BIT_has_cctor),
                BIT_is_blittable = 3,
                is_blittable = (1 << BIT_is_blittable),
                BIT_is_import_or_windows_runtime = 4,
                is_import_or_windows_runtime = (1 << BIT_is_import_or_windows_runtime),
                BIT_is_vtable_initialized = 5,
                is_vtable_initialized = (1 << BIT_is_vtable_initialized),
                BIT_is_byref_like = 6,
                is_byref_like = (1 << BIT_is_byref_like),
            }

        }

        internal class NativeStructWrapper : INativeClassStruct
        {
            public NativeStructWrapper(IntPtr ptr) => Pointer = ptr;
            private static int _bitfield0offset = Marshal.OffsetOf<Il2CppClass_29_1>(nameof(Il2CppClass_29_1._bitfield0)).ToInt32();
            private static int _bitfield1offset = Marshal.OffsetOf<Il2CppClass_29_1>(nameof(Il2CppClass_29_1._bitfield1)).ToInt32();
            public IntPtr Pointer { get; }
            private Il2CppClass_29_1* _ => (Il2CppClass_29_1*)Pointer;
            public IntPtr VTable => IntPtr.Add(Pointer, sizeof(Il2CppClass_29_1));
            public Il2CppClass* ClassPointer => (Il2CppClass*)Pointer;
            public INativeTypeStruct ByValArg => UnityVersionHandler.Wrap((Il2CppTypeStruct*)&_->byval_arg);
            public INativeTypeStruct ThisArg => UnityVersionHandler.Wrap((Il2CppTypeStruct*)&_->this_arg);
            public ref uint InstanceSize => ref _->instance_size;
            public ref ushort VtableCount => ref _->vtable_count;
            public ref ushort InterfaceCount => ref _->interfaces_count;
            public ref ushort InterfaceOffsetsCount => ref _->interface_offsets_count;
            public ref byte TypeHierarchyDepth => ref _->typeHierarchyDepth;
            public ref int NativeSize => ref _->native_size;
            public ref uint ActualSize => ref _->actualSize;
            public ref ushort MethodCount => ref _->method_count;
            public ref ushort FieldCount => ref _->field_count;
            public ref Il2CppClassAttributes Flags => ref *(Il2CppClassAttributes*)&_->flags;
            public ref IntPtr Name => ref *(IntPtr*)&_->name;
            public ref IntPtr Namespace => ref *(IntPtr*)&_->namespaze;
            public ref Il2CppImage* Image => ref _->image;
            public ref Il2CppClass* Parent => ref _->parent;
            public ref Il2CppClass* ElementClass => ref _->element_class;
            public ref Il2CppClass* CastClass => ref _->castClass;
            public ref Il2CppClass* DeclaringType => ref _->declaringType;
            public ref Il2CppClass* Class => ref _->klass;
            public ref Il2CppFieldInfo* Fields => ref _->fields;
            public ref Il2CppMethodInfo** Methods => ref _->methods;
            public ref Il2CppClass** ImplementedInterfaces => ref _->implementedInterfaces;
            public ref Il2CppRuntimeInterfaceOffsetPair* InterfaceOffsets => ref _->interfaceOffsets;
            public ref Il2CppClass** TypeHierarchy => ref _->typeHierarchy;
            public bool ValueType
            {
                get => ByValArg.ValueType && ThisArg.ValueType;
                set { }
            }
            public bool Initialized
            {
                get => this.CheckBit(_bitfield0offset, (int)Il2CppClass_29_1.Bitfield0.BIT_initialized);
                set => this.SetBit(_bitfield0offset, (int)Il2CppClass_29_1.Bitfield0.BIT_initialized, value);
            }
            public bool EnumType
            {
                get => this.CheckBit(_bitfield0offset, (int)Il2CppClass_29_1.Bitfield0.BIT_enumtype);
                set => this.SetBit(_bitfield0offset, (int)Il2CppClass_29_1.Bitfield0.BIT_enumtype, value);
            }
            public bool IsGeneric
            {
                get => this.CheckBit(_bitfield0offset, (int)Il2CppClass_29_1.Bitfield0.BIT_is_generic);
                set => this.SetBit(_bitfield0offset, (int)Il2CppClass_29_1.Bitfield0.BIT_is_generic, value);
            }
            public bool HasReferences
            {
                get => this.CheckBit(_bitfield0offset, (int)Il2CppClass_29_1.Bitfield0.BIT_has_references);
                set => this.SetBit(_bitfield0offset, (int)Il2CppClass_29_1.Bitfield0.BIT_has_references, value);
            }
            public bool SizeInited
            {
                get => this.CheckBit(_bitfield1offset, (int)Il2CppClass_29_1.Bitfield1.BIT_size_inited);
                set => this.SetBit(_bitfield1offset, (int)Il2CppClass_29_1.Bitfield1.BIT_size_inited, value);
            }
            public bool HasFinalize
            {
                get => this.CheckBit(_bitfield1offset, (int)Il2CppClass_29_1.Bitfield1.BIT_has_finalize);
                set => this.SetBit(_bitfield1offset, (int)Il2CppClass_29_1.Bitfield1.BIT_has_finalize, value);
            }
            public bool IsVtableInitialized
            {
                get => this.CheckBit(_bitfield1offset, (int)Il2CppClass_29_1.Bitfield1.BIT_is_vtable_initialized);
                set => this.SetBit(_bitfield1offset, (int)Il2CppClass_29_1.Bitfield1.BIT_is_vtable_initialized, value);
            }
            public bool InitializedAndNoError
            {
                get => this.CheckBit(_bitfield0offset, (int)Il2CppClass_29_1.Bitfield0.BIT_initialized_and_no_error);
                set => this.SetBit(_bitfield0offset, (int)Il2CppClass_29_1.Bitfield0.BIT_initialized_and_no_error, value);
            }
        }

    }

}
