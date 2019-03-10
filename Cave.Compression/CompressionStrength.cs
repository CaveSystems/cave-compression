namespace Cave.Compression
{
    /// <summary>
    /// Compression Level as an enum for safer use.
    /// </summary>
    public enum CompressionStrength
    {
        /// <summary>
        /// No compression at all
        /// </summary>
        None = 0,

        /// <summary>
        /// The worst but fastest compression level.
        /// </summary>
        Fastest = 1,

        /// <summary>
        /// The best and slowest compression level.
        /// </summary>
        Best = 9,
    }
}
