using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    [SerializeField] VisitorQueue queue;
    [SerializeField] HandController hand;
    [SerializeField] Text scoreText;
    [SerializeField] Text heartsText;
    [SerializeField] Text messageText;
    [SerializeField] GameObject gameOverPanel;
    [SerializeField] Text gameOverText;
    [SerializeField] int maxHearts = 3;

    int hearts;
    int score;
    bool over;
    float messageTimer;

    void Start()
    {
        hand.CanAct = () => !over && queue.FrontReady;
        hand.TossTarget = GetTossTarget;
        hand.Performed += OnAction;
        StartGame();
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
            ShowMessage("Correct!");
            hand.ThumbsUpNext = action == HandAction.Give;
        }
        else
        {
            hearts--;
            ShowMessage(action == HandAction.Give ? "That was an Omangi!" : "That was a Kunmungchi!");
        }

        queue.ResolveFront(action == HandAction.Give ? Vector3.right : Vector3.left, score);
        Refresh();

        if (hearts <= 0)
        {
            over = true;
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
        heartsText.text = "Hearts: " + hearts;
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
