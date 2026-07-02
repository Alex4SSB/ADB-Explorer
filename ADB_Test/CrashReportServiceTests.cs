using ADB_Explorer.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Reflection;

namespace ADB_Test;

[TestClass]
public class CrashReportServiceTests
{
    [TestMethod]
    public void GetReportableException_UnwrapsTargetInvocationException()
    {
        var inner = new NullReferenceException("DevicesObject was null");
        var outer = new TargetInvocationException("Exception has been thrown by the target of an invocation.", inner);

        var reportable = CrashReportService.GetReportableException(outer);

        Assert.AreSame(inner, reportable);
    }

    [TestMethod]
    public void GetReportableException_UnwrapsSingleInnerAggregateException()
    {
        var inner = new InvalidOperationException("root cause");
        var outer = new AggregateException(inner);

        var reportable = CrashReportService.GetReportableException(outer);

        Assert.AreSame(inner, reportable);
    }

    [TestMethod]
    public void GetReportableException_KeepsAggregateExceptionWithMultipleInners()
    {
        var outer = new AggregateException(
            new InvalidOperationException("first"),
            new InvalidOperationException("second"));

        var reportable = CrashReportService.GetReportableException(outer);

        Assert.AreSame(outer, reportable);
    }

    [TestMethod]
    public void GetReportableException_UnwrapsNestedTargetInvocationExceptions()
    {
        var root = new NullReferenceException("root");
        var nested = new TargetInvocationException("middle", root);
        var outer = new TargetInvocationException("outer", nested);

        var reportable = CrashReportService.GetReportableException(outer);

        Assert.AreSame(root, reportable);
    }

    [TestMethod]
    public void BuildPayloadJson_UsesUnwrappedExceptionAsPrimary()
    {
        var inner = new NullReferenceException("Object reference not set to an instance of an object.");
        var outer = new TargetInvocationException("Exception has been thrown by the target of an invocation.", inner);

        var json = InvokeBuildPayloadJson(outer, CrashReportService.GetReportableException(outer));

        StringAssert.Contains(json, "\"type\":\"System.NullReferenceException\"");
        StringAssert.Contains(json, "Object reference not set to an instance of an object.");
        StringAssert.Contains(json, "\"wrapperException\"");
        StringAssert.Contains(json, "TargetInvocationException");
        StringAssert.Contains(json, "\"fullException\"");
    }

    private static string InvokeBuildPayloadJson(Exception original, Exception reportable)
    {
        var method = typeof(CrashReportService).GetMethod(
            "BuildPayloadJson",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method);
        return (string)method.Invoke(null, [original, reportable])!;
    }
}
