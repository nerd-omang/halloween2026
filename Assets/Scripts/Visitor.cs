using UnityEngine;

public class Visitor : MonoBehaviour
{
    public VisitorKind kind;
    [SerializeField] float moveSpeed = 4f;
    [SerializeField] float bobHeight = 0.04f;

    Vector3 basePos;
    Vector3 target;
    bool leaving;
    float bobSeed;

    public bool Arrived { get; private set; }

    void Awake()
    {
        basePos = target = transform.position;
        bobSeed = Random.value * 10f;
        transform.rotation = Quaternion.Euler(0f, 180f, 0f);
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
        basePos = Vector3.MoveTowards(basePos, target, moveSpeed * Time.deltaTime);
        if ((basePos - target).sqrMagnitude < 0.0001f)
        {
            if (leaving)
            {
                Destroy(gameObject);
                return;
            }
            Arrived = true;
        }
        float bob = Mathf.Abs(Mathf.Sin(Time.time * 6f + bobSeed)) * bobHeight;
        transform.position = basePos + Vector3.up * bob;
    }
}
