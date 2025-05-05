using Notes.ViewModels;

namespace Notes.Views;

public partial class MainPage : ContentPage
{
  private MainPageViewModel _viewModel => BindingContext as MainPageViewModel;

  public MainPage()
  {
    InitializeComponent();
  }

  protected override void OnAppearing()
  {
    base.OnAppearing();
    _viewModel?.Initialize();
  }
}