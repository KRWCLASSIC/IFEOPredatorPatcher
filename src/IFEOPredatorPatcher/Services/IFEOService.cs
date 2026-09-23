using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using Microsoft.Win32;

namespace IFEOPredatorPatcher.Services
{
    public enum IFEOStatus
    {
        NotHooked,
        HookedToSelf,
        HookedToOther
    }

    public static class IFEOService
    {
        private const string IFEO_BASE_KEY = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

        public static bool IsElevated()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public static string GetCurrentLauncherPath()
        {
            return Process.GetCurrentProcess().MainModule?.FileName 
                   ?? Assembly.GetExecutingAssembly().Location
                   ?? string.Empty;
        }

        public static string GetAppDataRootDirectory()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string root = Path.Combine(appData, "IFEOPredatorPatcher");
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
            }
            return root;
        }

        public static string GetInstalledLauncherPath()
        {
            return Path.Combine(GetAppDataRootDirectory(), "IFEOPredatorPatcher.exe");
        }

        public static void DeployFilesToAppData()
        {
            string sourceDir = AppDomain.CurrentDomain.BaseDirectory;
            string targetDir = GetAppDataRootDirectory();

            // Ensure plugins subfolder exists
            string pluginsDir = Path.Combine(targetDir, "plugins");
            if (!Directory.Exists(pluginsDir))
            {
                Directory.CreateDirectory(pluginsDir);
            }

            string currentExe = GetCurrentLauncherPath();
            string targetExe = GetInstalledLauncherPath();

            // Copy exe if not running directly from destination
            if (!currentExe.Equals(targetExe, StringComparison.OrdinalIgnoreCase) && File.Exists(currentExe))
            {
                File.Copy(currentExe, targetExe, overwrite: true);
            }

            // Copy Cecil and required dependency DLLs
            string[] filesToCopy = {
                "Mono.Cecil.dll",
                "Mono.Cecil.Pdb.dll",
                "Mono.Cecil.Mdb.dll",
                "Mono.Cecil.Rocks.dll"
            };

            foreach (var file in filesToCopy)
            {
                string src = Path.Combine(sourceDir, file);
                string dst = Path.Combine(targetDir, file);
                if (File.Exists(src))
                {
                    try
                    {
                        File.Copy(src, dst, overwrite: true);
                    }
                    catch
                    {
                        // Ignore if already loaded/in-use
                    }
                }
            }
        }

        public static IFEOStatus CheckStatus(string exeName, string? packageFullName, out string currentDebugger)
        {
            currentDebugger = string.Empty;
            string installedPath = GetInstalledLauncherPath();
            string currentPath = GetCurrentLauncherPath();

            // 1. Check IFEO key
            string subKey = $@"{IFEO_BASE_KEY}\{exeName}";
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(subKey, writable: false))
                {
                    if (key != null)
                    {
                        if (key.GetValue("Debugger") is string val && !string.IsNullOrWhiteSpace(val))
                        {
                            currentDebugger = val;
                            string cleanVal = val.Trim('\"');
                            if (cleanVal.Equals(installedPath, StringComparison.OrdinalIgnoreCase) ||
                                cleanVal.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                            {
                                return IFEOStatus.HookedToSelf;
                            }
                            return IFEOStatus.HookedToOther;
                        }
                    }
                }
            }
            catch { }

            // 2. Check PackagedAppXDebug
            if (!string.IsNullOrWhiteSpace(packageFullName))
            {
                try
                {
                    string appxKey = $@"Software\Microsoft\Windows\CurrentVersion\PackagedAppXDebug\{packageFullName}";
                    using (var key = Registry.CurrentUser.OpenSubKey(appxKey, writable: false))
                    {
                        if (key != null)
                        {
                            if (key.GetValue("") is string val && !string.IsNullOrWhiteSpace(val))
                            {
                                currentDebugger = val;
                                string cleanVal = val.Trim('\"');
                                if (cleanVal.Equals(installedPath, StringComparison.OrdinalIgnoreCase) ||
                                    cleanVal.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                                {
                                    return IFEOStatus.HookedToSelf;
                                }
                                return IFEOStatus.HookedToOther;
                            }
                        }
                    }
                }
                catch { }
            }

            return IFEOStatus.NotHooked;
        }

        public static bool InstallHookDirect(string exeName, string launcherPath, string? targetFullPath = null, string? packageFullName = null)
        {
            string subKey = $@"{IFEO_BASE_KEY}\{exeName}";
            bool success = false;
            var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };

            // 1. Write standard IFEO
            foreach (var view in views)
            {
                try
                {
                    using (var baseHive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    {
                        using (var key = baseHive.CreateSubKey(subKey, writable: true))
                        {
                            if (key != null)
                            {
                                key.SetValue("Debugger", $"\"{launcherPath}\"", RegistryValueKind.String);

                                if (!string.IsNullOrWhiteSpace(targetFullPath))
                                {
                                    key.SetValue("UseFilter", 1, RegistryValueKind.DWord);
                                    using (var sub = key.CreateSubKey("AppxFilter", writable: true))
                                    {
                                        sub.SetValue("FilterFullPath", targetFullPath, RegistryValueKind.String);
                                        sub.SetValue("Debugger", $"\"{launcherPath}\"", RegistryValueKind.String);
                                    }
                                }
                                else
                                {
                                    try { key.DeleteValue("UseFilter"); } catch { }
                                    try { key.DeleteSubKeyTree("AppxFilter", false); } catch { }
                                }

                                success = true;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore view if unavailable
                }
            }

            // 2. Write AppX Debug keys if packaged
            if (!string.IsNullOrWhiteSpace(packageFullName))
            {
                try
                {
                    string appxKey = $@"Software\Microsoft\Windows\CurrentVersion\PackagedAppXDebug\{packageFullName}";
                    using (var key = Registry.CurrentUser.CreateSubKey(appxKey, writable: true))
                    {
                        key?.SetValue("", $"\"{launcherPath}\"", RegistryValueKind.String);
                    }

                    string actUserKey = $@"Software\Classes\ActivatableClasses\Package\{packageFullName}\DebugInformation\App";
                    using (var key = Registry.CurrentUser.CreateSubKey(actUserKey, writable: true))
                    {
                        key?.SetValue("DebugPath", $"\"{launcherPath}\"", RegistryValueKind.String);
                    }

                    foreach (var view in views)
                    {
                        try
                        {
                            using (var baseHive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                            {
                                string actMachineKey = $@"SOFTWARE\Classes\ActivatableClasses\Package\{packageFullName}\DebugInformation\App";
                                using (var key = baseHive.CreateSubKey(actMachineKey, writable: true))
                                {
                                    key?.SetValue("DebugPath", $"\"{launcherPath}\"", RegistryValueKind.String);
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[IFEOService] Error writing AppX debug keys: {ex.Message}");
                }
            }

            return success;
        }

        public static bool UninstallHookDirect(string exeName, string? packageFullName = null)
        {
            bool success = false;
            var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };

            foreach (var view in views)
            {
                try
                {
                    using (var baseHive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    {
                        using (var ifeoBase = baseHive.OpenSubKey(IFEO_BASE_KEY, writable: true))
                        {
                            if (ifeoBase != null)
                            {
                                ifeoBase.DeleteSubKeyTree(exeName, throwOnMissingSubKey: false);
                                success = true;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(packageFullName))
                        {
                            string actMachineKey = $@"SOFTWARE\Classes\ActivatableClasses\Package\{packageFullName}";
                            using (var actBase = baseHive.OpenSubKey(actMachineKey, writable: true))
                            {
                                actBase?.DeleteSubKeyTree("DebugInformation", throwOnMissingSubKey: false);
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(packageFullName))
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\PackagedAppXDebug", writable: true))
                    {
                        key?.DeleteSubKeyTree(packageFullName, throwOnMissingSubKey: false);
                    }

                    string actUserKey = $@"Software\Classes\ActivatableClasses\Package\{packageFullName}";
                    using (var key = Registry.CurrentUser.OpenSubKey(actUserKey, writable: true))
                    {
                        key?.DeleteSubKeyTree("DebugInformation", throwOnMissingSubKey: false);
                    }
                }
                catch { }
            }

            return success;
        }

        public static bool InstallOrUpdateWithElevation(string exeName, string? targetFullPath = null, string? packageFullName = null)
        {
            // 1. Copy files to %AppData%\IFEOPredatorPatcher
            try
            {
                DeployFilesToAppData();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IFEOService] Deploy error: {ex.Message}");
            }

            string permanentPath = GetInstalledLauncherPath();

            // 2. Set registry hook pointing to the permanent AppData path
            if (IsElevated())
            {
                return InstallHookDirect(exeName, permanentPath, targetFullPath, packageFullName);
            }

            string targetArg = !string.IsNullOrWhiteSpace(targetFullPath) ? $" \"{targetFullPath}\"" : " \"\"";
            string pkgArg = !string.IsNullOrWhiteSpace(packageFullName) ? $" \"{packageFullName}\"" : "";
            return RunElevatedCommand($"--install-ifeo \"{exeName}\" \"{permanentPath}\"{targetArg}{pkgArg}");
        }

        public static bool UninstallHookWithElevation(string exeName, string? packageFullName = null)
        {
            if (IsElevated())
            {
                return UninstallHookDirect(exeName, packageFullName);
            }

            string pkgArg = !string.IsNullOrWhiteSpace(packageFullName) ? $" \"{packageFullName}\"" : "";
            return RunElevatedCommand($"--uninstall-ifeo \"{exeName}\"{pkgArg}");
        }

        private static bool RunElevatedCommand(string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = GetCurrentLauncherPath(),
                    Arguments = arguments,
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (var process = Process.Start(startInfo))
                {
                    process?.WaitForExit();
                    return process?.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
