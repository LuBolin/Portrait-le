using System;
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
    
    // Truth related
    private Texture2D groundTruthImage;
    private string groundTruthName;
    private float groundTruthAspectRatio;
    
    // Guess related
    // 0, 2, 4... for guess, 1, 3, 5... for the parts that matched
    private Texture2D[] imageHistory = new Texture2D[2 * MAX_IMAGE_TRIES];
    private string[] nameHistory = new string[MAX_TEXT_TRIES];
    private int imageGuessesMade = 0;
    private int nameGuessesMade = 0;
    private Color[] transparentPixels; // helper
    private Texture2D currentUnionedMatchImage; // transparent means null
    private float currentMatchedPixelCount = 0;
    private float imageMatchPercentage = 0.0f;
    
    // UI elements
    private Canvas canvas;
    private RawImage mainImage;
    private Button toCameraButton;
    private TextField nameGuessInput;
    private GameObject cameraCaptureOPrefabInstance;
    
    
    void Start()
    {
        SetupUI();
    }

    void SetupUI()
    {
        Transform sceneRoot = transform.parent;
        canvas = sceneRoot.Find("Canvas").GetComponent<Canvas>();
        mainImage = sceneRoot.Find("MainImage").GetComponent<RawImage>();
        // get reference to the inputs
        
        // Uncomment the binding code below if we have the UI elements set up
        // toCameraButton.onClick.AddListener(OpenCamera);
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
    
    void SetTruth(Texture2D answerImage, string answerName)
    {
        groundTruthImage = answerImage;
        groundTruthName = answerName;
        
        groundTruthAspectRatio = (float)groundTruthImage.width / (float)groundTruthImage.height;
        AspectRatioFitter fitter = mainImage.gameObject.GetComponentInChildren<AspectRatioFitter>();
        fitter.aspectRatio = groundTruthAspectRatio;
        
        currentMatchedPixelCount = 0;
        imageHistory = new Texture2D[2 * MAX_IMAGE_TRIES];
        nameHistory = new string[MAX_TEXT_TRIES];
        imageGuessesMade = 0;
        nameGuessesMade = 0;
        
        // Transparent pixels implies un-set, similar to null
        transparentPixels = new Color[groundTruthImage.width * groundTruthImage.height];
        for (int i = 0; i < transparentPixels.Length; i++)
            transparentPixels[i] = new Color(0, 0, 0, 0);

        currentUnionedMatchImage = new Texture2D(groundTruthImage.width, groundTruthImage.height);
        currentUnionedMatchImage.SetPixels(transparentPixels);
        
        mainImage.texture = currentUnionedMatchImage;
    }
    
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
        
        if (imageMatchPercentage >= IMAGE_MATCH_PERCENTAGE_GOAL)
        {
            // Correct guess
        }
        else
        {
            imageHistory[imageGuessesMade * 2] = image;
            imageHistory[imageGuessesMade * 2 + 1] = matchingTexture;
            imageGuessesMade += 2;
        }
    }
    
    Texture2D VerifyImage(Texture2D image)
    {
        Color[] groundTruthPixels = groundTruthImage.GetPixels();
        Color[] guessPixels = image.GetPixels();
        Color[] matchingPixels = new Color[groundTruthImage.width * groundTruthImage.height];
        transparentPixels.CopyTo(matchingPixels, 0); // set all to "null"
        Color[] currentBestGuessPixels = currentUnionedMatchImage.GetPixels();
        
        for (int i = 0; i < groundTruthPixels.Length; i++)
        {
            Color groundTruthColor = groundTruthPixels[i];
            Color guessColor = guessPixels[i];

            // Convert RGB to HSV
            Color.RGBToHSV(groundTruthColor, out float groundTruthH, out float groundTruthS, out float groundTruthV);
            Color.RGBToHSV(guessColor, out float guessH, out float guessS, out float guessV);

            float hueTolerance = 0.1f; // 10%
            float saturationTolerance = 0.1f; // 10%
            float valueTolerance = 0.1f; // 10%
            
            float hueDiff = Mathf.Abs(groundTruthH - guessH);
            if (hueDiff > hueTolerance) continue;
            
            float saturationDiff = Mathf.Abs(groundTruthS - guessS);
            if (saturationDiff > saturationTolerance) continue;
            
            float valueDiff = Mathf.Abs(groundTruthV - guessV);
            if (valueDiff > valueTolerance) continue;

            Color currentGuess = currentBestGuessPixels[i];
            if (currentGuess.a == 0) // transparent means null
            {
                currentBestGuessPixels[i] = guessColor;
                currentMatchedPixelCount += 1;
            }
            matchingPixels[i] = guessColor;
        }

        imageMatchPercentage = currentMatchedPixelCount / groundTruthPixels.Length;
        Texture2D matchingTexture = new Texture2D(groundTruthImage.width, groundTruthImage.height);
        matchingTexture.SetPixels(matchingPixels);
        return matchingTexture;
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
    
    void OpenCamera()
    {
        toCameraButton.gameObject.SetActive(false);
        
        cameraCaptureOPrefabInstance = Instantiate(cameraCapturePrefab);
        CameraCapture cameraCapture = cameraCaptureOPrefabInstance.GetComponent<CameraCapture>();
        
        Camera sceneCamera = Camera.main;
        // cameraCapture.Initialize(groundTruthAspectRatio, sceneCamera, this);
        // TODO: modify CameraCapture to take in master controller class
    }
    
    // TODO: make CameraCapture call this function, instead of the previous WriteCapturedImage
    public void CameraGuessCallback(Texture2D texture)
    {
        if (texture != null) // May be null if the user cancels
            HandleImageInput(texture);

        Destroy(cameraCaptureOPrefabInstance);
        cameraCaptureOPrefabInstance = null;
        
        toCameraButton.gameObject.SetActive(true);
    }
}