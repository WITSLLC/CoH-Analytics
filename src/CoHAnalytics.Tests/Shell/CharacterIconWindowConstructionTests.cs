using CoHAnalytics.Shell;
using System.Windows;

namespace CoHAnalytics.Tests.Shell;

[Collection(WpfDispatcherCollection.Name)]
public sealed class CharacterIconWindowConstructionTests
{
    private readonly WpfDispatcherFixture _dispatcher;

    public CharacterIconWindowConstructionTests(WpfDispatcherFixture dispatcher)
    {
        _dispatcher = dispatcher;
    }

    [Fact]
    public Task Picker_and_editor_windows_load_all_runtime_resources() =>
        _dispatcher.InvokeAsync(() =>
        {
            var theme = new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/CoHAnalytics;component/Themes/Hero/HeroTheme.xaml",
                    UriKind.Absolute)
            };
            Application.Current.Resources.MergedDictionaries.Add(theme);
            try
            {
                var picker = new CharacterIconPickerWindow();
                var editor = new CustomCharacterIconEditorWindow();

                Assert.NotNull(picker.Content);
                Assert.NotNull(editor.Content);

                picker.Close();
                editor.Close();
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(theme);
            }
        });
}
