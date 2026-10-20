using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AvatarReplacer
{
    /// <summary>
    /// Loads the plugin's private UniVRM dependencies from resources embedded in
    /// AvatarReplacer.dll. Their original assembly identities are preserved so
    /// UniVRM reflection and runtime type lookups continue to work.
    /// </summary>
    internal static class EmbeddedAssemblyLoader
    {
        private const string ResourcePrefix = "AvatarReplacer.Dependencies.";
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Assembly> Loaded =
            new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        private static bool initialized;

        [ModuleInitializer]
        public static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            AssemblyName requested;
            try { requested = new AssemblyName(args.Name); }
            catch { return null; }

            string simpleName = requested.Name;
            if (string.IsNullOrEmpty(simpleName)) return null;

            lock (Sync)
            {
                if (Loaded.TryGetValue(simpleName, out var cached))
                    return cached;

                string resourceName = ResourcePrefix + simpleName + ".dll";
                var owner = typeof(EmbeddedAssemblyLoader).Assembly;
                using (Stream stream = owner.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;

                    try
                    {
                        using (var memory = new MemoryStream())
                        {
                            stream.CopyTo(memory);
                            var assembly = Assembly.Load(memory.ToArray());
                            Loaded[simpleName] = assembly;
                            return assembly;
                        }
                    }
                    catch (Exception ex)
                    {
                        ModLog.Error($"Failed to load embedded dependency '{simpleName}': {ex.Message}");
                        return null;
                    }
                }
            }
        }
    }
}
