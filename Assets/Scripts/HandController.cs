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
    [SerializeField] HandClip blastClip;
    [SerializeField] float blastHoldTime = 0.5f;
    [SerializeField] float blastCooldown = 0.3f;

    float blastReadyTime;
    [SerializeField] float fps = 96f;
    [SerializeField] float holdFps = 6f;
    [SerializeField] float holdTime = 0.15f;
    [SerializeField] float thumbsUpTime = 0.15f;
    [SerializeField] float candyPopTime = 0.05f;

    [Header("Candy toss")]
    [SerializeField] CandySparkle sparkle;
    [SerializeField] float candyShowTime = 0.1f;
    [SerializeField] float tossTime = 0.32f;
    [SerializeField] float tossArc = 0.25f;
    [SerializeField] float tossSpin = 540f;

    public bool ThumbsUpNext { get; set; }
    public Func<bool> CanAct;
    public event Action<HandAction> Performed;
    public Func<bool> CanBlast;
    public event Action Blasted;

    public bool Busy { get; private set; }

    public Func<Vector3?> TossTarget;

    Vector3 candyScale = Vector3.one;
    Transform candyParent;
    Vector3 candyLocalPosition;
    Quaternion candyLocalRotation = Quaternion.identity;
    bool candyBusy;

    void Awake()
    {
        if (candy != null)
        {
            candyScale = candy.transform.localScale;
            candyParent = candy.transform.parent;
            candyLocalPosition = candy.transform.localPosition;
            candyLocalRotation = candy.transform.localRotation;
        }
        ResetPose();
    }

    public void SetVisible(bool visible)
    {
        handRenderer.enabled = visible;
    }

    public void ResetPose()
    {
        StopAllCoroutines();
        candyBusy = false;
        RestoreCandy();
        Busy = false;
        ShowIdle();
    }

    void Update()
    {
        if (Busy) return;
        if (CanAct != null && !CanAct()) return;

        if (GameInput.Consume(GameAction.Give)) StartCoroutine(Perform(HandAction.Give));
        else if (GameInput.Consume(GameAction.Reject)) StartCoroutine(Perform(HandAction.Reject));
        else if (Time.time >= blastReadyTime && GameInput.Consume(GameAction.Blast) && CanBlast != null && CanBlast())
            StartCoroutine(PerformBlast());
    }

    IEnumerator PerformBlast()
    {
        Busy = true;
        ThumbsUpNext = false;
        AudioManager.Play(Sfx.BlastCharge);
        yield return PlayClip(blastClip, blastHoldTime, false, () => Blasted?.Invoke());
        ShowIdle();
        blastReadyTime = Time.time + blastCooldown;
        Busy = false;
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

        if (withCandy) StartCoroutine(CandySequence());
        onPeak?.Invoke();

        for (float t = 0f; t < hold || candyBusy; t += Time.deltaTime)
        {
            int span = last - holdStart;
            int i = span > 0 ? holdStart + Mathf.RoundToInt(Mathf.PingPong(t * holdFps, span)) : holdStart;
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
        AudioManager.Play(Sfx.CandyAppear);
        if (sparkle != null) sparkle.Burst(candy.transform.position, 16, 0.55f);
        for (float t = 0f; t < candyPopTime; t += Time.deltaTime)
        {
            candy.transform.localScale = candyScale * EaseOutBack(t / candyPopTime);
            yield return null;
        }
        candy.transform.localScale = candyScale;
    }

    IEnumerator CandySequence()
    {
        candyBusy = true;
        yield return PopCandy();
        yield return new WaitForSeconds(candyShowTime);
        yield return TossCandy();
        candyBusy = false;
    }

    IEnumerator TossCandy()
    {
        Vector3? targetPoint = TossTarget != null ? TossTarget() : null;
        if (candy == null || !candy.gameObject.activeSelf || !targetPoint.HasValue) yield break;

        Transform ct = candy.transform;
        Vector3 start = ct.position;
        Quaternion startRotation = ct.rotation;
        Vector3 end = targetPoint.Value;
        ct.SetParent(null, true);
        AudioManager.Play(Sfx.CandyToss);

        float trailTimer = 0f;
        for (float t = 0f; t < tossTime; t += Time.deltaTime)
        {
            float e = t / tossTime;
            Vector3 position = Vector3.Lerp(start, end, e) + Vector3.up * (tossArc * 4f * e * (1f - e));
            ct.position = position;
            ct.rotation = startRotation * Quaternion.AngleAxis(tossSpin * t, Vector3.forward);

            trailTimer += Time.deltaTime;
            if (sparkle != null && trailTimer > 0.03f)
            {
                trailTimer = 0f;
                sparkle.Emit(position, UnityEngine.Random.insideUnitSphere * 0.15f, 0.35f, 0.7f);
            }
            yield return null;
        }

        if (sparkle != null) sparkle.Burst(end, 12, 0.45f, 0.8f);
        AudioManager.Play(Sfx.CandyLand);
        RestoreCandy();
        HideCandy();
    }

    void RestoreCandy()
    {
        if (candy == null || candyParent == null) return;
        var ct = candy.transform;
        if (ct.parent != candyParent) ct.SetParent(candyParent, false);
        ct.localPosition = candyLocalPosition;
        ct.localRotation = candyLocalRotation;
        ct.localScale = candyScale;
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
