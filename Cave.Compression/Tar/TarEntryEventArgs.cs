using System;

namespace Cave.Compression.Tar
{
    /// <summary>
    /// Provides event arguments for <see cref="TarArchive"/> instances
    /// </summary>
    public class TarEntryEventArgs : EventArgs
    {
        /// <summary>
        /// Gets or sets a value indicating whether operation should break
        /// </summary>
        public bool Break { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this entry should be skipped
        /// </summary>
        public bool Skip { get; set; }

        /// <summary>
        /// Gets the entry
        /// </summary>
        public TarEntry Entry { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TarEntryEventArgs"/> class.
        /// </summary>
        /// <param name="entry">Entry</param>
        public TarEntryEventArgs(TarEntry entry)
        {
            Entry = entry;
        }
    }
}
