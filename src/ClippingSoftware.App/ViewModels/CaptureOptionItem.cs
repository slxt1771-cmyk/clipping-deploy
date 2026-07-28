namespace ClippingSoftware.App.ViewModels;

/// <summary>Binding-friendly (Label, Value) pair for the monitor/window picker ComboBoxes - wraps the tuples
/// ObsController.GetMonitorOptions/GetWindowOptions return, since WPF ComboBox item binding wants a type
/// with real properties rather than a ValueTuple. ToString() returns Label so a plain (no DisplayMemberPath)
/// ComboBox already renders it correctly.</summary>
public record CaptureOptionItem(string Label, string Value)
{
    public override string ToString() => Label;
}
