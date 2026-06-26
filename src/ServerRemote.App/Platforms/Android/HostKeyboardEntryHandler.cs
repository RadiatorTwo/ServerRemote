using Android.Content;
using Android.Views;
using Android.Views.InputMethods;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using ServerRemote.App.Components;

namespace ServerRemote.App.Platforms.Android;

/// <summary>
/// Android handler for <see cref="HostKeyboardEntry"/>. Forces the IME flags
/// <c>NoExtractUi</c>/<c>NoFullscreen</c> on the native input field via <c>OnCreateInputConnection</c> —
/// this is the reliable way, because the flags are written exactly into the <see cref="EditorInfo"/>
/// that the keyboard actually reads (merely setting <c>ImeOptions</c> would otherwise be overwritten).
/// In landscape it prevents the fullscreen "Extract" text field (with its "Done" button), which would
/// otherwise cover the entire live image.
/// </summary>
public sealed class HostKeyboardEntryHandler : EntryHandler
{
    protected override MauiAppCompatEditText CreatePlatformView() => new NoExtractEditText(Context);

    private sealed class NoExtractEditText : MauiAppCompatEditText
    {
        public NoExtractEditText(Context context) : base(context) { }

        public override IInputConnection? OnCreateInputConnection(EditorInfo? outAttrs)
        {
            var connection = base.OnCreateInputConnection(outAttrs);
            if (outAttrs is not null)
            {
                // EditorInfo.ImeOptions is of type ImeFlags (unlike EditText.ImeOptions = ImeAction).
                outAttrs.ImeOptions |= ImeFlags.NoExtractUi | ImeFlags.NoFullscreen;
            }
            return connection;
        }
    }
}
