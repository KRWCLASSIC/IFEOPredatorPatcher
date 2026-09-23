using System;
using System.Diagnostics;
using System.IO;

namespace IFEOPredatorPatcher.Bootstrapper
{
    public class PluginContext
    {
        public int ProcessId { get; set; }
        public string TargetPath { get; set; } = string.Empty;
        public string TargetName { get; set; } = string.Empty;
        public string TargetDirectory { get; set; } = string.Empty;
        public string[] CommandLineArgs { get; set; } = Array.Empty<string>();
        public string PluginsDirectory { get; set; } = string.Empty;

        public static PluginContext Create(string targetPath, string[] args, string pluginsDir)
        {
            var process = Process.GetCurrentProcess();
            return new PluginContext
            {
                ProcessId = process.Id,
                TargetPath = targetPath,
                TargetName = Path.GetFileName(targetPath),
                TargetDirectory = Path.GetDirectoryName(targetPath) ?? string.Empty,
                CommandLineArgs = args,
                PluginsDirectory = pluginsDir
            };
        }

        public void Log(string message)
        {
            Debug.WriteLine($"[IFEOPredatorPatcher] {message}");
            try
            {
                string appRoot = Path.GetDirectoryName(PluginsDirectory) ?? PluginsDirectory;
                string logFile = Path.Combine(appRoot, "bootstrapper.log");
                File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Non-blocking log failure
            }
        }
    }
}
