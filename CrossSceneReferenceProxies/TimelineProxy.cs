using SimpleCrossSceneReferences;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Object = System.Object;
using Type = System.Type;

/// <summary>
/// Proxy for Timeline tracks. Supports GameObject & Component type bindings.
/// </summary>
public struct TimelineProxy : CrossSceneReferenceProxy
{
    public Type RelevantComponentType { get => typeof(PlayableDirector); }

    public UnityEngine.Object GetPassthrough(ref object target, object context)
    {
        PlayableDirector director = context as PlayableDirector;
        TrackAsset externRefTrack = target as TrackAsset;
        return director.GetGenericBinding(externRefTrack);
    }

    public void Set(int routeHash, object target, object value, object context)
    {
        var trackBinding = value as UnityEngine.Object;
        var timelineTrack = target as TrackAsset;
        var director = context as PlayableDirector;

        if (trackBinding == null)
            director.ClearGenericBinding(timelineTrack);
        else
            director.SetGenericBinding(timelineTrack, trackBinding);
    }

    public UnityEngine.Object[] GetTargets(object context)
    {
        // For each instance, parse the AnimationTracks within, and identify 
        // which tracks are bound to GameObjects/Components from outside this PlayableDirector's scene.
        List<TrackAsset> externRefTimelineTrack = new List<TrackAsset>();
        PlayableDirector director = context as PlayableDirector;

        foreach (PlayableBinding binding in director.playableAsset.outputs)
        {
            if (binding.sourceObject is TrackAsset asTimelineTrack)
            {
                var timelineTrackBoundObject = director.GetGenericBinding(asTimelineTrack);

                if (timelineTrackBoundObject == null)
                    continue;

                bool doParentScenesMatch = true; // Initialized to a safe default value.

                if (timelineTrackBoundObject is GameObject asGO)
                {
                    doParentScenesMatch = (asGO.scene == director.gameObject.scene);
                }
                else if (timelineTrackBoundObject is Component asCmp)
                {
                    doParentScenesMatch = (asCmp.gameObject.scene == director.gameObject.scene);
                }

                if (!doParentScenesMatch)
                {
                    externRefTimelineTrack.Add(asTimelineTrack);
                }
            }
        }

        return externRefTimelineTrack.ToArray();
    }

    public UnityEngine.Object AcquireContext(Component relevantComponent)
    {
        // Our context is simply the PlayableDirector itself. Nothing to process here.
        return relevantComponent as PlayableDirector;
    }
    public int GenerateRouteHash(object passthrough, object context)
    {
        // This proxy does not make use of route hashes.
        return 0;
    }

    public void ReleaseContext(object context)
    {
        // The context is the PlayableDirector itself.
        // As such, no context is allocated, and therefore no context should be released/destroyed.
    }
}
