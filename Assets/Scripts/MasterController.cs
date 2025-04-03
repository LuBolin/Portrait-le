using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Firebase.Firestore;
using Button = UnityEngine.UI.Button;
using System.Threading.Tasks;
using UnityEngine.Networking;
using Firebase.Extensions;
using System.IO;
using System.Linq;
using TMPro;


public class MasterController : MonoBehaviour
{
    public GameObject cameraCapturePrefab;
    
    // Constants
    private const int MAX_IMAGE_TRIES = 5;
    private const int MAX_TEXT_TRIES = 5;
    private const float IMAGE_MATCH_PERCENTAGE_GOAL = 0.8f;
    private const float GOLDEN_RATIO = 1.618f;
    private const float GOLDEN_RATIO_INVERSE = 1.0f / GOLDEN_RATIO;
    private const float HUE_TOLERNCE = 0.15f; // eyes are sensitive to hue shift
    private const float SATURATION_TOLERANCE = 0.3f; // varies to lighting & transparency
    private const float VALUE_TOLERANCE = 0.2f; // varies to lighting & transparency
    private const float BANNER_ENDGAME_DURATION = 5f;
    private const float BANNER_INFO_DURATION = 2.5f;
    
    private const string CACHE_FOLDER = "DailyImageCache";
    private const string FB_TODAY_DOCUMENT = "today";
    private const string FB_COLLECTION = "portraits" ;
    private const string FB_DOCUMENT_NAME = "name" ;
    private const string FB_DOCUMENT_STORAGE = "mediaURL" ;
    
    // Truth related
    private Texture2D groundTruthImage;
    private string groundTruthName;
    private float groundTruthAspectRatio = GOLDEN_RATIO_INVERSE;
    
    // Guess related
    // 0, 2, 4... for guess, 1, 3, 5... for the parts that matched
    private InputImageHistory[] imageHistory = new InputImageHistory[MAX_IMAGE_TRIES];
    private string[] nameHistory = new string[MAX_TEXT_TRIES];
    private int imageGuessesMade = 0;
    private int nameGuessesMade = 0;
    private Color[] transparentPixels; // helper
    private Texture2D currentUnionedMatchImage; // transparent means null
    private float currentUnionedMatchedPixelCount = 0;
    private float imageMatchPercentage = 0.0f;
    
    // UI elements
    private GameObject canvas;
    private RawImage mainImage;
    private GameObject backgroundQuestionMark;
    private Button toCameraButton;
    private Transform imageHistoryParent;
    private TMP_InputField guessNameInput;
    private Button guessNameButton;
    private GameObject cameraCaptureOPrefabInstance;
    private MainMenu mainMenu;
    
    // Database
    private const string PORTRAIT_DATA_PATH = "Portraits";
    private Dictionary<string, Texture2D> portraitData;
    private string[] portraitNames;

    // Firebase
    private FirebaseFirestore fbFirestore;
    
    // Mobile Keyboard
    private Vector2 originalCanvasPos;
    private bool keyboardIsVisible = false;
    private Coroutine canvasMovementCoroutine;
    private Vector2[] initialYPositions;
    
    
    void Awake()
    {   
        SetupUI();

        SetupFirebase();
    }

    void Update()
    {
        #if UNITY_ANDROID || UNITY_IOS
            if (TouchScreenKeyboard.visible && !keyboardIsVisible)
            {
                keyboardIsVisible = true;
                ShiftCanvasUp();
            }
            else if (!TouchScreenKeyboard.visible && keyboardIsVisible)
            {
                keyboardIsVisible = false;
                ResetCanvasPosition();
            }
        #endif
    }

    void EndGame(bool isWon)
    {
        string bannerMessage = "";
        Color bannerColor = Color.red;
        
        // disable guessing
        toCameraButton.interactable = false;
        guessNameButton.interactable = false;
        guessNameInput.interactable = false;
        
        if (isWon)
        {
            bannerMessage = "You won!\nThe answer is: " + groundTruthName;
            bannerColor = Color.green;
        }
        else
        {
            bannerMessage = "You lost!\nThe answer is: " + groundTruthName;
            bannerColor = Color.red;
        }
        
        OverlayBanner.Instance.ShowBanner(bannerMessage, bannerColor, BANNER_ENDGAME_DURATION);
        
        // wait for BANNER_ENDGAME_DURATION, then "reload" the game
        IEnumerator EndGameTransition()
        {
            yield return new WaitForSeconds(BANNER_ENDGAME_DURATION);
            
            mainMenu.Restart();
        }
        
        StartCoroutine(EndGameTransition());
    }
    
    
    #region Setup
 
    void SetupUI()
    {
        canvas = GameObject.Find("Canvas");
        mainImage = GameObject.Find("MainImage").GetComponent<RawImage>();
        backgroundQuestionMark = mainImage.transform.Find("QuestionMark").gameObject;
        toCameraButton = GameObject.Find("ToCameraButton").GetComponent<Button>();
        imageHistoryParent = GameObject.Find("ImageHistoryParent").transform;
        guessNameInput = GameObject.Find("GuessNameInput").GetComponent<TMP_InputField>();
        guessNameButton = GameObject.Find("GuessNameButton").GetComponent<Button>();
        
        IsNullCheck(canvas);
        IsNullCheck(mainImage);
        IsNullCheck(backgroundQuestionMark);
        IsNullCheck(toCameraButton);
        IsNullCheck(imageHistoryParent);
        IsNullCheck(guessNameInput);
        IsNullCheck(guessNameButton);
        
        if (canvas == null
            || mainImage == null
            || backgroundQuestionMark == null
            || toCameraButton == null
            || imageHistoryParent == null
            || guessNameInput == null)
            return;

        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        originalCanvasPos = canvasRect.anchoredPosition;
        
        for (int i = 0; i < imageHistoryParent.childCount; i++)
        {
            imageHistory[i] = imageHistoryParent.GetChild(i).GetComponent<InputImageHistory>();
        }
        
        // make backgroundQuestionMark render behind mainImage
        // even though it is a child of mainImage
        // aka change its parent to mainImage's parent
        backgroundQuestionMark.transform.SetParent(mainImage.transform.parent);
        backgroundQuestionMark.transform.SetAsFirstSibling();
        
        initialYPositions = new Vector2[canvasRect.childCount];
        for (int i = 0; i < canvasRect.childCount; i++)
        {
            RectTransform child = canvasRect.GetChild(i).GetComponent<RectTransform>();
            initialYPositions[i] = child.anchoredPosition;
        }
        
        toCameraButton.onClick.AddListener(OpenCamera);
        guessNameInput.onEndEdit.AddListener(MonitorNameGuessInputEmptyness);
        // guessNameInput.onValueChanged.AddListener((string guessText) =>
        // {
        //     MonitorNameGuessInputEmptyness(guessText);
        // });
        guessNameButton.interactable = false;
        guessNameButton.onClick.AddListener(() =>
        {
            string guessText = guessNameInput.text;
            guessNameInput.text = string.Empty;
            HandleTextInput(guessText);
        });
    }

    
    #endregion
    
    
    #region Firebase
    
    void SetupFirebase() {
        fbFirestore = FirebaseFirestore.DefaultInstance;

        // StartCoroutine(UpdateAndSet());
        UpdateAndSet();
    }

    public async Task OnRefreshDailyPortrait()
    {
        await UpdateAndSet(forceUpdate: true);
    }


    
    public async Task OnShuffleRemoteDailyPortrait()
    {
        await ShuffleRemoteDailyPortrait();
        await UpdateAndSet(forceUpdate: true);
    }


    
    private async Task UpdateAndSet(bool forceUpdate = false)
    {
        if (forceUpdate)
        {
            string cacheDir = Path.Combine(Application.persistentDataPath, CACHE_FOLDER);
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, true);
                Debug.Log("Cache cleared");
            }
            else
            {
                Debug.Log("No cache to clear");
            }
        }

        mainImage.color = new Color(0, 0, 0, 0); // transparent to show question mark
        string todayKey = DateTime.Today.ToString("yyyy-MM-dd");
        await DownloadAndCache(todayKey);
        await Task.Delay(1000);
        await LoadTodayPortrait();
        mainImage.color = Color.white; // no need for transparent color, the pixels are transparent themselve
    }

    
    private async Task LoadTodayPortrait()
    {
        string todayKey = DateTime.Today.ToString("yyyy-MM-dd");
        (Texture2D, string) cachedData = TryGetCachedImage(todayKey);

        if (cachedData.Item1 != null && cachedData.Item2 != null)
        {
            SetTruth(cachedData.Item2, cachedData.Item1);
            return;
        }

        await DownloadAndCache(todayKey);

        (Texture2D, string) newData = TryGetCachedImage(todayKey);
        if (newData.Item1 != null && newData.Item2 != null)
        {
            SetTruth(newData.Item2, newData.Item1);
        }
        else
        {
            Debug.LogError("Failed to download and cache today's portrait");
            throw new Exception("LoadTodayPortrait failed: DownloadAndCache didn't produce usable data.");
        }
    }


    private (Texture2D, string) TryGetCachedImage(string dateKey)
    {
        string imagePath = Path.Combine(Application.persistentDataPath, CACHE_FOLDER, $"{dateKey}.jpg");
        string metaPath = Path.Combine(Application.persistentDataPath, CACHE_FOLDER, $"{dateKey}.meta");

        if (File.Exists(imagePath) && File.Exists(metaPath))
        {
            try
            {
                byte[] imageData = File.ReadAllBytes(imagePath);
                Texture2D texture = new Texture2D(2, 2);
                texture.LoadImage(imageData);

                string metaData = File.ReadAllText(metaPath);
                
                Debug.Log($"Loaded cached image and text for {dateKey}");
                return (texture, metaData);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to load cached image: {e.Message}");
                return (null, null);
            }
        }
        return (null, null);
    }
    
    private async Task DownloadAndCache(string dateKey)
    {
        DocumentReference portraitsRef = fbFirestore.Collection(FB_COLLECTION).Document(FB_TODAY_DOCUMENT);
        Debug.Log("Fetching Firestore document");

        DocumentSnapshot snapshot = await portraitsRef.GetSnapshotAsync();

        if (!snapshot.Exists)
        {
            Debug.LogError($"Document {snapshot.Id} does not exist!");
            throw new Exception("Daily portrait Firestore document not found.");
        }

        Dictionary<string, object> portraitDict = snapshot.ToDictionary();
        await DownloadAndCache((string)portraitDict[FB_DOCUMENT_STORAGE], (string)portraitDict[FB_DOCUMENT_NAME], dateKey);
    }

    
    // takes a URL and downloads image, saves to persistentDataPath/today.jpg
    private async Task DownloadAndCache(string mediaURL, string name, string dateKey)
    {
        string cacheDir = Path.Combine(Application.persistentDataPath, CACHE_FOLDER);
        string imagePath = Path.Combine(cacheDir, $"{dateKey}.jpg");
        string metaPath = Path.Combine(cacheDir, $"{dateKey}.meta");

        using UnityWebRequest request = UnityWebRequestTexture.GetTexture(mediaURL);
        var operation = request.SendWebRequest();

        while (!operation.isDone)
            await Task.Yield();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Download error: {request.error}");
            throw new Exception("Image download failed.");
        }

        Texture2D downloadedTexture = ((DownloadHandlerTexture)request.downloadHandler).texture;

        if (!Directory.Exists(cacheDir))
            Directory.CreateDirectory(cacheDir);

        File.WriteAllBytes(imagePath, downloadedTexture.EncodeToJPG());
        File.WriteAllText(metaPath, name);

        Debug.Log("Download and caching complete");
    }

    
    public async Task ShuffleRemoteDailyPortrait()
    {
        QuerySnapshot allPortraits = await fbFirestore.Collection(FB_COLLECTION).GetSnapshotAsync();

        List<DocumentSnapshot> portraitsList = allPortraits.Documents.ToList();
        portraitsList.RemoveAll(doc => doc.Id == FB_TODAY_DOCUMENT);

        if (portraitsList.Count == 0)
        {
            Debug.LogWarning("No portraits available for swapping");
            throw new Exception("No other portraits available to shuffle.");
        }

        int randomIndex = UnityEngine.Random.Range(0, portraitsList.Count);
        DocumentSnapshot randomPortrait = portraitsList[randomIndex];
        Dictionary<string, object> randomData = randomPortrait.ToDictionary();

        DocumentSnapshot currentDaily = await fbFirestore.Collection(FB_COLLECTION).Document(FB_TODAY_DOCUMENT).GetSnapshotAsync();

        if (!currentDaily.Exists)
        {
            Debug.LogError("Daily portrait document doesn't exist");
            throw new Exception("Daily portrait not found.");
        }

        Dictionary<string, object> dailyData = currentDaily.ToDictionary();

        Task task1 = fbFirestore.Collection(FB_COLLECTION).Document(FB_TODAY_DOCUMENT).SetAsync(randomData);
        Task task2 = fbFirestore.Collection(FB_COLLECTION).Document(randomPortrait.Id).SetAsync(dailyData);

        await Task.WhenAll(task1, task2);

        Debug.Log($"Successfully swapped daily with {randomPortrait.Id}");
    }


    #endregion
    
    #region Logic
    void SetTruth(string answerName, Texture2D answerImage)
    {
        Debug.Log("Setting truth to: " + answerName);
        groundTruthName = answerName;
        groundTruthImage = answerImage;

        groundTruthAspectRatio = (float)groundTruthImage.width / (float)groundTruthImage.height;
        AspectRatioFitter fitter = mainImage.gameObject.GetComponentInChildren<AspectRatioFitter>();
        fitter.aspectRatio = groundTruthAspectRatio;
        // update questionmark's too
        backgroundQuestionMark.GetComponent<AspectRatioFitter>().aspectRatio = groundTruthAspectRatio;
        
        currentUnionedMatchedPixelCount = 0;
        nameHistory = new string[MAX_TEXT_TRIES];
        for (int i = 0; i < MAX_IMAGE_TRIES; i++)
        {
            imageHistory[i].ClearGuess();
            imageHistory[i].SetAspectRatio(groundTruthAspectRatio);
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
            matchingPixels[i] = guessColor;
        }
        
        Debug.Log("Loop completed");
        
        // Update MainImage
        currentUnionedMatchImage.SetPixels(currentBestGuessPixels);
        currentUnionedMatchImage.Apply();
        
        imageMatchPercentage = currentUnionedMatchedPixelCount / groundTruthPixels.Length;
        Texture2D matchingTexture = new Texture2D(groundTruthImage.width, groundTruthImage.height);
        matchingTexture.SetPixels(matchingPixels);
        matchingTexture.Apply();
        return matchingTexture;
    }
    
    #endregion
 

    
    #region Local Data Retrieval
    
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
            
            // Debug.Log($"Portrait Name: {name}, Dimensions: {textures[i].width} x {textures[i].height}");
        }
    }
    
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
    
    #endregion
    
    #region Input Handlers
    void HandleTextInput(string guessText)
    {
        if (nameGuessesMade >= MAX_TEXT_TRIES)
        {
            // IMAGE_MATCH_PERCENTAGE_GOAL
            string message = "Out of text tries!\nSee if you can get " + IMAGE_MATCH_PERCENTAGE_GOAL + "% image match!";
            Color bannerColor = Color.yellow;
            OverlayBanner.Instance.ShowBanner(message, bannerColor, BANNER_INFO_DURATION);
            return;
        }
        
        nameHistory[nameGuessesMade] = guessText;
        nameGuessesMade += 1;
        if (guessText.ToLower() == groundTruthName.ToLower())
        {
            EndGame(true);
        }
        else
        {
            if (nameGuessesMade >= MAX_TEXT_TRIES && imageGuessesMade >= MAX_IMAGE_TRIES)
            {
                EndGame(false);
            }
            else
            {
                string message = "Incorrect guess.\nYou have " + (MAX_TEXT_TRIES - nameGuessesMade) + " text tries left.";
                Color bannerColor = Color.red;
                OverlayBanner.Instance.ShowBanner(message, bannerColor, BANNER_INFO_DURATION);
            }
        }
    }
    
    void HandleImageInput(Texture2D image)
    {
        if (imageGuessesMade >= MAX_IMAGE_TRIES)
        {
            string message = "Out of image tries!\nSee if you can get the name right!";
            Color bannerColor = Color.yellow;
            OverlayBanner.Instance.ShowBanner(message, bannerColor, BANNER_INFO_DURATION);
            return;
        }
        
        float previousMatchPercentage = imageMatchPercentage;
        
        // VerifyImage updates imageMatchPercentage
        Texture2D matchingTexture = VerifyImage(image);
        
        // Update UI
        imageHistory[imageGuessesMade].SetGuess(image, matchingTexture);
        imageGuessesMade += 1;
        
        if (imageMatchPercentage >= IMAGE_MATCH_PERCENTAGE_GOAL)
        {
            currentUnionedMatchImage.SetPixels(groundTruthImage.GetPixels());
            currentUnionedMatchImage.Apply();
            EndGame(true);
        }
        else
        {
            if (nameGuessesMade >= MAX_TEXT_TRIES && imageGuessesMade >= MAX_IMAGE_TRIES)
            {
                EndGame(false);
            }
            else
            {
                // round image percentage to 2 decimal places
                float newProgress = imageMatchPercentage - previousMatchPercentage;
                float roundedPercentage = Mathf.Round(imageMatchPercentage * 10000f) / 100f;
                newProgress = Mathf.Round(newProgress * 10000f) / 100f;
                // string message = "Image match percentage: " + roundedPercentage + "%\nYou have " + (MAX_IMAGE_TRIES - imageGuessesMade) + " image tries left.";
                string message = "Image match percentage: " + imageMatchPercentage + "%";
                message += "\nGains this guess: " + newProgress + "%";
                message += "\nYou have " + (MAX_IMAGE_TRIES - imageGuessesMade) + " image tries left.";
                Color bannerColor = Color.red;
                OverlayBanner.Instance.ShowBanner(message, bannerColor, BANNER_INFO_DURATION);
            }
        }
    }
    
    void MonitorNameGuessInputEmptyness(string guessText)
    {
        if (guessText == string.Empty)
            guessNameButton.interactable = false;
        else
            guessNameButton.interactable = true;
    }
    
    #endregion
    
    #region Camera Capture Prefab Interactions
    
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
        
        // activate first, since OverlayBanner is in canvas, and HandleImageInput may need the banner
        canvas.gameObject.SetActive(true);
        
        if (texture != null) // May be null if the user cancels
            HandleImageInput(texture);

        Destroy(cameraCaptureOPrefabInstance);
        cameraCaptureOPrefabInstance = null;
    }
    
    #endregion
    
    
    #region Mobile Keyboard Handling
    void ShiftCanvasUp()
    {
        RectTransform inputFieldRect = guessNameInput.GetComponent<RectTransform>();
        Vector2 inputFieldMiddle = RectTransformUtility.WorldToScreenPoint(Camera.main, inputFieldRect.transform.position);
        float distFromBottom = inputFieldMiddle.y - (inputFieldRect.rect.height / 2);
        // screen space is from bottom left, no need to modify by Screen.height / 2
        float keyboardHeight = GetKeyboardHeight();

        // if (keyboardHeight <= distFromBottom) // no need to move
        //     return;
        
        Debug.Log("Keyboard height: " + keyboardHeight);
        Debug.Log("Dist from bottom: " + distFromBottom);
        
        Vector2 targetPos = new Vector2(0, keyboardHeight - distFromBottom);
        targetPos.y += 64; // small buffer
        
        if (canvasMovementCoroutine != null)
            StopCoroutine(canvasMovementCoroutine);
        StartCoroutine(MoveCanvas(originalCanvasPos + targetPos));
    }

    void ResetCanvasPosition()
    {
        if (canvasMovementCoroutine != null)
            StopCoroutine(canvasMovementCoroutine);
        StartCoroutine(MoveCanvas(originalCanvasPos));
    }

    IEnumerator MoveCanvas(Vector2 targetPos)
    {
        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        Vector2 startPos = canvasRect.anchoredPosition;
        float duration = 0.25f;
        float time = 0f;
        Debug.Log("Moving canvas to: " + targetPos);
        float deltaY = targetPos.y - startPos.y;
        
        while (time < duration)
        {
            time += Time.deltaTime;
            float currentOffset = Mathf.Lerp(0f, deltaY, time / duration);
            for (int i = 0; i < canvasRect.childCount; i++)
            {
                RectTransform child = canvasRect.GetChild(i).GetComponent<RectTransform>();
                Vector2 initialYPos = initialYPositions[i];
                child.anchoredPosition = new Vector2(initialYPos.x, initialYPos.y + currentOffset);
            }
            yield return null;
        }
        
        canvasRect.anchoredPosition = targetPos;
    }
    
    // IEnumerator MoveCanvas(Vector2 targetPos)
    // {
    //     RectTransform canvasRect = canvas.GetComponent<RectTransform>();
    //     Vector2 startPos = canvasRect.anchoredPosition;
    //     float duration = 0.25f;
    //     float time = 0f;
    //     Debug.Log("Moving canvas to: " + targetPos);
    //     while (time < duration)
    //     {
    //         time += Time.deltaTime;
    //         canvasRect.anchoredPosition = Vector2.Lerp(startPos, targetPos, time / duration);
    //         yield return null;
    //     }
    //     
    //     canvasRect.anchoredPosition = targetPos;
    // }
    
    #endregion
    
    
    
    
    #region Helpers
    
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

    
    void IsNullCheck(object obj)
    {
        if (obj == null)
            Debug.Log("Null");
        else
            Debug.Log(obj.ToString() + " is not null");
    }
    
    public RawImage GetMainImage()
    {
        return mainImage;
    }

    public void SetMainMenu(MainMenu mainMenu)
    {
        this.mainMenu = mainMenu;
    }
    
    int GetKeyboardHeight()
    {
        using(AndroidJavaClass UnityClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            AndroidJavaObject View = UnityClass.GetStatic<AndroidJavaObject>("currentActivity").Get<AndroidJavaObject>("mUnityPlayer").Call<AndroidJavaObject>("getView");

            using(AndroidJavaObject Rct = new AndroidJavaObject("android.graphics.Rect"))
            {
                View.Call("getWindowVisibleDisplayFrame", Rct);

                return Screen.height - Rct.Call<int>("height");
            }
        }
    }
    
    #endregion

}

