using UnityEngine;

namespace StringAsASword.Core
{
    /// <summary>The caption cards shown between the main menu and the battle. Edit the text in the Inspector.</summary>
    [CreateAssetMenu(menuName = "String As a Sword/Story", fileName = "IntroStory")]
    public sealed class StoryDefinition : ScriptableObject
    {
        [System.Serializable]
        public sealed class Card
        {
            [TextArea(2, 4)] public string text;
            [Tooltip("Optional illustration shown above the caption.")]
            public Sprite image;
        }
        public Card[] cards =
        {
            new Card { text = "흑과 백으로 나뉜 판 위에서,\n모든 말은 정해진 칸만을 밟는다." },
            new Card { text = "판을 다스리는 것은 왕의 주선율.\n그 박자를 벗어난 소리는 잡음이라 불렸다." },
            new Card { text = "한 마리의 나이트가\n그 잡음을 들었다." },
            new Card { text = "흩어진 잡음을 모으면\n빼앗긴 선율을 되찾을 수 있다." },
            new Card { text = "검을 들어라.\n박자에 맞춰, 판을 벗어나기 위해." }
        };
    }
}
