using CommunityToolkit.Mvvm.ComponentModel;

namespace Fuse.ExternalEditor.ViewModels;

/// <summary>
/// Base for all view models. <see cref="ObservableObject"/> supplies
/// <c>INotifyPropertyChanged</c> so the source-generated <c>[ObservableProperty]</c>
/// fields raise change notifications without hand-written boilerplate.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
}
