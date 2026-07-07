using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Logger
{
    static float startTimer = 0;
    public static void TimerStart()
    {
        startTimer = Time.realtimeSinceStartup;        
    }
    public static void TimerEnd( string str)
    {
        Debug.Log(str + " :" + (Time.realtimeSinceStartup - startTimer).ToString());
    }
}