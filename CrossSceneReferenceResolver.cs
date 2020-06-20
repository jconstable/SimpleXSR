using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SimpleCrossSceneReferences
{
    // This MonoBehaviour is attached to GameObjects that contain an XSR-enabled behaviour.
    // Upon awake, this behaviour will register the data for each of the instance fields that need to be resolved
    // with the manager using the target object's XSR guid
    [ExecuteInEditMode]
    public class CrossSceneReferenceResolver : MonoBehaviour
    {
        #if !SIMPLE_CROSS_SCENE_REFERENCES_DEBUG
        [HideInInspector]
        #endif
        public List<CrossSceneReferenceSetupData> ResolverData = new List<CrossSceneReferenceSetupData>();
        
        private void Awake()
        {
            // TODO: Do not run this if the app is exiting playmode
            var instance = CrossSceneReferenceManager.Instance;
            foreach (var resolver in ResolverData)
            {
                instance.RegisterResolver(resolver);
            }
        }

        public void AddResolverData(CrossSceneReferenceSetupData data)
        {
            bool updated = false;
            for (int i = 0; i < ResolverData.Count; i++)
            {
                CrossSceneReferenceSetupData existingData = ResolverData[i];
                if (existingData.ClassHash == data.ClassHash &&
                    existingData.FieldHash == data.FieldHash &&
                    existingData.Target == data.Target)
                {
                    existingData.GUID = data.GUID;
                    ResolverData[i] = existingData;
                    updated = true;
                }
            }

            if (!updated)
            {
                ResolverData.Add(data);
            }

            Prune();
        }
        
        // Remove null entries from the list
        public void Prune()
        {
            for (int i = ResolverData.Count - 1; i >= 0; i--)
            {
                var data = ResolverData[i];
                if (string.IsNullOrEmpty(data.GUID) || data.Target == null)
                {
                    ResolverData.RemoveAt(i);
                }
            }
        }
    }
}