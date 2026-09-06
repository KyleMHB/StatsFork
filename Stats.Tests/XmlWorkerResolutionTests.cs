using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using NUnit.Framework;

namespace Stats.Tests;

[TestFixture]
public sealed class XmlWorkerResolutionTests
{
    [Test]
    public void Every_table_and_column_worker_class_in_mod_xml_resolves_in_a_built_project_assembly()
    {
        string repositoryRoot = FindRepositoryRoot();
        IReadOnlyList<Assembly> assemblies = LoadBuiltProjectAssemblies(repositoryRoot);
        var unresolved = new List<string>();
        int workerClassCount = 0;

        foreach (string xmlFile in FindModXmlFiles(repositoryRoot))
        {
            XDocument document = XDocument.Load(xmlFile, LoadOptions.SetBaseUri);
            foreach (XElement element in document.Descendants()
                .Where(candidate => candidate.Name.LocalName == "workerClass"))
            {
                string className = element.Value.Trim();
                workerClassCount++;
                if (className.Length == 0)
                {
                    unresolved.Add($"{DisplayPath(repositoryRoot, xmlFile)}: empty workerClass");
                    continue;
                }

                int assemblySeparator = className.IndexOf(',');
                string typeName = (assemblySeparator < 0
                        ? className
                        : className.Substring(0, assemblySeparator))
                    .Trim();
                Type? workerType = assemblies
                    .Select(assembly => assembly.GetType(typeName, throwOnError: false, ignoreCase: false))
                    .FirstOrDefault(type => type != null);

                if (workerType == null)
                {
                    unresolved.Add($"{DisplayPath(repositoryRoot, xmlFile)}: {className}");
                }
            }
        }

        Assert.That(workerClassCount, Is.GreaterThan(0), "No workerClass elements were found in the mod XML.");
        Assert.That(unresolved, Is.Empty, string.Join(Environment.NewLine, unresolved));
    }

    private static IEnumerable<string> FindModXmlFiles(string repositoryRoot)
    {
        foreach (string module in new[] { "Core", "Biotech", "Anomaly", "CE", "Odyssey" })
        {
            string moduleRoot = Path.Combine(repositoryRoot, module);
            if (!Directory.Exists(moduleRoot))
            {
                continue;
            }

            foreach (string xmlFile in Directory.GetFiles(moduleRoot, "*.xml", SearchOption.AllDirectories))
            {
                if (xmlFile.IndexOf($"{Path.DirectorySeparatorChar}Source{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase) >= 0
                    || xmlFile.IndexOf($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase) >= 0
                    || xmlFile.IndexOf($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase) >= 0
                    || xmlFile.IndexOf($"{Path.DirectorySeparatorChar}Assemblies{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                yield return xmlFile;
            }
        }
    }

    private static IReadOnlyList<Assembly> LoadBuiltProjectAssemblies(string repositoryRoot)
    {
        var assemblyPaths = new List<string>();
        foreach (string module in new[] { "Core", "Biotech", "Anomaly", "CE", "Odyssey" })
        {
            string assemblyDirectory = Path.Combine(repositoryRoot, module, "Assemblies");
            if (Directory.Exists(assemblyDirectory))
            {
                assemblyPaths.AddRange(Directory.GetFiles(assemblyDirectory, "*.dll"));
            }
        }

        Assert.That(assemblyPaths, Is.Not.Empty,
            "The production assemblies are missing. Build the solution before running XML worker validation.");
        return assemblyPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(Assembly.LoadFrom)
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        string? directory = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "Stats.sln")))
            {
                return directory!;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new AssertionException("Could not locate the repository root from the test output directory.");
    }

    private static string DisplayPath(string repositoryRoot, string path) =>
        path.Substring(repositoryRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
