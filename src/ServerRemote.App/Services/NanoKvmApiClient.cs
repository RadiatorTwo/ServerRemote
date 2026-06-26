using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ServerRemote.App.Models;

namespace ServerRemote.App.Services;

/// <summary>
/// Typed HTTP client for a NanoKVM device (a standalone IP-KVM, directly on the LAN).
/// Authenticates via <c>POST /api/auth/login</c> (AES-encrypted password) and keeps the
/// session alive through the <c>nano-kvm-token</c> cookie in a <see cref="CookieContainer"/>,
/// which is also available to the WebSocket layer. Re-logs in on a 401.
/// </summary>
public sealed class NanoKvmApiClient
{
    // Fixed AES key of the NanoKVM web UI; zero-padded to 32 bytes (AES-256).
    private const string PasswordKey = "nanokvm-sipeed-2024";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ISettingsService _settings;
    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _http;

    /// <summary>Shared cookie store — reused by the WebSocket layer (phase 5).</summary>
    public CookieContainer Cookies => _cookies;

    public bool IsAuthenticated { get; private set; }

    public NanoKvmApiClient(ISettingsService settings)
    {
        _settings = settings;

        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
    }

    /// <summary>Base URL of the configured device (<c>http://{host}</c>).</summary>
    public string BaseUrl => _settings.Current.NanoKvmBaseUrl;

    private Uri Url(string path) => new($"{BaseUrl}{path}");

    /// <summary>
    /// Encrypts the password exactly like the web UI (<c>CryptoJS.AES.encrypt(data, passphrase)</c>):
    /// OpenSSL-compatible format <c>Base64("Salted__" + salt(8) + AES-256-CBC(data))</c>, where
    /// key+IV are derived from passphrase + salt via EVP_BytesToKey (MD5, 1 iteration).
    /// Then URL-encoded (equivalent to <c>encodeURIComponent</c> in the frontend).
    /// </summary>
    private static string EncryptPassword(string plain)
    {
        var passphrase = Encoding.UTF8.GetBytes(PasswordKey);
        var salt = RandomNumberGenerator.GetBytes(8);
        DeriveKeyAndIv(passphrase, salt, out var key, out var iv);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plain);
        var cipher = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var prefix = Encoding.ASCII.GetBytes("Salted__");
        var buffer = new byte[prefix.Length + salt.Length + cipher.Length];
        Buffer.BlockCopy(prefix, 0, buffer, 0, prefix.Length);
        Buffer.BlockCopy(salt, 0, buffer, prefix.Length, salt.Length);
        Buffer.BlockCopy(cipher, 0, buffer, prefix.Length + salt.Length, cipher.Length);

        return Uri.EscapeDataString(Convert.ToBase64String(buffer));
    }

    // OpenSSL EVP_BytesToKey: D_i = MD5(D_{i-1} || passphrase || salt), until key+IV are filled.
    private static void DeriveKeyAndIv(byte[] passphrase, byte[] salt, out byte[] key, out byte[] iv)
    {
        const int keyLen = 32, ivLen = 16;
        var derived = new List<byte>(keyLen + ivLen);
        var block = Array.Empty<byte>();

        while (derived.Count < keyLen + ivLen)
        {
            var input = new byte[block.Length + passphrase.Length + salt.Length];
            Buffer.BlockCopy(block, 0, input, 0, block.Length);
            Buffer.BlockCopy(passphrase, 0, input, block.Length, passphrase.Length);
            Buffer.BlockCopy(salt, 0, input, block.Length + passphrase.Length, salt.Length);
            block = MD5.HashData(input);
            derived.AddRange(block);
        }

        key = derived.GetRange(0, keyLen).ToArray();
        iv = derived.GetRange(keyLen, ivLen).ToArray();
    }

    /// <summary>
    /// Logs in to the NanoKVM. The server does NOT set a cookie — the token comes in
    /// <c>data.token</c> and is stored here ourselves as the <c>nano-kvm-token</c> cookie,
    /// so that subsequent requests (and the WebSocket) send it along.
    /// </summary>
    public async Task<bool> LoginAsync(CancellationToken ct = default)
    {
        var s = _settings.Current;
        var body = new NanoKvmLoginRequest(s.NanoKvmUsername, EncryptPassword(s.NanoKvmPassword));

        using var resp = await _http.PostAsJsonAsync(Url("/api/auth/login"), body, JsonOptions, ct);
        if (!resp.IsSuccessStatusCode)
        {
            IsAuthenticated = false;
            return false;
        }

        var env = await resp.Content.ReadFromJsonAsync<NanoKvmResponse<NanoKvmLoginData>>(JsonOptions, ct);
        var token = env?.Data?.Token;
        if (env is null || !env.IsSuccess || string.IsNullOrEmpty(token))
        {
            IsAuthenticated = false;
            return false;
        }

        // Set the token as a cookie (the frontend does the same via js-cookie).
        _cookies.Add(new Uri(BaseUrl), new Cookie("nano-kvm-token", token));
        IsAuthenticated = true;
        return true;
    }

    private async Task EnsureAuthAsync(CancellationToken ct)
    {
        if (!IsAuthenticated)
            await LoginAsync(ct);
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        await EnsureAuthAsync(ct);
        using var resp = await _http.GetAsync(Url(path), ct);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            IsAuthenticated = false;
            if (await LoginAsync(ct))
                return await GetAsync<T>(path, ct);
        }
        resp.EnsureSuccessStatusCode();
        var env = await resp.Content.ReadFromJsonAsync<NanoKvmResponse<T>>(JsonOptions, ct);
        return env is null ? default : env.Data;
    }

    private async Task PostAsync(string path, object? body, CancellationToken ct)
    {
        await EnsureAuthAsync(ct);
        using var resp = await SendPostAsync(path, body, ct);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            IsAuthenticated = false;
            if (await LoginAsync(ct))
            {
                using var retry = await SendPostAsync(path, body, ct);
                retry.EnsureSuccessStatusCode();
                return;
            }
        }
        resp.EnsureSuccessStatusCode();
    }

    private Task<HttpResponseMessage> SendPostAsync(string path, object? body, CancellationToken ct)
    {
        var content = body is null
            ? null
            : JsonContent.Create(body, body.GetType(), options: JsonOptions);
        return _http.PostAsync(Url(path), content, ct);
    }

    // ----- GPIO / Power (phase 1) -----
    // LED state comes from GET /api/vm/gpio (there is NO /led sub-path).
    public Task<NanoKvmLedState?> GetLedAsync(CancellationToken ct = default)
        => GetAsync<NanoKvmLedState>("/api/vm/gpio", ct);

    public Task PowerAsync(int durationMs, CancellationToken ct = default)
        => PostAsync("/api/vm/gpio", new NanoKvmGpioRequest("power", durationMs), ct);

    public Task ResetAsync(CancellationToken ct = default)
        => PostAsync("/api/vm/gpio", new NanoKvmGpioRequest("reset", 800), ct);

    public Task RebootDeviceAsync(CancellationToken ct = default)
        => PostAsync("/api/vm/system/reboot", null, ct);

    // ----- Device info / HDMI (phase 2) -----
    public Task<NanoKvmInfo?> GetInfoAsync(CancellationToken ct = default)
        => GetAsync<NanoKvmInfo>("/api/vm/info", ct);

    public Task<NanoKvmHardware?> GetHardwareAsync(CancellationToken ct = default)
        => GetAsync<NanoKvmHardware>("/api/vm/hardware", ct);

    public Task<NanoKvmVersion?> GetVersionAsync(CancellationToken ct = default)
        => GetAsync<NanoKvmVersion>("/api/application/version", ct);

    public Task<NanoKvmHdmiState?> GetHdmiAsync(CancellationToken ct = default)
        => GetAsync<NanoKvmHdmiState>("/api/vm/hdmi", ct);

    public Task HdmiResetAsync(CancellationToken ct = default)
        => PostAsync("/api/vm/hdmi/reset", null, ct);

    public Task HdmiEnableAsync(CancellationToken ct = default)
        => PostAsync("/api/vm/hdmi/enable", null, ct);

    public Task HdmiDisableAsync(CancellationToken ct = default)
        => PostAsync("/api/vm/hdmi/disable", null, ct);

    // ----- Paste / Shortcuts (phase 4) -----
    public Task PasteAsync(string content, string langue, CancellationToken ct = default)
        => PostAsync("/api/hid/paste", new NanoKvmPasteRequest(content, langue), ct);

    // Returns the STORED shortcuts ({shortcuts:[…]}). Executing them runs over the
    // WebSocket HID channel, not through this endpoint (which only serves to create/delete them).
    public Task<NanoKvmShortcutsData?> GetShortcutsAsync(CancellationToken ct = default)
        => GetAsync<NanoKvmShortcutsData>("/api/hid/shortcuts", ct);

    // ----- MJPEG (phase 3): open the raw stream with ResponseHeadersRead -----
    public Task<HttpResponseMessage> OpenMjpegAsync(CancellationToken ct)
        => _http.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, Url("/api/stream/mjpeg")),
            HttpCompletionOption.ResponseHeadersRead, ct);
}
