using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = System.Object;

namespace SimpleCrossSceneReferences.Editor
{
    // This processor scans Scenes that are being saved, and sets up the data so that the link between
    // XSR fields and their target objects can be restored. 
    // TODO: GUID uniqueness is currently not enforced. We rely on GUIDs being unique enough as is.
    [InitializeOnLoad]
    public static class SceneSavedPostProcessor
    {
        // Constructor sets up listeners for scene saving
        static SceneSavedPostProcessor()
        {
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorSceneManager.sceneSaved -= OnSceneSaved;
            EditorSceneManager.sceneSaved += OnSceneSaved; 

            // Tell Unity that it's ok to allow cross-scene drag-and-drop
            EditorSceneManager.preventCrossSceneReferences = false;
        }

        // Temporary class used to restore cross-scene refs that were unset during Scene save
        public class TemporaryLinkResolver
        {
            public Object Target;
            public FieldInfo Field;
            public Object Value;
        }
        
        // Because we have to null out fields that store XSRs, maintain a list of these values so we can set the values
        // back after Scene saving is complete
        private static Dictionary<Scene,List<TemporaryLinkResolver>> TemporaryResolvers = new Dictionary<Scene,List<TemporaryLinkResolver>>();

        // EditorSceneManager.sceneSaving listener
        static void OnSceneSaving(Scene scene, string path)
        {
#if SIMPLE_CROSS_SCENE_REFERENCES_DEBUG
            Stopwatch timer = new Stopwatch();
            timer.Start();
#endif
            
            List<TemporaryLinkResolver> temporaryResolvers = new List<TemporaryLinkResolver>();
            
            // Obtain the CodeGenerator instance which contains Assembly information about XSR fields
            CodeGenerator generator = SimpleCrossSceneReferenceSetup.Generator;

            foreach (CodegenClass refClass in generator)
            {
                foreach (MonoBehaviour refUsage in GameObject.FindObjectsOfType(refClass.ClassType, true))
                {
                    CrossSceneReferenceResolver resolver = GetOrCreateResolver(refUsage);
                    foreach (var fieldInfo in refClass.Members)
                    {
                        // Get the field's current value
                        UnityEngine.Object fieldValue =
                            fieldInfo.FieldInfo.GetValue(refUsage) as UnityEngine.Object;
                        
                        if (fieldValue != null)
                        {
                            // Set up a CrossSceneReferenceLocator and get the GUID which has been assigned to that object
                            string referencedGUID = GetCrossSceneReferenceGUID(fieldValue);

                            // Store the data required so that the link can be restored at runtime
                            CollectResolveData(resolver, refUsage, fieldInfo.FieldInfo, referencedGUID);

                            // More immediate way to restore the links once save is complete
                            temporaryResolvers.Add(new TemporaryLinkResolver()
                            {
                                Target = refUsage,
                                Field = fieldInfo.FieldInfo,
                                Value = fieldValue
                            });

                            // Set to null during Scene save, otherwise Unity will complain about cross scene refs
                            fieldInfo.FieldInfo.SetValue(refUsage, null);
                        }
                    }
                }
            }
            
            TemporaryResolvers[scene] = temporaryResolvers;
            
#if SIMPLE_CROSS_SCENE_REFERENCES_DEBUG
            timer.Stop();
            UnityEngine.Debug.Log($"XSR scene processing took {timer.ElapsedMilliseconds}ms for scene {scene.name}");
#endif
        }

        // EditorSceneManager.sceneSaved listener
        static void OnSceneSaved(Scene scene)
        {
#if SIMPLE_CROSS_SCENE_REFERENCES_DEBUG
            Stopwatch timer = new Stopwatch();
            timer.Start();
#endif
            
            // Restore Cross scene refs
            List<TemporaryLinkResolver> temporaryResolvers;
            if (TemporaryResolvers.TryGetValue(scene, out temporaryResolvers))
            {
                foreach (var resolve in temporaryResolvers)
                {
                    resolve.Field.SetValue(resolve.Target, resolve.Value);
                }
            }
            
#if SIMPLE_CROSS_SCENE_REFERENCES_DEBUG
            timer.Stop();
            UnityEngine.Debug.Log($"XSR scene restoration took {timer.ElapsedMilliseconds}ms for scene {scene.name}");
#endif
        }

        // Find or create a resolver in the target GameObject
        static CrossSceneReferenceResolver GetOrCreateResolver(UnityEngine.Object subject)
        {
            MonoBehaviour behaviour = subject as MonoBehaviour;
            Component resolverRef;
            if (!behaviour.TryGetComponent(typeof(CrossSceneReferenceResolver), out resolverRef))
            {
                resolverRef = behaviour.gameObject.AddComponent<CrossSceneReferenceResolver>();
            }

            CrossSceneReferenceResolver resolver = resolverRef as CrossSceneReferenceResolver;
            resolver.Prune();
            return resolver;
        }

        // Collect the data required to set the link back up at runtime
        static void CollectResolveData(CrossSceneReferenceResolver resolver, UnityEngine.Object target, FieldInfo field,
            string GUID)
        {
            if (resolver != null &&
                target != null &&
                !string.IsNullOrEmpty(GUID))
            {
                CrossSceneReferenceSetupData data = new CrossSceneReferenceSetupData()
                {
                    ClassHash = CodeGenerator.ClassHashFromType(target.GetType()),
                    FieldHash = field.Name.GetHashCode(),
                    Target = target,
                    GUID = GUID
                };
                resolver.ResolverData.Add(data);
            }
        }

        // Generate a GUID to identify target at runtime
        static string GetCrossSceneReferenceGUID(UnityEngine.Object target)
        {
            if (target == null)
                return null;

            if (target is Transform asTransform)
            {
                CrossSceneReferenceLocator loc = GetOrCreateLocator(asTransform.gameObject);
                return loc.TransformGUID;
            }

            if (target is GameObject asGo)
            {
                CrossSceneReferenceLocator loc = GetOrCreateLocator(asGo);
                return loc.GameObjectGUID;
            }

            // Components and MonoBehaviours are treated the same
            Component comp = null;
            if (target is MonoBehaviour asBehaviour)
            {
                comp = asBehaviour;
            }
            if (target is Component asComponent)
            {
                comp = asComponent;
            }

            // Components and MonoBehaviours are treated the same
            if(comp != null)
            {
                CrossSceneReferenceLocator loc = GetOrCreateLocator(comp.gameObject);
                for (int i = 0; i < loc.Components.Count; i++)
                {
                    // If there is already a GUID for this object, return it
                    if (loc.Components[i] == comp)
                        return loc.ComponentGUIDS[i];
                }

                // Create a new GUID if needed
                string guid = GUID.Generate().ToString();
                loc.ComponentGUIDS.Add(guid);
                loc.Components.Add(comp);
                
                Scene scene = comp.gameObject.scene;
                EditorSceneManager.MarkSceneDirty(scene);

                return guid;
            }

            throw new Exception($"Field of Type {target.GetType()} cannot be marked as a CrossSceneReference.");
        }

        // Find or create a locator on the given target
        static CrossSceneReferenceLocator GetOrCreateLocator(GameObject target)
        {
            CrossSceneReferenceLocator loc = target.GetComponent<CrossSceneReferenceLocator>();
            if (loc == null)
            {
                loc = target.AddComponent<CrossSceneReferenceLocator>();
                loc.TransformGUID = GUID.Generate().ToString();
                loc.GameObjectGUID = GUID.Generate().ToString();
                loc.Components = new List<Component>();
                loc.ComponentGUIDS = new List<string>();
                
                Scene scene = target.gameObject.scene;
                EditorSceneManager.MarkSceneDirty(scene);
            }

            loc.Prune();
            return loc;
        }
    }
}