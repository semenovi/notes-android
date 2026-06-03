using Notes.Views.Pages;

namespace Notes.Helpers;

public static class IconSet
{
    public static async Task<string?> PickAsync(Page page)
    {
        var picker = new IconPickerPage();
        await page.Navigation.PushModalAsync(new NavigationPage(picker));
        return await picker.SelectedIconTask;
    }
}
