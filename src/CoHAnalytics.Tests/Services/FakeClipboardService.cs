using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

internal sealed class FakeClipboardService : IClipboardService
{
    private readonly Queue<bool> _results = new();

    public int CallCount { get; private set; }

    public IReadOnlyList<string> Texts { get; private set; } = [];

    public string? LastText => Texts.Count == 0 ? null : Texts[^1];

    public void EnqueueResult(bool success) => _results.Enqueue(success);

    public bool TrySetText(string? text)
    {
        CallCount++;
        Texts = [.. Texts, text ?? string.Empty];
        if (_results.Count > 0)
        {
            return _results.Dequeue();
        }

        return !string.IsNullOrWhiteSpace(text);
    }
}
