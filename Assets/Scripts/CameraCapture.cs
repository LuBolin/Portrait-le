using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif


public class CameraCapture : MonoBehaviour
{
    // rotationMaterial has RotateUVShader
    public Material rotationMaterial;
    public Texture2D shutterFramesResource;
    
    private const float GRAVITY_SCORE_TOLERANCE = 0.1f;
    
    private Canvas canvas;
    private RawImage rawImage;
    private AspectRatioFitter aspectFitter;

    private Button cancelButton;
    private Button captureButton;
    private Button flipButton;

    private Texture2D[] shutterFrames;
    private RawImage shutterOverlay;
    private float frameRate = 60f;
    private bool isShutterAnimating = false;
    private float shutterTimer = 0f;
    private int currentFrame = 0;
    private bool targetShutterState;
    
    private WebCamTexture webcamTexture;
    private WebCamDevice[] cameras;
    private int currentCameraIndex = 0;
    private bool initialized = false;
    private const float goldenRatio = 1.618f;
    private float targetAspectRatio = 1.0f / goldenRatio;
    private bool cameraReady = false;
    private AttitudeSensor attitudeSensor;
    private GravitySensor gravitySensor;
    
    private MasterController controller;
    
    void Awake()
    {
        GameObject canvasTransform = GameObject.Find("Canvas");
        canvas = canvasTransform.GetComponent<Canvas>();
        
        // CameraFootage, then Buttons_HBox
        GameObject cameraFootage = GameObject.Find("CameraFootage");
        rawImage = cameraFootage.GetComponent<RawImage>();
        aspectFitter = cameraFootage.GetComponent<AspectRatioFitter>();
        // aspectFitter.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
        aspectFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

        shutterOverlay = GameObject.Find("ShutterOverlay").GetComponent<RawImage>();
        shutterOverlay.gameObject.SetActive(false);
        
        cancelButton = GameObject.Find("CancelButton").GetComponent<Button>();
        captureButton = GameObject.Find("CaptureButton").GetComponent<Button>();
        flipButton = GameObject.Find("FlipButton").GetComponent<Button>();
        
        if(cancelButton != null)
            cancelButton.onClick.AddListener(ExitCamera);
        if (captureButton != null)
            captureButton.onClick.AddListener(CaptureImage);
        if (flipButton != null)
            flipButton.onClick.AddListener(SwitchCamera);

        LoadShutterFrames();

        // note: ratio should not go below 0.6f, otherwise UI will overlap
        // Initialize(1.0f / goldenRatio,);
    }

    void Update()
    {
        if (!initialized)
            return;
        
        ProcessShutter(GetPortraitnessScore());
    }
    
    float GetPortraitnessScore() 
    {
        Vector3 gravity = gravitySensor.gravity.value.normalized;
    
        // How much gravity aligns with portrait axis (Y)
        float portraitAlignment = Mathf.Abs(gravity.y);
        // How much gravity aligns with landscape axis (X)
        float landscapeAlignment = Mathf.Abs(gravity.x);
    
        // Score (1 = pure portrait, 0 = pure landscape)
        return Mathf.Clamp01(portraitAlignment - landscapeAlignment + 0.5f);
    }

    void LoadShutterFrames()
    {
        // shutter_frames_24 -> 24 frames
        string filename = shutterFramesResource.name;
        int framesCount = int.Parse(filename.Split('_').Last());
        shutterFrames = new Texture2D[framesCount];
        int width = shutterFramesResource.width / framesCount;
        int height = shutterFramesResource.height;
        for (int i = 0; i < framesCount; i++)
        {
            Texture2D frame = new Texture2D(width, height);
            Color[] pixels = shutterFramesResource.GetPixels(i * width, 0, width, height);
            frame.SetPixels(pixels);
            frame.Apply();
            shutterFrames[i] = frame;
        }
    }
    
    void ProcessShutter(float portraitness)
    {
        // if 1 - protraitness is more than tolerance, meaning we are not portrait enough,
        // have a shutter closing to cover the rawImage's area
        // else, open the shutter
        
        IEnumerator AnimateShutter(bool shouldOpen)
        {
            isShutterAnimating = true;
            shutterOverlay.gameObject.SetActive(true);

            bool shouldClose = !shouldOpen;
            
            if (shouldClose)
            {
                cancelButton.interactable = false;
                captureButton.interactable = false;
                flipButton.interactable = false;
            }
    
            int startFrame = shouldClose ? 0 : shutterFrames.Length - 1;
            int endFrame = shouldClose ? shutterFrames.Length - 1 : 0;
            int step = shouldClose ? 1 : -1;
    
            float period = 1f / frameRate;
            for (int i = startFrame; i != endFrame + step; i += step)
            {
                shutterOverlay.texture = shutterFrames[i];
                yield return new WaitForSeconds(period);
            }

            if (!shouldClose)
            {
                cancelButton.interactable = true;
                captureButton.interactable = true;
                flipButton.interactable = true;
            }
            
            isShutterAnimating = false;
            shutterOverlay.gameObject.SetActive(!targetShutterState);
        }
        
        bool shouldBeOpen = (1 - portraitness) <= GRAVITY_SCORE_TOLERANCE;
        Debug.Log("Portraitness score: " + portraitness + ", Should be open: " + shouldBeOpen);
    
        if (shouldBeOpen != targetShutterState && !isShutterAnimating)
        {
            targetShutterState = shouldBeOpen;
            StartCoroutine(AnimateShutter(shouldBeOpen));
        }
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
        // rectTransform.GetWorldCorners(corners);
        
        rectTransform.GetLocalCorners(corners);
    
        // Convert to world space without flip
        bool wasFlipped = Mathf.Abs(rawImage.rectTransform.localEulerAngles.y) > 90f;
        if (wasFlipped)
        {
            rawImage.rectTransform.localEulerAngles = Vector3.zero;
            rectTransform.GetWorldCorners(corners);
            rawImage.rectTransform.localEulerAngles = new Vector3(0, 180f, 0);
        }
        else
        {
            rectTransform.GetWorldCorners(corners);
        }
        

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
            // Debug.Log("Current webcam texture dimensions: " + webcamTexture.width + "x" + webcamTexture.height);
            // Debug.Log("Webcam is playing: " + webcamTexture.isPlaying);
            // if (!webcamTexture.isPlaying)
            //     webcamTexture.Play();
            
            timer += Time.deltaTime;
            yield return null;
        }

        if (webcamTexture.width <= 100 || !webcamTexture.isPlaying)
        {
            Debug.LogWarning("Webcam failed to initialize within timeout.");
            // controller.CameraGuessCallback(null);
            yield break;
        }
        

        float textureRatio = (float)webcamTexture.width / webcamTexture.height;
        textureRatio = 1.0f / textureRatio; // due to us rotating the texture
        
        Rect uvRect;
        if (textureRatio < targetAspectRatio)
        {
            // crop top and bottom
            float H = textureRatio / targetAspectRatio;
            uvRect = new Rect(0f, (1f - H) / 2f, 1f, H);
        }
        else if (textureRatio > targetAspectRatio)
        {
            // crop left and right
            float W = targetAspectRatio / textureRatio;
            uvRect = new Rect((1f - W) / 2f, 0f, W, 1f);
        }
        else
        {
            // no cropping needed
            uvRect = new Rect(0f, 0f, 1f, 1f);
        }

        
        // account for flipping
        Vector3 eulerAngles = rawImage.rectTransform.localEulerAngles;
        if (cameras[currentCameraIndex].isFrontFacing)
            eulerAngles.y = 180f;
        else
            eulerAngles.y = 0f;
        rawImage.rectTransform.localEulerAngles = eulerAngles;
        
        
        rawImage.uvRect = uvRect;
        
        rawImage.texture = webcamTexture;
        rawImage.material = rotationMaterial;
        // rotate 90 / -90 degrees depending on camera facing
        float angle = cameras[currentCameraIndex].isFrontFacing ? -90f : 90f;
        rotationMaterial.SetFloat("_Rotation", angle);
        
        cameraReady = true;
        Debug.Log("Camera ready");
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
        
        attitudeSensor = AttitudeSensor.current;
        if (attitudeSensor != null)
            InputSystem.EnableDevice(attitudeSensor);
        if (attitudeSensor == null || !attitudeSensor.enabled)
            attitudeSensor = null;
        if (attitudeSensor != null)
            Debug.Log("Attitude sensor IS supported on this device.");
        else
            Debug.LogWarning("Attitude sensor IS NOT supported on this device.");
        
        gravitySensor = GravitySensor.current;
        if (gravitySensor != null)
            InputSystem.EnableDevice(gravitySensor);
        if (gravitySensor == null || !gravitySensor.enabled)
            gravitySensor = null;
        if (gravitySensor != null)
            Debug.Log("Gravity sensor IS supported on this device.");
        else
            Debug.LogWarning("Gravity sensor IS NOT supported on this device.");
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
