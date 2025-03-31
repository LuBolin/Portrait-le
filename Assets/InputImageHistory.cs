using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InputImageHistory : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    private static RawImage mainImage;
    private RawImage myImage;
    private Button myButton;
    private AspectRatioFitter myAspectRatioFitter;
    
    private Texture2D guessTexture;
    private Texture2D matchTexture;
    private Texture2D mainTexture;

    private bool initialized = false;
    
    void Awake()
    {
        MasterController controller = FindFirstObjectByType<MasterController>();
        mainImage = controller.mainImage;
        myImage = GetComponent<RawImage>();
        myAspectRatioFitter = GetComponent<AspectRatioFitter>();
    }

    public void SetAspectRatio(float aspectRatio)
    {
        myAspectRatioFitter.aspectRatio = aspectRatio;
    }
    
    public void SetGuess(Texture2D guessTexture, Texture2D matchTexture)
    {
        this.guessTexture = guessTexture;
        this.matchTexture = matchTexture;
        myImage.texture = this.guessTexture;
        initialized = true;
    }

    public void ClearGuess()
    {
        guessTexture = null; 
        matchTexture = null;
        if (myImage)
            myImage.texture = null;
        initialized = false;
    }
    
    public void OnPointerDown(PointerEventData eventData)
    {
        if (!initialized) return;
        
        mainTexture = mainImage.texture as Texture2D;
        mainImage.texture = matchTexture;
    }
    
    public void OnPointerUp(PointerEventData eventData)
    {
        if (!initialized) return;
        
        mainImage.texture = mainTexture;
    }
    
    public void OnPointerClick(PointerEventData eventData)
    {
        OnPointerUp(eventData);
    }
}
