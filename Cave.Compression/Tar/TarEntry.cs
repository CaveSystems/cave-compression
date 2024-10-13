using System;
using System.IO;
using Cave.IO;

namespace Cave.Compression.Tar;

/// <summary>
/// This class represents an entry in a Tar archive. It consists of the entry's header, as well as the entry's File. Entries can be instantiated in one of three
/// ways, depending on how they are to be used.
/// <p>
/// TarEntries that are created from the header bytes read from an archive are instantiated with the TarEntry( byte[] ) constructor. These entries will be used
/// when extracting from or listing the contents of an archive. These entries have their header filled in using the header bytes. They also set the File to
/// null, since they reference an archive entry not a file.
/// </p>
/// <p>
/// TarEntries that are created from files that are to be written into an archive are instantiated with the CreateEntryFromFile(string) pseudo constructor.
/// These entries have their header filled in using the File's information. They also keep a reference to the File for convenience when writing entries.
/// </p>
/// <p>
/// Finally, TarEntries can be constructed from nothing but a name. This allows the programmer to construct the entry by hand, for instance when only an
/// InputStream is available for writing to the archive, and the header information is constructed from other information. In this case the header fields are
/// set to defaults and the File is set to null.
/// </p>
/// <see cref="TarHeader"/>.
/// </summary>
public class TarEntry
{
    #region Private Fields

    static readonly TarEntry[] Empty = [];

    #endregion Private Fields

    #region Private Constructors

    /// <summary>Initializes a new instance of the <see cref="TarEntry"/> class.</summary>
    TarEntry() => TarHeader = new TarHeader();

    #endregion Private Constructors

    #region Public Constructors

    /// <summary>Initializes a new instance of the <see cref="TarEntry"/> class.</summary>
    /// <param name="encoding">Encoding used for names</param>
    /// <param name="headerBuffer">The header bytes from a tar archive entry.</param>
    public TarEntry(StringEncoding encoding, byte[] headerBuffer)
    {
        TarHeader = new TarHeader();
        TarHeader.ParseBuffer(encoding, headerBuffer);
    }

    /// <summary>Initializes a new instance of the <see cref="TarEntry"/> class.</summary>
    /// <param name="header">Header details for entry.</param>
    public TarEntry(TarHeader header)
    {
        if (header == null)
        {
            throw new ArgumentNullException(nameof(header));
        }

        TarHeader = (TarHeader)header.Clone();
    }

    #endregion Public Constructors

    #region Public Properties

    /// <summary>Gets this entry's filename.</summary>
    /// <remarks>The name of the file this entry represents or null if the entry is not based on a file.</remarks>
    /// <returns>This entry's file.</returns>
    public string? FileName { get; private set; }

    /// <summary>Gets or sets this entry's group id.</summary>
    public int GroupId
    {
        get => TarHeader.GroupId;
        set => TarHeader.GroupId = value;
    }

    /// <summary>Gets or sets this entry's group name.</summary>
    public string GroupName
    {
        get => TarHeader.GroupName;
        set => TarHeader.GroupName = value ?? "None";
    }

    /// <summary>Gets a value indicating whether this entry represents a directory, false otherwise.</summary>
    /// <returns>True if this entry is a directory.</returns>
    public bool IsDirectory
    {
        get
        {
            if (FileName != null)
            {
                return Directory.Exists(FileName);
            }

            if (TarHeader != null)
            {
                if ((TarHeader.TypeFlag == TarEntryType.Directory) || Name.EndsWith('/'))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Gets or sets the modification time for this entry.</summary>
    public DateTime ModTime
    {
        get => TarHeader.ModTime;
        set => TarHeader.ModTime = value;
    }

    /// <summary>Gets or sets this entry's name.</summary>
    public string Name
    {
        get => TarHeader.Name;
        set => TarHeader.Name = value;
    }

    /// <summary>Gets or sets this entry's recorded file size.</summary>
    public long Size
    {
        get => TarHeader.Size;
        set => TarHeader.Size = value;
    }

    /// <summary>Gets or sets this entry's header.</summary>
    /// <returns>This entry's TarHeader.</returns>
    public TarHeader TarHeader { get; set; }

    /// <summary>Gets or sets this entry's user id.</summary>
    public int UserId
    {
        get => TarHeader.UserId;
        set => TarHeader.UserId = value;
    }

    /// <summary>Gets or sets this entry's user name.</summary>
    public string UserName
    {
        get => TarHeader.UserName;
        set => TarHeader.UserName = value ?? string.Empty;
    }

    #endregion Public Properties

    #region Public Methods

    /// <summary>Construct an entry for a file. File is set to file, and the header is constructed from information from the file.</summary>
    /// <param name="fileName">The file name that the entry represents.</param>
    /// <returns>Returns the newly created <see cref="TarEntry"/>.</returns>
    public static TarEntry CreateEntryFromFile(string fileName)
    {
        var entry = new TarEntry();
        entry.GetFileTarHeader(entry.TarHeader, fileName);
        return entry;
    }

    /// <summary>Construct an entry with only a <paramref name="name">name</paramref>. This allows the programmer to construct the entry's header "by hand".</summary>
    /// <param name="name">The name to use for the entry.</param>
    /// <returns>Returns the newly created <see cref="TarEntry"/>.</returns>
    public static TarEntry CreateTarEntry(string name)
    {
        var entry = new TarEntry();
        NameTarHeader(entry.TarHeader, name);
        return entry;
    }

    /// <summary>Fill in a TarHeader given only the entry's name.</summary>
    /// <param name="header">The TarHeader to fill in.</param>
    /// <param name="name">The tar entry name.</param>
    public static void NameTarHeader(TarHeader header, string name)
    {
        if (header == null)
        {
            throw new ArgumentNullException(nameof(header));
        }

        if (name == null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        var isDir = name.EndsWith('/');

        header.Name = name;
        header.Mode = isDir ? 1003 : 33216;
        header.UserId = 0;
        header.GroupId = 0;
        header.Size = 0;

        header.ModTime = DateTime.UtcNow;

        header.TypeFlag = isDir ? TarEntryType.Directory : TarEntryType.NormalFile;

        header.LinkName = string.Empty;
        header.UserName = string.Empty;
        header.GroupName = string.Empty;

        header.DevMajor = 0;
        header.DevMinor = 0;
    }

    /// <summary>Clone this tar entry.</summary>
    /// <returns>Returns a clone of this entry.</returns>
    public object Clone()
    {
        var entry = new TarEntry
        {
            FileName = FileName,
            TarHeader = (TarHeader)TarHeader.Clone(),
            Name = Name,
        };
        return entry;
    }

    /// <summary>Determine if the two entries are equal. Equality is determined by the header names being equal.</summary>
    /// <param name="obj">The <see cref="object"/> to compare with the current Object.</param>
    /// <returns>True if the entries are equal; false if not.</returns>
    public override bool Equals(object? obj)
    {
        if (obj is TarEntry localEntry)
        {
            return Name.Equals(localEntry.Name);
        }

        return false;
    }

    /// <summary>Get entries for all files present in this entries directory. If this entry doesnt represent a directory zero entries are returned.</summary>
    /// <returns>An array of TarEntry's for this entry's children.</returns>
    public TarEntry[] GetDirectoryEntries()
    {
        if ((FileName == null) || !Directory.Exists(FileName))
        {
            return Empty;
        }

        var list = Directory.GetFileSystemEntries(FileName);
        var result = new TarEntry[list.Length];

        for (var i = 0; i < list.Length; ++i)
        {
            result[i] = CreateEntryFromFile(list[i]);
        }

        return result;
    }

    /// <summary>Fill in a TarHeader with information from a File.</summary>
    /// <param name="header">The TarHeader to fill in.</param>
    /// <param name="file">The file from which to get the header information.</param>
    public void GetFileTarHeader(TarHeader header, string file)
    {
        if (header == null)
        {
            throw new ArgumentNullException(nameof(header));
        }

        FileName = file ?? throw new ArgumentNullException(nameof(file));

        // bugfix from torhovl from #D forum:
        var name = file;

        // 23-Jan-2004 GnuTar allows device names in path where the name is not local to the current directory
        var currentDir = Directory.GetCurrentDirectory();
        if (name.StartsWith(currentDir, StringComparison.Ordinal))
        {
            name = name[currentDir.Length..];
        }

        name = name.Replace(Path.DirectorySeparatorChar, '/');

        // No absolute pathnames Windows (and Posix?) paths can start with UNC style "\\NetworkDrive\", so we loop on starting /'s.
        while (name.StartsWith('/'))
        {
            name = name[1..];
        }

        header.LinkName = string.Empty;

        if (Directory.Exists(file))
        {
            header.Mode = 1003; // Magic number for security access for a UNIX filesystem
            header.TypeFlag = TarEntryType.Directory;
            if ((name.Length == 0) || name[^1] != '/')
            {
                name += "/";
            }
            header.Name = name;
            header.Size = 0;
        }
        else
        {
            header.Mode = 33216; // Magic number for security access for a UNIX filesystem
            header.TypeFlag = TarEntryType.NormalFile;
            header.Name = name;
            header.Size = new FileInfo(file).Length;
        }

        header.ModTime = File.GetLastWriteTime(file).ToUniversalTime();
        header.DevMajor = 0;
        header.DevMinor = 0;
    }

    /// <summary>Derive a Hash value for the current <see cref="object"/>.</summary>
    /// <returns>A Hash code for the current <see cref="object"/>.</returns>
    public override int GetHashCode() => Name.GetHashCode();

    /// <summary>
    /// Determine if the given entry is a descendant of this entry. Descendancy is determined by the name of the descendant starting with this entry's name.
    /// </summary>
    /// <param name="toTest">Entry to be checked as a descendent of this.</param>
    /// <returns>True if entry is a descendant of this.</returns>
    public bool IsDescendent(TarEntry toTest)
    {
        if (toTest == null)
        {
            throw new ArgumentNullException(nameof(toTest));
        }

        return toTest.Name.ToString().StartsWith(Name.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Convenience method to set this entry's group and user ids.</summary>
    /// <param name="userId">This entry's new user id.</param>
    /// <param name="groupId">This entry's new group id.</param>
    public void SetIds(int userId, int groupId)
    {
        UserId = userId;
        GroupId = groupId;
    }

    /// <summary>Convenience method to set this entry's group and user names.</summary>
    /// <param name="userName">This entry's new user name.</param>
    /// <param name="groupName">This entry's new group name.</param>
    public void SetNames(string userName, string groupName)
    {
        UserName = userName;
        GroupName = groupName;
    }

    /// <summary>Write an entry's header information to a header buffer.</summary>
    /// <param name="encoding">Encoding used for names.</param>
    /// <param name="outBuffer">The tar entry header buffer to fill in.</param>
    public void WriteEntryHeader(StringEncoding encoding, byte[] outBuffer) => TarHeader.WriteHeader(encoding, outBuffer);

    /// <summary>Write an entry's header information to a header buffer.</summary>
    /// <param name="encoding">Encoding used for names.</param>
    /// <param name="outBuffer">The tar entry header buffer to fill in.</param>
    /// <param name="replacementName">replaces the name on the fly (e.g. long filenames)</param>
    public void WriteEntryHeader(StringEncoding encoding, byte[] outBuffer, string replacementName) => TarHeader.WriteHeader(encoding, outBuffer, replacementName);

    #endregion Public Methods
}
