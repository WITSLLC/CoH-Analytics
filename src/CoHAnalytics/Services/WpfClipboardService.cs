using System.Runtime.InteropServices;
using System.Windows;

namespace CoHAnalytics.Services;

/// <summary>WPF clipboard writer with brief retries for transient CLIPBRD_E_CANT_OPEN contention.</summary>
public sealed class WpfClipboardService : IClipboardService
{
    private const int ClipboardOpenError = unchecked((int)0x800401D0);
    private const int MaxAttempts = 5;

    public bool TrySetText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (COMException ex) when (ex.HResult == ClipboardOpenError)
            {
                if (attempt >= MaxAttempts - 1)
                {
                    return false;
                }

                Thread.Sleep(20 * (attempt + 1));
            }
        }

        return false;
    }
}
