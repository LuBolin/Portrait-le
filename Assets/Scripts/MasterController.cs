using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
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
    private const int MAX_TEXT_TRIES = 10;
    private const float IMAGE_MATCH_PERCENTAGE_GOAL = 0.9f;
    private const float GOLDEN_RATIO = 1.618f;
    private const float GOLDEN_RATIO_INVERSE = 1.0f / GOLDEN_RATIO;
    private const float HUE_TOLERNCE = 0.1f; // eyes are sensitive to hue shift
    private const float SATURATION_TOLERANCE = 0.2f; // varies to lighting & transparency
    private const float VALUE_TOLERANCE = 0.2f; // varies to lighting & transparency
    
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
    private Canvas canvas;
    private RawImage mainImage;
    private Button toCameraButton;
    private Transform imageHistoryParent;
    private TMP_InputField guessNameInput;
    private Button guessNameButton;
    private GameObject cameraCaptureOPrefabInstance;
    
    // Database
    private const string PORTRAIT_DATA_PATH = "Portraits";
    private Dictionary<string, Texture2D> portraitData;
    private string[] portraitNames;

    // Firebase
    private FirebaseFirestore fbFirestore;
    
    void Start()
    {   
        SetupUI();

        SetupFirebase();
        
        // LoadPortraitsFromResources();

        // (string portraitName, Texture2D portraitTexture) = GetRandomPortrait();
        // SetTruth(portraitName, portraitTexture);
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

    
    #region Setup
 
    void SetupUI()
    {
        canvas = GameObject.Find("Canvas").GetComponent<Canvas>();
        mainImage = GameObject.Find("MainImage").GetComponent<RawImage>();
        toCameraButton = GameObject.Find("ToCameraButton").GetComponent<Button>();
        imageHistoryParent = GameObject.Find("ImageHistoryParent").transform;
        guessNameInput = GameObject.Find("NameGuessInput").GetComponent<TMP_InputField>();
        guessNameButton = GameObject.Find("NameGuessButton").GetComponent<Button>();
        
        // IsNullCheck(canvas);
        // IsNullCheck(mainImage);
        // IsNullCheck(toCameraButton);
        // IsNullCheck(imageHistoryParent);
        // IsNullCheck(guessNameInput);
        
        if (canvas == null
            || mainImage == null
            || toCameraButton == null
            || imageHistoryParent == null
            || guessNameInput == null)
            return;

        for (int i = 0; i < imageHistoryParent.childCount; i++)
        {
            imageHistory[i] = imageHistoryParent.GetChild(i).GetComponent<InputImageHistory>();
        }
            
        
        toCameraButton.onClick.AddListener(OpenCamera);
        guessNameInput.onEndEdit.AddListener(MonitorNameGuessInputEmptuness);
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

        StartCoroutine(LoadTodayPortrait());
    }

    public void UpdateDailyPortraitButton() {
        // for daily reset
        // StartCoroutine(UpdateDailyPortrait());

        // for debugging (automatically overrides current day's file such that there is no need to delete cache)
        StartCoroutine(UpdateAndSet());
    }
    
    IEnumerator UpdateAndSet() {
        yield return StartCoroutine(UpdateDailyPortrait());
        yield return new WaitForSeconds(1f);
        yield return StartCoroutine(DownloadAndCache(DateTime.Today.ToString("yyyy-MM-dd")));
        yield return new WaitForSeconds(1f);
        yield return StartCoroutine(LoadTodayPortrait());
    }
    
    IEnumerator LoadTodayPortrait() {
        string todayKey = DateTime.Today.ToString("yyyy-MM-dd");      
        // 1. Check cache first
        (Texture2D, string) cachedData = TryGetCachedImage(todayKey);
        if (cachedData.Item1 != null && cachedData.Item2 != null)
        {
            SetTruth(cachedData.Item2, cachedData.Item1);
            yield break;
        }

        // 2. Not cached - download fresh
        yield return StartCoroutine(DownloadAndCache(todayKey));
        
        // 3. Verify download succeeded by checking cache again
        (Texture2D, string) newData = TryGetCachedImage(todayKey);
        if (newData.Item1 != null && newData.Item2 != null)
        {
            SetTruth(newData.Item2, newData.Item1);
        }
        else
        {
            Debug.LogError("Failed to download and cache today's portrait");
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
    
    IEnumerator DownloadAndCache(string dateKey) {
        // get the document named "today" from "/portraits"
        DocumentReference portraitsRef = fbFirestore.Collection(FB_COLLECTION).Document(FB_TODAY_DOCUMENT);
        Debug.Log("fetching firebase stuff");
        var tcs = new TaskCompletionSource<DocumentSnapshot>();
            
        // Start the Firestore request
        portraitsRef.GetSnapshotAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted) {
                Debug.LogError("Firestore error: " + task.Exception);
                tcs.SetException(task.Exception);
            } else if (task.IsCanceled) {
                Debug.LogWarning("Firestore request canceled");
                tcs.SetCanceled();
            } else{
                tcs.SetResult(task.Result);
            }
        });

        yield return new WaitUntil(() => tcs.Task.IsCompleted);

        // Process the result
        if (tcs.Task.IsCompletedSuccessfully)
        {
            DocumentSnapshot snapshot = tcs.Task.Result;
            
            if (snapshot.Exists)
            {
                Debug.Log($"Document data for {snapshot.Id}:");
                Dictionary<string, object> portraitDict = snapshot.ToDictionary();
                
                foreach (var pair in portraitDict)
                {
                    Debug.Log($"{pair.Key}: {pair.Value}");
                }

                // Download the image
                yield return StartCoroutine(DownloadAndCache((string)portraitDict[FB_DOCUMENT_STORAGE], (string) portraitDict[FB_DOCUMENT_NAME], dateKey));
            }
            else
            {
                Debug.Log($"Document {snapshot.Id} does not exist!");
            }
        }
        else if (tcs.Task.IsFaulted)
        {
            Debug.LogError("Failed to get portrait: " + tcs.Task.Exception);
        }
        
    }
    
    // takes a URL and downloads image, saves to persistentDataPath/today.jpg
    IEnumerator DownloadAndCache(string mediaURL, string name, string dateKey){   
        string cacheDir = Path.Combine(Application.persistentDataPath, CACHE_FOLDER);
        string imagePath = Path.Combine(cacheDir, $"{dateKey}.jpg");
        string metaPath = Path.Combine(cacheDir, $"{dateKey}.meta");

        UnityWebRequest request = UnityWebRequestTexture.GetTexture(mediaURL);
        Texture2D downloadedTexture;
        yield return request.SendWebRequest();
        if(request.isNetworkError || request.isHttpError) 
            Debug.Log(request.error);
        else {
            Debug.Log(Application.persistentDataPath);

            if (!Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }

            downloadedTexture = ((DownloadHandlerTexture) request.downloadHandler).texture;
            File.WriteAllBytes(imagePath, downloadedTexture.EncodeToJPG());
            File.WriteAllText(metaPath, name);

            Debug.Log("download complete");
        }
    }
    
    public IEnumerator UpdateDailyPortrait() {
        // 1. Get all portrait documents
        Task<QuerySnapshot> getAllTask = fbFirestore.Collection(FB_COLLECTION).GetSnapshotAsync();
        yield return new WaitUntil(() => getAllTask.IsCompleted);

        if (getAllTask.IsFaulted) {
            Debug.LogError("Failed to get portraits: " + getAllTask.Exception);
            yield break;
        }

        QuerySnapshot allPortraits = getAllTask.Result;
        List<DocumentSnapshot> portraitsList = allPortraits.Documents.ToList();

        // Remove the daily document from the random selection
        portraitsList.RemoveAll(doc => doc.Id == FB_TODAY_DOCUMENT);

        if (portraitsList.Count == 0)
        {
            Debug.LogWarning("No portraits available for swapping");
            yield break;
        }

        // 2. Select a random portrait
        int randomIndex = UnityEngine.Random.Range(0, portraitsList.Count);
        DocumentSnapshot randomPortrait = portraitsList[randomIndex];
        Dictionary<string, object> randomData = randomPortrait.ToDictionary();

        // 3. Get current daily portrait data
        Task<DocumentSnapshot> getDailyTask = fbFirestore.Collection(FB_COLLECTION).Document(FB_TODAY_DOCUMENT).GetSnapshotAsync();
        yield return new WaitUntil(() => getDailyTask.IsCompleted);

        if (!getDailyTask.Result.Exists)
        {
            Debug.LogError("Daily portrait document doesn't exist");
            yield break;
        }

        Dictionary<string, object> dailyData = getDailyTask.Result.ToDictionary();

        // 4. Perform the swap
        Task swapTask1 = fbFirestore.Collection(FB_COLLECTION).Document(FB_TODAY_DOCUMENT).SetAsync(randomData);
        Task swapTask2 = fbFirestore.Collection(FB_COLLECTION).Document(randomPortrait.Id).SetAsync(dailyData);

        yield return new WaitUntil(() => swapTask1.IsCompleted && swapTask2.IsCompleted);

        if (swapTask1.IsCompletedSuccessfully && swapTask2.IsCompletedSuccessfully)
        {
            Debug.Log($"Successfully swapped daily with {randomPortrait.Id}");
        }
        else
        {
            Debug.LogError("Failed to complete swap: " + 
                         (swapTask1.Exception?.Message ?? swapTask2.Exception?.Message));
        }
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
            return;
        Debug.Log("Guess: " + guessText);
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
        imageHistory[imageGuessesMade].SetGuess(image, matchingTexture);
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
    
    void MonitorNameGuessInputEmptuness(string guessText)
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
        
        if (texture != null) // May be null if the user cancels
            HandleImageInput(texture);

        Destroy(cameraCaptureOPrefabInstance);
        cameraCaptureOPrefabInstance = null;
        
        canvas.gameObject.SetActive(true);
    }
    
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
    
    #endregion
}

