using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IFEOPredatorPatcher.Services
{
    public class SenseAppInfo
    {
        public string Name { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string ExeFileName => Path.GetFileName(ExecutablePath);
        public string InstallType { get; set; } = string.Empty; // "UWP (WindowsApps)" or "Desktop"
        public string? PackageFullName { get; set; }
        public string? AppId { get; set; }

        public string? AppUserModelId
        {
            get
            {
                if (string.IsNullOrWhiteSpace(PackageFullName)) return null;
                string[] parts = PackageFullName.Split('_');
                if (parts.Length >= 5)
                {
                    string pfn = $"{parts[0]}_{parts[parts.Length - 1]}";
                    return $"{pfn}!{AppId ?? "App"}";
                }
                return $"{PackageFullName}!{AppId ?? "App"}";
            }
        }

        public override string ToString() => $"{Name} ({InstallType}) - {ExecutablePath}";
    }

    public static class SenseScanner
    {
        public static List<SenseAppInfo> ScanAll()
        {
            var results = new List<SenseAppInfo>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Scan WindowsApps packages
            string windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            if (Directory.Exists(windowsApps))
            {
                try
                {
                    var dirs = Directory.GetDirectories(windowsApps, "*AcerIncorporated.*", SearchOption.TopDirectoryOnly);
                    foreach (var dir in dirs)
                    {
                        string dirName = Path.GetFileName(dir);
                        string win32Dir = Path.Combine(dir, "Win32");
                        if (!Directory.Exists(win32Dir))
                        {
                            win32Dir = dir;
                        }

                        // Check for NitroSense.exe
                        string nitroExe = Path.Combine(win32Dir, "NitroSense.exe");
                        if (File.Exists(nitroExe) && seenPaths.Add(nitroExe))
                        {
                            results.Add(new SenseAppInfo
                            {
                                Name = "NitroSense",
                                ExecutablePath = nitroExe,
                                InstallType = "UWP (WindowsApps)",
                                PackageFullName = dirName,
                                AppId = "App"
                            });
                        }

                        // Check for PredatorSense.exe
                        string predatorExe = Path.Combine(win32Dir, "PredatorSense.exe");
                        if (File.Exists(predatorExe) && seenPaths.Add(predatorExe))
                        {
                            results.Add(new SenseAppInfo
                            {
                                Name = "PredatorSense",
                                ExecutablePath = predatorExe,
                                InstallType = "UWP (WindowsApps)",
                                PackageFullName = dirName,
                                AppId = "App"
                            });
                        }
                    }
                }
                catch
                {
                    // Access restriction or missing
                }
            }

            // 2. Scan classic Program Files directories
            string[] searchBases = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Acer"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Acer"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OEM"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "OEM")
            };

            foreach (var b in searchBases)
            {
                if (!Directory.Exists(b)) continue;

                try
                {
                    foreach (var exe in Directory.GetFiles(b, "*.exe", SearchOption.AllDirectories))
                    {
                        string fn = Path.GetFileName(exe);
                        if (fn.Equals("NitroSense.exe", StringComparison.OrdinalIgnoreCase) && seenPaths.Add(exe))
                        {
                            results.Add(new SenseAppInfo
                            {
                                Name = "NitroSense",
                                ExecutablePath = exe,
                                InstallType = "Desktop"
                            });
                        }
                        else if (fn.Equals("PredatorSense.exe", StringComparison.OrdinalIgnoreCase) && seenPaths.Add(exe))
                        {
                            results.Add(new SenseAppInfo
                            {
                                Name = "PredatorSense",
                                ExecutablePath = exe,
                                InstallType = "Desktop"
                            });
                        }
                    }
                }
                catch
                {
                    // Ignore search permission errors
                }
            }

            return results;
        }
    }
}
