using System;
using UnityEngine;
using UnityEngine.UIElements;
// Stores the title, artwork and sections rendered by the Unity project's welcome Inspector.

public class Readme : ScriptableObject
{
    public StyleSheet commonStyle;
    public StyleSheet darkStyle;
    public StyleSheet lightStyle;
    public Texture2D icon;
    public string title;
    public Section[] sections;

    [Serializable]
    public class Section
    {
        public string heading, text, linkText, url;
    }
}
