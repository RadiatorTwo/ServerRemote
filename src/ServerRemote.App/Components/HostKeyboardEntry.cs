namespace ServerRemote.App.Components;

/// <summary>
/// Invisible/compact <see cref="Entry"/> for live keyboard input to the NanoKVM host.
/// On Android, the soft keyboard's fullscreen "extract" mode is disabled on the native field
/// (see the handler mapping in <c>MauiProgram</c>), so that in landscape no large input field
/// covers the live image.
/// </summary>
public sealed class HostKeyboardEntry : Entry
{
}
