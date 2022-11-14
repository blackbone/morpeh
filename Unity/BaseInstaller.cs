namespace Scellecs.Morpeh {

    using Providers;
#if UNITY_EDITOR && ODIN_INSPECTOR
    using UnityEditor;
    using Sirenix.OdinInspector;
#endif
    using UnityEngine;

#if UNITY_EDITOR && ODIN_INSPECTOR
    [HideMonoScript]
#endif
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    [Il2CppSetOption(Option.DivideByZeroChecks, false)]
    public abstract class BaseInstaller : WorldViewer {
        protected abstract void OnEnable();

        protected abstract void OnDisable();
        
#if UNITY_EDITOR && ODIN_INSPECTOR
        [OnInspectorGUI]
        private void OnEditorGUI() {
            this.gameObject.transform.hideFlags = HideFlags.HideInInspector;
        }
#endif
#if UNITY_EDITOR
        [MenuItem("GameObject/ECS/", true, 10)]
        private static bool OrderECS() => true;
#endif
    }
}
