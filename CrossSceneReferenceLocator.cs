using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimpleCrossSceneReferences
{
    // This MonoBehaviour is attached to GameObjects that are referenced by an XSR instance.
    // Upon awake, this behaviour will register each pertinent Object and corresponding GUID with the manager,
    // so that any resolves can occur
    [ExecuteInEditMode]
    public class CrossSceneReferenceLocator : MonoBehaviour
    {
#if !SIMPLE_CROSS_SCENE_REFERENCES_DEBUG
        [HideInInspector]
#endif
        public string TransformGUID;
#if !SIMPLE_CROSS_SCENE_REFERENCES_DEBUG
        [HideInInspector]
#endif
        public string GameObjectGUID;
#if !SIMPLE_CROSS_SCENE_REFERENCES_DEBUG
        [HideInInspector]
#endif
        public List<Component> Components;
#if !SIMPLE_CROSS_SCENE_REFERENCES_DEBUG
        [HideInInspector]
#endif
        public List<string> ComponentGUIDS;

        private void Awake()
        {
            if (!string.IsNullOrEmpty(TransformGUID))
            {
                var instance = CrossSceneReferenceManager.Instance;
                instance.RegisterTarget(TransformGUID, transform);
                instance.RegisterTarget(GameObjectGUID, gameObject);
                for (int i = 0; i < Components.Count; i++)
                {
                    if (Components[i] != null)
                        instance.RegisterTarget(ComponentGUIDS[i], Components[i]);
                }
            }
        }

        private void OnDestroy()
        {
            // TODO: Do not run this if the app is exiting playmode
            if (!string.IsNullOrEmpty(TransformGUID))
            {
                var instance = CrossSceneReferenceManager.Instance;
                instance.UnregisterTarget(TransformGUID);
                instance.UnregisterTarget(GameObjectGUID);
                for (int i = 0; i < Components.Count; i++)
                {
                    if (Components[i] != null)
                        instance.UnregisterTarget(ComponentGUIDS[i]);
                }
            }
        }

        // Remove null entries from the list
        public void Prune()
        {
            for (int i = Components.Count - 1; i >= 0; i--)
            {
                if (Components[i] == null)
                {
                    Components.RemoveAt(i);
                    ComponentGUIDS.RemoveAt(i);
                }
            }
        }
    }
}
