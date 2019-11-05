using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class ResultManager : MonoBehaviour {
    public static ResultManager instance { get; set; }
    public GameObject resultPanel;
    public GameObject warningPanel;

    private void Awake()
    {
        instance = this;
    }
    void Start () {
		
	}
	
	void Update () {
        ShowWarningPanel();
	}
    public void WriteScoreOnThePanel()
    {
        GameObject.Find("ResultText").GetComponent<Text>().text = QuestionManager.Instance.score + " puan";// + "\n" + "Toplam Puan : " + PlayerPrefs.GetInt("totalScore");
    }
    public void NextBtn()
    {
        UnityAds.Instance.ShowRewardedAd();
        if(UnityAds.Instance.showingAd == false)
        {
            resultPanel.SetActive(false);
        }
    }
    public void MainMenuBtn()
    {
        SceneManager.LoadScene("MainMenu");  
    }
    private void ShowWarningPanel()
    {
        if (UnityAds.Instance.showingAd)
        {
            warningPanel.SetActive(true);
        }
    }
    public void CloseWarningPanel()
    {
        warningPanel.SetActive(false);
        SceneManager.LoadScene("MainMenu");
        UnityAds.Instance.showingAd = false;
    }
}
