using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace MartianMap.Edits.Tests
{
    /// <summary>
    /// Every file and folder Unity imports from the package has its committed .meta, and every
    /// .meta has its asset (docs/guide/chapters/convenciones.md, "Forja y carpetas"). Unity skips
    /// names that start with a dot (.build/) or end with ~ (Tests~/).
    /// </summary>
    public class PackageMetaTests
    {
        [Test]
        public void EveryPackageFileHasItsMeta()
        {
            string root = PackageRoot();
            var missing = new List<string>();
            var orphans = new List<string>();
            Walk(root, root, missing, orphans);
            Assert.That(missing, Is.Empty, "Assets without a .meta: open the package in Unity and commit the .meta it writes.");
            Assert.That(orphans, Is.Empty, ".meta files without an asset.");
        }

        private static void Walk(string root, string dir, List<string> missing, List<string> orphans)
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(dir).OrderBy(p => p, StringComparer.Ordinal))
            {
                string name = Path.GetFileName(path);
                if (name.StartsWith(".", StringComparison.Ordinal) || name.EndsWith("~", StringComparison.Ordinal))
                {
                    continue;
                }

                string relative = Path.GetRelativePath(root, path);
                if (name.EndsWith(".meta", StringComparison.Ordinal))
                {
                    string asset = path.Substring(0, path.Length - ".meta".Length);
                    if (!File.Exists(asset) && !Directory.Exists(asset))
                    {
                        orphans.Add(relative);
                    }

                    continue;
                }

                if (!File.Exists(path + ".meta"))
                {
                    missing.Add(relative);
                }

                if (Directory.Exists(path))
                {
                    Walk(root, path, missing, orphans);
                }
            }
        }

        /// <summary>The package folder: the nearest ancestor of the test output that holds MartianMap.Edits.asmdef.</summary>
        private static string PackageRoot()
        {
            for (var d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); d != null; d = d.Parent)
            {
                if (File.Exists(Path.Combine(d.FullName, "MartianMap.Edits.asmdef")))
                {
                    return d.FullName;
                }
            }

            throw new DirectoryNotFoundException("No MartianMap.Edits.asmdef above the test output.");
        }
    }
}
