using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IFEOPredatorPatcher.Bootstrapper;
using IFEOPredatorPatcher.Services;

namespace IFEOPredatorPatcher.UI
{
    public partial class MainWindow : Window
    {
        private List<SenseAppInfo> _detectedApps = new List<SenseAppInfo>();
        private SenseAppInfo? _selectedApp;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateElevationStatus();
            ScanAndPopulateApps();
        }

        private void UpdateElevationStatus()
        {
            bool isElevated = IFEOService.IsElevated();
            ElevationStatusText.Text = isElevated 
                ? "Running as Administrator" 
                : "Standard User (UAC will elevate on install/uninstall)";
        }

        private void ScanAndPopulateApps()
        {
            _detectedApps = SenseScanner.ScanAll();
            TargetAppComboBox.Items.Clear();

            if (_detectedApps.Count == 0)
            {
                TargetAppComboBox.Items.Add("No Sense application found");
                TargetAppComboBox.SelectedIndex = 0;
                TargetAppComboBox.IsEnabled = false;
                InstallButton.IsEnabled = false;
                UninstallButton.IsEnabled = false;
                SelectedPathText.Text = "Please ensure NitroSense.exe or PredatorSense.exe is installed.";
                return;
            }

            TargetAppComboBox.IsEnabled = true;
            InstallButton.IsEnabled = true;
            UninstallButton.IsEnabled = true;

            foreach (var app in _detectedApps)
            {
                TargetAppComboBox.Items.Add($"{app.Name} ({app.InstallType}) - {app.ExecutablePath}");
            }

            TargetAppComboBox.SelectedIndex = 0;
        }

        private void TargetAppComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TargetAppComboBox.SelectedIndex >= 0 && TargetAppComboBox.SelectedIndex < _detectedApps.Count)
            {
                _selectedApp = _detectedApps[TargetAppComboBox.SelectedIndex];
                SelectedPathText.Text = _selectedApp.ExecutablePath;
                RefreshHookStatus();
            }
        }

        private void RescanButton_Click(object sender, RoutedEventArgs e)
        {
            ScanAndPopulateApps();
        }

        private void RefreshHookStatus()
        {
            if (_selectedApp == null)
            {
                SetStatusUnknown();
                return;
            }

            var status = IFEOService.CheckStatus(_selectedApp.ExeFileName, _selectedApp.PackageFullName, out string currentDebugger);

            switch (status)
            {
                case IFEOStatus.HookedToSelf:
                    StatusText.Text = "Status: Installed (Active in AppData)";
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x80, 0x00));
                    StatusDetailText.Text = $"Debugger: {currentDebugger}";
                    InstallButton.Content = "Update";
                    InstallButton.IsEnabled = true;
                    UninstallButton.IsEnabled = true;
                    break;

                case IFEOStatus.HookedToOther:
                    StatusText.Text = "Status: Hooked to other debugger";
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0x80, 0x00));
                    StatusDetailText.Text = $"Debugger: {currentDebugger}";
                    InstallButton.Content = "Install (Override)";
                    InstallButton.IsEnabled = true;
                    UninstallButton.IsEnabled = true;
                    break;

                case IFEOStatus.NotHooked:
                default:
                    StatusText.Text = "Status: Not Installed";
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x00, 0x00));
                    StatusDetailText.Text = "Stock executable will launch without patches.";
                    InstallButton.Content = "Install";
                    InstallButton.IsEnabled = true;
                    UninstallButton.IsEnabled = false;
                    break;
            }
        }

        private void SetStatusUnknown()
        {
            StatusText.Text = "Status: Unknown";
            StatusText.Foreground = new SolidColorBrush(Colors.Gray);
            StatusDetailText.Text = "Select an application above.";
            InstallButton.Content = "Install";
            InstallButton.IsEnabled = false;
            UninstallButton.IsEnabled = false;
        }

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedApp == null) return;

            bool isUpdating = InstallButton.Content?.ToString() == "Update";
            bool result = IFEOService.InstallOrUpdateWithElevation(_selectedApp.ExeFileName, _selectedApp.ExecutablePath, _selectedApp.PackageFullName);

            if (result)
            {
                string action = isUpdating ? "updated" : "installed";
                MessageBox.Show($"IFEO Bootstrap {action} successfully for {_selectedApp.ExeFileName} in %AppData%\\IFEOPredatorPatcher.",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Operation failed. Administrator permission was not granted.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            RefreshHookStatus();
        }

        private void UninstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedApp == null) return;

            bool result = IFEOService.UninstallHookWithElevation(_selectedApp.ExeFileName, _selectedApp.PackageFullName);

            if (result)
            {
                MessageBox.Show($"IFEO Bootstrap uninstalled for {_selectedApp.ExeFileName}.",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Failed to uninstall IFEO hook.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            RefreshHookStatus();
        }

        private void OpenPluginsFolder_Click(object sender, RoutedEventArgs e)
        {
            string dir = PluginLoader.GetDefaultPluginsDirectory();
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{dir}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open plugins folder:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TestLaunch_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedApp == null || !File.Exists(_selectedApp.ExecutablePath))
            {
                MessageBox.Show("Please select a valid Sense executable first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string targetPath = _selectedApp.ExecutablePath;
            string launcher = IFEOService.GetCurrentLauncherPath();

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = launcher,
                    Arguments = $"\"{targetPath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to launch:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
