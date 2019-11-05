using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Advertisements;
using UnityEngine.SceneManagement;

public class UnityAds : MonoBehaviour
{
    public static UnityAds Instance { get; set; }

    public  bool showingAd = false;

    private void Awake()
    {
        Instance = this;
    }
    public void ShowAd()
    {
        if (Advertisement.IsReady())
        {
            Advertisement.Show();
        }
    }
    public void ShowRewardedAd()
    {
        if (Advertisement.IsReady())
        {
             var options = new ShowOptions { resultCallback = HandleShowResult };
             Advertisement.Show("rewardedVideo", options);
        }
        else
        {
           //SceneManager.LoadScene("MainMenu");
           showingAd = true;
        }
    }

    private void HandleShowResult(ShowResult result)
    {
        switch (result)
        {
            case ShowResult.Finished:
                Debug.Log("The ad was successfully shown."); 
                QuestionManager.Instance.lives = 3;
                QuestionManager.Instance.Lives();
                QuestionManager.Instance.StartTimer();
               // QuestionManager.Instance.showingAd = false;
                break;
            case ShowResult.Skipped:
                Debug.Log("The ad was skipped before reaching the end.");
                break;
            case ShowResult.Failed:
                Debug.LogError("The ad failed to be shown.");
                break;
        }
    }
}
