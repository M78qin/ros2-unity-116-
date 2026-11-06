using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class TC : MonoBehaviour
{
    public GameObject TO;


    public void Toggle()
    {
        // 点击时直接切换显示状态
        TO.SetActive(!TO.activeSelf);
    }
}
