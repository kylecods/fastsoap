using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using FastSoap.Abstractions;
using Xunit;

namespace FastSoap.UnitTests
{

    /// <summary>
    /// Verifies assembly-level dependency boundaries using <see cref="PEReader"/> and <see cref="MetadataReader"/>.
    /// </summary>
    public sealed class AssemblyReferenceTests
    {

        #region Fields

        private static readonly string[] ForbiddenOraclePrefixes =
        [
            "CoreWCF",
            "SoapCore",
            "System.ServiceModel",
            "Microsoft.CodeAnalysis",
            "BenchmarkDotNet"
        ];

        #endregion

        #region Public Methods

        [Fact]
        public void FastSoapAbstractions_HasZeroDisallowedReferences()
        {
            string assemblyPath = typeof(SoapVersion).Assembly.Location;
            IReadOnlyList<string> references = ReadAssemblyReferences(assemblyPath);

            Assert.DoesNotContain(references, static name => name.StartsWith("System.Xml", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(references, static name => name.StartsWith("CloudNimble.XmlEssentials", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(references, static name => name.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
            AssertNoOracleReferences(references);
        }

        [Fact]
        public void FastSoapAspNetCore_HasZeroSystemXmlOrOracleReferences()
        {
            string assemblyPath = Path.Combine(AppContext.BaseDirectory, "FastSoap.AspNetCore.dll");
            Assert.True(File.Exists(assemblyPath));

            IReadOnlyList<string> references = ReadAssemblyReferences(assemblyPath);

            Assert.DoesNotContain(references, static name => name.StartsWith("System.Xml", StringComparison.OrdinalIgnoreCase));
            AssertNoOracleReferences(references);
        }

        [Fact]
        public void FastSoapCore_AndXmlEssentials_HaveZeroSystemXmlOrOracleReferences()
        {
            string coreAssemblyPath = Path.Combine(AppContext.BaseDirectory, "FastSoap.Core.dll");
            string xmlEssentialsAssemblyPath = Path.Combine(AppContext.BaseDirectory, "CloudNimble.XmlEssentials.dll");

            Assert.True(File.Exists(coreAssemblyPath));
            Assert.True(File.Exists(xmlEssentialsAssemblyPath));

            IReadOnlyList<string> coreReferences = ReadAssemblyReferences(coreAssemblyPath);
            IReadOnlyList<string> xmlEssentialsReferences = ReadAssemblyReferences(xmlEssentialsAssemblyPath);

            Assert.DoesNotContain(coreReferences, static name => name.StartsWith("System.Xml", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(coreReferences, static name => name.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
            AssertNoOracleReferences(coreReferences);

            Assert.DoesNotContain(xmlEssentialsReferences, static name => name.StartsWith("System.Xml", StringComparison.OrdinalIgnoreCase));
            AssertNoOracleReferences(xmlEssentialsReferences);
        }

        #endregion

        #region Private Methods

        private static void AssertNoOracleReferences(IReadOnlyList<string> references)
        {
            foreach (string reference in references)
            {
                bool isForbidden = ForbiddenOraclePrefixes.Any(
                    prefix => reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

                Assert.False(isForbidden, $"Unexpected disallowed assembly reference: {reference}");
            }
        }

        private static IReadOnlyList<string> ReadAssemblyReferences(string assemblyPath)
        {
            using FileStream stream = File.OpenRead(assemblyPath);
            using PEReader peReader = new PEReader(stream);
            MetadataReader metadataReader = peReader.GetMetadataReader();

            List<string> references = new List<string>(metadataReader.AssemblyReferences.Count);
            foreach (AssemblyReferenceHandle handle in metadataReader.AssemblyReferences)
            {
                AssemblyReference reference = metadataReader.GetAssemblyReference(handle);
                references.Add(metadataReader.GetString(reference.Name));
            }

            return references;
        }

        #endregion

    }

}
