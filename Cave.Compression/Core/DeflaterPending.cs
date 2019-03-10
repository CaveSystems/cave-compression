namespace Cave.Compression.Core
{
    /// <summary>
    /// This class stores the pending output of the Deflater.
    ///
    /// author of the original java version : Jochen Hoenicke.
    /// </summary>
    class DeflaterPending : PendingBuffer
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeflaterPending"/> class.
        /// </summary>
        public DeflaterPending()
            : base(DeflaterConstants.PendingBufferSize)
        {
        }
    }
}
