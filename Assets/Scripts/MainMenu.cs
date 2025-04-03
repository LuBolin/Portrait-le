using System;
using UnityEngine;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
    private const float BANNER_INFO_DURATION = 3f;
    
    private MasterController masterController;
    private GameObject mainContent;
    
    private Button startGameButton;
    private Button reloadDayButton;
    private Button shuffleRemoteButton;
    
    private void Start()
    {
        masterController = FindObjectOfType<MasterController>();
        mainContent = GameObject.Find("MainContent");
        
        startGameButton = GameObject.Find("StartGameButton").GetComponent<Button>();
        reloadDayButton = GameObject.Find("ReloadDayButton").GetComponent<Button>();
        shuffleRemoteButton = GameObject.Find("ShuffleRemoteButton").GetComponent<Button>();
        
        if (startGameButton != null)
            startGameButton.onClick.AddListener(OnStartGameButtonClicked);
        if (reloadDayButton != null)
            reloadDayButton.onClick.AddListener(OnReloadDayButtonClicked);
        if (shuffleRemoteButton != null)
            shuffleRemoteButton.onClick.AddListener(OnShuffleRemoteButtonClicked);
        
        masterController.SetMainMenu(this);
        
        mainContent.SetActive(false);
        OverlayBanner.Instance.HideBannerImmediately();
    }
    
    public void Restart()
    {
        gameObject.SetActive(true);
        mainContent.SetActive(false);
        OnReloadDayButtonClicked();
    }
    
    void OnStartGameButtonClicked()
    {
        mainContent.SetActive(true);
        gameObject.SetActive(false);
    }
    
    public async void OnReloadDayButtonClicked()
    {
        OverlayBanner.Instance.ShowBanner("Reloading Daily Portrait...", Color.yellow);
        try
        {
            await masterController.OnRefreshDailyPortrait();
            OverlayBanner.Instance.ShowBanner("Portrait reloaded successfully!", Color.green, BANNER_INFO_DURATION);
        }
        catch (Exception ex)
        {
            OverlayBanner.Instance.ShowBanner($"Reload failed: {ex.Message}", Color.red, BANNER_INFO_DURATION);
        }
    }


    async void OnShuffleRemoteButtonClicked()
    {
        OverlayBanner.Instance.ShowBanner("Shuffling Remote Daily Portrait...", Color.yellow);
        try
        {
            await masterController.OnShuffleRemoteDailyPortrait();
            OverlayBanner.Instance.ShowBanner("Remote portrait shuffled successfully!", Color.green, BANNER_INFO_DURATION);
        }
        catch (Exception ex)
        {
            OverlayBanner.Instance.ShowBanner($"Shuffle failed: {ex.Message}", Color.red, BANNER_INFO_DURATION);
        }
    }
    
    
    // void OnReloadDayButtonClicked()
    // {
    //     masterController.OnRefreshDailyPortrait();
    // }
    //
    // void OnShuffleRemoteButtonClicked()
    // {
    //     masterController.OnShuffleRemoteDailyPortrait();
    // }
}
