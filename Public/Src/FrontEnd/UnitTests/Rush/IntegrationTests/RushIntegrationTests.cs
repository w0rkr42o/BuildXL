// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Engine.Tracing;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.EngineTestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Rush
{
    public class RushIntegrationTests : RushIntegrationTestBase
    {
        public RushIntegrationTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void EndToEndPipExecutionWithDependencies()
        {
            // Create two projects A and B such that A -> B.

            const string ProjectA = "a.js";
            var projectAPath = R("src", "A", ProjectA);
            const string PackageJSonA = "package.json";
            var pathToPackageJSonA = R("src", "A", PackageJSonA);

            const string ProjectB = "b.js";
            var projectBPath = R("src", "B", ProjectB);
            const string PackageJSonB = "package.json";
            var pathToPackageJSonB = R("src", "B", PackageJSonB);

            var config = Build(new Dictionary<string, string> { ["PATH"] = PathToNodeFolder})
                    .AddSpec(projectAPath, "module.exports = function A(){}")
                    .AddSpec(pathToPackageJSonA, CreatePackageJson("@ms/project-A", "a.js", new string[] { }))
                    .AddSpec(projectBPath, "const A = require('@ms/project-A'); return A();")
                    .AddSpec(pathToPackageJSonB, CreatePackageJson("@ms/project-B", "b.js", new string[] { "@ms/project-A" }))
                    .PersistSpecsAndGetConfiguration();

            var engineResult = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
                ("src/B", "@ms/project-B"),
            });

            Assert.True(engineResult.IsSuccess);
            
            // Let's do some basic graph validations
            var processes = engineResult.EngineState.PipGraph.RetrievePipsOfType(global::BuildXL.Pips.Operations.PipType.Process).ToList();
            // There should be two process pips
            Assert.Equal(2, processes.Count);

            // Project A depends on project B
            var projectAPip = processes.First(pip => ((Process)pip).Provenance.OutputValueSymbol.ToString(engineResult.EngineState.SymbolTable).Contains("project_A"));
            var projectBPip = processes.First(pip => ((Process)pip).Provenance.OutputValueSymbol.ToString(engineResult.EngineState.SymbolTable).Contains("project_B"));
            Assert.True(engineResult.EngineState.PipGraph.IsReachableFrom(projectAPip.PipId.ToNodeId(), projectBPip.PipId.ToNodeId()));
        }

        [Fact]
        public void RushCacheGraphBehavior()
        {
            var testCache = new TestCache();

            const string ProjectA = "a.js";
            var projectAPath = R("src", "A", ProjectA);
            const string PackageJSonA = "package.json";
            var pathToPackageJSonA = R("src", "A", PackageJSonA);

            var config = (CommandLineConfiguration)Build(new Dictionary<string, string> { ["PATH"] = PathToNodeFolder })
                    .AddSpec(projectAPath, "module.exports = function A(){}")
                    .AddSpec(pathToPackageJSonA, CreatePackageJson("@ms/project-A", "a.js", new string[] { }))
                    .PersistSpecsAndGetConfiguration();

            config.Cache.CacheGraph = true;
            config.Cache.AllowFetchingCachedGraphFromContentCache = true;
            config.Cache.Incremental = true;

            // First time the graph should be computed
            var engineResult = RunRushProjects(
                config, 
                new[] {("src/A", "@ms/project-A") },
                testCache);

            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
            AssertLogContains(false, "Storing pip graph descriptor to cache: Status: Success");

            // The second build should fetch and reuse the graph from the cache
            engineResult = RunRushProjects(
                config, 
                new[] {("src/A", "@ms/project-A")},
                testCache);

            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
        }

        [Fact]
        public void ExposedEnvironmentVariablesBehavior()
        {
            var testCache = new TestCache();

            const string ProjectA = "a.js";
            var projectAPath = R("src", "A", ProjectA);
            const string PackageJSonA = "package.json";
            var pathToPackageJSonA = R("src", "A", PackageJSonA);

            // Set env var 'Test' to an arbitrary value, but override that value in the main config, so
            // we actually don't depend on it
            Environment.SetEnvironmentVariable("Test", "2");

            var config = (CommandLineConfiguration)Build(new Dictionary<string, string> { ["PATH"] = PathToNodeFolder, ["Test"] = "3" })
                    .AddSpec(projectAPath, "module.exports = function A(){}")
                    .AddSpec(pathToPackageJSonA, CreatePackageJson("@ms/project-A", "a.js", new string[] { }))
                    .PersistSpecsAndGetConfiguration();

            config.Cache.CacheGraph = true;
            config.Cache.AllowFetchingCachedGraphFromContentCache = true;
            config.Cache.Incremental = true;

            // First time the graph should be computed
            var engineResult = RunRushProjects(
                config,
                new[] { ("src/A", "@ms/project-A") },
                testCache);
            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
            AssertLogContains(false, "Storing pip graph descriptor to cache: Status: Success");

            // Change the environment variable. Since we overrode the value, the second
            // build should fetch and reuse the graph from the cache
            Environment.SetEnvironmentVariable("Test", "3");
            engineResult = RunRushProjects(
                config,
                new[] { ("src/A", "@ms/project-A") },
                testCache);
            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
        }

        [Fact]
        public void PassthroughVariablesAreHonored()
        {
            var testCache = new TestCache();

            const string ProjectA = "a.js";
            var projectAPath = R("src", "A", ProjectA);
            const string PackageJSonA = "package.json";
            var pathToPackageJSonA = R("src", "A", PackageJSonA);

            Environment.SetEnvironmentVariable("Test", "originalValue");

            var environment = new Dictionary<string, DiscriminatingUnion<string, UnitValue>> { 
                ["PATH"] = new DiscriminatingUnion<string, UnitValue>(PathToNodeFolder), 
                ["Test"] = new DiscriminatingUnion<string, UnitValue>(UnitValue.Unit) };

            var config = (CommandLineConfiguration)Build(environment)
                .AddSpec(projectAPath, "module.exports = function A(){}")
                .AddSpec(pathToPackageJSonA, CreatePackageJson("@ms/project-A", "a.js", new string[] { }))
                .PersistSpecsAndGetConfiguration();

            config.Cache.CacheGraph = true;
            config.Cache.AllowFetchingCachedGraphFromContentCache = true;
            config.Cache.Incremental = true;

            // First time the graph should be computed
            var engineResult = RunRushProjects(
                config,
                new[] { ("src/A", "@ms/project-A") },
                testCache);

            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
            AssertLogContains(false, "Storing pip graph descriptor to cache: Status: Success");

            Environment.SetEnvironmentVariable("Test", "modifiedValue");

            engineResult = RunRushProjects(
                config,
                new[] { ("src/A", "@ms/project-A") },
                testCache);

            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
        }
    }
}
