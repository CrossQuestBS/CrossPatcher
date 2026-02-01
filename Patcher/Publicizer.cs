using System.IO;
using System.Linq;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Patch.CrossPatcher
{
    public class Publicizer : IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder => -1;
        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            Debug.Log("start: publicizer");
            var originalFiles = report.GetFiles().Where(it => it.path.EndsWith(".dll")).ToList();

            var projectPath = Directory.GetParent(Application.dataPath).FullName;
            
            foreach (var file in Directory.GetFiles(Path.Join(projectPath, "Publicized")))
            {
                var fileName = Path.GetFileName(file);

                var potentialFile = originalFiles.Select(it => it.path).FirstOrDefault(path => Path.GetFileName(path) == fileName);

                if (potentialFile is not null)
                {
                    Debug.Log("Removing file: " + potentialFile);
                    File.Delete(potentialFile);
                    Debug.Log("Replacing with : " + file);
                    File.Copy(file, potentialFile);
                }

            }
            Debug.Log("end: publicizer");
        }
    }
}