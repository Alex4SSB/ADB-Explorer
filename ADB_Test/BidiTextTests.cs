using ADB_Explorer.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ADB_Test;

[TestClass]
public class BidiTextTests
{
    [TestMethod]
    public void GuardLtrInRtl_WrapsLatinNameInLtrMarks()
    {
        Assert.AreEqual("\u200E(Nothing Phone (3a)\u200E", TextHelper.GuardLtrInRtl("(Nothing Phone (3a)"));
    }

    [TestMethod]
    public void GuardLtrInRtl_LeavesRtlAndEmptyTextAlone()
    {
        Assert.AreEqual("טלפון (2)", TextHelper.GuardLtrInRtl("טלפון (2)"));
        Assert.AreEqual("", TextHelper.GuardLtrInRtl(""));
    }
}
