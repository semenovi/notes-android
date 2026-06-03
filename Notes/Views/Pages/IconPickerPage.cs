using Microsoft.Maui.Controls.Shapes;

namespace Notes.Views.Pages;

public class IconPickerPage : ContentPage
{
    public static readonly string[] Icons =
    {
        "", // folder
        "", // folder_open
        "", // description
        "", // assignment
        "", // book
        "", // library_books
        "", // note
        "", // notes
        "", // subject
        "", // person
        "", // group
        "", // home
        "", // work
        "", // school
        "", // place
        "", // language
        "", // edit
        "", // settings
        "", // build
        "", // code
        "", // lock
        "", // cloud
        "", // mail
        "", // image
        "", // palette
        "", // music_note
        "", // videocam
        "", // brush
        "", // star
        "", // favorite
        "", // bookmark
        "", // label
        "", // flag
        "", // bar_chart
        "", // alarm
        "", // calendar_today
        "", // security
    };

    private string? _selectedIcon;
    private readonly TaskCompletionSource<string?> _tcs = new();

    public Task<string?> SelectedIconTask => _tcs.Task;

    public IconPickerPage()
    {
        Title = "Choose Icon";

        var cancelItem = new ToolbarItem { Text = "Cancel" };
        cancelItem.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(cancelItem);

        var collectionView = new CollectionView
        {
            ItemsLayout = new GridItemsLayout(5, ItemsLayoutOrientation.Vertical)
            {
                HorizontalItemSpacing = 4,
                VerticalItemSpacing = 4,
            },
            ItemsSource = Icons,
            Margin = new Thickness(8),
            ItemTemplate = new DataTemplate(BuildCell),
        };

        Content = collectionView;
    }

    private View BuildCell()
    {
        var label = new Label
        {
            FontFamily = "MaterialIcons",
            FontSize = 30,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        label.SetBinding(Label.TextProperty, new Binding("."));

        var border = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(10),
            MinimumHeightRequest = 56,
            Content = label,
        };
        border.SetAppThemeColor(Border.BackgroundColorProperty,
            Color.FromArgb("#F0F0F5"), Color.FromArgb("#2C2C2E"));

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnIconTapped;
        border.GestureRecognizers.Add(tap);

        return border;
    }

    private async void OnIconTapped(object? sender, TappedEventArgs e)
    {
        if (sender is View view && view.BindingContext is string icon)
        {
            _selectedIcon = icon;
            await Navigation.PopModalAsync();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _tcs.TrySetResult(_selectedIcon);
    }
}
