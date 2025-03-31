using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UI.Button;


public class MasterController : MonoBehaviour
{
    public GameObject cameraCapturePrefab;
    
    // Constants
    private const int MAX_IMAGE_TRIES = 5;
    private const int MAX_TEXT_TRIES = 10;
    private const float IMAGE_MATCH_PERCENTAGE_GOAL = 0.9f;
    private const float GOLDEN_RATIO = 1.618f;
    private const float GOLDEN_RATIO_INVERSE = 1.0f / GOLDEN_RATIO;
    private const float HUE_TOLERNCE = 0.1f; // eyes are sensitive to hue shift
    private const float SATURATION_TOLERANCE = 0.2f; // varies to lighting & transparency
    private const float VALUE_TOLERANCE = 0.2f; // varies to lighting & transparency
    
    //  For Debug
    [SerializeField] public bool canSeeGroundTruth;
    
    // Truth related
    private Texture2D groundTruthImage;
    private string groundTruthName;
    private float groundTruthAspectRatio = GOLDEN_RATIO_INVERSE;
    
    // Guess related
    // 0, 2, 4... for guess, 1, 3, 5... for the parts that matched
    private Texture2D[] imageHistory = new Texture2D[2 * MAX_IMAGE_TRIES];
    private RawImage[] imageHistoryRawImages = new RawImage[MAX_IMAGE_TRIES];
    private string[] nameHistory = new string[MAX_TEXT_TRIES];
    private int imageGuessesMade = 0;
    private int nameGuessesMade = 0;
    private Color[] transparentPixels; // helper
    private Texture2D currentUnionedMatchImage; // transparent means null
    private float currentUnionedMatchedPixelCount = 0;
    private float imageMatchPercentage = 0.0f;
    
    
    
    // UI elements
    public Canvas canvas;
    public RawImage mainImage;
    public Button toCameraButton;
    public Transform imageHistoryRawImagesParent;
    // public TextField nameGuessInput;
    private GameObject cameraCaptureOPrefabInstance;
    
    // Database
    private const string PORTRAIT_DATA_PATH = "Portraits";
    private Dictionary<string, Texture2D> portraitData;
    private string[] portraitNames;
    
    void Start()
    {   
        SetupUI();
        
        LoadPortraitsFromResources();

        (string portraitName, Texture2D portraitTexture) = GetRandomPortrait();
        SetTruth(portraitName, portraitTexture);
    }

    
    void EndGame(bool isWon)
    {
        if (isWon)
        {
            Debug.Log("Won!");
            Debug.Log("The answer is: " + groundTruthName);
        }
        else
        {
            Debug.Log("Lost!");
            Debug.Log("The answer is: " + groundTruthName);
        }
    }
    
    
    // Logic
    void SetTruth(string answerName, Texture2D answerImage)
    {
        Debug.Log("Setting truth to: " + answerName);
        groundTruthName = answerName;
        groundTruthImage = answerImage;
        
        groundTruthAspectRatio = (float)groundTruthImage.width / (float)groundTruthImage.height;
        AspectRatioFitter fitter = mainImage.gameObject.GetComponentInChildren<AspectRatioFitter>();
        fitter.aspectRatio = groundTruthAspectRatio;
        
        //  for ethan debugging
        if (canSeeGroundTruth) {
            mainImage.texture = answerImage;
            return ;
        }

        currentUnionedMatchedPixelCount = 0;
        imageHistory = new Texture2D[2 * MAX_IMAGE_TRIES];
        nameHistory = new string[MAX_TEXT_TRIES];
        for (int i = 0; i < MAX_IMAGE_TRIES; i++)
        {
            RawImage imageHistoryRawImage = imageHistoryRawImages[i];
            AspectRatioFitter historyFitter = imageHistoryRawImage.gameObject.GetComponentInChildren<AspectRatioFitter>();
            historyFitter.aspectRatio = groundTruthAspectRatio;
            imageHistoryRawImage.texture = null;
        }
        imageGuessesMade = 0;
        nameGuessesMade = 0;
        
        // Transparent pixels implies un-set, similar to null
        transparentPixels = new Color[groundTruthImage.width * groundTruthImage.height];
        for (int i = 0; i < transparentPixels.Length; i++)
            transparentPixels[i] = new Color(0, 0, 0, 0);

        currentUnionedMatchImage = new Texture2D(groundTruthImage.width, groundTruthImage.height);
        currentUnionedMatchImage.SetPixels(transparentPixels);
        currentUnionedMatchImage.Apply();
        
        mainImage.texture = currentUnionedMatchImage;
    }
    
    Texture2D VerifyImage(Texture2D image)
    {
        if (groundTruthImage == null)
            return null;
        
        // scale to dimensions
        image = ScaleTexture(image, groundTruthImage);
        Color[] groundTruthPixels = groundTruthImage.GetPixels();
        Color[] guessPixels = image.GetPixels();
        Color[] matchingPixels = new Color[groundTruthImage.width * groundTruthImage.height];
        transparentPixels.CopyTo(matchingPixels, 0); // set all to "null"
        Color[] currentBestGuessPixels = currentUnionedMatchImage.GetPixels();
        
        // Debug.Log("GroundTruth Pixels: " + groundTruthPixels.Length);
        // Debug.Log("Guess Pixels: " + guessPixels.Length);
        // Debug.Log("Matching Pixels: " + matchingPixels.Length);
        // Debug.Log("Current Best Guess Pixels: " + currentBestGuessPixels.Length);
        // Debug.Log("Guess Dimensions: " + image.width + " x " + image.height);
        // Debug.Log("Ground Truth Dimensions: " + groundTruthImage.width + " x " + groundTruthImage.height);
        
        int currentMatchedPixelCount = 0;
        for (int i = 0; i < groundTruthPixels.Length; i++)
        {
            Color groundTruthColor = groundTruthPixels[i];
            Color guessColor = guessPixels[i];

            // Convert RGB to HSV
            Color.RGBToHSV(groundTruthColor, out float groundTruthH, out float groundTruthS, out float groundTruthV);
            Color.RGBToHSV(guessColor, out float guessH, out float guessS, out float guessV);
            
            float hueDiff = Mathf.Abs(groundTruthH - guessH);
            if (hueDiff > HUE_TOLERNCE) continue;
            
            float saturationDiff = Mathf.Abs(groundTruthS - guessS);
            if (saturationDiff > SATURATION_TOLERANCE) continue;
            
            float valueDiff = Mathf.Abs(groundTruthV - guessV);
            if (valueDiff > VALUE_TOLERANCE) continue;

            Color currentGuess = currentBestGuessPixels[i];
            if (currentGuess.a == 0) // transparent means null
            {
                currentBestGuessPixels[i] = groundTruthColor;
                currentUnionedMatchedPixelCount += 1;
                currentMatchedPixelCount += 1;
            }
            matchingPixels[i] = Color.blue;
        }
        
        // Update MainImage
        currentUnionedMatchImage.SetPixels(currentBestGuessPixels);
        currentUnionedMatchImage.Apply();
        
        imageMatchPercentage = currentUnionedMatchedPixelCount / groundTruthPixels.Length;
        Texture2D matchingTexture = new Texture2D(groundTruthImage.width, groundTruthImage.height);
        matchingTexture.SetPixels(matchingPixels);
        matchingTexture.Apply();
        return matchingTexture;
    }
    
    Texture2D ScaleTexture(Texture2D source, Texture2D target)
    {
        int width = target.width;
        int height = target.height;
    
        RenderTexture rt = RenderTexture.GetTemporary(width, height);
        Graphics.Blit(source, rt);
    
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
    
        Texture2D scaled = new Texture2D(width, height, TextureFormat.RGBA32, false);
        scaled.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        scaled.Apply();
    
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
    
        return scaled;
    }
    
    
    // Setup
    void SetupUI()
    {
        // Transform sceneRoot = transform.parent;
        // canvas = sceneRoot.Find("Canvas").GetComponent<Canvas>();
        // mainImage = sceneRoot.Find("MainImage").GetComponent<RawImage>();
        // get reference to the inputs
        
        // assert all ui alements exist
        if (canvas == null
            || mainImage == null
            || toCameraButton == null
            || imageHistoryRawImagesParent == null)
            // || nameGuessInput == null)
            return;

        // iterate imageparent's children and assign to imageHistoryRawImages

        for (int i = 0; i < imageHistoryRawImagesParent.childCount; i++)
        {
            imageHistoryRawImages[i] = imageHistoryRawImagesParent.GetChild(i).GetComponent<RawImage>();
        }
            
        
        toCameraButton.onClick.AddListener(OpenCamera);
        // nameGuessInput.RegisterCallback<KeyDownEvent>(evt =>
        // {
        //     if (evt.keyCode == KeyCode.Return)
        //     {
        //         if (nameGuessInput.text == "")
        //             return;
        //         HandleTextInput(nameGuessInput.text);
        //         nameGuessInput.value = "";
        //     }
        // });
    }

    void LoadPortraitsFromResources()
    {
        // Note: In editor, set the resource's Read/Write to true
        // It is under the "Advanced" section
        Texture2D[] textures = Resources.LoadAll<Texture2D>(PORTRAIT_DATA_PATH);
        portraitData = new Dictionary<string, Texture2D>();
        portraitNames = new string[textures.Length];

        for (int i = 0; i < textures.Length; i++)
        {
            string name = textures[i].name;
            portraitData[name] = textures[i];
            portraitNames[i] = name;
            
            Debug.Log($"Portrait Name: {name}, Dimensions: {textures[i].width} x {textures[i].height}");
        }
    }
    
    
    // Data Retrieval
    (string name, Texture2D texture) GetPortraitByIndex(int index)
    {
        if (index < 0 || index >= portraitNames.Length)
        {
            Debug.LogError("Index out of bounds.");
            return default;
        }

        string name = portraitNames[index];
        return (name, portraitData[name]);
    }

    (string name, Texture2D texture) GetPortraitByname(string name)
    {
        if (!portraitData.ContainsKey(name))
        {
            Debug.LogError($"Texture with name '{name}' not found.");
            return default;
        }

        return (name, portraitData[name]);
    }

    (string name, Texture2D texture) GetRandomPortrait()
    {
        int randomIndex = UnityEngine.Random.Range(0, portraitNames.Length);
        return GetPortraitByIndex(randomIndex);
    }
    
    // Input Handlers
    void HandleTextInput(string guessText)
    {
        if (nameGuessesMade >= MAX_TEXT_TRIES)
            return;
        
        if (guessText == groundTruthName)
        {
            
        }
        else
        {
            nameHistory[nameGuessesMade] = guessText;
            nameGuessesMade += 1;
        }
    }
    
    void HandleImageInput(Texture2D image)
    {
        if (imageGuessesMade >= MAX_IMAGE_TRIES)
            return;
        
        // VerifyImage updates imageMatchPercentage
        Texture2D matchingTexture = VerifyImage(image);
        
        // Update UI
        imageHistoryRawImages[imageGuessesMade].texture = image;
        
        imageHistory[imageGuessesMade * 2] = image;
        imageHistory[imageGuessesMade * 2 + 1] = matchingTexture;
        imageGuessesMade += 1;
        
        // Debug.Log("Image Match Percentage: " + imageMatchPercentage);
        // Debug.Log("Image Match Percentage Goal: " + IMAGE_MATCH_PERCENTAGE_GOAL);
        if (imageMatchPercentage >= IMAGE_MATCH_PERCENTAGE_GOAL)
        {
            currentUnionedMatchImage.SetPixels(groundTruthImage.GetPixels());
            currentUnionedMatchImage.Apply();
        }
        else
        {
            // Incorrect guess
        }
        
        if (imageGuessesMade == MAX_IMAGE_TRIES)
        {
            // Game over
            toCameraButton.onClick.RemoveAllListeners();
            toCameraButton.interactable = false;
        }
        
    }
    
    
    // Camera Capture Prefab Interactions
    void OpenCamera()
    {
        canvas.gameObject.SetActive(false);
        
        // second parameter is the parent object
        cameraCaptureOPrefabInstance = Instantiate(cameraCapturePrefab, transform.parent);
        CameraCapture cameraCapture = cameraCaptureOPrefabInstance.GetComponent<CameraCapture>();
        
        Camera sceneCamera = Camera.main;
        cameraCapture.Initialize(groundTruthAspectRatio, sceneCamera, this);
    }
    
    public void CameraGuessCallback(Texture2D texture)
    {
        bool isNull = texture == null;
        Debug.Log("Camera returned. IsNull: " + isNull);
        
        if (texture != null) // May be null if the user cancels
            HandleImageInput(texture);

        Destroy(cameraCaptureOPrefabInstance);
        cameraCaptureOPrefabInstance = null;
        
        canvas.gameObject.SetActive(true);
    }
}