using ClippingSoftware.Data.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClippingSoftware.App.ViewModels;

/// <summary>Binding-friendly wrapper around a Tag, reused across three spots: the Library filter-chip row
/// (IsSelected = "included in the active AND filter"), the TAGS manager's tag list (IsSelected = "currently
/// being edited/its clip checklist is shown"), and a clip card's read-only tag chips (no selection state
/// used there).</summary>
public partial class TagViewModel(Tag tag) : ObservableObject
{
    public Guid Id { get; } = tag.Id;

    [ObservableProperty]
    private string _name = tag.Name;

    [ObservableProperty]
    private string _colorHex = tag.ColorHex;

    [ObservableProperty]
    private bool _isSelected;

    public Tag ToModel() => new() { Id = Id, Name = Name, ColorHex = ColorHex };
}
