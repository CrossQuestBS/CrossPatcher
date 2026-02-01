using System.IO;
using System.Linq;
using Mono.Cecil;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Patch.CrossPatcher
{
    public class Resourcer : IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder => 1000;

        private string _resourceFileName = "ResourcePaths.txt";
        
        private DefaultAssemblyResolver _resolver;
        
        public void InitializeResolver(string assemblyParentPath)
        {
            _resolver = new DefaultAssemblyResolver();

            _resolver.AddSearchDirectory(assemblyParentPath);
        }
        
        private AssemblyDefinition ReadAssembly(string path)
        {
            return AssemblyDefinition.ReadAssembly(path,
                new ReaderParameters { ReadWrite = true, InMemory = true, AssemblyResolver = _resolver });
        }



        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            var modfolder = Path.Combine(Application.dataPath, "Plugins", "Mods");

            var assemblyPath = report.GetFiles();
            
            
            var modsWithResources = Directory.GetDirectories(modfolder).Where(it => File.Exists(Path.Join(it, _resourceFileName))).ToArray();

            Debug.Log("Found mods with resource: " + string.Join(" ", modsWithResources));
            foreach (var modDirectory in modsWithResources)
            {
                var resourcePathsFile = Path.Join(modDirectory, _resourceFileName);
                var modName = Path.GetFileName(modDirectory).Split("Mod.")[1];

                            
                var assemblyBuildFile = assemblyPath.FirstOrDefault(it => it.path.EndsWith(Path.GetFileName(modDirectory) + ".dll"));

                var assemblypath = assemblyBuildFile.path ?? "";

                var assemblyParentPath = Directory.GetParent(assemblypath).FullName;

                if (assemblyParentPath is null)
                {
                    Debug.LogError("Failed to get parent path for: " + assemblypath);
                    return;
                }
                
                InitializeResolver(assemblyParentPath);

                var assembly = ReadAssembly(assemblypath);
                
                foreach (var path in File.ReadLines(resourcePathsFile))
                {
                    if (path.Trim() == "")
                        continue;
                    
                    var folder = Path.Join(modDirectory, path);
                    if (!Directory.Exists(folder))
                        continue;

                    var resourcePath = path.Contains("Mod") ? path.Split("Mod")[1] : path;
                    
                    var resourcePrefix = $"{modName}.{resourcePath}";

                    foreach (var file in Directory.GetFiles(folder))
                    {
                        if (file.EndsWith(".meta"))
                            continue;
                        
                        Debug.Log("Add resource: " + $"{resourcePrefix}.{Path.GetFileName(file)}");
                        var resource = new EmbeddedResource($"{resourcePrefix}.{Path.GetFileName(file)}",
                            ManifestResourceAttributes.Public, File.ReadAllBytes(file));
                        
                        assembly.MainModule.Resources.Add(resource);
                    }
                }
                assembly.Write(assemblypath);

            }
            
        }
    }
}