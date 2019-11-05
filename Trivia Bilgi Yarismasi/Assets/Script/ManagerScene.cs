using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;


public class ManagerScene : MonoBehaviour {

    public static ManagerScene Instance { get; set; }

    public bool selectedTurkLigi = false;
    public bool selectedUefakupalari = false;
    public bool selectedDunyafutbolu = false;

    private void Awake()
    {
        Instance = this;
    }

    public void HomeButton()
    {
        SceneManager.LoadScene("MainMenu");
    }

    public void TurkLigi()
    {
        SceneManager.LoadScene("TurkLigi");
        selectedTurkLigi = true;
        selectedDunyafutbolu = false;
        selectedUefakupalari = false;

    }
    public void UefaKupalari()
    {
        SceneManager.LoadScene("Uefakupalari");
        selectedUefakupalari = true;
        selectedTurkLigi = false;
        selectedDunyafutbolu = false;

    }
    public void DunyaFutbolu()
    {
        SceneManager.LoadScene("DunyaFutbolu");
        selectedDunyafutbolu = true;
        selectedTurkLigi = false;
        selectedUefakupalari = false;

    }
}
