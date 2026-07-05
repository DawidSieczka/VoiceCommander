using VoiceTypePL.App.Overlay;
using VoiceTypePL.Core.Speech;

namespace VoiceTypePL.App.Tests;

/// <summary>Testy kolejki dymka (§5.4): jedno zdanie pokazane, reszta czeka, licznik oczekujących.</summary>
public sealed class OverlaySentenceQueueTests
{
    private static TranscribedSentence Sentence(string text) =>
        new(text, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(500), DateTimeOffset.Now);

    [Fact]
    public void Enqueue_First_BecomesCurrent()
    {
        var queue = new OverlaySentenceQueue();

        var became = queue.Enqueue(Sentence("pierwsze"));

        Assert.True(became);
        Assert.Equal("pierwsze", queue.Current!.Text);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void Enqueue_WhileShowing_GoesToPending()
    {
        var queue = new OverlaySentenceQueue();
        queue.Enqueue(Sentence("pierwsze"));

        var became = queue.Enqueue(Sentence("drugie"));

        Assert.False(became);
        Assert.Equal("pierwsze", queue.Current!.Text);   // bieżące bez zmian
        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public void Advance_MovesToNext_AndDecrementsPending()
    {
        var queue = new OverlaySentenceQueue();
        queue.Enqueue(Sentence("pierwsze"));
        queue.Enqueue(Sentence("drugie"));
        queue.Enqueue(Sentence("trzecie"));

        var next = queue.Advance();

        Assert.Equal("drugie", next!.Text);
        Assert.Equal("drugie", queue.Current!.Text);
        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public void Advance_WhenEmpty_ClearsCurrent()
    {
        var queue = new OverlaySentenceQueue();
        queue.Enqueue(Sentence("jedyne"));

        var next = queue.Advance();

        Assert.Null(next);
        Assert.Null(queue.Current);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void Clear_ResetsEverything()
    {
        var queue = new OverlaySentenceQueue();
        queue.Enqueue(Sentence("a"));
        queue.Enqueue(Sentence("b"));

        queue.Clear();

        Assert.Null(queue.Current);
        Assert.Equal(0, queue.PendingCount);
    }
}
