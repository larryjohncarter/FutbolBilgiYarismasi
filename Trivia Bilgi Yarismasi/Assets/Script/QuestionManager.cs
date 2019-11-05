using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LitJson;
using UnityEngine.UI;
using System.IO;
using System;
using UnityEngine.SceneManagement;

public class QuestionManager : MonoBehaviour
{

    public static QuestionManager Instance { get; set; }

    string filePath;
    string eldeEdilenFilePathString;
    JsonData sorularJsonData;
    public GameObject answerBtn;
    JsonData yazilanJsonData;
    bool clickAnswer;
    public int score;
    public int totalScore;
    int countQuestion;
    int numberQuestion;
    public GameObject resultPanel;
    public int turkLigiSorulari;
    public int uefaSorulari;
    public int dunyaFutbolSorulari;
    public GameObject questionManager;
    public int lives = 3;
    public GameObject firstLive, secondLive, thirdLive;
    int time = 11;
    int correcAnswers;
  //  public bool showingAd = false;
    public int highScore; 

    private void Awake()
    {
 
        Instance = this;
        turkLigiSorulari = 51;
        uefaSorulari = 1;
        dunyaFutbolSorulari = 1;
    }
    void Start()
    {
        // PlayerPrefs.DeleteAll();
        PlayerPrefs.DeleteKey("questionNumber");
        PlayerPrefs.DeleteKey("totalScore");
        countQuestion = PlayerPrefs.GetInt("questionNumber", 0);
        totalScore = PlayerPrefs.GetInt("totalScore", 0);
        numberQuestion = 0;
        GetJsonData();
        StartCoroutine("Timer");
    }

    // Update is called once per frame
    void Update()
    {
        CorrectAnswers();
    }
    public void GetJsonData()
    {
        filePath = System.IO.Path.Combine(Application.streamingAssetsPath, "triviaQuizGame.json"); //burasi editorde calisiyor
        filePath = Application.streamingAssetsPath + "/triviaQuizGame.json";
        StartCoroutine("Json");
    }
    IEnumerator Json()
    {

        if (filePath.Contains("://"))
        {
            WWW www = new WWW(filePath);
            yield return www;
            eldeEdilenFilePathString = www.text;
            sorularJsonData = JsonMapper.ToObject(eldeEdilenFilePathString);
        }
        else
        {
            eldeEdilenFilePathString = System.IO.File.ReadAllText(filePath);
            sorularJsonData = JsonMapper.ToObject(eldeEdilenFilePathString);
        }

        ShowQuestions();
    }
    private void ShowAd()
    {
            questionManager.GetComponent<UnityAds>().ShowAd();
       
    }
    public void ReturnMainMenu()
    {
        SceneManager.LoadScene("MainMenu");
    }
    public void ShowQuestions()
    {
        if (ManagerScene.Instance.selectedTurkLigi == true)
        {
            ShowTurkLigiQuestions();
        }

        if (ManagerScene.Instance.selectedUefakupalari == true)
        {
            ShowUefaQuestions();
        }
        if(ManagerScene.Instance.selectedDunyafutbolu == true)
        {
            ShowDunyaFutboluQuestions();
        }
    }
    void CorrectAnswers() //if player has 5 correct answers, gives them 1 life
    {
        if(lives < 3)
        {
            if (correcAnswers == 5)
            {
                lives = lives + 1;
                correcAnswers = 0;
                if (lives == 2)
                {
                    secondLive.GetComponent<Image>().color = Color.white;
                }
                if (lives == 3)
                {
                    firstLive.GetComponent<Image>().color = Color.white;
                }
            }
            else if (correcAnswers > 5)
            {
                correcAnswers = 0;
            }
        }
      
    }
    void HighScore()
    {
        if (totalScore > PlayerPrefs.GetInt("HighScore"))
        {
            highScore = totalScore;
            PlayerPrefs.SetInt("HighScore", highScore);
            PlayerPrefs.Save();
        }
        if (PlayerPrefs.GetInt("HighScore") >= totalScore)
        {
          //  Debug.Log("TEST");
            //return;
        }
    }
    void Result(string whichAnswer)
    {
        if (clickAnswer)
        {
            if (whichAnswer == "0")
            {
                GameObject.Find("Correct").GetComponent<Button>().image.color = Color.green;
                score += 10;
                totalScore += 10;
                correcAnswers += 1;
                GameObject.Find("ScoreText").GetComponent<Text>().text =  score.ToString();
                time = 10;
            }
            else
            {
                GameObject.Find("Wrong" + whichAnswer).GetComponent<Button>().image.color = Color.red;
                lives = lives - 1;
                time = 10;
            }
             clickAnswer = false;
             Invoke("ShowQuestions", 1.5f);
        }
       
    }
    void Result()
    {
        PlayerPrefs.SetInt("totalScore", totalScore);
        numberQuestion = 0;
        resultPanel.SetActive(true);
        ResultManager.instance.WriteScoreOnThePanel();
        StopCoroutine("Timer");
      //  LeaderBoardManager.instance.AddScoreToLeaderboard();
        
    }
    IEnumerator Timer()
    {
        while (time > 0)
        {
            time--;
            GameObject.Find("Timer").GetComponent<Text>().text = time + " sn";
            if (time <= 0 && clickAnswer)
            {
                lives = lives - 1;
                Invoke("ShowQuestions", 1.5f);
                time = 10;
            }
            yield return new WaitForSeconds(1.5f);
        }
    }
    public void Lives()
    {
        if (lives == 2)
        {
            firstLive.GetComponent<Image>().color = Color.red;
        }
        if (lives == 1)
        {
            secondLive.GetComponent<Image>().color = Color.red;
        }
        if (lives == 0)
        {
            thirdLive.GetComponent<Image>().color = Color.red;
        }
        else if (lives == 3)
        {
            firstLive.GetComponent<Image>().color = Color.white;
            secondLive.GetComponent<Image>().color = Color.white;
            thirdLive.GetComponent<Image>().color = Color.white;
         //   showingAd = false;

        }

        if (lives <= 0)
        {
           // showingAd = true;
            HighScore();
            Result();
        }
    }
    public void StartTimer()
    {
        StartCoroutine("Timer");
    }
    public void ShowTurkLigiQuestions()
    {

            clickAnswer = true;
            if (countQuestion >= sorularJsonData["Question"].Count)
            {
                countQuestion = 0;
            }

            GameObject.Find("Canvas/Panel/QuestionPanel").GetComponentInChildren<Text>().text = sorularJsonData["Question"][countQuestion]["question"].ToString();
            GameObject[] destroyAnswer = GameObject.FindGameObjectsWithTag("answerBtnTag");
            if (destroyAnswer != null)
            {
                for (int i = 0; i < destroyAnswer.Length; i++)
                {
                    DestroyImmediate(destroyAnswer[i]);
                }
            }
            for (int i = 0; i < 4; i++)
            {
                GameObject answerInst = Instantiate(answerBtn);
                answerInst.GetComponentInChildren<Text>().text = sorularJsonData["Question"][countQuestion]["answers"][i].ToString();
                Transform answerContainter = GameObject.Find("AnswerBox").GetComponent<Transform>();
                answerInst.transform.SetParent(answerContainter);
                answerInst.GetComponent<Transform>().localScale = new Vector3(1, 1, 1);
                answerInst.transform.SetSiblingIndex(UnityEngine.Random.Range(0, 3));

                string j = i.ToString();
                if (i == 0)
                {
                    answerInst.name = "Correct";
                    answerInst.GetComponent<Button>().onClick.AddListener(() => Result("0"));
                }
                else
                {
                    answerInst.name = "Wrong" + j;
                    answerInst.GetComponent<Button>().onClick.AddListener(() => Result(j));
               }
            }
            countQuestion++;
            numberQuestion++;

            if (numberQuestion >= turkLigiSorulari)
            {
                Result();
            }
        // StartCoroutine("Lives");
        Lives();

        // GameObject.Find("QuestionCount").GetComponent<Text>().text = numberQuestion + " / 50";
        PlayerPrefs.SetInt("questionNumber", countQuestion);
    }
    public void ShowUefaQuestions()
    {
        clickAnswer = true;
        if (countQuestion >= sorularJsonData["UEFAKupasi"].Count)
        {
            countQuestion = 0;
        }

        GameObject.Find("Canvas/Panel/QuestionPanel").GetComponentInChildren<Text>().text = sorularJsonData["UEFAKupasi"][countQuestion]["question"].ToString();
        GameObject[] destroyAnswer = GameObject.FindGameObjectsWithTag("answerBtnTag");
        if (destroyAnswer != null)
        {
            for (int i = 0; i < destroyAnswer.Length; i++)
            {
                DestroyImmediate(destroyAnswer[i]);
            }
        }
        for (int i = 0; i < 4; i++)
        {
            GameObject answerInst = Instantiate(answerBtn);
            answerInst.GetComponentInChildren<Text>().text = sorularJsonData["UEFAKupasi"][countQuestion]["answers"][i].ToString();
            Transform answerContainter = GameObject.Find("AnswerBox").GetComponent<Transform>();
            answerInst.transform.SetParent(answerContainter);
            answerInst.GetComponent<Transform>().localScale = new Vector3(1, 1, 1);
            answerInst.transform.SetSiblingIndex(UnityEngine.Random.Range(0, 3));

            string j = i.ToString();
            if (i == 0)
            {
                answerInst.name = "Correct";
                answerInst.GetComponent<Button>().onClick.AddListener(() => Result("0"));
            }
            else
            {
                answerInst.name = "Wrong" + j;
                answerInst.GetComponent<Button>().onClick.AddListener(() => Result(j));
            }
        }
        countQuestion++;
        numberQuestion++;


        if (numberQuestion >= uefaSorulari)
        {
            Result();
        }
        // GameObject.Find("QuestionCount").GetComponent<Text>().text = numberQuestion + " / 50";
        PlayerPrefs.SetInt("questionNumber", countQuestion);
    }
    private void ShowDunyaFutboluQuestions()
    {
        clickAnswer = true;
        if (countQuestion >= sorularJsonData["DunyaFutbolu"].Count)
        {
            countQuestion = 0;
        }

        GameObject.Find("Canvas/Panel/QuestionPanel").GetComponentInChildren<Text>().text = sorularJsonData["DunyaFutbolu"][countQuestion]["question"].ToString();
        GameObject[] destroyAnswer = GameObject.FindGameObjectsWithTag("answerBtnTag");
        if (destroyAnswer != null)
        {
            for (int i = 0; i < destroyAnswer.Length; i++)
            {
                DestroyImmediate(destroyAnswer[i]);
            }
        }
        for (int i = 0; i < 4; i++)
        {
            GameObject answerInst = Instantiate(answerBtn);
            answerInst.GetComponentInChildren<Text>().text = sorularJsonData["DunyaFutbolu"][countQuestion]["answers"][i].ToString();
            Transform answerContainter = GameObject.Find("AnswerBox").GetComponent<Transform>();
            answerInst.transform.SetParent(answerContainter);
            answerInst.GetComponent<Transform>().localScale = new Vector3(1, 1, 1);
            answerInst.transform.SetSiblingIndex(UnityEngine.Random.Range(0, 3));

            string j = i.ToString();
            if (i == 0)
            {
                answerInst.name = "Correct";
                answerInst.GetComponent<Button>().onClick.AddListener(() => Result("0"));
            }
            else
            {
                answerInst.name = "Wrong" + j;
                answerInst.GetComponent<Button>().onClick.AddListener(() => Result(j));
            }
        }
        countQuestion++;
        numberQuestion++;


        if (numberQuestion >= dunyaFutbolSorulari)
        {
            Result();
        }
        // GameObject.Find("QuestionCount").GetComponent<Text>().text = numberQuestion + " / 50";
        PlayerPrefs.SetInt("questionNumber", countQuestion);
    }
}