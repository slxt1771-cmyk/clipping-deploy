using System.Windows.Controls;

namespace ClippingSoftware.App.Views;

/// <summary>
/// Game profile management UI embedded in MainWindow's Settings tab. Purely a shell around
/// GameProfilesViewModel (set as DataContext by the host) - all behavior lives in the view model.
/// </summary>
public partial class GameProfilesView : UserControl
{
    public GameProfilesView()
    {
        InitializeComponent();
    }
}
