using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;
using Apeiron.Services;

namespace Apeiron;

public partial class AuthWindow : Window, ILocalizable
{
    private readonly MainWindow _mainWindow;
    private readonly AuthService _auth;
    private bool _isProcessing = false;

    public AuthWindow(MainWindow mainWindow, AuthService auth)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _auth = auth;
        Owner = mainWindow;
        
        Loaded += async (s, e) =>
        {
            ApplyLocalization();
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            await InitializeWebView();
        };
        Closed += (_, _) => LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged() => Dispatcher.Invoke(ApplyLocalization);

    public void ApplyLocalization()
    {
        Title = LocalizationService.T("auth.title");
        AuthHeadingText.Text = LocalizationService.T("auth.heading");
        AuthSubtitleText.Text = LocalizationService.T("auth.subtitle");
    }
    
    private async Task InitializeWebView()
    {
        try
        {
            await AuthWebView.EnsureCoreWebView2Async(null);
            AuthWebView.CoreWebView2.Settings.IsScriptEnabled = true;
            AuthWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;
            
            AuthWebView.CoreWebView2.NavigationStarting += (sender, args) =>
            {
                if (_isProcessing) return;
                
                var uri = new Uri(args.Uri);
                
                if (uri.Host == "login.live.com" && 
                    uri.AbsolutePath == "/oauth20_desktop.srf" &&
                    System.Web.HttpUtility.ParseQueryString(uri.Query).Get("code") != null)
                {
                    _isProcessing = true;
                    var code = System.Web.HttpUtility.ParseQueryString(uri.Query).Get("code");
                    
                    Dispatcher.Invoke(() =>
                    {
                        Close();
                    });
                    
                    _ = Task.Run(async () =>
                    {
                        await _auth.ExchangeCode(code);
                    });
                    
                    args.Cancel = true;
                }
            };
            
            var authUrl = "https://login.live.com/oauth20_authorize.srf?" +
                "client_id=00000000402b5328" +
                "&response_type=code" +
                "&redirect_uri=https://login.live.com/oauth20_desktop.srf" +
                "&scope=XboxLive.signin%20offline_access" +
                "&response_mode=query";
            
            AuthWebView.CoreWebView2.Navigate(authUrl);
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationService.F("auth.init_error", ex.Message), LocalizationService.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}