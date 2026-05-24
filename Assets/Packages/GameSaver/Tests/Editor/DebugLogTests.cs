using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ThanhDV.GameSaver.Common;

namespace ThanhDV.GameSaver.Tests.Editor
{
    /// <summary>
    /// Tests for <see cref="DebugLog"/> sanitization helpers and Conditional attribute wiring (cluster #19).
    /// Tests execute in Editor (UNITY_EDITOR defined) so sanitize helpers return full info paths/exceptions.
    /// Build behavior is verified indirectly via the Conditional attribute presence on Log/Success.
    /// </summary>
    public class DebugLogTests
    {
        // ===================================================================
        // SanitizePath
        // ===================================================================

        [Test]
        public void SanitizePath_InEditor_ReturnsFullPath()
        {
            string fullPath = @"C:\Users\TestUser\AppData\LocalLow\Company\Game\saves\Slot1\save.sav";
            Assert.That(DebugLog.SanitizePath(fullPath), Is.EqualTo(fullPath),
                "In Editor builds, sanitize must preserve the full path for debugging.");
        }

        [Test]
        public void SanitizePath_NullInput_ReturnsNull()
        {
            Assert.That(DebugLog.SanitizePath(null), Is.Null,
                "Null input returns null in both Editor and build.");
        }

        [Test]
        public void SanitizePath_EmptyInput_ReturnsEmpty()
        {
            Assert.That(DebugLog.SanitizePath(string.Empty), Is.EqualTo(string.Empty),
                "Empty input returns empty in both Editor and build (no crash).");
        }

        [Test]
        public void SanitizePath_OnlyFileName_StillReturnsInputInEditor()
        {
            // No directory component — Editor returns as-is.
            string fileName = "save.sav";
            Assert.That(DebugLog.SanitizePath(fileName), Is.EqualTo(fileName));
        }

        // ===================================================================
        // SanitizeException
        // ===================================================================

        [Test]
        public void SanitizeException_InEditor_ReturnsFullToString()
        {
            // ToString includes type name, message, and stack trace.
            // Force a real stack trace by throwing + catching.
            Exception caught = null;
            try { throw new InvalidOperationException("specific test message"); }
            catch (Exception e) { caught = e; }

            string sanitized = DebugLog.SanitizeException(caught);

            Assert.That(sanitized, Does.Contain("specific test message"));
            Assert.That(sanitized, Does.Contain(nameof(InvalidOperationException)),
                "Editor sanitize must include exception type (part of ToString format).");
        }

        [Test]
        public void SanitizeException_NullInput_ReturnsPlaceholder()
        {
            Assert.That(DebugLog.SanitizeException(null), Is.EqualTo("<null>"),
                "Null exception returns placeholder string (no NRE).");
        }

        [Test]
        public void SanitizeException_PreservesMessage_AcrossBuildModes()
        {
            // Whether Editor or build, the Message must be reachable in sanitized output.
            // (Editor returns ToString which contains Message; build returns Message directly.)
            Exception e = new ArgumentException("crucial detail");
            string sanitized = DebugLog.SanitizeException(e);
            Assert.That(sanitized, Does.Contain("crucial detail"),
                "Message is the minimum sanitized output guaranteed in all build modes.");
        }

        // ===================================================================
        // Conditional attribute wiring (verifies strip behavior at compile time)
        // ===================================================================

        [Test]
        public void Log_HasUnityEditorConditionalAttribute()
        {
            AssertHasConditional(nameof(DebugLog.Log), "UNITY_EDITOR");
        }

        [Test]
        public void Success_HasUnityEditorConditionalAttribute()
        {
            AssertHasConditional(nameof(DebugLog.Success), "UNITY_EDITOR");
        }

        [Test]
        public void Warning_DoesNotHaveConditionalAttribute()
        {
            AssertNoConditional(nameof(DebugLog.Warning),
                "Warning must remain visible in built games — it signals real issues developers need to see.");
        }

        [Test]
        public void Error_DoesNotHaveConditionalAttribute()
        {
            AssertNoConditional(nameof(DebugLog.Error),
                "Error must remain visible in built games — it signals real issues developers need to see.");
        }

        // ===================================================================
        // Helpers
        // ===================================================================

        private static void AssertHasConditional(string methodName, string expectedSymbol)
        {
            MethodInfo method = typeof(DebugLog).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, $"Method {methodName} must exist on DebugLog.");

            ConditionalAttribute[] conditionals = method.GetCustomAttributes<ConditionalAttribute>(inherit: false).ToArray();
            Assert.That(conditionals.Any(a => a.ConditionString == expectedSymbol), Is.True,
                $"{methodName} must have [Conditional(\"{expectedSymbol}\")] so call sites are stripped in non-Editor builds.");
        }

        private static void AssertNoConditional(string methodName, string reason)
        {
            MethodInfo method = typeof(DebugLog).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, $"Method {methodName} must exist on DebugLog.");

            ConditionalAttribute[] conditionals = method.GetCustomAttributes<ConditionalAttribute>(inherit: false).ToArray();
            Assert.That(conditionals, Is.Empty, reason);
        }
    }
}
