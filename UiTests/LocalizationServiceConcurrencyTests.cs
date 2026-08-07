using CoderCommander.Services;

namespace CoderCommander.UiTests;

/// <summary>
/// Direct (no UI) stress test for the data race fixed in <see cref="LocalizationService"/>:
/// GetString read _strings (a plain Dictionary) with no synchronization while LoadLanguage
/// cleared and repopulated it. This is reachable in production because
/// Operations/FileOperation.cs uses ConfigureAwait(false) throughout, so a running copy/move's
/// progress callbacks (which reach GetString via MainViewModel.OnOperationChanged) run on a
/// thread-pool thread - switching the UI language mid-operation raced a read against
/// Clear()+repopulate on the same unsynchronized Dictionary, which can throw
/// "Collection was modified" or worse. Hammering both concurrently is the only way to make an
/// unsynchronized Dictionary race actually surface within a short, deterministic test run.
/// </summary>
public class LocalizationServiceConcurrencyTests
{
    [TearDown]
    public void RestoreEnglish()
    {
        // LocalizationService.Current is a process-wide singleton shared with any other test
        // that runs in the same session - leave it the way every other test expects to find it.
        LocalizationService.Current.LoadLanguage("en");
    }

    [Test]
    public void GetString_ConcurrentWithLoadLanguage_DoesNotThrow()
    {
        var service = LocalizationService.Current;
        using var cts = new CancellationTokenSource();

        // Wide enough reader fan-out and a long enough wall-clock budget to reliably land a
        // read inside the Clear()-to-repopulate window on an unsynchronized Dictionary - a
        // handful of threads over a few hundred fast LoadLanguage calls was not enough to
        // reproduce the original race deterministically (verified: 5/5 clean runs against the
        // pre-fix code with that budget), so this uses 16 readers over a full second.
        var readerCount = Math.Max(16, Environment.ProcessorCount * 4);
        var readers = Enumerable.Range(0, readerCount).Select(_ => Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
                service.GetString("Common.Error");
        })).ToArray();

        var writer = Task.Run(() =>
        {
            var i = 0;
            while (!cts.IsCancellationRequested)
            {
                service.LoadLanguage(i % 2 == 0 ? "en" : "ru");
                i++;
            }
        });

        Thread.Sleep(1000);
        cts.Cancel();

        // Task.WaitAll rethrows (wrapped in AggregateException) if any task's GetString/
        // LoadLanguage call threw - which is exactly the race this test exists to catch.
        Task.WaitAll(readers.Append(writer).ToArray());
    }
}
