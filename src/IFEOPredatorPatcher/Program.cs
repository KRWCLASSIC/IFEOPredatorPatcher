using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using IFEOPredatorPatcher.Bootstrapper;
using IFEOPredatorPatcher.Services;
using IFEOPredatorPatcher.UI;

namespace IFEOPredatorPatcher
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            // Log all process invocations to diagnose startup triggers
            try
            {
                string logFile = Path.Combine(IFEOService.GetAppDataRootDirectory(), "bootstrapper.log");
                string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Program.Main] PID: {System.Diagnostics.Process.GetCurrentProcess().Id}, Args ({args.Length}): [{string.Join(" | ", args)}]\n";
                File.AppendAllText(logFile, logLine);
            }
            catch { }

            // 1. Check for CLI / Elevation Helper arguments
            if (args.Length >= 2 && args[0].Equals("--install-ifeo", StringComparison.OrdinalIgnoreCase))
            {
                string exeName = args[1];
                string launcherPath = args.Length >= 3 ? args[2] : IFEOService.GetCurrentLauncherPath();
                string? targetFullPath = (args.Length >= 4 && !string.IsNullOrWhiteSpace(args[3])) ? args[3] : null;
                string? packageFullName = (args.Length >= 5 && !string.IsNullOrWhiteSpace(args[4])) ? args[4] : null;
                bool success = IFEOService.InstallHookDirect(exeName, launcherPath, targetFullPath, packageFullName);
                return success ? 0 : 1;
            }

            if (args.Length >= 2 && args[0].Equals("--uninstall-ifeo", StringComparison.OrdinalIgnoreCase))
            {
                string exeName = args[1];
                string? packageFullName = (args.Length >= 3 && !string.IsNullOrWhiteSpace(args[2])) ? args[2] : null;
                bool success = IFEOService.UninstallHookDirect(exeName, packageFullName);
                return success ? 0 : 1;
            }

            // 2. Check for IFEO or AppX Bootstrapper Mode
            if (args.Length > 0)
            {
                // Case A: Explicit target path passed in args[0] (Standard IFEO)
                if (IsTargetExecutable(args[0]))
                {
                    string targetPath = args[0];
                    string[] forwardArgs = args.Skip(1).ToArray();

                    BootstrapperEngine.Launch(targetPath, forwardArgs);
                    return 0;
                }

                // Case B: Windows AppModel / PLM Debugger launch (-p <pid> -tid <tid>)
                int pIndex = Array.FindIndex(args, a => a.Equals("-p", StringComparison.OrdinalIgnoreCase));
                if (pIndex >= 0 && pIndex + 1 < args.Length && int.TryParse(args[pIndex + 1], out int targetPid))
                {
                    string? targetExe = null;
                    try
                    {
                        using (var proc = Process.GetProcessById(targetPid))
                        {
                            targetExe = proc.MainModule?.FileName;
                            try { proc.Kill(); } catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            string logFile = Path.Combine(IFEOService.GetAppDataRootDirectory(), "bootstrapper.log");
                            File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Warning inspecting/terminating suspended process {targetPid}: {ex.Message}\n");
                        }
                        catch { }
                    }

                    if (string.IsNullOrWhiteSpace(targetExe) || !File.Exists(targetExe))
                    {
                        var apps = SenseScanner.ScanAll();
                        var targetApp = apps.FirstOrDefault(a => a.InstallType.Contains("WindowsApps")) ?? apps.FirstOrDefault();
                        targetExe = targetApp?.ExecutablePath;
                    }

                    if (targetExe != null && File.Exists(targetExe))
                    {
                        BootstrapperEngine.Launch(targetExe, Array.Empty<string>());
                        return 0;
                    }
                }

                // Case C: AppX / Centennial Activation (-ServerName:App... or AppX flags)
                if (args.Any(a => a.IndexOf("ServerName", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  a.IndexOf("AppX", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  a.IndexOf("Embedding", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    var apps = SenseScanner.ScanAll();
                    var targetApp = apps.FirstOrDefault(a => a.InstallType.Contains("WindowsApps")) ?? apps.FirstOrDefault();
                    if (targetApp != null && File.Exists(targetApp.ExecutablePath))
                    {
                        BootstrapperEngine.Launch(targetApp.ExecutablePath, args);
                        return 0;
                    }
                }
            }

            // 3. Management UI Mode (User double-clicked or ran without args)
            var app = new App();
            app.InitializeComponent();
            return app.Run();
        }

        private static bool IsTargetExecutable(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                string cleanPath = path.Trim('\"');
                if (File.Exists(cleanPath) && cleanPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    string fileName = Path.GetFileName(cleanPath);
                    return fileName.IndexOf("Sense", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           fileName.IndexOf("Nitro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           fileName.IndexOf("Predator", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                // Fallback
            }

            return false;
        }
    }
}
