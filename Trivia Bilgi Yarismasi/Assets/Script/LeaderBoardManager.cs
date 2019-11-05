using UnityEngine;
using GooglePlayGames;
using UnityEngine.SocialPlatforms;


public class LeaderBoardManager : MonoBehaviour
{
    public static LeaderBoardManager instance;

    private void Awake()
    {
        if(instance == null)
        {
            instance = this;
        }
    }


    // Start is called before the first frame update
    void Start()
    {
        Login();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void Login()
    {
        PlayGamesPlatform.Activate();
        
        Social.localUser.Authenticate((bool success) => {

      
        });
    }

    public void AddScoreToLeaderboard()
    {
        Social.ReportScore(QuestionManager.Instance.totalScore, "CgkI1sSb29wGEAIQAA", (bool success) => {

        });
    }

    public void ShowLeaderboard()
    {
        //Social.ShowLeaderboardUI();
        if (Social.localUser.authenticated)
        {
            PlayGamesPlatform.Instance.ShowLeaderboardUI("CgkI1sSb29wGEAIQAA");
        } else
        {
            Login();
        }
    }
}
