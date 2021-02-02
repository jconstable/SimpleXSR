using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SimpleCrossSceneReferences
{
    // Attribute that allows a field of a type that derrives from UnityEngine.Object to be handled
    // as cross scene references
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class CrossSceneReferenceAttribute : Attribute
    {
    }
}