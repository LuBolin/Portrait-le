using System.Collections;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif


public class CameraStreamTest : MonoBehaviour
{
    public Button rotateButton;
    private RawImage rawImage;
    private AspectRatioFitter aspectFitter;
    private WebCamTexture webcamTexture;
    private WebCamDevice[] cameras;
    private int currentCameraIndex = 0;
    private bool initialized = false;
    private float targetAspectRatio = 0.618f; // golden ratio
    private bool isGyroSupported = false;
    private bool isPortrait = true;
    private float currentAngle = 0f;
    private bool cameraReady = false;
    
    void Start()
    {
        rawImage = GetComponent<RawImage>();
        aspectFitter = GetComponent<AspectRatioFitter>();
        aspectFitter.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
        Debug.Log("RawImage dimensions: " + rawImage.rectTransform.rect.width + " x " + rawImage.rectTransform.rect.height);
        Initialize(1.0f);
        
        if (rotateButton != null)
            rotateButton.onClick.AddListener(SwitchCamera);
    }

    void Update()
    {
        if (!initialized || !cameraReady)
            return;

        isPortrait = Screen.orientation == ScreenOrientation.Portrait;
        Debug.Log("Portrait: " + isPortrait);
        currentAngle = GetDeviceYaw();
        Debug.Log("Yaw: " + currentAngle);
    }
    
    private float GetDeviceYaw() // Yaw is accountable for device orientation
    {
        if (!isGyroSupported)
            return 0f;
        
        Quaternion gyroRotation = Input.gyro.attitude;
        Debug.Log("Gyro rotation: " + gyroRotation);
        Vector3 gyroEuler = gyroRotation.eulerAngles;
        
        float yaw = gyroEuler.y;
        return yaw;
    }

    void SwitchCamera()
    {
        currentCameraIndex += 1;
        currentCameraIndex %= cameras.Length;
        LaunchCamera(currentCameraIndex);
    }

    void LaunchCamera(int index)
    {
        cameraReady = false;
        // stop previous camera
        if (webcamTexture != null && webcamTexture.isPlaying)
            webcamTexture.Stop();
        
        currentCameraIndex = index % cameras.Length;
        string camName = cameras[currentCameraIndex].name;
        Debug.Log("Launching camera: " + camName);
        webcamTexture = new WebCamTexture(camName);
        rawImage.texture = webcamTexture;
        webcamTexture.Play();

        cameraReady = true;
        
        // print rawImage's actual dimensions
        Debug.Log("RawImage dimensions: " + rawImage.rectTransform.rect.width + " x " + rawImage.rectTransform.rect.height);
    }
    
    
    #region Public methods

    public void Initialize(float aspectRatio) // golden ratio
    {
        // reset to false, incase initialization of camera fails
        initialized = false;
        
        targetAspectRatio = aspectRatio;
        
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            AskCameraPermission();
        else
            InitializeCamera();

        if (SystemInfo.supportsGyroscope)
        {
            Input.gyro.enabled = true;
            isGyroSupported = true;
        } 
        else
            Debug.LogWarning("Gyroscope not supported on this device.");
    }
    
    #endregion
    
    
    #region Camera helpers
    // Source: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/WebCamTexture.html
    
    private void InitializeCamera()
    {
        cameras = WebCamTexture.devices;
        
        if (cameras.Length == 0)
        {
            Debug.LogError("No cameras found");
            return;
        }
        
        foreach (WebCamDevice device in cameras)
        {
            string deviceName = device.name;
            bool isFrontFacing = device.isFrontFacing;
            Debug.Log($"Device name: {deviceName}, isFrontFacing: {isFrontFacing}");
        }
        
        // find first front facing (selfie) camera if possible
        bool frontFacingFound = false;
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i].isFrontFacing)
            {
                currentCameraIndex = i;
                frontFacingFound = true;
                break;
            }
        }
        if (!frontFacingFound)
            currentCameraIndex = 0;

        aspectFitter.aspectRatio = targetAspectRatio;
        
        LaunchCamera(currentCameraIndex);
        initialized = true;
    }
    
    
    // Android specific helpers
    private void AskCameraPermission()
    {
        var callbacks = new PermissionCallbacks();
        callbacks.PermissionDenied += PermissionCallbacksPermissionDenied;
        callbacks.PermissionGranted += PermissionCallbacksPermissionGranted;
        Permission.RequestUserPermission(Permission.Camera, callbacks);
    }
    
    private void PermissionCallbacksPermissionDenied(string permissionName)
    {
        Debug.LogWarning($"Permission {permissionName} Denied");
    }

    private void PermissionCallbacksPermissionGranted(string permissionName)
    {
        StartCoroutine(DelayedCameraInitialization());
    }
    
    private IEnumerator DelayedCameraInitialization()
    {
        yield return null;
        InitializeCamera();
    }
    #endregion
}
