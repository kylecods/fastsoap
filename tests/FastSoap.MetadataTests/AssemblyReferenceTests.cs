using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using FastSoap.Metadata;
using Xunit;

namespace FastSoap.MetadataTests
{

    /// <summary>
    /// Verifies that <c>FastSoap.Metadata.dll</c> has zero <c>System.Xml*</c>, ASP.NET Core, Roslyn, or oracle dependencies.
    /// </summary>
    public sealed class AssemblyReferenceTests
    {

        #region Fields

        private static readonly string[] ForbiddenPrefixes =
        [
            "System.Xml",
            "Microsoft.AspNetCore",
            "Microsoft.CodeAnalysis",
            "CoreWCF",
            "SoapCore",
            "System.ServiceModel",
            "BenchmarkDotNet"
        ];

        #endregion

        #region Public Methods

        [Fact]
        public void FastSoapMetadata_HasZeroSystemXmlOrDisallowedReferences()
        {
            string metadataAssemblyPath = typeof(SoapWsdlGenerator).Assembly.Location;
            Assert.True(File.Exists(metadataAssemblyPath));

            IReadOnlyList<string> metadataReferences = ReadAssemblyReferences(metadataAssemblyPath);

            Assert.Contains(metadataReferences, static name => string.Equals(name, "FastSoap.Abstractions", StringComparison.Ordinal));
            Assert.Contains(metadataReferences, static name => string.Equals(name, "CloudNimble.XmlEssentials", StringComparison.Ordinal));

            foreach (string reference in metadataReferences)
            {
                bool isForbidden = ForbiddenPrefixes.Any(
                    prefix => reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

                Assert.False(isForbidden, $"Disallowed assembly reference detected in FastSoap.Metadata.dll: {reference}");
            }

            string xmlEssentialsPath = Path.Combine(AppContext.BaseDirectory, "CloudNimble.XmlEssentials.dll");
            Assert.True(File.Exists(xmlEssentialsPath));

            IReadOnlyList<string> xmlEssentialsReferences = ReadAssemblyReferences(xmlEssentialsPath);
            Assert.DoesNotContain(
                xmlEssentialsReferences,
                static name => name.StartsWith("System.Xml", StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        #region Private Methods

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
