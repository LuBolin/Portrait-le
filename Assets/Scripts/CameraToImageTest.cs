using UnityEngine;
using UnityEngine.UI;

public class CameraToImageTest : MonoBehaviour
{
    // variable for a prefab to be assigned
    public GameObject imageCapturePrefab;
    public GameObject capturedImageObject;
    public Button toCameraButton;
    
    private Camera sceneCamera;
    private RawImage capturedImage;
    private AspectRatioFitter aspectFitter;
    
    private float targetAspectRatio = 1.0f / 1.618f;
        
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        sceneCamera = Camera.main;
        capturedImage = capturedImageObject.GetComponent<RawImage>();
        aspectFitter = gameObject.GetComponent<AspectRatioFitter>();
        
        toCameraButton.onClick.AddListener(TryCaptureImage);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void TryCaptureImage()
    {
        toCameraButton.gameObject.SetActive(false);
        
        GameObject imageCaptureOverlay = Instantiate(imageCapturePrefab);
        CameraCapture cameraCapture = imageCaptureOverlay.GetComponent<CameraCapture>();
        
        cameraCapture.Initialize(targetAspectRatio, sceneCamera, this);
    }
    
    public void WriteCapturedImage(Texture texture)
    {
        // Set the aspect ratio of th
        capturedImage.texture = texture;
        aspectFitter.aspectRatio = targetAspectRatio;
        aspectFitter.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
        
        toCameraButton.gameObject.SetActive(true);
    }
}
