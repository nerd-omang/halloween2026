using System;
using System.Collections;
using UnityEngine;

public class Visitor : MonoBehaviour
{
    [Serializable]
    public struct WaddleStyle
    {
        public float speed;
        public float frequency;
        public float tilt;
        public float sway;
        public float hop;
    }

    public VisitorKind kind;
    [SerializeField] float bobHeight = 0.04f;
    [SerializeField] float turnSpeed = 360f;

    [Header("Waddle (queue advance)")]
    [SerializeField] WaddleStyle advance = new WaddleStyle { speed = 2.8f, frequency = 2.2f, tilt = 8f, sway = 5f, hop = 0.05f };

    [Header("Waddle (Kunmungchi leaving)")]
    [SerializeField] WaddleStyle relaxed = new WaddleStyle { speed = 1.6f, frequency = 1.6f, tilt = 8f, sway = 5f, hop = 0.05f };

    [Header("Waddle (Omangi leaving)")]
    [SerializeField] WaddleStyle frantic = new WaddleStyle { speed = 3f, frequency = 2.5f, tilt = 16f, sway = 10f, hop = 0.1f };

    [Header("Knockdown (palm blast on a plain visitor)")]
    [SerializeField] float knockFallTime = 0.5f;
    [SerializeField] float knockLieTime = 0.35f;
    [SerializeField] float knockGetUpTime = 0.45f;
    [SerializeField] float knockAngle = 110f;
    [SerializeField] float knockHop = 0.06f;
    [SerializeField] float knockPivotHeight = 0.38f;
    [SerializeField] float knockRollRadius = 0.3f;

    Vector3 basePos;
    Vector3 target;
    bool leaving;
    float knockTime;
    float bobSeed;
    float waddleTime;
    float moveBlend;
    float yaw = 180f;
    int lastStep = -1;

    public bool Arrived { get; private set; }
    public bool IsMoving { get; private set; }
    public bool IsCoveredBySheet => ghostSheet != null && ghostSheet.Covered;
    public bool IsDown { get; private set; }
    public SpecialGuest Special => GetComponent<SpecialGuest>(); // null for ordinary visitors

    GhostSheet ghostSheet;

    // The look the player sees right now (see OutfitApplier.Read).
    public Outfit CurrentOutfit => OutfitApplier.Read(this);

    public void KnockDown()
    {
        if (IsDown) return;
        IsDown = true;
        knockTime = 0f;
        StartCoroutine(PlayBounceSounds());
    }

    // The fall lands in three bounces; the times follow the EaseOutBounce landings so the sound matches the motion.
    IEnumerator PlayBounceSounds()
    {
        float[] landings = { 0.36f, 0.73f, 0.91f };
        float last = 0f;
        foreach (float landing in landings)
        {
            yield return new WaitForSeconds((landing - last) * knockFallTime);
            last = landing;
            AudioManager.Play(Sfx.VisitorBounce);
        }
    }

    public void BlowOffSheet(Vector3 direction)
    {
        if (ghostSheet != null) ghostSheet.BlowOff(direction);
    }

    void Awake()
    {
        ghostSheet = GetComponent<GhostSheet>();
        basePos = target = transform.position;
        bobSeed = UnityEngine.Random.value * 10f;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        Arrived = true;
    }

    public void Place(Vector3 p)
    {
        basePos = target = p;
        transform.position = p;
        Arrived = true;
    }

    public void SetTarget(Vector3 p)
    {
        target = p;
        Arrived = false;
    }

    public void Leave(Vector3 exit)
    {
        leaving = true;
        target = exit;
        Arrived = false;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        bool moving = (basePos - target).sqrMagnitude > 0.0001f;
        IsMoving = moving;
        WaddleStyle style = !leaving ? advance : (kind == VisitorKind.Omangi ? frantic : relaxed);

        moveBlend = Mathf.MoveTowards(moveBlend, moving ? 1f : 0f, dt * 6f);
        if (moving) waddleTime += dt;

        if (moving)
        {
            int step = Mathf.FloorToInt(waddleTime * style.frequency);
            if (step != lastStep)
            {
                lastStep = step;
                AudioManager.Play(Sfx.VisitorStep);
            }
        }

        float phase = waddleTime * style.frequency * Mathf.PI * 2f;
        float s = Mathf.Sin(phase);
        float c = Mathf.Cos(phase);

        if (leaving)
        {
            Vector3 dir = target - basePos;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
            {
                float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                yaw = Mathf.MoveTowardsAngle(yaw, targetYaw, turnSpeed * dt);
            }
        }

        if (moving)
        {
            float speed = style.speed * (0.4f + 1.2f * Mathf.Abs(s));
            basePos = Vector3.MoveTowards(basePos, target, speed * dt);
        }

        if ((basePos - target).sqrMagnitude < 0.0001f)
        {
            if (leaving)
            {
                Destroy(gameObject);
                return;
            }
            Arrived = true;
        }

        float hop = Mathf.Abs(s) * style.hop * moveBlend;
        float idleBob = (1f - moveBlend) * Mathf.Abs(Mathf.Sin(Time.time * 6f + bobSeed)) * bobHeight;
        float knockPitch = 0f;

        if (IsDown)
        {
            knockTime += dt;
            knockPitch = EvaluateKnockPitch(knockTime, out float knockLift);
            hop += knockLift;
            idleBob = 0f;
            if (knockTime >= knockFallTime + knockLieTime + knockGetUpTime) IsDown = false;
        }

        // Roll about the round body's center and travel as far as a ball of that radius would roll.
        Quaternion knockRotation = Quaternion.AngleAxis(knockPitch, Vector3.right);
        Vector3 pivot = Vector3.up * knockPivotHeight;
        Vector3 knockOffset = (pivot - knockRotation * pivot)
            + Vector3.forward * (knockRollRadius * knockPitch * Mathf.Deg2Rad);

        transform.position = basePos + Vector3.up * (hop + idleBob) + knockOffset;
        transform.rotation = knockRotation
            * Quaternion.Euler(0f, yaw + c * style.sway * moveBlend, s * style.tilt * moveBlend);
    }

    static float EaseOutBounce(float x)
    {
        const float n1 = 7.5625f;
        const float d1 = 2.75f;
        if (x < 1f / d1) return n1 * x * x;
        if (x < 2f / d1)
        {
            x -= 1.5f / d1;
            return n1 * x * x + 0.75f;
        }
        if (x < 2.5f / d1)
        {
            x -= 2.25f / d1;
            return n1 * x * x + 0.9375f;
        }
        x -= 2.625f / d1;
        return n1 * x * x + 0.984375f;
    }

    // Rolls backward (away from the player) like a ball, lies for a moment, then rolls back up with a small wobble.
    float EvaluateKnockPitch(float t, out float lift)
    {
        lift = 0f;
        if (t < knockFallTime)
        {
            float u = t / knockFallTime;
            lift = Mathf.Sin(u * Mathf.PI) * knockHop;
            return knockAngle * EaseOutBounce(u);
        }

        t -= knockFallTime;
        if (t < knockLieTime)
        {
            return knockAngle + Mathf.Sin(t * 30f) * 2f * (1f - t / knockLieTime);
        }

        t -= knockLieTime;
        float g = Mathf.Clamp01(t / knockGetUpTime);
        float smooth = g * g * (3f - 2f * g);
        float wobble = Mathf.Sin(g * Mathf.PI * 3f) * (1f - g) * 6f;
        return knockAngle * (1f - smooth) - wobble;
    }
}
