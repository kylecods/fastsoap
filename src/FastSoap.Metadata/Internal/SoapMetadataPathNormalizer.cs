using System;
using System.Diagnostics.CodeAnalysis;

namespace FastSoap.Metadata.Internal
{

    /// <summary>
    /// Provides strict, allocation-conscious validation and normalization for static metadata schema keys.
    /// </summary>
    internal static class SoapMetadataPathNormalizer
    {

        #region Fields

        private const int MaxSchemaNameLength = 256;

        #endregion

        #region Internal Methods

        internal static bool TryNormalizeSchemaName(string? rawSchemaName, [NotNullWhen(true)] out string? normalizedName)
        {
            if (string.IsNullOrWhiteSpace(rawSchemaName))
            {
                normalizedName = null;
                return false;
            }

            if (rawSchemaName.Length > MaxSchemaNameLength)
            {
                normalizedName = null;
                return false;
            }

            if (char.IsWhiteSpace(rawSchemaName[0]) || char.IsWhiteSpace(rawSchemaName[^1]))
            {
                normalizedName = null;
                return false;
            }

            if (rawSchemaName[0] == '/' || rawSchemaName[^1] == '/')
            {
                normalizedName = null;
                return false;
            }

            if (rawSchemaName.Contains("..", StringComparison.Ordinal))
            {
                normalizedName = null;
                return false;
            }

            ReadOnlySpan<char> span = rawSchemaName.AsSpan();
            int segmentStart = 0;

            for (int index = 0; index <= span.Length; index++)
            {
                bool isSeparator = index == span.Length || span[index] == '/';
                if (!isSeparator)
                {
                    char current = span[index];
                    if (!IsAllowedSchemaCharacter(current))
                    {
                        normalizedName = null;
                        return false;
                    }

                    continue;
                }

                ReadOnlySpan<char> segment = span.Slice(segmentStart, index - segmentStart);
                if (!IsValidSegment(segment))
                {
                    normalizedName = null;
                    return false;
                }

                segmentStart = index + 1;
            }

            normalizedName = rawSchemaName;
            return true;
        }

        #endregion

        #region Private Methods

        private static bool IsAllowedSchemaCharacter(char value)
        {
            return (value >= 'a' && value <= 'z')
                || (value >= 'A' && value <= 'Z')
                || (value >= '0' && value <= '9')
                || value == '.'
                || value == '-'
                || value == '_';
        }

        private static bool IsValidSegment(ReadOnlySpan<char> segment)
        {
            if (segment.IsEmpty)
            {
                return false;
            }

            if (segment is "." or "..")
            {
                return false;
            }

            if (segment[0] == '.' || segment[^1] == '.')
            {
                return false;
            }

            return true;
        }

        #endregion

    }

}
