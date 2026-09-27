using System;
using System.Collections.Generic;
using System.IO;
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
    private delegate IntPtr ClassIteratorDelegate(IntPtr klass, ref IntPtr iterator);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint FieldOffsetDelegate(IntPtr field);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FieldStaticValueDelegate(IntPtr field, void* value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr ClassFromTypeDelegate(IntPtr type);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint MethodParamCountDelegate(IntPtr method);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr MethodParamDelegate(IntPtr method, uint index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr MethodObjectDelegate(IntPtr method, IntPtr refclass);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte MethodPredicateDelegate(IntPtr method);

    private static readonly Lazy<ArrayNewDelegate> ArrayNew = new(() =>
        Marshal.GetDelegateForFunctionPointer<ArrayNewDelegate>(
            Injection.InjectorHelpers.GetIl2CppExport("il2cpp_array_new")));
    private static readonly Lazy<FieldPointerDelegate> FieldGetName = CreateResolver<FieldPointerDelegate>("il2cpp_field_get_name");
    private static readonly Lazy<FieldPointerDelegate> FieldGetParent = CreateResolver<FieldPointerDelegate>("il2cpp_field_get_parent");
    private static readonly Lazy<FieldPointerDelegate> FieldGetType = CreateResolver<FieldPointerDelegate>("il2cpp_field_get_type");
    private static readonly Lazy<ClassIteratorDelegate> ClassGetEvents = CreateResolver<ClassIteratorDelegate>("il2cpp_class_get_events");
    private static readonly Lazy<ClassIteratorDelegate> ClassGetFields = CreateResolver<ClassIteratorDelegate>("il2cpp_class_get_fields");
    private static readonly Lazy<ClassIteratorDelegate> ClassGetNestedTypes = CreateResolver<ClassIteratorDelegate>("il2cpp_class_get_nested_types");
    private static readonly Lazy<ClassIteratorDelegate> ClassGetInterfaces = CreateResolver<ClassIteratorDelegate>("il2cpp_class_get_interfaces");
    private static readonly Lazy<ClassIteratorDelegate> ClassGetProperties = CreateResolver<ClassIteratorDelegate>("il2cpp_class_get_properties");
    private static readonly Lazy<FieldStaticValueDelegate> FieldStaticGetValue = new(() =>
        Marshal.GetDelegateForFunctionPointer<FieldStaticValueDelegate>(
            NativeLibrary.Load("GameAssembly.dll") + 0xAC8AF0));
    private static readonly Lazy<FieldStaticValueDelegate> FieldStaticSetValue = new(() =>
        Marshal.GetDelegateForFunctionPointer<FieldStaticValueDelegate>(
            NativeLibrary.Load("GameAssembly.dll") + 0xAC8B00));
    private static readonly Lazy<ClassFromTypeDelegate> ClassFromType = new(() =>
        Marshal.GetDelegateForFunctionPointer<ClassFromTypeDelegate>(
            NativeLibrary.Load("GameAssembly.dll") + 0xAC7F20));
    private static readonly Lazy<MethodParamCountDelegate> MethodGetParamCount = new(() =>
        Marshal.GetDelegateForFunctionPointer<MethodParamCountDelegate>(
            NativeLibrary.Load("GameAssembly.dll") + 0xAC9100));
    private static readonly Lazy<MethodParamDelegate> MethodGetParam = new(() =>
        Marshal.GetDelegateForFunctionPointer<MethodParamDelegate>(
            NativeLibrary.Load("GameAssembly.dll") + 0xAC9110));
    private static readonly Lazy<MethodObjectDelegate> MethodGetObject = new(() =>
        Marshal.GetDelegateForFunctionPointer<MethodObjectDelegate>(
            NativeLibrary.Load("GameAssembly.dll") + 0xAC90C0));
    private static readonly Lazy<MethodPredicateDelegate> MethodIsGeneric = new(() =>
        Marshal.GetDelegateForFunctionPointer<MethodPredicateDelegate>(
            NativeLibrary.Load("GameAssembly.dll") + 0xAC90D0));
    private static readonly Lazy<MethodPredicateDelegate> MethodIsInflated = new(() =>
        Marshal.GetDelegateForFunctionPointer<MethodPredicateDelegate>(
            NativeLibrary.Load("GameAssembly.dll") + 0xAC90E0));
    private static readonly Lazy<MethodPredicateDelegate> MethodIsInstance = new(() =>
        Marshal.GetDelegateForFunctionPointer<MethodPredicateDelegate>(
            NativeLibrary.Load("GameAssembly.dll") + 0xAC90F0));

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

                var imageName = il2cpp_image_get_name_(image);
                RegisterImageName(imageName, image);
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

        var genericDescriptor = (token & int.MinValue) != 0;
        var genericOrdinal = token & 0xFFFF;
        var genericParameterCount = (token >> 16) & 0x7FFF;
        var iter = IntPtr.Zero;
        var methods = new List<IntPtr>();
        IntPtr method;
        while ((method = il2cpp_class_get_methods(clazz, ref iter)) != IntPtr.Zero)
            methods.Add(method);

        methods.Sort((left, right) =>
            unchecked((uint)Marshal.ReadInt32(left, 0x48)).CompareTo(
                unchecked((uint)Marshal.ReadInt32(right, 0x48))));
        if (genericDescriptor)
        {
            foreach (var candidate in methods)
            {
                if (!il2cpp_method_is_generic(candidate)
                    || il2cpp_method_get_param_count(candidate) != genericParameterCount)
                    continue;
                if (genericOrdinal-- == 0)
                    return candidate;
            }

            return NativeStructUtils.GetMethodInfoForMissingMethod(token.ToString());
        }

        if ((uint)token < (uint)methods.Count)
            return methods[token];

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
        var genericCandidates = 0;
        var lastGenericCandidate = IntPtr.Zero;
        var iter = IntPtr.Zero;
        IntPtr method;
        while ((method = il2cpp_class_get_methods(clazz, ref iter)) != IntPtr.Zero)
        {
            var candidateName = il2cpp_method_get_name_(method);
            if (candidateName != methodName)
                continue;

            var candidateParameterCount = il2cpp_method_get_param_count(method);
            if (candidateParameterCount != argTypes.Length)
                continue;

            if (isGeneric)
            {
                if (il2cpp_method_is_inflated(method))
                    continue;
                genericCandidates++;
                lastGenericCandidate = method;
            }
            else if (il2cpp_method_is_generic(method))
            {
                continue;
            }

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

        if (isGeneric && genericCandidates == 1)
            return lastGenericCandidate;

        if (!isGeneric && methodsSeen == 1)
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

        try
        {
            if (nestedTypeName.StartsWith("#ordinal:", StringComparison.Ordinal)
                && int.TryParse(nestedTypeName.AsSpan(9), out var requestedOrdinal))
            {
                var currentOrdinal = 0;
                while ((nestedTypePtr = il2cpp_class_get_nested_types(enclosingType, ref iter)) != IntPtr.Zero)
                {
                    if (currentOrdinal++ == requestedOrdinal)
                        return nestedTypePtr;
                }
                iter = IntPtr.Zero;
            }

            while ((nestedTypePtr = il2cpp_class_get_nested_types(enclosingType, ref iter)) != IntPtr.Zero)
                if (il2cpp_class_get_name_(nestedTypePtr) == nestedTypeName)
                    return nestedTypePtr;
        }
        catch (Exception exception) when (exception is EntryPointNotFoundException
                                          or MissingMethodException
                                          or ArgumentNullException)
        {
            Logger.Instance.LogDebug(exception,
                "Native nested-type enumeration is unavailable; using runtime reflection");
        }

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

    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FieldWriteWbarrierStub(IntPtr obj, IntPtr targetAddress, IntPtr value)
    {
        
        *(IntPtr*)targetAddress = value;
    }

    
    [DllImport("GameAssembly", EntryPoint = "FPSHifuDkOi", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_init(IntPtr domain_name);

        [DllImport("GameAssembly", EntryPoint = "hGXVmqTxPxP", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_init_utf16(IntPtr domain_name);

        [DllImport("GameAssembly", EntryPoint = "eXNYCVZufzd", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_shutdown();

        [DllImport("GameAssembly", EntryPoint = "guVbrZSIgMy", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_set_config_dir(IntPtr config_path);

        [DllImport("GameAssembly", EntryPoint = "ICVNpeGjSRY", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_set_data_dir(IntPtr data_path);

        [DllImport("GameAssembly", EntryPoint = "_HardPPuksR", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_set_temp_dir(IntPtr temp_path);

    [DllImport("GameAssembly", EntryPoint = "PvpisoSuPbz", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_set_commandline_arguments(int argc, IntPtr argv, IntPtr basedir);

        [DllImport("GameAssembly", EntryPoint = "DYHPJCyVo_b", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_set_commandline_arguments_utf16(int argc, IntPtr argv, IntPtr basedir);

        [DllImport("GameAssembly", EntryPoint = "JdEmbgNaqMp", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_set_config_utf16(IntPtr executablePath);

    [DllImport("GameAssembly", EntryPoint = "NjLjTSfMwde", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_set_config(IntPtr executablePath);

    [DllImport("GameAssembly", EntryPoint = "AknAAvLQKQq", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_set_memory_callbacks(IntPtr callbacks);

    [DllImport("GameAssembly", EntryPoint = "opSZMLHEJlP", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_get_corlib();

    [DllImport("GameAssembly", EntryPoint = "THOUvypMFKd", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_add_internal_call(IntPtr name, IntPtr method);

    [DllImport("GameAssembly", EntryPoint = "erHVTwfkfHT", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_resolve_icall([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport("GameAssembly", EntryPoint = "cBnsZRW_sbj", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
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
        return *(uint*)((byte*)array + 0x18);
    }

    [DllImport("GameAssembly", EntryPoint = "iTSJnJDeAVf", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern uint il2cpp_array_get_byte_length(IntPtr array);

    public static IntPtr il2cpp_array_new(IntPtr elementTypeInfo, ulong length)
        => ArrayNew.Value(elementTypeInfo, length);

    [DllImport("GameAssembly", EntryPoint = "QJmsRlkbEVv", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_array_new_specific(IntPtr arrayTypeInfo, ulong length);

    [DllImport("GameAssembly", EntryPoint = "EHXabjZGEwx", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_array_new_full(IntPtr array_class, ref ulong lengths, ref ulong lower_bounds);

    [DllImport("GameAssembly", EntryPoint = "svlo_pcufRr", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_bounded_array_class_get(IntPtr element_class, uint rank,
        [MarshalAs(UnmanagedType.I1)] bool bounded);

    [DllImport("GameAssembly", EntryPoint = "EkbTtvQKcqf", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int il2cpp_array_element_size(IntPtr array_class);

    public static IntPtr il2cpp_assembly_get_image(IntPtr assembly)
        => assembly == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(assembly, IntPtr.Size);

    [DllImport("GameAssembly", EntryPoint = "trdmsBrfkWq", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
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

    [DllImport("GameAssembly", EntryPoint = "ltuLgTotCAV", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_class_is_assignable_from(IntPtr klass, IntPtr oklass);

    [DllImport("GameAssembly", EntryPoint = "zFed_sTKhlP", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_class_is_subclass_of(IntPtr klass, IntPtr klassc,
        [MarshalAs(UnmanagedType.I1)] bool check_interfaces);

    [DllImport("GameAssembly", EntryPoint = "TPjLgJOTQWQ", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_class_has_parent(IntPtr klass, IntPtr klassc);

    [DllImport("GameAssembly", EntryPoint = "f_PUimKnMaC", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_class_from_il2cpp_type(IntPtr type);

    [DllImport("GameAssembly", EntryPoint = "YLYTLCLeytw", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_class_from_name(IntPtr image, [MarshalAs(UnmanagedType.LPUTF8Str)] string namespaze,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    public static IntPtr il2cpp_class_from_system_type(IntPtr type)
    {
        if (type == IntPtr.Zero)
            return IntPtr.Zero;

        var nativeType = *(IntPtr*)((byte*)type + IntPtr.Size * 2);
        return nativeType == IntPtr.Zero ? IntPtr.Zero : il2cpp_class_from_type(nativeType);
    }

    [DllImport("GameAssembly", EntryPoint = "hHDPjODpPqJ", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_class_get_element_class(IntPtr klass);

    public static IntPtr il2cpp_class_get_events(IntPtr klass, ref IntPtr iter)
        => ClassGetEvents.Value(klass, ref iter);

    public static IntPtr il2cpp_class_get_fields(IntPtr klass, ref IntPtr iter)
        => ClassGetFields.Value(klass, ref iter);

    public static IntPtr il2cpp_class_get_nested_types(IntPtr klass, ref IntPtr iter)
        => ClassGetNestedTypes.Value(klass, ref iter);

    public static IntPtr il2cpp_class_get_interfaces(IntPtr klass, ref IntPtr iter)
        => ClassGetInterfaces.Value(klass, ref iter);

    public static IntPtr il2cpp_class_get_properties(IntPtr klass, ref IntPtr iter)
        => ClassGetProperties.Value(klass, ref iter);

    [DllImport("GameAssembly", EntryPoint = "gSNYvFDvuTn",
        CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_class_get_property_from_name(IntPtr klass, IntPtr name);

    [DllImport("GameAssembly", EntryPoint = "AKYvZrsgFxf", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_class_get_field_from_name(IntPtr klass,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport("GameAssembly", EntryPoint = "IlDxeJCLSbo", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
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

    [DllImport("GameAssembly", EntryPoint = "AeQfdkESyHM", CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Ansi)]
    public static extern nint il2cpp_class_get_name(IntPtr klass);

    public static string? il2cpp_class_get_name_(IntPtr klass)
        => Marshal.PtrToStringUTF8(il2cpp_class_get_name(klass));

    [DllImport("GameAssembly", EntryPoint = "QSczjJei_BP", CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Ansi)]
    public static extern nint il2cpp_class_get_namespace(IntPtr klass);

    public static string? il2cpp_class_get_namespace_(IntPtr klass)
        => Marshal.PtrToStringUTF8(il2cpp_class_get_namespace(klass));

    [DllImport("GameAssembly", EntryPoint = "dYxEmJjPXEV", CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_class_get_parent(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "QJhqkuS_VIy",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr il2cpp_class_get_declaring_type(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "TicwmXAdufc", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int il2cpp_class_instance_size(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "SimPPlvSTSt", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern uint il2cpp_class_num_fields(IntPtr enumKlass);

    public static bool il2cpp_class_is_valuetype(IntPtr klass)
        => klass != IntPtr.Zero && UnityVersionHandler.Wrap((Il2CppClass*)klass).ValueType;

    [DllImport("GameAssembly", EntryPoint = "VeKcLpwOHDY", CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Ansi)]
    public static extern int il2cpp_class_value_size(IntPtr klass, ref uint align);

    public static bool il2cpp_class_is_blittable(IntPtr klass)
        => klass != IntPtr.Zero && (Marshal.ReadByte(klass, 0x136) & 0x08) != 0;

    public static int il2cpp_class_get_flags(IntPtr klass)
        => klass == IntPtr.Zero ? 0 : Marshal.ReadInt32(klass, 0x118);

    [DllImport("GameAssembly", EntryPoint = "OaE_TuGohqx", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_class_is_abstract(IntPtr klass);

    public static bool il2cpp_class_is_interface(IntPtr klass)
        => klass != IntPtr.Zero && (Marshal.ReadInt32(klass, 0x118) & 0x20) != 0;

    [DllImport("GameAssembly", EntryPoint = "hfgruPRyihM", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int il2cpp_class_array_element_size(IntPtr klass);

    public static IntPtr il2cpp_class_from_type(IntPtr type) => ClassFromType.Value(type);

    [DllImport("GameAssembly", EntryPoint = "MSaOxeuklKe", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_class_get_type(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "qZTOaXxgpXz", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern uint il2cpp_class_get_type_token(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "vUNqHMMuwUN", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_class_has_attribute(IntPtr klass, IntPtr attr_class);

    [DllImport("GameAssembly", EntryPoint = "lXSkTWigJIo", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_class_has_references(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "PqpLjZLcQlb", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_class_is_enum(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "XHGVJgcvoOp", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_class_get_image(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "lwgQoJoALXy", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern nint il2cpp_class_get_assemblyname(IntPtr klass);

    public static string? il2cpp_class_get_assemblyname_(IntPtr klass)
        => Marshal.PtrToStringUTF8(il2cpp_class_get_assemblyname(klass));

    [DllImport("GameAssembly", EntryPoint = "ZLReJNDJOoE", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int il2cpp_class_get_rank(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "ROyxzFBIzUe", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern uint il2cpp_class_get_bitmap_size(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "XXEHtmSccXW", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_class_get_bitmap(IntPtr klass, ref uint bitmap);

    public static bool il2cpp_stats_dump_to_file(IntPtr path) => false;

    
    public static IntPtr il2cpp_domain_get() => new(1);

    [DllImport("GameAssembly", EntryPoint = "wTYYbLvPotE", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_domain_assembly_open(IntPtr domain, IntPtr name);

    [DllImport("GameAssembly", EntryPoint = "usn_wODIDSV", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr* il2cpp_domain_get_assemblies(IntPtr domain, ref nuint size);

    [DllImport("GameAssembly", EntryPoint = "uYCqpmuydon", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr
        il2cpp_exception_from_name_msg(IntPtr image, IntPtr name_space, IntPtr name, IntPtr msg);

    [DllImport("GameAssembly", EntryPoint = "BwNXxaOpKlT", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_get_exception_argument_null(IntPtr arg);

    [DllImport("GameAssembly", EntryPoint = "jZlBAzkDplZ", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_format_exception(IntPtr ex, void* message, int message_size);

    [DllImport("GameAssembly", EntryPoint = "vriFvHxap_f", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_format_stack_trace(IntPtr ex, void* output, int output_size);

    [DllImport("GameAssembly", EntryPoint = "ECTOGjeML_J", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_unhandled_exception(IntPtr ex);

    [DllImport("GameAssembly", EntryPoint = "vaTGQganPXj", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int il2cpp_field_get_flags(IntPtr field);

    public static nint il2cpp_field_get_name(IntPtr field) => FieldGetName.Value(field);

    public static string? il2cpp_field_get_name_(IntPtr field)
        => Marshal.PtrToStringUTF8(il2cpp_field_get_name(field));

    public static IntPtr il2cpp_field_get_parent(IntPtr field) => FieldGetParent.Value(field);

    public static uint il2cpp_field_get_offset(IntPtr field)
        => field == IntPtr.Zero ? 0 : il2cpp_field_get_offset_native(field);

    [DllImport("GameAssembly", EntryPoint = "DCMvdQoLobz", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint il2cpp_field_get_offset_native(IntPtr field);

    public static IntPtr il2cpp_field_get_type(IntPtr field) => FieldGetType.Value(field);

    [DllImport("GameAssembly", EntryPoint = "UNWGwFycngU", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_field_get_value(IntPtr obj, IntPtr field, void* value);

    [DllImport("GameAssembly", EntryPoint = "scMkiDdcRLF", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_field_get_value_object(IntPtr field, IntPtr obj);

    [DllImport("GameAssembly", EntryPoint = "etcGxexNSaL", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_field_has_attribute(IntPtr field, IntPtr attr_class);

    [DllImport("GameAssembly", EntryPoint = "nJxSeHqMwvz", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_field_set_value(IntPtr obj, IntPtr field, void* value);

    public static void il2cpp_field_static_get_value(IntPtr field, void* value) => FieldStaticGetValue.Value(field, value);

    public static void il2cpp_field_static_set_value(IntPtr field, void* value) => FieldStaticSetValue.Value(field, value);

    [DllImport("GameAssembly", EntryPoint = "WFwPGRzOvre", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_field_set_value_object(IntPtr instance, IntPtr field, IntPtr value);

    [DllImport("GameAssembly", EntryPoint = "GGxWFczMbqP", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_gc_collect(int maxGenerations);

    [DllImport("GameAssembly", EntryPoint = "_nhYhhYxEWZ", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int il2cpp_gc_collect_a_little();

    [DllImport("GameAssembly", EntryPoint = "PVuKFLqGQuV", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_gc_disable();

    [DllImport("GameAssembly", EntryPoint = "ftL_vVntAbQ", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_gc_enable();

    [DllImport("GameAssembly", EntryPoint = "URy_RypazKb", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_gc_is_disabled();

    [DllImport("GameAssembly", EntryPoint = "cW_fRcTSzCQ", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern long il2cpp_gc_get_used_size();

    [DllImport("GameAssembly", EntryPoint = "lyZRSFXnPCv", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern long il2cpp_gc_get_heap_size();

    [DllImport("GameAssembly", EntryPoint = "ClgbcIXTSTz", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_gc_wbarrier_set_field(IntPtr obj, IntPtr targetAddress, IntPtr gcObj);

    [DllImport("GameAssembly", EntryPoint = "ExbhzweFDdE", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern nint il2cpp_gchandle_new(IntPtr obj, [MarshalAs(UnmanagedType.I1)] bool pinned);

    [DllImport("GameAssembly", EntryPoint = "eoSRhaCNDTJ", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern nint il2cpp_gchandle_new_weakref(IntPtr obj,
        [MarshalAs(UnmanagedType.I1)] bool track_resurrection);

    [DllImport("GameAssembly", EntryPoint = "il2cpp_gchandle_get_target", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_gchandle_get_target(nint gchandle);

    [DllImport("GameAssembly", EntryPoint = "djLxhuaEUsD", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
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
        => method == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(method, 0x20);

    public static IntPtr il2cpp_method_get_declaring_type(IntPtr method)
        => method == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(method, 0x28);

    public static nint il2cpp_method_get_name(IntPtr method)
        => method == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(method, 0x38);

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
        => method == IntPtr.Zero ? IntPtr.Zero : MethodGetObject.Value(method, refclass);

    public static bool il2cpp_method_is_generic(IntPtr method)
        => method != IntPtr.Zero && MethodIsGeneric.Value(method) != 0;

    public static bool il2cpp_method_is_inflated(IntPtr method)
        => method != IntPtr.Zero && MethodIsInflated.Value(method) != 0;

    public static bool il2cpp_method_is_instance(IntPtr method)
        => method != IntPtr.Zero && MethodIsInstance.Value(method) != 0;

    public static uint il2cpp_method_get_param_count(IntPtr method)
        => method == IntPtr.Zero ? 0 : MethodGetParamCount.Value(method);

    public static IntPtr il2cpp_method_get_param(IntPtr method, uint index)
        => method == IntPtr.Zero ? IntPtr.Zero : MethodGetParam.Value(method, index);

    public static IntPtr il2cpp_method_get_class(IntPtr method)
        => il2cpp_method_get_declaring_type(method);

    [DllImport("GameAssembly", EntryPoint = "LfAwwrIeaBa", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
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

    [DllImport("GameAssembly", EntryPoint = "ufjpDXIZiay", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint il2cpp_method_get_token(IntPtr method);

    public static nint il2cpp_method_get_param_name(IntPtr method, uint index)
        => IntPtr.Zero;

    public static string? il2cpp_method_get_param_name_(IntPtr method, uint index)
        => Marshal.PtrToStringUTF8(il2cpp_method_get_param_name(method, index));

    public static void il2cpp_profiler_install(IntPtr prof, IntPtr shutdown_callback)
    {
    }

    
    
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

    [DllImport("GameAssembly", EntryPoint = "AdTiinIloxM", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern uint il2cpp_property_get_flags(IntPtr prop);

    [DllImport("GameAssembly", EntryPoint = "ggFANwE_XUa", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_property_get_get_method(IntPtr prop);

    [DllImport("GameAssembly", EntryPoint = "axcDSxPJlLf", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_property_get_set_method(IntPtr prop);

    public static nint il2cpp_property_get_name(IntPtr prop)
        => prop == IntPtr.Zero
            ? IntPtr.Zero
            : UnityVersionHandler.Wrap((Il2CppPropertyInfo*)prop)?.Name ?? IntPtr.Zero;

    public static string? il2cpp_property_get_name_(IntPtr prop)
        => Marshal.PtrToStringUTF8(il2cpp_property_get_name(prop));

    [DllImport("GameAssembly", EntryPoint = "ckIruwNKykr", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_property_get_parent(IntPtr prop);

    public static IntPtr il2cpp_object_get_class(IntPtr obj)
        => obj == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)obj;

    public static uint il2cpp_object_get_size(IntPtr obj)
    {
        var klass = il2cpp_object_get_class(obj);
        return klass == IntPtr.Zero ? 0 : *(uint*)((byte*)klass + 0xF8);
    }

    [DllImport("GameAssembly", EntryPoint = "HXMxptThzdF", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_object_get_virtual_method(IntPtr obj, IntPtr method);

    [DllImport("GameAssembly", EntryPoint = "UTJsarQUEsf", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_object_new(IntPtr klass);

    public static IntPtr il2cpp_object_unbox(IntPtr obj)
        => obj == IntPtr.Zero ? IntPtr.Zero : obj + (2 * IntPtr.Size);

    [DllImport("GameAssembly", EntryPoint = "lHgnIrYSuJI", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_value_box(IntPtr klass, IntPtr data);

    [DllImport("GameAssembly", EntryPoint = "YGRXYQJHNVe", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_monitor_enter(IntPtr obj);

    [DllImport("GameAssembly", EntryPoint = "btPPOOqvIgy", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_monitor_try_enter(IntPtr obj, uint timeout);

    [DllImport("GameAssembly", EntryPoint = "jJVuxBkWjJV", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_monitor_exit(IntPtr obj);

    [DllImport("GameAssembly", EntryPoint = "YvBjLwghBcI", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_monitor_pulse(IntPtr obj);

    [DllImport("GameAssembly", EntryPoint = "ZroCqdhHM_e", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_monitor_pulse_all(IntPtr obj);

    [DllImport("GameAssembly", EntryPoint = "wBpTLUioQKY", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_monitor_wait(IntPtr obj);

    [DllImport("GameAssembly", EntryPoint = "bQHeMDnvlEE", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_monitor_try_wait(IntPtr obj, uint timeout);

    [DllImport("GameAssembly", EntryPoint = "BkPyAyKoENa", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern IntPtr il2cpp_runtime_invoke_native(IntPtr method, IntPtr obj, void** param, ref IntPtr exc);

    public static IntPtr il2cpp_runtime_invoke(IntPtr method, IntPtr obj, void** param, ref IntPtr exc)
    {
        return il2cpp_runtime_invoke_native(method, obj, param, ref exc);
    }

    [DllImport("GameAssembly", EntryPoint = "ryTGurUTmRs", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    
    public static extern IntPtr il2cpp_runtime_invoke_convert_args(IntPtr method, IntPtr obj, void** param,
        int paramCount, ref IntPtr exc);

    [DllImport("GameAssembly", EntryPoint = "bxDeZRldFLP", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_runtime_class_init(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "stEMnSNEZIF", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_runtime_object_init(IntPtr obj);

    [DllImport("GameAssembly", EntryPoint = "KfvNZBWjLyF", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_runtime_object_init_exception(IntPtr obj, ref IntPtr exc);

    
    [DllImport("GameAssembly", EntryPoint = "NkVRNOtribg", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int il2cpp_string_length(IntPtr str);

    [DllImport("GameAssembly", EntryPoint = "rLsah__vtKN", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern char* il2cpp_string_chars(IntPtr str);

    [DllImport("GameAssembly", EntryPoint = "nagrXzmvcLA", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_string_new(string str);

    [DllImport("GameAssembly", EntryPoint = "ttcPzbvrwdy", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_string_new_len(string str, uint length);

    [DllImport("GameAssembly", EntryPoint = "sJTHmUmewxV", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_string_new_utf16(char* text, int len);

    [DllImport("GameAssembly", EntryPoint = "nagrXzmvcLA", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_string_new_wrapper(string str);

    [DllImport("GameAssembly", EntryPoint = "wMMmNClZtZn", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_string_intern(string str);

    [DllImport("GameAssembly", EntryPoint = "aZxzfpAGP_k", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_string_is_interned(string str);

    [DllImport("GameAssembly", EntryPoint = "qXGIjmbgCpH", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_thread_current();

    [DllImport("GameAssembly", EntryPoint = "geJSlRUNYTW", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_thread_attach(IntPtr domain);

    [DllImport("GameAssembly", EntryPoint = "FSXwdnuJ_wa", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_thread_detach(IntPtr thread);

    [DllImport("GameAssembly", EntryPoint = "hZaGUwKUCih", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void** il2cpp_thread_get_all_attached_threads(ref uint size);

    [DllImport("GameAssembly", EntryPoint = "boiotpOuFVR", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_is_vm_thread(IntPtr thread);

    [DllImport("GameAssembly", EntryPoint = "unYtDydzJEC", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_current_thread_walk_frame_stack(IntPtr func, IntPtr user_data);

    [DllImport("GameAssembly", EntryPoint = "AMOhGjklLp_", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_thread_walk_frame_stack(IntPtr thread, IntPtr func, IntPtr user_data);

    [DllImport("GameAssembly", EntryPoint = "AMCSS_yLHtG", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_current_thread_get_top_frame(IntPtr frame);

    [DllImport("GameAssembly", EntryPoint = "VKYKcVgdUdC", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_thread_get_top_frame(IntPtr thread, IntPtr frame);

    [DllImport("GameAssembly", EntryPoint = "RfdBFVPtaGO", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_current_thread_get_frame_at(int offset, IntPtr frame);

    [DllImport("GameAssembly", EntryPoint = "PDNvkBterCj", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_thread_get_frame_at(IntPtr thread, int offset, IntPtr frame);

    [DllImport("GameAssembly", EntryPoint = "lkw_uuaeIuG", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int il2cpp_current_thread_get_stack_depth();

    [DllImport("GameAssembly", EntryPoint = "IZVrLIwUOvO", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int il2cpp_thread_get_stack_depth(IntPtr thread);

    [DllImport("GameAssembly", EntryPoint = "zkfnoJTKrxi", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_type_get_object(IntPtr type);

    [DllImport("GameAssembly", EntryPoint = "nQfjMgVCNqA", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int il2cpp_type_get_type(IntPtr type);

    [DllImport("GameAssembly", EntryPoint = "BJHjRgwZfpL", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_type_get_class_or_element_class(IntPtr type);

    [DllImport("GameAssembly", EntryPoint = "wUfrFIAhzjI", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern nint il2cpp_type_get_name(IntPtr type);

    public static string? il2cpp_type_get_name_(IntPtr type)
        => Marshal.PtrToStringUTF8(il2cpp_type_get_name(type));

    public static bool il2cpp_type_is_byref(IntPtr type)
        => type != IntPtr.Zero
           && (UnityVersionHandler.Wrap((Il2CppTypeStruct*)type)?.ByRef ?? false);

    [DllImport("GameAssembly", EntryPoint = "EDDmfSLdudJ", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern uint il2cpp_type_get_attrs(IntPtr type);

    [DllImport("GameAssembly", EntryPoint = "TO_NEYLGpCv", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_type_equals(IntPtr type, IntPtr otherType);

    [DllImport("GameAssembly", EntryPoint = "DNcHIduQcxF", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_type_get_assembly_qualified_name(IntPtr type);

    public static IntPtr il2cpp_image_get_assembly(IntPtr image)
        => image == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(image, 0x20);

    public static nint il2cpp_image_get_name(IntPtr image)
        => image == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(image, 0x10);

    public static string? il2cpp_image_get_name_(IntPtr image)
        => Marshal.PtrToStringUTF8(il2cpp_image_get_name(image));

    [DllImport("GameAssembly", EntryPoint = "AarTPqjxzGu", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern nint il2cpp_image_get_filename(IntPtr image);

    public static string? il2cpp_image_get_filename_(IntPtr image)
        => Marshal.PtrToStringUTF8(il2cpp_image_get_filename(image));

    [DllImport("GameAssembly", EntryPoint = "AQQbRzQtrHD", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_image_get_entry_point(IntPtr image);

    [DllImport("GameAssembly", EntryPoint = "VLtaCuyrrDI", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern uint il2cpp_image_get_class_count(IntPtr image);

    [DllImport("GameAssembly", EntryPoint = "QNJWHhkkxMm", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_image_get_class(IntPtr image, uint index);

    public static IntPtr il2cpp_capture_memory_snapshot() => IntPtr.Zero;

    public static void il2cpp_free_captured_memory_snapshot(IntPtr snapshot)
    {
    }

    [DllImport("GameAssembly", EntryPoint = "ZDYWCTONLGb", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
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

    [DllImport("GameAssembly", EntryPoint = "STUlhJPmGzK", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_custom_attrs_from_class(IntPtr klass);

    [DllImport("GameAssembly", EntryPoint = "WiuOqkdlFFP", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_custom_attrs_from_method(IntPtr method);

    [DllImport("GameAssembly", EntryPoint = "KJMDLkbxjvA", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_custom_attrs_get_attr(IntPtr ainfo, IntPtr attr_klass);

    [DllImport("GameAssembly", EntryPoint = "aGTUFKBkyZX", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool il2cpp_custom_attrs_has_attr(IntPtr ainfo, IntPtr attr_klass);

    [DllImport("GameAssembly", EntryPoint = "JyPMLIBBagQ", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr il2cpp_custom_attrs_construct(IntPtr cinfo);

    [DllImport("GameAssembly", EntryPoint = "oXcuBhPrxGr", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void il2cpp_custom_attrs_free(IntPtr ainfo);
}
