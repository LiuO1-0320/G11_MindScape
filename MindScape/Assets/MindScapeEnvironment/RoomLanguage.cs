using System;
using UnityEngine;
using UnityEngine.UI;

namespace MindScape.Environment
{
    public static class RoomLanguage
    {
        const string Preference = "MindScape.Language";
        public static bool English { get; private set; } = PlayerPrefs.GetString(Preference, "zh-CN") == "en";
        public static event Action Changed;

        public static string Pick(string chinese, string english) => English ? english : chinese;

        public static void Bind(Text label, string chinese, string english) => RoomLanguageLabel.Bind(label, chinese, english);

        public static void Toggle()
        {
            English = !English;
            PlayerPrefs.SetString(Preference, English ? "en" : "zh-CN");
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }

    internal sealed class RoomLanguageLabel : MonoBehaviour
    {
        Text label;
        string chinese, english;

        public static void Bind(Text label, string chinese, string english)
        {
            var binding = label.gameObject.AddComponent<RoomLanguageLabel>();
            binding.label = label;
            binding.chinese = chinese;
            binding.english = english;
            binding.Refresh();
        }

        void Refresh()
        {
            if (label != null) label.text = RoomLanguage.Pick(chinese, english);
        }

        void OnEnable() { RoomLanguage.Changed += Refresh; Refresh(); }
        void OnDisable() { RoomLanguage.Changed -= Refresh; }
    }
}
