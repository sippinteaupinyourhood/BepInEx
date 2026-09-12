using System;
using System.Linq;
using System.Runtime.InteropServices;
using Il2CppInterop.Common;
using Il2CppInterop.Common.XrefScans;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime.Startup;

namespace Il2CppInterop.Runtime.Injection.Hooks
{
    internal unsafe class GenericMethod_GetMethod_Unity6_Hook : Hook<GenericMethod_GetMethod_Unity6_Hook.MethodDelegate>
    {
        public override string TargetMethodName => "GenericMethod::GetMethod";
        public override MethodDelegate GetDetour() => Hook;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate Il2CppMethodInfo* MethodDelegate(Il2CppMethodInfo* methodDefinition, Il2CppGenericInst* classInst, Il2CppGenericInst* methodInst);

        private Il2CppMethodInfo* Hook(Il2CppMethodInfo* methodDefinition, Il2CppGenericInst* classInst, Il2CppGenericInst* methodInst)
        {
            if (methodDefinition == null)
                return Original(methodDefinition, classInst, methodInst);

            try
            {
                if (!ClassInjector.InflatedMethodFromContextDictionary.TryGetValue(
                        (IntPtr)methodDefinition, out var methods) || methods.Item1 == null)
                    return Original(methodDefinition, classInst, methodInst);

                if (methodInst == null || methodInst->type_argc == 0 || methodInst->type_argc > 256 ||
                    methodInst->type_argv == null)
                    return Original(methodDefinition, classInst, methodInst);

                lock (methods.Item2)
                {
                    if (methods.Item2.TryGetValue((IntPtr)methodInst, out var inflatedMethodPointer))
                        return (Il2CppMethodInfo*)inflatedMethodPointer;

                    var typeArguments = new Type[methodInst->type_argc];
                    for (var i = 0; i < methodInst->type_argc; i++)
                    {
                        var il2cppType = methodInst->type_argv[i];
                        if (il2cppType == null)
                            return Original(methodDefinition, classInst, methodInst);
                        typeArguments[i] = ClassInjector.SystemTypeFromIl2CppType(il2cppType);
                        if (typeArguments[i] == null)
                            return Original(methodDefinition, classInst, methodInst);
                    }

                    var inflatedMethod = methods.Item1.MakeGenericMethod(typeArguments);
                    var wrappedMethod = UnityVersionHandler.Wrap(methodDefinition);
                    if (wrappedMethod == null)
                        return Original(methodDefinition, classInst, methodInst);

                    inflatedMethodPointer = (IntPtr)ClassInjector.ConvertMethodInfo(
                        inflatedMethod, UnityVersionHandler.Wrap(wrappedMethod.Class));
                    if (inflatedMethodPointer == IntPtr.Zero)
                        return Original(methodDefinition, classInst, methodInst);
                    methods.Item2[(IntPtr)methodInst] = inflatedMethodPointer;
                    return (Il2CppMethodInfo*)inflatedMethodPointer;
                }
            }
            catch
            {
                return Original(methodDefinition, classInst, methodInst);
            }
        }

        public override IntPtr FindTargetMethod()
        {
            var getVirtualMethodAPI = InjectorHelpers.GetIl2CppExport(nameof(IL2CPP.il2cpp_object_get_virtual_method));
            if (getVirtualMethodAPI == IntPtr.Zero) return IntPtr.Zero;

            var getVirtualMethod = XrefScannerLowLevel.JumpTargets(getVirtualMethodAPI).FirstOrDefault();
            if (getVirtualMethod == IntPtr.Zero) return IntPtr.Zero;

            var xrefs = XrefScannerLowLevel.JumpTargets(getVirtualMethod).ToArray();
            if (xrefs.Length == 0) return IntPtr.Zero;

            IntPtr getGenericVirtualMethod = xrefs.Last();
            var finalXrefs = XrefScannerLowLevel.JumpTargets(getGenericVirtualMethod).ToArray();

            if (finalXrefs.Length == 0)
                return IntPtr.Zero;

            IntPtr candidate = finalXrefs.Last();
            return candidate;
        }
    }
}
