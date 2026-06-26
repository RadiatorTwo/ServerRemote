using System.Text.Json.Serialization;

namespace ServerRemote.App.Models;

/// <summary>
/// Standard response envelope of the NanoKVM API: <c>{ "code": 0, "msg": "success", "data": { … } }</c>.
/// </summary>
public sealed class NanoKvmResponse<T>
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("msg")] public string? Msg { get; set; }
    [JsonPropertyName("data")] public T? Data { get; set; }

    public bool IsSuccess => Code == 0;
}

/// <summary>Login body. <see cref="Password"/> is Base64(IV + AES-256-CBC(plaintext)).</summary>
public sealed class NanoKvmLoginRequest
{
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("password")] public string Password { get; set; } = "";

    public NanoKvmLoginRequest() { }

    public NanoKvmLoginRequest(string username, string password)
    {
        Username = username;
        Password = password;
    }
}

/// <summary>Login response data: the token is set as a cookie by the client itself.</summary>
public sealed class NanoKvmLoginData
{
    [JsonPropertyName("token")] public string? Token { get; set; }
}

/// <summary>GPIO action: <c>type</c> = "power" | "reset", <c>duration</c> in milliseconds.</summary>
public sealed class NanoKvmGpioRequest
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("duration")] public int Duration { get; set; }

    public NanoKvmGpioRequest() { }

    public NanoKvmGpioRequest(string type, int duration)
    {
        Type = type;
        Duration = duration;
    }
}

/// <summary>State of the two front LEDs.</summary>
public sealed class NanoKvmLedState
{
    [JsonPropertyName("pwr")] public bool Pwr { get; set; }
    [JsonPropertyName("hdd")] public bool Hdd { get; set; }
}

/// <summary>Device information from <c>/api/vm/info</c>.</summary>
public sealed class NanoKvmInfo
{
    [JsonPropertyName("ip")] public string? Ip { get; set; }
    [JsonPropertyName("mdns")] public string? Mdns { get; set; }
    [JsonPropertyName("image")] public string? Image { get; set; }
    [JsonPropertyName("application")] public string? Application { get; set; }
    [JsonPropertyName("device_key")] public string? DeviceKey { get; set; }
}

/// <summary>Hardware information from <c>/api/vm/hardware</c> (fields depend on firmware).</summary>
public sealed class NanoKvmHardware
{
    [JsonPropertyName("version")] public string? Version { get; set; }
}

/// <summary>App version from <c>/api/application/version</c>.</summary>
public sealed class NanoKvmVersion
{
    [JsonPropertyName("current")] public string? Current { get; set; }
    [JsonPropertyName("latest")] public string? Latest { get; set; }
}

/// <summary>HDMI input state from <c>/api/vm/hdmi</c> (PCIe variant only).</summary>
public sealed class NanoKvmHdmiState
{
    [JsonPropertyName("state")] public int State { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }

    public bool Connected => State == 1;
    public string StateText => Connected ? "Connected" : "No Signal";
    public string Resolution => Connected ? $"{Width} × {Height}" : "—";
}

/// <summary>Response from <c>/api/hid/shortcuts</c>: <c>data.shortcuts</c>.</summary>
public sealed class NanoKvmShortcutsData
{
    [JsonPropertyName("shortcuts")] public List<NanoKvmShortcut> Shortcuts { get; set; } = new();
}

/// <summary>A single key of a saved shortcut (JS KeyboardEvent code + display text).</summary>
public sealed class NanoKvmShortcutKey
{
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
}

/// <summary>Saved keyboard shortcut from <c>/api/hid/shortcuts</c>.</summary>
public sealed class NanoKvmShortcut
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("keys")] public List<NanoKvmShortcutKey> Keys { get; set; } = new();

    /// <summary>Display text, e.g. "Ctrl + Alt + Del".</summary>
    public string Display => Keys.Count == 0 ? "(empty)" : string.Join(" + ", Keys.Select(k => k.Label));
}

/// <summary>Paste body: text plus optional keyboard layout (<c>""</c> = US, <c>"de"</c>, <c>"fr"</c>).</summary>
public sealed class NanoKvmPasteRequest
{
    [JsonPropertyName("content")] public string Content { get; set; } = "";
    [JsonPropertyName("langue")] public string Langue { get; set; } = "";

    public NanoKvmPasteRequest() { }

    public NanoKvmPasteRequest(string content, string langue)
    {
        Content = content;
        Langue = langue;
    }
}
