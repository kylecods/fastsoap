using System;
using FastSoap.Abstractions;
using FastSoap.Metadata;
using Xunit;

namespace FastSoap.MetadataTests
{

    /// <summary>
    /// Fail-first validation tests for <see cref="SoapSchemaType"/>, <see cref="SoapSchemaElement"/>,
    /// <see cref="SoapOperationMetadata"/>, and <see cref="SoapServiceMetadata"/>.
    /// </summary>
    public sealed class SoapContractModelsValidationTests
    {

        #region Public Methods

        [Fact]
        public void SoapOperationMetadata_RejectsQNameOrBoundsMismatch()
        {
            SoapOperationDescriptor descriptor = new SoapOperationDescriptor(
                "Ping",
                "urn:test/Ping",
                null,
                new SoapQName("urn:test", "Ping"),
                new SoapQName("urn:test", "PingResponse"));

            SoapSchemaElement matchingRequest = SoapSchemaElement.CreateWrapped(new SoapQName("urn:test", "Ping"));
            SoapSchemaElement matchingResponse = SoapSchemaElement.CreateWrapped(new SoapQName("urn:test", "PingResponse"));
            SoapSchemaElement wrongName = SoapSchemaElement.CreateWrapped(new SoapQName("urn:test", "Other"));
            SoapSchemaElement wrongNamespace = SoapSchemaElement.CreateWrapped(new SoapQName("urn:other", "PingResponse"));

            Assert.Throws<ArgumentException>(() => new SoapOperationMetadata(descriptor, wrongName, matchingResponse));
            Assert.Throws<ArgumentException>(() => new SoapOperationMetadata(descriptor, matchingRequest, wrongNamespace));
        }

        [Fact]
        public void SoapSchemaElement_RejectsInvalidOccurrenceBoundsAndUnqualifiedGlobals()
        {
            Assert.Throws<ArgumentException>(
                static () => SoapSchemaElement.CreateGlobal(new SoapQName(string.Empty, "Ping"), SoapSchemaType.String));

            Assert.Throws<ArgumentException>(
                static () => SoapSchemaElement.CreateWrapped(new SoapQName(string.Empty, "Ping")));

            Assert.Throws<ArgumentOutOfRangeException>(
                static () => SoapSchemaElement.CreateMember("Item", SoapSchemaType.String, minOccurs: -1, maxOccurs: 1));

            Assert.Throws<ArgumentOutOfRangeException>(
                static () => SoapSchemaElement.CreateMember("Item", SoapSchemaType.String, minOccurs: 0, maxOccurs: 0));

            Assert.Throws<ArgumentOutOfRangeException>(
                static () => SoapSchemaElement.CreateMember("Item", SoapSchemaType.String, minOccurs: 2, maxOccurs: 1));

            Assert.Throws<ArgumentException>(
                static () => SoapSchemaElement.CreateMember("bad:name", SoapSchemaType.String));
        }

        [Fact]
        public void SoapSchemaType_RejectsUnsupportedShapesReservedNamespacesAndDuplicates()
        {
            Assert.Throws<ArgumentException>(static () => SoapSchemaType.Primitive("anyType"));
            Assert.Throws<ArgumentException>(static () => SoapSchemaType.Primitive(" "));

            Assert.Throws<ArgumentException>(
                static () => SoapSchemaType.Complex(new SoapQName(string.Empty, "Dto"), []));

            Assert.Throws<ArgumentException>(
                static () => SoapSchemaType.Complex(new SoapQName(SoapSchemaType.XmlSchemaNamespace, "Dto"), []));

            Assert.Throws<ArgumentException>(
                static () => SoapSchemaType.Complex(
                    new SoapQName("urn:test", "Dto"),
                    [
                        SoapSchemaElement.CreateMember("Field", SoapSchemaType.String),
                        SoapSchemaElement.CreateMember("Field", SoapSchemaType.Int)
                    ]));

            Assert.Throws<ArgumentException>(
                static () => SoapSchemaType.Complex(
                    new SoapQName("urn:test", "Dto"),
                    [
                        SoapSchemaElement.CreateMember("Field", SoapSchemaType.String, namespaceUri: "urn:other")
                    ]));

            Assert.Throws<ArgumentException>(
                static () => SoapSchemaType.Enum(new SoapQName("urn:test", "Status"), []));

            Assert.Throws<ArgumentException>(
                static () => SoapSchemaType.Enum(new SoapQName("urn:test", "Status"), ["Pending", "Pending"]));
        }

        [Fact]
        public void SoapServiceMetadata_RejectsEmptyInvalidVersionsDuplicatesAndConflictingTypes()
        {
            SoapOperationMetadata pingOp = CreateSimpleOp("Ping", "urn:test/Ping", "Ping", "PingResponse");
            SoapOperationMetadata dupNameOp = CreateSimpleOp("Ping", "urn:test/Ping2", "Ping2", "Ping2Response");
            SoapOperationMetadata dupActionOp = CreateSimpleOp("Ping2", "urn:test/Ping", "Ping2", "Ping2Response");
            SoapOperationMetadata dupRequestOp = CreateSimpleOp("Ping2", "urn:test/Ping2", "Ping", "Ping2Response");
            SoapOperationMetadata dupResponseOp = CreateSimpleOp("Ping2", "urn:test/Ping2", "Ping2", "PingResponse");

            Assert.Throws<ArgumentException>(
                static () => new SoapServiceMetadata("Svc", "urn:test", []));

            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SoapServiceMetadata("Svc", "urn:test", [pingOp], SoapVersionFlags.None));

            Assert.Throws<ArgumentException>(
                () => new SoapServiceMetadata("Svc", "http://schemas.xmlsoap.org/wsdl/", [pingOp]));

            Assert.Throws<ArgumentException>(
                () => new SoapServiceMetadata("Svc", "urn:test", [pingOp, dupNameOp]));

            Assert.Throws<ArgumentException>(
                () => new SoapServiceMetadata("Svc", "urn:test", [pingOp, dupActionOp]));

            Assert.Throws<ArgumentException>(
                () => new SoapServiceMetadata("Svc", "urn:test", [pingOp, dupRequestOp]));

            Assert.Throws<ArgumentException>(
                () => new SoapServiceMetadata("Svc", "urn:test", [pingOp, dupResponseOp]));

            SoapSchemaType dtoV1 = SoapSchemaType.Complex(
                new SoapQName("urn:test", "Payload"),
                [SoapSchemaElement.CreateMember("Value", SoapSchemaType.String)]);

            SoapSchemaType dtoV2 = SoapSchemaType.Complex(
                new SoapQName("urn:test", "Payload"),
                [SoapSchemaElement.CreateMember("Value", SoapSchemaType.Int)]);

            SoapOperationMetadata opA = new SoapOperationMetadata(
                new SoapOperationDescriptor("OpA", "urn:test/OpA", null, new SoapQName("urn:test", "OpA"), new SoapQName("urn:test", "OpAResponse")),
                SoapSchemaElement.CreateWrapped(new SoapQName("urn:test", "OpA"), [SoapSchemaElement.CreateMember("p", dtoV1)]),
                SoapSchemaElement.CreateWrapped(new SoapQName("urn:test", "OpAResponse")));

            SoapOperationMetadata opB = new SoapOperationMetadata(
                new SoapOperationDescriptor("OpB", "urn:test/OpB", null, new SoapQName("urn:test", "OpB"), new SoapQName("urn:test", "OpBResponse")),
                SoapSchemaElement.CreateWrapped(new SoapQName("urn:test", "OpB"), [SoapSchemaElement.CreateMember("p", dtoV2)]),
                SoapSchemaElement.CreateWrapped(new SoapQName("urn:test", "OpBResponse")));

            Assert.Throws<ArgumentException>(
                () => new SoapServiceMetadata("Svc", "urn:test", [opA, opB]));
        }

        #endregion

        #region Private Methods

        private static SoapOperationMetadata CreateSimpleOp(
            string name,
            string action,
            string requestLocalName,
            string responseLocalName)
        {
            SoapOperationDescriptor descriptor = new SoapOperationDescriptor(
                name,
                action,
                null,
                new SoapQName("urn:test", requestLocalName),
                new SoapQName("urn:test", responseLocalName));

            return new SoapOperationMetadata(
                descriptor,
                SoapSchemaElement.CreateWrapped(descriptor.RequestElement),
                SoapSchemaElement.CreateWrapped(descriptor.ResponseElement));
        }

        #endregion

    }

}
