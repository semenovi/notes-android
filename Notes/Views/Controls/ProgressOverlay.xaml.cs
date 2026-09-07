using Notes.Services;

namespace Notes.Views.Controls;

public partial class ProgressOverlay : ContentView
{
    // Bumped on every show/hide request. An in-flight animation whose generation no
    // longer matches must not touch visibility — otherwise a Hide animation that was
    // superseded by a new Show still runs its trailing `IsVisible = false` and buries
    // the session that just appeared.
    private int _generation;
    private ProgressSession? _desired;

    public ProgressOverlay()
    {
        InitializeComponent();
    }

    public async void ShowProgress(ProgressSession session)
    {
        _desired = session;
        int gen = ++_generation;
        UpdateContent(session);
        ProgressBorder.Opacity = 0;
        ProgressBorder.TranslationY = 16;
        ProgressBorder.IsVisible = true;
        await Task.WhenAll(
            ProgressBorder.FadeTo(1, 200),
            ProgressBorder.TranslateTo(0, 0, 200, Easing.CubicOut));
        if (gen != _generation) return;
    }

    public void UpdateProgress(ProgressSession session)
    {
        if (ProgressBorder.IsVisible && ReferenceEquals(_desired, session))
            UpdateContent(session);
    }

    public async void HideProgress()
    {
        _desired = null;
        int gen = ++_generation;
        if (!ProgressBorder.IsVisible) return;
        await Task.WhenAll(
            ProgressBorder.FadeTo(0, 300),
            ProgressBorder.TranslateTo(0, 8, 300, Easing.CubicIn));
        if (gen != _generation) return;
        ProgressBorder.IsVisible = false;
        ProgressBorder.Opacity = 1;
        ProgressBorder.TranslationY = 0;
    }

    public void Reset()
    {
        _desired = null;
        _generation++;
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
