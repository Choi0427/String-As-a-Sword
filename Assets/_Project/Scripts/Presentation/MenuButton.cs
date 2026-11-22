using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace StringAsASword.Presentation
{
    /// <summary>Pointer hover/click relay for menu items built in code.</summary>
    public sealed class MenuButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public Action Hovered, Exited, Clicked;
        public void OnPointerEnter(PointerEventData eventData) => Hovered?.Invoke();
        public void OnPointerExit(PointerEventData eventData) => Exited?.Invoke();
        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke();
    }
}
