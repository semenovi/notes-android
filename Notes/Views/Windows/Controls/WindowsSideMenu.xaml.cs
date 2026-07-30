using Notes.Services.Sync;

namespace Notes.Views.Windows.Controls;

public partial class WindowsSideMenu : ContentView
{
  private const double PanelWidth = 280;

  private readonly SyncSettingsService _syncSettingsService;
  private bool _animating;

  public event EventHandler? SyncToggleClicked;
  public event EventHandler? SyncNowClicked;
  public event EventHandler? SyncSettingsClicked;
  public event EventHandler? ExportClicked;
  public event EventHandler? ImportClicked;

  public WindowsSideMenu()
  {
    InitializeComponent();
    var services = App.Current!.Handler!.MauiContext!.Services;
    _syncSettingsService = services.GetService<SyncSettingsService>()!;
  }

  public async Task ShowAsync()
  {
    if (_animating || Root.IsVisible) return;
    _animating = true;

    var settings = await _syncSettingsService.LoadAsync();
    UpdateSyncState(settings.Enabled);

    Panel.TranslationX = -PanelWidth;
    Scrim.Opacity = 0;
    Root.IsVisible = true;
    await Task.WhenAll(
        Panel.TranslateTo(0, 0, 180, Easing.CubicOut),
        Scrim.FadeTo(0.35, 180));
    _animating = false;
  }

  public async Task HideAsync()
  {
    if (_animating || !Root.IsVisible) return;
    _animating = true;
    await Task.WhenAll(
        Panel.TranslateTo(-PanelWidth, 0, 150, Easing.CubicIn),
        Scrim.FadeTo(0, 150));
    Root.IsVisible = false;
    _animating = false;
  }

  private void UpdateSyncState(bool enabled)
  {
    SyncStateLabel.Text = enabled ? "on" : "off";
    SyncStateLabel.TextColor = enabled ? Color.FromArgb("#34C759") : Color.FromArgb("#8E8E93");
  }

  private async void OnScrimTapped(object sender, EventArgs e) => await HideAsync();

  private async Task InvokeItemAsync(EventHandler? handler)
  {
    await HideAsync();
    handler?.Invoke(this, EventArgs.Empty);
  }

  private async void OnSyncToggleTapped(object sender, EventArgs e) => await InvokeItemAsync(SyncToggleClicked);
  private async void OnSyncNowTapped(object sender, EventArgs e) => await InvokeItemAsync(SyncNowClicked);
  private async void OnSyncSettingsTapped(object sender, EventArgs e) => await InvokeItemAsync(SyncSettingsClicked);
  private async void OnExportTapped(object sender, EventArgs e) => await InvokeItemAsync(ExportClicked);
  private async void OnImportTapped(object sender, EventArgs e) => await InvokeItemAsync(ImportClicked);
}
