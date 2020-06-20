using System.IO;
using UnityEditor;
using UnityEngine;

namespace SimpleCrossSceneReferences.Editor
{
    [InitializeOnLoad]
    public static class SimpleCrossSceneReferenceSetup
    {
        // Update this if you want the codegen to write to a different location
        public static readonly string CodegenClassDirectorPath = "/CrossSceneReferenceData";
        public static readonly string CodegenClassFileName = "/CrossSceneReferenceCodegen.cs";
        public static readonly string ConditionalCodeDefine = "SIMPLE_CROSS_SCENE_REFERENCES";

        // The last Generator used, for Editor classes that need the extracted Assembly information
        public static CodeGenerator Generator;

        // Constructor
        static SimpleCrossSceneReferenceSetup()
        {
            EnsureCodegenExistsAndProjectIsConfigured();
        }

        // Ensure things are set up:
        //  Codegen script exists
        //  Codegen script is updated with current Assembly info
        //  Project has the conditional defined, such that runtime reference resolution can occur
        private static void EnsureCodegenExistsAndProjectIsConfigured()
        {
            bool IsDirty = false;
            string directory = string.Join("/",Application.dataPath, CodegenClassDirectorPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                IsDirty = true;
            }
            
            IsDirty |= RegenerateInteral();
            IsDirty |= AddDefine();

            if (IsDirty)
            {
                Refresh();
            }
        }

        [MenuItem("Tools/Cross Scene References/Regenerate References")]
        public static void Regenerate()
        {
            RegenerateInteral();
            Refresh();
        }

        // Update Unity with new data
        private static void Refresh()
        {
            AssetDatabase.ImportAsset(string.Join("/","Assets", CodegenClassDirectorPath, CodegenClassFileName), ImportAssetOptions.ImportRecursive);
            AssetDatabase.Refresh();
        }

        // Add the define which enables runtime reference resolution to the build settings
        private static bool AddDefine()
        {
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
            if (!defines.Contains(ConditionalCodeDefine))
            {
                defines += $";{ConditionalCodeDefine}";
                PlayerSettings.SetScriptingDefineSymbolsForGroup(group,defines);
                return true;
            }

            return false;
        }

        // Generate codegen
        private static bool RegenerateInteral()
        {
            Generator = new CodeGenerator();
            return Generator.Generate();
        }

        public static string CodegenFilePath
        {
            get
            {
                return string.Join("/",Application.dataPath, CodegenClassDirectorPath, CodegenClassFileName);
            }
        }
    }
}