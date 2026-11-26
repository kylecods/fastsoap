namespace FastSoap.Generators.Models
{

    /// <summary>
    /// Represents a lightweight, value-equatable diagnostic specification emitted during extraction
    /// and converted into a Roslyn <c>Diagnostic</c> only at reporting time.
    /// </summary>
    internal sealed record DiagnosticSpec(
        string DiagnosticId,
        EquatableLocation Location,
        EquatableArray<string> MessageArguments);

    /// <summary>
    /// Represents a fully validated, value-equatable SOAP operation model used for source emission.
    /// </summary>
    /// <remarks>
    /// Contains zero source locations so trivia or line-offset shifts do not invalidate code emission.
    /// </remarks>
    internal sealed record OperationGenerationModel(
        string Action,
        bool HasCancellationToken,
        string MethodName,
        string OperationName,
        string? ReplyAction,
        string RequestContextFullyQualifiedName,
        string RequestContextPropertyName,
        string RequestLocalName,
        string? RequestMemberName,
        string RequestNamespace,
        string RequestWireFullyQualifiedName,
        string ResponseContextFullyQualifiedName,
        string ResponseContextPropertyName,
        string ResponseLocalName,
        string? ResponseMemberName,
        string ResponseNamespace,
        string ResponseWireFullyQualifiedName,
        string ServiceReturnInnerFullyQualifiedName);

    /// <summary>
    /// Represents a validated, value-equatable SOAP service contract model used for deterministic C# emission.
    /// </summary>
    internal sealed record ServiceGenerationModel(
        string HintNamePrefix,
        bool IsPublic,
        EquatableArray<OperationGenerationModel> Operations,
        string RegistrationMethodName,
        string ServiceFullyQualifiedName,
        string ServiceName,
        string ServiceNamespace,
        string TargetNamespace);

    /// <summary>
    /// Represents lightweight identity and conflict-detection metadata for a discovered <c>[SoapService]</c> contract.
    /// </summary>
    internal sealed record ServiceIdentityModel(
        bool HasUserContractTypeConflict,
        bool HasUserRegistrationMethodConflict,
        string HintNamePrefix,
        string RegistrationMethodName,
        EquatableLocation ServiceLocation,
        string ServiceName,
        string TargetNamespace);

    /// <summary>
    /// Represents the complete value-equatable output of the semantic extraction transform for one service contract.
    /// </summary>
    internal sealed record ServiceExtractionResult(
        EquatableArray<DiagnosticSpec> Diagnostics,
        ServiceGenerationModel? GenerationModel,
        ServiceIdentityModel Identity);

}
