using UnityEngine;

// Circles a bat slowly around a point (in the parent's space), bobbing up and down and banking into the
// turn. The wing flap itself is in the EnvironmentProp shader (_FlapAmount), so this only moves the root.
public class BatFlight : MonoBehaviour
{
    [SerializeField] Vector3 center = new Vector3(0f, 6f, 18f);
    [SerializeField] float radius = 3f;
    [SerializeField] float angularSpeed = 35f;   // degrees per second, negative circles the other way
    [SerializeField] float bobHeight = 0.4f;
    [SerializeField] float bobSpeed = 1.3f;
    [SerializeField] float bankAngle = 22f;
    [SerializeField] float startAngle;

    public void Configure(Vector3 center, float radius, float angularSpeed, float startAngle)
    {
        this.center = center;
        this.radius = radius;
        this.angularSpeed = angularSpeed;
        this.startAngle = startAngle;
        Apply(0f);
    }

    void OnEnable()
    {
        Apply(Time.time);
    }

    void Update()
    {
        Apply(Time.time);
    }

    void Apply(float time)
    {
        float angle = (startAngle + angularSpeed * time) * Mathf.Deg2Rad;
        var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
        offset.y = Mathf.Sin(time * bobSpeed + startAngle) * bobHeight;
        transform.localPosition = center + offset;

        // Face along the circle, leaning into the turn.
        float direction = Mathf.Sign(angularSpeed);
        var tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * direction;
        transform.localRotation = Quaternion.LookRotation(tangent, Vector3.up) * Quaternion.Euler(0f, 0f, -bankAngle * direction);
    }
}
