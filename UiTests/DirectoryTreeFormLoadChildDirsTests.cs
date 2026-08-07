using System.Reflection;
using System.Windows.Forms;
using CoderCommander.WinForms;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression tests for <see cref="DirectoryTreeForm"/>'s child-node reconciliation: the original
/// fix for stale tree data (re-scan on every expand, not just the first) used a plain
/// Nodes.Clear()-and-rebuild, which - caught by an ultrareview pass on that same commit - destroys
/// the entire previously-expanded descendant subtree on every re-expand of an ancestor, since
/// TreeNodeCollection.Clear() removes descendants along with their parent. LoadChildDirs is
/// private static and operates on plain TreeNode objects with no live Form/Control needed -
/// invoked via reflection.
/// </summary>
public class DirectoryTreeFormLoadChildDirsTests
{
    private static readonly MethodInfo LoadChildDirs = typeof(DirectoryTreeForm)
        .GetMethod("LoadChildDirs", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void Invoke(TreeNode parent, string path) =>
        LoadChildDirs.Invoke(null, new object[] { parent, path });

    private string _root = "";

    [SetUp]
    public void CreateTempTree()
    {
        _root = Directory.CreateTempSubdirectory("cc_dirtree_test_").FullName;
    }

    [TearDown]
    public void DeleteTempTree()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void ReExpandingAncestor_PreservesAlreadyLoadedDescendantSubtree()
    {
        var bPath = Directory.CreateDirectory(Path.Combine(_root, "B")).FullName;
        var cPath = Directory.CreateDirectory(Path.Combine(bPath, "C")).FullName;

        var root = new TreeNode { Tag = _root };
        Invoke(root, _root);
        var bNode = root.Nodes.Cast<TreeNode>().Single();
        Assert.That(bNode.Text, Is.EqualTo("B"));

        // Simulates the user expanding B: replaces its dummy "..." with the real child C.
        Invoke(bNode, bPath);
        Assert.That(bNode.Nodes.Cast<TreeNode>().Single().Text, Is.EqualTo("C"));

        // Simulates collapsing and re-expanding the root (OnBeforeExpand firing on root again).
        Invoke(root, _root);

        var bNodeAfter = root.Nodes.Cast<TreeNode>().Single();
        Assert.That(ReferenceEquals(bNodeAfter, bNode), Is.True,
            "B's TreeNode instance must be preserved, not replaced, when its folder still exists");
        Assert.That(bNodeAfter.Nodes.Cast<TreeNode>().Single().Text, Is.EqualTo("C"),
            "B's already-loaded child C must survive re-expanding the root - not collapse back to the \"...\" placeholder");
    }

    [Test]
    public void ReExpanding_PicksUpExternallyCreatedAndRemovedFolders()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Existing"));

        var root = new TreeNode { Tag = _root };
        Invoke(root, _root);
        Assert.That(root.Nodes.Cast<TreeNode>().Select(n => n.Text), Is.EquivalentTo(new[] { "Existing" }));

        Directory.CreateDirectory(Path.Combine(_root, "NewOne"));
        Directory.Delete(Path.Combine(_root, "Existing"));

        Invoke(root, _root);
        Assert.That(root.Nodes.Cast<TreeNode>().Select(n => n.Text), Is.EquivalentTo(new[] { "NewOne" }));
    }

    [Test]
    public void FirstLoad_LeavesNoStrayDummyNodeAlongsideRealChildren()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Child"));

        var root = new TreeNode { Tag = _root };
        Invoke(root, _root);

        Assert.That(root.Nodes.Count, Is.EqualTo(1));
        Assert.That(root.Nodes[0].Text, Is.EqualTo("Child"));
    }
}
