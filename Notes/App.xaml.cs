namespace Notes;

public partial class App : Application
{
  public App()
  {
    InitializeComponent();

    // Убедитесь, что AppShell правильно инициализирован
    MainPage = new AppShell();

    // Альтернативно, можно напрямую задать MainPage для отладки
    // MainPage = new Views.MainPage();
  }

  // Добавьте обработку событий жизненного цикла для отладки
  protected override void OnStart()
  {
    // Код, который выполняется при запуске приложения
    base.OnStart();
  }
}