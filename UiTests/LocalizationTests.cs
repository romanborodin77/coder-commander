using CoderCommander.Services;

namespace CoderCommander.UiTests;

/// <summary>
/// Direct tests against LocalizationService for the Operation Queue's "Type" column - it used to
/// show the literal key (e.g. "OpQueue.Type.Copy") in both languages because lang/english.lng was
/// never loaded and lang/russian.lng was missing these specific keys.
/// </summary>
public class LocalizationTests
{
    [TearDown]
    public void RestoreDefaultLanguage()
    {
        // Don't leak a language change into other tests/processes that might read the same
        // static LocalizationService.Current in this test run.
        LocalizationService.Current.LoadLanguage("en");
    }

    private static readonly string[] OpQueueTypeKeys =
    [
        "OpQueue.Type.Copy", "OpQueue.Type.Move", "OpQueue.Type.Delete",
        "OpQueue.Type.Pack", "OpQueue.Type.Unpack"
    ];

    [Test]
    public void OpQueueTypeKeys_NeverResolveToTheRawKey_InEitherLanguage()
    {
        // A raw, untranslated key comes back as itself (GetString's fallback) - this is exactly
        // what showed up as literal "OpQueue.Type.Copy" text in the Operation Queue window.
        foreach (var lang in new[] { "en", "ru" })
        {
            LocalizationService.Current.LoadLanguage(lang);
            foreach (var key in OpQueueTypeKeys)
            {
                var resolved = LocalizationService.Current.GetString(key);
                Assert.That(resolved, Is.Not.EqualTo(key), $"Key '{key}' did not resolve to a translation in language '{lang}' - still showing the raw key");
            }
        }
    }

    [Test]
    public void OpQueueTypeKeys_ResolveToTheExpectedWords_English()
    {
        LocalizationService.Current.LoadLanguage("en");
        var L = LocalizationService.Current;

        Assert.That(L.GetString("OpQueue.Type.Copy"), Is.EqualTo("Copy"));
        Assert.That(L.GetString("OpQueue.Type.Move"), Is.EqualTo("Move"));
        Assert.That(L.GetString("OpQueue.Type.Delete"), Is.EqualTo("Delete"));
        Assert.That(L.GetString("OpQueue.Type.Pack"), Is.EqualTo("Pack"));
        Assert.That(L.GetString("OpQueue.Type.Unpack"), Is.EqualTo("Unpack"));
    }

    [Test]
    public void OpQueueTypeKeys_ResolveToTheExpectedWords_Russian()
    {
        LocalizationService.Current.LoadLanguage("ru");
        var L = LocalizationService.Current;

        Assert.That(L.GetString("OpQueue.Type.Copy"), Is.EqualTo("Копирование"));
        Assert.That(L.GetString("OpQueue.Type.Move"), Is.EqualTo("Перемещение"));
        Assert.That(L.GetString("OpQueue.Type.Delete"), Is.EqualTo("Удаление"));
        Assert.That(L.GetString("OpQueue.Type.Pack"), Is.EqualTo("Упаковка"));
        Assert.That(L.GetString("OpQueue.Type.Unpack"), Is.EqualTo("Распаковка"));
    }

    [Test]
    public void EnglishLngFile_IsActuallyLoaded()
    {
        // Regression guard for the root cause, not just its symptom: LoadLanguage("en") used to
        // skip reading lang/english.lng entirely and rely solely on the built-in defaults. Pick a
        // key that only ever existed in english.lng (never in LoadDefaults()) to prove the file
        // itself is being read, not just that someone added the string to the C# defaults instead.
        LocalizationService.Current.LoadLanguage("en");
        Assert.That(LocalizationService.Current.GetString("Edit.StatusPosition"), Is.Not.EqualTo("Edit.StatusPosition"));
    }
}
