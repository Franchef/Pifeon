using CommunityToolkit.Mvvm.ComponentModel;

namespace Pifeon.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public string Title { get; } = "Pifeon";
}
