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

    public static event Action Changed;

    public static bool Configured => !string.IsNullOrEmpty(BaseUrl);
    public static LoadStatus Status => status;
    public static IReadOnlyList<WorldEntry> Entries => entries;

    // Rank the server gave the last submitted game (null while unknown or when it did not reach the board).
    public static int LastRank { get; private set; }

    [Serializable]
    class Row
    {
        public string initials;
        public int score;
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
                    foreach (var row in list.items) entries.Add(new WorldEntry { initials = LocalRanking.CleanInitials(row.initials), score = row.score });
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
}
