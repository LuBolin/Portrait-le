using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class OverlayBanner : MonoBehaviour
{
    private Image overlayBackground;
    private Image overlayBanner;
    private TMP_Text bannerText;

    private Coroutine currentBannerRoutine;
    
    // Singleton instance
    private static OverlayBanner _instance;

    public static OverlayBanner Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<OverlayBanner>();
            }
            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        
        overlayBackground = GetComponent<Image>();
        overlayBanner = GameObject.Find("OverlayBanner").GetComponent<Image>();
        bannerText = GameObject.Find("InfoBannerText").GetComponent<TMP_Text>();
        
        SetBannerActive(false);
    }

    public void ShowBanner(string message, Color bannerColor, float duration)
    {
        if (currentBannerRoutine != null)
            StopCoroutine(currentBannerRoutine);

        currentBannerRoutine = StartCoroutine(ShowBannerRoutine(message, bannerColor, duration));
    }

    private IEnumerator ShowBannerRoutine(string message, Color bannerColor, float duration)
    {
        bannerText.text = message;
        overlayBanner.color = bannerColor;
        
        SetBannerActive(true);

        yield return new WaitForSeconds(duration);

        SetBannerActive(false);
        
        currentBannerRoutine = null;
    }

    private void SetBannerActive(bool active)
    {
        overlayBackground.enabled = active;
        overlayBanner.enabled = active;
        bannerText.enabled = active;
    }

    // Untimed operations
    
    public void ShowBanner(string message, Color bannerColor)
    {
        if (currentBannerRoutine != null)
            StopCoroutine(currentBannerRoutine);
        currentBannerRoutine = null;

        bannerText.text = message;
        overlayBanner.color = bannerColor;
        
        SetBannerActive(true);
    }
    
    public void HideBannerImmediately()
    {
        if (currentBannerRoutine != null)
        {
            StopCoroutine(currentBannerRoutine);
            currentBannerRoutine = null;
        }
        SetBannerActive(false);
    }
}