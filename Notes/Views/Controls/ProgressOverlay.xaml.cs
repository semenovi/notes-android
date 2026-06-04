using Notes.Services;

namespace Notes.Views.Controls;

public partial class ProgressOverlay : ContentView
{
    public ProgressOverlay()
    {
        InitializeComponent();
    }

    public async void ShowProgress(ProgressSession session)
    {
        UpdateContent(session);
        ProgressBorder.Opacity = 0;
        ProgressBorder.TranslationY = 16;
        ProgressBorder.IsVisible = true;
        await Task.WhenAll(
            ProgressBorder.FadeTo(1, 200),
            ProgressBorder.TranslateTo(0, 0, 200, Easing.CubicOut));
    }

    public void UpdateProgress(ProgressSession session)
    {
        if (ProgressBorder.IsVisible)
            UpdateContent(session);
    }

    public async void HideProgress()
    {
        if (!ProgressBorder.IsVisible) return;
        await Task.WhenAll(
            ProgressBorder.FadeTo(0, 300),
            ProgressBorder.TranslateTo(0, 8, 300, Easing.CubicIn));
        ProgressBorder.IsVisible = false;
        ProgressBorder.TranslationY = 0;
    }

    public void Reset()
    {
        ProgressBorder.IsVisible = false;
        ProgressBorder.Opacity = 1;
        ProgressBorder.TranslationY = 0;
    }

    private void UpdateContent(ProgressSession session)
    {
        TitleLabel.Text = session.Title;
        bool indeterminate = double.IsNaN(session.Progress);
        Spinner.IsVisible = indeterminate;
        ProgressBarControl.IsVisible = !indeterminate;
        if (!indeterminate)
            ProgressBarControl.Progress = session.Progress;
        bool hasSub = !string.IsNullOrEmpty(session.Subtitle);
        SubtitleLabel.IsVisible = hasSub;
        SubtitleLabel.Text = session.Subtitle ?? "";
    }
}
