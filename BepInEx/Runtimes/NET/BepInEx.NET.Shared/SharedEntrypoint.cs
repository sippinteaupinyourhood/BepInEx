using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;






namespace BepInEx.NET.Shared
{
    internal static class SharedEntrypoint
    {
        public static ResolveEventHandler RemoteResolve(List<string> resolveDirectories)
        {
            return (sender, args) => RemoteResolveInternal(sender, args, resolveDirectories);
        }

        private static Assembly RemoteResolveInternal(object sender,
                                                      ResolveEventArgs reference,
                                                      List<string> resolveDirectories)
        {
            var assemblyName = new AssemblyName(reference.Name);

            foreach (var directory in resolveDirectories)
            {
                if (!Directory.Exists(directory))
                    continue;

                var potentialDirectories = new List<string> { directory };

                potentialDirectories.AddRange(Directory.GetDirectories(directory, "*", SearchOption.AllDirectories));

                var potentialFiles = potentialDirectories.Select(x => Path.Combine(x, $"{assemblyName.Name}.dll"))
                                                         .Concat(potentialDirectories.Select(x =>
                                                                     Path
                                                                         .Combine(x,
                                                                             $"{assemblyName.Name}.exe")));

                foreach (var path in potentialFiles)
                {
                    if (!File.Exists(path))
                        continue;

                    Assembly assembly;

                    try
                    {
                        assembly = Assembly.LoadFrom(path);
                    }
                    catch (Exception ex)
                    {
                        continue;
                    }

                    if (assembly.GetName().Name == assemblyName.Name)
                        return assembly;
                }
            }

            return null;
        }

        public static Assembly LocalResolve(object sender, ResolveEventArgs args)
        {
            var assemblyName = new AssemblyName(args.Name);

            var foundAssembly = AppDomain.CurrentDomain.GetAssemblies()
                                         .FirstOrDefault(x => x.GetName().Name == assemblyName.Name);

            if (foundAssembly != null)
                return foundAssembly;

            if (LocalUtility.TryResolveDllAssembly(assemblyName, Paths.BepInExAssemblyDirectory, out foundAssembly)
             || LocalUtility.TryResolveDllAssembly(assemblyName, Paths.PatcherPluginPath, out foundAssembly)
             || LocalUtility.TryResolveDllAssembly(assemblyName, Paths.PluginPath, out foundAssembly))
                return foundAssembly;

            return null;
        }
    }


    
    
    
    internal static class LocalUtility
    {
        
        
        
        
        
        
        
        private static bool TryResolveDllAssembly<T>(AssemblyName assemblyName,
                                                     string directory,
                                                     Func<string, T> loader,
                                                     out T assembly) where T : class
        {
            assembly = null;

            if (!Directory.Exists(directory))
                return false;

            var potentialDirectories = new List<string> { directory };

            potentialDirectories.AddRange(Directory.GetDirectories(directory, "*", SearchOption.AllDirectories));

            foreach (var subDirectory in potentialDirectories)
            {
                var path = Path.Combine(subDirectory, $"{assemblyName.Name}.dll");

                if (!File.Exists(path))
                    continue;

                try
                {
                    assembly = loader(path);
                }
                catch (Exception)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        
        
        
        
        
        
        
        public static bool TryResolveDllAssembly(AssemblyName assemblyName, string directory, out Assembly assembly) =>
            TryResolveDllAssembly(assemblyName, directory, Assembly.LoadFrom, out assembly);
    }
}
