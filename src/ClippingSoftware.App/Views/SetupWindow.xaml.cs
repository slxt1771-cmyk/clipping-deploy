using System.IO;
using System.Windows;
using ClippingSoftware.App.ViewModels;
using ClippingSoftware.Data;
using ClippingSoftware.Data.ClipLibrary;
using Microsoft.Win32;

namespace ClippingSoftware.App.Views;

/// <summary>
/// The first-run setup wizard window. Shown once, before MainWindow exists, whenever
/// AppSettings.HasCompletedFirstRunSetup is false - see <see cref="RunIfNeeded"/>.
///
/// The three Browse handlers live in code-behind rather than as view-model commands because file/folder
/// pickers are view concerns that need an owner window to be modal against; the view model holds only the
/// resulting paths.
/// </summary>
public partial class SetupWindow : Window
{
    private readonly SetupViewModel _viewModel;

    public SetupWindow(SettingsRepository settingsRepository)
    {
        InitializeComponent();

        _viewModel = new SetupViewModel(settingsRepository);
        DataContext = _viewModel;
        _viewModel.Completed += result =>
        {
            DialogResult = result;
            Close();
        };
    }

    /// <summary>
    /// Runs the wizard if this install hasn't been through it, and reports whether the app should carry on
    /// starting. Returns true when setup completed *or* wasn't needed; false only when the user cancelled,
    /// in which case the caller should exit without opening the main window (the settings were never
    /// saved, so the wizard runs again next launch rather than leaving a half-configured app).
    ///
    /// Takes its own Database/SettingsRepository rather than borrowing MainViewModel's: MainViewModel reads
    /// settings in its constructor, so it must not be constructed until after the wizard has written them.
    /// Database.Initialize is idempotent, so opening a second short-lived one here is safe.
    /// </summary>
    public static bool RunIfNeeded()
    {
        SettingsRepository settingsRepository;
        try
        {
            settingsRepository = new SettingsRepository(new Database());
            if (settingsRepository.Load().HasCompletedFirstRunSetup)
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            // A database that can't even be opened is a problem the wizard can't fix, and blocking startup
            // behind a setup screen that will fail the same way helps nobody - let the app start and
            // surface the failure through its normal status line instead.
            MessageBox.Show(
                $"Could not open the settings database, so first-run setup was skipped:\n\n{ex.Message}",
                "Clipping Software", MessageBoxButton.OK, MessageBoxImage.Warning);
            return true;
        }

        return new SetupWindow(settingsRepository).ShowDialog() == true;
    }

    private void BrowseObs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select obs64.exe",
            Filter = "OBS Studio (obs64.exe)|obs64.exe|Executables (*.exe)|*.exe",
            CheckFileExists = true,
        };

        var current = _viewModel.ObsExecutablePath;
        if (!string.IsNullOrWhiteSpace(current))
        {
            var directory = Path.GetDirectoryName(current);
            if (Directory.Exists(directory))
            {
                dialog.InitialDirectory = directory;
            }
        }

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.ObsExecutablePath = dialog.FileName;
        }
    }

    private void BrowseClips_Click(object sender, RoutedEventArgs e) =>
        _viewModel.ClipStorageFolder = PickFolder("Choose where recordings are saved", _viewModel.ClipStorageFolder);

    private void BrowseExports_Click(object sender, RoutedEventArgs e) =>
        _viewModel.ExportStorageFolder = PickFolder("Choose where exported clips are saved", _viewModel.ExportStorageFolder);

    /// <summary>Shows a folder picker seeded with the current value, returning that same value unchanged
    /// if the user cancels - so a cancelled browse never blanks a field the user already filled in.</summary>
    private string PickFolder(string title, string currentValue)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
        };

        if (Directory.Exists(currentValue))
        {
            dialog.InitialDirectory = currentValue;
        }

        return dialog.ShowDialog(this) == true ? dialog.FolderName : currentValue;
    }
}
