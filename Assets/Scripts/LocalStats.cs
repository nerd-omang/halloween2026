using System.Collections.Generic;
using System.Text;
using UnityEngine;

// The player's own statistics, kept in PlayerPrefs on this device: how many games, how well, and which
// looks fooled this player most. Counted per item bit (the same bits as ItemTable / LookCode); a look with
// no items counts as "natural look" (NaturalKey), and a sheeted look counts the sheet and what is under it.
public static class LocalStats
{
    public const int NaturalKey = 31;

    const string SaveKey = "local_stats_v1";

    static bool loaded;
    static int games;
    static int totalScore;
    static int gaveToOmangi;
    static int sentKunmungchi;
    static readonly Dictionary<int, int> fooled = new Dictionary<int, int>();

    public static int Games { get { Load(); return games; } }
    public static int TotalScore { get { Load(); return totalScore; } }
    public static float AverageScore { get { Load(); return games == 0 ? 0f : totalScore / (float)games; } }
    // Mistakes by kind: gave candy to an Omangi / sent a Kunmungchi away.
    public static int GaveToOmangi { get { Load(); return gaveToOmangi; } }
    public static int SentKunmungchi { get { Load(); return sentKunmungchi; } }

    public static int Fooled(int key)
    {
        Load();
        return fooled.TryGetValue(key, out int count) ? count : 0;
    }

    // Looks that fooled the player most, most first: (item bit or NaturalKey, times).
    public static List<KeyValuePair<int, int>> TopFooled(int count)
    {
        Load();
        var list = new List<KeyValuePair<int, int>>(fooled);
        list.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : a.Key.CompareTo(b.Key));
        if (list.Count > count) list.RemoveRange(count, list.Count - count);
        return list;
    }

    // Adds one finished game: its score and the looks that cost a heart.
    public static void RecordGame(int score, IList<Outfit> mistakes)
    {
        Load();
        games++;
        totalScore += score;
        foreach (var outfit in mistakes)
        {
            if (outfit.kind == VisitorKind.Omangi) gaveToOmangi++;
            else sentKunmungchi++;

            if (outfit.items == 0)
            {
                Bump(NaturalKey);
                continue;
            }
            foreach (var def in ItemTable.All)
            {
                if (outfit.Has(def.Bit)) Bump(def.Bit);
            }
        }
        Save();
    }

    public static void Reset()
    {
        games = totalScore = gaveToOmangi = sentKunmungchi = 0;
        fooled.Clear();
        loaded = true;
        Save();
    }

    static void Bump(int key)
    {
        fooled[key] = Fooled(key) + 1;
    }

    // "games,totalScore,gaveToOmangi,sentKunmungchi;bit:count,bit:count..."
    static void Load()
    {
        if (loaded) return;
        loaded = true;
        string[] parts = PlayerPrefs.GetString(SaveKey, "").Split(';');
        if (parts.Length == 0 || parts[0].Length == 0) return;

        string[] head = parts[0].Split(',');
        if (head.Length >= 4)
        {
            int.TryParse(head[0], out games);
            int.TryParse(head[1], out totalScore);
            int.TryParse(head[2], out gaveToOmangi);
            int.TryParse(head[3], out sentKunmungchi);
        }
        if (parts.Length < 2) return;
        foreach (string pair in parts[1].Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            string[] kv = pair.Split(':');
            if (kv.Length == 2 && int.TryParse(kv[0], out int key) && int.TryParse(kv[1], out int count)) fooled[key] = count;
        }
    }

    static void Save()
    {
        var sb = new StringBuilder();
        sb.Append(games).Append(',').Append(totalScore).Append(',').Append(gaveToOmangi).Append(',').Append(sentKunmungchi).Append(';');
        bool first = true;
        foreach (var pair in fooled)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(pair.Key).Append(':').Append(pair.Value);
        }
        PlayerPrefs.SetString(SaveKey, sb.ToString());
        PlayerPrefs.Save();
    }
}
