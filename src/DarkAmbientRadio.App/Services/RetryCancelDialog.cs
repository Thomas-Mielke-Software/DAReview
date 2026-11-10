using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DarkAmbientRadio.App.Services;

/// <summary>
/// A message box with <em>Wiederholen</em> and <em>Abbrechen</em>. WPF cannot produce that pair:
/// <see cref="MessageBoxButton"/> only knows OK, OKCancel, YesNo and YesNoCancel — no
/// RetryCancel, unlike WinForms — so the Win32 box is called directly. Windows supplies and
/// localises the buttons itself, so the result looks exactly like the app's other dialogs.
/// </summary>
internal static class RetryCancelDialog
{
    private const uint MbRetryCancel = 0x00000005;
    private const uint MbIconWarning = 0x00000030;
    private const uint MbTaskModal = 0x00002000;
    private const int IdRetry = 4;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    /// <summary>True when the user chose "Wiederholen".</summary>
    internal static bool Show(string text, string caption)
    {
        var owner = OwnerHandle();

        // An owner is what keeps the box in front of the main window (see MainViewModel.Show).
        // Before the window exists — this can fire from the MainViewModel constructor, i.e.
        // during startup — there is none, and the box must at least be task-modal, or it can end
        // up behind the splash-less app while blocking it.
        var type = MbRetryCancel | MbIconWarning | (owner == IntPtr.Zero ? MbTaskModal : 0);

        return MessageBoxW(owner, text, caption, type) == IdRetry;
    }

    private static IntPtr OwnerHandle()
    {
        var window = Application.Current?.MainWindow;
        return window is null ? IntPtr.Zero : new WindowInteropHelper(window).Handle;
    }
}
