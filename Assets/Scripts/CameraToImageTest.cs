using System;
using Unity.VisualScripting;
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

    private GameObject imageCaptureOverlay;

    private float targetAspectRatio = 2.0f; // 1.0f / 1.618f;
        
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        sceneCamera = Camera.main;
        capturedImage = capturedImageObject.GetComponent<RawImage>();
        aspectFitter = capturedImageObject.GetComponent<AspectRatioFitter>();
        aspectFitter.aspectRatio = targetAspectRatio;
        aspectFitter.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
    
        toCameraButton.onClick.AddListener(TryCaptureImage);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void TryCaptureImage()
    {
        toCameraButton.gameObject.SetActive(false);
        
        imageCaptureOverlay = Instantiate(imageCapturePrefab);
        CameraCapture cameraCapture = imageCaptureOverlay.GetComponent<CameraCapture>();
        
        cameraCapture.Initialize(targetAspectRatio, sceneCamera, this);
    }
    
    public void WriteCapturedImage(Texture texture)
    {
        capturedImage.texture = texture;

        Destroy(imageCaptureOverlay);
        imageCaptureOverlay = null;
        
        toCameraButton.gameObject.SetActive(true);
    }
}
