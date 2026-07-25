using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Apeiron.Services;

public sealed class AccountSummary
{
    public string Username { get; init; } = "";
    public string Uuid { get; init; } = "";
    public bool IsActive { get; init; }
}

public class AuthService
{
    private static readonly HttpClient Http = AppHttp.Client;
    private const string ClientId = "00000000402b5328";

    private string? _accessToken;
    private string? _refreshToken;
    private string? _username;
    private string? _uuid;
    private readonly string _authFile;
    private readonly List<StoredAccount> _accounts = new();

    public event Action<string, string>? OnAuthSuccess;
    public event Action<string>? OnAuthError;
    public event Action? OnSessionExpired;
    public event Action? OnAccountsChanged;

    public AuthService()
    {
        var launcherDir = AppDomain.CurrentDomain.BaseDirectory;
        var configDir = Path.Combine(launcherDir, "config");
        Directory.CreateDirectory(configDir);
        _authFile = Path.Combine(configDir, "auth.json");
    }

    public bool HasSavedAuth() => File.Exists(_authFile);

    public IReadOnlyList<AccountSummary> GetAccounts()
    {
        var active = NormalizeUuid(_uuid);
        return _accounts
            .Select(a => new AccountSummary
            {
                Username = a.Username,
                Uuid = a.Uuid,
                IsActive = string.Equals(NormalizeUuid(a.Uuid), active, StringComparison.OrdinalIgnoreCase)
            })
            .OrderBy(a => a.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool LoadSavedAuth()
    {
        try
        {
            if (!File.Exists(_authFile))
                return false;

            var wasPlainText = SecureStorage.IsPlainTextFile(_authFile);
            var json = SecureStorage.ReadText(_authFile);
            if (string.IsNullOrEmpty(json))
                return false;

            var data = JObject.Parse(json);
            _accounts.Clear();
            LoadAccountsFromJson(data);

            var activeUuid = data["active_uuid"]?.ToString();
            if (!TryActivate(activeUuid) && _accounts.Count > 0)
                TryActivate(_accounts[0].Uuid);

            var loaded = IsAuthenticated();
            if (loaded && (wasPlainText || data["accounts"] == null))
                SaveAuthData();

            return loaded;
        }
        catch (Exception ex)
        {
            Console.WriteLine(LocalizationService.F("auth.token_load_error", ex.Message));
            return false;
        }
    }

    public bool SwitchAccount(string? uuid)
    {
        if (!TryActivate(uuid))
            return false;

        SaveAuthData();
        NotifyAccountsChanged();
        return true;
    }

    public void RemoveAccount(string? uuid)
    {
        var target = NormalizeUuid(uuid);
        if (string.IsNullOrEmpty(target))
            return;

        var wasActive = string.Equals(NormalizeUuid(_uuid), target, StringComparison.OrdinalIgnoreCase);
        _accounts.RemoveAll(a => string.Equals(NormalizeUuid(a.Uuid), target, StringComparison.OrdinalIgnoreCase));

        if (_accounts.Count == 0)
        {
            ClearMemory();
            DeleteAuthFile();
            NotifyAccountsChanged();
            if (wasActive)
                OnSessionExpired?.Invoke();
            return;
        }

        if (wasActive)
        {
            TryActivate(_accounts[0].Uuid);
            SaveAuthData();
            NotifyAccountsChanged();
        }
        else
        {
            SaveAuthData();
            NotifyAccountsChanged();
        }
    }

    private void SaveAuthData()
    {
        try
        {
            UpsertActiveIntoList();

            var accounts = new JArray();
            foreach (var account in _accounts)
            {
                accounts.Add(new JObject
                {
                    ["access_token"] = account.AccessToken,
                    ["refresh_token"] = account.RefreshToken,
                    ["username"] = account.Username,
                    ["uuid"] = account.Uuid,
                    ["saved_at"] = account.SavedAt
                });
            }

            var data = new JObject
            {
                ["active_uuid"] = _uuid ?? "",
                ["accounts"] = accounts
            };

            SecureStorage.WriteText(_authFile, data.ToString(Formatting.Indented));
        }
        catch (Exception ex)
        {
            Console.WriteLine(LocalizationService.F("auth.token_save_error", ex.Message));
        }
    }

    private void NotifyAccountsChanged() => OnAccountsChanged?.Invoke();

    public async Task<bool> ExchangeCode(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            OnAuthError?.Invoke(LocalizationService.T("auth.code_empty"));
            return false;
        }

        try
        {
            var tokenContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["code"] = code,
                ["redirect_uri"] = "https://login.live.com/oauth20_desktop.srf",
                ["grant_type"] = "authorization_code"
            });

            tokenContent.Headers.Clear();
            tokenContent.Headers.Add("Content-Type", "application/x-www-form-urlencoded");

            var tokenResponse = await Http.PostAsync(
                "https://login.live.com/oauth20_token.srf",
                tokenContent
            );

            if (!tokenResponse.IsSuccessStatusCode)
            {
                var errorText = await tokenResponse.Content.ReadAsStringAsync();
                OnAuthError?.Invoke(LocalizationService.F("auth.token_error", errorText));
                return false;
            }

            var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
            var tokenData = JObject.Parse(tokenJson);
            var accessToken = tokenData["access_token"]?.ToString();
            var refreshToken = tokenData["refresh_token"]?.ToString();

            if (string.IsNullOrEmpty(accessToken))
            {
                OnAuthError?.Invoke(LocalizationService.T("auth.no_token"));
                return false;
            }

            _refreshToken = refreshToken;
            return await ExchangeTokens(accessToken);
        }
        catch (Exception ex)
        {
            OnAuthError?.Invoke(LocalizationService.F("auth.error_status", ex.Message));
            return false;
        }
    }

    public async Task<bool> EnsureValidSessionAsync()
    {
        if (string.IsNullOrEmpty(_accessToken) || string.IsNullOrEmpty(_username))
            return false;

        if (await ValidateMinecraftTokenAsync())
            return true;

        if (string.IsNullOrEmpty(_refreshToken))
        {
            DropActiveAccountAfterFailure();
            return IsAuthenticated();
        }

        var liveToken = await RefreshMicrosoftTokenAsync();
        if (string.IsNullOrEmpty(liveToken))
        {
            DropActiveAccountAfterFailure();
            return IsAuthenticated();
        }

        var exchanged = await ExchangeTokens(liveToken);
        if (!exchanged)
            DropActiveAccountAfterFailure();

        return IsAuthenticated();
    }

    public void InvalidateSession()
    {
        DropActiveAccountAfterFailure();
    }

    private void DropActiveAccountAfterFailure()
    {
        var failedUuid = NormalizeUuid(_uuid);
        if (!string.IsNullOrEmpty(failedUuid))
            _accounts.RemoveAll(a => string.Equals(NormalizeUuid(a.Uuid), failedUuid, StringComparison.OrdinalIgnoreCase));

        ClearMemory();

        if (_accounts.Count == 0)
        {
            DeleteAuthFile();
            NotifyAccountsChanged();
            OnSessionExpired?.Invoke();
            return;
        }

        TryActivate(_accounts[0].Uuid);
        SaveAuthData();
        NotifyAccountsChanged();
        OnAuthSuccess?.Invoke(_username!, _uuid!);
    }

    private async Task<bool> ValidateMinecraftTokenAsync()
    {
        if (string.IsNullOrEmpty(_accessToken))
            return false;

        try
        {
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                "https://api.minecraftservices.com/minecraft/profile");
            request.Headers.Add("Authorization", $"Bearer {_accessToken}");
            var response = await Http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> RefreshMicrosoftTokenAsync()
    {
        if (string.IsNullOrEmpty(_refreshToken))
            return null;

        try
        {
            var tokenContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _refreshToken
            });

            tokenContent.Headers.Clear();
            tokenContent.Headers.Add("Content-Type", "application/x-www-form-urlencoded");

            var tokenResponse = await Http.PostAsync(
                "https://login.live.com/oauth20_token.srf",
                tokenContent);

            var tokenResponseText = await tokenResponse.Content.ReadAsStringAsync();
            if (!tokenResponse.IsSuccessStatusCode)
                return null;

            var tokenData = JObject.Parse(tokenResponseText);
            var accessToken = tokenData["access_token"]?.ToString();
            var refreshToken = tokenData["refresh_token"]?.ToString();

            if (!string.IsNullOrEmpty(refreshToken))
                _refreshToken = refreshToken;

            return accessToken;
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> ExchangeTokens(string accessToken)
    {
        try
        {
            var xblContent = new StringContent(
                $@"{{
                    ""Properties"": {{
                        ""AuthMethod"": ""RPS"",
                        ""SiteName"": ""user.auth.xboxlive.com"",
                        ""RpsTicket"": ""d={accessToken}""
                    }},
                    ""RelyingParty"": ""http://auth.xboxlive.com"",
                    ""TokenType"": ""JWT""
                }}",
                System.Text.Encoding.UTF8,
                "application/json"
            );

            var xblResponse = await Http.PostAsync(
                "https://user.auth.xboxlive.com/user/authenticate",
                xblContent
            );

            if (!xblResponse.IsSuccessStatusCode)
            {
                OnAuthError?.Invoke(LocalizationService.T("auth.xbox_error"));
                return false;
            }

            var xblJson = await xblResponse.Content.ReadAsStringAsync();
            var xblData = JObject.Parse(xblJson);
            var xblToken = xblData["Token"]?.ToString();
            var userHash = xblData["DisplayClaims"]?["xui"]?[0]?["uhs"]?.ToString();

            if (string.IsNullOrEmpty(xblToken) || string.IsNullOrEmpty(userHash))
            {
                OnAuthError?.Invoke(LocalizationService.T("auth.xbox_no_token"));
                return false;
            }

            var xstsContent = new StringContent(
                $@"{{
                    ""Properties"": {{
                        ""SandboxId"": ""RETAIL"",
                        ""UserTokens"": [""{xblToken}""]
                    }},
                    ""RelyingParty"": ""rp://api.minecraftservices.com/"",
                    ""TokenType"": ""JWT""
                }}",
                System.Text.Encoding.UTF8,
                "application/json"
            );

            var xstsResponse = await Http.PostAsync(
                "https://xsts.auth.xboxlive.com/xsts/authorize",
                xstsContent
            );

            if (!xstsResponse.IsSuccessStatusCode)
            {
                OnAuthError?.Invoke(LocalizationService.T("auth.xsts_error"));
                return false;
            }

            var xstsJson = await xstsResponse.Content.ReadAsStringAsync();
            var xstsData = JObject.Parse(xstsJson);
            var xstsToken = xstsData["Token"]?.ToString();

            var mcContent = new StringContent(
                $@"{{ ""identityToken"": ""XBL3.0 x={userHash};{xstsToken}"" }}",
                System.Text.Encoding.UTF8,
                "application/json"
            );

            var mcResponse = await Http.PostAsync(
                "https://api.minecraftservices.com/authentication/login_with_xbox",
                mcContent
            );

            if (!mcResponse.IsSuccessStatusCode)
            {
                OnAuthError?.Invoke(LocalizationService.T("auth.mc_auth_error"));
                return false;
            }

            var mcJson = await mcResponse.Content.ReadAsStringAsync();
            var mcData = JObject.Parse(mcJson);
            _accessToken = mcData["access_token"]?.ToString();

            var profileRequest = new HttpRequestMessage(
                HttpMethod.Get,
                "https://api.minecraftservices.com/minecraft/profile"
            );
            profileRequest.Headers.Add("Authorization", $"Bearer {_accessToken}");
            var profileResponse = await Http.SendAsync(profileRequest);

            if (!profileResponse.IsSuccessStatusCode)
            {
                OnAuthError?.Invoke(LocalizationService.T("auth.profile_error"));
                return false;
            }

            var profileJson = await profileResponse.Content.ReadAsStringAsync();
            var profileData = JObject.Parse(profileJson);

            _username = profileData["name"]?.ToString() ?? LocalizationService.T("auth.default_player");
            _uuid = profileData["id"]?.ToString() ?? "";

            UpsertActiveIntoList();
            SaveAuthData();
            NotifyAccountsChanged();

            OnAuthSuccess?.Invoke(_username, _uuid);
            return true;
        }
        catch (Exception ex)
        {
            OnAuthError?.Invoke(LocalizationService.F("auth.error_status", ex.Message));
            return false;
        }
    }

    public void Logout()
    {
        // Sign out of the active account only; keep other saved accounts.
        RemoveAccount(_uuid);
    }

    public void LogoutAll()
    {
        _accounts.Clear();
        ClearMemory();
        DeleteAuthFile();
        NotifyAccountsChanged();
    }

    public string? GetAccessToken() => _accessToken;
    public string? GetUsername() => _username;
    public string? GetUUID() => _uuid;
    public bool IsAuthenticated() => !string.IsNullOrEmpty(_accessToken) && !string.IsNullOrEmpty(_username);

    private void LoadAccountsFromJson(JObject data)
    {
        if (data["accounts"] is JArray array)
        {
            foreach (var item in array)
            {
                if (item is not JObject obj)
                    continue;
                var account = ReadAccount(obj);
                if (account != null)
                    _accounts.Add(account);
            }

            return;
        }

        // Legacy single-account file.
        var legacy = ReadAccount(data);
        if (legacy != null)
            _accounts.Add(legacy);
    }

    private static StoredAccount? ReadAccount(JObject data)
    {
        var access = data["access_token"]?.ToString();
        var username = data["username"]?.ToString();
        if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(username))
            return null;

        return new StoredAccount
        {
            AccessToken = access,
            RefreshToken = data["refresh_token"]?.ToString() ?? "",
            Username = username,
            Uuid = data["uuid"]?.ToString() ?? "",
            SavedAt = data["saved_at"]?.ToString() ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };
    }

    private bool TryActivate(string? uuid)
    {
        var target = NormalizeUuid(uuid);
        StoredAccount? account = null;
        if (!string.IsNullOrEmpty(target))
            account = _accounts.FirstOrDefault(a =>
                string.Equals(NormalizeUuid(a.Uuid), target, StringComparison.OrdinalIgnoreCase));

        if (account == null)
            return false;

        _accessToken = account.AccessToken;
        _refreshToken = string.IsNullOrEmpty(account.RefreshToken) ? null : account.RefreshToken;
        _username = account.Username;
        _uuid = account.Uuid;
        return !string.IsNullOrEmpty(_accessToken) && !string.IsNullOrEmpty(_username);
    }

    private void UpsertActiveIntoList()
    {
        if (string.IsNullOrEmpty(_accessToken) || string.IsNullOrEmpty(_username))
            return;

        var uuid = _uuid ?? "";
        var existing = _accounts.FirstOrDefault(a =>
            string.Equals(NormalizeUuid(a.Uuid), NormalizeUuid(uuid), StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.AccessToken = _accessToken;
            existing.RefreshToken = _refreshToken ?? "";
            existing.Username = _username;
            existing.Uuid = uuid;
            existing.SavedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        }
        else
        {
            _accounts.Add(new StoredAccount
            {
                AccessToken = _accessToken,
                RefreshToken = _refreshToken ?? "",
                Username = _username,
                Uuid = uuid,
                SavedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
            });
        }
    }

    private void ClearMemory()
    {
        _accessToken = null;
        _refreshToken = null;
        _username = null;
        _uuid = null;
    }

    private void DeleteAuthFile()
    {
        try
        {
            if (File.Exists(_authFile))
                File.Delete(_authFile);
        }
        catch (Exception ex)
        {
            Console.WriteLine(LocalizationService.F("auth.token_save_error", ex.Message));
        }
    }

    private static string NormalizeUuid(string? uuid) =>
        (uuid ?? "").Replace("-", "", StringComparison.Ordinal).Trim().ToLowerInvariant();

    private sealed class StoredAccount
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string Username { get; set; } = "";
        public string Uuid { get; set; } = "";
        public string SavedAt { get; set; } = "";
    }
}
