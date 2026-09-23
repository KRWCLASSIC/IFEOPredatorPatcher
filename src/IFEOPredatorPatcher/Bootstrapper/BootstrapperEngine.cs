using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using IFEOPredatorPatcher.Services;
using Mono.Cecil;

namespace IFEOPredatorPatcher.Bootstrapper
{
    public static class BootstrapperEngine
    {
        [DllImport("shell32.dll", SetLastError = true)]
        private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

        public static void Launch(string targetExePath, string[] args)
        {
            if (string.IsNullOrWhiteSpace(targetExePath)) return;
            targetExePath = targetExePath.Trim('\"');

            if (!File.Exists(targetExePath))
            {
                MessageBox.Show($"Target executable not found:\n{targetExePath}", "IFEOPredatorPatcher Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string targetDir = Path.GetDirectoryName(targetExePath) ?? string.Empty;
            string pluginsDir = PluginLoader.GetDefaultPluginsDirectory();
            var context = PluginContext.Create(targetExePath, args, pluginsDir);

            context.Log($"Starting bootstrapper for: {targetExePath}");

            // 1. Group window under official NitroSense/PredatorSense taskbar icon
            try
            {
                var apps = SenseScanner.ScanAll();
                var matchedApp = apps.FirstOrDefault(a => a.ExecutablePath.Equals(targetExePath, StringComparison.OrdinalIgnoreCase))
                                 ?? apps.FirstOrDefault();
                if (matchedApp?.AppUserModelId != null)
                {
                    SetCurrentProcessExplicitAppUserModelID(matchedApp.AppUserModelId);
                    context.Log($"Set taskbar AppUserModelID to: {matchedApp.AppUserModelId}");
                }
            }
            catch (Exception ex)
            {
                context.Log($"Warning setting AppUserModelID: {ex.Message}");
            }

            // 2. Set current directory to target directory so relative paths in NitroSense work properly
            try
            {
                if (!string.IsNullOrEmpty(targetDir) && Directory.Exists(targetDir))
                {
                    Environment.CurrentDirectory = targetDir;
                }
            }
            catch (Exception ex)
            {
                context.Log($"Warning: Failed to set working directory to '{targetDir}': {ex.Message}");
            }

            // 3. Set up AssemblyResolve to locate target dependencies (TsDotNetLib, ICSharpCode.SharpZipLib, etc.)
            AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
            {
                try
                {
                    string assemblyName = new AssemblyName(resolveArgs.Name).Name;
                    
                    // First check target folder
                    if (!string.IsNullOrEmpty(targetDir))
                    {
                        string candidate = Path.Combine(targetDir, assemblyName + ".dll");
                        if (File.Exists(candidate))
                        {
                            return Assembly.LoadFrom(candidate);
                        }
                    }

                    // Next check plugins folder
                    string pluginCandidate = Path.Combine(pluginsDir, assemblyName + ".dll");
                    if (File.Exists(pluginCandidate))
                    {
                        return Assembly.LoadFrom(pluginCandidate);
                    }
                }
                catch (Exception ex)
                {
                    context.Log($"AssemblyResolve error for '{resolveArgs.Name}': {ex.Message}");
                }

                return null;
            };

            // 4. Fast-boot assembly cache check
            byte[]? patchedBytes = null;
            string cacheDir = Path.Combine(IFEOService.GetAppDataRootDirectory(), "cache");
            string exeName = Path.GetFileNameWithoutExtension(targetExePath);
            string cachedAssemblyPath = Path.Combine(cacheDir, $"{exeName}.patched.dll");

            if (IsCacheValid(cachedAssemblyPath, targetExePath))
            {
                try
                {
                    patchedBytes = File.ReadAllBytes(cachedAssemblyPath);
                    context.Log($"Loaded patched assembly from cache ({patchedBytes.Length} bytes) - Fast Boot.");
                }
                catch (Exception ex)
                {
                    context.Log($"Failed to read from cache: {ex.Message}");
                    patchedBytes = null;
                }
            }

            // 5. If cache not available or outdated, run Mono.Cecil pipeline and update cache
            if (patchedBytes == null)
            {
                byte[] originalBytes = File.ReadAllBytes(targetExePath);

                using (var inputStream = new MemoryStream(originalBytes))
                {
                    var readerParams = new ReaderParameters
                    {
                        ReadingMode = ReadingMode.Immediate,
                        ReadWrite = false,
                        AssemblyResolver = new TargetAssemblyResolver(targetDir)
                    };

                    var assemblyDef = AssemblyDefinition.ReadAssembly(inputStream, readerParams);

                    context.Log("Halting execution to execute plugin patch pipeline...");
                    PluginLoader.RunAllPlugins(assemblyDef, context);
                    context.Log("All plugins completed.");

                    using (var outputStream = new MemoryStream())
                    {
                        assemblyDef.Write(outputStream);
                        patchedBytes = outputStream.ToArray();
                    }
                }

                // Save to cache for instant subsequent startups
                try
                {
                    if (!Directory.Exists(cacheDir))
                    {
                        Directory.CreateDirectory(cacheDir);
                    }
                    File.WriteAllBytes(cachedAssemblyPath, patchedBytes);
                    context.Log("Patched assembly written to cache.");
                }
                catch (Exception ex)
                {
                    context.Log($"Warning writing cache: {ex.Message}");
                }
            }

            if (patchedBytes == null || patchedBytes.Length == 0)
            {
                context.Log("Error: Failed to obtain patched assembly bytes.");
                return;
            }

            context.Log($"Loading assembly into AppDomain ({patchedBytes.Length} bytes)...");

            // 6. Load patched assembly into memory
            Assembly loadedAssembly = Assembly.Load(patchedBytes);

            // 7. CRITICAL: Register ResourceAssembly so WPF pack URIs (/NitroSense;component/...) resolve
            try
            {
                var field = typeof(Application).GetField("_resourceAssembly", BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null)
                {
                    field.SetValue(null, loadedAssembly);
                    context.Log("Successfully set Application._resourceAssembly to target assembly.");
                }
                else
                {
                    Application.ResourceAssembly = loadedAssembly;
                }
            }
            catch (Exception ex)
            {
                context.Log($"Warning: Failed to set Application.ResourceAssembly: {ex.Message}");
            }

            // 8. Find EntryPoint and execute
            MethodInfo? entryPoint = loadedAssembly.EntryPoint;
            if (entryPoint == null)
            {
                context.Log("Error: No entry point found in target assembly.");
                MessageBox.Show($"Could not find entry point in '{targetExePath}'", "IFEOPredatorPatcher Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            context.Log($"Invoking EntryPoint {entryPoint.DeclaringType?.FullName}.{entryPoint.Name}...");

            object?[] parameters;
            var entryParams = entryPoint.GetParameters();
            if (entryParams.Length == 0)
            {
                parameters = Array.Empty<object>();
            }
            else
            {
                parameters = new object?[] { args };
            }

            try
            {
                entryPoint.Invoke(null, parameters);
            }
            catch (Exception ex)
            {
                var actualEx = (ex is TargetInvocationException tie && tie.InnerException != null) 
                    ? tie.InnerException 
                    : ex;
                context.Log($"Exception in target execution: {actualEx}");
                MessageBox.Show($"Exception in bootstrapped application:\n{actualEx}", "IFEOPredatorPatcher Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool IsCacheValid(string cachedAssemblyPath, string targetExePath)
        {
            if (!File.Exists(cachedAssemblyPath)) return false;

            try
            {
                var cacheTime = File.GetLastWriteTimeUtc(cachedAssemblyPath);
                var targetTime = File.GetLastWriteTimeUtc(targetExePath);

                if (targetTime > cacheTime) return false;

                // Invalidate if any plugin was updated
                foreach (var dir in PluginLoader.GetPluginDirectories())
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var dll in Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly))
                    {
                        if (File.GetLastWriteTimeUtc(dll) > cacheTime) return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private class TargetAssemblyResolver : DefaultAssemblyResolver
        {
            public TargetAssemblyResolver(string targetDir)
            {
                if (!string.IsNullOrEmpty(targetDir) && Directory.Exists(targetDir))
                {
                    AddSearchDirectory(targetDir);
                }
            }
        }
    }
}
