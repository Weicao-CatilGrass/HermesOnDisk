// ┌─────────────────────────────────────────────────────────────────────────────────────┐
// │ "The Hermes On Your Disk!".cs                                                       │
// │                                                                                     │
// │   It's a dumb, stupid game archive system, but FAST.                                │
// │                                                                                     │
// │ # HOW TO IMPORT?                                                                    │
// │                                                                                     │
// │   Just drag into your project :)                                                    │
// │                                                                                     │
// │ # LICENSE                                                                           │
// │                                                                                     │
// │  Copyright (c) 2026 Weicao-CatilGrass                                               │
// │                                                                                     │
// │  Permission is hereby granted, free of charge, to any person obtaining a copy       │
// │      of this software and associated documentation files (the "Software"), to deal  │
// │      in the Software without restriction, including without limitation the rights   │
// │  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell          │
// │  copies of the Software, and to permit persons to whom the Software is              │
// │      furnished to do so, subject to the following conditions:                       │
// │                                                                                     │
// │  The above copyright notice and this permission notice shall be included in all     │
// │      copies or substantial portions of the Software.                                │
// │                                                                                     │
// │      THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR     │
// │      IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,       │
// │      FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE    │
// │      AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER         │
// │      LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,  │
// │  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE      │
// │  SOFTWARE.                                                                          │
// └─────────────────────────────────────────────────────────────────────────────────────┘

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

#if UNITY_2020_1_OR_NEWER
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
#endif

namespace HermesOnDisk
{
    #region Core
    // ┌───────────┐
    // │   USAGE   │
    // └───────────┘
    class Usage
    {
        private void YourMain()
        {
            // Set save location
            Hermes.Root = new DirectoryInfo(".");

            // Read boolean
            var doorOpened = Hermes.Boolean[2];

            // Set boolean
            Hermes.Boolean[0] = true;

            // Read float
            var health = Hermes.Float[0];

            // Set float
            Hermes.Float[0] = 0;

            // Read name
            var name = Hermes.String[0];

            // Set name
            Hermes.String[0] = "Peter";

            // Save
            Hermes.Store();

            // Garbage collect variable-length data
            Hermes.GC();
        }
    }

#if UNITY_2020_1_OR_NEWER
#else
    public static class HermesOnDisk
    {
        /// <summary>
        /// Returns true if there are uncommitted changes in memory.
        /// </summary>
        public static bool IsDirty => Hermes.IsDirty;

        /// <summary>
        /// Boolean indexer
        /// </summary>
        public static Hermes.BoolIndexer Boolean => Hermes.Boolean;

        /// <summary>
        /// Float indexer
        /// </summary>
        public static Hermes.FloatIndexer Float => Hermes.Float;

        /// <summary>
        /// String indexer
        /// </summary>
        public static Hermes.StringIndexer String => Hermes.String;

        /// <summary>
        /// Variable-length byte array indexer
        /// </summary>
        public static Hermes.ByteIndexer Bytes => Hermes.Bytes;

        /// <summary>
        /// Root directory
        /// </summary>
        public static DirectoryInfo Root
        {
            get => Hermes.Root;
            set => Hermes.Root = value;
        }

        /// <summary>
        /// Store to disk
        /// </summary>
        public static void Store()
            => Hermes.Store();

        /// <summary>
        /// Asynchronously fully rewrites pack.dat
        /// </summary>
        public static void AsyncGC()
            => Hermes.AsyncGC();

        /// <summary>
        /// Full rewrite of pack.dat, reclaiming freed space
        /// </summary>
        public static void GC()
            => Hermes.GC();

        /// <summary>
        /// Backup pack.dat → pack.dat.backup
        /// </summary>
        public static void BackupPack()
            => Hermes.BackupPack();
    }
#endif

    public static class Hermes
    {
        private static HermesPathfinder _hermesPathfinder = new(new DirectoryInfo("."));

        /// <summary>
        /// Returns true if there are uncommitted changes in memory.
        /// </summary>
        public static bool IsDirty =>
            ModifiedBooleans.Count > 0 || ModifiedFloat.Count > 0 || ModifiedByteMap.Count > 0;

        // ┌─────────────────────────────────────────────┐
        // │  Pack file constants (COW + pointer table)  │
        // └─────────────────────────────────────────────┘
        private const string PackDataFile = "pack.dat";
        private const string PackIndexFile = "pack.idx";
        private const string PackGcFile = "pack.gc";

        /// <summary>
        /// Boolean indexer
        /// </summary>
        public static BoolIndexer Boolean { get; } = new();

        /// <summary>
        /// Float indexer
        /// </summary>
        public static FloatIndexer Float { get; } = new();

        /// <summary>
        /// Variable-length byte array indexer
        /// </summary>
        public static ByteIndexer Bytes { get; } = new();

        /// <summary>
        /// String indexer
        /// </summary>
        public static StringIndexer String { get; } = new();

        /// <summary>
        /// Root directory
        /// </summary>
        public static DirectoryInfo Root
        {
            set => _hermesPathfinder = new HermesPathfinder(value);
            get => _hermesPathfinder.RootDirectory;
        }

        /// <summary>
        /// Store to disk
        /// </summary>
        public static void Store()
        {
            if (!IsDirty) return;
            if (ModifiedByteMap.Count > 0) BackupPack();
            StoreAll();
        }

        /// <summary>
        /// Backup pack.dat → pack.dat.backup
        /// </summary>
        public static void BackupPack()
        {
            var dataFile = _hermesPathfinder.RootDirectory.JoinToFile(PackDataFile);
            if (dataFile.Exists)
            {
                var backup = new FileInfo(dataFile.FullName + ".backup");
                dataFile.CopyTo(backup.FullName, true);
            }
        }

        /// <summary>
        /// Full rewrite of pack.dat, reclaiming freed space
        /// </summary>
        public static void GC()
        {
            GcCore();
        }

        /// <summary>
        /// Asynchronously fully rewrites pack.dat
        /// </summary>
        public static void AsyncGC()
        {
            System.Threading.Tasks.Task.Run(() => GcCore());
        }

        private static void GcCore()
        {
            var dataFile = _hermesPathfinder.RootDirectory.JoinToFile(PackDataFile);
            var idxFile = _hermesPathfinder.RootDirectory.JoinToFile(PackIndexFile);
            if (!idxFile.Exists) return;

            long idxLen = idxFile.Length;
            int numEntries = (int)(idxLen / 8);
            if (numEntries == 0) return;

            var liveEntries = new List<(uint index, byte[] data, uint oldLen)>();
            bool hasGarbage = false;

            for (uint i = 0; i < (uint)numEntries; i++)
            {
                if (!TryReadPackIndex(i, out uint offset, out uint length) || length == 0)
                {
                    hasGarbage = true;
                    continue;
                }

                if (TryReadSlice(dataFile, offset, length, out byte[] buffer))
                    liveEntries.Add((i, buffer, length));
                else
                    hasGarbage = true;
            }

            var gcFile = _hermesPathfinder.RootDirectory.JoinToFile(PackGcFile);
            if (!hasGarbage && (!gcFile.Exists || gcFile.Length == 0))
                return;

            var tmpDataFile = _hermesPathfinder.RootDirectory.JoinToFile(PackDataFile + ".tmp");
            var tmpIdxFile = _hermesPathfinder.RootDirectory.JoinToFile(PackIndexFile + ".tmp");
            if (tmpDataFile.Directory != null) tmpDataFile.Directory.Create();

            var liveMap = liveEntries.ToDictionary(e => e.index, e => (e.data, e.oldLen));

            using (var dataFs = new FileStream(tmpDataFile.FullName, FileMode.Create, FileAccess.Write))
            using (var idxFs = new FileStream(tmpIdxFile.FullName, FileMode.Create, FileAccess.Write))
            {
                byte[] indexBuffer = new byte[8];
                for (uint i = 0; i < (uint)numEntries; i++)
                {
                    if (liveMap.TryGetValue(i, out var entry))
                    {
                        // Live entry: write data to pack, update index with new offset/length
                        uint newOffset = (uint)dataFs.Position;
                        dataFs.Write(entry.data, 0, entry.data.Length);

                        BitConverter.GetBytes(newOffset).CopyTo(indexBuffer, 0);
                        BitConverter.GetBytes((uint)entry.data.Length).CopyTo(indexBuffer, 4);
                    }
                    else
                    {
                        // Dead entry: clear to zero (offset=0, length=0)
                        Array.Clear(indexBuffer, 0, 8);
                    }

                    long idxPos = i * 8;
                    if (idxFs.Length < idxPos + 8)
                        idxFs.SetLength(idxPos + 8);
                    idxFs.Seek(idxPos, SeekOrigin.Begin);
                    idxFs.Write(indexBuffer, 0, 8);
                }
            }

            if (dataFile.Exists) dataFile.Delete();
            if (idxFile.Exists) idxFile.Delete();
            tmpDataFile.MoveTo(dataFile.FullName);
            tmpIdxFile.MoveTo(idxFile.FullName);

            if (gcFile.Exists) gcFile.Delete();
        }

        private static readonly SortedDictionary<uint, bool> CacheBooleans = new();
        private static readonly SortedDictionary<uint, float> CacheFloats = new();
        private static readonly SortedDictionary<uint, byte[]> CacheByteMap = new();

        private static readonly HashSet<uint> ModifiedBooleans = new();
        private static readonly HashSet<uint> ModifiedFloat = new();
        private static readonly HashSet<uint> ModifiedByteMap = new();

        /// <summary>
        /// Attempts to read a boolean value at the specified index.
        /// </summary>
        /// <param name="index">The global index of the boolean.</param>
        /// <param name="value">When this method returns, contains the boolean value if successful; otherwise, false.</param>
        /// <returns>true if the value was successfully read; otherwise, false.</returns>
        private static bool ReadBool(uint index, out bool value)
        {
            // Try to get the boolean from the cache
            if (CacheBooleans.TryGetValue(index, out value))
            {
                // Cache hit, return success
                return true;
            }

            // Cache miss, attempt to load from disk
            var file = _hermesPathfinder.GetBoolPathFromIndex(index);
            var localIndex = _hermesPathfinder.IndexToLocalIndex(index);
            var offset = localIndex;
            var length = 1;

            // Try to read the slice from the file
            if (TryReadSlice(file, offset, length, out byte[] buffer))
            {
                value = buffer[0] == 1;

                // Store into cache
                CacheBooleans[index] = value;
                return true;
            }

            value = false;
            return false;
        }

        /// <summary>
        /// Writes a boolean value at the specified index.
        /// </summary>
        /// <param name="index">The global index of the boolean.</param>
        /// <param name="value">The boolean value to write.</param>
        private static void WriteBool(uint index, bool value)
        {
            // Write to cache
            CacheBooleans[index] = value;

            // Mark the value as modified
            ModifiedBooleans.Add(index);
        }

        /// <summary>
        /// Attempts to read a float value at the specified index.
        /// </summary>
        /// <param name="index">The global index of the float.</param>
        /// <param name="value">When this method returns, contains the float value if successful; otherwise, 0f.</param>
        /// <returns>true if the value was successfully read; otherwise, false.</returns>
        private static bool ReadFloat(uint index, out float value)
        {
            // Try to get the float from the cache
            if (CacheFloats.TryGetValue(index, out value))
            {
                // Cache hit, return success
                return true;
            }

            // Cache miss, attempt to load from disk
            var file = _hermesPathfinder.GetFloatPathFromIndex(index);
            var localIndex = _hermesPathfinder.IndexToLocalIndex(index);
            var offset = localIndex * 4;
            var length = 4;

            // Try to read the slice from the file
            if (TryReadSlice(file, offset, length, out byte[] buffer))
            {
                value = BitConverter.ToSingle(buffer, 0);

                // Store into cache
                CacheFloats[index] = value;
                return true;
            }

            value = 0f;
            return false;
        }

        /// <summary>
        /// Writes a float value at the specified index.
        /// </summary>
        /// <param name="index">The global index of the float.</param>
        /// <param name="value">The float value to write.</param>
        private static void WriteFloat(uint index, float value)
        {
            // Write to cache
            CacheFloats[index] = value;

            // Mark the value as modified
            ModifiedFloat.Add(index);
        }

        /// <summary>
        /// Attempts to read a byte array value at the specified index from the pack file system.
        /// </summary>
        /// <param name="index">The global index of the byte array.</param>
        /// <param name="value">When this method returns, contains the byte array if successful; otherwise, null.</param>
        /// <returns>true if the value was successfully read; otherwise, false.</returns>
        private static bool ReadByteArray(uint index, out byte[] value)
        {
            if (CacheByteMap.TryGetValue(index, out value))
                return true;

            if (TryReadPackIndex(index, out uint offset, out uint length) && length > 0)
            {
                var dataFile = _hermesPathfinder.RootDirectory.JoinToFile(PackDataFile);
                if (TryReadSlice(dataFile, offset, length, out byte[] buffer))
                {
                    value = buffer;
                    CacheByteMap[index] = value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Writes a byte array value at the specified index.
        /// The data is appended to the pack file, and the old space (if any) is marked for garbage collection.
        /// </summary>
        /// <param name="index">The global index of the byte array.</param>
        /// <param name="value">The byte array value to write.</param>
        private static void WriteByteArray(uint index, byte[] value)
        {
            if (value != null)
                CacheByteMap[index] = value;
            else
                CacheByteMap.Remove(index);
            ModifiedByteMap.Add(index);
        }

        /// <summary>
        /// Attempts to read a pointer entry from the pack index file.
        /// </summary>
        /// <param name="index">The entry index in the pointer table.</param>
        /// <param name="offset">When this method returns, contains the offset in pack.dat if successful; otherwise, 0.</param>
        /// <param name="length">When this method returns, contains the length of the data if successful; otherwise, 0.</param>
        /// <returns>true if the pointer entry was successfully read; otherwise, false.</returns>
        private static bool TryReadPackIndex(uint index, out uint offset, out uint length)
        {
            offset = 0;
            length = 0;
            var idxFile = _hermesPathfinder.RootDirectory.JoinToFile(PackIndexFile);
            if (!idxFile.Exists) return false;

            long pos = index * 8;
            if (pos + 8 > idxFile.Length) return false;

            byte[] buffer = new byte[8];
            using var fs = idxFile.OpenRead();
            fs.Seek(pos, SeekOrigin.Begin);
            if (fs.Read(buffer, 0, 8) != 8) return false;

            offset = BitConverter.ToUInt32(buffer, 0);
            length = BitConverter.ToUInt32(buffer, 4);
            return true;
        }

        /// <summary>
        /// Writes a pointer entry (offset and length) to the pack index file at the specified index.
        /// </summary>
        /// <param name="index">The entry index in the pointer table.</param>
        /// <param name="offset">The offset in pack.dat where the data begins.</param>
        /// <param name="length">The length of the data in bytes.</param>
        private static void WritePackIndex(uint index, uint offset, uint length)
        {
            var idxFile = _hermesPathfinder.RootDirectory.JoinToFile(PackIndexFile);
            if (idxFile.Directory != null) idxFile.Directory.Create();

            long pos = index * 8;
            long needed = pos + 8;

            using var fs = new FileStream(idxFile.FullName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            if (fs.Length < needed)
                fs.SetLength(needed);

            byte[] buffer = new byte[8];
            BitConverter.GetBytes(offset).CopyTo(buffer, 0);
            BitConverter.GetBytes(length).CopyTo(buffer, 4);

            fs.Seek(pos, SeekOrigin.Begin);
            fs.Write(buffer, 0, 8);
        }

        /// <summary>
        /// Appends data to the end of the pack data file and returns the offset and length of the appended data.
        /// </summary>
        /// <param name="data">The byte array to append to the pack data file.</param>
        /// <returns>A tuple containing the offset (position in file) and length of the appended data.</returns>
        private static (uint offset, uint length) AppendToPack(byte[] data)
        {
            var dataFile = _hermesPathfinder.RootDirectory.JoinToFile(PackDataFile);
            if (dataFile.Directory != null) dataFile.Directory.Create();

            using var fs = new FileStream(dataFile.FullName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            fs.Seek(0, SeekOrigin.End);
            uint offset = (uint)fs.Position;
            uint length = (uint)data.Length;
            fs.Write(data, 0, data.Length);

            return (offset, length);
        }

        /// <summary>
        /// Appends a garbage collection record (offset and length) to the GC file.
        /// These records mark regions in pack.dat that are no longer in use and can be reclaimed during GC.
        /// </summary>
        /// <param name="offset">The offset of the stale data in pack.dat.</param>
        /// <param name="length">The length of the stale data in bytes.</param>
        private static void AppendToGc(uint offset, uint length)
        {
            var gcFile = _hermesPathfinder.RootDirectory.JoinToFile(PackGcFile);
            gcFile.Directory.Create();

            byte[] buffer = new byte[8];
            BitConverter.GetBytes(offset).CopyTo(buffer, 0);
            BitConverter.GetBytes(length).CopyTo(buffer, 4);

            using var fs = new FileStream(gcFile.FullName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            fs.Seek(0, SeekOrigin.End);
            fs.Write(buffer, 0, 8);
        }

        /// <summary>
        /// Store all modifications to disk
        /// </summary>
        private static void StoreAll()
        {
            // Handle booleans
            if (ModifiedBooleans.Count > 0)
            {
                uint[] indices = ModifiedBooleans.ToArray();
                bool[] values = new bool[indices.Length];
                for (int i = 0; i < indices.Length; i++)
                    values[i] = CacheBooleans[indices[i]];

                HelperWriteValueToFile(indices, values);
                ModifiedBooleans.Clear();
            }

            // Handle floats
            if (ModifiedFloat.Count > 0)
            {
                uint[] indices = ModifiedFloat.ToArray();
                float[] values = new float[indices.Length];
                for (int i = 0; i < indices.Length; i++)
                    values[i] = CacheFloats[indices[i]];

                HelperWriteValueToFile(indices, values);
                ModifiedFloat.Clear();
            }

            // Handle variable-length byte arrays
            if (ModifiedByteMap.Count > 0)
            {
                uint[] indices = ModifiedByteMap.ToArray();
                foreach (uint index in indices)
                {
                    // Read old pointer -> mark old space for GC
                    if (TryReadPackIndex(index, out uint oldOffset, out uint oldLen) && oldLen > 0)
                        AppendToGc(oldOffset, oldLen);

                    if (CacheByteMap.TryGetValue(index, out byte[] data) && data != null)
                    {
                        // Append new data to pack.dat
                        var (newOffset, newLen) = AppendToPack(data);

                        // Update pack.idx pointer
                        WritePackIndex(index, newOffset, newLen);
                    }
                    else
                    {
                        // Value is null (deleted): write zero offset/length to mark as empty slot
                        WritePackIndex(index, 0, 0);
                        CacheByteMap.Remove(index);
                    }
                }

                ModifiedByteMap.Clear();
            }
        }

        /// <summary>
        /// Writes data blocks to files
        /// </summary>
        /// <param name="globalIndices">List of global indices to write</param>
        /// <param name="values">List of values to write</param>
        /// <typeparam name="T">Type to write</typeparam>
        /// <returns>true if successful; otherwise, false</returns>
        private static bool HelperWriteValueToFile<T>(uint[] globalIndices, T[] values)
        {
            if (globalIndices == null || values == null || globalIndices.Length != values.Length || globalIndices.Length == 0)
                return false;

            int typeSize;
            Func<uint, FileInfo> getFileByIndex;
            if (typeof(T) == typeof(float))
            {
                typeSize = 4;
                getFileByIndex = _hermesPathfinder.GetFloatPathFromIndex;
            }
            else if (typeof(T) == typeof(bool))
            {
                typeSize = 1;
                getFileByIndex = _hermesPathfinder.GetBoolPathFromIndex;
            }
            else
                return false;

            var groups = new Dictionary<string, (List<uint> localIndices, List<T> values)>();

            for (int i = 0; i < globalIndices.Length; i++)
            {
                uint globalIdx = globalIndices[i];
                FileInfo file = getFileByIndex(globalIdx);
                string fileKey = file.FullName;

                uint localIdx = _hermesPathfinder.IndexToLocalIndex(globalIdx);

                if (!groups.TryGetValue(fileKey, out var group))
                {
                    group = (new List<uint>(), new List<T>());
                    groups[fileKey] = group;
                }
                group.localIndices.Add(localIdx);
                group.values.Add(values[i]);
            }

            bool allSuccess = true;
            foreach (var kvp in groups)
            {
                FileInfo file = new FileInfo(kvp.Key);
                var localIndices = kvp.Value.localIndices.ToArray();
                var vals = kvp.Value.values.ToArray();
                if (!WriteValuesToFile(localIndices, vals, file, typeSize))
                    allSuccess = false;
            }
            return allSuccess;
        }

        /// <summary>
        /// Writes data blocks that belong to the same file
        /// </summary>
        private static bool WriteValuesToFile<T>(uint[] localIndices, T[] values, FileInfo file, int typeSize)
        {
            try
            {
                const int maxLocalIndex = 65535;
                long totalSize = (maxLocalIndex + 1) * typeSize;

                file.Directory?.Create();

                using var fs = new FileStream(file.FullName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
                if (fs.Length != totalSize)
                    fs.SetLength(totalSize);

                for (int i = 0; i < localIndices.Length; i++)
                {
                    int idx = Convert.ToInt32(localIndices[i]);
                    if (idx < 0 || idx > maxLocalIndex) continue;

                    long offset = idx * typeSize;
                    fs.Seek(offset, SeekOrigin.Begin);

                    // Fascinating!
                    var bytes =
                        typeof(T) == typeof(float) ?
                            BitConverter.GetBytes(
                                (float)(object) values[i]) :
                            new[]
                            {
                                (byte)(
                                    (bool)(object) values[i] ?
                                    1 : 0
                                    )
                            };

                    fs.Write(bytes, 0, bytes.Length);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Attempts to read a slice of data from a file
        /// </summary>
        /// <param name="file">The file</param>
        /// <param name="offset">Offset value</param>
        /// <param name="length">Length to read</param>
        /// <param name="buffer">Output buffer</param>
        /// <returns></returns>
        private static bool TryReadSlice(FileInfo file, uint offset, long length, out byte[] buffer)
        {
            buffer = null;

            if (file == null || !file.Exists)
                return false;

            try
            {
                long fileLength = file.Length;
                if (offset + length > fileLength)
                    return false;

                buffer = new byte[length];
                using (FileStream fs = file.OpenRead())
                {
                    fs.Seek(offset, SeekOrigin.Begin);
                    int bytesRead = 0;
                    while (bytesRead < length)
                    {
                        int read = fs.Read(buffer, bytesRead, (int)(length - bytesRead));
                        if (read == 0)
                        {
                            buffer = null;
                            return false;
                        }
                        bytesRead += read;
                    }
                }
                return true;
            }
            catch (Exception)
            {
                buffer = null;
                return false;
            }
        }

        public class BoolIndexer
        {
            public bool this[uint index]
            {
                get => ReadBool(index, out var value) && value;
                set => WriteBool(index, value);
            }
        }

        public class FloatIndexer
        {
            public float this[uint index]
            {
                get => ReadFloat(index, out var value) ? value : 0f;
                set => WriteFloat(index, value);
            }
        }

        public class ByteIndexer
        {
            public byte[] this[uint index]
            {
                get => ReadByteArray(index, out var value) ? value : null;
                set => WriteByteArray(index, value);
            }
        }

        public class StringIndexer
        {
            public string this[uint index]
            {
                get
                {
                    var data = Hermes.Bytes[index];
                    return data != null ? System.Text.Encoding.UTF8.GetString(data) : null;
                }
                set => Hermes.Bytes[index] = value != null ? System.Text.Encoding.UTF8.GetBytes(value) : null;
            }
        }
    }

    /// <summary>
    /// HermesPathfinder
    /// Type used for locating data positions
    /// </summary>
    public class HermesPathfinder
    {
        private readonly DirectoryInfo _rootDirectory;

        /// <summary>
        /// Get root directory
        /// </summary>
        public DirectoryInfo RootDirectory => _rootDirectory;

        public HermesPathfinder(DirectoryInfo rootDirectory)
            => _rootDirectory = rootDirectory;

        /// <summary>
        /// Gets the file path of the float data file for the specified data index
        /// </summary>
        /// <param name="index">Data index (0-based)</param>
        /// <returns>File information containing the float data file for the given index</returns>
        public FileInfo GetFloatPathFromIndex(uint index)
            => CombinePathByChunkId(IndexToChunkId(index), "f", ".dat");

        /// <summary>
        /// Gets the file path of the float data file for the specified chunk ID
        /// </summary>
        /// <param name="chunkId"> Chunk Id (each chunk contains 65536 indices)</param>
        /// <returns>File information containing the float data file for the given chunk</returns>
        public FileInfo GetFloatPathFromChunkId(uint chunkId)
            => CombinePathByChunkId(chunkId, "f", ".dat");

        /// <summary>
        /// Gets the file path of the boolean data file for the specified data index
        /// </summary>
        /// <param name="index">Data index (0-based)</param>
        /// <returns>File information containing the boolean data file for the given index</returns>
        public FileInfo GetBoolPathFromIndex(uint index)
            => CombinePathByChunkId(IndexToChunkId(index), "b", ".dat");

        /// <summary>
        /// Gets the file path of the boolean data file for the specified chunk ID
        /// </summary>
        /// <param name="chunkId">Chunk Id (each chunk contains 65536 indices)</param>
        /// <returns>File information containing the boolean data file for the given chunk</returns>
        public FileInfo GetBoolPathFromChunkId(uint chunkId)
            => CombinePathByChunkId(chunkId, "b", ".dat");

        /// <summary>
        /// Convert chunkId to index
        /// </summary>
        /// <param name="chunkId"> File Chunk Id </param>
        /// <returns> Index </returns>
        public uint ChunkIdToIndex(uint chunkId)
            => chunkId * 65536;

        /// <summary>
        /// Convert index to chunkId
        /// </summary>
        /// <param name="index"> Index </param>
        /// <returns> File Chunk Id </returns>
        public uint IndexToChunkId(uint index)
            => index / 65536;

        /// <summary>
        /// Convert global index to local index within a chunk
        /// </summary>
        /// <param name="index"> Global index </param>
        /// <returns> Local index within the chunk (0–65535) </returns>
        public uint IndexToLocalIndex(uint index)
        {
            return index % 65536;
        }

        /// <summary>
        /// Convert chunk id and local index to global index
        /// </summary>
        /// <param name="chunkId"> File Chunk Id </param>
        /// <param name="localIndex"> Local index within the chunk </param>
        /// <returns> Global index </returns>
        public uint LocalIndexToIndex(uint chunkId, uint localIndex)
        {
            return chunkId * 65536 + localIndex;
        }

        private FileInfo CombinePathByChunkId(uint chunkId, string prefix, string suffix)
            => _rootDirectory.JoinToFile($"{prefix}{chunkId}{suffix}");
    }

    public static class DirectoryInfoExtensions
    {
        public static DirectoryInfo Join(this DirectoryInfo dir, params string[] paths)
        {
            string full = Path.Combine(dir.FullName, Path.Combine(paths));
            return new DirectoryInfo(full);
        }

        public static FileInfo JoinToFile(this DirectoryInfo dir, params string[] paths)
        {
            string full = Path.Combine(dir.FullName, Path.Combine(paths));
            return new FileInfo(full);
        }
    }
    #endregion

    #region UNITY
#if UNITY_2020_1_OR_NEWER

    public class HermesOnDisk : MonoBehaviour
    {
        private enum SaveLocationType
        {
            PersistentDataPath,
            TemporaryCachePath
        }

        /// <summary>
        /// Returns true if there are uncommitted changes in memory.
        /// </summary>
        public static bool IsDirty => Hermes.IsDirty;

        /// <summary>
        /// Boolean indexer
        /// </summary>
        public static Hermes.BoolIndexer Boolean => Hermes.Boolean;

        /// <summary>
        /// Float indexer
        /// </summary>
        public static Hermes.FloatIndexer Float => Hermes.Float;

        /// <summary>
        /// String indexer
        /// </summary>
        public static Hermes.StringIndexer String => Hermes.String;

        /// <summary>
        /// Variable-length byte array indexer
        /// </summary>
        public static Hermes.ByteIndexer Bytes => Hermes.Bytes;

        [Header("Save Location")]
        [SerializeField] private SaveLocationType saveLocation = SaveLocationType.PersistentDataPath;
        [SerializeField] private string relativePath = "Saves";

        private void Awake()
        {
            string basePath = saveLocation switch
            {
                SaveLocationType.PersistentDataPath => Application.persistentDataPath,
                SaveLocationType.TemporaryCachePath => Application.temporaryCachePath,
                _ => Application.persistentDataPath
            };

            string root = Path.Combine(basePath, relativePath);
            Hermes.Root = new DirectoryInfo(root);
        }

        private void OnDestroy()
        {
            Hermes.Store();
        }

        [ContextMenu("GC And Store")]
        public void DoGCAndSave()
        {
            Hermes.GC();
            Hermes.Store();
        }

        [ContextMenu("GC")]
        public void DoGC()
        {
            Hermes.GC();
        }

        [ContextMenu("Store")]
        public void DoStore()
        {
            Hermes.Store();
        }

        public void Save() => Hermes.Store();
        public void GC() => Hermes.GC();
    }

#endif

    #endregion
}
