using System.Collections;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif


public class CameraCapture : MonoBehaviour
{
    // rotationMaterial has RotateUVShader
    public Material rotationMaterial;
    
    private Canvas canvas;
    private RawImage rawImage;
    private AspectRatioFitter aspectFitter;
    
    private Button cancelButton;
    private Button captureButton;
    private Button rotateButton;
    
    private WebCamTexture webcamTexture;
    private WebCamDevice[] cameras;
    private int currentCameraIndex = 0;
    private bool initialized = false;
    private const float goldenRatio = 1.618f;
    private float targetAspectRatio = 1.0f / goldenRatio;
    private bool isGyroSupported = false;
    private bool isPortrait = true;
    private float currentAngle = 0f;
    private bool cameraReady = false;

    private CameraToImageTest tester;
    
    void Awake()
    {
        Transform myTransform = this.gameObject.transform;
        canvas = myTransform.GetChild(0).GetComponent<Canvas>();
        
        // CameraFootage, then Buttons_HBox
        Transform cameraFootage = myTransform.Find("CameraFootage");
        rawImage = cameraFootage.GetComponent<RawImage>();
        aspectFitter = cameraFootage.GetComponent<AspectRatioFitter>();
        aspectFitter.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;

        Transform buttonsHBox = myTransform.Find("ButtonsHBox");
        cancelButton = buttonsHBox.Find("CancelButton").GetComponent<Button>();
        captureButton = buttonsHBox.Find("CaptureButton").GetComponent<Button>();
        rotateButton = buttonsHBox.Find("RotateButton").GetComponent<Button>();
        
        if(cancelButton != null)
            cancelButton.onClick.AddListener(ExitCamera);
        if (captureButton != null)
            captureButton.onClick.AddListener(CaptureImage);
        if (rotateButton != null)
            rotateButton.onClick.AddListener(SwitchCamera);
        
                
        // note: ratio should not go below 0.6f, otherwise UI will overlap
        // Initialize(1.0f / goldenRatio,);
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

    void ExitCamera()
    {
        
        if (webcamTexture == null || !webcamTexture.isPlaying)
            return;

        if (tester == null)
            return;

        tester.WriteCapturedImage(null);
    }
    
    void CaptureImage()
    {
        if (webcamTexture == null || !webcamTexture.isPlaying)
            return;

        if (tester == null)
            return;

        tester.WriteCapturedImage(rawImage.mainTexture);
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
        WebCamDevice device = cameras[currentCameraIndex];
        string camName = device.name;
        Debug.Log("Launching camera: " + camName);
        webcamTexture = new WebCamTexture(camName);
        webcamTexture.Play();
        rawImage.texture = webcamTexture;
        
        rawImage.material = rotationMaterial;
        // rotate 90 / -90 degrees depending on camera facing
        float angle = cameras[currentCameraIndex].isFrontFacing ? -90f : 90f;
        rotationMaterial.SetFloat("_Rotation", angle);
        
        StartCoroutine(CropCameraTextureAndReady());
      }
    
    private IEnumerator CropCameraTextureAndReady()
    {
        // Wait for the webcam texture to initialize
        yield return new WaitUntil(() => webcamTexture.width > 100);

        float textureRatio = (float)webcamTexture.width / webcamTexture.height;
        textureRatio = 1.0f / textureRatio; // due to us rotating the texture
        // priont texture ratio
        if (textureRatio < targetAspectRatio)
        {
            // crop top and bottom
            float H = textureRatio / targetAspectRatio;
            rawImage.uvRect = new Rect(0f, (1f - H) / 2f, 1f, H);
        }
        else if (textureRatio > targetAspectRatio)
        {
            // crop left and right
            float W = targetAspectRatio / textureRatio;
            rawImage.uvRect = new Rect((1f - W) / 2f, 0f, W, 1f);
        }
        else
        {
            // no cropping needed
            rawImage.uvRect = new Rect(0f, 0f, 1f, 1f);
        }
        
        WebCamDevice device = cameras[currentCameraIndex];
        string camName = device.name;
        Debug.Log("Launching camera: " + camName);
        webcamTexture = new WebCamTexture(camName);
        webcamTexture.Play();
        rawImage.texture = webcamTexture;
        
        rawImage.material = rotationMaterial;
        // rotate 90 / -90 degrees depending on camera facing
        float angle = cameras[currentCameraIndex].isFrontFacing ? -90f : 90f;
        rotationMaterial.SetFloat("_Rotation", angle);
        
        cameraReady = true;
    }

    
    #region Public methods

    public void Initialize(float aspectRatio, Camera canvasCamera, CameraToImageTest tester)
    {
        canvas.worldCamera = canvasCamera;
        this.tester = tester;
        
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
