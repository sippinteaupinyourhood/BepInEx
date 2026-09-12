using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Il2CppInterop.Common;
using Il2CppInterop.Common.Attributes;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime.Startup;
using Microsoft.Extensions.Logging;

namespace Il2CppInterop.Runtime;

public static unsafe class IL2CPP
{
    private static readonly Dictionary<string, IntPtr> ourImagesMap = new();
    private static readonly object ImagesLock = new();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr ArrayNewDelegate(IntPtr elementClass, ulong length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr FieldPointerDelegate(IntPtr field);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint FieldOffsetDelegate(IntPtr field);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FieldStaticValueDelegate(IntPtr field, void* value);

    private static readonly Lazy<ArrayNewDelegate> ArrayNew = new(() =>
        Marshal.GetDelegateForFunctionPointer<ArrayNewDelegate>(
            Injection.InjectorHelpers.GetIl2CppExport("il2cpp_array_new")));
    private static readonly Lazy<FieldPointerDelegate> FieldGetName = CreateResolver<FieldPointerDelegate>("il2cpp_field_get_name");
    private static readonly Lazy<FieldPointerDelegate> FieldGetParent = CreateResolver<FieldPointerDelegate>("il2cpp_field_get_parent");
    private static readonly Lazy<FieldPointerDelegate> FieldGetType = CreateResolver<FieldPointerDelegate>("il2cpp_field_get_type");
    private static readonly Lazy<FieldStaticValueDelegate> FieldStaticGetValue = CreateResolver<FieldStaticValueDelegate>("il2cpp_field_static_get_value");
    private static readonly Lazy<FieldStaticValueDelegate> FieldStaticSetValue = CreateResolver<FieldStaticValueDelegate>("il2cpp_field_static_set_value");

    private static Lazy<T> CreateResolver<T>(string name) where T : Delegate
        => new(() => Marshal.GetDelegateForFunctionPointer<T>(Injection.InjectorHelpers.GetIl2CppExport(name)));

    static IL2CPP()
    {
        TryPopulateImages();
    }

    private static bool TryPopulateImages()
    {
        lock (ImagesLock)
        {
            if (ourImagesMap.Count != 0)
                return true;

        var domain = il2cpp_domain_get();
        if (domain == IntPtr.Zero)
        {
                return false;
        }

        nuint assembliesCount = 0;
        var assemblies = il2cpp_domain_get_assemblies(domain, ref assembliesCount);
            if (assemblies == null)
                return false;

            for (nuint i = 0; i < assembliesCount; i++)
            {
                var assembly = assemblies[i];
                var image = il2cpp_assembly_get_image(assembly);
                if (image == IntPtr.Zero)
                    continue;

                RegisterImageName(il2cpp_image_get_name_(image), image);
            }

            return ourImagesMap.Count != 0;
        }
    }

    private static void RegisterImageName(string? name, IntPtr image)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        ourImagesMap[name] = image;
        if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            ourImagesMap[name[..^4]] = image;
        else
            ourImagesMap[name + ".dll"] = image;
    }

    internal static IntPtr GetIl2CppImage(string name)
    {
        TryPopulateImages();
        if (ourImagesMap.ContainsKey(name)) return ourImagesMap[name];
        return IntPtr.Zero;
    }

    internal static IntPtr[] GetIl2CppImages()
    {
        TryPopulateImages();
        lock (ImagesLock)
            return ourImagesMap.Values.Distinct().ToArray();
    }

    public static IntPtr GetIl2CppClass(string assemblyName, string namespaze, string className)
    {
        TryPopulateImages();
        if (!ourImagesMap.TryGetValue(assemblyName, out var image))
        {
            Logger.Instance.LogError("Assembly {AssemblyName} is not registered in il2cpp", assemblyName);
            return IntPtr.Zero;
        }

        var clazz = il2cpp_class_from_name(image, namespaze, className);
        return clazz;
    }

    public static IntPtr GetIl2CppField(IntPtr clazz, string fieldName)
    {
        if (clazz == IntPtr.Zero) return IntPtr.Zero;

        var field = il2cpp_class_get_field_from_name(clazz, fieldName);
        if (field == IntPtr.Zero)
            Logger.Instance.LogError(
                "Field {FieldName} was not found on class {ClassName}", fieldName, il2cpp_class_get_name_(clazz));
        return field;
    }

    public static IntPtr GetIl2CppMethodByToken(IntPtr clazz, int token)
    {
        if (clazz == IntPtr.Zero)
            return NativeStructUtils.GetMethodInfoForMissingMethod(token.ToString());

        var iter = IntPtr.Zero;
        IntPtr method;
        while ((method = il2cpp_class_get_methods(clazz, ref iter)) != IntPtr.Zero)
            if (il2cpp_method_get_token(method) == token)
                return method;

        var className = il2cpp_class_get_name_(clazz);
        Logger.Instance.LogTrace("Unable to find method {ClassName}::{Token}", className, token);

        return NativeStructUtils.GetMethodInfoForMissingMethod(className + "::" + token);
    }

    public static IntPtr GetIl2CppMethod(IntPtr clazz, bool isGeneric, string methodName, string returnTypeName,
        params string[] argTypes)
    {
        if (clazz == IntPtr.Zero)
            return NativeStructUtils.GetMethodInfoForMissingMethod(methodName + "(" + string.Join(", ", argTypes) +
                                                                   ")");

        returnTypeName = Regex.Replace(returnTypeName, "\\`\\d+", "").Replace('/', '.').Replace('+', '.');
        for (var index = 0; index < argTypes.Length; index++)
        {
            var argType = argTypes[index];
            argTypes[index] = Regex.Replace(argType, "\\`\\d+", "").Replace('/', '.').Replace('+', '.');
        }

        var methodsSeen = 0;
        var lastMethod = IntPtr.Zero;
        var iter = IntPtr.Zero;
        IntPtr method;
        while ((method = il2cpp_class_get_methods(clazz, ref iter)) != IntPtr.Zero)
        {
            if (il2cpp_method_get_name_(method) != methodName)
                continue;

            if (il2cpp_method_get_param_count(method) != argTypes.Length)
                continue;

            if (il2cpp_method_is_generic(method) != isGeneric)
                continue;

            var returnType = il2cpp_method_get_return_type(method);
            var returnTypeNameActual = il2cpp_type_get_name_(returnType);
            if (returnTypeNameActual != returnTypeName)
                continue;

            methodsSeen++;
            lastMethod = method;

            var badType = false;
            for (var i = 0; i < argTypes.Length; i++)
            {
                var paramType = il2cpp_method_get_param(method, (uint)i);
                var typeName = il2cpp_type_get_name_(paramType);
                if (typeName != argTypes[i])
                {
                    badType = true;
                    break;
                }
            }

            if (badType) continue;

            return method;
        }

        var className = il2cpp_class_get_name_(clazz);

        if (methodsSeen == 1)
        {
            Logger.Instance.LogTrace(
                "Method {ClassName}::{MethodName} was stubbed with a random matching method of the same name", className, methodName);
            Logger.Instance.LogTrace(
                "Stubby return type/target: {LastMethod} / {ReturnTypeName}", il2cpp_type_get_name_(il2cpp_method_get_return_type(lastMethod)), returnTypeName);
            Logger.Instance.LogTrace("Stubby parameter types/targets follow:");
            for (var i = 0; i < argTypes.Length; i++)
            {
                var paramType = il2cpp_method_get_param(lastMethod, (uint)i);
                var typeName = il2cpp_type_get_name_(paramType);
                Logger.Instance.LogTrace("    {TypeName} / {ArgType}", typeName, argTypes[i]);
            }

            return lastMethod;
        }

        Logger.Instance.LogTrace("Unable to find method {ClassName}::{MethodName}; signature follows", className, methodName);
        Logger.Instance.LogTrace("    return {ReturnTypeName}", returnTypeName);
        foreach (var argType in argTypes)
            Logger.Instance.LogTrace("    {ArgType}", argType);
        Logger.Instance.LogTrace("Available methods of this name follow:");
        iter = IntPtr.Zero;
        while ((method = il2cpp_class_get_methods(clazz, ref iter)) != IntPtr.Zero)
        {
            if (il2cpp_method_get_name_(method) != methodName)
                continue;

            var nParams = il2cpp_method_get_param_count(method);
            Logger.Instance.LogTrace("Method starts");
            Logger.Instance.LogTrace(
                "     return {MethodTypeName}", il2cpp_type_get_name_(il2cpp_method_get_return_type(method)));
            for (var i = 0; i < nParams; i++)
            {
                var paramType = il2cpp_method_get_param(method, (uint)i);
                var typeName = il2cpp_type_get_name_(paramType);
                Logger.Instance.LogTrace("    {TypeName}", typeName);
            }

            return method;
        }

        return NativeStructUtils.GetMethodInfoForMissingMethod(className + "::" + methodName + "(" +
                                                               string.Join(", ", argTypes) + ")");
    }

    public static string? Il2CppStringToManaged(IntPtr il2CppString)
    {
        if (il2CppString == IntPtr.Zero) return null;

        var length = il2cpp_string_length(il2CppString);
        var chars = il2cpp_string_chars(il2CppString);

        return new string(chars, 0, length);
    }

    public static IntPtr ManagedStringToIl2Cpp(string? str)
    {
        if (str == null) return IntPtr.Zero;

        fixed (char* chars = str)
        {
            return il2cpp_string_new_utf16(chars, str.Length);
        }
    }

    public static IntPtr Il2CppObjectBaseToPtr(Il2CppObjectBase obj)
    {
        return obj?.Pointer ?? IntPtr.Zero;
    }

    public static IntPtr Il2CppObjectBaseToPtrNotNull(Il2CppObjectBase obj)
    {
        return obj?.Pointer ?? throw new NullReferenceException();
    }

    public static IntPtr GetIl2CppNestedType(IntPtr enclosingType, string nestedTypeName)
    {
        if (enclosingType == IntPtr.Zero) return IntPtr.Zero;

        if (Il2CppInteropRuntime.Instance.UnityVersion.Major >= 6000)
            return RuntimeReflectionHelper.GetNestedTypeViaReflection(enclosingType, nestedTypeName);

        var iter = IntPtr.Zero;
        IntPtr nestedTypePtr;
        if (il2cpp_class_is_inflated(enclosingType))
        {
            Logger.Instance.LogTrace("Original class was inflated, falling back to reflection");

            return RuntimeReflectionHelper.GetNestedTypeViaReflection(enclosingType, nestedTypeName);
        }

        while ((nestedTypePtr = il2cpp_class_get_nested_types(enclosingType, ref iter)) != IntPtr.Zero)
            if (il2cpp_class_get_name_(nestedTypePtr) == nestedTypeName)
                return nestedTypePtr;

        nestedTypePtr = RuntimeReflectionHelper.GetNestedTypeViaReflection(enclosingType, nestedTypeName);
        if (nestedTypePtr != IntPtr.Zero)
            return nestedTypePtr;

        Logger.Instance.LogError(
            "Nested type {NestedTypeName} on {EnclosingTypeName} not found!", nestedTypeName, il2cpp_class_get_name_(enclosingType));

        return IntPtr.Zero;
    }

    public static void ThrowIfNull(object arg)
    {
        if (arg == null)
            throw new NullReferenceException();
    }

    public static T ResolveICall<T>(string signature) where T : Delegate
    {
        var icallPtr = il2cpp_resolve_icall(signature);
        if (icallPtr == IntPtr.Zero)
        {
            Logger.Instance.LogTrace("ICall {Signature} not resolved", signature);
            return GenerateDelegateForMissingICall<T>(signature);
        }

        return Marshal.GetDelegateForFunctionPointer<T>(icallPtr);
    }

    private static T GenerateDelegateForMissingICall<T>(string signature) where T : Delegate
    {
        var invoke = typeof(T).GetMethod("Invoke")!;

        var trampoline = new DynamicMethod("(missing icall delegate) " + typeof(T).FullName,
            invoke.ReturnType, invoke.GetParameters().Select(it => it.ParameterType).ToArray(), typeof(IL2CPP), true);
        var bodyBuilder = trampoline.GetILGenerator();

        bodyBuilder.Emit(OpCodes.Ldstr, $"ICall with signature {signature} was not resolved");
        bodyBuilder.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor(new[] { typeof(string) })!);
        bodyBuilder.Emit(OpCodes.Throw);

        return (T)trampoline.CreateDelegate(typeof(T));
    }

    public static T? PointerToValueGeneric<T>(IntPtr objectPointer, bool isFieldPointer, bool valueTypeWouldBeBoxed)
    {
        if (isFieldPointer)
        {
            if (il2cpp_class_is_valuetype(Il2CppClassPointerStore<T>.NativeClassPtr))
                objectPointer = il2cpp_value_box(Il2CppClassPointerStore<T>.NativeClassPtr, objectPointer);
            else
                objectPointer = *(IntPtr*)objectPointer;
        }

        if (!valueTypeWouldBeBoxed && il2cpp_class_is_valuetype(Il2CppClassPointerStore<T>.NativeClassPtr))
            objectPointer = il2cpp_value_box(Il2CppClassPointerStore<T>.NativeClassPtr, objectPointer);

        if (typeof(T) == typeof(string))
            return (T)(object)Il2CppStringToManaged(objectPointer);

        if (objectPointer == IntPtr.Zero)
            return default;

        if (typeof(T).IsValueType)
            return Il2CppObjectBase.UnboxUnsafe<T>(objectPointer);

        return Il2CppObjectPool.Get<T>(objectPointer);
    }

    public static string RenderTypeName<T>(bool addRefMarker = false)
    {
        return RenderTypeName(typeof(T), addRefMarker);
    }

    public static string RenderTypeName(Type t, bool addRefMarker = false)
    {
        if (addRefMarker) return RenderTypeName(t) + "&";
        if (t.IsArray) return RenderTypeName(t.GetElementType()) + "[]";
        if (t.IsByRef) return RenderTypeName(t.GetElementType()) + "&";
        if (t.IsPointer) return RenderTypeName(t.GetElementType()) + "*";
        if (t.IsGenericParameter) return t.Name;

        if (t.IsGenericType)
        {
            if (t.TypeHasIl2CppArrayBase())
                return RenderTypeName(t.GetGenericArguments()[0]) + "[]";

            var builder = new StringBuilder();
            builder.Append(t.GetGenericTypeDefinition().FullNameObfuscated().TrimIl2CppPrefix());
            builder.Append('<');
            var genericArguments = t.GetGenericArguments();
            for (var i = 0; i < genericArguments.Length; i++)
            {
                if (i != 0) builder.Append(',');
                builder.Append(RenderTypeName(genericArguments[i]));
            }

            builder.Append('>');
            return builder.ToString();
        }

        if (t == typeof(Il2CppStringArray))
            return "System.String[]";

        return t.FullNameObfuscated().TrimIl2CppPrefix();
    }

    private static string FullNameObfuscated(this Type t)
    {
        var obfuscatedNameAnnotations = t.GetCustomAttribute<ObfuscatedNameAttribute>();
        if (obfuscatedNameAnnotations == null) return t.FullName;
        return obfuscatedNameAnnotations.ObfuscatedName;
    }

    private static string TrimIl2CppPrefix(this string s)
    {
        return s.StartsWith("Il2Cpp") ? s.Substring("Il2Cpp".Length) : s;
    }

    private static bool TypeHasIl2CppArrayBase(this Type type)
    {
        if (type == null) return false;
        if (type.IsConstructedGenericType) type = type.GetGenericTypeDefinition();
        if (type == typeof(Il2CppArrayBase<>)) return true;
        return TypeHasIl2CppArrayBase(type.BaseType);
    }

    // this is called if there's no actual il2cpp_gc_wbarrier_set_field()
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FieldWriteWbarrierStub(IntPtr obj, IntPtr targetAddress, IntPtr value)
    {
        // ignore obj
        *(IntPtr*)targetAddress = value;
    }

    // IL2CPP Functions
    [DllImport("GameAssembly", EntryPoint = "wcFuyyMHlLr", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_init(IntPtr domain_name);

    [DllImport("GameAssembly", EntryPoint = "QTyNUiCgCBo", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_init_utf16(IntPtr domain_name);

    [DllImport("GameAssembly", EntryPoint = "iXVXrGhrTXj", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_shutdown();

    [DllImport("GameAssembly", EntryPoint = "lUEZVhGTrVV", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_set_config_dir(IntPtr config_path);

    [DllImport("GameAssembly", EntryPoint = "AduYkwousTM", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_set_data_dir(IntPtr data_path);

    [DllImport("GameAssembly", EntryPoint = "tbDiH_YGrFK", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_set_temp_dir(IntPtr temp_path);

    [DllImport("GameAssembly", EntryPoint = "SCxSXLpZRKk", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_set_commandline_arguments(int argc, IntPtr argv, IntPtr basedir);

    [DllImport("GameAssembly", EntryPoint = "SnRfpqaKydv", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_set_commandline_arguments_utf16(int argc, IntPtr argv, IntPtr basedir);

    [DllImport("GameAssembly", EntryPoint = "vMJDjWOxm_q", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_set_config_utf16(IntPtr executablePath);

    [DllImport("GameAssembly", EntryPoint = "tnYqQnkpbab", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_set_config(IntPtr executablePath);

    [DllImport("GameAssembly", EntryPoint = "IPcaUSPUepT", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_set_memory_callbacks(IntPtr callbacks);

    [DllImport("GameAssembly", EntryPoint = "wkvazbPVXUm", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_get_corlib();

    [DllImport("GameAssembly", EntryPoint = "OGgcPnJlNDE", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_add_internal_call(IntPtr name, IntPtr method);

    [DllImport("GameAssembly", EntryPoint = "umJrGZzsxtO", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_resolve_icall([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport("GameAssembly", EntryPoint = "GPELQyujGZL", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_alloc(uint size);

    [DllImport("GameAssembly", EntryPoint = "il2cpp_free", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_free(IntPtr ptr);

    public static IntPtr il2cpp_array_class_get(IntPtr element_class, uint rank)
    {
        if (element_class == IntPtr.Zero || rank != 1) return IntPtr.Zero;
        var array = ArrayNew.Value(element_class, 0);
        return array == IntPtr.Zero ? IntPtr.Zero : il2cpp_object_get_class(array);
    }

    public static uint il2cpp_array_length(IntPtr array)
    {
        if (array == IntPtr.Zero) return 0;

        var length = *(nuint*)((byte*)array + 0x18);
        return checked((uint)length);
    }

    [DllImport("GameAssembly", EntryPoint = "yfVZkaCPXYZ", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern uint il2cpp_array_get_byte_length(IntPtr array);

    public static IntPtr il2cpp_array_new(IntPtr elementTypeInfo, ulong length)
        => ArrayNew.Value(elementTypeInfo, length);

    [DllImport("GameAssembly", EntryPoint = "qERBKIjlDGF", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_array_new_specific(IntPtr arrayTypeInfo, ulong length);

    [DllImport("GameAssembly", EntryPoint = "ihzDrcvpVQW", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_array_new_full(IntPtr array_class, ref ulong lengths, ref ulong lower_bounds);

    [DllImport("GameAssembly", EntryPoint = "zxNBCNcPeNC", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_bounded_array_class_get(IntPtr element_class, uint rank,
        [MarshalAs(UnmanagedType.I1)] bool bounded);

    [DllImport("GameAssembly", EntryPoint = "CQMxHDBQpFL", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int il2cpp_array_element_size(IntPtr array_class);

    public static IntPtr il2cpp_assembly_get_image(IntPtr assembly)
        => assembly == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(assembly);

    [DllImport("GameAssembly", EntryPoint = "_rrCxeMjXqI", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_class_enum_basetype(IntPtr klass);

    public static bool il2cpp_class_is_generic(IntPtr klass)
    {
        if (klass == IntPtr.Zero) return false;
        return UnityVersionHandler.Wrap((Il2CppClass*)klass)?.IsGeneric == true;
    }

    public static bool il2cpp_class_is_inflated(IntPtr klass)
    {
        if (klass == IntPtr.Zero) return false;
        var nativeClass = UnityVersionHandler.Wrap((Il2CppClass*)klass);
        return nativeClass?.ByValArg?.Type == Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST;
    }

    [DllImport("GameAssembly", EntryPoint = "RWELuUYNIbW", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_class_is_assignable_from(IntPtr klass, IntPtr oklass);

    [DllImport("GameAssembly", EntryPoint = "mexBDgIHWvt", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_class_is_subclass_of(IntPtr klass, IntPtr klassc,
        [MarshalAs(UnmanagedType.I1)] bool check_interfaces);

    [DllImport("GameAssembly", EntryPoint = "cxmcROhQlMn", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_class_has_parent(IntPtr klass, IntPtr klassc);

    [DllImport("GameAssembly", EntryPoint = "oQbALanOaVu", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_class_from_il2cpp_type(IntPtr type);

    [DllImport("GameAssembly", EntryPoint = "eGcVXosuX_V", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_class_from_name(IntPtr image, [MarshalAs(UnmanagedType.LPUTF8Str)] string namespaze,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    public static IntPtr il2cpp_class_from_system_type(IntPtr type)
    {
        if (type == IntPtr.Zero)
            return IntPtr.Zero;

        var nativeType = *(IntPtr*)((byte*)type + IntPtr.Size * 2);
        return nativeType == IntPtr.Zero ? IntPtr.Zero : il2cpp_class_from_type(nativeType);
    }

    [DllImport("GameAssembly", EntryPoint = "CpyYDPJYEPq", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_class_get_element_class(IntPtr klass);

    public static IntPtr il2cpp_class_get_events(IntPtr klass, ref IntPtr iter)
        => GetClassStructArrayElement(klass, ref iter, 0x58, 0x122, 0x38);

    public static IntPtr il2cpp_class_get_fields(IntPtr klass, ref IntPtr iter)
        => GetClassStructArrayElement(klass, ref iter, 0x80, 0x124, 0x20);

    public static IntPtr il2cpp_class_get_nested_types(IntPtr klass, ref IntPtr iter)
        => GetClassPointerArrayElement(klass, ref iter, 0xA0, 0x128);

    public static IntPtr il2cpp_class_get_interfaces(IntPtr klass, ref IntPtr iter)
        => GetClassPointerArrayElement(klass, ref iter, 0xA8, 0x12C);

    public static IntPtr il2cpp_class_get_properties(IntPtr klass, ref IntPtr iter)
        => GetClassStructArrayElement(klass, ref iter, 0x90, 0x126, 0x28);

    public static IntPtr il2cpp_class_get_property_from_name(IntPtr klass, IntPtr name)
    {
        if (klass == IntPtr.Zero || name == IntPtr.Zero)
            return IntPtr.Zero;

        var requestedName = Marshal.PtrToStringUTF8(name);
        for (var current = klass; current != IntPtr.Zero; current = il2cpp_class_get_parent(current))
        {
            var iter = IntPtr.Zero;
            IntPtr property;
            while ((property = il2cpp_class_get_properties(current, ref iter)) != IntPtr.Zero)
                if (string.Equals(Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(property, 0x08)), requestedName,
                        StringComparison.Ordinal))
                    return property;
        }

        return IntPtr.Zero;
    }

    [DllImport("GameAssembly", EntryPoint = "FZOoKkQcXXF", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_class_get_field_from_name(IntPtr klass,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport("GameAssembly", EntryPoint = "nVnxgXSkJqb", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_class_get_methods(IntPtr klass, ref IntPtr iter);

    public static IntPtr il2cpp_class_get_method_from_name(IntPtr klass, string name, int argsCount)
    {
        if (klass == IntPtr.Zero || name == null) return IntPtr.Zero;

        for (var current = klass; current != IntPtr.Zero; current = il2cpp_class_get_parent(current))
        {
            var iterator = IntPtr.Zero;
            IntPtr method;
            while ((method = il2cpp_class_get_methods(current, ref iterator)) != IntPtr.Zero)
            {
                var nativeMethod = UnityVersionHandler.Wrap((Il2CppMethodInfo*)method);
                if (nativeMethod == null || nativeMethod.Name == IntPtr.Zero) continue;
                if (argsCount >= 0 && nativeMethod.ParametersCount != argsCount) continue;
                if (string.Equals(Marshal.PtrToStringUTF8(nativeMethod.Name), name, StringComparison.Ordinal))
                    return method;
            }
        }

        return IntPtr.Zero;
    }

    public static nint il2cpp_class_get_name(IntPtr klass)
        => klass == IntPtr.Zero ? IntPtr.Zero : UnityVersionHandler.Wrap((Il2CppClass*)klass).Name;

    public static string? il2cpp_class_get_name_(IntPtr klass)
        => Marshal.PtrToStringUTF8(il2cpp_class_get_name(klass));

    public static nint il2cpp_class_get_namespace(IntPtr klass)
        => klass == IntPtr.Zero ? IntPtr.Zero : UnityVersionHandler.Wrap((Il2CppClass*)klass).Namespace;

    public static string? il2cpp_class_get_namespace_(IntPtr klass)
        => Marshal.PtrToStringUTF8(il2cpp_class_get_namespace(klass));

    private static IntPtr GetClassPointerArrayElement(IntPtr klass, ref IntPtr iter, int arrayOffset, int countOffset)
    {
        if (klass == IntPtr.Zero)
            return IntPtr.Zero;

        var count = Marshal.ReadInt16(klass, countOffset);
        if (count <= 0)
            return IntPtr.Zero;

        var array = Marshal.ReadIntPtr(klass, arrayOffset);
        if (array == IntPtr.Zero)
            return IntPtr.Zero;

        var index = iter == IntPtr.Zero ? 0 : iter.ToInt64() - 1;
        if ((ulong)index >= (ulong)count)
            return IntPtr.Zero;

        iter = new IntPtr(index + 2);
        return Marshal.ReadIntPtr(array, checked((int)(index * IntPtr.Size)));
    }

    private static IntPtr GetClassStructArrayElement(IntPtr klass, ref IntPtr iter, int arrayOffset, int countOffset,
        int elementSize)
    {
        if (klass == IntPtr.Zero)
            return IntPtr.Zero;

        var count = Marshal.ReadInt16(klass, countOffset);
        var array = Marshal.ReadIntPtr(klass, arrayOffset);
        if (count <= 0 || array == IntPtr.Zero)
            return IntPtr.Zero;

        var index = iter == IntPtr.Zero ? 0 : iter.ToInt64() - 1;
        if ((ulong)index >= (ulong)count)
            return IntPtr.Zero;

        iter = new IntPtr(index + 2);
        return IntPtr.Add(array, checked((int)(index * elementSize)));
    }

    public static IntPtr il2cpp_class_get_parent(IntPtr klass)
        => klass == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(klass, 0x88);

    public static IntPtr il2cpp_class_get_declaring_type(IntPtr klass)
        => klass == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(klass, 0x70);

    [DllImport("GameAssembly", EntryPoint = "tTSXTpXzULO", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int il2cpp_class_instance_size(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "mkvScKEqsqc", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern uint il2cpp_class_num_fields(IntPtr enumKlass);

    public static bool il2cpp_class_is_valuetype(IntPtr klass)
        => klass != IntPtr.Zero && UnityVersionHandler.Wrap((Il2CppClass*)klass).ValueType;

    [DllImport("GameAssembly", EntryPoint = "JHbMc_nxoYZ", CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Ansi)]
    public static extern int il2cpp_class_value_size(IntPtr klass, ref uint align);

    public static bool il2cpp_class_is_blittable(IntPtr klass)
        => klass != IntPtr.Zero && (Marshal.ReadByte(klass, 0x136) & 0x08) != 0;

    public static int il2cpp_class_get_flags(IntPtr klass)
        => klass == IntPtr.Zero ? 0 : Marshal.ReadInt32(klass, 0x118);

    [DllImport("GameAssembly", EntryPoint = "McSomelMpJa", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_class_is_abstract(IntPtr klass);

    public static bool il2cpp_class_is_interface(IntPtr klass)
        => klass != IntPtr.Zero && (Marshal.ReadInt32(klass, 0x118) & 0x20) != 0;

    [DllImport("GameAssembly", EntryPoint = "muJafdnTwns", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int il2cpp_class_array_element_size(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "oQbALanOaVu", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_class_from_type(IntPtr type);

    [DllImport("GameAssembly", EntryPoint = "rGYyDkAT_iT", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_class_get_type(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "ElVuffXvuGg", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern uint il2cpp_class_get_type_token(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "sXGLbRb_cLn", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_class_has_attribute(IntPtr klass, IntPtr attr_class);

    [DllImport("GameAssembly", EntryPoint = "jtTKPODKugH", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_class_has_references(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "QFVTvtcihYB", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_class_is_enum(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "ovOClb_DVbi", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_class_get_image(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "gfaZrSmHYCC", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern nint il2cpp_class_get_assemblyname(IntPtr klass);

    public static string? il2cpp_class_get_assemblyname_(IntPtr klass)
        => Marshal.PtrToStringUTF8(il2cpp_class_get_assemblyname(klass));

    [DllImport("GameAssembly", EntryPoint = "NdrhSXzZxyy", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int il2cpp_class_get_rank(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "KrbVlIu_dQI", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern uint il2cpp_class_get_bitmap_size(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "JwfRbPNwlJk", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_class_get_bitmap(IntPtr klass, ref uint bitmap);

    public static bool il2cpp_stats_dump_to_file(IntPtr path) => false;

    //[DllImport("GameAssembly", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    //public extern static ulong il2cpp_stats_get_value(IL2CPP_Stat stat);
    public static IntPtr il2cpp_domain_get() => new(1);

    [DllImport("GameAssembly", EntryPoint = "cDsdGuvksgW", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_domain_assembly_open(IntPtr domain, IntPtr name);

    [DllImport("GameAssembly", EntryPoint = "tReyvQBXGqO", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr* il2cpp_domain_get_assemblies(IntPtr domain, ref nuint size);

    [DllImport("GameAssembly", EntryPoint = "lEgPxzCptOX", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr
        il2cpp_exception_from_name_msg(IntPtr image, IntPtr name_space, IntPtr name, IntPtr msg);

    [DllImport("GameAssembly", EntryPoint = "kYcHLF_Lrgq", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_get_exception_argument_null(IntPtr arg);

    [DllImport("GameAssembly", EntryPoint = "bRVcFdppOes", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_format_exception(IntPtr ex, void* message, int message_size);

    [DllImport("GameAssembly", EntryPoint = "sBBTSmkeSCa", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_format_stack_trace(IntPtr ex, void* output, int output_size);

    [DllImport("GameAssembly", EntryPoint = "mkujMKTache", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_unhandled_exception(IntPtr ex);

    [DllImport("GameAssembly", EntryPoint = "KehzMY_GKJm", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int il2cpp_field_get_flags(IntPtr field);

    public static nint il2cpp_field_get_name(IntPtr field) => FieldGetName.Value(field);

    public static string? il2cpp_field_get_name_(IntPtr field)
        => Marshal.PtrToStringUTF8(il2cpp_field_get_name(field));

    public static IntPtr il2cpp_field_get_parent(IntPtr field) => FieldGetParent.Value(field);

    public static uint il2cpp_field_get_offset(IntPtr field)
        => field == IntPtr.Zero ? 0u : unchecked((uint)Marshal.ReadInt32(field, 0x1C));

    public static IntPtr il2cpp_field_get_type(IntPtr field) => FieldGetType.Value(field);

    [DllImport("GameAssembly", EntryPoint = "cQFrZhNYEjS", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_field_get_value(IntPtr obj, IntPtr field, void* value);

    [DllImport("GameAssembly", EntryPoint = "NOqHdYrbQCN", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_field_get_value_object(IntPtr field, IntPtr obj);

    [DllImport("GameAssembly", EntryPoint = "yxYpdphblDQ", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_field_has_attribute(IntPtr field, IntPtr attr_class);

    [DllImport("GameAssembly", EntryPoint = "U_mbVCbpXNM", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_field_set_value(IntPtr obj, IntPtr field, void* value);

    public static void il2cpp_field_static_get_value(IntPtr field, void* value) => FieldStaticGetValue.Value(field, value);

    public static void il2cpp_field_static_set_value(IntPtr field, void* value) => FieldStaticSetValue.Value(field, value);

    [DllImport("GameAssembly", EntryPoint = "btrcrMLFgKu", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_field_set_value_object(IntPtr instance, IntPtr field, IntPtr value);

    [DllImport("GameAssembly", EntryPoint = "opSboBybIfA", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_gc_collect(int maxGenerations);

    [DllImport("GameAssembly", EntryPoint = "YLzGJghUUJb", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int il2cpp_gc_collect_a_little();

    [DllImport("GameAssembly", EntryPoint = "mJtCwXSZrtR", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_gc_disable();

    [DllImport("GameAssembly", EntryPoint = "UrUeQtSsoCM", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_gc_enable();

    [DllImport("GameAssembly", EntryPoint = "YahZmmPNIkq", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_gc_is_disabled();

    [DllImport("GameAssembly", EntryPoint = "_CiVzDueBB_", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern long il2cpp_gc_get_used_size();

    [DllImport("GameAssembly", EntryPoint = "_CiVzDueBB_", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern long il2cpp_gc_get_heap_size();

    [DllImport("GameAssembly", EntryPoint = "ZveEKZkuxLD", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_gc_wbarrier_set_field(IntPtr obj, IntPtr targetAddress, IntPtr gcObj);

    [DllImport("GameAssembly", EntryPoint = "KUZulelZoCy", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern nint il2cpp_gchandle_new(IntPtr obj, [MarshalAs(UnmanagedType.I1)] bool pinned);

    [DllImport("GameAssembly", EntryPoint = "MHaleuaBdai", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern nint il2cpp_gchandle_new_weakref(IntPtr obj,
        [MarshalAs(UnmanagedType.I1)] bool track_resurrection);

    [DllImport("GameAssembly", EntryPoint = "il2cpp_gchandle_get_target", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_gchandle_get_target(nint gchandle);

    [DllImport("GameAssembly", EntryPoint = "WxVSQuJkWPd", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_gchandle_free(nint gchandle);

    public static IntPtr il2cpp_unity_liveness_calculation_begin(IntPtr filter, int max_object_count,
        IntPtr callback, IntPtr userdata, IntPtr onWorldStarted, IntPtr onWorldStopped) => IntPtr.Zero;

    public static void il2cpp_unity_liveness_calculation_end(IntPtr state)
    {
    }

    public static void il2cpp_unity_liveness_calculation_from_root(IntPtr root, IntPtr state)
    {
    }

    public static void il2cpp_unity_liveness_calculation_from_statics(IntPtr state)
    {
    }

    public static IntPtr il2cpp_method_get_return_type(IntPtr method)
        => method == IntPtr.Zero
            ? IntPtr.Zero
            : (IntPtr)UnityVersionHandler.Wrap((Il2CppMethodInfo*)method).ReturnType;

    public static IntPtr il2cpp_method_get_declaring_type(IntPtr method)
        => method == IntPtr.Zero
            ? IntPtr.Zero
            : (IntPtr)UnityVersionHandler.Wrap((Il2CppMethodInfo*)method).Class;

    public static nint il2cpp_method_get_name(IntPtr method)
    {
        if (method == IntPtr.Zero)
            return IntPtr.Zero;

        var nativeMethod = UnityVersionHandler.Wrap((Il2CppMethodInfo*)method);
        return nativeMethod == null ? IntPtr.Zero : nativeMethod.Name;
    }

    public static string? il2cpp_method_get_name_(IntPtr method)
        => Marshal.PtrToStringUTF8(il2cpp_method_get_name(method));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntPtr il2cpp_method_get_from_reflection(IntPtr method)
    {
        if (method == IntPtr.Zero) return IntPtr.Zero;

        Il2CppReflectionMethod* reflectionMethod = (Il2CppReflectionMethod*)method;
        return (IntPtr)reflectionMethod->method;
    }

    [DllImport("GameAssembly", EntryPoint = nameof(il2cpp_method_get_from_reflection), CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern IntPtr _il2cpp_method_get_from_reflection(IntPtr method);

    public static IntPtr il2cpp_method_get_object(IntPtr method, IntPtr refclass)
    {
        if (method == IntPtr.Zero) return IntPtr.Zero;
        var runtimeMethodInfoClass = GetIl2CppClass("mscorlib.dll", "System.Reflection", "RuntimeMethodInfo");
        if (runtimeMethodInfoClass == IntPtr.Zero) return IntPtr.Zero;
        var result = il2cpp_object_new(runtimeMethodInfoClass);
        if (result == IntPtr.Zero) return IntPtr.Zero;
        var reflectionMethod = (Il2CppReflectionMethod*)result;
        reflectionMethod->method = (Il2CppMethodInfo*)method;
        reflectionMethod->name = null;
        reflectionMethod->reftype = IntPtr.Zero;
        return result;
    }

    public static bool il2cpp_method_is_generic(IntPtr method)
        => method != IntPtr.Zero && UnityVersionHandler.Wrap((Il2CppMethodInfo*)method).IsGeneric;

    public static bool il2cpp_method_is_inflated(IntPtr method)
        => method != IntPtr.Zero && UnityVersionHandler.Wrap((Il2CppMethodInfo*)method).IsInflated;

    public static bool il2cpp_method_is_instance(IntPtr method)
        => method != IntPtr.Zero &&
           (UnityVersionHandler.Wrap((Il2CppMethodInfo*)method).Flags & Il2CppMethodFlags.METHOD_ATTRIBUTE_STATIC) == 0;

    public static uint il2cpp_method_get_param_count(IntPtr method)
        => method == IntPtr.Zero
            ? 0u
            : UnityVersionHandler.Wrap((Il2CppMethodInfo*)method)?.ParametersCount ?? 0u;

    public static IntPtr il2cpp_method_get_param(IntPtr method, uint index)
    {
        if (method == IntPtr.Zero)
            return IntPtr.Zero;

        var nativeMethod = UnityVersionHandler.Wrap((Il2CppMethodInfo*)method);
        if (nativeMethod == null || index >= nativeMethod.ParametersCount)
            return IntPtr.Zero;

        return (IntPtr)UnityVersionHandler.Wrap(nativeMethod.Parameters, checked((int)index)).ParameterType;
    }

    public static IntPtr il2cpp_method_get_class(IntPtr method)
        => il2cpp_method_get_declaring_type(method);

    [DllImport("GameAssembly", EntryPoint = "lkjFRnNxQvt", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_method_has_attribute(IntPtr method, IntPtr attr_class);

    public static uint il2cpp_method_get_flags(IntPtr method, ref uint iflags)
    {
        if (method == IntPtr.Zero)
        {
            iflags = 0;
            return 0;
        }

        iflags = (ushort)Marshal.ReadInt16(method, 0x4E);
        return (uint)UnityVersionHandler.Wrap((Il2CppMethodInfo*)method).Flags;
    }

    public static uint il2cpp_method_get_token(IntPtr method)
        => method == IntPtr.Zero ? 0u : UnityVersionHandler.Wrap((Il2CppMethodInfo*)method).Token;

    public static nint il2cpp_method_get_param_name(IntPtr method, uint index)
        => IntPtr.Zero;

    public static string? il2cpp_method_get_param_name_(IntPtr method, uint index)
        => Marshal.PtrToStringUTF8(il2cpp_method_get_param_name(method, index));

    public static void il2cpp_profiler_install(IntPtr prof, IntPtr shutdown_callback)
    {
    }

    // [DllImport("GameAssembly", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    // public extern static void il2cpp_profiler_set_events(IL2CPP_ProfileFlags events);
    public static void il2cpp_profiler_install_enter_leave(IntPtr enter, IntPtr fleave)
    {
    }

    public static void il2cpp_profiler_install_allocation(IntPtr callback)
    {
    }

    public static void il2cpp_profiler_install_gc(IntPtr callback, IntPtr heap_resize_callback)
    {
    }

    public static void il2cpp_profiler_install_fileio(IntPtr callback)
    {
    }

    public static void il2cpp_profiler_install_thread(IntPtr start, IntPtr end)
    {
    }

    [DllImport("GameAssembly", EntryPoint = "NmRHXzqdRCd", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern uint il2cpp_property_get_flags(IntPtr prop);

    [DllImport("GameAssembly", EntryPoint = "_LEjTkwsJmL", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_property_get_get_method(IntPtr prop);

    [DllImport("GameAssembly", EntryPoint = "WTbzsLdsaHw", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_property_get_set_method(IntPtr prop);

    public static nint il2cpp_property_get_name(IntPtr prop)
        => prop == IntPtr.Zero
            ? IntPtr.Zero
            : UnityVersionHandler.Wrap((Il2CppPropertyInfo*)prop)?.Name ?? IntPtr.Zero;

    public static string? il2cpp_property_get_name_(IntPtr prop)
        => Marshal.PtrToStringUTF8(il2cpp_property_get_name(prop));

    [DllImport("GameAssembly", EntryPoint = "yi_DSPPSFyN", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_property_get_parent(IntPtr prop);

    public static IntPtr il2cpp_object_get_class(IntPtr obj)
        => obj == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)obj;

    public static uint il2cpp_object_get_size(IntPtr obj)
    {
        var klass = il2cpp_object_get_class(obj);
        return klass == IntPtr.Zero ? 0 : *(uint*)((byte*)klass + 0xF8);
    }

    [DllImport("GameAssembly", EntryPoint = "bdetPLUGbMl", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_object_get_virtual_method(IntPtr obj, IntPtr method);

    [DllImport("GameAssembly", EntryPoint = "fdQySFUbQRI", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_object_new(IntPtr klass);


    public static IntPtr il2cpp_object_unbox(IntPtr obj)
        => obj == IntPtr.Zero ? IntPtr.Zero : obj + (2 * IntPtr.Size);

    [DllImport("GameAssembly", EntryPoint = "kfRjLqgoDfp", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_value_box(IntPtr klass, IntPtr data);

    [DllImport("GameAssembly", EntryPoint = "hjlcmtwuiOn", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_monitor_enter(IntPtr obj);

    [DllImport("GameAssembly", EntryPoint = "XciMatVgZBL", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_monitor_try_enter(IntPtr obj, uint timeout);

    [DllImport("GameAssembly", EntryPoint = "VAxDZKpDGYp", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_monitor_exit(IntPtr obj);

    [DllImport("GameAssembly", EntryPoint = "kpSkOkDOHex", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_monitor_pulse(IntPtr obj);

    [DllImport("GameAssembly", EntryPoint = "yiUnJdHnSjQ", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_monitor_pulse_all(IntPtr obj);

    [DllImport("GameAssembly", EntryPoint = "wIDbvDtpBXN", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_monitor_wait(IntPtr obj);

    [DllImport("GameAssembly", EntryPoint = "legIIoGfFPc", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_monitor_try_wait(IntPtr obj, uint timeout);

    [DllImport("GameAssembly", EntryPoint = "UPSmEOKKvNa", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_runtime_invoke(IntPtr method, IntPtr obj, void** param, ref IntPtr exc);

    [DllImport("GameAssembly", EntryPoint = "wxwmdwXHzJB", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    // param can be of Il2CppObject*
    public static extern IntPtr il2cpp_runtime_invoke_convert_args(IntPtr method, IntPtr obj, void** param,
        int paramCount, ref IntPtr exc);

    [DllImport("GameAssembly", EntryPoint = "zoAvzVWlTDk", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_runtime_class_init(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "_yPYMXAfWVB", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_runtime_object_init(IntPtr obj);

    [DllImport("GameAssembly", EntryPoint = "vCoeiHusAlp", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_runtime_object_init_exception(IntPtr obj, ref IntPtr exc);

    // [DllImport("GameAssembly", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    // public extern static void il2cpp_runtime_unhandled_exception_policy_set(IL2CPP_RuntimeUnhandledExceptionPolicy value);
    [DllImport("GameAssembly", EntryPoint = "zHAxRjeysoX", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int il2cpp_string_length(IntPtr str);

    [DllImport("GameAssembly", EntryPoint = "mrQEtxuutFi", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern char* il2cpp_string_chars(IntPtr str);

    [DllImport("GameAssembly", EntryPoint = "rRpOavDcJjQ", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_string_new(string str);

    [DllImport("GameAssembly", EntryPoint = "eVrtxFNoNgv", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_string_new_len(string str, uint length);

    [DllImport("GameAssembly", EntryPoint = "SlRLNWxveku", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_string_new_utf16(char* text, int len);

    [DllImport("GameAssembly", EntryPoint = "rRpOavDcJjQ", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_string_new_wrapper(string str);

    [DllImport("GameAssembly", EntryPoint = "EFEFMOTiZFT", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_string_intern(string str);

    [DllImport("GameAssembly", EntryPoint = "xAxRUuBmYhI", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_string_is_interned(string str);

    [DllImport("GameAssembly", EntryPoint = "comANrJMGDW", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_thread_current();

    [DllImport("GameAssembly", EntryPoint = "OPmhRVifFhM", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_thread_attach(IntPtr domain);

    [DllImport("GameAssembly", EntryPoint = "mFRvBORPgnr", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_thread_detach(IntPtr thread);

    [DllImport("GameAssembly", EntryPoint = "POvoavzjjgL", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void** il2cpp_thread_get_all_attached_threads(ref uint size);

    [DllImport("GameAssembly", EntryPoint = "xcTy__DTPCr", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_is_vm_thread(IntPtr thread);

    [DllImport("GameAssembly", EntryPoint = "mdpkZLFFlGe", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_current_thread_walk_frame_stack(IntPtr func, IntPtr user_data);

    [DllImport("GameAssembly", EntryPoint = "abGVMYTYaEX", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_thread_walk_frame_stack(IntPtr thread, IntPtr func, IntPtr user_data);

    [DllImport("GameAssembly", EntryPoint = "YOhMOKNfFrq", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_current_thread_get_top_frame(IntPtr frame);

    [DllImport("GameAssembly", EntryPoint = "GPNWhwVgomj", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_thread_get_top_frame(IntPtr thread, IntPtr frame);

    [DllImport("GameAssembly", EntryPoint = "PnOfWnHiQrW", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_current_thread_get_frame_at(int offset, IntPtr frame);

    [DllImport("GameAssembly", EntryPoint = "JbKd_rQmCPM", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_thread_get_frame_at(IntPtr thread, int offset, IntPtr frame);

    [DllImport("GameAssembly", EntryPoint = "YaJwgmYLVQg", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int il2cpp_current_thread_get_stack_depth();

    [DllImport("GameAssembly", EntryPoint = "VLjfTwiNhlm", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int il2cpp_thread_get_stack_depth(IntPtr thread);

    [DllImport("GameAssembly", EntryPoint = "iEbdxvAvURT", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_type_get_object(IntPtr type);

    [DllImport("GameAssembly", EntryPoint = "IZsiBCsnQgy", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int il2cpp_type_get_type(IntPtr type);

    [DllImport("GameAssembly", EntryPoint = "QcFzNwJhNiH", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_type_get_class_or_element_class(IntPtr type);

    [DllImport("GameAssembly", EntryPoint = "xxRQNlWTvVU", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern nint il2cpp_type_get_name(IntPtr type);

    public static string? il2cpp_type_get_name_(IntPtr type)
        => Marshal.PtrToStringUTF8(il2cpp_type_get_name(type));

    public static bool il2cpp_type_is_byref(IntPtr type)
        => type != IntPtr.Zero
           && (UnityVersionHandler.Wrap((Il2CppTypeStruct*)type)?.ByRef ?? false);

    [DllImport("GameAssembly", EntryPoint = "DNQolFF_Rkf", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern uint il2cpp_type_get_attrs(IntPtr type);

    [DllImport("GameAssembly", EntryPoint = "xiXqcTFLsSA", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_type_equals(IntPtr type, IntPtr otherType);

    [DllImport("GameAssembly", EntryPoint = "TNqkLCvleZp", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_type_get_assembly_qualified_name(IntPtr type);

    [DllImport("GameAssembly", EntryPoint = "WTbzsLdsaHw", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_image_get_assembly(IntPtr image);

    [DllImport("GameAssembly", EntryPoint = "zdVUCEElvaJ", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern nint il2cpp_image_get_name(IntPtr image);

    public static string? il2cpp_image_get_name_(IntPtr image)
        => Marshal.PtrToStringUTF8(il2cpp_image_get_name(image));

    [DllImport("GameAssembly", EntryPoint = "yi_DSPPSFyN", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern nint il2cpp_image_get_filename(IntPtr image);

    public static string? il2cpp_image_get_filename_(IntPtr image)
        => Marshal.PtrToStringUTF8(il2cpp_image_get_filename(image));

    [DllImport("GameAssembly", EntryPoint = "mRlTxuIYXOB", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_image_get_entry_point(IntPtr image);

    [DllImport("GameAssembly", EntryPoint = "NmRHXzqdRCd", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern uint il2cpp_image_get_class_count(IntPtr image);

    [DllImport("GameAssembly", EntryPoint = "VyIwDMHEORj", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_image_get_class(IntPtr image, uint index);

    public static IntPtr il2cpp_capture_memory_snapshot() => IntPtr.Zero;

    public static void il2cpp_free_captured_memory_snapshot(IntPtr snapshot)
    {
    }

    [DllImport("GameAssembly", EntryPoint = "ylQOdUsuWGE", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_set_find_plugin_callback(IntPtr method);

    public static void il2cpp_register_log_callback(IntPtr method)
    {
    }

    public static void il2cpp_debugger_set_agent_options(IntPtr options)
    {
    }

    public static bool il2cpp_is_debugger_attached() => false;

    public static void il2cpp_unity_install_unitytls_interface(void* unitytlsInterfaceStruct)
    {
    }

    [DllImport("GameAssembly", EntryPoint = "zdXrqEeRYCm", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_custom_attrs_from_class(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "bMTGFOUnXhr", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_custom_attrs_from_method(IntPtr method);

    [DllImport("GameAssembly", EntryPoint = "ViNBsYmCAMk", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_custom_attrs_get_attr(IntPtr ainfo, IntPtr attr_klass);

    [DllImport("GameAssembly", EntryPoint = "vjDJcqzusNH", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_custom_attrs_has_attr(IntPtr ainfo, IntPtr attr_klass);

    [DllImport("GameAssembly", EntryPoint = "Pj_sIHRuXRz", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_custom_attrs_construct(IntPtr cinfo);

    [DllImport("GameAssembly", EntryPoint = "hTDtFrSKPwe", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_custom_attrs_free(IntPtr ainfo);
}
