using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[Serializable]
public class HandClip
{
    public Sprite[] frames;
    public int holdStart;
}

public class HandController : MonoBehaviour
{
    [SerializeField] SpriteRenderer handRenderer;
    [SerializeField] SpriteRenderer candy;
    [SerializeField] Sprite[] candySprites;
    [SerializeField] Sprite idleSprite;
    [SerializeField] HandClip giveClip;
    [SerializeField] HandClip rejectClip;
    [SerializeField] HandClip thumbsUpClip;
    [SerializeField] float fps = 96f;
    [SerializeField] float holdFps = 6f;
    [SerializeField] float holdTime = 0.15f;
    [SerializeField] float thumbsUpTime = 0.15f;
    [SerializeField] float candyPopTime = 0.05f;

    public bool ThumbsUpNext { get; set; }
    public Func<bool> CanAct;
    public event Action<HandAction> Performed;

    public bool Busy { get; private set; }

    Vector3 candyScale = Vector3.one;

    void Awake()
    {
        if (candy != null) candyScale = candy.transform.localScale;
        ResetPose();
    }

    public void ResetPose()
    {
        StopAllCoroutines();
        Busy = false;
        ShowIdle();
    }

    void Update()
    {
        var mouse = Mouse.current;
        if (Busy || mouse == null) return;
        if (CanAct != null && !CanAct()) return;

        if (mouse.leftButton.wasPressedThisFrame) StartCoroutine(Perform(HandAction.Give));
        else if (mouse.rightButton.wasPressedThisFrame) StartCoroutine(Perform(HandAction.Reject));
    }

    IEnumerator Perform(HandAction action)
    {
        Busy = true;
        ThumbsUpNext = false;

        if (action == HandAction.Give)
        {
            yield return PlayClip(giveClip, holdTime, true, null);
            Performed?.Invoke(action);
        }
        else
        {
            yield return PlayClip(rejectClip, holdTime, false, () => Performed?.Invoke(action));
        }

        if (ThumbsUpNext && HasFrames(thumbsUpClip))
        {
            ThumbsUpNext = false;
            yield return PlayClip(thumbsUpClip, thumbsUpTime, false, null);
        }
        ShowIdle();
        Busy = false;
    }

    IEnumerator PlayClip(HandClip clip, float hold, bool withCandy, Action onPeak)
    {
        if (!HasFrames(clip))
        {
            onPeak?.Invoke();
            yield return new WaitForSeconds(hold);
            yield break;
        }

        int last = clip.frames.Length - 1;
        int holdStart = Mathf.Clamp(clip.holdStart, 0, last);

        for (float t = 0f; ; t += Time.deltaTime)
        {
            int i = Mathf.Min(Mathf.FloorToInt(t * fps), holdStart);
            handRenderer.sprite = clip.frames[i];
            if (i >= holdStart) break;
            yield return null;
        }

        if (withCandy) StartCoroutine(PopCandy());
        onPeak?.Invoke();

        for (float t = 0f; t < hold; t += Time.deltaTime)
        {
            int i = holdStart + Mathf.RoundToInt(Mathf.PingPong(t * holdFps, last - holdStart));
            handRenderer.sprite = clip.frames[i];
            yield return null;
        }

        HideCandy();
        for (float t = 0f; ; t += Time.deltaTime)
        {
            int i = Mathf.Max(holdStart - Mathf.FloorToInt(t * fps), 0);
            handRenderer.sprite = clip.frames[i];
            if (i <= 0) break;
            yield return null;
        }
    }

    IEnumerator PopCandy()
    {
        if (candy == null) yield break;
        if (candySprites != null && candySprites.Length > 0)
            candy.sprite = candySprites[UnityEngine.Random.Range(0, candySprites.Length)];
        if (candy.sprite == null) yield break;

        candy.gameObject.SetActive(true);
        for (float t = 0f; t < candyPopTime; t += Time.deltaTime)
        {
            candy.transform.localScale = candyScale * EaseOutBack(t / candyPopTime);
            yield return null;
        }
        candy.transform.localScale = candyScale;
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float u = x - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    static bool HasFrames(HandClip clip)
    {
        return clip != null && clip.frames != null && clip.frames.Length > 0;
    }

    void HideCandy()
    {
        if (candy != null) candy.gameObject.SetActive(false);
    }

    void ShowIdle()
    {
        handRenderer.sprite = idleSprite;
        HideCandy();
    }
}
