using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Debug = UnityEngine.Debug;
using Object = System.Object;

namespace SimpleCrossSceneReferences.Editor
{
    // This processor scans Scenes that are being saved, and sets up the data so that the link between
    // XSR fields and their target objects can be restored. 
    // TODO: GUID uniqueness is currently not enforced. We rely on GUIDs being unique enough as is.
    [InitializeOnLoad]
    public static class SceneSavedPostProcessor
    {
        // Enum defining what Object types we support
        public enum SupportedObjectType
        {
            NULL,
            GameObject,
            Transform,
            Component,
            MonoBehaviour,
            CrossSceneReferenceProxy
        }
        
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
        public interface LinkResolver { }
        
        public class TemporaryLinkResolver : LinkResolver
        {
            public Object Target;
            public Object Route;
            public Object Value;
        }
        public class TemporaryProxyLinkResolver : LinkResolver
        {
            public Type ProxyType;
            public Object Target;
            public int Route;
            public Object Value;
            public Object Context;
        }

        // Because we have to null out fields that store XSRs, maintain a list of these values so we can set the values
        // back after Scene saving is complete
        private static Dictionary<Scene,List<LinkResolver>> TemporaryResolvers = new Dictionary<Scene,List<LinkResolver>>();

        // EditorSceneManager.sceneSaving listener
        static void OnSceneSaving(Scene scene, string path)
        {
#if SIMPLE_CROSS_SCENE_REFERENCES_DEBUG
            Stopwatch timer = new Stopwatch();
            timer.Start();
#endif
            
            List<LinkResolver> temporaryResolvers = new List<LinkResolver>();
            
            // Obtain the CodeGenerator instance which contains Assembly information about XSR fields
            CodeGenerator generator = SimpleCrossSceneReferenceSetup.Generator;

            foreach (CodegenClass refClass in generator)
            {
                // Proxies are handled differently.
                if (CodeGenerator.IsProxyImpl(refClass.ClassType))
                {
                    CollectProxy(temporaryResolvers, refClass.ClassType);
                }
                else
                {
                    CollectClass(temporaryResolvers, refClass);
                }
            }

            TemporaryResolvers[scene] = temporaryResolvers;
            
#if SIMPLE_CROSS_SCENE_REFERENCES_DEBUG
            timer.Stop();
            UnityEngine.Debug.Log($"XSR scene processing took {timer.ElapsedMilliseconds}ms for scene {scene.name}");
#endif
        }

        private static void CollectClass(List<LinkResolver> temporaryResolvers, CodegenClass refClass)
        {
            // Search all open scenes for GameObject components matching 'refClass'
            foreach (MonoBehaviour refUsage in UnityEngine.Object.FindObjectsOfType(refClass.ClassType, true))
            {
                CrossSceneReferenceResolver resolver = GetOrCreateResolver(refUsage);
                foreach (CodegenClassMember member in refClass.Members)
                {
                    // Get the field's current value
                    UnityEngine.Object memberValue = member.InfoWrapper.GetValue(refUsage) as UnityEngine.Object;

                    if (memberValue == null)
                    {
                        CreateNullResolver(resolver, refUsage, member.InfoWrapper);
                    }
                    else
                    {
                        GameObject fieldValueGameObject = GetGameObject(memberValue, out SupportedObjectType type);
                        // We don't need to store anything for references within the same scene
                        if (fieldValueGameObject.scene == refUsage.gameObject.scene)
                        {
                            // Null out any existing resolver data pointing to this field
                            CreateNullResolver(resolver, refUsage, member.InfoWrapper);
                            continue;
                        }


                        // Set up a CrossSceneReferenceLocator and get the GUID which has been assigned to that object
                        string referencedGUID = GetCrossSceneReferenceGUID(memberValue);

                        // Store the data required so that the link can be restored at runtime
                        CollectResolveData(resolver, refUsage, refUsage.GetType(), member.InfoWrapper.MemberInfo, referencedGUID);

                        // More immediate way to restore the links once save is complete
                        temporaryResolvers.Add(new TemporaryLinkResolver()
                        {
                            Target = refUsage,
                            Route = member.InfoWrapper,
                            Value = memberValue
                        });

                        // Set to null during Scene save, otherwise Unity will complain about cross scene refs
                        member.InfoWrapper.SetValue(refUsage, null);
                    }
                }
            }
        }

        private static void CollectProxy(List<LinkResolver> temporaryResolvers, Type proxyType) //AnimTrackProxy
        {
            GameObject GetPassthroughGameObject(UnityEngine.Object obj)
            {
                if(obj is Component asComponent)
                {
                    return asComponent.gameObject;
                }
                else if(obj is GameObject asGO)
                {
                    return asGO;
                }

                throw new NotSupportedException($"Unsupported passthrough type {obj.GetType()}.");
            }

            var proxy = Activator.CreateInstance(proxyType) as CrossSceneReferenceProxy;

            // Search all open scenes for GameObject components matching the proxy's relevant component type
            foreach (Component relevantComponent in UnityEngine.Object.FindObjectsOfType(proxy.RelevantComponentType, true))
            {
                // Generate a context for the proxy to effectively operate on targets & passthroughs.
                UnityEngine.Object context = proxy.AcquireContext(relevantComponent);

                // Iterate through all the proxy-type members contained within the relevant component instance.
                UnityEngine.Object[] proxyTargets = proxy.GetTargets(context);

                // However, if there are no valid targets, the context is useless. 
                // No further logic is necessary for this iteration. Let's interrupt & dispose.
                if(proxyTargets == null || proxyTargets.Length == 0)
                {
                    proxy.ReleaseContext(context);
                    continue;
                }

                for (int i = 0; i < proxyTargets.Length; ++i)
                {
                    var target = proxyTargets[i] as object;

                    // Obtain the component at the other end of the proxy.
                    UnityEngine.Object passthrough = proxy.GetPassthrough(ref target, context);

                    if (passthrough == null)
                        continue; // No proxying to be done. Skip iteration.

                    // By now, we guarantee a GameObject component with a valid proxy endpoint.
                    // Create a resolver to connect both proxy endpoints.
                    CrossSceneReferenceResolver resolver = GetOrCreateResolver(relevantComponent);

                    // Set up a CrossSceneReferenceLocator and get the GUID which has been assigned to the passthrough component.
                    CrossSceneReferenceLocator loc = GetOrCreateLocator(GetPassthroughGameObject(passthrough));

                    int locGuidIdx = loc.Passthroughs.FindIndex(x => x == passthrough);
                    if (locGuidIdx == -1)
                    {
                        // Only generate new GUID for unique entries.
                        loc.ComponentGUIDS.Add(GUID.Generate().ToString());
                        loc.Passthroughs.Add(passthrough);

                        locGuidIdx = loc.ComponentGUIDS.Count - 1;
                    }

                    // Mark relevant component's scene for saving to serialize the resolver on the Unity scene.
                    Scene scene = relevantComponent.gameObject.scene;
                    EditorSceneManager.MarkSceneDirty(scene);

                    // Store the data required so that the link can be restored at runtime
                    CrossSceneReferenceSetupData data = new CrossSceneReferenceSetupData()
                    {
                        ClassHash = CodeGenerator.ClassHashFromType(proxyType),
                        RouteHash = proxy.GenerateRouteHash(passthrough, context),
                        Target = target as UnityEngine.Object,
                        GUID = loc.ComponentGUIDS[locGuidIdx],
                        Context = context
                    };
                    resolver.AddResolverData(data);

                    // More immediate way to restore the links once save is complete
                    temporaryResolvers.Add(new TemporaryProxyLinkResolver() // REVIEW: Consider not overloading Temp Link Resolver. Make specific classes for use case.
                    {
                        ProxyType = proxyType,
                        Target = target,
                        Value = passthrough,
                        Context = context,
                        Route = data.RouteHash
                    });

                    // Set to null during Scene save, otherwise Unity will complain about cross scene refs
                    proxy.Set(data.RouteHash, target, null, context);
                }
            }
        }

        static void CreateNullResolver(CrossSceneReferenceResolver resolver, Behaviour refUsage, FieldOrPropertyInfo fieldInfoWrapper)
        {
            CollectResolveData(resolver, refUsage, refUsage.GetType(), fieldInfoWrapper?.MemberInfo, null);
        }

        // EditorSceneManager.sceneSaved listener
        static void OnSceneSaved(Scene scene)
        {
#if SIMPLE_CROSS_SCENE_REFERENCES_DEBUG
            Stopwatch timer = new Stopwatch();
            timer.Start();
#endif
            
            // Restore Cross scene refs
            if (TemporaryResolvers.TryGetValue(scene, out List<LinkResolver> temporaryResolvers))
            {
                foreach (var item in temporaryResolvers)
                {
                    if (item is TemporaryProxyLinkResolver pResolver)
                    {
                        var proxy = Activator.CreateInstance(pResolver.ProxyType) as CrossSceneReferenceProxy;
                        proxy.Set(pResolver.Route, pResolver.Target, pResolver.Value, pResolver.Context);
                    }
                    else if(item is TemporaryLinkResolver sResolver)
                    {
                        (sResolver.Route as FieldOrPropertyInfo).SetValue(sResolver.Target, sResolver.Value);
                    }
                    else
                    {
                        throw new InvalidCastException($"Unsupported resolver type detected: {item}");
                    }
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
            Behaviour behaviour = subject as Behaviour;
            if (!behaviour.TryGetComponent(typeof(CrossSceneReferenceResolver), out Component resolverRef))
            {
                resolverRef = behaviour.gameObject.AddComponent<CrossSceneReferenceResolver>();
            }

            CrossSceneReferenceResolver resolver = resolverRef as CrossSceneReferenceResolver;
            resolver.Prune();
            return resolver;
        }

        // Collect the data required to set the link back up at runtime
        static void CollectResolveData(CrossSceneReferenceResolver resolver, UnityEngine.Object target, Type targetType, MemberInfo member, string GUID)
        {
            if (resolver != null)
            {
                CrossSceneReferenceSetupData data = new CrossSceneReferenceSetupData()
                {
                    ClassHash = CodeGenerator.ClassHashFromType(targetType),
                    RouteHash = member.Name.GetHashCode(),
                    Target = target,
                    GUID = GUID
                };
                resolver.AddResolverData(data);
            }
        }

        static GameObject GetGameObject(UnityEngine.Object target, out SupportedObjectType type)
        {
            type = SupportedObjectType.NULL;
            if (target == null)
                return null;

            if (target is Transform asTransform)
            {
                type = SupportedObjectType.Transform;
                return asTransform.gameObject;
            }

            if (target is GameObject asGo)
            {
                type = SupportedObjectType.GameObject;
                return asGo;
            }

            if (target is MonoBehaviour asBehaviour)
            {
                type = SupportedObjectType.MonoBehaviour;
                return asBehaviour.gameObject;
            }

            if (target is Component asComponent)
            {
                type = SupportedObjectType.Component;
                return asComponent.gameObject;
            }

            return null;
        }

        

        // Generate a GUID to identify target at runtime
        static string GetCrossSceneReferenceGUID(UnityEngine.Object target)
        {
            if (target == null)
                return null;

            GameObject targetObject = GetGameObject(target, out SupportedObjectType type);
            if (type == SupportedObjectType.NULL)
                return null;

            CrossSceneReferenceLocator loc = GetOrCreateLocator(targetObject);
            switch (type)
            {
                case SupportedObjectType.Transform:
                    return loc.TransformGUID;
                case SupportedObjectType.GameObject:
                    return loc.GameObjectGUID;
                case SupportedObjectType.MonoBehaviour:
                case SupportedObjectType.Component:
                    Component comp = target as Component;
                    for (int i = 0; i < loc.Passthroughs.Count; i++)
                    {
                        // If there is already a GUID for this object, return it
                        if (loc.Passthroughs[i] == comp)
                            return loc.ComponentGUIDS[i];
                    }

                    // Create a new GUID if needed
                    string guid = GUID.Generate().ToString();
                    loc.ComponentGUIDS.Add(guid);
                    loc.Passthroughs.Add(comp);
                
                    Scene scene = comp.gameObject.scene;
                    EditorSceneManager.MarkSceneDirty(scene);

                    return guid;
                default:
                    throw new Exception($"Field of Type {target.GetType()} cannot be marked as a CrossSceneReference.");
            }
        }

        // Find or create a locator on the given target
        static CrossSceneReferenceLocator GetOrCreateLocator(GameObject target)
        {
            CrossSceneReferenceLocator loc = target.GetComponent<CrossSceneReferenceLocator>();
            if (loc == null)
            {
                loc = target.AddComponent<CrossSceneReferenceLocator>();
                loc.GenerateGUIDs();
                
                Scene scene = target.gameObject.scene;
                EditorSceneManager.MarkSceneDirty(scene);
            }

            if (string.IsNullOrEmpty(loc.TransformGUID))
            {
                loc.GenerateGUIDs();
            }

            loc.Prune();
            return loc;
        }
    }
}