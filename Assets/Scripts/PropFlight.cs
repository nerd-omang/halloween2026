using System.Collections.Generic;
using UnityEngine;

// Worn parts knocked off a visitor by a palm blast: detached, thrown along the blast and up, tumbling
// around their visual center, bouncing once and shrinking away, then parked back under the visitor,
// switched off, in their original pose. Shared by VisitorDisguise (fake props) and VisitorCostume;
// the owner forwards its Update and OnDestroy.
[System.Serializable]
public class PropFlight
{
    [SerializeField] float duration = 0.7f;
    [SerializeField] float speed = 1.6f;
    [SerializeField] float lift = 2.6f;
    [SerializeField] float spin = 720f;
    [SerializeField] float gravity = 9.8f;

    class Flight
    {
        public Transform prop;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
        public Vector3 center;      // world point it tumbles around
        public Vector3 centerInProp;
        public Quaternion startRotation;
        public Vector3 velocity;
        public Vector3 spinAxis;
        public float groundY;
        public bool bounced;
        public float time;
    }

    [System.NonSerialized] List<Flight> flights;
    List<Flight> Flights => flights ??= new List<Flight>();

    // direction: camera -> visitor; only its horizontal part is used.
    public void Launch(Transform owner, Transform prop, Vector3 direction)
    {
        var r = prop.GetComponentInChildren<Renderer>();
        Vector3 center = r != null ? r.bounds.center : prop.position;
        Vector3 forward = Vector3.ProjectOnPlane(direction, Vector3.up);
        forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : -owner.forward;
        var flight = new Flight
        {
            prop = prop,
            localPosition = prop.localPosition,
            localRotation = prop.localRotation,
            localScale = prop.localScale,
            center = center,
            centerInProp = prop.InverseTransformPoint(center),
            velocity = forward * speed + Vector3.up * lift,
            spinAxis = Random.onUnitSphere,
            groundY = owner.position.y,
        };
        prop.SetParent(null, true); // fly free of the visitor (it is being knocked down)
        flight.startRotation = prop.rotation;
        Flights.Add(flight);
        AudioManager.Play(Sfx.PropPop);
    }

    public void Update(Transform owner)
    {
        for (int i = Flights.Count - 1; i >= 0; i--)
        {
            var f = flights[i];
            f.time += Time.deltaTime;
            f.velocity.y -= gravity * Time.deltaTime;
            f.center += f.velocity * Time.deltaTime;
            if (!f.bounced && f.velocity.y < 0f && f.center.y < f.groundY + 0.05f)
            {
                f.bounced = true;
                f.velocity = new Vector3(f.velocity.x * 0.5f, -f.velocity.y * 0.4f, f.velocity.z * 0.5f);
            }
            float t = Mathf.Clamp01(f.time / duration);
            float scale = t < 0.7f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.7f) / 0.3f);
            f.prop.rotation = Quaternion.AngleAxis(spin * f.time, f.spinAxis) * f.startRotation;
            f.prop.localScale = f.localScale * scale;
            f.prop.position = f.center - f.prop.rotation * Vector3.Scale(f.centerInProp, f.prop.lossyScale);
            if (t < 1f) continue;

            // Done: park it back under the visitor, switched off, in its original pose.
            f.prop.gameObject.SetActive(false);
            f.prop.SetParent(owner, false);
            f.prop.localPosition = f.localPosition;
            f.prop.localRotation = f.localRotation;
            f.prop.localScale = f.localScale;
            flights.RemoveAt(i);
        }
    }

    // Props in flight are not the visitor's children, so they would outlive a destroyed visitor.
    public void DestroyAll()
    {
        if (flights == null) return;
        foreach (var f in flights)
        {
            if (f.prop != null) Object.Destroy(f.prop.gameObject);
        }
        flights.Clear();
    }
}
