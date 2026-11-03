using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>Animator and VFX calls that tolerate art without the matching parameter or prefab.</summary>
    public static class ArtCue
    {
        public const float VfxSeconds = 2;

        public static void Trigger(GameObject target, string name)
        {
            var animator = Find(target, name, AnimatorControllerParameterType.Trigger);
            if (animator != null) animator.SetTrigger(name);
        }
        public static void SetBool(GameObject target, string name, bool value)
        {
            var animator = Find(target, name, AnimatorControllerParameterType.Bool);
            if (animator != null) animator.SetBool(name, value);
        }
        public static void Spawn(GameObject prefab, Transform parent, Vector3 localPosition)
        {
            if (prefab == null) return;
            var item = Object.Instantiate(prefab, parent, false);
            item.transform.localPosition = localPosition;
            Object.Destroy(item, VfxSeconds);
        }
        private static Animator Find(GameObject target, string name, AnimatorControllerParameterType type)
        {
            if (target == null) return null;
            var animator = target.GetComponentInChildren<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null || !animator.isActiveAndEnabled) return null;
            foreach (var parameter in animator.parameters)
                if (parameter.name == name && parameter.type == type) return animator;
            return null;
        }
    }
}
