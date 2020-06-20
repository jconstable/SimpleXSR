using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SimpleCrossSceneReferences.Editor
{
    public static class FileSystemUtil
    {
        // Search the project for a script file named the same thing as the Type
        public static string FindScriptFileForClass(System.Type t)
        {
            string queryFileName = $"{t.Name}.cs";
            DirectoryInfo dir = new DirectoryInfo(Application.dataPath);
            FileInfo[] files = dir.GetFiles(queryFileName, SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                return files[0].FullName;
            }

            return null;
        }

        // Rewrite a path to be a relative Asset path
        public static string SystemPathToAssetPath(string path)
        {
            if (!path.Contains("Assets"))
                return path;

            return path.Substring(path.IndexOf("Assets"));
        }
    }

}
