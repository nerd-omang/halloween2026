using System;
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

    Vector3 basePos;
    Vector3 target;
    bool leaving;
    float bobSeed;
    float waddleTime;
    float moveBlend;
    float yaw = 180f;

    public bool Arrived { get; private set; }
    public bool IsMoving { get; private set; }

    void Awake()
    {
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
        transform.position = basePos + Vector3.up * (hop + idleBob);
        transform.rotation = Quaternion.Euler(0f, yaw + c * style.sway * moveBlend, s * style.tilt * moveBlend);
    }
}
