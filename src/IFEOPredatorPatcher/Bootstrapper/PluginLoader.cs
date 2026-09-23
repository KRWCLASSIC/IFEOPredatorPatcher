using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;

namespace IFEOPredatorPatcher.Bootstrapper
{
    public static class PluginLoader
    {
        public static string GetDefaultPluginsDirectory()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string pluginsDir = Path.Combine(appData, "IFEOPredatorPatcher", "plugins");
            if (!Directory.Exists(pluginsDir))
            {
                Directory.CreateDirectory(pluginsDir);
            }
            return pluginsDir;
        }

        public static List<string> GetPluginDirectories()
        {
            var list = new List<string>();

            // 1. AppData directory
            string appDataDir = GetDefaultPluginsDirectory();
            if (Directory.Exists(appDataDir))
            {
                list.Add(appDataDir);
            }

            // 2. Local ./plugins directory next to exe
            string localDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
            if (Directory.Exists(localDir) && !list.Contains(localDir))
            {
                list.Add(localDir);
            }

            return list;
        }

        public static void RunAllPlugins(AssemblyDefinition assemblyDef, PluginContext context)
        {
            var pluginDirs = GetPluginDirectories();
            var pluginFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in pluginDirs)
            {
                try
                {
                    foreach (var dll in Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly))
                    {
                        // Avoid loading self or core dependencies as plugins
                        string fileName = Path.GetFileName(dll);
                        if (fileName.Equals("Mono.Cecil.dll", StringComparison.OrdinalIgnoreCase) ||
                            fileName.Equals("Mono.Cecil.Pdb.dll", StringComparison.OrdinalIgnoreCase) ||
                            fileName.Equals("Mono.Cecil.Mdb.dll", StringComparison.OrdinalIgnoreCase) ||
                            fileName.Equals("Mono.Cecil.Rocks.dll", StringComparison.OrdinalIgnoreCase) ||
                            fileName.Equals("IFEOPredatorPatcher.exe", StringComparison.OrdinalIgnoreCase) ||
                            fileName.Equals("IFEOPredatorPatcher.dll", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        pluginFiles.Add(dll);
                    }
                }
                catch (Exception ex)
                {
                    context.Log($"Error scanning directory '{dir}': {ex.Message}");
                }
            }

            context.Log($"Discovered {pluginFiles.Count} plugin(s) to execute.");

            foreach (var pluginPath in pluginFiles)
            {
                try
                {
                    ExecutePlugin(pluginPath, assemblyDef, context);
                }
                catch (Exception ex)
                {
                    context.Log($"Exception while executing plugin '{Path.GetFileName(pluginPath)}': {ex}");
                }
            }
        }

        private static void ExecutePlugin(string pluginPath, AssemblyDefinition assemblyDef, PluginContext context)
        {
            context.Log($"Loading plugin: {Path.GetFileName(pluginPath)}");
            var pluginAssembly = Assembly.LoadFrom(pluginPath);

            bool executedAny = false;

            foreach (var type in pluginAssembly.GetExportedTypes())
            {
                // Look for Patch(AssemblyDefinition, PluginContext)
                var method = type.GetMethod("Patch", BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance,
                    null, new[] { typeof(AssemblyDefinition), typeof(PluginContext) }, null);

                if (method != null)
                {
                    object? instance = method.IsStatic ? null : Activator.CreateInstance(type);
                    method.Invoke(instance, new object[] { assemblyDef, context });
                    context.Log($"Executed {type.FullName}.Patch(AssemblyDefinition, PluginContext) in {Path.GetFileName(pluginPath)}");
                    executedAny = true;
                    continue;
                }

                // Fallback: Patch(AssemblyDefinition, string)
                method = type.GetMethod("Patch", BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance,
                    null, new[] { typeof(AssemblyDefinition), typeof(string) }, null);

                if (method != null)
                {
                    object? instance = method.IsStatic ? null : Activator.CreateInstance(type);
                    method.Invoke(instance, new object[] { assemblyDef, context.TargetPath });
                    context.Log($"Executed {type.FullName}.Patch(AssemblyDefinition, string) in {Path.GetFileName(pluginPath)}");
                    executedAny = true;
                    continue;
                }

                // Fallback: Patch(AssemblyDefinition)
                method = type.GetMethod("Patch", BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance,
                    null, new[] { typeof(AssemblyDefinition) }, null);

                if (method != null)
                {
                    object? instance = method.IsStatic ? null : Activator.CreateInstance(type);
                    method.Invoke(instance, new object[] { assemblyDef });
                    context.Log($"Executed {type.FullName}.Patch(AssemblyDefinition) in {Path.GetFileName(pluginPath)}");
                    executedAny = true;
                    continue;
                }

                // Fallback: Initialize(AssemblyDefinition, PluginContext)
                method = type.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance,
                    null, new[] { typeof(AssemblyDefinition), typeof(PluginContext) }, null);

                if (method != null)
                {
                    object? instance = method.IsStatic ? null : Activator.CreateInstance(type);
                    method.Invoke(instance, new object[] { assemblyDef, context });
                    context.Log($"Executed {type.FullName}.Initialize(AssemblyDefinition, PluginContext) in {Path.GetFileName(pluginPath)}");
                    executedAny = true;
                }
            }

            if (!executedAny)
            {
                context.Log($"Warning: No matching Patch/Initialize method found in {Path.GetFileName(pluginPath)}");
            }
        }
    }
}
