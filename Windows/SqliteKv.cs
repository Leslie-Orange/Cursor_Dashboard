using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

internal static class SqliteKv
{
    public static string Read(string path, string key)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        byte[] data = ReadBytes(path);
        if (data == null)
        {
            string temp = Path.Combine(Path.GetTempPath(), "cursor-quota-" + Guid.NewGuid().ToString("N") + ".vscdb");
            try
            {
                File.Copy(path, temp, true);
                data = File.ReadAllBytes(temp);
            }
            catch
            {
                return null;
            }
            finally
            {
                try { File.Delete(temp); }
                catch { }
            }
        }

        if (data == null || data.Length < 100)
        {
            return null;
        }

        string value = ReadFromBtree(data, key);
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        return HeuristicFind(data, key);
    }

    private static byte[] ReadBytes(string path)
    {
        try
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                MemoryStream memory = new MemoryStream();
                byte[] buffer = new byte[64 * 1024];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    memory.Write(buffer, 0, read);
                }
                return memory.ToArray();
            }
        }
        catch
        {
            return null;
        }
    }

    private static string ReadFromBtree(byte[] data, string key)
    {
        try
        {
            if (Encoding.ASCII.GetString(data, 0, 16) != "SQLite format 3\0")
            {
                return null;
            }

            int pageSize = (data[16] << 8) | data[17];
            if (pageSize == 1)
            {
                pageSize = 65536;
            }
            if (pageSize < 512 || (pageSize & (pageSize - 1)) != 0)
            {
                return null;
            }

            int reserved = data[20];
            int usable = pageSize - reserved;
            if (usable < 100)
            {
                return null;
            }

            int itemRoot = FindItemTableRoot(data, pageSize, usable);
            if (itemRoot <= 0)
            {
                return null;
            }

            return FindValue(data, pageSize, usable, itemRoot, key);
        }
        catch
        {
            return null;
        }
    }

    private static int FindItemTableRoot(byte[] data, int pageSize, int usable)
    {
        List<int> pages = new List<int>();
        pages.Add(1);
        for (int i = 0; i < pages.Count; i++)
        {
            int page = pages[i];
            int pageOffset = (page - 1) * pageSize;
            if (pageOffset + 12 >= data.Length)
            {
                continue;
            }

            int headerOffset = page == 1 ? pageOffset + 100 : pageOffset;
            if (headerOffset + 8 >= data.Length)
            {
                continue;
            }

            byte flag = data[headerOffset];
            int cellCount = ReadUInt16(data, headerOffset + 3);
            int pointerStart = headerOffset + 8;
            if (flag == 0x05)
            {
                pointerStart = headerOffset + 12;
                int right = ReadUInt32(data, headerOffset + 8);
                if (right > 0)
                {
                    pages.Add(right);
                }
            }

            for (int c = 0; c < cellCount; c++)
            {
                int ptrPos = pointerStart + c * 2;
                if (ptrPos + 1 >= data.Length)
                {
                    break;
                }
                int cell = pageOffset + ReadUInt16(data, ptrPos);
                if (flag == 0x05)
                {
                    if (cell + 4 < data.Length)
                    {
                        pages.Add(ReadUInt32(data, cell));
                    }
                    continue;
                }
                if (flag != 0x0D)
                {
                    continue;
                }

                List<object> record = ReadLeafRecord(data, pageSize, usable, pageOffset, cell);
                if (record == null || record.Count < 4)
                {
                    continue;
                }

                string name = record[1] as string;
                if (name != "ItemTable")
                {
                    continue;
                }

                double? root = JsonUtil.Number(record[3]);
                if (root.HasValue && root.Value > 0)
                {
                    return (int)root.Value;
                }
            }
        }
        return 0;
    }

    private static string FindValue(byte[] data, int pageSize, int usable, int root, string key)
    {
        List<int> pages = new List<int>();
        pages.Add(root);
        for (int i = 0; i < pages.Count; i++)
        {
            int page = pages[i];
            int pageOffset = (page - 1) * pageSize;
            if (pageOffset + 12 >= data.Length)
            {
                continue;
            }

            byte flag = data[pageOffset];
            int cellCount = ReadUInt16(data, pageOffset + 3);
            int pointerStart = pageOffset + 8;
            if (flag == 0x05)
            {
                pointerStart = pageOffset + 12;
                int right = ReadUInt32(data, pageOffset + 8);
                if (right > 0)
                {
                    pages.Add(right);
                }
            }

            for (int c = 0; c < cellCount; c++)
            {
                int ptrPos = pointerStart + c * 2;
                if (ptrPos + 1 >= data.Length)
                {
                    break;
                }
                int cell = pageOffset + ReadUInt16(data, ptrPos);
                if (flag == 0x05)
                {
                    if (cell + 4 < data.Length)
                    {
                        pages.Add(ReadUInt32(data, cell));
                    }
                    continue;
                }
                if (flag != 0x0D)
                {
                    continue;
                }

                List<object> record = ReadLeafRecord(data, pageSize, usable, pageOffset, cell);
                if (record == null || record.Count < 2)
                {
                    continue;
                }

                string rowKey = CoerceString(record[0]);
                if (rowKey != key)
                {
                    continue;
                }

                string value = CoerceString(record[1]);
                if (!string.IsNullOrEmpty(value))
                {
                    return value.Trim();
                }
            }
        }
        return null;
    }

    private static List<object> ReadLeafRecord(byte[] data, int pageSize, int usable, int pageOffset, int cell)
    {
        if (cell < 0 || cell >= data.Length)
        {
            return null;
        }

        int offset = cell;
        long payloadSize = ReadVarint(data, ref offset);
        ReadVarint(data, ref offset);

        byte[] payload = ReadPayload(data, pageSize, usable, pageOffset, offset, payloadSize, true);
        if (payload == null || payload.Length == 0)
        {
            return null;
        }

        int pos = 0;
        long headerSize = ReadVarint(payload, ref pos);
        int headerEnd = (int)headerSize;
        if (headerEnd > payload.Length || headerEnd < pos)
        {
            return null;
        }

        List<long> serials = new List<long>();
        while (pos < headerEnd)
        {
            serials.Add(ReadVarint(payload, ref pos));
        }

        pos = headerEnd;
        List<object> values = new List<object>();
        for (int i = 0; i < serials.Count; i++)
        {
            object value;
            pos = ReadColumn(payload, pos, serials[i], out value);
            if (pos < 0)
            {
                return null;
            }
            values.Add(value);
        }
        return values;
    }

    private static byte[] ReadPayload(byte[] data, int pageSize, int usable, int pageOffset, int localStart, long payloadSize, bool tableLeaf)
    {
        int maxLocal = tableLeaf ? usable - 35 : usable - 6;
        if (payloadSize < 0 || payloadSize > 16 * 1024 * 1024)
        {
            return null;
        }

        if (payloadSize <= maxLocal)
        {
            int size = (int)payloadSize;
            if (localStart + size > data.Length)
            {
                return null;
            }
            byte[] local = new byte[size];
            Buffer.BlockCopy(data, localStart, local, 0, size);
            return local;
        }

        int minLocal = ((usable - 12) * 32 / 255) - 23;
        if (minLocal < 0)
        {
            minLocal = 0;
        }
        int surplus = (int)((payloadSize - minLocal) % (usable - 4));
        int localSize = minLocal + surplus;
        if (localSize > maxLocal)
        {
            localSize = minLocal;
        }

        MemoryStream memory = new MemoryStream((int)payloadSize);
        if (localStart + localSize + 4 > data.Length)
        {
            return null;
        }
        memory.Write(data, localStart, localSize);
        int overflowPage = ReadUInt32(data, localStart + localSize);
        int remaining = (int)payloadSize - localSize;
        int guard = 0;
        while (remaining > 0 && overflowPage > 0 && guard++ < 1024)
        {
            int overflowOffset = (overflowPage - 1) * pageSize;
            if (overflowOffset + 4 >= data.Length)
            {
                break;
            }
            int next = ReadUInt32(data, overflowOffset);
            int chunk = Math.Min(remaining, usable - 4);
            if (overflowOffset + 4 + chunk > data.Length)
            {
                chunk = data.Length - overflowOffset - 4;
            }
            if (chunk <= 0)
            {
                break;
            }
            memory.Write(data, overflowOffset + 4, chunk);
            remaining -= chunk;
            overflowPage = next;
        }
        return memory.ToArray();
    }

    private static int ReadColumn(byte[] payload, int pos, long serial, out object value)
    {
        value = null;
        if (serial == 0)
        {
            return pos;
        }
        if (serial == 8)
        {
            value = 0L;
            return pos;
        }
        if (serial == 9)
        {
            value = 1L;
            return pos;
        }
        if (serial == 1)
        {
            if (pos >= payload.Length) return -1;
            value = (long)(sbyte)payload[pos];
            return pos + 1;
        }
        if (serial == 2)
        {
            if (pos + 1 >= payload.Length) return -1;
            value = (long)(short)((payload[pos] << 8) | payload[pos + 1]);
            return pos + 2;
        }
        if (serial == 3)
        {
            if (pos + 2 >= payload.Length) return -1;
            int n = (payload[pos] << 16) | (payload[pos + 1] << 8) | payload[pos + 2];
            if ((n & 0x800000) != 0) n |= unchecked((int)0xFF000000);
            value = (long)n;
            return pos + 3;
        }
        if (serial == 4)
        {
            if (pos + 3 >= payload.Length) return -1;
            value = (long)ReadInt32(payload, pos);
            return pos + 4;
        }
        if (serial == 5)
        {
            if (pos + 5 >= payload.Length) return -1;
            long n = ((long)payload[pos] << 40) | ((long)payload[pos + 1] << 32) | ((long)payload[pos + 2] << 24)
                | ((long)payload[pos + 3] << 16) | ((long)payload[pos + 4] << 8) | payload[pos + 5];
            if ((n & 0x800000000000L) != 0) n |= unchecked((long)0xFFFF000000000000);
            value = n;
            return pos + 6;
        }
        if (serial == 6)
        {
            if (pos + 7 >= payload.Length) return -1;
            value = ReadInt64(payload, pos);
            return pos + 8;
        }
        if (serial == 7)
        {
            if (pos + 7 >= payload.Length) return -1;
            byte[] bits = new byte[8];
            for (int i = 0; i < 8; i++)
            {
                bits[7 - i] = payload[pos + i];
            }
            value = BitConverter.ToDouble(bits, 0);
            return pos + 8;
        }
        if (serial >= 12 && serial % 2 == 0)
        {
            int length = (int)((serial - 12) / 2);
            if (pos + length > payload.Length) return -1;
            byte[] blob = new byte[length];
            Buffer.BlockCopy(payload, pos, blob, 0, length);
            value = blob;
            return pos + length;
        }
        if (serial >= 13 && serial % 2 == 1)
        {
            int length = (int)((serial - 13) / 2);
            if (pos + length > payload.Length) return -1;
            value = Encoding.UTF8.GetString(payload, pos, length);
            return pos + length;
        }
        return pos;
    }

    private static string CoerceString(object value)
    {
        if (value == null)
        {
            return null;
        }
        string text = value as string;
        if (text != null)
        {
            return text;
        }
        byte[] blob = value as byte[];
        if (blob != null)
        {
            return Encoding.UTF8.GetString(blob);
        }
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string HeuristicFind(byte[] data, string key)
    {
        byte[] needle = Encoding.UTF8.GetBytes(key);
        int index = IndexOf(data, needle);
        if (index < 0)
        {
            return null;
        }

        int start = index + needle.Length;
        while (start < data.Length && data[start] < 33)
        {
            start++;
        }
        if (start >= data.Length)
        {
            return null;
        }

        int end = start;
        while (end < data.Length && data[end] >= 33 && data[end] < 127)
        {
            end++;
        }
        if (end <= start)
        {
            return null;
        }
        return Encoding.ASCII.GetString(data, start, end - start);
    }

    private static int IndexOf(byte[] data, byte[] needle)
    {
        int last = data.Length - needle.Length;
        for (int i = 0; i <= last; i++)
        {
            int j = 0;
            for (; j < needle.Length; j++)
            {
                if (data[i + j] != needle[j])
                {
                    break;
                }
            }
            if (j == needle.Length)
            {
                return i;
            }
        }
        return -1;
    }

    private static int ReadUInt16(byte[] data, int offset)
    {
        return (data[offset] << 8) | data[offset + 1];
    }

    private static int ReadUInt32(byte[] data, int offset)
    {
        return (int)((uint)(data[offset] << 24) | (uint)(data[offset + 1] << 16) | (uint)(data[offset + 2] << 8) | data[offset + 3]);
    }

    private static int ReadInt32(byte[] data, int offset)
    {
        return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
    }

    private static long ReadInt64(byte[] data, int offset)
    {
        uint hi = (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        uint lo = (uint)((data[offset + 4] << 24) | (data[offset + 5] << 16) | (data[offset + 6] << 8) | data[offset + 7]);
        return (long)(((ulong)hi << 32) | lo);
    }

    private static long ReadVarint(byte[] data, ref int offset)
    {
        long value = 0;
        for (int i = 0; i < 8; i++)
        {
            if (offset >= data.Length)
            {
                return value;
            }
            byte b = data[offset++];
            value = (value << 7) | (long)((uint)b & 0x7Fu);
            if ((b & 0x80) == 0)
            {
                return value;
            }
        }
        if (offset >= data.Length)
        {
            return value;
        }
        return (value << 8) | (long)data[offset++];
    }
}
