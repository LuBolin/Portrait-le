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

    private Transform buttonsHBox;
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

    private MasterController controller;
    
    void Awake()
    {
        Transform myTransform = gameObject.transform;
        Transform canvasTransform = myTransform.Find("Canvas");
        canvas = canvasTransform.GetComponent<Canvas>();
        
        // CameraFootage, then Buttons_HBox
        Transform cameraFootage = canvasTransform.Find("CameraFootage");
        rawImage = cameraFootage.GetComponent<RawImage>();
        aspectFitter = cameraFootage.GetComponent<AspectRatioFitter>();
        aspectFitter.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;

        buttonsHBox = canvasTransform.Find("ButtonsHBox");
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

        // isPortrait = Screen.orientation == ScreenOrientation.Portrait;
        // Debug.Log("Portrait: " + isPortrait);
        // currentAngle = GetDeviceYaw();
        // Debug.Log("Yaw: " + currentAngle);
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
        if (controller == null)
            return;

        controller.CameraGuessCallback(null);
    }
    
    void CaptureImage()
    {
        if (controller == null)
            return;
        
        if (webcamTexture == null || !webcamTexture.isPlaying)
        {
            ExitCamera();
            return;
        };

        RectTransform rectTransform = rawImage.rectTransform;
        Canvas canvas = rawImage.canvas;
        Camera cam = canvas.worldCamera;
        
        Vector3[] corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);

        // Convert world corners to screen space
        Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 topRight = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);

        int width = Mathf.RoundToInt(topRight.x - bottomLeft.x);
        int height = Mathf.RoundToInt(topRight.y - bottomLeft.y);

        if (width <= 0 || height <= 0)
        {
            Debug.LogWarning("RawImage is off-screen or too small to capture.");
            controller.CameraGuessCallback(null);
        }

        RenderTexture rt = new RenderTexture(Screen.width, Screen.height, 24);
        Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false);

        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;

        // Read pixels from screen space
        result.ReadPixels(new Rect(bottomLeft.x, bottomLeft.y, width, height), 0, 0);
        result.Apply();

        cam.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);
        
        controller.CameraGuessCallback(result);
    }
    
    void SwitchCamera()
    {
        currentCameraIndex += 1;
        currentCameraIndex %= cameras.Length;
        LaunchCamera(currentCameraIndex);
    }

    void LaunchCamera(int index)
    {
        Debug.Log("Launch camera called on index: " + index);
        cameraReady = false;
        // stop previous camera
        if (webcamTexture != null && webcamTexture.isPlaying)
            webcamTexture.Stop();
        
        int prevIndex = currentCameraIndex;
        
        currentCameraIndex = index % cameras.Length;
        WebCamDevice device = cameras[currentCameraIndex];
        string camName = device.name;
        webcamTexture = new WebCamTexture(camName);
        webcamTexture.Play();
        
        StartCoroutine(CropCameraTextureAndReady());
      }
    
    private IEnumerator CropCameraTextureAndReady()
    {
        Debug.Log("CropCameraTextureAndReady called");
        
        float timeout = 5f; // max time to wait, in seconds
        float timer = 0f;
        // Busy wait for the webcam texture to initialize
        while (webcamTexture.width <= 100 && timer < timeout)
        {
            Debug.Log("Current webcam texture dimensions: " + webcamTexture.width + "x" + webcamTexture.height);
            Debug.Log("Webcam is playing: " + webcamTexture.isPlaying);
            if (!webcamTexture.isPlaying)
                webcamTexture.Play();
            timer += Time.deltaTime;
            yield return null;
        }

        if (webcamTexture.width <= 100 || !webcamTexture.isPlaying)
        {
            Debug.LogWarning("Webcam failed to initialize within timeout.");
            // controller.CameraGuessCallback(null);
            yield break;
        }
        
        // Debug.Log("Current camera texture: " + webcamTexture);
        // Debug.Log("Current camera texture is playing: " + webcamTexture.isPlaying);
        // Debug.Log("Current camera texture width: " + webcamTexture.width);
        // Debug.Log("Current camera texture height: " + webcamTexture.height);
        

        float textureRatio = (float)webcamTexture.width / webcamTexture.height;
        textureRatio = 1.0f / textureRatio; // due to us rotating the texture
        
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

        rawImage.texture = webcamTexture;
        rawImage.material = rotationMaterial;
        // rotate 90 / -90 degrees depending on camera facing
        float angle = cameras[currentCameraIndex].isFrontFacing ? -90f : 90f;
        rotationMaterial.SetFloat("_Rotation", angle);
        
        cameraReady = true;
        Debug.Log("Camera ready");
        
        // Debug.Log("Capture prefab position: " + transform.position);
        // RectTransform rectTransform = rawImage.rectTransform;
        // Debug.Log("rectTransform :" + rectTransform);
        // Canvas canvas = rawImage.canvas;
        // Debug.Log("Image canvas: " + canvas);
        // Camera cam = canvas.worldCamera;
        // Debug.Log("Image Camera: " + cam);
        // Vector3[] corners = new Vector3[4];
        // rectTransform.GetWorldCorners(corners);
        // Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        // Vector2 topRight = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
        // Debug.Log("RawImage bottom left: " + bottomLeft);
        // Debug.Log("RawImage top right: " + topRight);
    }

    
    #region Public methods

    public void Initialize(float aspectRatio, Camera canvasCamera, MasterController controller)
    {
        canvas.worldCamera = canvasCamera;
        this.controller = controller;
        
        // reset to false, incase initialization of camera fails
        initialized = false;
        
        targetAspectRatio = aspectRatio;

        #if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            AskCameraPermission();
        else
            InitializeCamera();
        #else
            InitializeCamera();
        #endif
        
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
    
    #if UNITY_ANDROID
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
    #endif
    
    #endregion
}
