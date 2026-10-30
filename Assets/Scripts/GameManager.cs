using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    [SerializeField] VisitorQueue queue;
    [SerializeField] HandController hand;
    [SerializeField] Text scoreText;
    [SerializeField] HeartsDisplay heartsDisplay;
    [SerializeField] Text messageText;
    [SerializeField] GameObject gameOverPanel;
    [SerializeField] Text gameOverText;
    [SerializeField] int maxHearts = 3;

    int hearts;
    int score;
    int streak;
    bool over;
    float messageTimer;

    void Start()
    {
        hand.CanAct = () => !over && queue.FrontReady;
        hand.TossTarget = GetTossTarget;
        hand.CanBlast = () => !over && queue.FrontReady;
        hand.Blasted += OnBlast;
        hand.Performed += OnAction;
        StartGame();
    }

    void OnBlast()
    {
        AudioManager.Play(Sfx.VisitorShoo);
        var visitor = queue.Front;
        if (visitor == null) return;

        var cam = Camera.main;
        Vector3 direction = cam != null ? visitor.transform.position - cam.transform.position : Vector3.forward;
        if (visitor.IsCoveredBySheet)
        {
            visitor.BlowOffSheet(direction);
            return;
        }

        visitor.KnockDown();
        if (visitor.TryGetComponent(out VisitorDisguise disguise))
        {
            disguise.DropOneFakeProp(direction);
        }
    }

    Vector3? GetTossTarget()
    {
        var visitor = queue.Front;
        if (visitor == null) return null;

        Vector3 point = visitor.transform.position + Vector3.up * 0.3f;
        var cam = Camera.main;
        if (cam != null) point += (cam.transform.position - point).normalized * 0.45f;
        return point;
    }

    void StartGame()
    {
        over = false;
        hearts = maxHearts;
        score = 0;
        streak = 0;
        messageTimer = 0f;
        queue.Begin();
        hand.ResetPose();
        gameOverPanel.SetActive(false);
        messageText.text = "";
        Refresh();
    }

    void OnAction(HandAction action)
    {
        var visitor = queue.Front;
        if (visitor == null) return;

        bool correct = action == HandAction.Give
            ? visitor.kind == VisitorKind.Kunmungchi
            : visitor.kind == VisitorKind.Omangi;

        if (correct)
        {
            score++;
            streak++;
            ShowMessage("Correct!");
            hand.ThumbsUpNext = action == HandAction.Give;
            AudioManager.Play(action == HandAction.Give ? Sfx.AnswerCorrect : Sfx.VisitorShoo);
            // From the second right answer in a row, a blip layers on top. Each step of the streak raises it one semitone, up to an octave.
            if (streak >= 2) AudioManager.Play(Sfx.ComboUp, Mathf.Pow(2f, Mathf.Min(streak - 2, 12) / 12f));
        }
        else
        {
            hearts--;
            streak = 0;
            ShowMessage(action == HandAction.Give ? "That was an Omangi!" : "That was a Kunmungchi!");
            AudioManager.Play(Sfx.AnswerWrong);
        }

        queue.ResolveFront(action == HandAction.Give ? Vector3.right : Vector3.left, score);
        Refresh();

        if (hearts <= 0)
        {
            over = true;
            AudioManager.Play(Sfx.GameOver);
            gameOverText.text = "GAME OVER\nScore: " + score + "\nPress R to restart";
            gameOverPanel.SetActive(true);
        }
    }

    void ShowMessage(string text)
    {
        messageText.text = text;
        messageTimer = 1.2f;
    }

    void Refresh()
    {
        scoreText.text = "Score: " + score;
        if (heartsDisplay != null) heartsDisplay.Set(hearts, maxHearts);
    }

    void Update()
    {
        if (messageTimer > 0f)
        {
            messageTimer -= Time.deltaTime;
            if (messageTimer <= 0f) messageText.text = "";
        }

        var kb = Keyboard.current;
        if (over && kb != null && (kb.rKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
        {
            StartGame();
        }
    }
}
