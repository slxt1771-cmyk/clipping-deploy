using System.Windows.Controls;
using ClippingSoftware.App.ViewModels;

namespace ClippingSoftware.App.Views;

/// <summary>
/// Clip library browser, hosted in MainWindow's "Clips" tab with a ClipBrowserViewModel as DataContext.
/// </summary>
public partial class ClipBrowserView : UserControl
{
    public ClipBrowserView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Convenience for hosts that construct the view with its view model already built: triggers the
    /// initial load. Not required if the caller already calls ClipBrowserViewModel.LoadAsync() itself.
    /// </summary>
    public async void LoadIfViewModelAttached()
    {
        if (DataContext is ClipBrowserViewModel vm)
        {
            await vm.LoadAsync();
        }
    }
}
