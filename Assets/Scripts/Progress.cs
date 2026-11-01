using System;
using UnityEngine;

// Everything the player has earned on this device: best score, statistics, the gallery and the special guests
// met. ResetAll() wipes these; the local ranking, the initials and the settings (volumes, dither, language) are kept.
public static class Progress
{
    public const string BestKey = "best_score";
    public const string GuideKey = "guide_done";

    // Raised after the saved data is gone, so objects holding copies in memory can drop them.
    public static event Action Cleared;

    public static void ResetAll()
    {
        LocalStats.Reset();
        PlayerPrefs.DeleteKey(BestKey);
        PlayerPrefs.DeleteKey(GuideKey);
        SpecialGuest.Forget();
        Cleared?.Invoke();
        PlayerPrefs.Save();
    }
}
