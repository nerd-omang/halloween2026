using UnityEngine;

// Frames the edges of the view like a porch: two posts, a header beam with a drippy fascia, and corner
// brackets (with cobwebs), all set at a fixed depth in front of the camera. Positions are fitted to the
// camera's field of view and aspect, so the frame hugs the screen edges at any window shape and never
// reaches into the middle (the queue and the HUD stay clear). Without a camera it fits the game's default
// view (camera at (0, 1.6, 0), pitched 8 degrees down, 60 degree FOV, 16:9).
public class PorchFrame : MonoBehaviour
{
    [SerializeField] Transform postLeft;
    [SerializeField] Transform postRight;
    [SerializeField] Transform header;
    [SerializeField] Transform fascia;
    [SerializeField] Transform bracketLeft;
    [SerializeField] Transform bracketRight;
    [SerializeField] Transform webLeft;
    [SerializeField] Transform webRight;

    [Header("Layout (fractions of the half view at the porch depth)")]
    [SerializeField] float depth = 1f;
    [SerializeField] float postCenterX = 0.95f;    // post centers sit this far toward the screen edges
    [SerializeField] float postWidth = 0.3f;      // in half-view heights, so posts look the same width at any aspect
    [SerializeField] float headerBottomY = 0.92f;  // header lower edge; 1 is the top of the screen
    [SerializeField] float fasciaDrop = 0.07f;
    [SerializeField] float bracketSize = 0.2f;
    [SerializeField] float webSize = 0.32f;

    static readonly Vector3 DefaultCameraPosition = new Vector3(0f, 1.6f, 0f);
    static readonly Quaternion DefaultCameraRotation = Quaternion.Euler(8f, 0f, 0f);

    Camera fitted;
    Vector3 fittedPosition;
    Quaternion fittedRotation;
    float fittedAspect;
    float fittedFov;

    void Start()
    {
        FitToCamera();
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null)
            return;
        var t = cam.transform;
        if (cam != fitted || cam.aspect != fittedAspect || cam.fieldOfView != fittedFov
            || t.position != fittedPosition || t.rotation != fittedRotation)
            FitToCamera();
    }

    public void FitToCamera()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            Fit(DefaultCameraPosition, DefaultCameraRotation, 60f, 16f / 9f);
            return;
        }
        fitted = cam;
        fittedPosition = cam.transform.position;
        fittedRotation = cam.transform.rotation;
        fittedAspect = cam.aspect;
        fittedFov = cam.fieldOfView;
        Fit(fittedPosition, fittedRotation, fittedFov, fittedAspect);
    }

    public void Fit(Vector3 cameraPosition, Quaternion cameraRotation, float verticalFov, float aspect)
    {
        float halfH = Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad) * depth;
        float halfW = halfH * aspect;
        transform.SetPositionAndRotation(cameraPosition + cameraRotation * new Vector3(0f, 0f, depth), cameraRotation);
        transform.localScale = Vector3.one;

        float postW = postWidth * halfH;
        float postX = postCenterX * halfW;
        float topY = headerBottomY * halfH;
        float bracket = bracketSize * halfH;
        Place(postLeft, new Vector3(-postX, 0f, 0f), new Vector3(postW, 3f * halfH, 1f));
        Place(postRight, new Vector3(postX, 0f, 0f), new Vector3(postW, 3f * halfH, 1f));
        Place(header, new Vector3(0f, topY, 0f), new Vector3(2.4f * halfW, (1.3f - headerBottomY) * halfH, 1f));
        Place(fascia, new Vector3(0f, topY, 0f), new Vector3(2f * halfW, fasciaDrop * halfH, 1f));

        float innerX = postX - postW * 0.5f;
        Place(bracketLeft, new Vector3(-innerX, topY, -0.01f), new Vector3(bracket, bracket, 1f));
        Place(bracketRight, new Vector3(innerX, topY, -0.01f), new Vector3(-bracket, bracket, 1f));
        float web = webSize * halfH;
        Place(webLeft, new Vector3(-innerX, topY, -0.03f), new Vector3(web, -web, 1f));
        Place(webRight, new Vector3(innerX, topY, -0.03f), new Vector3(-web, -web, 1f));
    }

    void Place(Transform part, Vector3 localPosition, Vector3 localScale)
    {
        if (part == null)
            return;
        part.localPosition = localPosition;
        part.localRotation = Quaternion.identity;
        part.localScale = localScale;
    }
}
