using System;
using System.IO;
using System.Text;

namespace Cave.Compression;

/// <summary>provides access to a ar file header.</summary>
public class ArHeader
{
    #region Private Fields

    static readonly char[] PathSeparators = ['\\', '/'];
    readonly byte[] data = new byte[60];

    #endregion Private Fields

    #region Private Constructors

    /// <summary>Initializes a new instance of the <see cref="ArHeader"/> class.</summary>
    private ArHeader()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ArHeader"/> class.</summary>
    /// <param name="data">The header data.</param>
    private ArHeader(byte[] data)
    {
        this.data = data;
        if (this.data.Length != 60)
        {
            throw new InvalidDataException(string.Format("Data length invalid!"));
        }

        if ((this.data[58] != 0x60) || (this.data[59] != 0x0A))
        {
            throw new InvalidDataException(string.Format("Invalid data! (File magic not correct)"));
        }
    }

    #endregion Private Constructors

    #region Private Methods

    string GetString(int index, int count)
    {
        var result = new StringBuilder();
        var nullDetected = false;
        var str = ASCII.GetString(data, index, count);
        foreach (var c in str)
        {
            if (c == '\0')
            {
                nullDetected = true;
            }
            else
            {
                if ((c != ' ') && nullDetected && (result.Length > 0))
                {
                    throw new FormatException(string.Format("Format failure! (Found ascii null in the middle of the string)!"));
                }

                result.Append(c);
            }
        }

        return result.ToString();
    }

    int GetValue(int index, int count, int toBase)
    {
        // 0 = no valid char found 1 = at least one valid octal number 2 = space after number found, there shouldn't be any more valid numbers
        var valueDetection = 0;
        var result = 0;
        var str = GetString(index, count);
        foreach (var c in str)
        {
            switch (c)
            {
                case '\0':
                case ' ':
                    if (valueDetection == 1)
                    {
                        valueDetection = 2;
                    }

                    break;

                case '0':
                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9':
                    if (valueDetection == 2)
                    {
                        throw new FormatException(string.Format("Format of the stored number not correct ! (Found space in the middle of the number)"));
                    }

                    if (valueDetection == 0)
                    {
                        valueDetection = 1;
                    }

                    var value = c - '0';
                    if (value >= toBase)
                    {
                        throw new FormatException(string.Format("Format of the stored number not correct ! (Value exceeds base)"));
                    }

                    result = (result * toBase) + value;
                    break;

                default:
                    throw new FormatException(string.Format("Format of the stored number not correct ! (Illegal character found)"));
            }
        }

        return result;
    }

    void Initialize(string name, long size, int fileMode, int owner, int group, DateTime modificationTime)
    {
        if (fileMode <= 0)
        {
            fileMode = 644;
        }

        if (owner < 0)
        {
            throw new ArgumentException(string.Format("Owner may not be a value smaller than 0!"), nameof(owner));
        }

        if (group < 0)
        {
            throw new ArgumentException(string.Format("Group may not be a value smaller than 0!"), nameof(group));
        }

        // check FileSize
        if (size > 0x7FFFFFFFL)
        {
            throw new NotSupportedException(string.Format("A FileSize with more then 2GiB is currently not supported!"));
        }

        // prepare unix file path and name
        var fileNameInAr = string.Empty;
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c is >= (char)32 and < (char)128)
            {
                if (PathSeparators.IndexOf(c) >= 0)
                {
                    throw new NotSupportedException(string.Format("(Sub)Directories are not supported!"));
                }
                else if (Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0)
                {
                    c = '_';
                }

                fileNameInAr += c;
            }
        }

        // check length
        if (fileNameInAr.Length > 15)
        {
            throw new ArgumentException(string.Format("FileName may not exceed 15 chars!"), nameof(name));
        }

        fileNameInAr += "/";

        // set FileName
        SetString(0, 16, fileNameInAr);

        // set modification time
        SetValue(16, 12, (int)modificationTime.ToUniversalTime().Subtract(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds, 10);

        // set owner
        SetString(28, 6, owner.ToString());

        // set group
        SetString(34, 6, group.ToString());

        // set filemode
        SetString(40, 8, fileMode.ToString());

        // set FileSize
        SetValue(48, 10, (int)size, 10);

        // set file magic
        data[58] = 0x60;
        data[59] = 0x0A;
    }

    void SetString(int index, int count, string text)
    {
        var result = text;
        if (result.Length < count)
        {
            result += new string(' ', count - result.Length);
        }

        if (result.Length > count)
        {
            throw new ArgumentOutOfRangeException(string.Format("String range out of bounds!"));
        }

        foreach (var b in ASCII.GetBytes(result))
        {
            data[index++] = b;
        }
    }

    void SetValue(int index, int count, int value, int toBase) => SetString(index, count, Convert.ToString(value, toBase));

    #endregion Private Methods

    #region Public Constructors

    /// <summary>Initializes a new instance of the <see cref="ArHeader"/> class.</summary>
    /// <param name="file">Name and path of the file/directory at the local system.</param>
    public ArHeader(string file)
        : this(file, 644, 0, 0)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ArHeader"/> class.</summary>
    /// <param name="file">FileName and path of the file at the local system.</param>
    /// <param name="fileMode">the unix filemode to use.</param>
    /// <param name="owner">the unix owner.</param>
    /// <param name="group">the unix group.</param>
    public ArHeader(string file, int fileMode, int owner, int group)
    {
        if (File.Exists(file))
        {
            var info = new FileInfo(file);
            Initialize(info.Name, info.Length, fileMode, owner, group, info.LastWriteTime);
        }
        else
        {
            throw new FileNotFoundException(string.Format("File '{0}' not found!", file));
        }
    }

    #endregion Public Constructors

    #region Public Properties

    /// <summary>Gets a copy of the header data.</summary>
    public byte[] Data => (byte[])data.Clone();

    /// <summary>Gets the unix file mode (ugo) default = 640.</summary>
    public int FileMode => GetValue(40, 8, 10);

    /// <summary>Gets the unix FileName.</summary>
    public string FileName
    {
        get
        {
            var name = GetString(0, 16);
            return name[..name.LastIndexOf('/')];
        }
    }

    /// <summary>Gets the file size.</summary>
    public int FileSize => GetValue(48, 10, 10);

    /// <summary>Gets the unix group id.</summary>
    public int Group => GetValue(34, 6, 10);

    /// <summary>Gets the last modification date (utc).</summary>
    public DateTime LastWriteTime => new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddSeconds(GetValue(16, 12, 10)).ToLocalTime();

    /// <summary>Gets the unix owner id.</summary>
    public int Owner => GetValue(28, 6, 10);

    #endregion Public Properties

    #region Public Methods

    /// <summary>Creates a new <see cref="ArHeader"/> for the specified file.</summary>
    /// <param name="file">The file to create the header for.</param>
    /// <returns>Returns a new header instance.</returns>
    public static ArHeader Create(string file)
    {
        var info = new FileInfo(file);
        return CreateFile(Path.GetFileName(file), info.Length, 644, 0, 0, info.LastWriteTime);
    }

    /// <summary>Creates a new <see cref="ArHeader"/> for the specified file name. (Name may not contain subdirectories).</summary>
    /// <param name="name">The name of the file.</param>
    /// <param name="size">The size of the file in bytes.</param>
    /// <returns>Returns a new header instance.</returns>
    public static ArHeader CreateFile(string name, long size) => CreateFile(name, size, 644, 0, 0, DateTime.Now);

    /// <summary>Creates a new <see cref="ArHeader"/> for the specified file name. (Name may not contain subdirectories).</summary>
    /// <param name="name">The name of the file.</param>
    /// <param name="size">The size of the file in bytes.</param>
    /// <param name="fileMode">the unix filemode to use.</param>
    /// <returns>Returns a new header instance.</returns>
    public static ArHeader CreateFile(string name, long size, int fileMode) => CreateFile(name, size, fileMode, 0, 0, DateTime.Now);

    /// <summary>Creates a new <see cref="ArHeader"/> for the specified file name. (Name may not contain subdirectories).</summary>
    /// <param name="name">The name of the file.</param>
    /// <param name="size">The size of the file in bytes.</param>
    /// <param name="fileMode">the unix filemode to use.</param>
    /// <param name="owner">the unix owner.</param>
    /// <param name="group">the unix group.</param>
    /// <returns>Returns a new header instance.</returns>
    public static ArHeader CreateFile(string name, long size, int fileMode, int owner, int group) => CreateFile(name, size, fileMode, owner, group, DateTime.Now);

    /// <summary>Creates a new <see cref="ArHeader"/> for the specified file name. (Name may not contain subdirectories).</summary>
    /// <param name="name">The name of the file.</param>
    /// <param name="size">The size of the file in bytes.</param>
    /// <param name="fileMode">the unix filemode to use.</param>
    /// <param name="owner">the unix owner.</param>
    /// <param name="group">the unix group.</param>
    /// <param name="modificationTime">The last modification time.</param>
    /// <returns>Returns a new header instance.</returns>
    public static ArHeader CreateFile(string name, long size, int fileMode, int owner, int group, DateTime modificationTime)
    {
        var result = new ArHeader();
        result.Initialize(name, size, fileMode, owner, group, modificationTime);
        return result;
    }

    /// <summary>Reads a <see cref="ArHeader"/> from the specified stream.</summary>
    /// <param name="stream">The stream to read the header from.</param>
    /// <returns>Returns a new header instance.</returns>
    public static ArHeader FromStream(Stream stream)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        var data = new byte[60];
        if (stream.Read(data, 0, 60) != 60)
        {
            throw new EndOfStreamException();
        }

        return new ArHeader(data);
    }

    /// <summary>Gets a summary of the header.</summary>
    /// <returns>File: {FileName} {FileMode} {Owner} {Group} {FileSize}.</returns>
    public override string ToString() => $"File: {FileName} {FileMode} {Owner} {Group} {FileSize}";

    #endregion Public Methods
}
