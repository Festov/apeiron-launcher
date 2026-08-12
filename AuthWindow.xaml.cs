using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Apeiron.Services;

namespace Apeiron;

public partial class AuthWindow : Window, ILocalizable
{
    private readonly AuthService _auth;
    private readonly string _minecraftDir;
    private bool _isProcessing;

    public AuthWindow(MainWindow mainWindow, AuthService auth, string? minecraftDir = null)
    {
        InitializeComponent();
        _auth = auth;
        _minecraftDir = string.IsNullOrWhiteSpace(minecraftDir)
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".minecraft")
            : minecraftDir.Trim();
        Owner = mainWindow;

        Loaded += async (_, _) =>
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
        AuthHeadingText.Text = _isProcessing
            ? LocalizationService.T("auth.processing")
            : LocalizationService.T("auth.heading");
        AuthSubtitleText.Text = LocalizationService.T("auth.subtitle");
    }

    private async Task InitializeWebView()
    {
        try
        {
            await AuthWebView.EnsureCoreWebView2Async(
                await WebView2Helper.CreateEnvironmentAsync(_minecraftDir));
            AuthWebView.CoreWebView2.Settings.IsScriptEnabled = true;
            AuthWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;

            AuthWebView.CoreWebView2.NavigationStarting -= OnWebViewNavigationStarting;
            AuthWebView.CoreWebView2.NavigationStarting += OnWebViewNavigationStarting;

            AuthWebView.CoreWebView2.Navigate(AuthService.BuildAuthorizeUrl());
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                LocalizationService.F("auth.init_error", ex.Message),
                LocalizationService.T("common.error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnWebViewNavigationStarting(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs args)
    {
        if (_isProcessing)
            return;

        if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri))
            return;

        if (uri.Host == "login.live.com" &&
            uri.AbsolutePath == "/oauth20_desktop.srf" &&
            !string.IsNullOrWhiteSpace(System.Web.HttpUtility.ParseQueryString(uri.Query).Get("code")))
        {
            _isProcessing = true;
            var code = System.Web.HttpUtility.ParseQueryString(uri.Query).Get("code");
            args.Cancel = true;
            _ = CompleteAuthAsync(code);
        }
    }

    private async Task CompleteAuthAsync(string? code)
    {
        try
        {
            Dispatcher.Invoke(ApplyLocalization);

            var success = await _auth.ExchangeCode(code);
            Dispatcher.Invoke(() =>
            {
                if (success)
                {
                    Close();
                    return;
                }

                _isProcessing = false;
                ApplyLocalization();
                AuthWebView.CoreWebView2?.Reload();
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                _isProcessing = false;
                ApplyLocalization();
                MessageBox.Show(
                    LocalizationService.F("auth.error_status", ex.Message),
                    LocalizationService.T("common.error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            });
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_isProcessing)
            Close();
    }
}
