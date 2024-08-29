using System;
using System.Text;
using Cave.IO;

namespace Cave.Compression.Tar
{
    /// <summary>This class encapsulates the Tar Entry Header used in Tar Archives. The class also holds a number of tar constants, used mostly in headers.</summary>
    /// <remarks>
    /// The tar format and its POSIX successor PAX have a long history which makes for compatability issues when creating and reading files.
    ///
    /// This is further complicated by a large number of programs with variations on formats One common issue is the handling of names longer than 100
    /// characters. GNU style long names are currently supported.
    /// </remarks>
    public class TarHeader
    {
        #region Private Fields

        static readonly DateTime DateTime1970 = new DateTime(1970, 1, 1, 0, 0, 0, 0);

        int devMinor;

        string groupName;

        bool isChecksumValid;

        string linkName;

        string magic;

        DateTime modTime;

        string name;

        long size;

        int userId;

        string userName;

        string version;

        #endregion Private Fields

        #region Private Methods

        /// <summary>Compute the checksum for a tar entry header. The checksum field must be all spaces prior to this happening.</summary>
        /// <param name="buffer">The tar entry's header buffer.</param>
        /// <returns>The computed checksum.</returns>
        static int ComputeCheckSum(byte[] buffer)
        {
            var sum = 0;
            for (var i = 0; i < buffer.Length; ++i)
            {
                sum += buffer[i];
            }

            return sum;
        }

        /// <summary>Put an octal or binary representation of a value into a buffer.</summary>
        /// <param name="value">Value to be convert to octal.</param>
        /// <param name="buffer">The buffer to update.</param>
        /// <param name="offset">The offset into the buffer to store the value.</param>
        /// <param name="length">The length of the octal string. Must be 12.</param>
        /// <returns>Index of next byte.</returns>
        static int GetBinaryOrOctalBytes(long value, byte[] buffer, int offset, int length)
        {
            if (value > 0x1FFFFFFFF)
            { // Octal 77777777777 (11 digits)
              // Put value as binary, right-justified into the buffer. Set high order bit of left-most byte.
                for (var pos = length - 1; pos > 0; pos--)
                {
                    buffer[offset + pos] = (byte)value;
                    value = value >> 8;
                }

                buffer[offset] = 0x80;
                return offset + length;
            }

            return WriteOctalBytes(value, buffer, offset, length);
        }

        /// <summary>Add the checksum integer to header buffer.</summary>
        /// <param name="value">The checksum value.</param>
        /// <param name="buffer">The header buffer to set the checksum for.</param>
        /// <param name="offset">The offset into the buffer for the checksum.</param>
        /// <param name="length">
        /// The number of header bytes to update. It's formatted differently from the other fields: it has 6 digits, a null, then a space -- rather than digits,
        /// a space, then a null. The final space is already there, from checksumming.
        /// </param>
        static void GetCheckSumOctalBytes(long value, byte[] buffer, int offset, int length)
        {
            WriteOctalBytes(value, buffer, offset, length - 1);
        }

        static int GetCTime(DateTime dateTime)
        {
            return unchecked((int)((dateTime.Ticks - DateTime1970.Ticks) / TimeConversionFactor));
        }

        static DateTime GetDateTimeFromCTime(long ticks)
        {
            DateTime result;

            try
            {
                result = new DateTime(DateTime1970.Ticks + (ticks * TimeConversionFactor));
            }
            catch (ArgumentOutOfRangeException)
            {
                result = DateTime1970;
            }

            return result;
        }

        /// <summary>Make a checksum for a tar entry ignoring the checksum contents.</summary>
        /// <param name="buffer">The tar entry's header buffer.</param>
        /// <returns>The checksum for the buffer.</returns>
        static int MakeCheckSum(byte[] buffer)
        {
            var sum = 0;
            for (var i = 0; i < ChecksumOffset; ++i)
            {
                sum += buffer[i];
            }

            for (var i = 0; i < ChecksumLength; ++i)
            {
                sum += (byte)' ';
            }

            for (var i = ChecksumOffset + ChecksumLength; i < buffer.Length; ++i)
            {
                sum += buffer[i];
            }

            return sum;
        }

        // Return value that may be stored in octal or binary. Length must exceed 8.
        static long ParseBinaryOrOctal(byte[] header, int offset, int length)
        {
            if (header[offset] >= 0x80)
            {
                // File sizes over 8GB are stored in 8 right-justified bytes of binary indicated by setting the high-order bit of the leftmost byte of a numeric field.
                long result = 0;
                for (var pos = length - 8; pos < length; pos++)
                {
                    result = (result << 8) | header[offset + pos];
                }

                return result;
            }

            return ParseOctal(header, offset, length);
        }

        #endregion Private Methods

        #region Internal Properties

        internal static int DefaultGroupId { get; set; }

        internal static string DefaultGroupName { get; set; } = "None";

        internal static string DefaultUser { get; set; }

        internal static int DefaultUserId { get; set; }

        internal static int GroupIdAsSet { get; set; }

        internal static string GroupNameAsSet { get; set; } = "None";

        // Values used during recursive operations.
        internal static int UserIdAsSet { get; set; }

        internal static string UserNameAsSet { get; set; }

        #endregion Internal Properties

        #region Internal Methods

        internal static void RestoreSetValues()
        {
            DefaultUserId = UserIdAsSet;
            DefaultUser = UserNameAsSet;
            DefaultGroupId = GroupIdAsSet;
            DefaultGroupName = GroupNameAsSet;
        }

        /// <summary>Set defaults for values used when constructing a TarHeader instance.</summary>
        /// <param name="userId">Value to apply as a default for userId.</param>
        /// <param name="userName">Value to apply as a default for userName.</param>
        /// <param name="groupId">Value to apply as a default for groupId.</param>
        /// <param name="groupName">Value to apply as a default for groupName.</param>
        internal static void SetValueDefaults(int userId, string userName, int groupId, string groupName)
        {
            DefaultUserId = UserIdAsSet = userId;
            DefaultUser = UserNameAsSet = userName;
            DefaultGroupId = GroupIdAsSet = groupId;
            DefaultGroupName = GroupNameAsSet = groupName;
        }

        #endregion Internal Methods

        #region Public Fields

        /// <summary>The length of the checksum field in a header buffer.</summary>
        public const int ChecksumLength = 8;

        /// <summary>Offset of checksum in a header buffer.</summary>
        public const int ChecksumOffset = 148;

        /// <summary>The magic tag representing a POSIX tar archive. (would be written with a trailing NULL).</summary>
        public const string DefaultMagic = "ustar";

        /// <summary>The length of the devices field in a header buffer.</summary>
        public const int DeviceLength = 8;

        /// <summary>The length of the size field in a header buffer.</summary>
        public const int FileSizeLength = 12;

        /// <summary>The length of the group id field in a header buffer.</summary>
        public const int GroupIdLength = 8;

        /// <summary>The length of the group name field in a header buffer.</summary>
        public const int GroupnameLength = 32;

        /// <summary>The length of the magic field in a header buffer.</summary>
        public const int MagicLength = 6;

        /// <summary>The length of the mode field in a header buffer.</summary>
        public const int ModeLength = 8;

        /// <summary>The length of the modification time field in a header buffer.</summary>
        public const int ModificationTimeLength = 12;

        /// <summary>The length of the name field in a header buffer.</summary>
        public const int NameLength = 100;

        /// <summary>The length of the name prefix field in a header buffer.</summary>
        public const int PrefixLength = 155;

        /// <summary>1 tick == 100 nanoseconds.</summary>
        public const long TimeConversionFactor = 10000000L;

        /// <summary>The length of the user id field in a header buffer.</summary>
        public const int UserIdLength = 8;

        /// <summary>The length of the user name field in a header buffer.</summary>
        public const int UsernameLength = 32;

        /// <summary>The length of the version field in a header buffer.</summary>
        public const int VersionLength = 2;

        #endregion Public Fields

        #region Public Constructors

        /// <summary>Initializes a new instance of the <see cref="TarHeader"/> class.</summary>
        public TarHeader()
        {
            Magic = DefaultMagic;
            Version = " ";

            Name = string.Empty;
            LinkName = string.Empty;

            UserId = DefaultUserId;
            GroupId = DefaultGroupId;
            UserName = DefaultUser;
            GroupName = DefaultGroupName;
            Size = 0;
        }

        #endregion Public Constructors

        #region Public Properties

        /// <summary>Gets or sets the entry's checksum. This is only valid/updated after writing or reading an entry.</summary>
        public int Checksum { get; set; }

        /// <summary>Gets or sets the entry's major device number.</summary>
        public int DevMajor { get; set; }

        /// <summary>Gets or sets the entry's minor device number.</summary>
        public int DevMinor
        {
            get { return devMinor; }
            set { devMinor = value; }
        }

        /// <summary>Gets or sets the entry's group id.</summary>
        /// <remarks>This is only directly relevant to linux/unix systems. The default value is zero.</remarks>
        public int GroupId { get; set; }

        /// <summary>Gets or sets the entry's group name.</summary>
        /// <remarks>This is only directly relevant to unix systems.</remarks>
        public string GroupName
        {
            get => groupName;
            set
            {
                if (value == null)
                {
                    groupName = "None";
                }
                else
                {
                    groupName = value;
                }
            }
        }

        /// <summary>Gets a value indicating whether the header checksum is valid, false otherwise.</summary>
        public bool IsChecksumValid
        {
            get { return isChecksumValid; }
        }

        /// <summary>Gets or sets the entry's link name.</summary>
        /// <exception cref="ArgumentNullException">Thrown when attempting to set LinkName to null.</exception>
        public string LinkName
        {
            get => linkName;
            set => linkName = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Gets or sets the entry's magic tag.</summary>
        /// <exception cref="ArgumentNullException">Thrown when attempting to set Magic to null.</exception>
        public string Magic
        {
            get => magic;
            set => magic = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Gets or sets the entry's Unix style permission mode.</summary>
        public int Mode { get; set; }

        /// <summary>Gets or sets the entry's modification time.</summary>
        /// <remarks>The modification time is only accurate to within a second.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when setting the date time to less than 1/1/1970.</exception>
        public DateTime ModTime
        {
            get => modTime;
            set
            {
                if (value < DateTime1970)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "ModTime cannot be before Jan 1st 1970");
                }

                modTime = new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second);
            }
        }

        /// <summary>Gets or sets the name for this tar entry.</summary>
        /// <exception cref="ArgumentNullException">Thrown when attempting to set the property to null.</exception>
        public string Name
        {
            get => name;
            set => name = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Gets or sets the entry's size.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when setting the size to less than zero.</exception>
        public long Size
        {
            get => size;
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Cannot be less than zero");
                }

                size = value;
            }
        }

        /// <summary>Gets or sets the entry's type flag.</summary>
        public TarEntryType TypeFlag { get; set; }

        /// <summary>Gets or sets the entry's user id.</summary>
        /// <remarks>This is only directly relevant to unix systems. The default is zero.</remarks>
        public int UserId
        {
            get { return userId; }
            set { userId = value; }
        }

        /// <summary>Gets or sets the entry's user name.</summary>
        public string UserName
        {
            get => userName;
            set
            {
                if (value != null)
                {
                    userName = value[..Math.Min(UsernameLength, value.Length)];
                }
                else
                {
                    var currentUser = "user";
                    if (currentUser.Length > UsernameLength)
                    {
                        currentUser = currentUser[..UsernameLength];
                    }

                    userName = currentUser;
                }
            }
        }

        /// <summary>Gets or sets the entry's version.</summary>
        /// <exception cref="ArgumentNullException">Thrown when attempting to set Version to null.</exception>
        public string Version
        {
            get => version;
            set => version = value ?? throw new ArgumentNullException(nameof(value));
        }

        #endregion Public Properties

        #region Public Methods

        /// <summary>Parse a name from a header buffer.</summary>
        /// <param name="encoding">Encoding used for names</param>
        /// <param name="header">The header buffer from which to parse.</param>
        /// <param name="offset">The offset into the buffer from which to parse.</param>
        /// <param name="length">The number of header bytes to parse.</param>
        /// <returns>The name parsed.</returns>
        public static string ParseName(StringEncoding encoding, byte[] header, int offset, int length)
        {
            var name = encoding.Decode(header, offset, length);
            var endIndex = name.IndexOf('\0');
            if (endIndex > -1) name = name[..endIndex];
            return name;
        }

        /// <summary>Parse an octal string from a header buffer.</summary>
        /// <param name="header">The header buffer from which to parse.</param>
        /// <param name="offset">The offset into the buffer from which to parse.</param>
        /// <param name="length">The number of header bytes to parse.</param>
        /// <returns>The long equivalent of the octal string.</returns>
        public static long ParseOctal(byte[] header, int offset, int length)
        {
            if (header == null)
            {
                throw new ArgumentNullException(nameof(header));
            }

            long result = 0;
            var stillPadding = true;

            var end = offset + length;
            for (var i = offset; i < end; ++i)
            {
                if (header[i] == 0)
                {
                    break;
                }

                if (header[i] == (byte)' ' || header[i] == '0')
                {
                    if (stillPadding)
                    {
                        continue;
                    }

                    if (header[i] == (byte)' ')
                    {
                        break;
                    }
                }

                stillPadding = false;

                result = (result << 3) + (header[i] - '0');
            }

            return result;
        }

        /// <summary>Writes data to the buffer.</summary>
        /// <param name="data">The data to add.</param>
        /// <param name="dataOffset">The offset of the first byte.</param>
        /// <param name="buffer">The buffer to add to.</param>
        /// <param name="bufferOffset">The index of the first byte to add.</param>
        /// <param name="length">The number of bytes to add.</param>
        /// <param name="allowContinue">Set to false if <paramref name="data"/> has to fit into <paramref name="length"/>, true otherwise.</param>
        /// <returns>The next free index in the <paramref name="buffer"/>.</returns>
        public static int WriteBytes(byte[] data, int dataOffset, byte[] buffer, int bufferOffset, int length, bool allowContinue)
        {
            var sourceLength = data.Length - dataOffset;
            if (sourceLength > length)
            {
                if (!allowContinue) throw new InvalidOperationException($"Try to write {sourceLength} to a buffer of length {length}!");
                sourceLength = length;
            }

            if (sourceLength > 0)
            {
                Array.Copy(data, dataOffset, buffer, bufferOffset, sourceLength);
                bufferOffset += sourceLength;
            }

            for (var i = sourceLength; i < length; ++i)
            {
                buffer[bufferOffset++] = 0;
            }
            return bufferOffset;
        }

        /// <summary>Put an octal representation of a value into a buffer.</summary>
        /// <param name="value">the value to be converted to octal.</param>
        /// <param name="buffer">buffer to store the octal string.</param>
        /// <param name="offset">The offset into the buffer where the value starts.</param>
        /// <param name="length">The length of the octal string to create.</param>
        /// <returns>The offset of the character next byte after the octal string.</returns>
        public static int WriteOctalBytes(long value, byte[] buffer, int offset, int length)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            var localIndex = length - 1;

            // Either a space or null is valid here. We use NULL as per GNUTar
            buffer[offset + localIndex] = 0;
            --localIndex;

            if (value > 0)
            {
                for (var v = value; (localIndex >= 0) && (v > 0); --localIndex)
                {
                    buffer[offset + localIndex] = (byte)((byte)'0' + (byte)(v & 7));
                    v >>= 3;
                }
            }

            for (; localIndex >= 0; --localIndex)
            {
                buffer[offset + localIndex] = (byte)'0';
            }

            return offset + length;
        }

        /// <summary>Writes string data to the buffer.</summary>
        /// <param name="encoding">Encoding to use</param>
        /// <param name="data">The data to add.</param>
        /// <param name="buffer">The buffer to add to.</param>
        /// <param name="bufferOffset">The index of the first byte to add.</param>
        /// <param name="length">The number of bytes to add.</param>
        /// <returns>The next free index in the <paramref name="buffer"/>.</returns>
        public static int WriteStringBytes(StringEncoding encoding, string data, byte[] buffer, int bufferOffset, int length)
        {
            return WriteBytes(encoding.Encode(data), 0, buffer, bufferOffset, length, false);
        }

        /// <summary>Create a new <see cref="TarHeader"/> that is a copy of the current instance.</summary>
        /// <returns>A new <see cref="object"/> that is a copy of the current instance.</returns>
        public object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>Determines if this instance is equal to the specified object.</summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns>true if the objects are equal, false otherwise.</returns>
        public override bool Equals(object obj)
        {
            bool result;
            if (obj is TarHeader localHeader)
            {
                result = (name == localHeader.name)
                    && (Mode == localHeader.Mode)
                    && (UserId == localHeader.UserId)
                    && (GroupId == localHeader.GroupId)
                    && (Size == localHeader.Size)
                    && (ModTime == localHeader.ModTime)
                    && (Checksum == localHeader.Checksum)
                    && (TypeFlag == localHeader.TypeFlag)
                    && (LinkName == localHeader.LinkName)
                    && (Magic == localHeader.Magic)
                    && (Version == localHeader.Version)
                    && (UserName == localHeader.UserName)
                    && (GroupName == localHeader.GroupName)
                    && (DevMajor == localHeader.DevMajor)
                    && (DevMinor == localHeader.DevMinor);
            }
            else
            {
                result = false;
            }

            return result;
        }

        /// <summary>Get a hash code for the current object.</summary>
        /// <returns>A hash code for the current object.</returns>
        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }

        /// <summary>Parse TarHeader information from a header buffer.</summary>
        /// <param name="encoding">Encoding used for names</param>
        /// <param name="header">The tar entry header buffer to get information from.</param>
        public void ParseBuffer(StringEncoding encoding, byte[] header)
        {
            if (header == null)
            {
                throw new ArgumentNullException(nameof(header));
            }

            var offset = 0;

            Name = ParseName(encoding, header, offset, NameLength);
            offset += NameLength;

            Mode = (int)ParseOctal(header, offset, ModeLength);
            offset += ModeLength;

            UserId = (int)ParseOctal(header, offset, UserIdLength);
            offset += UserIdLength;

            GroupId = (int)ParseOctal(header, offset, GroupIdLength);
            offset += GroupIdLength;

            Size = ParseBinaryOrOctal(header, offset, FileSizeLength);
            offset += FileSizeLength;

            ModTime = GetDateTimeFromCTime(ParseOctal(header, offset, ModificationTimeLength));
            offset += ModificationTimeLength;

            Checksum = (int)ParseOctal(header, offset, ChecksumLength);
            offset += ChecksumLength;

            TypeFlag = (TarEntryType)header[offset++];

            LinkName = ParseName(encoding, header, offset, NameLength).ToString();
            offset += NameLength;

            Magic = ParseName(encoding, header, offset, MagicLength).ToString();
            offset += MagicLength;

            if (Magic == "ustar")
            {
                Version = ParseName(encoding, header, offset, VersionLength).ToString();
                offset += VersionLength;

                UserName = ParseName(encoding, header, offset, UsernameLength).ToString();
                offset += UsernameLength;

                GroupName = ParseName(encoding, header, offset, GroupnameLength).ToString();
                offset += GroupnameLength;

                DevMajor = (int)ParseOctal(header, offset, DeviceLength);
                offset += DeviceLength;

                DevMinor = (int)ParseOctal(header, offset, DeviceLength);
                offset += DeviceLength;

                var prefix = ParseName(encoding, header, offset, PrefixLength).ToString();
                if (!string.IsNullOrEmpty(prefix))
                {
                    Name = prefix + '/' + Name;
                }
            }

            isChecksumValid = Checksum == MakeCheckSum(header);
        }

        /// <summary>'Write' header information to buffer provided, updating the <see cref="Checksum">check sum</see>.</summary>
        /// <param name="encoding">Encoding to use</param>
        /// <param name="outBuffer">output buffer for header information.</param>
        public void WriteHeader(StringEncoding encoding, byte[] outBuffer) => WriteHeader(encoding, outBuffer, null);

        /// <summary>'Write' header information to buffer provided, updating the <see cref="Checksum">check sum</see>.</summary>
        /// <param name="encoding">Encoding to use</param>
        /// <param name="outBuffer">output buffer for header information.</param>
        /// <param name="replacementName">replaces the name on the fly (e.g. long filenames)</param>
        public void WriteHeader(StringEncoding encoding, byte[] outBuffer, string replacementName)
        {
            if (outBuffer == null)
            {
                throw new ArgumentNullException(nameof(outBuffer));
            }

            var nameData = encoding.Encode(replacementName ?? Name);
            var offset = 0;
            offset = WriteBytes(nameData, 0, outBuffer, offset, NameLength, false);
            offset = WriteOctalBytes(Mode, outBuffer, offset, ModeLength);
            offset = WriteOctalBytes(UserId, outBuffer, offset, UserIdLength);
            offset = WriteOctalBytes(GroupId, outBuffer, offset, GroupIdLength);
            offset = GetBinaryOrOctalBytes(Size, outBuffer, offset, FileSizeLength);
            offset = WriteOctalBytes(GetCTime(ModTime), outBuffer, offset, ModificationTimeLength);

            var csOffset = offset;
            for (var c = 0; c < ChecksumLength; ++c)
            {
                outBuffer[offset++] = (byte)' ';
            }

            outBuffer[offset++] = (byte)TypeFlag;
            offset = WriteStringBytes(encoding, LinkName, outBuffer, offset, NameLength);
            offset = WriteStringBytes(StringEncoding.ASCII, Magic, outBuffer, offset, MagicLength);
            offset = WriteStringBytes(encoding, Version, outBuffer, offset, VersionLength);
            offset = WriteStringBytes(encoding, UserName, outBuffer, offset, UsernameLength);
            offset = WriteStringBytes(encoding, GroupName, outBuffer, offset, GroupnameLength);

            if ((TypeFlag == TarEntryType.Character) || (TypeFlag == TarEntryType.Block))
            {
                offset = WriteOctalBytes(DevMajor, outBuffer, offset, DeviceLength);
                offset = WriteOctalBytes(DevMinor, outBuffer, offset, DeviceLength);
            }

            for (; offset < outBuffer.Length;)
            {
                outBuffer[offset++] = 0;
            }

            Checksum = ComputeCheckSum(outBuffer);

            GetCheckSumOctalBytes(Checksum, outBuffer, csOffset, ChecksumLength);
            isChecksumValid = true;
        }

        #endregion Public Methods
    }
}
