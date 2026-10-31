using UnityEngine;

// Easter-egg visitors that join the line now and then. Either answer is right for them (give candy or send
// away): they never cost a heart and always score. Which ones the player has met is kept on this device.
public class SpecialGuest : MonoBehaviour
{
    public const int Count = 2;
    public static readonly string[] Names = { "Moku", "Myeong7" };

    const string SeenKey = "special_seen_v1";

    [SerializeField] int id; // index into Names

    public int Id { get => id; set => id = Mathf.Clamp(value, 0, Count - 1); }
    public string GuestName => Names[id];

    public static bool Seen(int guest) => (PlayerPrefs.GetInt(SeenKey, 0) & (1 << guest)) != 0;

    public static void UnlockAll()
    {
        PlayerPrefs.SetInt(SeenKey, (1 << Count) - 1);
        PlayerPrefs.Save();
    }

    // Returns true the first time this guest is met.
    public static bool MarkSeen(int guest)
    {
        if (Seen(guest)) return false;
        PlayerPrefs.SetInt(SeenKey, PlayerPrefs.GetInt(SeenKey, 0) | (1 << guest));
        PlayerPrefs.Save();
        return true;
    }
}
