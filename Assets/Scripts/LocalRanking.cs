using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// One finished game in the local ranking. `result` is the LookCode result text of that game (score and the
// three looks that cost a heart), so the looks can be shown again later; initials are kept separately.
public struct RankEntry
{
    public int score;
    public string initials;
    public long unixTime;
    public string result;

    public DateTime Date => DateTimeOffset.FromUnixTimeSeconds(unixTime).LocalDateTime;

    // The three looks of that game (fewer if the text is damaged or empty).
    public bool TryGetLooks(out Outfit[] looks)
    {
        looks = new Outfit[0];
        return !string.IsNullOrEmpty(result) && LookCode.TryDecode(result, out _, out _, out looks, out _);
    }
}

// The player's own top scores, kept in PlayerPrefs on this device (works offline, next to the world ranking).
// 20 entries, shown 10 per page: ranks 1-10 and 11-20.
public static class LocalRanking
{
    public const int Capacity = 20;
    public const int PageSize = 10;
    public const int PageCount = Capacity / PageSize;

    const string SaveKey = "local_ranking_v1";
    const string InitialsKey = "last_initials";
    const string SharedKey = "local_shared_v1";
    const int SharedKept = 200;

    static List<RankEntry> entries;

    public static IReadOnlyList<RankEntry> Entries
    {
        get
        {
            Load();
            return entries;
        }
    }

    // The initials typed last time, offered as the default next time.
    public static string LastInitials
    {
        get => CleanInitials(PlayerPrefs.GetString(InitialsKey, "AAA"));
        set
        {
            PlayerPrefs.SetString(InitialsKey, CleanInitials(value));
            PlayerPrefs.Save();
        }
    }

    // Three capital letters A-Z; anything else is dropped and the rest is filled with A.
    public static string CleanInitials(string text)
    {
        var sb = new StringBuilder(3);
        if (text != null)
        {
            foreach (char c in text.ToUpperInvariant())
            {
                if (c >= 'A' && c <= 'Z' && sb.Length < 3) sb.Append(c);
            }
        }
        while (sb.Length < 3) sb.Append('A');
        return sb.ToString();
    }

    // Would this score get into the ranking? A tie never pushes an older record out.
    public static bool Qualifies(int score)
    {
        Load();
        return score > 0 && (entries.Count < Capacity || score > entries[entries.Count - 1].score);
    }

    // Adds the game and returns its rank (1 = best), or 0 if it did not qualify.
    public static int Add(int score, string initials, string resultText)
    {
        if (!Qualifies(score)) return 0;

        var entry = new RankEntry
        {
            score = score,
            initials = CleanInitials(initials),
            unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            result = resultText ?? "",
        };
        int index = entries.Count;
        while (index > 0 && entries[index - 1].score < score) index--; // after equal scores
        entries.Insert(index, entry);
        if (entries.Count > Capacity) entries.RemoveRange(Capacity, entries.Count - Capacity);
        Save();
        return index + 1;
    }

    // Changes the initials of the entry at this rank (1-based), e.g. after the player typed them.
    public static void SetInitials(int rank, string initials)
    {
        Load();
        if (rank < 1 || rank > entries.Count) return;
        var entry = entries[rank - 1];
        entry.initials = CleanInitials(initials);
        entries[rank - 1] = entry;
        LastInitials = entry.initials;
        Save();
    }

    // The entries on a page (0-based page), at most PageSize.
    public static List<RankEntry> Page(int page)
    {
        Load();
        var list = new List<RankEntry>(PageSize);
        for (int i = page * PageSize; i < (page + 1) * PageSize && i < entries.Count; i++) list.Add(entries[i]);
        return list;
    }

    public static void Clear()
    {
        entries = new List<RankEntry>();
        Save();
    }

    // ---- sharing the local records with the world ranking ----

    static List<string> shared; // result texts already sent, oldest first

    // The record as the server wants it: its three looks (read from whatever version saved them) with the
    // record's own score and initials, written in the current format. Null when the looks cannot be read.
    public static string ShareText(RankEntry entry)
    {
        if (entry.score <= 0 || !entry.TryGetLooks(out Outfit[] looks) || looks.Length == 0) return null;
        return LookCode.Encode(entry.score, entry.initials, looks);
    }

    // Texts of the records not sent yet (equal records count once). A record whose score does not satisfy `reaches`
    // (it would not make the world ranking) is left out and, when `skipped` is given, listed there instead.
    public static List<string> PendingShare(Func<int, bool> reaches = null, List<string> skipped = null)
    {
        Load();
        LoadShared();
        var list = new List<string>();
        foreach (var entry in entries)
        {
            string text = ShareText(entry);
            if (text == null || shared.Contains(text) || list.Contains(text)) continue;
            if (reaches != null && !reaches(entry.score))
            {
                skipped?.Add(text);
                continue;
            }
            list.Add(text);
        }
        return list;
    }

    public static void MarkShared(string text)
    {
        LoadShared();
        if (shared.Contains(text)) return;
        shared.Add(text);
        if (shared.Count > SharedKept) shared.RemoveRange(0, shared.Count - SharedKept);
        PlayerPrefs.SetString(SharedKey, string.Join(";", shared));
        PlayerPrefs.Save();
    }

    static void LoadShared()
    {
        if (shared != null) return;
        shared = new List<string>(PlayerPrefs.GetString(SharedKey, "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
    }

    // Entries are separated by ';', fields by '|': score|initials|unixTime|resultText (base64url has neither).
    static void Load()
    {
        if (entries != null) return;
        entries = new List<RankEntry>();
        bool upgradedAny = false;
        foreach (string row in PlayerPrefs.GetString(SaveKey, "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] f = row.Split('|');
            if (f.Length < 4 || !int.TryParse(f[0], out int score) || !long.TryParse(f[2], out long time)) continue;
            string result = f[3];
            // Results saved by an older version are rewritten in the current format (the looks stay readable).
            if (LookCode.TryUpgrade(result, out string upgraded) && upgraded != result)
            {
                result = upgraded;
                upgradedAny = true;
            }
            entries.Add(new RankEntry { score = score, initials = CleanInitials(f[1]), unixTime = time, result = result });
        }
        if (upgradedAny) Save();
        // Saved in rank order already; keep it as it is (a sort could shuffle equal scores).
    }

    static void Save()
    {
        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            if (sb.Length > 0) sb.Append(';');
            sb.Append(e.score).Append('|').Append(e.initials).Append('|').Append(e.unixTime).Append('|').Append(e.result);
        }
        PlayerPrefs.SetString(SaveKey, sb.ToString());
        PlayerPrefs.Save();
    }
}
