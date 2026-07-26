#if UNITY_EDITOR

using System;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMAGeneratorRuntimeStatisticsTests
    {
        private GameObject generatorObject;
        private UMAGenerator generator;

        [SetUp]
        public void SetUp()
        {
            generatorObject = new GameObject("Runtime CSV Generator");
            generatorObject.SetActive(false);
            generator = generatorObject.AddComponent<UMAGenerator>();
            generator.atlasResolution = 512;
            generator.IterationCount = 2;
            generator.InterFrameDelay = 1;
            generator.MaxMultiStepWorkMilliseconds = 2f;
        }

        [TearDown]
        public void TearDown()
        {
            if (generatorObject != null)
            {
                UnityEngine.Object.DestroyImmediate(generatorObject);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("GeneratorStatistics")]
        public void CsvIncludesRuntimeFrameGeneratorAndAtomicStepTiming()
        {
            generator.ElapsedTicks = Stopwatch.Frequency / 2;
            generator.TextureChanged = 2;
            generator.textureprocessingTicks = Stopwatch.Frequency / 5;

            InvokePrivate(
                "RecordMultiStepAtomicStep",
                "Schedule Renderer",
                1.25f);
            InvokePrivate(
                "RecordMultiStepAtomicStep",
                "Schedule Renderer",
                2.75f);
            InvokePrivate(
                "RecordMultiStepBudgetOverrun",
                "Schedule Renderer",
                2.75f,
                0.75f);

            var capture =
                new UMAGeneratorRuntimeStatistics.RuntimeCapture(
                    120,
                    2d,
                    2000d,
                    25d);
            string csv = UMAGeneratorRuntimeStatistics.CreateCsv(
                generator,
                capture,
                new DateTime(
                    2026, 7, 25, 12, 30, 0, DateTimeKind.Utc),
                "Crowd, Build");

            StringAssert.StartsWith(
                "Captured UTC,Scene,Application Version",
                csv);
            StringAssert.Contains("\"Crowd, Build\"", csv);
            StringAssert.Contains(",2,120,", csv);
            StringAssert.Contains(
                ",Generator Phase,Generator Work,0,500,",
                csv);
            StringAssert.Contains(
                ",Incremental Step,Schedule Renderer,2,4,2,2.75,1,0.75,",
                csv);
        }

        [Test]
        [Category("UMA")]
        [Category("GeneratorStatistics")]
        public void ResetStatisticsRemovesPreviouslyRecordedAtomicSteps()
        {
            InvokePrivate(
                "RecordMultiStepAtomicStep",
                "Temporary Step",
                3f);
            generator.ResetStatistics();

            string csv = UMAGeneratorRuntimeStatistics.CreateCsv(
                generator,
                new UMAGeneratorRuntimeStatistics.RuntimeCapture(),
                DateTime.UtcNow,
                "Crowd");

            StringAssert.DoesNotContain("Temporary Step", csv);
        }

        private void InvokePrivate(string methodName, params object[] arguments)
        {
            MethodInfo method = typeof(UMAGeneratorBuiltin).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method, $"Expected runtime timing method '{methodName}'.");
            method.Invoke(generator, arguments);
        }
    }
}

#endif
