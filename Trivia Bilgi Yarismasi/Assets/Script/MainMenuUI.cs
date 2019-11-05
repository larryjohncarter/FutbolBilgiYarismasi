using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour {
    public static MainMenuUI Instance { get; set; }

    // public Text highScore;

    private void Awake()
    {
        Instance = this;
    }

    public void QuestionScene()
    {
        SceneManager.LoadScene("ChoiceScene");
    }
   /* public void Update()
    {
        GameObject.Find("HighScoreText").GetComponent<Text>().text = QuestionManager.Instance.totalScore.ToString();

    }*/
 

    public void ShowLeaderBoard()
    {
        LeaderBoardManager.instance.ShowLeaderboard();
    }
}
