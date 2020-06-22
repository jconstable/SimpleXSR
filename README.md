# SimpleXSR
Simple cross-scene reference extension for Unity 2018+.

Multi-scene workflows are essential for medium to large teams collaborating in Unity. For architectural reasons, Unity does not support cross-scene references by default. 

SimpleXSR provides an easy way to support cross-scene references in Unity, at a very small runtime cost. Currently supported field types are:
* GameObjects
* Transforms
* Components (necessarily including MonoBehaviours)

## Known issues
* GUIDs need to be stripped from Prefabs
* GUIDs need to be regenerated upon copy of a CrossSceneReferenceLocator

## Installation
Simply clone this repository into your Assets folder. The Editor scripts will handle setup and codegen.

## Sample Usage
Once SimpleXSR is installed, Unity will no longer prevent your from dragging and dropping cross-scene references into inspector fields. By default, Unity will complain about these fields when the Scene is saved, and the Scenes will not serialize these values. To enable saving of these field values, simply use the CrossSceneReference attribute.
```
public class MyBehaviour : MonoBehaviour {
    [CrossSceneReference]
    public GameObject OtherObject;
    
    [CrossSceneReference]
    public Transform OtherTransform;
    
    [CrossSceneReference]
    public MyBehaviour OtherBehaviour;
}
```

In order to support the ability to rename classes, SimpleXSR attempts to locate the GUID that Unity associates with a MonoBehaviour class. This is one of the reasons for the requirement that MonoBehaviours live in a file with the same name as the class. However, it is still possible to abuse this requirement. In the event that you prefer to leave your MonoBehaviour class in a file that does not follow this guideline, you can use the WeakClassReference attribute to have SimpleXSR respect the class anyway. The caveat is that if you rename this class, existing references to instances of the class will break.
```
[WeakClassReference]
public class MyBehaviourThatBreaksFileToClassNameRule : MonoBehaviour {
    [CrossSceneReference]
    public GameObject OtherObject;
}
```

## How it works
SimpleXSR uses Reflection in Edit mode to inspect your Assemblies and find classes that use the CrossSceneReference Attribute. We will call these classes XSRBehaviours. SimpleXSR will then generate a script in the user Assembly which handles assigning an object value to a instance's field. No Reflection occurs at runtime. Whenever SimpleXSR detect an impactful code change, codegen will run again.

When a Scene is saved, SimpleXSR will scan the Scene for XSRBehaviours, and add a small MonoBehaviour on both ends of the reference link:
* The GameObject being referenced in the link will receive a CrossSceneReferenceLocator behaviour, which stores a mapping of Object -> GUID references for that GameObject
* The GameObject that requires the cross-scene reference will receive a CrossSceneReferenceResolver behaviour, which stores data that is used to resolve the link at runtime.

These behaviours store only references to Objects within the same Scene, as well as string and integer data used to resolve links at runtime. A single link requires ~500bytes of memory total.

At runtime, when CrossSceneReferenceLocator and CrossSceneReferenceResolver Awake(), they will register with a Singleton manager that attempts to establish links. These links will be given to the automatically generated class, which handles field value assignment.

## Troubleshooting
If you are encountering issues with your references saving, you can enable more inspector functionality by adding the SIMPLE_CROSS_SCENE_REFERENCES_DEBUG define to your Player's scripting defines.

## TODO
* Better enforcement of GUID uniqueness
* Detect when the Editor is exiting playmode, and prevent creation of a new singleton manager in cleanup code.
* Add some better diagnostics/messaging about links that are never resolved
* Determine if links should continue to use string GUIDs at runtime, or if there is a need for a more efficient comparison type

### Small example of generated code
This is a sample of the codegen output that SimpleXSR uses at runtime for link resolution assignment.
```
// CrossSceneReference manager passes resolved link data into this entry point
public static class CrossSceneReference_Codegen_Entry {
    public static void Set(int classHash, int fieldHash, UnityEngine.Object target, UnityEngine.Object value){
        if(classHash == -1458364425) {
            SimpleBehaviour_XSR_Codegen.Set(fieldHash, target, value);
            return;
        }
        if(classHash == 1117548823) {
            OtherSimpleBehaviour_XSR_Codegen.Set(fieldHash, target, value);
            return;
        }
        throw new Exception($"Unable to resolve class for classHash {classHash}.");
    }
}
public static class SimpleBehaviour_XSR_Codegen {
    public static void Set(int fieldHash, UnityEngine.Object target, UnityEngine.Object value){
        SimpleBehaviour behaviour = target as SimpleBehaviour;
        if(fieldHash == -582599340) {
            behaviour.Other = value as UnityEngine.GameObject;
            return;
        }
        if(fieldHash == 1629313707) {
            behaviour.Anim = value as UnityEngine.Animator;
            return;
        }
        throw new Exception($"Unable to resolve field for fieldHash {fieldHash}.");
    }
}
public static class OtherSimpleBehaviour_XSR_Codegen {
    public static void Set(int fieldHash, UnityEngine.Object target, UnityEngine.Object value){
        OtherSimpleBehaviour behaviour = target as OtherSimpleBehaviour;
        if(fieldHash == 372029379) {
            behaviour.Other = value as UnityEngine.GameObject;
            return;
        }
        throw new Exception($"Unable to resolve field for fieldHash {fieldHash}.");
    }
}
```
