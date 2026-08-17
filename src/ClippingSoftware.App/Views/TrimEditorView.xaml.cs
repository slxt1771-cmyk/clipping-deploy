using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ClippingSoftware.App.ViewModels;

namespace ClippingSoftware.App.Views;

/// <summary>
/// Hosts a MediaElement for scrubbing/previewing the source clip plus in/out point controls wired
/// to <see cref="TrimEditorViewModel"/>. MediaElement.Position and .Play()/.Pause() are plain CLR
/// members, not DependencyProperties/bindable commands, so playback/seek wiring is necessarily
/// code-behind here - that's the standard WPF MediaElement pattern, not something specific to this
/// app's MVVM conventions.
///
/// Embedded persistently inside ClipBrowserView (M16, was a separate popup Window) rather than
/// created fresh per edit session, so DataContext changes as the user switches clips instead of a new
/// instance being constructed - MediaElement's source/lock needs releasing on every such change, not
/// just once on window close (see DataContextChanged below).
/// </summary>
public partial class TrimEditorView : UserControl
{
    private readonly DispatcherTimer _positionTimer;
    private bool _isDraggingSlider;

    /// <summary>MediaElement has no IsPlaying property of its own, so Play_Click/Pause_Click/Player_Click
    /// all track it here to know which state a plain click on the video should toggle to.</summary>
    private bool _isPlaying;

    public TrimEditorView()
    {
        InitializeComponent();

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _positionTimer.Tick += (_, _) => UpdatePositionFromPlayer();

        DataContextChanged += (_, e) =>
        {
            // Release whatever was previously loaded (switching clips, or going back to the library and
            // clearing ActiveEditor to null) before opening the next one - MediaElement keeps its source
            // file locked until explicitly told to release it, and unlike the old per-session window this
            // same MediaElement instance now lives across multiple clips.
            Player.Stop();
            Player.Close();

            if (e.NewValue is TrimEditorViewModel vm)
            {
                Player.Source = new Uri(vm.SourceClip.FilePath);
                ScrubSlider.Maximum = Math.Max(0.01, vm.TotalSeconds);
            }
        };

        Loaded += (_, _) => _positionTimer.Start();
        Unloaded += (_, _) => _positionTimer.Stop();
    }

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        // Nudge playback then immediately pause so the first frame renders - MediaElement shows a
        // blank/black surface after just setting Source until playback has actually started once.
        Player.Play();
        Player.Pause();
        _isPlaying = false;
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        Player.Play();
        _isPlaying = true;
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        Player.Pause();
        _isPlaying = false;
    }

    /// <summary>Click anywhere on the video to toggle play/pause, matching the common player convention.</summary>
    private void Player_Click(object sender, MouseButtonEventArgs e)
    {
        if (_isPlaying)
        {
            Pause_Click(sender, e);
        }
        else
        {
            Play_Click(sender, e);
        }
    }

    private void SetIn_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is TrimEditorViewModel vm)
        {
            vm.SetInPointFromPlayhead();
        }
    }

    private void SetOut_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is TrimEditorViewModel vm)
        {
            vm.SetOutPointFromPlayhead();
        }
    }

    private void ScrubSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e) => _isDraggingSlider = true;

    private void ScrubSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSlider = false;
        Player.Position = TimeSpan.FromSeconds(ScrubSlider.Value);
        if (DataContext is TrimEditorViewModel vm)
        {
            vm.CurrentPositionSeconds = ScrubSlider.Value;
        }
    }

    private void UpdatePositionFromPlayer()
    {
        if (_isDraggingSlider || DataContext is not TrimEditorViewModel vm)
        {
            return;
        }

        var seconds = Player.Position.TotalSeconds;
        vm.CurrentPositionSeconds = seconds;
        ScrubSlider.Value = seconds;
    }
}
