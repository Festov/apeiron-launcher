using System.Windows;
using System.Windows.Input;
using Apeiron.Services;

namespace Apeiron;

public partial class OnboardingWindow : Window, ILocalizable
{
    private sealed class LanguageOption
    {
        public string Code { get; init; } = "";
        public string Display { get; set; } = "";
    }

    private readonly SettingsService _settings;

    public bool CreateInstanceRequested { get; private set; }

    public OnboardingWindow(SettingsService settings)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        RoundedDialogChrome.Attach(DialogChrome);
        _settings = settings;

        Loaded += (_, _) =>
        {
            ApplyLocalization();
            SelectLanguage(settings.Language);
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        };
        Closed += (_, _) => LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged() => Dispatcher.Invoke(ApplyLocalization);

    public void ApplyLocalization()
    {
        Title = LocalizationService.T("onboard.window_title");
        TitleText.Text = LocalizationService.T("onboard.title");
        IntroText.Text = LocalizationService.T("onboard.intro");
        LanguageLabel.Text = LocalizationService.T("settings.language");
        CreateInstanceButton.Content = LocalizationService.T("onboard.create_instance");
        SkipButton.Content = LocalizationService.T("onboard.skip");

        var selected = LanguageComboBox.SelectedValue as string ?? _settings.Language;
        LanguageComboBox.ItemsSource = new[]
        {
            new LanguageOption { Code = "auto", Display = LocalizationService.T("lang.auto") },
            new LanguageOption { Code = "en", Display = LocalizationService.T("lang.en") },
            new LanguageOption { Code = "ru", Display = LocalizationService.T("lang.ru") },
            new LanguageOption { Code = "zh", Display = LocalizationService.T("lang.zh") }
        };
        SelectLanguage(selected);
    }

    private void SelectLanguage(string code)
    {
        foreach (LanguageOption item in LanguageComboBox.Items)
        {
            if (item.Code == code)
            {
                LanguageComboBox.SelectedItem = item;
                return;
            }
        }
        LanguageComboBox.SelectedIndex = 0;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void CreateInstance_Click(object sender, RoutedEventArgs e)
    {
        Finish(createInstance: true);
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        Finish(createInstance: false);
    }

    private void Finish(bool createInstance)
    {
        var language = (LanguageComboBox.SelectedItem as LanguageOption)?.Code ?? "auto";
        _settings.Language = language;
        _settings.OnboardingCompleted = true;
        _settings.Save();
        LocalizationService.Instance.ApplySetting(language);

        CreateInstanceRequested = createInstance;
        DialogResult = true;
        Close();
    }
}
