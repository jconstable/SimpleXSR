using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimpleCrossSceneReferences
{
    // This MonoBehaviour is a singleton instance that listens for both XSR locators and XSR Resolvers.
    // Locators map a GUID to an Object instance, and resolver wait for a GUID -> Object map to exist so that
    // the Object value can be assigned to a field.
    // Link resolution is triggered when a new GUID -> Object map is added, or a new Resolver is added.
    public class CrossSceneReferenceManager : MonoBehaviour
    {
        // TODO: Add some sort of bookkeeping so that a developer can be made aware if links are not being resolved
        // Singleton Instance, with corresponding bool so we don't need to test the Object against null, which is expensive.
        private static CrossSceneReferenceManager _Instance;
        private static bool _IsSetup;

        // Instance getter
        public static CrossSceneReferenceManager Instance
        {
            get
            {                
                if (!_IsSetup)
                {
                    // Create a host GameObject with a CrossSceneReferenceManager
                    GameObject host = new GameObject("CrossSceneReferenceManager");
                    _Instance = host.AddComponent<CrossSceneReferenceManager>();
                    _IsSetup = true;
                    if (Application.isPlaying)
                    {
                        DontDestroyOnLoad(host);
                    }
                    else
                    {
                        host.hideFlags |= HideFlags.DontSave;
                    }
                    
                    // If not in debug mode, hide the GameObject
#if !SIMPLE_CROSS_SCENE_REFERENCES_DEBUG
                    host.hideFlags |= HideFlags.HideInHierarchy;
#endif
                }
                
                return _Instance;
            }
        }

        // GUID maps that are available for resolving
        // TODO: Maybe consider using int hashes instead of GUIDS, to avoid constantly hashing the GUID string for lookups
        private Dictionary<string, Object> LinkTargets = new Dictionary<string, Object>();
        // Resolve requests that hope to find a GUID match
        private List<CrossSceneReferenceSetupData> ResolveTargets = new List<CrossSceneReferenceSetupData>();

        private void OnDestroy()
        {
            _IsSetup = false;
        }

        // Register a GUID mapping
        public void RegisterTarget(string GUID, Object obj)
        {
#if DEBUG
            Object o;
            if (LinkTargets.TryGetValue(GUID, out o))
            {
                throw new Exception($"Object with GUID {GUID} has already been registered");
            }
#endif
            LinkTargets[GUID] = obj;
            ResolveLinks();
        }

        // Unregister a GUID mapping
        public void UnregisterTarget(string GUID)
        {
            LinkTargets.Remove(GUID);
        }

        // Register a resolver
        public void RegisterResolver(CrossSceneReferenceSetupData resolver)
        {
            ResolveTargets.Add(resolver);
            ResolveLinks();
        }

        public void DeregisterResolver(CrossSceneReferenceSetupData resolver)
        {
            for (int i = ResolveTargets.Count - 1; i >= 0; i--)
            {
                if (ResolveTargets[i].Target == resolver.Target)
                {
                    ResolveTargets.RemoveAt(i);
                }
            }
        }

        // Scan the list of pending resolvers, and see if we have an Object mapped to the requested GUID.
        // Once a resolver is complete, it will be removed from the list.
        // GUID maps are only removed once the target Object is destroyed
        public void ResolveLinks()
        {
            // Define that is only set once the SimpleCrossSceneReferenceSetup has run once
            // This is because the XSR.Codegen.CrossSceneReference_Codegen_Entry won't exist in the User assembly
            // until after codegen runs
#if SIMPLE_CROSS_SCENE_REFERENCES
            if (ResolveTargets.Count > 0 && LinkTargets.Count > 0)
            {
#if SIMPLE_CROSS_SCENE_REFERENCES_DEBUG
                Stopwatch timer = new Stopwatch();
                timer.Start();
#endif
                // Loop over resolvers
                for (int i = ResolveTargets.Count - 1; i >= 0; i--)
                {
                    CrossSceneReferenceSetupData resolve = ResolveTargets[i];
                    if (LinkTargets.TryGetValue(resolve.GUID, out Object Value))
                    {
                        // A mapped GUID was found. Use the codegen class to assign the Object value to the field
                        XSR.Codegen.CrossSceneReference_Codegen_Entry.Set(resolve.ClassHash, resolve.RouteHash, resolve.Target, Value, resolve.Context);
                        ResolveTargets.RemoveAt(i);
                    }
                }
                
#if SIMPLE_CROSS_SCENE_REFERENCES_DEBUG 
                timer.Stop();
                UnityEngine.Debug.Log($"XSR manager spent {timer.ElapsedMilliseconds}ms resolving links");
#endif
            }
#endif
        }
    }

    // Small class to hold the data needed to set a field value through our codegen
    [Serializable]
    public struct CrossSceneReferenceSetupData
    {
        public int ClassHash;
        public int RouteHash;
        public Object Target;
        public string GUID;
        public Object Context;
    }
}