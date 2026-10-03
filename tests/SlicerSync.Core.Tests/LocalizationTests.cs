using SlicerSync.Core.Git;
using SlicerSync.Core.Sync;

namespace SlicerSync.Core.Tests;

/// <summary>Switches the global language, so it must not run in parallel with other tests.</summary>
[CollectionDefinition(nameof(LanguageCollection), DisableParallelization = true)]
public sealed class LanguageCollection { }

[Collection(nameof(LanguageCollection))]
public sealed class LocalizationTests
{
    [Fact]
    public void EnglishIsDefault_GermanCanBeSelected()
    {
        try
        {
            Assert.False(Loc.IsGerman);
            Assert.Equal("modified", new ChangedFile("M", "a").StatusText);
            Assert.StartsWith("Profile sync", CommitMessage.Format("", DateTime.Now, "PC", "u", 1));

            Loc.SetLanguage("de");
            Assert.True(Loc.IsGerman);
            Assert.Equal("geändert", new ChangedFile("M", "a").StatusText);
            Assert.StartsWith("Profil-Sync", CommitMessage.Format("", DateTime.Now, "PC", "u", 1));
            Assert.Equal("03.10.2026 09:05", Loc.DateTime(new DateTime(2026, 10, 3, 9, 5, 0)));

            Loc.SetLanguage("xx");
            Assert.False(Loc.IsGerman);
        }
        finally
        {
            Loc.SetLanguage("en");
        }
    }

    [Fact]
    public void Placeholders_WorkInBothLanguages()
    {
        var now = new DateTime(2026, 10, 3, 9, 5, 0);
        Assert.Equal("2026-10-03 09:05 PC1 max 7", CommitMessage.Format("{date} {time} {computer} {user} {count}", now, "PC1", "max", 7));
        Assert.Equal("2026-10-03 09:05 PC1 max 7", CommitMessage.Format("{datum} {zeit} {rechner} {benutzer} {anzahl}", now, "PC1", "max", 7));
    }
}
