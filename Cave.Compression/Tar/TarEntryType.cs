namespace Cave.Compression.Tar
{
    /// <summary>
    /// Provides available tar entry types
    /// </summary>
    public enum TarEntryType : byte
    {
        /// <summary>
        ///  The "old way" of indicating a normal file.
        /// </summary>
        OldNormalFile = 0,

        /// <summary>
        /// Normal file type.
        /// </summary>
        NormalFile = (byte)'0',

        /// <summary>
        /// Link file type.
        /// </summary>
        Link,

        /// <summary>
        /// Symbolic link file type.
        /// </summary>
        Symlink,

        /// <summary>
        /// Character device file type.
        /// </summary>
        Character,

        /// <summary>
        /// Block device file type.
        /// </summary>
        Block,

        /// <summary>
        /// Directory file type.
        /// </summary>
        Directory,

        /// <summary>
        /// FIFO (pipe) file type.
        /// </summary>
        Fifo,

        /// <summary>
        /// Contiguous file type.
        /// </summary>
        Contiguous,

        /// <summary>
        /// Posix.1 2001 global extended header
        /// </summary>
        GlobalExtHeader = (byte)'g',

        /// <summary>
        /// Posix.1 2001 extended header
        /// </summary>
        ExtendedHeader = (byte)'x',

        /// <summary>
        /// Solaris access control list file type
        /// </summary>
        SolarisAccessControlList = (byte)'A',

        /// <summary>
        /// GNU dir dump file type
        /// This is a dir entry that contains the names of files that were in the
        /// dir at the time the dump was made
        /// </summary>
        DirDump = (byte)'D',

        /// <summary>
        /// Solaris Extended Attribute File
        /// </summary>
        SolarisExtendedAttributeFile = (byte)'E',

        /// <summary>
        /// Inode (metadata only) no file content
        /// </summary>
        InodeMetadata = (byte)'I',

        /// <summary>
        /// Identifies the next file on the tape as having a long link name
        /// </summary>
        LongLink = (byte)'K',

        /// <summary>
        /// Identifies the next file on the tape as having a long name
        /// </summary>
        LongName = (byte)'L',

        /// <summary>
        /// Continuation of a file that began on another volume
        /// </summary>
        MultiVolume = (byte)'M',

        /// <summary>
        /// For storing filenames that dont fit in the main header (old GNU)
        /// </summary>
        Names = (byte)'N',

        /// <summary>
        /// GNU Sparse file
        /// </summary>
        Sparse = (byte)'S',

        /// <summary>
        /// GNU Tape/volume header ignore on extraction
        /// </summary>
        VolumeHeader = (byte)'V',
    }
}
