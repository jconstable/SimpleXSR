using UnityEngine;
using Object = System.Object;

namespace SimpleCrossSceneReferences
{
    /// <summary>
    /// Implement this interface to provide a stateful proxy for third-party Components, in order to participate
    /// in the CrossSceneReference system.
    /// </summary>
    public interface CrossSceneReferenceProxy
    {
        public void Set(Object target, Object value, Object context);
    
        /// <summary>
        /// The component type that is searched for accross all scenes for Cross Scene Reference processing.
        /// </summary>
        public System.Type RelevantComponentType { get;  }

        /// <summary>
        /// Obtains the observed Component on the other end of the proxy.
        /// </summary>
        /// <param name="target"></param>
        /// <returns>UnityEngine.Component referenced by the target.</returns>
        public UnityEngine.Object GetPassthrough(Object target, Object context);

        /// <summary>
        /// Obtains all members identified within the Context which directly
        /// reference a Component on a separate scene.
        /// </summary>
        /// <returns>Array of targets whose type is of which the proxy intends to wrap.</returns>
        public UnityEngine.Object[] GetTargets(Object context);

        /// <summary>
        /// Parses the relevant Component to obtain the proxied elements.
        /// </summary>
        /// <param name="relevantComponent"></param>
        /// <returns>A Context instance to be used in subsequent operations.</returns>
        public UnityEngine.Object GenerateContext(Component relevantComponent);
    }
}