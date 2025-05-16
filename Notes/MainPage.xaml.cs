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
    DisplayAlert("Сообщение", "Вы нажали кнопку!", "OK");
  }
}