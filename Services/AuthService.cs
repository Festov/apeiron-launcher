using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace Apeiron.Services;

public class AuthService
{
    private static readonly HttpClient Http = AppHttp.Client;
    private const string ClientId = "00000000402b5328";
    private string? _accessToken;
    private string? _refreshToken;
    private string? _username;
    private string? _uuid;
    private readonly string _authFile;
    
    public event Action<string>? OnCodeReceived;
    public event Action<string, string>? OnAuthSuccess;
    public event Action<string>? OnAuthError;
    
    public AuthService()
    {
        var launcherDir = AppDomain.CurrentDomain.BaseDirectory;
        var configDir = Path.Combine(launcherDir, "config");
        Directory.CreateDirectory(configDir);
        _authFile = Path.Combine(configDir, "auth.json");
    }
    
    public bool HasSavedAuth()
    {
        return File.Exists(_authFile);
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
            
            _accessToken = data["access_token"]?.ToString();
            _refreshToken = data["refresh_token"]?.ToString();
            _username = data["username"]?.ToString();
            _uuid = data["uuid"]?.ToString();

            var loaded = !string.IsNullOrEmpty(_accessToken) && !string.IsNullOrEmpty(_username);
            if (loaded && wasPlainText)
                SaveAuthData();

            return loaded;
        }
        catch (Exception ex)
        {
            Console.WriteLine(LocalizationService.F("auth.token_load_error", ex.Message));
            return false;
        }
    }
    
    private void SaveAuthData()
    {
        try
        {
            var data = new JObject
            {
                ["access_token"] = _accessToken,
                ["refresh_token"] = _refreshToken,
                ["username"] = _username,
                ["uuid"] = _uuid,
                ["saved_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
            };
            
            SecureStorage.WriteText(_authFile, data.ToString(Formatting.Indented));
        }
        catch (Exception ex)
        {
            Console.WriteLine(LocalizationService.F("auth.token_save_error", ex.Message));
        }
    }
    
    public async Task<bool> StartDeviceCodeFlow()
    {
        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["scope"] = "XboxLive.signin offline_access",
                ["response_type"] = "device_code"
            });
            
            content.Headers.Clear();
            content.Headers.Add("Content-Type", "application/x-www-form-urlencoded");
            
            var deviceResponse = await Http.PostAsync(
                "https://login.live.com/oauth20_authorize.srf",
                content
            );
            
            var responseText = await deviceResponse.Content.ReadAsStringAsync();
            
            if (responseText.TrimStart().StartsWith("<"))
            {
                OnAuthError?.Invoke(LocalizationService.T("auth.server_html"));
                return false;
            }
            
            if (!deviceResponse.IsSuccessStatusCode)
            {
                OnAuthError?.Invoke(LocalizationService.F("auth.error_status", deviceResponse.StatusCode));
                return false;
            }
            
            var deviceData = JObject.Parse(responseText);
            
            var deviceCode = deviceData["device_code"]?.ToString();
            var userCode = deviceData["user_code"]?.ToString();
            var verificationUri = deviceData["verification_uri"]?.ToString();
            var interval = deviceData["interval"]?.Value<int>() ?? 5;
            var expiresIn = deviceData["expires_in"]?.Value<int>() ?? 900;
            
            if (string.IsNullOrEmpty(deviceCode) || string.IsNullOrEmpty(userCode))
            {
                OnAuthError?.Invoke(LocalizationService.T("auth.no_auth_code"));
                return false;
            }
            
            OnCodeReceived?.Invoke($"{userCode}|{verificationUri}");
            
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = verificationUri,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch { }
            
            var startTime = DateTime.UtcNow;
            
            while ((DateTime.UtcNow - startTime).TotalSeconds < expiresIn)
            {
                await Task.Delay(interval * 1000);
                
                var tokenContent = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId,
                    ["device_code"] = deviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                });
                
                tokenContent.Headers.Clear();
                tokenContent.Headers.Add("Content-Type", "application/x-www-form-urlencoded");
                
                var tokenResponse = await Http.PostAsync(
                    "https://login.live.com/oauth20_token.srf",
                    tokenContent
                );
                
                var tokenResponseText = await tokenResponse.Content.ReadAsStringAsync();
                
                if (tokenResponse.IsSuccessStatusCode)
                {
                    var tokenData = JObject.Parse(tokenResponseText);
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
                else
                {
                    try
                    {
                        var errorData = JObject.Parse(tokenResponseText);
                        var error = errorData["error"]?.ToString();
                        
                        if (error == "authorization_pending") continue;
                        if (error == "authorization_declined")
                        {
                            OnAuthError?.Invoke(LocalizationService.T("auth.cancelled"));
                            return false;
                        }
                        if (error == "expired_token")
                        {
                            OnAuthError?.Invoke(LocalizationService.T("auth.timeout"));
                            return false;
                        }
                        
                        OnAuthError?.Invoke(LocalizationService.F("auth.error_description", errorData["error_description"]?.ToString() ?? error ?? ""));
                        return false;
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
            
            OnAuthError?.Invoke(LocalizationService.T("auth.timeout"));
            return false;
        }
        catch (Exception ex)
        {
            OnAuthError?.Invoke(LocalizationService.F("auth.error_status", ex.Message));
            return false;
        }
    }
    
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
            return false;

        var liveToken = await RefreshMicrosoftTokenAsync();
        if (string.IsNullOrEmpty(liveToken))
            return false;

        return await ExchangeTokens(liveToken);
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
            
            SaveAuthData();
            
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
        _accessToken = null;
        _refreshToken = null;
        _username = null;
        _uuid = null;
        
        if (File.Exists(_authFile))
        {
            File.Delete(_authFile);
        }
    }
    
    public string? GetAccessToken() => _accessToken;
    public string? GetUsername() => _username;
    public string? GetUUID() => _uuid;
    public bool IsAuthenticated() => !string.IsNullOrEmpty(_accessToken) && !string.IsNullOrEmpty(_username);
}