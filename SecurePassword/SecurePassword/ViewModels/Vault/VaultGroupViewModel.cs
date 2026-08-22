using System.Collections.ObjectModel;

namespace SecurePassword.ViewModels.Vault;

/// <summary>
/// Group model for categorized CollectionView representation (Logins, Cards, Notes).
/// </summary>
public sealed class VaultGroupViewModel : ObservableCollection<VaultListItemViewModel>
{
    public string Key { get; }
    public string Title { get; }
    public string IconGlyph { get; }
    public string IconSource { get; }
    public int ItemCount => Count;

    public VaultGroupViewModel(
        string key,
        string title,
        string iconGlyph,
        string iconSource,
        IEnumerable<VaultListItemViewModel> items)
        : base(items)
    {
        Key = key;
        Title = title;
        IconGlyph = iconGlyph;
        IconSource = iconSource;
    }
}
