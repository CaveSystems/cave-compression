using System;
using System.IO;
using System.Text;

namespace Cave.Compression
{
    /// <summary>
    /// provides access to a ar file header
    /// </summary>
    public class ArHeader
    {
        /// <summary>
        /// Reads a <see cref="ArHeader"/> from the specified stream
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static ArHeader FromStream(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            byte[] data = new byte[60];
            if (stream.Read(data, 0, 60) != 60) throw new EndOfStreamException();
            return new ArHeader(data);
        }

        /// <summary>
        /// Creates a new <see cref="ArHeader"/> for the specified file.
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        public static ArHeader Create(string file)
        {
            FileInfo info = new FileInfo(file);
            return CreateFile(Path.GetFileName(file), info.Length, 644, 0, 0, info.LastWriteTime);
        }

        /// <summary>
        /// Creates a new <see cref="ArHeader"/> for the specified file name. (Name may not contain subdirectories)
        /// </summary>
        /// <param name="name"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public static ArHeader CreateFile(string name, long size)
        {
            return CreateFile(name, size, 644, 0, 0, DateTime.Now);
        }

        /// <summary>
        /// Creates a new <see cref="ArHeader"/> for the specified file name. (Name may not contain subdirectories)
        /// </summary>
        /// <param name="name"></param>
        /// <param name="size"></param>
        /// <param name="fileMode"></param>
        /// <returns></returns>
        public static ArHeader CreateFile(string name, long size, int fileMode)
        {
            return CreateFile(name, size, fileMode, 0, 0, DateTime.Now);
        }

        /// <summary>
        /// Creates a new <see cref="ArHeader"/> for the specified file name. (Name may not contain subdirectories)
        /// </summary>
        /// <param name="name"></param>
        /// <param name="size"></param>
        /// <param name="fileMode"></param>
        /// <param name="owner"></param>
        /// <param name="group"></param>
        /// <returns></returns>
        public static ArHeader CreateFile(string name, long size, int fileMode, int owner, int group)
        {
            return CreateFile(name, size, fileMode, owner, group, DateTime.Now);
        }

        /// <summary>
        /// Creates a new <see cref="ArHeader"/> for the specified file name. (Name may not contain subdirectories)
        /// </summary>
        /// <param name="name"></param>
        /// <param name="size"></param>
        /// <param name="fileMode"></param>
        /// <param name="owner"></param>
        /// <param name="group"></param>
        /// <param name="modificationTime"></param>
        /// <returns></returns>
        public static ArHeader CreateFile(string name, long size, int fileMode, int owner, int group, DateTime modificationTime)
        {
            ArHeader result = new ArHeader();
            result.m_Initialize(name, size, fileMode, owner, group, modificationTime);
            return result;
        }

        #region constructors
        /// <summary>
        /// Creates a new empty <see cref="ArHeader"/>
        /// </summary>
        private ArHeader() { }

        /// <summary>
        /// Creates a new <see cref="ArHeader"/> with the specified data
        /// </summary>
        /// <param name="data"></param>
        private ArHeader(byte[] data)
        {
            m_Data = data;
            if (m_Data.Length != 60) throw new InvalidDataException(string.Format("Data length invalid!"));
            if ((m_Data[58] != 0x60) || (m_Data[59] != 0x0A)) throw new InvalidDataException(string.Format("Invalid data! (File magic not correct)"));
        }

        /// <summary>
        /// creates a new ar file header block with default owner, group, FileType and filemode (root:root 644 type dir or file)
        /// </summary>
        /// <param name="file">Name and path of the file/directory at the local system</param>
        public ArHeader(string file)
            : this(file, 644, 0, 0)
        {
        }

        /// <summary>
        /// creates a new ar file header block with specified owner, group, and filemode (root:root 644) type is selected automatically
        /// </summary>
        /// <param name="file">FileName and path of the file at the local system</param>
        /// <param name="fileMode">the unix filemode to use</param>
        /// <param name="owner">the unix owner</param>
        /// <param name="group">the unix group</param>
        public ArHeader(string file, int fileMode, int owner, int group)
        {
            if (File.Exists(file))
            {
                FileInfo info = new FileInfo(file);
                m_Initialize(info.Name, info.Length, fileMode, owner, group, info.LastWriteTime);
            }
            else throw new FileNotFoundException(string.Format("File '{0}' not found!", file));
        }
        #endregion

        #region private functionality
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Literale nicht als lokalisierte Parameter übergeben", MessageId = "Cave.IO.ArHeader.m_SetString(System.Int32,System.Int32,System.String)")]
        private void m_Initialize(string name, long size, int fileMode, int owner, int group, DateTime modificationTime)
        {
            if (fileMode <= 0)
            {
                fileMode = 644;
            }
            if (owner < 0) throw new ArgumentException(string.Format("Owner may not be a value smaller than 0!"), "owner");
            if (group < 0) throw new ArgumentException(string.Format("Group may not be a value smaller than 0!"), "group");

            //check FileSize
            if (size > 0x7FFFFFFFL) throw new NotSupportedException(string.Format("A FileSize with more then 2GiB is currently not supported!"));

            //prepare unix file path and name
            string fileNameInAr = "";
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c >= 32 && c < 128)
                {
                    if (Array.IndexOf(new char[] { '\\', '/' }, c) >= 0)
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
            //check length
            if (fileNameInAr.Length > 15) throw new ArgumentException(string.Format("FileName may not exceed 15 chars!"), "name");
            fileNameInAr += "/";
            //set FileName
            m_SetString(0, 16, fileNameInAr);
            //set modification time
            m_SetValue(16, 12, (int)modificationTime.ToUniversalTime().Subtract(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds, 10);
            //set owner
            m_SetString(28, 6, owner.ToString());
            //set group
            m_SetString(34, 6, group.ToString());
            //set filemode
            m_SetString(40, 8, fileMode.ToString());
            //set FileSize
            m_SetValue(48, 10, (int)size, 10);
            //set file magic
            m_Data[58] = 0x60;
            m_Data[59] = 0x0A;
        }

        private void m_SetString(int index, int count, string text)
        {
            string result = text;
            if (result.Length < count) result += new string(' ', count - result.Length);
            if (result.Length > count) throw new ArgumentOutOfRangeException(string.Format("String range out of bounds!"));
            foreach (byte b in ASCII.GetBytes(result))
            {
                m_Data[index++] = b;
            }
        }

        private void m_SetValue(int index, int count, int value, int toBase)
        {
            m_SetString(index, count, Convert.ToString(value, toBase));
        }

        private string m_GetString(int index, int count)
        {
            StringBuilder result = new StringBuilder();
            bool nullDetected = false;
            string str = ASCII.GetString(m_Data, index, count);
            foreach (char c in str)
            {
                if (c == '\0')
                {
                    nullDetected = true;
                }
                else
                {
                    if ((c != ' ') && (nullDetected) && (result.Length > 0)) throw new FormatException(string.Format("Format failure! (Found ascii null in the middle of the string)!"));
                    result.Append(c);
                }
            }
            return result.ToString();
        }

        private int m_GetValue(int index, int count, int toBase)
        {
            //0 = no valid char found
            //1 = at least one valid octal number
            //2 = space after number found, there shouldn't be any more valid numbers
            int valueDetection = 0;
            int result = 0;
            string str = m_GetString(index, count);
            foreach (char c in str)
            {
                switch (c)
                {
                    case '\0':
                    case ' ':
                        if (valueDetection == 1) valueDetection = 2;
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
                        if (valueDetection == 2) throw new FormatException(string.Format("Format of the stored number not correct ! (Found space in the middle of the number)"));
                        if (valueDetection == 0) valueDetection = 1;
                        int value = c - '0';
                        if (value >= toBase) throw new FormatException(string.Format("Format of the stored number not correct ! (Value exceeds base)"));
                        result = result * toBase + value;
                        break;
                    default:
                        throw new FormatException(string.Format("Format of the stored number not correct ! (Illegal character found)"));
                }
            }
            return result;
        }

        #endregion

        #region public functionality
        byte[] m_Data = new byte[60];

        /// <summary>
        /// retrieves a copy of the header data
        /// </summary>
        public byte[] Data { get { return (byte[])m_Data.Clone(); } }

        /// <summary>
        /// retrieves the unix FileName
        /// </summary>
        public string FileName
        {
            get
            {
                string name = m_GetString(0, 16);
                return name.Substring(0, name.LastIndexOf('/'));
            }
        }

        /// <summary>
        /// retrieves the unix file mode (ugo) default = 640
        /// </summary>
        public int FileMode
        {
            get
            {
                return m_GetValue(40, 8, 10);
            }
        }

        /// <summary>
        /// retrieves the unix owner id
        /// </summary>
        public int Owner
        {
            get
            {
                return m_GetValue(28, 6, 10);
            }
        }

        /// <summary>
        /// retrieves the unix group id
        /// </summary>
        public int Group
        {
            get
            {
                return m_GetValue(34, 6, 10);
            }
        }

        /// <summary>
        /// retrieves the file size
        /// </summary>
        public int FileSize
        {
            get
            {
                return m_GetValue(48, 10, 10);
            }
        }

        /// <summary>
        /// retrieves the last modification date (utc)
        /// </summary>
        public DateTime LastWriteTime
        {
            get
            {
                return (new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)).AddSeconds(m_GetValue(16, 12, 10)).ToLocalTime();
            }
        }

        /// <summary>
        /// retrieves a summary of the header
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return string.Format("File:") + FileName + " " + FileMode + " " + Owner + ":" + Group + " " + FileSize;
        }
        #endregion
    }
}
