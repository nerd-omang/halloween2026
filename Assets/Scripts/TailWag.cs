using UnityEngine;

// Wags a tail side to side around its base (the tail object's pivot, set in the FBX) about the
// visitor's up axis. Wags while the Visitor on this object stands in line and eases out while it
// walks; without a Visitor (preview scenes) it always wags.
public class TailWag : MonoBehaviour
{
    [SerializeField] Transform tail;
    [SerializeField] float amplitude = 20f;
    [SerializeField] float frequency = 0.4f;
    [SerializeField] float blendSpeed = 3f;

    Visitor visitor;
    Quaternion restRotation;
    float phase;
    float idleBlend = 1f;

    void Awake()
    {
        visitor = GetComponent<Visitor>();
        restRotation = tail.localRotation;
        phase = Random.value * Mathf.PI * 2f;
    }

    void Update()
    {
        bool idle = visitor == null || !visitor.IsMoving;
        idleBlend = Mathf.MoveTowards(idleBlend, idle ? 1f : 0f, blendSpeed * Time.deltaTime);
        float weight = Mathf.SmoothStep(0f, 1f, idleBlend);
        float angle = Mathf.Sin(Time.time * frequency * 2f * Mathf.PI + phase) * amplitude * weight;
        tail.localRotation = Quaternion.AngleAxis(angle, Vector3.up) * restRotation;
    }
}
