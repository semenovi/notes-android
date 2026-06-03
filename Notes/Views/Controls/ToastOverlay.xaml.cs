namespace Notes.Views.Controls;

public partial class ToastOverlay : ContentView
{
    private CancellationTokenSource? _hideCts;

    public ToastOverlay()
    {
        InitializeComponent();
    }

    public async void ShowToast(string message)
    {
        _hideCts?.Cancel();
        _hideCts = new CancellationTokenSource();
        var token = _hideCts.Token;

        ToastLabel.Text = message;
        ToastBorder.Opacity = 0;
        ToastBorder.TranslationY = 16;
        ToastBorder.IsVisible = true;

        await Task.WhenAll(
            ToastBorder.FadeTo(1, 200),
            ToastBorder.TranslateTo(0, 0, 200, Easing.CubicOut));

        try { await Task.Delay(2500, token); }
        catch (OperationCanceledException) { return; }

        await Task.WhenAll(
            ToastBorder.FadeTo(0, 300),
            ToastBorder.TranslateTo(0, 8, 300, Easing.CubicIn));

        ToastBorder.IsVisible = false;
    }
}
