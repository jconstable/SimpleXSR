using System;
using System.Collections;
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
        public List<UnityEngine.Object> Passthroughs;
#if !SIMPLE_CROSS_SCENE_REFERENCES_DEBUG
        [HideInInspector]
#endif
        public List<string> ComponentGUIDS;
        
        void OnValidate()
        {
#if UNITY_EDITOR
            if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(this))
            {
                NullGUIDs();
                return;
            }
#endif
            Event e = Event.current;
 
            if (e != null)
            {
                // If the object is duplicated, then it needs its own GUIDs
                if (e.type == EventType.ExecuteCommand && e.commandName == "Duplicate")
                {
                    GenerateGUIDs();
                }
            }
        }

        private void Start()
        {
            if (!string.IsNullOrEmpty(TransformGUID))
            {
                var instance = CrossSceneReferenceManager.Instance;
                // Exiting playmode
                if (instance == null)
                    return;
                
                instance.RegisterTarget(TransformGUID, transform);
                instance.RegisterTarget(GameObjectGUID, gameObject);
                for (int i = 0; i < Passthroughs.Count; i++)
                {
                    if (Passthroughs[i] != null)
                        instance.RegisterTarget(ComponentGUIDS[i], Passthroughs[i]);
                }
            }
        }

        private void OnDestroy()
        {
            if (!string.IsNullOrEmpty(TransformGUID))
            {
                var instance = CrossSceneReferenceManager.Instance;
                if (instance == null)
                    // Exiting playmode
                    return;
                
                instance.UnregisterTarget(TransformGUID);
                instance.UnregisterTarget(GameObjectGUID);
                for (int i = 0; i < Passthroughs.Count; i++)
                {
                    if (Passthroughs[i] != null)
                        instance.UnregisterTarget(ComponentGUIDS[i]);
                }
            }
        }

        // Remove null entries from the list
        public void Prune()
        {
            for (int i = Passthroughs.Count - 1; i >= 0; i--)
            {
                if (Passthroughs[i] == null)
                {
                    Passthroughs.RemoveAt(i);
                    ComponentGUIDS.RemoveAt(i);
                }
            }
        }

        public void GenerateGUIDs()
        {
            #if UNITY_EDITOR
            TransformGUID = UnityEditor.GUID.Generate().ToString();
            GameObjectGUID = UnityEditor.GUID.Generate().ToString();
            #else
            TransformGUID = null;
            GameObjectGUID = null;
            #endif
            Passthroughs = new List<UnityEngine.Object>();
            ComponentGUIDS = new List<string>();
        }

        public void NullGUIDs()
        {
            TransformGUID = null;
            GameObjectGUID = null;
            Passthroughs = new List<UnityEngine.Object>();
            ComponentGUIDS = new List<string>();
        }
    }
}
