using NUnit.Framework;

namespace Sparky.Tests.Regression;

[TestFixture]
public class RegressionTests
{
    private static string TestDataDir => Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "testdata", "regression");

    [Test]
    [TestCaseSource(nameof(GetRegressionTestFiles))]
    public void RunRegressionTest(string testFile)
    {
        var results = RegressionTestRunner.RunTestFile(testFile);

        var failures = results.Where(r => !r.Passed).ToList();
        if (failures.Count > 0)
        {
            var messages = failures.Select(f => $"Line {f.LineNumber}: {f.Message}");
            Assert.Fail($"Assertion failures in {Path.GetFileName(testFile)}:\n{string.Join("\n", messages)}");
        }

        Assert.That(results.Count, Is.GreaterThan(0), "Test file should contain at least one assertion");
    }

    private static IEnumerable<TestCaseData> GetRegressionTestFiles()
    {
        var testDataDir = TestDataDir;
        if (!Directory.Exists(testDataDir))
        {
            yield break;
        }

        foreach (var file in Directory.GetFiles(testDataDir, "*.jsonl"))
        {
            yield return new TestCaseData(file).SetName(Path.GetFileNameWithoutExtension(file));
        }
    }

}
