using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// One row of the online top list.
public struct WorldEntry
{
    public string initials;
    public int score;
    public string result; // LookCode result text of that game, empty for entries stored before the server kept it

    // The three looks of that game (fewer, or none, if the text is missing or damaged).
    public bool TryGetLooks(out Outfit[] looks)
    {
        looks = new Outfit[0];
        return !string.IsNullOrEmpty(result) && LookCode.TryDecode(result, out _, out _, out looks, out _);
    }
}

// Client of the leaderboard server (server/API.md): sends each finished game as its short result text and
// fetches the top list. Separate from LocalRanking: the two boards never share data. Without a BaseUrl
// (server not deployed yet) every call does nothing and Status stays NotConfigured.
public static class WorldBoard
{
    public enum LoadStatus
    {
        NotConfigured,
        Idle,
        Loading,
        Ready,
        Failed,
    }

    // The fly.io app URL (https, no trailing slash), e.g. "https://halloween2026-board.fly.dev".
    public const string BaseUrl = "https://halloween2026-board.fly.dev";

    const int TimeoutSeconds = 12;
    const float MinRefreshInterval = 15f;

    static readonly List<WorldEntry> entries = new List<WorldEntry>();
    static LoadStatus status = string.IsNullOrEmpty(BaseUrl) ? LoadStatus.NotConfigured : LoadStatus.Idle;
    static float lastRefreshTime = -999f;

    // World statistics (/api/stats): games played, and how often each item fooled players. Same item keys as LocalStats.
    static readonly Dictionary<int, int> fooled = new Dictionary<int, int>();
    static LoadStatus statsStatus = string.IsNullOrEmpty(BaseUrl) ? LoadStatus.NotConfigured : LoadStatus.Idle;
    static float lastStatsTime = -999f;

    public static event Action Changed;

    public static bool Configured => !string.IsNullOrEmpty(BaseUrl);
    public static LoadStatus Status => status;
    public static IReadOnlyList<WorldEntry> Entries => entries;

    public static LoadStatus StatsStatus => statsStatus;
    public static int StatsGames { get; private set; }

    public static int Fooled(int key) => fooled.TryGetValue(key, out int count) ? count : 0;

    // Looks that fooled players most, most first: (item bit or LocalStats.NaturalKey, times).
    public static List<KeyValuePair<int, int>> TopFooled(int count)
    {
        var list = new List<KeyValuePair<int, int>>(fooled);
        list.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : a.Key.CompareTo(b.Key));
        if (list.Count > count) list.RemoveRange(count, list.Count - count);
        return list;
    }

    // Rank the server gave the last submitted game (null while unknown or when it did not reach the board).
    public static int LastRank { get; private set; }

    [Serializable]
    class Row
    {
        public string initials;
        public int score;
        public string r; // result text, see server/API.md
    }

    [Serializable]
    class RowList
    {
        public Row[] items;
    }

    [Serializable]
    class SubmitAnswer
    {
        public int rank;
        public bool top;
    }

    // Fetches the top list unless it was fetched a moment ago (the server caches it for 20 s anyway).
    public static void Refresh(bool force = false)
    {
        if (!Configured || status == LoadStatus.Loading) return;
        if (!force && status == LoadStatus.Ready && Time.realtimeSinceStartup - lastRefreshTime < MinRefreshInterval) return;

        status = LoadStatus.Loading;
        Changed?.Invoke();
        var request = UnityWebRequest.Get(BaseUrl + "/api/top?t=" + (int)Time.realtimeSinceStartup);
        request.timeout = TimeoutSeconds;
        request.SendWebRequest().completed += _ =>
        {
            try
            {
                if (request.result != UnityWebRequest.Result.Success) throw new Exception(request.error);
                var list = JsonUtility.FromJson<RowList>("{\"items\":" + request.downloadHandler.text + "}");
                entries.Clear();
                if (list?.items != null)
                {
                    foreach (var row in list.items) entries.Add(new WorldEntry { initials = LocalRanking.CleanInitials(row.initials), score = row.score, result = row.r ?? "" });
                }
                status = LoadStatus.Ready;
                lastRefreshTime = Time.realtimeSinceStartup;
            }
            catch (Exception e)
            {
                Debug.LogWarning("WorldBoard: could not load the top list (" + e.Message + ")");
                status = LoadStatus.Failed;
            }
            finally
            {
                request.Dispose();
            }
            Changed?.Invoke();
        };
    }

    // Fetches the world statistics unless they were fetched a moment ago (the server caches them as well).
    public static void RefreshStats(bool force = false)
    {
        if (!Configured || statsStatus == LoadStatus.Loading) return;
        if (!force && statsStatus == LoadStatus.Ready && Time.realtimeSinceStartup - lastStatsTime < MinRefreshInterval) return;

        statsStatus = LoadStatus.Loading;
        Changed?.Invoke();
        var request = UnityWebRequest.Get(BaseUrl + "/api/stats?t=" + (int)Time.realtimeSinceStartup);
        request.timeout = TimeoutSeconds;
        request.SendWebRequest().completed += _ =>
        {
            try
            {
                if (request.result != UnityWebRequest.Result.Success) throw new Exception(request.error);
                ParseStats(request.downloadHandler.text);
                statsStatus = LoadStatus.Ready;
                lastStatsTime = Time.realtimeSinceStartup;
            }
            catch (Exception e)
            {
                Debug.LogWarning("WorldBoard: could not load the statistics (" + e.Message + ")");
                statsStatus = LoadStatus.Failed;
            }
            finally
            {
                request.Dispose();
            }
            Changed?.Invoke();
        };
    }

    // {"games":N,"items":{"<bit>":n,...},"looks":{"<code>":n,...}}. JsonUtility cannot read objects with
    // changing keys, so the two maps are read by hand.
    static void ParseStats(string json)
    {
        fooled.Clear();
        StatsGames = ReadInt(json, "\"games\":");

        var items = new Dictionary<long, int>();
        ReadMap(json, "\"items\":{", items);
        foreach (var pair in items) fooled[(int)pair.Key] = pair.Value;

        // A look without any item (the natural look) is not an item bit: count it from the whole-look map.
        var looks = new Dictionary<long, int>();
        ReadMap(json, "\"looks\":{", looks);
        int natural = 0;
        foreach (var pair in looks)
        {
            if ((pair.Key >> 3) == 0) natural += pair.Value;
        }
        if (natural > 0) fooled[LocalStats.NaturalKey] = natural;
    }

    static int ReadInt(string json, string field)
    {
        int start = json.IndexOf(field, StringComparison.Ordinal);
        if (start < 0) return 0;
        start += field.Length;
        int end = start;
        while (end < json.Length && char.IsDigit(json[end])) end++;
        return int.TryParse(json.Substring(start, end - start), out int value) ? value : 0;
    }

    static void ReadMap(string json, string field, Dictionary<long, int> into)
    {
        int start = json.IndexOf(field, StringComparison.Ordinal);
        if (start < 0) return;
        start += field.Length;
        int end = json.IndexOf('}', start);
        if (end < 0) return;
        foreach (string pair in json.Substring(start, end - start).Split(','))
        {
            string[] kv = pair.Split(':');
            if (kv.Length != 2) continue;
            if (long.TryParse(kv[0].Trim().Trim('"'), out long key) && int.TryParse(kv[1].Trim(), out int count)) into[key] = count;
        }
    }

    // Sends one finished game (LookCode.Encode text, with or without initials). Fire and forget: the game-over
    // screen never waits for it, and a failure only costs that one statistic.
    public static void Submit(string resultText)
    {
        if (!Configured || string.IsNullOrEmpty(resultText)) return;

        LastRank = 0;
        var request = new UnityWebRequest(BaseUrl + "/api/result", "POST")
        {
            uploadHandler = new UploadHandlerRaw(Encoding.ASCII.GetBytes(resultText)),
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = TimeoutSeconds,
        };
        request.SetRequestHeader("Content-Type", "text/plain");
        request.SendWebRequest().completed += _ =>
        {
            try
            {
                if (request.result == UnityWebRequest.Result.Success)
                {
                    var answer = JsonUtility.FromJson<SubmitAnswer>(request.downloadHandler.text);
                    if (answer != null && answer.top) LastRank = answer.rank;
                    lastRefreshTime = -999f; // the board may have changed
                }
                else
                {
                    Debug.LogWarning("WorldBoard: result not sent (" + request.error + ")");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("WorldBoard: bad answer to the result (" + e.Message + ")");
            }
            finally
            {
                request.Dispose();
            }
            Changed?.Invoke();
        };
    }

    // Sends several finished games at once (the local records). A game counts as sent when the server answered
    // 200, a repeat of an earlier one included; `sent` gets each such text, `done` the number sent and failed.
    public static void SubmitMany(IList<string> texts, Action<string> sent, Action<int, int> done)
    {
        if (!Configured || texts == null || texts.Count == 0)
        {
            done?.Invoke(0, 0);
            return;
        }

        int remaining = texts.Count;
        int ok = 0;
        foreach (string text in texts)
        {
            string current = text;
            var request = new UnityWebRequest(BaseUrl + "/api/result", "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.ASCII.GetBytes(current)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = TimeoutSeconds,
            };
            request.SetRequestHeader("Content-Type", "text/plain");
            request.SendWebRequest().completed += _ =>
            {
                if (request.result == UnityWebRequest.Result.Success)
                {
                    ok++;
                    sent?.Invoke(current);
                }
                else
                {
                    Debug.LogWarning("WorldBoard: record not sent (" + request.error + ")");
                }
                request.Dispose();
                if (--remaining > 0) return;
                lastRefreshTime = -999f; // the board may have changed
                done?.Invoke(ok, texts.Count - ok);
                Changed?.Invoke();
            };
        }
    }
}
