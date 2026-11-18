using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Xunit.v3;

namespace CalmMouse.Core.Tests;

/// <summary>
/// Keeps the C# tests in step with the Swift ones. Every `func test…` in
/// Tests/CalmMouseCoreTests/*.swift must have a C# test method with exactly the same name
/// (for example `testFoo`), in a C# class with the same name as the Swift class it sits in.
/// A Swift file usually holds one class named after the file; a few hold several
/// (TapRecognizerTests.swift has eight), and each of those becomes its own C# class.
/// Only C# classes that share a name with a Swift test class are compared, so this class,
/// the golden-trace tests and the router tests are left alone.
/// </summary>
public sealed class ParityGuardTests
{
    /// <summary>
    /// Swift test files (name without ".swift") whose tests haven't been ported yet. Their
    /// comparison is skipped. Remove a file from here in the same commit that ports it; the
    /// guard fails if a C# class for a listed file already exists. Empty by the end of task 7.
    /// </summary>
    private static readonly HashSet<string> NotYetPorted = new(StringComparer.Ordinal)
    {
        "AppVersionTests",
        "ControllerPointerTests",
        "DeviceRulesTests",
        "FeatureTests",
        "MagicMouseReverseTests",
        "MouseFigureTests",
        "PresetTests",
        "ScrollBlockerTests",
        "SupportInviteTests",
        "TapRecognizerTests",
        "TapRightClickTests",
        "WheelMouseTests",
    };

    private const string SwiftTestsFolder = "Tests/CalmMouseCoreTests";

    [Fact]
    public void EverySwiftTestHasACSharpTestWithTheSameName()
    {
        string swiftFolder = Path.Combine(FindRepoRoot(), "Tests", "CalmMouseCoreTests");
        List<SwiftFile> swiftFiles = Directory.GetFiles(swiftFolder, "*.swift")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(SwiftFile.Read)
            .ToList();
        Assert.True(swiftFiles.Count > 0, $"No .swift files found in {swiftFolder}.");
        Assert.True(swiftFiles.Sum(f => f.Classes.Sum(c => c.Tests.Count)) > 0,
            $"No `func test…` found in any .swift file in {swiftFolder}. The guard's Swift reader needs updating.");
        ILookup<string, Type> csharpClasses = typeof(ParityGuardTests).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .ToLookup(t => t.Name, StringComparer.Ordinal);

        var problems = new StringBuilder();

        foreach (string stale in NotYetPorted
                     .Where(name => swiftFiles.All(f => f.Name != name))
                     .Order(StringComparer.Ordinal))
        {
            problems.AppendLine($"NotYetPorted lists \"{stale}\", but there is no {SwiftTestsFolder}/{stale}.swift. Remove it from the set.");
            problems.AppendLine();
        }

        foreach (SwiftFile file in swiftFiles)
        {
            var lines = new List<string>(file.ReadProblems.Select(p => "  " + p));

            if (file.Classes.Sum(c => c.Tests.Count) == 0)
            {
                lines.Add("  No `func test…` found in this file. If it does have tests, the guard's Swift reader needs updating.");
            }

            if (NotYetPorted.Contains(file.Name))
            {
                foreach (SwiftClass swiftClass in file.Classes.Where(c => csharpClasses.Contains(c.Name)))
                {
                    lines.Add($"  C# class {swiftClass.Name} exists, but \"{file.Name}\" is still in NotYetPorted. Remove it from the set so the names get compared.");
                }
            }
            else
            {
                foreach (SwiftClass swiftClass in file.Classes)
                {
                    lines.AddRange(Compare(swiftClass, csharpClasses[swiftClass.Name].ToList()));
                }
            }

            if (lines.Count > 0)
            {
                problems.AppendLine($"{file.Name}.swift");
                foreach (string line in lines)
                {
                    problems.AppendLine(line);
                }
                problems.AppendLine();
            }
        }

        if (problems.Length > 0)
        {
            Assert.Fail("The C# tests don't match the Swift tests.\n\n" + problems.ToString().TrimEnd());
        }
    }

    private static IEnumerable<string> Compare(SwiftClass swiftClass, List<Type> matches)
    {
        if (matches.Count == 0)
        {
            int count = swiftClass.Tests.Count;
            yield return $"  C# class {swiftClass.Name} not found ({count} Swift {(count == 1 ? "test" : "tests")} to port).";
            yield break;
        }
        if (matches.Count > 1)
        {
            string names = string.Join(", ", matches.Select(t => t.FullName).Order(StringComparer.Ordinal));
            yield return $"  More than one C# class is called {swiftClass.Name}: {names}. Keep one.";
            yield break;
        }

        HashSet<string> csharpTests = matches[0]
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.GetCustomAttributes(inherit: true).OfType<IFactAttribute>().Any())
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        List<string> missing = swiftClass.Tests.Where(t => !csharpTests.Contains(t)).ToList();
        List<string> extra = csharpTests.Where(t => !swiftClass.Tests.Contains(t)).Order(StringComparer.Ordinal).ToList();

        if (missing.Count > 0)
        {
            yield return $"  Missing in C# class {swiftClass.Name} ({missing.Count}):";
            foreach (string name in missing)
            {
                yield return $"    {name}";
            }
        }
        if (extra.Count > 0)
        {
            yield return $"  In C# class {swiftClass.Name} but not in Swift ({extra.Count}):";
            foreach (string name in extra)
            {
                yield return $"    {name}";
            }
        }
    }

    /// <summary>
    /// Walks up from the test binary's folder to the first folder that has both
    /// Tests/CalmMouseCoreTests and windows, so it works from any working directory.
    /// </summary>
    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Tests", "CalmMouseCoreTests")) &&
                Directory.Exists(Path.Combine(dir.FullName, "windows")))
            {
                return dir.FullName;
            }
        }
        throw new DirectoryNotFoundException(
            $"Couldn't find the repo root (a folder with {SwiftTestsFolder} and windows) above {AppContext.BaseDirectory}.");
    }

    private sealed record SwiftClass(string Name, List<string> Tests);

    /// <summary>
    /// The XCTest classes of one Swift file and their test methods, in file order, plus anything
    /// the reader couldn't make sense of (which fails the guard rather than being skipped quietly).
    /// </summary>
    private sealed record SwiftFile(string Name, List<SwiftClass> Classes, List<string> ReadProblems)
    {
        // `final class FooTests: XCTestCase {`
        private static readonly Regex ClassLine = new(
            @"^\s*(?:@\w+(?:\([^)]*\))?\s+)*(?:(?:public|internal|open|final)\s+)*class\s+(\w+)\s*:[^{]*\bXCTestCase\b");

        // `extension FooTests {` (tests in it belong to FooTests)
        private static readonly Regex ExtensionLine = new(
            @"^\s*(?:(?:public|internal|private|fileprivate)\s+)*extension\s+(\w+)\b");

        // `func testFoo() {`, `func testFoo() throws {`, `@MainActor func testFoo() async throws {`.
        // XCTest only runs parameterless, non-private instance methods named test… that return
        // nothing, so after `()` only `async`, `throws` and then `{` or the end of the line may follow.
        private static readonly Regex TestLine = new(
            @"^\s*(?:@\w+(?:\([^)]*\))?\s+)*(?:(?:public|internal|open|final|override|nonisolated)\s+)*func\s+(test\w*)\s*\(\s*\)\s*(?:async\s*)?(?:throws\s*)?(?:\{|$)");

        // Anything that looks like a test declaration. Each one must be a TestLine or an IntendedSkip.
        private static readonly Regex AnyTestFunc = new(@"\bfunc\s+test\w*\s*\(");

        // Not run by XCTest, so not a test: private, fileprivate, static or class func, or with parameters.
        private static readonly Regex IntendedSkip = new(
            @"\b(?:private|fileprivate|static|class)\b[^(]*\bfunc\s+test\w*\s*\(|\bfunc\s+test\w*\s*\(\s*[^)\s]");

        public static SwiftFile Read(string path)
        {
            string fileName = Path.GetFileNameWithoutExtension(path);
            string text = File.ReadAllText(path);
            var classes = new List<SwiftClass>();
            var problems = new List<string>();

            // The reader works line by line and doesn't know about block comments or multi-line
            // strings, so it refuses them instead of guessing.
            foreach (string construct in new[] { "/*", "\"\"\"" })
            {
                if (text.Contains(construct, StringComparison.Ordinal))
                {
                    problems.Add($"Unsupported construct: the file contains {construct}, which the guard's Swift reader can't follow. Use // comments and single-line strings, or update the reader.");
                }
            }

            // Tests before any class line (unusual) are filed under the file's name.
            string current = fileName;
            string[] rawLines = text.Split('\n');

            for (int i = 0; i < rawLines.Length; i++)
            {
                string rawLine = rawLines[i].TrimEnd();
                int comment = rawLine.IndexOf("//", StringComparison.Ordinal);
                string line = comment >= 0 ? rawLine[..comment] : rawLine;

                Match match = ClassLine.Match(line);
                if (!match.Success)
                {
                    match = ExtensionLine.Match(line);
                }
                if (match.Success)
                {
                    current = match.Groups[1].Value;
                    continue;
                }

                match = TestLine.Match(line);
                if (match.Success)
                {
                    SwiftClass? swiftClass = classes.Find(c => c.Name == current);
                    if (swiftClass is null)
                    {
                        swiftClass = new SwiftClass(current, []);
                        classes.Add(swiftClass);
                    }
                    swiftClass.Tests.Add(match.Groups[1].Value);
                }
                else if (AnyTestFunc.IsMatch(line) && !IntendedSkip.IsMatch(line))
                {
                    problems.Add($"{fileName}.swift:{i + 1}: can't read this test declaration: {line.Trim()}");
                }
            }

            return new SwiftFile(fileName, classes, problems);
        }
    }
}
