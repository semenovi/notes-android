using Microsoft.Maui.Controls;

namespace Notes;

public partial class MainPage : ContentPage
{
  public MainPage()
  {
    InitializeComponent();
  }

  private void OnButtonClicked(object sender, EventArgs e)
  {
    DisplayAlert("message", "you clicked the button!", "ok");
  }
}