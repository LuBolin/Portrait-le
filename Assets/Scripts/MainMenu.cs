using UnityEngine;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
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
        
        mainContent.SetActive(false);
        OverlayBanner.Instance.HideBannerImmediately();
    }
    
    void OnStartGameButtonClicked()
    {
        mainContent.SetActive(true);
        gameObject.SetActive(false);
    }
    
    void OnReloadDayButtonClicked()
    {
        masterController.OnRefreshDailyPortrait();
    }
    
    void OnShuffleRemoteButtonClicked()
    {
        masterController.OnShuffleRemoteDailyPortrait();
    }
}
