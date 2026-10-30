using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public enum GameState
{
    Menu,
    Playing,
    Review,
    Gallery,
    DressUp
}

public class GameManager : MonoBehaviour
{
    [SerializeField] VisitorQueue queue;
    [SerializeField] HandController hand;
    [SerializeField] OutfitCatalog catalog;
    [SerializeField] ModelStage stage;
    [SerializeField] Text scoreText;
    [SerializeField] HeartsDisplay heartsDisplay;
    [SerializeField] Text messageText;
    [SerializeField] int maxHearts = 3;
    [SerializeField] float reviewDelay = 1.3f;

    const string BestKey = "best_score";

    readonly List<MistakeRecord> mistakes = new List<MistakeRecord>();

    GameState state;
    MenuScreen menu;
    GalleryScreen gallery;
    DressUpScreen dressUp;
    ReviewScreen review;
    Text toast;

    int hearts;
    int score;
    int streak;
    int best;
    bool over;
    float messageTimer;
    float toastTimer;
    float inputUnlockTime;
    float reviewUnlockTime;

    void Start()
    {
        // HUD texts come from the scene builder with the default font; give them the UI font and shadow.
        UiKit.StyleHud(scoreText, true);
        UiKit.StyleHud(messageText);
        scoreText.fontSize = 66;
        scoreText.verticalOverflow = VerticalWrapMode.Overflow;
        scoreText.color = UiKit.ComboColor(0);
        messageText.fontSize = 44;
        var canvasRoot = scoreText.canvas.transform;
        toast = UiKit.Label(canvasRoot, "Toast", "", 33, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -135f), new Vector2(1000f, 44f), UiKit.Accent);

        menu = new MenuScreen(canvasRoot);
        menu.StartClicked += StartGame;
        menu.GalleryClicked += OpenGallery;
        menu.DressUpClicked += OpenDressUp;

        gallery = new GalleryScreen(canvasRoot, catalog, stage);
        gallery.Back += EnterMenu;
        dressUp = new DressUpScreen(canvasRoot, catalog, stage);
        dressUp.Back += EnterMenu;
        review = new ReviewScreen(canvasRoot, catalog, stage);
        review.PlayAgain += StartGame;
        review.Title += EnterMenu;

        hand.CanAct = () => state == GameState.Playing && !over && queue.FrontReady && Time.time >= inputUnlockTime;
        hand.TossTarget = GetTossTarget;
        hand.CanBlast = () => state == GameState.Playing && !over && queue.FrontReady && Time.time >= inputUnlockTime;
        hand.Blasted += OnBlast;
        hand.Performed += OnAction;

        best = PlayerPrefs.GetInt(BestKey, 0);
        EnterMenu();
    }

    void HideScreens()
    {
        menu.Hide();
        gallery.Hide();
        dressUp.Hide();
        review.Hide();
        stage.Exit();
    }

    void SetHud(bool visible)
    {
        scoreText.gameObject.SetActive(visible);
        heartsDisplay.gameObject.SetActive(visible);
        messageText.gameObject.SetActive(visible);
        toast.gameObject.SetActive(visible);
    }

    void EnterMenu()
    {
        StopAllCoroutines();
        HideScreens();
        state = GameState.Menu;
        over = false;
        SetHud(false);
        hand.ResetPose();
        hand.SetVisible(false);
        queue.Begin();
        menu.Show(best, catalog.UnlockedCount, catalog.TotalCount);
    }

    void OpenGallery()
    {
        HideScreens();
        state = GameState.Gallery;
        stage.Enter();
        gallery.Show();
    }

    void OpenDressUp()
    {
        HideScreens();
        state = GameState.DressUp;
        stage.Enter();
        dressUp.Show();
    }

    void StartGame()
    {
        StopAllCoroutines();
        HideScreens();
        state = GameState.Playing;
        over = false;
        hearts = maxHearts;
        score = 0;
        streak = 0;
        messageTimer = 0f;
        toastTimer = 0f;
        inputUnlockTime = Time.time + 0.25f;
        mistakes.Clear();
        queue.Begin();
        hand.ResetPose();
        hand.SetVisible(true);
        SetHud(true);
        messageText.text = "";
        toast.text = "";
        Refresh();
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
            ShowMessage(Loc.T("hud.correct"));
            hand.ThumbsUpNext = action == HandAction.Give;
            AudioManager.Play(action == HandAction.Give ? Sfx.AnswerCorrect : Sfx.VisitorShoo);
            // From the second right answer in a row, a blip layers on top. Each step of the streak raises it one semitone, up to an octave.
            if (streak >= 2) AudioManager.Play(Sfx.ComboUp, Mathf.Pow(2f, Mathf.Min(streak - 2, 12) / 12f));
        }
        else
        {
            hearts--;
            streak = 0;
            mistakes.Add(new MistakeRecord { outfit = visitor.CurrentOutfit, action = action });
            ShowMessage(Loc.T(action == HandAction.Give ? "hud.wrongOma" : "hud.wrongKun"));
            AudioManager.Play(Sfx.AnswerWrong);
        }

        queue.ResolveFront(action == HandAction.Give ? Vector3.right : Vector3.left, score);
        Refresh();

        if (hearts <= 0)
        {
            over = true;
            AudioManager.Play(Sfx.GameOver);
            if (score > best)
            {
                best = score;
                PlayerPrefs.SetInt(BestKey, best);
                PlayerPrefs.Save();
            }
            StartCoroutine(ShowReview());
        }
    }

    IEnumerator ShowReview()
    {
        yield return new WaitForSeconds(reviewDelay);
        state = GameState.Review;
        SetHud(false);
        hand.ResetPose();
        hand.SetVisible(false);
        stage.Enter();
        review.Show(mistakes, score, best);
        reviewUnlockTime = Time.time + 0.6f;
    }

    void ShowMessage(string text)
    {
        messageText.text = text;
        messageTimer = 1.2f;
    }

    void ShowToast(string text)
    {
        toast.text = text;
        toastTimer = 2.5f;
    }

    void Refresh()
    {
        scoreText.text = score.ToString();
        heartsDisplay.Set(hearts, maxHearts);
    }

    // Looks the player gets to see at the front of the line are added to the gallery.
    void RecordEncounter()
    {
        var front = queue.Front;
        if (front == null || !front.Arrived) return;

        var outfit = front.CurrentOutfit;
        if (!catalog.Unlock(outfit)) return;
        if (outfit.mask == 0 && !outfit.sheet) return;
        ShowToast(Loc.T("hud.newLook", OutfitCatalog.KindName(outfit.kind), catalog.EntryNumber(outfit)));
    }

    void Update()
    {
        scoreText.color = Color.Lerp(scoreText.color, UiKit.ComboColor(streak), 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
        if (messageTimer > 0f)
        {
            messageTimer -= Time.deltaTime;
            if (messageTimer <= 0f) messageText.text = "";
        }
        if (toastTimer > 0f)
        {
            toastTimer -= Time.deltaTime;
            if (toastTimer <= 0f) toast.text = "";
        }

        if (state == GameState.Playing)
        {
            if (!over) RecordEncounter();
            return;
        }

        var kb = Keyboard.current;
        if (state == GameState.Review && Time.time >= reviewUnlockTime && kb != null
            && (kb.rKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
        {
            StartGame();
        }
    }
}
