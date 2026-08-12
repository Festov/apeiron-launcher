using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using Apeiron.Services;

namespace Apeiron;

public partial class MainWindow
{
    private void SetProfileState(bool isLoggedIn, string? username = null)
    {
        if (_settings.OfflineOnly)
        {
            ApplyOfflineOnlyMode();
            return;
        }

        if (isLoggedIn && !string.IsNullOrWhiteSpace(username))
        {
            UserInfoText.Text = username;
            UserStatusText.Text = LocalizationService.T("main.microsoft_account");
            AuthButton.Visibility = Visibility.Visible;
            AuthButtonText.Text = LocalizationService.T("main.add_account");
            LogoutButton.Visibility = Visibility.Visible;
            OfflineNameRow.Visibility = Visibility.Collapsed;
        }
        else
        {
            var offlineName = OfflineUsernameHelper.Sanitize(_settings.OfflineUsername);
            UserInfoText.Text = offlineName;
            UserStatusText.Text = LocalizationService.T("main.login_prompt");
            AuthButton.Visibility = Visibility.Visible;
            AuthButtonText.Text = LocalizationService.T("main.login_microsoft");
            LogoutButton.Visibility = Visibility.Collapsed;
            OfflineNameRow.Visibility = Visibility.Visible;
            if (string.IsNullOrWhiteSpace(OfflineNameTextBox.Text))
                OfflineNameTextBox.Text = offlineName;
            UpdateOfflineNameFeedback();
        }

        RefreshAccountSwitcher();
    }

    private void RefreshAccountSwitcher()
    {
        if (AccountComboBox == null)
            return;

        if (_settings.OfflineOnly)
        {
            AccountComboBox.Visibility = Visibility.Collapsed;
            return;
        }

        var accounts = _auth.GetAccounts();
        _suppressAccountComboEvent = true;
        try
        {
            AccountComboBox.ItemsSource = accounts;
            AccountComboBox.DisplayMemberPath = nameof(AccountSummary.Username);
            AccountComboBox.SelectedValuePath = nameof(AccountSummary.Uuid);

            if (accounts.Count == 0)
            {
                AccountComboBox.Visibility = Visibility.Collapsed;
                AccountComboBox.SelectedItem = null;
                return;
            }

            AccountComboBox.Visibility = Visibility.Visible;
            var active = accounts.FirstOrDefault(a => a.IsActive) ?? accounts[0];
            AccountComboBox.SelectedItem = accounts.FirstOrDefault(a =>
                string.Equals(a.Uuid, active.Uuid, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _suppressAccountComboEvent = false;
        }
    }

    private void AccountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAccountComboEvent || AccountComboBox.SelectedItem is not AccountSummary selected)
            return;

        var current = _auth.GetUUID();
        if (string.Equals(
                (current ?? "").Replace("-", "", StringComparison.Ordinal),
                (selected.Uuid ?? "").Replace("-", "", StringComparison.Ordinal),
                StringComparison.OrdinalIgnoreCase))
            return;

        if (!_auth.SwitchAccount(selected.Uuid))
            return;

        AddConsoleLine(LocalizationService.F("main.account_switched", selected.Username));
        SetProfileState(true, selected.Username);
        _ = RefreshAvatarAsync();
    }

    private void ApplyOfflineOnlyMode()
    {
        if (!_settings.OfflineOnly)
            return;

        if (_auth.IsAuthenticated() || _auth.GetAccounts().Count > 0)
        {
            _auth.LogoutAll();
            _ = RefreshAvatarAsync();
        }

        var offlineName = OfflineUsernameHelper.Sanitize(_settings.OfflineUsername);
        UserInfoText.Text = offlineName;
        UserStatusText.Text = LocalizationService.T("main.offline_only_mode");
        AuthButton.Visibility = Visibility.Collapsed;
        LogoutButton.Visibility = Visibility.Collapsed;
        if (AccountComboBox != null)
            AccountComboBox.Visibility = Visibility.Collapsed;
        OfflineNameRow.Visibility = Visibility.Visible;
        if (string.IsNullOrWhiteSpace(OfflineNameTextBox.Text))
            OfflineNameTextBox.Text = offlineName;
        UpdateOfflineNameFeedback();
    }

    private void UpdateOfflineNameFeedback()
    {
        if (OfflineNameErrorText == null || OfflineNameTextBox == null)
            return;

        if (_auth.IsAuthenticated() || OfflineNameRow.Visibility != Visibility.Visible)
        {
            OfflineNameErrorText.Visibility = Visibility.Collapsed;
            return;
        }

        var message = GetOfflineUsernameErrorMessage(OfflineNameTextBox.Text);
        if (string.IsNullOrEmpty(message))
        {
            OfflineNameErrorText.Visibility = Visibility.Collapsed;
            OfflineNameErrorText.Text = "";
            return;
        }

        OfflineNameErrorText.Text = message;
        OfflineNameErrorText.Visibility = Visibility.Visible;
    }

    private static string? GetOfflineUsernameErrorMessage(string? name)
    {
        return OfflineUsernameHelper.Validate(name) switch
        {
            OfflineUsernameValidation.Valid => null,
            OfflineUsernameValidation.Empty => LocalizationService.T("main.offline_name_empty"),
            OfflineUsernameValidation.TooShort => LocalizationService.F("main.offline_name_too_short", OfflineUsernameHelper.MinLength),
            _ => LocalizationService.T("main.offline_name_invalid")
        };
    }

    private bool TryValidateOfflineUsernameForLaunch()
    {
        if (_auth.IsAuthenticated())
            return true;

        var error = GetOfflineUsernameErrorMessage(OfflineNameTextBox.Text);
        if (string.IsNullOrEmpty(error))
            return true;

        UpdateOfflineNameFeedback();
        _viewModel.SetStatus(error);
        AddConsoleLine("❌ " + error);
        OfflineNameTextBox.Focus();
        return false;
    }

    private void SaveOfflineUsername()
    {
        var name = OfflineUsernameHelper.Sanitize(OfflineNameTextBox.Text);
        OfflineNameTextBox.Text = name;
        _settings.OfflineUsername = name;
        _settings.Save();

        if (!_auth.IsAuthenticated())
            UserInfoText.Text = name;

        UpdateOfflineNameFeedback();
    }

    private void OfflineNameTextBox_TextChanged(object sender, TextChangedEventArgs e) =>
        UpdateOfflineNameFeedback();

    private void OfflineNameTextBox_LostFocus(object sender, RoutedEventArgs e) => SaveOfflineUsername();

    private void OfflineNameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            SaveOfflineUsername();
    }

    private void OfflineNameTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(ch => !IsOfflineNameCharacterAllowed(ch));
    }

    private void OfflineNameTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var pasted = e.DataObject.GetData(DataFormats.Text) as string ?? "";
        if (OfflineUsernameHelper.NormalizeInput(pasted) != pasted.Trim())
            e.CancelCommand();
    }

    private static bool IsOfflineNameCharacterAllowed(char ch) =>
        ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_';

    private void OnAuthSuccess(string username, string uuid)
    {
        Dispatcher.Invoke(() =>
        {
            _authInProgress = false;
            SetProfileState(true, username);
            _viewModel.SetStatus(LocalizationService.F("main.auth_success_status", username));
            AddConsoleLine(LocalizationService.F("main.auth_welcome", username));
            AddConsoleLine(LocalizationService.F("main.auth_uuid", uuid));
            _ = RefreshAvatarAsync();
        });
    }
    
    private void OnAuthError(string error)
    {
        Dispatcher.Invoke(() =>
        {
            _authInProgress = false;
            _viewModel.SetStatus(LocalizationService.F("main.error_with_message", error));
            AddConsoleLine(LocalizationService.F("main.error_with_message", error));
            AuthButton.IsEnabled = true;
            RefreshAccountSwitcher();
        });
    }

    private void OnSessionExpired()
    {
        Dispatcher.Invoke(() =>
        {
            SetProfileState(false);
            _ = RefreshAvatarAsync();
            _viewModel.SetStatus(LocalizationService.T("main.session_expired_status"));
            AddConsoleLine(LocalizationService.T("main.session_expired_console"));
        });
    }
    
    private void AuthButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.OfflineOnly || _authInProgress) return;
        AuthButton.IsEnabled = false;
        _authInProgress = true;
        _viewModel.SetStatus(_auth.IsAuthenticated()
            ? LocalizationService.T("main.add_account_starting")
            : LocalizationService.T("main.auth_starting_status"));
        AddConsoleLine(_auth.IsAuthenticated()
            ? LocalizationService.T("main.add_account_starting")
            : LocalizationService.T("main.auth_starting_console"));
        
        try
        {
            var authWindow = new AuthWindow(this, _auth, _minecraft.MinecraftDir);
            authWindow.Closed += (s, args) =>
            {
                _authInProgress = false;
                AuthButton.IsEnabled = true;
                AddConsoleLine(LocalizationService.T("main.auth_window_closed"));
                RefreshAccountSwitcher();
            };
            using (DialogShade.Begin(this))
                authWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            AddConsoleLine(LocalizationService.F("main.error_with_message", ex.Message));
            AuthButton.IsEnabled = true;
            _authInProgress = false;
        }
    }
    
    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        var leaving = _auth.GetUsername() ?? "";
        _auth.Logout();

        if (_auth.IsAuthenticated())
        {
            var next = _auth.GetUsername() ?? "";
            SetProfileState(true, next);
            _viewModel.SetStatus(LocalizationService.F("main.account_switched", next));
            AddConsoleLine(LocalizationService.F("main.account_signed_out_switched", leaving, next));
            _ = RefreshAvatarAsync();
        }
        else
        {
            SetProfileState(false);
            _viewModel.SetStatus(LocalizationService.T("main.logout_done"));
            AddConsoleLine(LocalizationService.T("main.logout_console"));
            _ = RefreshAvatarAsync();
        }

        _authInProgress = false;
    }
}
