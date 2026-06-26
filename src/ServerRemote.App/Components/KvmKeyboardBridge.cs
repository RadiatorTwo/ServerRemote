using ServerRemote.App.ViewModels;

namespace ServerRemote.App.Components;

/// <summary>
/// Connects an (invisible) <see cref="Entry"/> to the <see cref="NanoKvmViewModel"/> in order to type
/// to the host via the soft keyboard while in fullscreen mode. The entry always holds a single
/// anchor character (zero-width space): when the user types, the new character appears after the anchor
/// (it is sent, then reset back to the anchor); if the user deletes the anchor, it was a backspace.
/// This way even backspace and Enter can be detected, which a simple "TextChanged" delta logic would
/// otherwise swallow.
/// </summary>
public sealed class KvmKeyboardBridge
{
    private const string Anchor = "​"; // zero-width space

    private readonly Entry _entry;
    private readonly NanoKvmViewModel _vm;
    private bool _muted;

    public KvmKeyboardBridge(Entry entry, NanoKvmViewModel vm)
    {
        _entry = entry;
        _vm = vm;
        ResetAnchor();
        _entry.TextChanged += OnTextChanged;
        _entry.Completed += OnCompleted;
    }

    /// <summary>Show the soft keyboard (set focus).</summary>
    public void Show() => _entry.Focus();

    /// <summary>Hide the soft keyboard.</summary>
    public void Hide() => _entry.Unfocus();

    /// <summary>Is the keyboard (focus) currently active?</summary>
    public bool IsActive => _entry.IsFocused;

    public void Detach()
    {
        _entry.TextChanged -= OnTextChanged;
        _entry.Completed -= OnCompleted;
    }

    private async void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_muted)
            return;

        var text = e.NewTextValue ?? "";
        if (text == Anchor)
            return;

        if (text.StartsWith(Anchor) && text.Length > Anchor.Length)
        {
            // New characters after the anchor → send.
            var added = text[Anchor.Length..];
            ResetAnchor();
            foreach (var c in added)
            {
                if (c is '\n' or '\r')
                    await _vm.SendEnterAsync();
                else
                    await _vm.SendCharAsync(c);
            }
        }
        else
        {
            // Anchor was (partly) deleted → backspace.
            ResetAnchor();
            await _vm.SendBackspaceAsync();
        }
    }

    private async void OnCompleted(object? sender, EventArgs e)
    {
        ResetAnchor();
        await _vm.SendEnterAsync();
    }

    private void ResetAnchor()
    {
        _muted = true;
        _entry.Text = Anchor;
        _entry.CursorPosition = Anchor.Length;
        _muted = false;
    }
}
