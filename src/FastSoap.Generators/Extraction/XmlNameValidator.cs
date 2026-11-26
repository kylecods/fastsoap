using System;
using Microsoft.CodeAnalysis.CSharp;

namespace FastSoap.Generators.Extraction
{

    /// <summary>
    /// Provides deterministic XML 1.0 NCName, namespace URI, action URI, and C# identifier validation helpers
    /// matching <c>FastSoap.Abstractions.SoapQName</c> and <c>SoapOperationDescriptor</c> invariants.
    /// </summary>
    internal static class XmlNameValidator
    {

        #region Fields

        private static readonly char[] ForbiddenLocalNameChars =
        {
            '\0', '\t', '\n', '\r', ' ', ':', '<', '>', '"', '\'', '&', '/', '\\'
        };

        private static readonly char[] ForbiddenUriControlChars =
        {
            '\0', '\r', '\n'
        };

        #endregion

        #region Internal Methods

        /// <summary>
        /// Determines whether <paramref name="action"/> is a valid SOAP action or reply action URI.
        /// </summary>
        /// <param name="action">The candidate action URI.</param>
        /// <returns><see langword="true"/> when non-blank and free of forbidden control characters; otherwise <see langword="false"/>.</returns>
        internal static bool IsValidAction(string? action)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return false;
            }

            if (action!.IndexOfAny(ForbiddenUriControlChars) >= 0)
            {
                return false;
            }

            for (int index = 0; index < action.Length; index++)
            {
                if (char.IsControl(action[index]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Determines whether <paramref name="localName"/> is a valid XML local name (NCName) matching <c>SoapQName</c>.
        /// </summary>
        /// <param name="localName">The candidate XML local name.</param>
        /// <returns><see langword="true"/> if valid; otherwise <see langword="false"/>.</returns>
        internal static bool IsValidLocalName(string? localName)
        {
            if (string.IsNullOrWhiteSpace(localName))
            {
                return false;
            }

            if (localName!.IndexOfAny(ForbiddenLocalNameChars) >= 0)
            {
                return false;
            }

            char first = localName[0];
            if (char.IsDigit(first) || first == '-' || first == '.' || char.IsWhiteSpace(first))
            {
                return false;
            }

            for (int index = 0; index < localName.Length; index++)
            {
                char current = localName[index];
                if (char.IsControl(current) || char.IsWhiteSpace(current))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Determines whether <paramref name="namespaceUri"/> is a valid optional XML namespace URI (empty permitted when explicitly unqualified).
        /// </summary>
        /// <param name="namespaceUri">The candidate XML namespace URI.</param>
        /// <returns><see langword="true"/> when non-null and free of control characters; otherwise <see langword="false"/>.</returns>
        internal static bool IsValidOptionalNamespaceUri(string? namespaceUri)
        {
            if (namespaceUri is null)
            {
                return false;
            }

            if (namespaceUri.IndexOfAny(ForbiddenUriControlChars) >= 0)
            {
                return false;
            }

            for (int index = 0; index < namespaceUri.Length; index++)
            {
                char current = namespaceUri[index];
                if (char.IsControl(current) || char.IsWhiteSpace(current))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Determines whether <paramref name="namespaceUri"/> is a valid non-empty SOAP service target namespace URI.
        /// </summary>
        /// <param name="namespaceUri">The candidate service target namespace URI.</param>
        /// <returns><see langword="true"/> when non-blank and free of whitespace or control characters; otherwise <see langword="false"/>.</returns>
        internal static bool IsValidRequiredServiceNamespace(string? namespaceUri)
        {
            if (string.IsNullOrWhiteSpace(namespaceUri))
            {
                return false;
            }

            return IsValidOptionalNamespaceUri(namespaceUri);
        }

        /// <summary>
        /// Determines whether <paramref name="name"/> is valid as both a C# identifier and an XML local name.
        /// </summary>
        /// <param name="name">The candidate contract or operation name.</param>
        /// <returns><see langword="true"/> if valid for generated identifiers and XML names; otherwise <see langword="false"/>.</returns>
        internal static bool IsValidServiceOrOperationIdentifier(string? name)
        {
            if (!IsValidLocalName(name))
            {
                return false;
            }

            return SyntaxFacts.IsValidIdentifier(name!);
        }

        #endregion

    }

}
