using WinCleaner.Core;
using Logger = WinCleaner.Core.Logger;

namespace WinCleaner.Tests;

public class HashCacheTests
{
    [Fact]
    public void TryGet_MissOnEmptyCache_HitAfterSet()
    {
        using var tmp = new TempDir();
        var cache = HashCache.Load(System.IO.Path.Combine(tmp.Path, "cache.json"), new Logger());
        var mtime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(cache.TryGet(@"C:\x\a.txt", 10, mtime, out _));

        cache.Set(@"C:\x\a.txt", 10, mtime, "HASH_A");
        Assert.True(cache.TryGet(@"C:\x\a.txt", 10, mtime, out var hash));
        Assert.Equal("HASH_A", hash);
        Assert.Equal(1, cache.Hits);
    }

    [Fact]
    public void TryGet_InvalidatesOnSizeOrMTimeChange()
    {
        using var tmp = new TempDir();
        var cache = HashCache.Load(System.IO.Path.Combine(tmp.Path, "cache.json"), new Logger());
        var mtime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        cache.Set(@"C:\x\a.txt", 10, mtime, "HASH_A");

        // Andere Größe bzw. andere Schreibzeit -> Eintrag gilt nicht mehr.
        Assert.False(cache.TryGet(@"C:\x\a.txt", 11, mtime, out _));
        Assert.False(cache.TryGet(@"C:\x\a.txt", 10, mtime.AddSeconds(1), out _));
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        using var tmp = new TempDir();
        var file = System.IO.Path.Combine(tmp.Path, "sub", "cache.json"); // Unterordner wird mit angelegt
        var mtime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var cache = HashCache.Load(file, new Logger());
        cache.Set(@"C:\x\a.txt", 10, mtime, "HASH_A");
        cache.Save();

        var reloaded = HashCache.Load(file, new Logger());
        Assert.True(reloaded.TryGet(@"C:\x\a.txt", 10, mtime, out var hash));
        Assert.Equal("HASH_A", hash);
    }

    [Fact]
    public void Load_CorruptFile_YieldsEmptyCacheInsteadOfError()
    {
        using var tmp = new TempDir();
        var file = tmp.Write("cache.json", "kein json {{{");

        var cache = HashCache.Load(file, new Logger());
        Assert.False(cache.TryGet(@"C:\x\a.txt", 10, DateTime.UtcNow, out _));
    }

    [Fact]
    public void Find_WithCache_SameResultAndCacheHitsOnSecondRun()
    {
        using var tmp = new TempDir();
        tmp.Write("a.txt", "inhalt gleich");
        tmp.Write("sub/b.txt", "inhalt gleich");
        tmp.Write("c.txt", "anders");

        var cachePath = System.IO.Path.Combine(tmp.Path, "cache.json");
        var finder = new DuplicateFinder(new Logger());

        var cache1 = HashCache.Load(cachePath, new Logger());
        var run1 = finder.Find(tmp.Path, cache1);
        cache1.Save();

        var cache2 = HashCache.Load(cachePath, new Logger());
        var run2 = finder.Find(tmp.Path, cache2);

        // Gleiches Ergebnis wie ohne Cache, und im zweiten Lauf kommen die
        // vollen Hashes aus dem Cache.
        Assert.Single(run1);
        Assert.Single(run2);
        Assert.Equal(run1[0].Hash, run2[0].Hash);
        Assert.Equal(2, run1[0].Files.Count);
        Assert.True(cache2.Hits >= 2, $"Erwartet >=2 Cache-Treffer, war {cache2.Hits}");
    }
}
