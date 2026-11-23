using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace StringAsASword.Presentation
{
    /// <summary>Pointer event relay for menu items built in code, with optional mouse hold repeat.</summary>
    public sealed class MenuButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        public Action Hovered, Exited, Clicked;

        private float repeatDelay, repeatInterval;
        private Coroutine repeatRoutine;

        internal void RepeatWhileHeld(float delay, float interval)
        {
            repeatDelay = delay;
            repeatInterval = interval;
        }

        public void OnPointerEnter(PointerEventData eventData) => Hovered?.Invoke();

        public void OnPointerExit(PointerEventData eventData)
        {
            if (eventData is ExtendedPointerEventData { device: Mouse }) StopRepeating();
            Exited?.Invoke();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!RepeatsOnMousePress(eventData)) return;

            StopRepeating();
            repeatRoutine = StartCoroutine(RepeatClick());
            Clicked?.Invoke();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (RepeatsOnMousePress(eventData)) StopRepeating();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // A repeat-enabled mouse press already performed its first step on pointer down.
            if (!RepeatsOnMousePress(eventData)) Clicked?.Invoke();
        }

        private bool RepeatsOnMousePress(PointerEventData eventData) =>
            repeatInterval > 0 && eventData is ExtendedPointerEventData
            {
                button: PointerEventData.InputButton.Left,
                device: Mouse
            };

        private IEnumerator RepeatClick()
        {
            yield return new WaitForSecondsRealtime(repeatDelay);
            var interval = new WaitForSecondsRealtime(repeatInterval);
            while (true)
            {
                Clicked?.Invoke();
                yield return interval;
            }
        }

        private void StopRepeating()
        {
            if (repeatRoutine == null) return;
            StopCoroutine(repeatRoutine);
            repeatRoutine = null;
        }

        private void OnDisable() => StopRepeating();

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) StopRepeating();
        }
    }
}
