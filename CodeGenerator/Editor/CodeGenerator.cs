using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using CrossSceneReference = SimpleCrossSceneReferences.CrossSceneReferenceAttribute;

// This file defines the logic required for SimpleXSR to write a programatically-generated script, which
// can assign values to MonoBehaviour instance fields based on baked data. The generated script
// does not use Reflection at runtime.
namespace SimpleCrossSceneReferences.Editor
{
    // Collection of data used to define a user class that uses the CrossSceneReference attributes
    public class CodegenClass
    {
        public string ClassName;
        public int ClassHash;
        public Type ClassType;
        public string CodegenClassName;
        public List<CodegenClassMember> Members;
    }
    
    // Collection of data used to locate specific fields within a class that use the CrossSceneReference attribute
    public class CodegenClassMember
    {
        public readonly string FieldName;
        public readonly FieldOrPropertyInfo InfoWrapper;
        public readonly int FieldHash;

        public CodegenClassMember(string fieldName, MemberInfo memberInfo, int fieldHash)
        {
            InfoWrapper = new FieldOrPropertyInfo(memberInfo);
            FieldName = fieldName;
            FieldHash = fieldHash;
        }

        public Type FieldType
        {
            get
            {
                return InfoWrapper.GetDerrivedType();
            }
        }
    }

    // This class handles creating the script file used to set field values at runtime.
    public class CodeGenerator : IEnumerable<CodegenClass>
    {
        static readonly string ClassGUIDCacheStringPrefix = "XSRCachedClassGUID_";
        // Data collected at Editor time, describing the pertinent parts of the User Assembly that we
        // need to represent in the codegen class.
        List<CodegenClass> Classes = new List<CodegenClass>();

        // Generate the script file
        // Returns true if Generate changed any data on disk.
        // Return false if no changes resulted
        public bool Generate()
        {
#if SIMPLE_CROSS_SCENE_REFERENCES_DEBUG
            Stopwatch timer = new Stopwatch();
            timer.Start();
#endif

            bool changesOccurred = false;
            
            // Crawl Assemblies to find classes that use the CrossSceneReference attributes
            CollectCrossSceneScripts();

            // Attempt to collect the old contents of the codegen script, if available
            string oldContent = string.Empty;
            string path = SimpleCrossSceneReferenceSetup.CodegenFilePath;
            if (File.Exists(path))
            {
                oldContent = File.ReadAllText(path);
            }

            // Generate the new content for the script file
            string newContent = GenerateScriptContent();

            // If the newly generated file is actually different than what is already on disk, write it
            if (!oldContent.Equals(newContent))
            {
                File.WriteAllText(path,newContent);
                changesOccurred = true;
            }
            
#if SIMPLE_CROSS_SCENE_REFERENCES_DEBUG
            timer.Stop();
            Debug.Log($"XSR Code generation took {timer.ElapsedMilliseconds}ms");
#endif

            return changesOccurred;
        }
        
        // Iterate over assemblies, and build the data representing user classes that use CrossSceneReference attributes
        void CollectCrossSceneScripts()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in assembly.GetTypes())
                {
                    if(IsProxyImpl(type))
                    {
                        CollectProxy(type);
                    }
                    else
                    {
                        CollectClass(type);
                    }
                }
            }
        }

        //TODO: maybe put this in the interface and convert interface to abstract class.
        public static bool IsProxyImpl(System.Type type)
        {
            return !type.IsInterface && typeof(CrossSceneReferenceProxy).IsAssignableFrom(type);
        }

        void CollectProxy(System.Type type)
        {
            AddProxy(type);
        }

        void CollectClass(System.Type type)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
            {
                if (field.GetCustomAttribute<CrossSceneReference>(true) != null)
                {
                    CollectClassAndMember(type, field);
                }
            }

            foreach (var prop in type.GetProperties())
            {
                if(prop.GetCustomAttribute<CrossSceneReference>(true) != null)
                {
                    CollectClassAndMember(type, prop);
                }
            }
        }

        void CollectClassAndMember(System.Type type, MemberInfo info)
        {
            Type assignableType = null;
            if (info.MemberType == MemberTypes.Field)
            {
                assignableType = (info as FieldInfo).FieldType;
            }
            else if (info.MemberType == MemberTypes.Property)
            {
                assignableType = (info as PropertyInfo).PropertyType;
            }

            if (!typeof(UnityEngine.Object).IsAssignableFrom(assignableType))
            {
                Debug.LogError($"CrossSceneReference attributes in {type} cannot be used on fields of type {assignableType}. Only fields with types that inherit from UnityEngine.Object may be used.");
                return;
            }

            int classHash = AddClass(type, out _);
            AddMember(classHash, info);
        }

        void AddProxy(Type codegenClass)
        {
            AddClass(codegenClass, out CodegenClass proxyClass);

            // Preserve the name of the class (because no codegen will be used).
            proxyClass.CodegenClassName = codegenClass.Name;
        }

        // Add a class to the Classes data
        // Returns the classHash for the class
        int AddClass(Type codegenClass, out CodegenClass outputClass)
        {
            // Generate the hash, and ensure that we don't already know about the class
            int hash = ClassHashFromType(codegenClass);
            foreach (var c in Classes)
            {
                if (c.ClassHash == hash)
                {
                    Debug.Assert(c.ClassName.Equals(codegenClass.ToString()));
                    outputClass = c;
                    return c.ClassHash;
                }
            }

            // Create the collection of data we need for the CodegenClass instance
            string newClassName = codegenClass.ToString();
            CodegenClass newClass = new CodegenClass()
            {
                ClassName = newClassName,
                ClassHash = hash,
                ClassType = codegenClass,
                CodegenClassName = $"{newClassName.Split('.').Last()}_XSR_Codegen",
                Members = new List<CodegenClassMember>()
            };
            Classes.Add(newClass);

            outputClass = newClass;

            return hash;
        }

        // Given a classHash, collect data required to populate a CodegenClassMember from the FieldInfo
        private void AddMember(int classHash, MemberInfo member)
        {
            // Attempt to resolve the CodegenClass to which this field belongs
            CodegenClass destClass = null;
            foreach (var c in Classes)
            {
                if (c.ClassHash == classHash)
                {
                    destClass = c;
                    break;
                }
            }
            
            Debug.Assert(destClass != null);

            // Ensure we don't already know about the field
            string fieldName = member.Name;
            int fieldHash = fieldName.GetHashCode();
            foreach (var f in destClass.Members)
            {
                if (f.FieldHash == fieldHash)
                {
                    Debug.Assert(fieldName.Equals(f.FieldName));
                    return;
                }
            }

            // Create the collection of data we need for the CodegenClassMember instance
            CodegenClassMember newMember = new CodegenClassMember(fieldName, member, fieldHash);
            destClass.Members.Add(newMember);
        }

        // In order to not break references when a class is renamed, we want to use the GUID instead. If the class is
        // a MonoBehaviour that does not live in its own file (it won't have its own GUID), throw an error unless
        // the users has waived this protection by using the WeakClassReferenceAttribute.
        public static int ClassHashFromType(System.Type t)
        {     
            if (typeof(MonoBehaviour).IsAssignableFrom(t))
            {
                // If the type has been decorated with the WeakClassReferenceAttribute, it's ok to just use the name
                if (t.GetCustomAttributes().Any(a => { return a.GetType() == typeof(WeakClassReferenceAttribute); }))
                {
                    // Use name for hash
                }
                else
                {
                    // Check player prefs to see if we've cached this GUID before
                    string cachedGUID = GetCachedClassGUID(t);
                    if (!string.IsNullOrEmpty(cachedGUID))
                    {
                        return cachedGUID.GetHashCode();
                    }
                    
                    // Scan the filesystem for a file matching the type, assuming that MonoBehaviours live in script
                    // files with the same name
                    var path = FileSystemUtil.FindScriptFileForClass(t);

                    Debug.Assert(!string.IsNullOrEmpty(path), $"Unable to find script representing class {t.Name}. Either put this class in its own script file, or use the [WeakClassReferenceAttribute] attribute. This attribute means you accept the risk of breaking any references to these classes if they are renamed.");
                    path = FileSystemUtil.SystemPathToAssetPath(path);
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    Debug.Assert(!string.IsNullOrEmpty(guid), $"Unable to locate GUID for file {path}");

                    SetCachedClassGUID(t, guid);
                    
                    return guid.GetHashCode();
                }
            }
            return t.FullName.GetHashCode();
        }

        private static string GetCachedClassGUID(System.Type t)
        {
            return EditorPrefs.GetString($"{ClassGUIDCacheStringPrefix}{t.FullName}", null);
        }

        private static void SetCachedClassGUID(System.Type t, string GUID)
        {
            EditorPrefs.SetString($"{ClassGUIDCacheStringPrefix}{t.FullName}", GUID);
        }

        // Required for IEnumerable
        public IEnumerator<CodegenClass> GetEnumerator()
        {
            return Classes.GetEnumerator();
        }

        // Required for IEnumerable
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        // Generate the text contents for the generated class
        string GenerateScriptContent()
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine("using System;");
            b.AppendLine("using System.Reflection;");
            b.AppendLine("namespace XSR.Codegen {");
            b.AppendLine("public static class CrossSceneReference_Codegen_Entry {");
            b.AppendLine("    public static void Set(int classHash, int fieldHash, object target, object value, object context){");

            // Generate the code to forward assignment data to the proper generated class
            foreach (var klass in Classes)
            {
                GenerateClassInvocation(b, klass);
            }
            
            b.AppendLine("        throw new Exception($\"Unable to resolve class for classHash {classHash}.\");");
            b.AppendLine("    }");
            b.AppendLine("}");
            b.AppendLine("}");
            
            // Generate the code representing each of the generated classes
            foreach (CodegenClass klass in Classes)
            {
                // Skip proxies. They are not generated (handwritten).
                if (IsProxyImpl(klass.ClassType))
                    continue;

                GenerateClassImplementation(b, klass);
            }

            return b.ToString();
        }

        // Add code for the class forwarding
        void GenerateClassInvocation(StringBuilder builder, CodegenClass klass)
        {
            builder.AppendLine($"        if(classHash == {klass.ClassHash}) {{");

            if (IsProxyImpl(klass.ClassType))
            {
                builder.AppendLine($"            new {klass.CodegenClassName}().Set(fieldHash, target, value, context);");
            }
            else
            { 
                builder.AppendLine($"            {klass.CodegenClassName}.Set(fieldHash, target, value);");
            }
            builder.AppendLine($"            return;");
            builder.AppendLine($"        }}");
        }

        // Add code for the handling of field data, given a class
        void GenerateClassImplementation(StringBuilder builder, CodegenClass klass)
        {
            builder.AppendLine($"public static class {klass.CodegenClassName} {{");
            builder.AppendLine($"    public static void Set(int fieldHash, object target, object value){{");
            builder.AppendLine($"        {klass.ClassType} behaviour = target as {klass.ClassType};");

            // Add code to handle each of the CrossSceneReference fields
            foreach (var member in klass.Members)
            {
                GenerateFieldImplementation(builder, member);
            }

            builder.AppendLine($"        throw new Exception($\"Unable to resolve field for fieldHash {{fieldHash}}.\");");
            builder.AppendLine($"    }}");
            builder.AppendLine($"}}");
        }

        // Add code for each of the fields
        void GenerateFieldImplementation(StringBuilder builder, CodegenClassMember member)
        {
            builder.AppendLine($"        if(fieldHash == {member.FieldHash}) {{");
            if (member.InfoWrapper.IsPublic())
            {
                builder.AppendLine($"            behaviour.{member.FieldName} = value as {member.FieldType};");
            }
            else
            {
                switch (member.InfoWrapper.MemberInfo.MemberType)
                {
                    case MemberTypes.Field:
                        builder.AppendLine($"            behaviour.GetType().GetField(\"{ member.FieldName}\", BindingFlags.Instance|BindingFlags.NonPublic).SetValue(behaviour, value); ");
                        break;
                    case MemberTypes.Property:
                        builder.AppendLine($"            behaviour.GetType().GetProperty(\"{ member.FieldName}\", BindingFlags.Instance|BindingFlags.NonPublic).SetValue(behaviour, value); ");
                        break;
                    default:
                        break;
                }
            }
            builder.AppendLine($"            return;");
            builder.AppendLine("        }");
        }
    }
}