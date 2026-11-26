using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace FastSoap.Generators.Models
{

    /// <summary>
    /// Stores lightweight, value-equatable source location coordinates without retaining a <see cref="SyntaxTree"/> or <see cref="Location"/> reference.
    /// </summary>
    internal readonly record struct EquatableLocation(
        string FilePath,
        TextSpan TextSpan,
        LinePositionSpan LineSpan)
    {

        #region Public Methods

        /// <summary>
        /// Captures an <see cref="EquatableLocation"/> from a Roslyn <see cref="Location"/> during symbol extraction.
        /// </summary>
        /// <param name="location">The Roslyn source location.</param>
        /// <returns>An equatable location record, or <see langword="default"/> when not in source.</returns>
        public static EquatableLocation Create(Location? location)
        {
            if (location is null || !location.IsInSource)
            {
                return new EquatableLocation(string.Empty, default, default);
            }

            FileLinePositionSpan fileLineSpan = location.GetLineSpan();

            return new EquatableLocation(
                fileLineSpan.Path ?? string.Empty,
                location.SourceSpan,
                fileLineSpan.Span);
        }

        /// <summary>
        /// Reconstructs a Roslyn <see cref="Location"/> at diagnostic reporting time.
        /// </summary>
        /// <returns>The reconstructed <see cref="Location"/>, or <see cref="Location.None"/> when empty.</returns>
        public Location ToLocation()
        {
            if (string.IsNullOrWhiteSpace(FilePath) && TextSpan.Length == 0)
            {
                return Location.None;
            }

            return Location.Create(FilePath, TextSpan, LineSpan);
        }

        #endregion

    }

}
