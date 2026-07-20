using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

namespace CoderCommander.Services;

/// <summary>
/// Сервис OAuth2-аутентификации Google Drive.
/// Google Drive OAuth2 authentication service.
/// Запускает локальный HTTP-сервер для перехвата callback, открывает браузер, обменивает код на Refresh Token.
/// Starts a local HTTP server to capture the callback, opens the browser, exchanges the code for a Refresh Token.
/// </summary>
public static class GoogleOAuthService
{
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const int DefaultPort = 8080;

    /// <summary>
    /// Запускает HTTP-сервер, открывает браузер для авторизации, возвращает код авторизации.
    /// Starts an HTTP server, opens the browser for authorization, returns the authorization code.
    /// </summary>
    public static async Task<string> GetAuthorizationCodeAsync(
        string clientId, int port = DefaultPort, CancellationToken ct = default)
    {
        var redirectUri = $"http://localhost:{port}/";
        var scope = Uri.EscapeDataString(DriveService.Scope.Drive);

        var authUrl = $"{AuthEndpoint}?client_id={Uri.EscapeDataString(clientId)}" +
                      $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                      $"&scope={scope}" +
                      "&response_type=code" +
                      "&access_type=offline" +
                      "&prompt=consent";

        var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        try
        {
            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

            var getContextTask = listener.GetContextAsync();
            using var registration = ct.Register(() => listener.Stop());

            HttpListenerContext context;
            try
            {
                context = await getContextTask;
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException("OAuth authorization cancelled.", ct);
            }

            var code = context.Request.QueryString["code"];
            var error = context.Request.QueryString["error"];

            if (!string.IsNullOrEmpty(error))
                throw new InvalidOperationException($"Google authorization error: {error}");

            var responseHtml = "<html><body style='font-family:Segoe UI;display:flex;justify-content:center;align-items:center;height:100vh;background:#1a1a2e;color:#e0e0e0;'>" +
                               "<div style='text-align:center;padding:40px;background:#16213e;border-radius:12px;border:1px solid #0f3460;'>" +
                               "<h2 style='color:#7a8bfa;'>&#10003; Authorization successful</h2>" +
                               "<p>You can close this window and return to Coder Commander.</p>" +
                               "</div></body></html>";

            var buffer = Encoding.UTF8.GetBytes(responseHtml);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length, ct);
            context.Response.Close();

            if (string.IsNullOrEmpty(code))
                throw new InvalidOperationException("Authorization code not received from Google.");

            return code;
        }
        finally
        {
            try { listener.Stop(); } catch { }
            try { listener.Close(); } catch { }
        }
    }

    /// <summary>
    /// Обменивает код авторизации на Refresh Token через Google OAuth2.
    /// Exchanges authorization code for a Refresh Token via Google OAuth2.
    /// </summary>
    public static async Task<string> ExchangeCodeForRefreshTokenAsync(
        string code, string clientId, string clientSecret, CancellationToken ct = default)
    {
        using var httpClient = new HttpClient();
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = $"http://localhost:{DefaultPort}/",
            ["grant_type"] = "authorization_code",
        });

        var response = await httpClient.PostAsync(TokenEndpoint, content, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token exchange failed ({response.StatusCode}): {json}");

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("refresh_token", out var rt))
            return rt.GetString() ?? throw new InvalidOperationException("Refresh token is empty.");

        throw new InvalidOperationException($"Refresh token not found in response: {json}");
    }

    /// <summary>
    /// Создаёт <see cref="UserCredential"/> из Refresh Token для использования с Google API.
    /// Creates a <see cref="UserCredential"/> from a Refresh Token for use with Google APIs.
    /// </summary>
    public static UserCredential CreateUserCredential(
        string clientId, string clientSecret, string refreshToken)
    {
        var flow = new Google.Apis.Auth.OAuth2.Flows.GoogleAuthorizationCodeFlow(
            new Google.Apis.Auth.OAuth2.Flows.GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new Google.Apis.Auth.OAuth2.ClientSecrets
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret,
                },
                Scopes = new[] { DriveService.Scope.Drive },
            });

        var token = new Google.Apis.Auth.OAuth2.Responses.TokenResponse
        {
            RefreshToken = refreshToken,
        };

        return new UserCredential(flow, "user", token);
    }

    /// <summary>
    /// Создаёт настроенный <see cref="DriveService"/> с OAuth2-аутентификацией.
    /// Creates a configured <see cref="DriveService"/> with OAuth2 authentication.
    /// </summary>
    public static DriveService CreateDriveService(
        string clientId, string clientSecret, string refreshToken)
    {
        var credential = CreateUserCredential(clientId, clientSecret, refreshToken);
        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "CoderCommander",
        });
    }
}
